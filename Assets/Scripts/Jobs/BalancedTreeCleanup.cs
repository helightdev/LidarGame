using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Common;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Jobs {
    public static class BalancedTreeCleanup {
        public static async UniTask<KdTree> BalanceAndCleanup(NativeList<PointData> points, int samples,
            float maxLifetime) {
            var available = new NativeList<PointData>(points.Length, Allocator.TempJob);

            new CopyAndFilter {
                from = points,
                to = available,
                maxLifetime = maxLifetime,
                time = Time.time
            }.Run();

            var effectiveCount = math.min(available.Length, samples);
            var data = new NativeList<PointData>(effectiveCount, Allocator.TempJob);

            KdTree tree;
            try {
                var random = new Random((uint)Time.frameCount);
                for (var i = 0; i < effectiveCount; i++) {
                    var index = random.NextInt(available.Length);
                    data.Add(available[index]);
                    available.RemoveAtSwapBack(index);
                }

                tree = await ConstructBalancedTree(data);

                await new InsertJob {
                    data = available,
                    tree = tree
                }.Schedule();
            } finally {
                available.Dispose();
                data.Dispose();
            }

            return tree;
        }

        private static async UniTask<KdTree> ConstructBalancedTree(NativeList<PointData> points) {
            if (points.Length < 3) {
                var smallTree = new KdTree(points.Length, Allocator.Persistent);
                foreach (var point in points) smallTree.Insert(point);

                return smallTree;
            }

            var stopwatch = Stopwatch.StartNew();
            var xyz = new NativeList<PointData>(points.Length, Allocator.TempJob);
            var yzx = new NativeList<PointData>(points.Length, Allocator.TempJob);
            var zxy = new NativeList<PointData>(points.Length, Allocator.TempJob);
            foreach (var data in points) {
                xyz.Add(data);
                yzx.Add(data);
                zxy.Add(data);
            }

            var sort0 = xyz.SortJob(new XYZComparer()).Schedule();
            var sort1 = yzx.SortJob(new YZXComparer()).Schedule();
            var sort2 = zxy.SortJob(new ZXYComparer()).Schedule();

            await JobHandle.CombineDependencies(sort0, sort1, sort2);
            // Debug.Log($"Sorting took {stopwatch.ElapsedMilliseconds}ms");
            stopwatch.Restart();

            var regions = new NativeList<AssemblyStepArgs>(Allocator.TempJob);
            regions.Add(new AssemblyStepArgs {
                start = 0,
                end = xyz.Length,
                parent = -1,
                isLeft = false
            });
            var axis = 0;
            var newTree = new KdTree(points.Length, Allocator.Persistent);
            try {
                while (newTree.points.Length < xyz.Length) {
                    var regionCount = regions.Length;
                    var results = new NativeArray<AssemblyStepResult>(regionCount, Allocator.TempJob);
                    var regionBuffer = regions.ToArray(Allocator.TempJob);
                    try {
                        var job = new ParallelTreeAssembler {
                            xyz = xyz,
                            yzx = yzx,
                            zxy = zxy,
                            regions = regionBuffer,
                            results = results,
                            axis = axis
                        };
                        await job.Schedule(regionCount, 32);
                        regions.Clear();
                        foreach (var result in results)
                            switch (result.type) {
                                case 0:
                                    break;
                                case 1:
                                    newTree.Insert(result.point);
                                    break;
                                case 2:
                                    newTree.Insert(result.point);
                                    regions.Add(result.left);
                                    regions.Add(result.right);
                                    break;
                            }
                    } finally {
                        regionBuffer.Dispose();
                        results.Dispose();
                    }

                    axis = (axis + 1) % 3;
                }
            } finally {
                regions.Dispose();
                xyz.Dispose();
                yzx.Dispose();
                zxy.Dispose();
            }

            // Debug.Log($"Balancing took {stopwatch.ElapsedMilliseconds}ms");

            return newTree;
        }

        [BurstCompile]
        private struct CopyAndFilter : IJob {
            public NativeList<PointData> from;
            public NativeList<PointData> to;
            public float maxLifetime;
            public float time;

            public void Execute() {
                foreach (var pointData in from)
                    if (time - pointData.timestamp < maxLifetime) {
                        to.Add(pointData);
                    }
            }
        }

        [BurstCompile]
        private struct InsertJob : IJob {
            public NativeList<PointData> data;
            public KdTree tree;


            public void Execute() {
                foreach (var pointData in data) tree.Insert(pointData);
            }
        }

        public struct ParallelTreeAssembler : IJobParallelFor {
            [ReadOnly] public NativeList<PointData> xyz;
            [ReadOnly] public NativeList<PointData> yzx;
            [ReadOnly] public NativeList<PointData> zxy;
            [ReadOnly] public NativeArray<AssemblyStepArgs> regions;
            [WriteOnly] public NativeArray<AssemblyStepResult> results;
            public int axis;

            public void Execute(int i) {
                var r = regions[i];
                var start = r.start;
                var end = r.end;
                var length = end - start;
                PointData d;
                switch (length) {
                    case 0:
                        results[i] = new AssemblyStepResult { type = 0 };
                        return;
                    case 1:
                        d = axis switch {
                            0 => xyz[start],
                            1 => yzx[start],
                            2 => zxy[start],
                            _ => default
                        };

                        results[i] = new AssemblyStepResult {
                            type = 1,
                            point = d
                        };
                        return;
                }

                var mid = start + length / 2;
                d = axis switch {
                    0 => xyz[mid],
                    1 => yzx[mid],
                    2 => zxy[mid],
                    _ => default
                };


                results[i] = new AssemblyStepResult {
                    type = 2,
                    point = d,
                    left = new AssemblyStepArgs {
                        start = start,
                        end = mid,
                        parent = i,
                        isLeft = true
                    },
                    right = new AssemblyStepArgs {
                        start = mid + 1,
                        end = end,
                        parent = i,
                        isLeft = false
                    }
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AssemblyStepArgs {
            public int start;
            public int end;
            public int parent;
            public bool isLeft;
        }

        public struct AssemblyStepResult {
            public byte type;
            public PointData point;
            public AssemblyStepArgs left;
            public AssemblyStepArgs right;
        }

        private struct XYZComparer : IComparer<PointData> {
            public int Compare(PointData a, PointData b) {
                return a.position.x.CompareTo(b.position.x);
            }
        }

        private struct YZXComparer : IComparer<PointData> {
            public int Compare(PointData a, PointData b) {
                return a.position.y.CompareTo(b.position.y);
            }
        }

        private struct ZXYComparer : IComparer<PointData> {
            public int Compare(PointData a, PointData b) {
                return a.position.z.CompareTo(b.position.z);
            }
        }
    }
}