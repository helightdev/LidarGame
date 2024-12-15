using UnityEngine;
using Random = UnityEngine.Random;

namespace Behaviours {
    public class PointRaycaster : MonoBehaviour {
        public LayerMask mask;
        public static PointRaycaster Instance { get; private set; }

        private void Awake() {
            if (Instance == null) {
                Instance = this;
            } else {
                Destroy(gameObject);
            }
        }


        public void Brush(Vector3 position, Vector3 forward, float angle, int sampleCount) {
            forward.Normalize();

            for (var i = 0; i < sampleCount; i++) {
                var randomDirection = Random.insideUnitSphere.normalized;
                randomDirection = Vector3.Slerp(forward, randomDirection, Random.Range(0f, angle / 180f));
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