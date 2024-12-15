using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using UnityEngine;
using UnityEngine.VFX;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace DefaultNamespace {
    
    public class AsyncGasLock {
        private Queue<UniTaskCompletionSource<short>> queue = new();
        private short holder = -1;
        public bool IsLocked => holder != -1;
        
        
        private short idGenerator = 0;
        private short GetNextLockId() {
            if (idGenerator == short.MaxValue) idGenerator = 0;
            return idGenerator++;
        }
        
        public async UniTask<short> Lock() {
            if (holder == -1) {
                var id = GetNextLockId();
                holder = id;
                return id;
            }
            var source = new UniTaskCompletionSource<short>();
            queue.Enqueue(source);
            var awaitedId = await source.Task;
            return awaitedId;
        }
        
        public void Unlock(short id) {
            if (holder != id) throw new InvalidOperationException("Cannot unlock lock with different id");
            if (queue.Count == 0) {
                holder = -1;
                return;
            }

            var next = GetNextLockId();
            var source = queue.Dequeue();
            holder = next;
            source.TrySetResult(next);
        }
    }

    public class RateLimiter {
        private readonly float _rate;
        private float _nextTime = 0;
        
        public RateLimiter(float rate) {
            _rate = rate;
        }
        
        public RateLimiter(int rate) {
            _rate = 1f / rate;
        }
        
        public bool TryConsume() {
            if (Time.time < _nextTime) return false;
            _nextTime = Time.time + _rate;
            return true;
        }
    }
    
    public class PointRenderContainer : MonoBehaviour {
        public const int TEXTURE_2D_MAX_HEIGHT = 16384;
        public const int TEXTURE_2D_COLUMNS = 2;
        public const float LIFE_TIME = 10f;

        private const string POSITIONS_TEXTURE_NAME = "Positions";
        private const string CAPACITY_PARAM_NAME = "Capacity";
        private const string REFERENCE_POS_PARAM_NAME = "ReferencePosition";
        private const string LIFETIME_PARAM_NAME = "Lifetime";
        private const string BOUNDS_CENTER_PARAM_NAME = "BoundsCenter";
        private const string BOUNDS_SIZE_PARAM_NAME = "BoundsSize";

        public UnsafeList<PointRenderEntry> entries;
        public BurstKdTree tree;
        public List<PointRenderVfxEntry> vfxEntries = new List<PointRenderVfxEntry>();
        public GameObject vfxPrefab;

        public Transform referenceTransform;

        public bool dirty = false;
        public bool isCleaningUp = false;

        public NativeList<PointInsertData> scheduledInsertData;
        public AsyncGasLock asyncGasLock = new AsyncGasLock();

        private RateLimiter _rebuildRateLimiter = new RateLimiter(30);
        private RateLimiter _brushRateLimiter = new RateLimiter(60);

        private void Awake() {
            
            tree = new BurstKdTree(1024, Allocator.Persistent);
            entries = new UnsafeList<PointRenderEntry>(1, Allocator.Persistent);
            scheduledInsertData = new NativeList<PointInsertData>(Allocator.Persistent);
            CleanupTask().Forget();
        }

        private void OnDestroy() {
            tree.Dispose();
            entries.Dispose();
            scheduledInsertData.Dispose();
        }

        private void Update() {
            if (scheduledInsertData.Length > 0 && _brushRateLimiter.TryConsume()) {
                InsertionTask().Forget();
            }
            
            if (dirty && _rebuildRateLimiter.TryConsume()) {
                CopyTask().Forget();
            }
        }

        private void FixedUpdate() {
            if (referenceTransform) {
                var transformPosition = referenceTransform.position;
                foreach (var entry in vfxEntries) {
                    entry.effect.SetVector3(REFERENCE_POS_PARAM_NAME, transformPosition);
                }
            }
        }

        public async UniTaskVoid InsertionTask() {
            var lockRef = await asyncGasLock.Lock();
            var temp = new NativeList<PointInsertData>(scheduledInsertData.Length, Allocator.Persistent);
            temp.CopyFrom(scheduledInsertData);
            scheduledInsertData.Clear();
            try {
                if (temp.Length == 0) return;
                var job = new TreeInsertJob {
                    data = temp,
                    tree = tree
                };
                var handle = job.Schedule();
                await UniTask.WaitUntil(() => handle.IsCompleted);
                handle.Complete();
                dirty = true;
            } finally {
                temp.Dispose();
                asyncGasLock.Unlock(lockRef);
            }
        }

        public async UniTaskVoid CleanupTask() {
            while (this) {
                await UniTask.Delay(TimeSpan.FromSeconds(5));
                var lockRef = await asyncGasLock.Lock();
                try {
                    var stopwatch = Stopwatch.StartNew();
                    var lengthBefore = tree.points.Length;
                    var job = new TreeCleanupJob {
                        tree = tree,
                        currentTime = Time.time,
                        maxAge = LIFE_TIME
                    };
                    var handle = job.Schedule();
                    await UniTask.WaitUntil(() => handle.IsCompleted);
                    handle.Complete();
                    var lengthAfter = tree.points.Length;
                    stopwatch.Stop();
                    Debug.Log(
                        $"Cleanup took {stopwatch.ElapsedMilliseconds}ms, removed {lengthBefore - lengthAfter} points");
                    if (lengthAfter != lengthBefore) dirty = true;
                } catch (Exception e) {
                    Debug.LogException(e);
                } finally {
                    asyncGasLock.Unlock(lockRef);
                }
            }
        }

        public async UniTaskVoid CopyTask() {
            var lockRef = await asyncGasLock.Lock();
            try {
                var stopwatch = Stopwatch.StartNew();
                await MirrorTreeToEntries();
                ApplyEntriesToGameObjects();
                dirty = false;
                stopwatch.Stop();
                Debug.Log($"Copy took {stopwatch.ElapsedMilliseconds}ms");
            } finally {
                asyncGasLock.Unlock(lockRef);
            }
        }
        
        public void ScheduleInsertData(PointInsertData data) {
            scheduledInsertData.Add(data);
        }
        

        // ReSharper disable Unity.PerformanceAnalysis
        public void ApplyEntriesToGameObjects() {
            if (entries.Length > vfxEntries.Count) {
                var diff = entries.Length - vfxEntries.Count;
                for (var i = 0; i < diff; i++) {
                    var texture = new Texture2D(TEXTURE_2D_MAX_HEIGHT, TEXTURE_2D_COLUMNS, TextureFormat.RGBAFloat,
                        false);
                    var effect = Instantiate(vfxPrefab, transform).GetComponent<VisualEffect>();
                    effect.SetVector3(
                        REFERENCE_POS_PARAM_NAME,
                        referenceTransform ? referenceTransform.position : Vector3.zero
                    );

                    var entry = new PointRenderVfxEntry(texture, effect);
                    vfxEntries.Add(entry);
                }
            } else if (entries.Length < vfxEntries.Count) {
                var diff = vfxEntries.Count - entries.Length;
                for (var i = 0; i < diff; i++) {
                    vfxEntries[^1].Dispose();
                    vfxEntries.RemoveAt(vfxEntries.Count - 1);
                }
            }

            for (var i = 0; i < entries.Length; i++) {
                var entry = entries[i];
                var vfxEntry = vfxEntries[i];
                var texture = vfxEntry.texture;
                texture.LoadRawTextureData(entry.data);
                texture.Apply();
                var effect = vfxEntry.effect;
                effect.SetTexture(POSITIONS_TEXTURE_NAME, texture);
                effect.SetUInt(CAPACITY_PARAM_NAME, entry.count);
                effect.SetFloat(LIFETIME_PARAM_NAME, LIFE_TIME);
                effect.SetVector3(BOUNDS_CENTER_PARAM_NAME, entry.bounds.Center);
                effect.SetVector3(BOUNDS_SIZE_PARAM_NAME, entry.bounds.Extents);
                effect.Reinit();
            }
        }

        public async UniTask MirrorTreeToEntries() {
            
            var entryCount = math.ceil(tree.points.Length / (float)TEXTURE_2D_MAX_HEIGHT);
            if (entryCount > entries.Length) {
                for (var i = entries.Length; i < entryCount; i++) {
                    CreateNewEntry();
                }
            } else if (entryCount < entries.Length) {
                for (var i = entries.Length - 1; i >= entryCount; i--) {
                    entries.RemoveAtSwapBack(i);
                }
            }

            var job = new EntryMirrorJob() {
                entries = entries,
                tree = tree
            };
            await job.Schedule();
            return;
            
            // Set count to 0 for all entries
            for (var i = 0; i < entries.Length; i++) {
                var entry = entries[i];
                entry.count = 0;
                entries[i] = entry;
            }

            if (entries.Length == 0) {
                CreateNewEntry();
            }

            var currentIndex = 0;
            var currentEntry = entries[currentIndex];
            var cursor = 0;
            var bounds = new MinMaxAABB();
            foreach (var point in tree.points) {
                if (cursor == TEXTURE_2D_MAX_HEIGHT) {
                    entries[currentIndex] = new PointRenderEntry {
                        data = currentEntry.data,
                        count = TEXTURE_2D_MAX_HEIGHT,
                        bounds = bounds
                    };
                    currentIndex++;
                    if (currentIndex == entries.Length) {
                        CreateNewEntry();
                    }

                    currentEntry = entries[currentIndex];
                    cursor = 0;
                }

                if (cursor == 0) {
                    bounds = new MinMaxAABB(point.position, point.position);
                } else {
                    bounds.Encapsulate(point.position);
                }
                currentEntry.data[cursor] = new float4(point.position, 1f);
                currentEntry.data[cursor + TEXTURE_2D_MAX_HEIGHT] = new float4(point.color, point.timestamp);
                cursor++;
            }

            if (cursor > 0) {
                entries[currentIndex] = new PointRenderEntry {
                    data = currentEntry.data,
                    count = (uint)cursor,
                    bounds = bounds
                };
            }

            // Remove empty entries from the list
            for (var i = 0; i < entries.Length; i++) {
                if (entries[i].count != 0) continue;
                entries.RemoveAtSwapBack(i);
                i--;
            }
        }

        private void CreateNewEntry() {
            entries.Add(new PointRenderEntry(TEXTURE_2D_MAX_HEIGHT * TEXTURE_2D_COLUMNS, Allocator.Persistent));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointRenderEntry {
        public NativeArray<float4> data;
        public uint count;
        public MinMaxAABB bounds;
        
        public PointRenderEntry(int length, Allocator allocator) {
            data = new NativeArray<float4>(length, allocator);
            count = 0;
            bounds = new MinMaxAABB();

        }

        public void Dispose() {
            data.Dispose();
        }
    }

    public class PointRenderVfxEntry {
        public Texture2D texture;
        public VisualEffect effect;

        public PointRenderVfxEntry(Texture2D texture, VisualEffect effect) {
            this.texture = texture;
            this.effect = effect;
        }

        public void Dispose() {
            Object.Destroy(texture);
            Object.Destroy(effect.gameObject);
        }
    }

    [BurstCompile]
    public struct EntryMirrorJob : IJob {
        
        public UnsafeList<PointRenderEntry> entries;
        [ReadOnly] public BurstKdTree tree;
        
        public void Execute() {
            if (tree.points.Length == 0) return;
            
            // Set count to 0 for all entries
            for (var i = 0; i < entries.Length; i++) {
                var entry = entries[i];
                entry.count = 0;
                entries[i] = entry;
            }

            var currentIndex = 0;
            var currentEntry = entries[currentIndex];
            var cursor = 0;
            var bounds = new MinMaxAABB();
            foreach (var point in tree.points) {
                if (cursor == PointRenderContainer.TEXTURE_2D_MAX_HEIGHT) {
                    entries[currentIndex] = new PointRenderEntry {
                        data = currentEntry.data,
                        count = PointRenderContainer.TEXTURE_2D_MAX_HEIGHT,
                        bounds = bounds
                    };
                    currentIndex++;
                    currentEntry = entries[currentIndex];
                    cursor = 0;
                }

                if (cursor == 0) {
                    bounds = new MinMaxAABB(point.position, point.position);
                } else {
                    bounds.Encapsulate(point.position);
                }
                currentEntry.data[cursor] = new float4(point.position, 1f);
                currentEntry.data[cursor + PointRenderContainer.TEXTURE_2D_MAX_HEIGHT] = new float4(point.color, point.timestamp);
                cursor++;
            }

            if (cursor > 0) {
                entries[currentIndex] = new PointRenderEntry {
                    data = currentEntry.data,
                    count = (uint)cursor,
                    bounds = bounds
                };
            }
        }
    }
}