using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Behaviours {
    public class PointRaycaster : MonoBehaviour {
        public LayerMask mask;
        public static PointRaycaster Instance { get; private set; }

        private void Awake() {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }


        public void Brush(Vector3 position, Vector3 forward, float angle, int sampleCount) {
            forward.Normalize();

            for (var i = 0; i < sampleCount; i++) {
                var randomDirection = Random.insideUnitSphere.normalized;
                randomDirection = Vector3.Slerp(forward, randomDirection, Random.Range(0f, angle / 180f));
                Raycast(position, randomDirection);
            }
        }

        public async UniTask ScanLines(Camera c, float resolution, int linesPerFixedFrame) {
            var scanWidth = (int)(720 * resolution);
            var scanHeight = (int)(480 * resolution);

            var stepY = (1f / scanWidth);
            var stepX = (1f / scanHeight);
            
            var bufferCount = scanWidth * linesPerFixedFrame;
            var vectors = new Vector3[bufferCount];
            var directions = new Vector3[bufferCount];

            var i = 0;
            var cbi = 0;
            for (float y = 0; y <= 1; y+=stepY) {
                for (float x = 0; x <= 1; x+=stepX) {
                    var ray = c.ViewportPointToRay(new Vector3(x, y, 0));
                    var randomDirection = Random.insideUnitSphere.normalized;
                    randomDirection = Vector3.Slerp(ray.direction, randomDirection, Random.Range(0f, 5 / 180f));
                    vectors[cbi] = ray.origin;
                    directions[cbi] = randomDirection;
                    cbi++;
                }
                if (++i % linesPerFixedFrame == 0) {
                    RaycastBatch(vectors, directions, cbi);
                    cbi = 0;
                    await UniTask.WaitForFixedUpdate();
                }
            }
        }

        public void Raycast(Vector3 position, Vector3 direction) {
            if (Physics.Raycast(position, direction, out var hit, 1000, mask)) {
                if (hit.collider.TryGetComponent<LidarEnvironment>(out var environment))
                    environment.HandleRaycastHit(hit.point);
                else
                    Debug.Log("No LidarEnvironment found");
            }
        }
        
        public void RaycastBatch(Vector3[] positions, Vector3[] directions, int length) {
            var commands = new NativeArray<RaycastCommand>(length, Allocator.TempJob);
            var results = new NativeArray<RaycastHit>(length, Allocator.TempJob);
            try {
                for (var i = 0; i < length; i++) {
                    commands[i] = new RaycastCommand(positions[i], directions[i], new QueryParameters() {
                        layerMask = mask,
                    });
                }

                RaycastCommand.ScheduleBatch(commands, results, 1, 1).Complete();

                var colliderCache = new Dictionary<int, LidarEnvironment>();
                for (var i = 0; i < length; i++) {
                    var result = results[i];
                    if (result.collider == null) continue;
                    if (colliderCache.TryGetValue(result.colliderInstanceID, out var env)) {
                        if (env != null) env.HandleRaycastHit(result.point);
                    } else {
                        if (result.collider.TryGetComponent(out env)) {
                            env.HandleRaycastHit(result.point);
                        }

                        colliderCache.Add(result.colliderInstanceID, env);
                    }
                }
            } finally {
                commands.Dispose();
                results.Dispose();
            }
        }
    }
}