using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Common;
using Cysharp.Threading.Tasks;
using Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.VFX;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public class PointRenderContainer : MonoBehaviour {
    public const int TEXTURE_2D_MAX_HEIGHT = 16384;
    public const int TEXTURE_2D_COLUMNS = 2;
    public const float LIFE_TIME = 60f;
    public const float CLEANUP_INTERVAL = 30f;

    private const string _positionsTextureName = "Positions";
    private const string _capacityParamName = "Capacity";
    private const string _referencePosParamName = "ReferencePosition";
    private const string _lifetimeParamName = "Lifetime";
    private const string _boundsCenterParamName = "BoundsCenter";
    private const string _boundsSizeParamName = "BoundsSize";
    public GameObject vfxPrefab;

    public Transform referenceTransform;

    [DoNotSerialize] public bool dirty;
    private readonly RateLimiter _cleanupRateLimiter = new(CLEANUP_INTERVAL);
    private readonly RateLimiter _insertionRateLimiter = new(60);
    private readonly RateLimiter _rebuildRateLimiter = new(30);
    private readonly AsyncReadWriteLock _treeLock = new();
    private readonly List<PointRenderVfxEntry> _vfxEntries = new();

    private UnsafeList<PointRenderEntry> _renderEntries;
    private NativeList<PointInsertData> _scheduledInsertData;

    public KdTree tree;

    private void Awake() {
        tree = new KdTree(1024, Allocator.Persistent);
        _renderEntries = new UnsafeList<PointRenderEntry>(1, Allocator.Persistent);
        _scheduledInsertData = new NativeList<PointInsertData>(Allocator.Persistent);
    }

    private void Update() {
        if (_scheduledInsertData.Length > 0 && _insertionRateLimiter.Limit()) InsertionTask().Forget();

        if (dirty && _rebuildRateLimiter.Limit()) CopyTask().Forget();
    }

    private void FixedUpdate() {
        if (_cleanupRateLimiter.Limit()) CleanupTask().Forget();

        if (referenceTransform) {
            var transformPosition = referenceTransform.position;
            foreach (var entry in _vfxEntries) entry.effect.SetVector3(_referencePosParamName, transformPosition);
        }
    }

    private void OnDestroy() {
        tree.Dispose();
        _renderEntries.Dispose();
        _scheduledInsertData.Dispose();
    }

    private async UniTaskVoid InsertionTask() {
        var insertions = new NativeArray<PointInsertData>(_scheduledInsertData.Length, Allocator.TempJob);
        var temp = new NativeArray<PointInsertData>(_scheduledInsertData.Length, Allocator.TempJob);
        temp.CopyFrom(_scheduledInsertData.AsArray());
        _scheduledInsertData.Clear();

        await _treeLock.EnterReadLockAsync();
        try {
            var filter = new TreeFilterUpdateJob {
                data = temp,
                insertions = insertions,
                tree = tree
            };
            await filter.Schedule(temp.Length, 16);
        } catch (Exception e) {
            // ReSharper disable once Unity.PerformanceCriticalCodeInvocation
            Debug.LogException(e);
        } finally {
            temp.Dispose();
            _treeLock.ExitReadLock();
        }

        await _treeLock.EnterWriteLockAsync();
        try {
            if (temp.Length == 0) return;
            var job = new TreeInsertJob {
                data = insertions,
                tree = tree
            };
            var stopwatch = Stopwatch.StartNew();
            job.Run();
            stopwatch.Stop();
            // Debug.Log($"Insertion took {stopwatch.ElapsedMilliseconds}ms");
            dirty = true;
        } finally {
            insertions.Dispose();
            _treeLock.ExitWriteLock();
        }
    }

    private async UniTaskVoid CleanupTask() {
        await UniTask.Delay(TimeSpan.FromSeconds(CLEANUP_INTERVAL));
        await _treeLock.EnterWriteLockAsync();
        try {
            var stopwatch = Stopwatch.StartNew();
            var lengthBefore = tree.points.Length;

            var newTree = await BalancedTreeCleanup.BalanceAndCleanup(tree.points, 1024, LIFE_TIME);
            tree.Dispose();
            tree = newTree;

            var lengthAfter = tree.points.Length;
            stopwatch.Stop();
            // Debug.Log($"Cleanup took {stopwatch.ElapsedMilliseconds}ms, removed {lengthBefore - lengthAfter} points");

            if (lengthAfter != lengthBefore) dirty = true;
        } catch (Exception e) {
            Debug.LogException(e);
        } finally {
            _treeLock.ExitWriteLock();
        }
    }

    private async UniTaskVoid CopyTask() {
        await _treeLock.EnterReadLockAsync();
        try {
            var stopwatch = Stopwatch.StartNew();
            await MirrorTreeToEntries();
            ApplyEntriesToGameObjects();
            dirty = false;
            stopwatch.Stop();
            // Debug.Log($"Copy took {stopwatch.ElapsedMilliseconds}ms");
        } finally {
            _treeLock.ExitReadLock();
        }
    }

    public void ScheduleInsertData(PointInsertData data) {
        _scheduledInsertData.Add(data);
    } // ReSharper disable Unity.PerformanceAnalysis
    private void ApplyEntriesToGameObjects() {
        if (_renderEntries.Length > _vfxEntries.Count) {
            var diff = _renderEntries.Length - _vfxEntries.Count;
            for (var i = 0; i < diff; i++) {
                var texture = new Texture2D(TEXTURE_2D_MAX_HEIGHT, TEXTURE_2D_COLUMNS, TextureFormat.RGBAFloat,
                    false);
                var effect = Instantiate(vfxPrefab, transform).GetComponent<VisualEffect>();
                effect.SetVector3(
                    _referencePosParamName,
                    referenceTransform ? referenceTransform.position : Vector3.zero
                );

                var entry = new PointRenderVfxEntry(texture, effect);
                _vfxEntries.Add(entry);
            }
        } else if (_renderEntries.Length < _vfxEntries.Count) {
            var diff = _vfxEntries.Count - _renderEntries.Length;
            for (var i = 0; i < diff; i++) {
                _vfxEntries[^1].Dispose();
                _vfxEntries.RemoveAt(_vfxEntries.Count - 1);
            }
        }

        for (var i = 0; i < _renderEntries.Length; i++) {
            var entry = _renderEntries[i];
            var vfxEntry = _vfxEntries[i];
            var texture = vfxEntry.texture;
            texture.LoadRawTextureData(entry.data);
            texture.Apply();
            var effect = vfxEntry.effect;
            effect.SetTexture(_positionsTextureName, texture);
            effect.SetUInt(_capacityParamName, entry.count);
            effect.SetFloat(_lifetimeParamName, LIFE_TIME);
            effect.SetVector3(_boundsCenterParamName, entry.bounds.Center);
            effect.SetVector3(_boundsSizeParamName, entry.bounds.Extents);
            effect.Reinit();
        }
    }

    private async UniTask MirrorTreeToEntries() {
        var entryCount = math.ceil(tree.points.Length / (float)TEXTURE_2D_MAX_HEIGHT);
        if (entryCount > _renderEntries.Length)
            for (var i = _renderEntries.Length; i < entryCount; i++)
                CreateRenderEntry();
        else if (entryCount < _renderEntries.Length)
            for (var i = _renderEntries.Length - 1; i >= entryCount; i--) {
                var entry = _renderEntries[i];
                entry.Dispose();
                _renderEntries.RemoveAtSwapBack(i);
            }

        var stopwatch = Stopwatch.StartNew();
        var job = new MirrorTreeToVfxJob {
            entries = _renderEntries,
            tree = tree
        };
        if (entryCount <= 2)
            // Maybe make some of those framerate dependent
            job.Run();
        else
            await job.Schedule();

        stopwatch.Stop();
        // Debug.Log($"Mirror took {stopwatch.ElapsedMilliseconds}ms");
    }

    private void CreateRenderEntry() {
        _renderEntries.Add(new PointRenderEntry(TEXTURE_2D_MAX_HEIGHT * TEXTURE_2D_COLUMNS, Allocator.Persistent));
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
    public readonly VisualEffect effect;
    public readonly Texture2D texture;

    public PointRenderVfxEntry(Texture2D texture, VisualEffect effect) {
        this.texture = texture;
        this.effect = effect;
    }

    public void Dispose() {
        Object.Destroy(texture);
        Object.Destroy(effect.gameObject);
    }
}