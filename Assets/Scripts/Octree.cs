using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace DefaultNamespace {
    
    [StructLayout(LayoutKind.Sequential)]
    public struct AABB {
        public float3 Min;
        public float3 Max;

        public AABB(float3 min, float3 max) {
            Min = min;
            Max = max;
        }

        public float3 Center => (Min + Max) * 0.5f;
        public float3 Extents => (Max - Min) * 0.5f;

        public bool Contains(float3 p) {
            return p.x >= Min.x && p.x <= Max.x &&
                   p.y >= Min.y && p.y <= Max.y &&
                   p.z >= Min.z && p.z <= Max.z;
        }

        public bool IntersectsSphere(float3 center, float radius) {
            // Used in nearest search pruning: 
            // Find the point on AABB closest to the sphere center
            float3 clamped = math.clamp(center, Min, Max);
            float distSq = math.lengthsq(clamped - center);
            return distSq <= radius * radius;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OctreeNode {
        // AABB defining the region of space this node covers
        public AABB Bounds;

        // Indices of children nodes: -1 if child does not exist
        public int Child0;
        public int Child1;
        public int Child2;
        public int Child3;
        public int Child4;
        public int Child5;
        public int Child6;
        public int Child7;

        // Index in the points array where this node's points start
        public int PointStartIndex;

        // How many points this node currently holds (before subdivision)
        public int PointCount;

        public OctreeNode(AABB bounds) {
            Bounds = bounds;
            Child0 = Child1 = Child2 = Child3 = Child4 = Child5 = Child6 = Child7 = -1;
            PointStartIndex = -1;
            PointCount = 0;
        }

        public bool IsLeaf => Child0 == -1 && Child1 == -1 && Child2 == -1 && Child3 == -1 &&
                              Child4 == -1 && Child5 == -1 && Child6 == -1 && Child7 == -1;
    }

    [BurstCompile]
    public struct BurstOctree : IDisposable {
        // Maximum number of points in a leaf node before subdivision
        private const int MaxPointsPerNode = 8;

        public NativeList<OctreeNode> nodes;
        public NativeList<PointData> points;

        // This array maps from a node's point storage to the actual point index in `points`.
        // i.e., node i has points [PointIndexBuffer[node.PointStartIndex] ...]
        public NativeList<int> PointIndexBuffer;

        // Used to keep track of current capacity in the PointIndexBuffer
        private int _pointIndexCount;

        public BurstOctree(AABB worldBounds, int initialCapacity, Allocator allocator) {
            nodes = new NativeList<OctreeNode>(initialCapacity, allocator);
            points = new NativeList<PointData>(initialCapacity, allocator);
            PointIndexBuffer = new NativeList<int>(initialCapacity, allocator);
            _pointIndexCount = 0;

            // Create root node
            nodes.Add(new OctreeNode(worldBounds));
        }

        public void Dispose() {
            nodes.Dispose();
            points.Dispose();
            PointIndexBuffer.Dispose();
        }

        public void Insert(PointData point) {
            // Add the point to the global list
            int pointIndex = points.Length;
            points.Add(point);

            InsertPoint(0, pointIndex);
        }

        private void InsertPoint(int nodeIndex, int pointIndex) {
            var node = nodes[nodeIndex];

            // This should never happen if bounds are chosen correctly at the start
            if (!node.Bounds.Contains(points[pointIndex].position)) {
                // The point lies outside this node's region. Either ignore or handle error.
                return;
            }

            if (node.IsLeaf && node.PointCount < MaxPointsPerNode) {
                // Insert point directly into this leaf
                if (node.PointStartIndex == -1) {
                    node.PointStartIndex = _pointIndexCount;
                }

                PointIndexBuffer.Add(pointIndex);
                node.PointCount++;
                _pointIndexCount++;

                nodes[nodeIndex] = node;
            } else {
                // If leaf but full, subdivide if not done already
                if (node.IsLeaf) {
                    Subdivide(nodeIndex);
                    // After subdivision, redistribute existing points
                    RedistributePoints(nodeIndex);
                    node = nodes[nodeIndex]; // refresh after redistribution
                }

                // Insert into the appropriate child
                int childIndex = GetChildIndex(node.Bounds, points[pointIndex].position);
                InsertPoint(childIndex, pointIndex);
            }
        }

        private void Subdivide(int nodeIndex) {
            var node = nodes[nodeIndex];
            float3 c = node.Bounds.Center;
            float3 e = node.Bounds.Extents;

            // Create 8 child bounding boxes
            // Each octant: half-size boxes. The pattern:
            // For each axis, lower half or upper half: min or max side of center
            // Child order pattern (example):
            // 0: (-x, -y, -z)
            // 1: (+x, -y, -z)
            // 2: (-x, +y, -z)
            // 3: (+x, +y, -z)
            // 4: (-x, -y, +z)
            // 5: (+x, -y, +z)
            // 6: (-x, +y, +z)
            // 7: (+x, +y, +z)

            float3 min = node.Bounds.Min;
            float3 max = node.Bounds.Max;

            float3 half = (max - min) * 0.5f;

            // We define a function to create AABBs for children:
            AABB ChildAABB(bool x, bool y, bool z) {
                float3 newMin = new float3(
                    x ? c.x : min.x,
                    y ? c.y : min.y,
                    z ? c.z : min.z
                );
                float3 newMax = new float3(
                    x ? max.x : c.x,
                    y ? max.y : c.y,
                    z ? max.z : c.z
                );
                return new AABB(newMin, newMax);
            }

            node.Child0 = CreateNode(ChildAABB(false, false, false));
            node.Child1 = CreateNode(ChildAABB(true, false, false));
            node.Child2 = CreateNode(ChildAABB(false, true, false));
            node.Child3 = CreateNode(ChildAABB(true, true, false));
            node.Child4 = CreateNode(ChildAABB(false, false, true));
            node.Child5 = CreateNode(ChildAABB(true, false, true));
            node.Child6 = CreateNode(ChildAABB(false, true, true));
            node.Child7 = CreateNode(ChildAABB(true, true, true));

            nodes[nodeIndex] = node;
        }

        private int CreateNode(AABB bounds) {
            var childNode = new OctreeNode(bounds);
            int childIndex = nodes.Length;
            nodes.Add(childNode);
            return childIndex;
        }

        private void RedistributePoints(int nodeIndex) {
            var node = nodes[nodeIndex];

            // Move existing points into children
            for (int i = 0; i < node.PointCount; i++) {
                int globalPointIndex = PointIndexBuffer[node.PointStartIndex + i];
                float3 p = points[globalPointIndex].position;

                int childIndex = GetChildIndex(node.Bounds, p);
                InsertPoint(childIndex, globalPointIndex);
            }

            // Clear current node's point storage
            // This node is no longer a leaf and holds no direct points
            node.PointStartIndex = -1;
            node.PointCount = 0;
            nodes[nodeIndex] = node;
        }

        private static int GetChildIndex(AABB bounds, float3 p) {
            float3 c = bounds.Center;
            int index = 0;
            if (p.x > c.x) index |= 1;
            if (p.y > c.y) index |= 2;
            if (p.z > c.z) index |= 4;
            return index;
        }

        public int FindNearest(float3 target) {
            if (nodes.Length == 0) return -1;
            float bestDist = float.MaxValue;
            int bestIndex = -1;
            FindNearestRecursive(0, target, ref bestDist, ref bestIndex);
            return bestIndex;
        }

        private void FindNearestRecursive(int nodeIndex, float3 target, ref float bestDist, ref int bestIndex) {
            if (nodeIndex == -1) return;

            var node = nodes[nodeIndex];

            // If this node is a leaf, check all points
            if (node.IsLeaf && node.PointCount > 0) {
                for (int i = 0; i < node.PointCount; i++) {
                    int pIndex = PointIndexBuffer[node.PointStartIndex + i];
                    float d = math.distance(points[pIndex].position, target);
                    if (d < bestDist) {
                        bestDist = d;
                        bestIndex = pIndex;
                    }
                }
            } else {
                // This is an internal node, check children that could have closer points
                // Since we have a bestDist, we can prune children whose bounding boxes are too far

                // Check all children that exist
                float radius = bestDist;
                for (int i = 0; i < 8; i++) {
                    int child = GetChildAt(node, i);
                    if (child == -1) continue;
                    var childBounds = nodes[child].Bounds;
                    if (childBounds.IntersectsSphere(target, radius)) {
                        FindNearestRecursive(child, target, ref bestDist, ref bestIndex);
                        // Update radius if we found a closer point
                        radius = bestDist;
                    }
                }
            }
        }

        private static int GetChildAt(OctreeNode node, int i) {
            switch (i) {
                case 0: return node.Child0;
                case 1: return node.Child1;
                case 2: return node.Child2;
                case 3: return node.Child3;
                case 4: return node.Child4;
                case 5: return node.Child5;
                case 6: return node.Child6;
                case 7: return node.Child7;
            }

            return -1;
        }
    }
}