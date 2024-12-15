using Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Jobs {
    [BurstCompile]
    public struct TreeFilterUpdateJob : IJobParallelFor {
        [ReadOnly] public NativeArray<PointInsertData> data;
        [WriteOnly] public NativeArray<PointInsertData> insertions;
        [ReadOnly] public KdTree tree;

        public void Execute(int index) {
            var d = data[index];
            var nearIndex = tree.FindNearest(d.position);
            if (nearIndex == -1) {
                insertions[index] = d;
                return;
            }

            var nearestData = tree.points[nearIndex];
            if (math.distance(d.position, nearestData.position) < d.density) {
                d.isUpdate = true;
                d.updateIndex = nearIndex;
                insertions[index] = d;
                return;
            }

            insertions[index] = d;
        }
    }
}