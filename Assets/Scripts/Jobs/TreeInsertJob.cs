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
            for (var i = 0; i < data.Length; i++) {
                var d = data[i];
                if (d.isUpdate) {
                    var point = tree.points[d.updateIndex];
                    point.timestamp = d.timestamp;
                    tree.points[d.updateIndex] = point;
                } else {
                    tree.Insert(new PointData {
                        position = d.position,
                        color = d.color,
                        timestamp = d.timestamp
                    });
                }
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