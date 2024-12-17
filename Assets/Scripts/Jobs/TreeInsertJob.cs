using System.Runtime.InteropServices;
using Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Jobs {
    [BurstCompile]
    public struct TreeInsertJob : IJob {
        public NativeArray<PointInsertData> data;
        public KdTree tree;

        public void Execute() {
            using var addedBuffer = new NativeList<float3>(Allocator.Temp);
            foreach (var d in data)
                if (d.isUpdate) {
                    var point = tree.points[d.updateIndex];
                    point.timestamp = d.timestamp;
                    tree.points[d.updateIndex] = point;
                } else {
                    var pos = d.position;
                    foreach (var p in addedBuffer) {
                        if (math.distance(pos, p) < d.density) {
                            goto skip;
                        }
                    }
                    tree.Insert(new PointData {
                        position = pos,
                        color = d.color,
                        timestamp = d.timestamp
                    });
                    addedBuffer.Add(pos);
                    skip:{}
                }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointInsertData {
        public float3 position;
        public float3 color;
        public float timestamp;
        public float density;

        public bool isUpdate;
        public int updateIndex;
    }
}