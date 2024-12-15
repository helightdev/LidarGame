using System;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DefaultNamespace {
    
    public enum LidarColorMode {
        Solid,
        Gradient
    }
    public class LidarEnvironment : MonoBehaviour {
        public PointRenderContainer container;
        [ColorUsage(false, true)]
        public Color color;
        public float density = 0.05f;
        
        [GradientUsage(true)]
        public Gradient gradient;
        public LidarColorMode colorMode = LidarColorMode.Solid;
        
        public Color SampleColor() {
            return colorMode switch {
                LidarColorMode.Solid => color,
                LidarColorMode.Gradient => gradient.Evaluate(Random.value),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        [ContextMenu("Reveal")]
        public void Reveal() {
            if (TryGetComponent(out MeshFilter filter)) {
                var mesh = filter.sharedMesh;
                for (var i = 0; i < 256; i++) {
                    var meshPosition = SamplePointOnMesh(mesh);
                    var point = transform.TransformPoint(meshPosition);
                    HandleRaycastHit(point);
                }
            }
        }
        
        public void HandleRaycastHit(Vector3 point) {
            var c = SampleColor();
            container.ScheduleInsertData(new PointInsertData {
                position = point,
                color = new float3(c.r, c.g, c.b),
                density = density,
                timestamp = Time.time
            });
        }
        
        private static Vector3 SamplePointOnMesh(Mesh mesh) {
            var triangles = mesh.triangles;
            var index = Mathf.FloorToInt(Random.value * (triangles.Length / 3f));
            var v0 = mesh.vertices[triangles[index * 3 + 0]];
            var v1 = mesh.vertices[triangles[index * 3 + 1]];
            var v2 = mesh.vertices[triangles[index * 3 + 2]];
            return Vector3.Lerp(v0, Vector3.Lerp(v1, v2, Random.value), Random.value);
        }
    }
}