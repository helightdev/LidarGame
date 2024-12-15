using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DefaultNamespace {
    public class PlayerActions : MonoBehaviour {
        
        public InputActionReference shootAction;
        public Transform cameraBone;

        private void Update() {
            if (shootAction.action.IsPressed()) {
                var pos = cameraBone.position;
                var forward = cameraBone.forward;
                PointRaycaster.Instance.Brush(pos, forward, 30, 64);
            }
        }
    }
}