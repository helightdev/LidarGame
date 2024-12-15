using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Behaviours {
    public class PlayerMovement : MonoBehaviour {
        [SerializeField] private CharacterController characterController;
        [SerializeField] private float horizontalSpeed;
        [SerializeField] private float verticalJumpSpeed;
        [SerializeField] private float gravity = 9.81f;

        public Camera mainCamera;
        public CinemachineCamera virtualCamera;

        public InputActionReference moveAction;
        public InputActionReference jumpAction;

        public Transform cameraBone;
        private Transform _cameraTransform;

        private Transform _transform;
        private float _upwardMotion;
        private bool IsJumping => _upwardMotion > 0;


        private void Awake() {
            _transform = GetComponent<Transform>();
            _cameraTransform = mainCamera.transform;
            jumpAction.action.performed += _ => Jump();
            // Lock cursor to center of screen
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void Update() {
            if (virtualCamera.IsLive) {
                var currentCameraAngle = _cameraTransform.localEulerAngles;
                _transform.localEulerAngles = new Vector3(0, currentCameraAngle.y, 0);
                cameraBone.localEulerAngles = new Vector3(currentCameraAngle.x, 0, 0);
            }

            var movement = moveAction.action.ReadValue<Vector2>();
            var horizontalVelocity = (_transform.forward * movement.y + _transform.right * movement.x).normalized *
                                     horizontalSpeed;
            SetUpwardMotion(Time.deltaTime);
            var verticalVelocity = Vector3.up * _upwardMotion;

            var motion = (horizontalVelocity + verticalVelocity) * Time.deltaTime;
            characterController.Move(motion);
        }

        public void Jump() {
            if (!characterController.isGrounded) {
                return;
            }

            _upwardMotion = verticalJumpSpeed;
        }

        private void SetUpwardMotion(float deltaTime) {
            if (characterController.isGrounded && !IsJumping) {
                _upwardMotion = 0;
            }

            _upwardMotion -= gravity * deltaTime;
        }
    }
}