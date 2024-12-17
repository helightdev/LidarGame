using System;
using System.Collections.Generic;
using System.Linq;
using Common;
using Cysharp.Threading.Tasks;
using Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.VFX;

namespace Storage {
    public class GridHashStorage : PointStorageBase {
        public const int TEXTURE_2D_MAX_HEIGHT = 16384;
        public const int TEXTURE_2D_COLUMNS = 2;
        public const float LIFE_TIME = 10f;

        private const string _positionsTextureName = "Positions";
        private const string _capacityParamName = "Capacity";
        private const string _referencePosParamName = "ReferencePosition";
        private const string _lifetimeParamName = "Lifetime";
        private const string _boundsCenterParamName = "BoundsCenter";
        private const string _boundsSizeParamName = "BoundsSize";

        public GameObject vfxPrefab;

        private UnsafeList<PointRenderEntry> _renderEntries;
        private readonly List<PointRenderVfxEntry> _vfxEntries = new();
        private NativeList<PointInsertData> _insertData;
        private GridHashList<PointData> _hashList;
        
        private float3 _playerPosition;
        private readonly AsyncReadWriteLock _lock = new();
        private readonly RateLimiter _insertionRateLimiter = new(60);
        private readonly RateLimiter _rebuildRateLimiter = new(30);


        [HideInInspector, DoNotSerialize] public bool dirty;

        public override void Init(PointRenderContainer container) {
            _renderEntries = new UnsafeList<PointRenderEntry>(1, Allocator.Persistent);
            _insertData = new NativeList<PointInsertData>(Allocator.Persistent);
            _hashList = new GridHashList<PointData>(0.5f, Allocator.Persistent);
        }

        public override void Insert(PointInsertData data) {
            _insertData.Add(data);
        }

        public override void UpdatePlayerPosition(float3 position) {
            _playerPosition = position;
        }

        public override void Dispose() {
            _insertData.Dispose();
            _hashList.Dispose();
            _renderEntries.Dispose();            
            foreach (var entry in _vfxEntries) {
                entry.Dispose();
            }
            _vfxEntries.Clear();
        }

        private void FixedUpdate() {
            if (dirty && _rebuildRateLimiter.Limit()) {
                CopyAndRebuild().Forget();
            }

            if (_insertData.Length > 0 && _insertionRateLimiter.Limit()) {
                RunInsertionJob().Forget();
            }


            foreach (var entry in _vfxEntries) {
                entry.effect.SetVector3(_referencePosParamName, _playerPosition);
            }
        }

        private async UniTaskVoid RunInsertionJob() {
            var input = _insertData.ToArray(Allocator.Persistent);
            _insertData.Clear();
            var prefiltered = new NativeArray<PrefilterGridData>(input.Length, Allocator.Persistent);
            await _lock.EnterWriteLockAsync();
            try {
                await new GridPrefilterJob {
                    insertions = input,
                    grid = _hashList,
                    data = prefiltered
                }.Schedule(input.Length, 8);

                await new GridPrefilterApplyJob {
                    data = prefiltered,
                    grid = _hashList
                }.Schedule();
            } finally {
                input.Dispose();
                prefiltered.Dispose();
                _lock.ExitWriteLock();
            }
            dirty = true;
        }

        private async UniTaskVoid CopyAndRebuild() {
            await _lock.EnterWriteLockAsync();
            try {
                int count;
                using (var countBuffer = new NativeArray<int>(1, Allocator.TempJob)) {
                    await new CountAndCleanupJob {
                        data = _hashList,
                        time = Time.time,
                        counterBuffer = countBuffer
                    }.Schedule();
                    count = countBuffer[0];
                }
                
                var requiredEntries = (int)math.ceil(count / (float)TEXTURE_2D_MAX_HEIGHT);
                EnsureVfxEntries(requiredEntries);
                DeleteUnusedVfxEntries(requiredEntries);

                await new CopyDataJob {
                    entries = _renderEntries,
                    hashList = _hashList
                }.Schedule();

                for (var i = 0; i < requiredEntries; i++) {
                    var renderEntry = _renderEntries[i];
                    var entry = _vfxEntries[i];
                    ApplyBuffer(renderEntry.count, entry, renderEntry.data, renderEntry.bounds);
                }
                
                dirty = false;
            } finally {
                _lock.ExitWriteLock();
            }
        }


        private static void ApplyBuffer(uint currentCount, PointRenderVfxEntry currentEntry, NativeArray<float4> data,
            MinMaxAABB bounds) {
            currentEntry.texture.LoadRawTextureData(data);
            currentEntry.texture.Apply();
            currentEntry.effect.SetTexture(_positionsTextureName, currentEntry.texture);
            currentEntry.effect.SetUInt(_capacityParamName, currentCount);
            currentEntry.effect.SetFloat(_lifetimeParamName, LIFE_TIME);
            currentEntry.effect.SetVector3(_boundsCenterParamName, bounds.Center);
            currentEntry.effect.SetVector3(_boundsSizeParamName, bounds.Extents);
            currentEntry.effect.Reinit();
        }

        private void EnsureVfxEntries(int count) {
            while (_vfxEntries.Count < count) {
                CreateNewVfxEntry();
            }
        }

        private void DeleteUnusedVfxEntries(int count) {
            for (var i = _vfxEntries.Count - 1; i > count; i--) {
                var entry = _vfxEntries[i];
                entry.Dispose();
                _vfxEntries.RemoveAt(i);
                var renderEntry = _renderEntries[i];
                renderEntry.data.Dispose();
                _renderEntries.RemoveAt(i);
            }
        }

        private void CreateNewVfxEntry() {
            var texture = new Texture2D(TEXTURE_2D_MAX_HEIGHT, TEXTURE_2D_COLUMNS, TextureFormat.RGBAFloat,
                false);
            var effect = Instantiate(vfxPrefab, transform).GetComponent<VisualEffect>();
            effect.SetVector3(_referencePosParamName, _playerPosition);

            var entry = new PointRenderVfxEntry(texture, effect);
            _vfxEntries.Add(entry);

            _renderEntries.Add(new PointRenderEntry {
                data = new NativeArray<float4>(TEXTURE_2D_MAX_HEIGHT * TEXTURE_2D_COLUMNS, Allocator.Persistent),
                count = 0,
                bounds = new MinMaxAABB()
            });
        }


        [BurstCompile]
        private struct CopyDataJob : IJob {
            public UnsafeList<PointRenderEntry> entries;
            public GridHashList<PointData> hashList;

            public void Execute() {
                var currentEntry = entries[0];
                var currentIndex = 0;
                var currentCount = 0;
                var bounds = new MinMaxAABB();

                using var enumerator = hashList.GetEnumerator();
                while (enumerator.MoveNext()) {
                    var value = enumerator.Current.Value;
                    currentEntry.data[currentCount] =
                        new float4(value.position.x, value.position.y, value.position.z, 1f);
                    currentEntry.data[currentCount + TEXTURE_2D_MAX_HEIGHT] =
                        new float4(value.color.x, value.color.y, value.color.z, value.timestamp);
                    currentCount += 1;

                    if (currentCount == 0) {
                        bounds = new MinMaxAABB(value.position, value.position);
                    } else bounds.Encapsulate(value.position);

                    if (currentCount == TEXTURE_2D_MAX_HEIGHT) {
                        currentEntry.bounds = bounds;
                        currentEntry.count = (uint)currentCount;
                        entries[currentIndex] = currentEntry;
                        
                        currentCount = 0;
                        currentIndex++;
                        currentEntry = entries[currentIndex];
                    }
                }
                
                if (currentCount > 0) {
                    entries[currentIndex] = new PointRenderEntry {
                        data = currentEntry.data,
                        count = (uint)currentCount,
                        bounds = bounds
                    };
                }
            }
        }
    }

    [BurstCompile]
    public struct GridPrefilterJob : IJobParallelFor {
        [ReadOnly] public NativeArray<PointInsertData> insertions;
        [ReadOnly] public GridHashList<PointData> grid;
        [WriteOnly] public NativeArray<PrefilterGridData> data;

        public void Execute(int index) {
            var d = insertions[index];
            var position = d.position;
            for (var i = index + 1; i < insertions.Length; i++) {
                var target = insertions[i];
                if (math.distance(position, target.position) < d.density) {
                    data[index] = new PrefilterGridData { skip = true };
                    return;
                }
            }

            var optional = grid.ClosestValue(position, d.density);
            if (optional.hasValue) {
                var v = optional.value;
                v.timestamp = d.timestamp;
                data[index] = new PrefilterGridData() {
                    value = v,
                    update = true
                };
            } else {
                data[index] = new PrefilterGridData() {
                    value = new PointData() {
                        position = d.position,
                        timestamp = d.timestamp,
                        color = d.color
                    },
                    update = false
                };
            }
        }
    }

    [BurstCompile]
    public struct GridPrefilterApplyJob : IJob {
        [ReadOnly] public NativeArray<PrefilterGridData> data;
        public GridHashList<PointData> grid;

        public void Execute() {
            foreach (var gridData in data) {
                if (gridData.skip) continue;
                var v = gridData.value;
                if (gridData.update) {
                    grid.Remove(v);
                }

                grid.Insert(v);
            }
        }
    }

    [BurstCompile]
    public struct CountAndCleanupJob : IJob {
        public GridHashList<PointData> data;
        public NativeArray<int> counterBuffer;
        public float time;

        public void Execute() {
            var deletions = new NativeList<KeyValue<int3, PointData>>(Allocator.Temp);
            var count = 0;
            using var enumerator = data.GetEnumerator();
            while (enumerator.MoveNext()) {
                var current = enumerator.Current;
                if (time - current.Value.timestamp > GridHashStorage.LIFE_TIME) {
                    deletions.Add(current);
                } else {
                    count++;
                }
            }

            foreach (var deletion in deletions) {
                data.Remove(deletion.Value);
            }
            counterBuffer[0] = count;
        }
    }

    public struct PrefilterGridData {
        public PointData value;
        public bool update;
        public bool skip;
    }
}