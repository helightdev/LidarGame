using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Common {
    [BurstCompile]
    public struct SimpleKdTree {
        [StructLayout(LayoutKind.Sequential)]
        public struct KdTreeNode {
            public int PointIndex;
            public int Left;
            public int Right;

            public KdTreeNode(int pointIndex) {
                PointIndex = pointIndex;
                Left = -1;
                Right = -1;
            }
        }

        public NativeList<KdTreeNode> nodes;
        public NativeList<SimplePointData> points;

        public SimpleKdTree(int initialCapacity, Allocator allocator) {
            nodes = new NativeList<KdTreeNode>(initialCapacity, allocator);
            points = new NativeList<SimplePointData>(initialCapacity, allocator);
        }

        public void Dispose() {
            nodes.Dispose();
            points.Dispose();
        }

        public void Insert(SimplePointData point) {
            var pointIndex = points.Length;
            points.Add(point);

            if (nodes.Length == 0)
                nodes.Add(new KdTreeNode(pointIndex));
            else
                Insert(0, pointIndex, 0);
        }

        public NativeList<SimplePointData> TraverseLeftToRightIterative(Allocator allocator) {
            var data = new NativeList<SimplePointData>(points.Length, allocator);
            if (nodes.Length == 0) return data;

            var stack = new NativeList<int>(Allocator.Temp);
            try {
                stack.Add(0);
                while (stack.Length > 0) {
                    var nodeIndex = stack[^1];
                    stack.RemoveAt(stack.Length - 1);
                    if (nodeIndex == -1) continue;

                    var node = nodes[nodeIndex];
                    stack.Add(node.Right);
                    stack.Add(node.Left);
                    data.Add(points[node.PointIndex]);
                }

                return data;
            } finally {
                stack.Dispose();
            }
        }

        private void Insert(int nodeIndex, int pointIndex, int depth) {
            var axis = depth % 3;
            while (true) {
                var node = nodes[nodeIndex];
                var nodePoint = points[node.PointIndex];
                var newPoint = points[pointIndex];
                if (ComparePoints(newPoint.position, nodePoint.position, axis) < 0) {
                    if (node.Left == -1) {
                        node.Left = nodes.Length;
                        nodes[nodeIndex] = node;
                        nodes.Add(new KdTreeNode(pointIndex));
                        break;
                    }

                    nodeIndex = node.Left;
                } else {
                    if (node.Right == -1) {
                        node.Right = nodes.Length;
                        nodes[nodeIndex] = node;
                        nodes.Add(new KdTreeNode(pointIndex));
                        break;
                    }

                    nodeIndex = node.Right;
                }

                axis = (axis + 1) % 3;
            }
        }

        public int FindNearest(float3 target) {
            if (nodes.Length == 0) return -1;

            return FindNearest(0, target, 0, -1);
        }

        private int FindNearest(int nodeIndex, float3 target, int depth, int bestIndex) {
            if (nodeIndex == -1) return bestIndex;

            var node = nodes[nodeIndex];
            var nodePoint = points[node.PointIndex];

            var bestDistance = bestIndex == -1 ? float.MaxValue : math.distance(target, points[bestIndex].position);
            var currentDistance = math.distance(target, nodePoint.position);

            var nextBestIndex = bestIndex;
            if (currentDistance < bestDistance) nextBestIndex = node.PointIndex;

            var axis = depth % 3;
            var nextNode = ComparePoints(target, nodePoint.position, axis) < 0 ? node.Left : node.Right;
            var otherNode = nextNode == node.Left ? node.Right : node.Left;

            nextBestIndex = FindNearest(nextNode, target, depth + 1, nextBestIndex);

            var axisDistance = math.abs(GetCoordinate(target, axis) - GetCoordinate(nodePoint.position, axis));
            if (axisDistance < math.distance(target, points[nextBestIndex].position))
                nextBestIndex = FindNearest(otherNode, target, depth + 1, nextBestIndex);

            return nextBestIndex;
        }


        private static int ComparePoints(float3 a, float3 b, int axis) {
            return axis switch {
                0 => a.x.CompareTo(b.x),
                1 => a.y.CompareTo(b.y),
                2 => a.z.CompareTo(b.z),
                _ => throw new ArgumentOutOfRangeException(nameof(axis))
            };
        }

        private static float GetCoordinate(float3 point, int axis) {
            return axis switch {
                0 => point.x,
                1 => point.y,
                2 => point.z,
                _ => throw new ArgumentOutOfRangeException(nameof(axis))
            };
        }


        public int GetMaxDepth() {
            return GetMaxDepth(0);
        }

        private int GetMaxDepth(int nodeIndex) {
            if (nodeIndex == -1) return 0;

            var node = nodes[nodeIndex];
            return 1 + math.max(GetMaxDepth(node.Left), GetMaxDepth(node.Right));
        }

        public float GetAverageDepth() {
            if (nodes.Length == 0) return 0;

            var totalDepth = 0;
            var nodeCount = 0;
            GetDepths(0, 0, ref totalDepth, ref nodeCount);
            return (float)totalDepth / nodeCount;
        }

        private void GetDepths(int nodeIndex, int currentDepth, ref int totalDepth, ref int nodeCount) {
            if (nodeIndex == -1) return;

            totalDepth += currentDepth;
            nodeCount++;

            var node = nodes[nodeIndex];
            GetDepths(node.Left, currentDepth + 1, ref totalDepth, ref nodeCount);
            GetDepths(node.Right, currentDepth + 1, ref totalDepth, ref nodeCount);
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct SimplePointData {
        public float3 position;
        public float data;
    }
}