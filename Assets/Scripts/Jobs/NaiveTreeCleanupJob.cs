using Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Jobs {
    [BurstCompile]
    public struct NaiveTreeCleanupJob : IJob {
        public float currentTime;
        public float maxAge;
        public KdTree tree;

        public void Execute() {
            var buffer = new NativeList<PointData>(tree.points.Length, Allocator.Temp);

            foreach (var point in tree.points)
                if (currentTime - point.timestamp < maxAge)
                    buffer.Add(point);

            if (buffer.Length != tree.points.Length) {
                tree.points.Clear();
                tree.nodes.Clear();

                foreach (var data in buffer) tree.Insert(data);
            }

            buffer.Dispose();
        }
    }
}