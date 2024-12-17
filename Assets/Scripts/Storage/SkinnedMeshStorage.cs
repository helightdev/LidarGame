using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Common;
using Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;
using Random = Unity.Mathematics.Random;

namespace Storage {
    public class SkinnedMeshStorage : PointStorageBase {
        public const int TEXTURE_2D_MAX_HEIGHT = 16384;
        public const int TEXTURE_2D_COLUMNS = 2;

        public SkinnedMeshRenderer skinnedMeshRenderer;
        public VisualEffect effect;

        [HideInInspector, NonSerialized] public Texture2D texture;
        [HideInInspector, NonSerialized] public int actualVertexCount;
        public NativeArray<Color> vertexPositions;
        private PointRenderContainer _container;

        public float density = 0.01f;

        private void Awake() {
            var mesh = skinnedMeshRenderer.sharedMesh;
            actualVertexCount = math.min(mesh.vertexCount, TEXTURE_2D_MAX_HEIGHT);

            var stopWatch = System.Diagnostics.Stopwatch.StartNew();
            var meshVertexCount = mesh.vertexCount;
            
            var nativeVertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
            var tree = new SimpleKdTree(meshVertexCount, Allocator.TempJob);
            new SampleSkinJob {
                vertices = nativeVertices,
                simpleTree = tree,
                actualVertexCount = actualVertexCount,
                density = density
            }.Run();
            nativeVertices.Dispose();
            
            actualVertexCount = tree.points.Length;
            vertexPositions = new NativeArray<Color>(actualVertexCount * TEXTURE_2D_COLUMNS, Allocator.Persistent);
            
            new CreateVertexPositions {
                tree = tree,
                vertexPositions = vertexPositions,
                actualVertexCount = actualVertexCount
            }.Run();
            
            tree.Dispose();
            
            texture = new Texture2D(actualVertexCount, 2, TextureFormat.RGBAFloat, false);
            stopWatch.Stop();
            Debug.Log($"Tree generation took {stopWatch.ElapsedMilliseconds}ms");
        }

        private void FixedUpdate() {
            texture.SetPixelData(vertexPositions, 0);
            texture.Apply();

            effect.SetTexture("Positions", texture);
            effect.SetUInt("Capacity", (uint)actualVertexCount);
            effect.Reinit();
        }

        public override void Init(PointRenderContainer container) {
            _container = container;
        }

        public override void Insert(PointInsertData data) {
            var i = UnityEngine.Random.Range(0, actualVertexCount);
            var p = vertexPositions[i + actualVertexCount];
            p.a = Time.time;
            vertexPositions[i + actualVertexCount] = p;
        }

        public override void UpdatePlayerPosition(float3 position) { }

        public override void Dispose() {
            vertexPositions.Dispose();
        }

        public struct CreateVertexPositions : IJob {
            [ReadOnly] public SimpleKdTree tree;
            [WriteOnly] public NativeArray<Color> vertexPositions;
            public int actualVertexCount;
            
            public void Execute() {
                for (var i = 0; i < actualVertexCount; i++) {
                    var index = (uint)tree.points[i].data;
                    vertexPositions[i] = new Color(4, 0, 0, index);
                    vertexPositions[i + actualVertexCount] = new Color(0, 0, 0, -999);
                }
            }
        }
        
        public struct SampleSkinJob : IJob {
            
            [ReadOnly] public NativeArray<Vector3> vertices;
            public int actualVertexCount;
            public float density;
            public SimpleKdTree simpleTree;
            
            public void Execute() {
                for (var i = 0; i < vertices.Length; i++) {
                    if (simpleTree.points.Length >= actualVertexCount) {
                        Debug.Log("Tree is full");
                        break;
                    }

                    var vertex = vertices[i];
                    var found = simpleTree.FindNearest(vertex);
                    if (found == -1) {
                        simpleTree.Insert(new SimplePointData {
                            position = vertex,
                            data = i
                        });
                    } else {
                        var point = simpleTree.points[found];
                        if (math.distance(point.position, vertex) > density) {
                            simpleTree.Insert(new SimplePointData {
                                position = vertex,
                                data = i
                            });
                        }
                    }
                }
            }
        }
    }
}