using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DefaultNamespace {
    public class PointRaycaster : MonoBehaviour {
        public static PointRaycaster Instance { get; private set; }

        public LayerMask mask;
        
        private void Awake() {
            if (Instance == null) {
                Instance = this;
            } else {
                Destroy(gameObject);
            }
        }
        

        // Circular brush that shoots rays in a cone with a given angle spread
        public void Brush(Vector3 position, Vector3 forward, float angle, int sampleCount) {
            // Normalize the forward direction
            forward.Normalize();

            // Iterate through the number of samples
            for (int i = 0; i < sampleCount; i++) {
                // Randomly pick a direction within the cone defined by the angle
                Vector3 randomDirection = Random.insideUnitSphere.normalized; // Random unit vector
                randomDirection = Vector3.Slerp(forward, randomDirection, Random.Range(0f, angle / 180f));

                // Raycast from the position in the computed random direction
                Raycast(position, randomDirection);
            }
        }

        public void Raycast(Vector3 position, Vector3 direction) {
            if (Physics.Raycast(position, direction, out var hit, 1000, mask)) {
                if (hit.collider.TryGetComponent<LidarEnvironment>(out var environment)) {
                    environment.HandleRaycastHit(hit.point);
                } else {
                    Debug.Log("No LidarEnvironment found");
                }
            }
        }
        
    }
}