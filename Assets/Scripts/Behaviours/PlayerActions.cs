using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Behaviours {
    public class PlayerActions : MonoBehaviour {
        public InputActionReference shootAction;
        public InputActionReference optimizeAction;

        public Transform cameraBone;
        public Camera playerCamera;

        private void Awake() {
            Application.targetFrameRate = 144;
        }

        private void Update() {
            if (shootAction.action.IsPressed()) {
                var pos = cameraBone.position;
                var forward = cameraBone.forward;
                PointRaycaster.Instance.Brush(pos, forward, 180, 256);
            }

            if (optimizeAction.action.triggered) {
                PointRaycaster.Instance.ScanLines(playerCamera, 0.5f, 8).Forget();
            }
        }
    }
}