using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DefaultNamespace {
    [StructLayout(LayoutKind.Sequential)]
    public struct PointData {
        public float3 position;
        public float3 color;
        public float timestamp;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    public struct PointInsertData {
        public float3 position;
        public float3 color;
        public float timestamp;
        public float density;
    }

    [BurstCompile]
    public struct BurstKdTree {
        public NativeList<KdTreeNode> nodes;
        public NativeList<PointData> points;

        public BurstKdTree(int initialCapacity, Allocator allocator) {
            nodes = new NativeList<KdTreeNode>(initialCapacity, allocator);
            points = new NativeList<PointData>(initialCapacity, allocator);
        }

        public void Dispose() {
            nodes.Dispose();
            points.Dispose();
        }

        public void Insert(PointData point) {
            var pointIndex = points.Length;
            points.Add(point);

            if (nodes.Length == 0) {
                nodes.Add(new KdTreeNode(pointIndex));
            } else {
                Insert(0, pointIndex, 0);
            }
        }

        private void Insert(int nodeIndex, int pointIndex, int depth) {
            while (true) {
                var node = nodes[nodeIndex];
                var nodePoint = points[node.PointIndex];
                var newPoint = points[pointIndex];

                var axis = depth % 3;
                if (ComparePoints(newPoint.position, nodePoint.position, axis) < 0) {
                    if (node.Left == -1) {
                        node.Left = nodes.Length;
                        nodes[nodeIndex] = node;
                        nodes.Add(new KdTreeNode(pointIndex));
                    } else {
                        nodeIndex = node.Left;
                        depth++;
                        continue;
                    }
                } else {
                    if (node.Right == -1) {
                        node.Right = nodes.Length;
                        nodes[nodeIndex] = node;
                        nodes.Add(new KdTreeNode(pointIndex));
                    } else {
                        nodeIndex = node.Right;
                        depth++;
                        continue;
                    }
                }

                break;
            }
        }

        public int FindNearest(float3 target) {
            if (nodes.Length == 0) return -1;
            return FindNearest(0, target, 0, -1);
        }

        private int FindNearest(int nodeIndex, float3 target, int depth, int bestIndex) {
            if (nodeIndex == -1) {
                return bestIndex;
            }

            var node = nodes[nodeIndex];
            var nodePoint = points[node.PointIndex];

            var bestDistance = bestIndex == -1 ? float.MaxValue : math.distance(target, points[bestIndex].position);
            var currentDistance = math.distance(target, nodePoint.position);

            var nextBestIndex = bestIndex;
            if (currentDistance < bestDistance) {
                nextBestIndex = node.PointIndex;
            }

            var axis = depth % 3;
            var nextNode = ComparePoints(target, nodePoint.position, axis) < 0 ? node.Left : node.Right;
            var otherNode = nextNode == node.Left ? node.Right : node.Left;

            nextBestIndex = FindNearest(nextNode, target, depth + 1, nextBestIndex);

            var axisDistance = math.abs(GetCoordinate(target, axis) - GetCoordinate(nodePoint.position, axis));
            if (axisDistance < math.distance(target, points[nextBestIndex].position)) {
                nextBestIndex = FindNearest(otherNode, target, depth + 1, nextBestIndex);
            }

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
    }

    [BurstCompile]
    public struct TreeInsertJob : IJob {
        public NativeList<PointInsertData> data;
        public BurstKdTree tree;

        public void Execute() {
            foreach (var d in data) {
                var nearIndex = tree.FindNearest(d.position);
                if (nearIndex == -1) goto insert;
                
                var nearestData = tree.points[nearIndex];
                if (math.distance(d.position, nearestData.position) < d.density) {
                    nearestData.timestamp = d.timestamp;
                    tree.points[nearIndex] = nearestData;
                    continue;
                }

                insert:
                {
                    tree.Insert(new PointData {
                        position = d.position,
                        color = d.color,
                        timestamp = d.timestamp
                    });
                }
            }
        }

        public static void Run(NativeList<PointInsertData> positions, BurstKdTree tree) {
            var job = new TreeInsertJob {
                data = positions,
                tree = tree
            };
            job.Run();
        }
    }

    [BurstCompile]
    public struct TreeCleanupJob : IJob {
        public float currentTime;
        public float maxAge;
        public BurstKdTree tree;

        public void Execute() {
            var buffer = new NativeList<PointData>(tree.points.Length, Allocator.Persistent);
            
            foreach (var point in tree.points) {
                if (currentTime - point.timestamp < maxAge) {
                    buffer.Add(point);
                }
            }

            if (buffer.Length != tree.points.Length) {
                tree.points.Clear();
                tree.nodes.Clear();
            
                foreach (var data in buffer) {
                    tree.Insert(data);
                }
            }
            
            buffer.Dispose();
        }
    }

    public class KDTree {
        public class Node {
            public PointData Point { get; set; }
            public Node Left { get; set; }
            public Node Right { get; set; }

            public Node(PointData point) {
                Point = point;
            }
        }

        public Node root;
        private readonly System.Random _random = new();

        public void Insert(PointData point) {
            root = Insert(root, point, 0);
        }

        private Node Insert(Node node, PointData point, int depth) {
            if (node == null) {
                return new Node(point);
            }

            var axis = depth % 3;
            if (ComparePoints(point.position, node.Point.position, axis) < 0) {
                node.Left = Insert(node.Left, point, depth + 1);
            } else {
                node.Right = Insert(node.Right, point, depth + 1);
            }

            return node;
        }

        public Node FindNearest(float3 target) {
            return FindNearest(root, target, 0, null) ?? default;
        }

        private Node FindNearest(Node node, float3 target, int depth, Node best) {
            if (node == null) {
                return best;
            }

            var bestDistance = best == null ? float.MaxValue : math.distance(target, best.Point.position);
            var currentDistance = math.distance(target, node.Point.position);

            var nextBest = best;
            if (currentDistance < bestDistance) {
                nextBest = node;
            }

            var axis = depth % 3;
            var nextNode = ComparePoints(target, node.Point.position, axis) < 0 ? node.Left : node.Right;
            var otherNode = nextNode == node.Left ? node.Right : node.Left;

            nextBest = FindNearest(nextNode, target, depth + 1, nextBest);

            if (math.abs(GetCoordinate(target, axis) - GetCoordinate(node.Point.position, axis)) <
                math.distance(target, nextBest.Point.position)) {
                nextBest = FindNearest(otherNode, target, depth + 1, nextBest);
            }

            return nextBest;
        }


        public int GetMaxHeight() {
            return GetHeight(root);
        }

        public double GetAverageHeight() {
            var totalHeight = 0;
            var nodeCount = 0;
            CalculateHeightStats(root, 1, ref totalHeight, ref nodeCount);
            return nodeCount == 0 ? 0 : (double)totalHeight / nodeCount;
        }

        private int GetHeight(Node node) {
            if (node == null) {
                return 0;
            }

            return 1 + Mathf.Max(GetHeight(node.Left), GetHeight(node.Right));
        }

        private void CalculateHeightStats(Node node, int currentHeight, ref int totalHeight, ref int nodeCount) {
            if (node == null) {
                return;
            }

            totalHeight += currentHeight;
            nodeCount++;

            CalculateHeightStats(node.Left, currentHeight + 1, ref totalHeight, ref nodeCount);
            CalculateHeightStats(node.Right, currentHeight + 1, ref totalHeight, ref nodeCount);
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
    }
}