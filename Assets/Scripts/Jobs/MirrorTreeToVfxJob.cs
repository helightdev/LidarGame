using Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;

namespace Jobs {
    [BurstCompile]
    public struct MirrorTreeToVfxJob : IJob {
        [ReadOnly] public KdTree tree;
        public UnsafeList<PointRenderEntry> entries;

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

                if (cursor == 0)
                    bounds = new MinMaxAABB(point.position, point.position);
                else
                    bounds.Encapsulate(point.position);

                currentEntry.data[cursor] = new float4(point.position, 1f);
                currentEntry.data[cursor + PointRenderContainer.TEXTURE_2D_MAX_HEIGHT] =
                    new float4(point.color, point.timestamp);
                cursor++;
            }

            if (cursor > 0)
                entries[currentIndex] = new PointRenderEntry {
                    data = currentEntry.data,
                    count = (uint)cursor,
                    bounds = bounds
                };
        }
    }
}