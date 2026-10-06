// Autor: Murillo Gomes Yonamine
// Data: 15/02/2026

using System;
using Sirenix.OdinInspector;
using UnityEngine;
using FifthSemester.Core.Events;
using FifthSemester.Core.Services;

namespace FifthSemester.Player.Components {
    public class PlayerJump : MonoBehaviour, IPauseable {
        [Header("Jump")]
        [FoldoutGroup("Jump")]
        [SerializeField] private bool _enableJump = true;

        [FoldoutGroup("Jump"), ShowIf("_enableJump"), Range(1f, 20f)]
        [SerializeField] private float _jumpUpVelocity = 5f;

        [SerializeField, Range(0f, 1f), Tooltip("Tempo que o jogador 'flutua' no topo do pulo, antes de começar a cair")] 
        private float _hangTime = 0.2f;    
        
        [SerializeField, Range(1f, 5f), Tooltip("Multiplicador de gravidade durante a queda")] 
        private float _fallGravityMultiplier = 2f;
        
        [SerializeField] private float _groundCheckDistance = 0.75f;

        private Rigidbody _rigidbody;
        private PlayerMovement _movement;
        private PlayerController _player;
        private IEventBus _eventBus;
        private IPauseService _pauseService;
        private bool _paused;

        private bool _isGrounded;
        private bool _isJumping;
        private float _airTime;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            _player = GetComponent<PlayerController>();
            _rigidbody = _player.GetComponent<Rigidbody>();
            _movement = _player.GetComponent<PlayerMovement>();
        }

        private void OnEnable() {
            _pauseService.Register(this);
        }

        private void Start() {
            _eventBus = ServiceLocator.Get<IEventBus>();
            _eventBus?.Subscribe<JumpInputEvent>(HandleJump);
        }

        private void OnDisable() {
            _pauseService.Unregister(this);
            _eventBus?.Unsubscribe<JumpInputEvent>(HandleJump);
        }

        private void FixedUpdate() {
            if (_paused) return;
            CheckGround();
            if (_rigidbody == null) return;

            if (_isGrounded) {
                _isJumping = false;
                _airTime = 0f;
                return;
            }

            if (!_isJumping) return;

            _airTime += Time.fixedDeltaTime;

            Vector3 vel = _rigidbody.linearVelocity;

            if (vel.y <= 0f) {
                if (_airTime < _hangTime) {
                    if (vel.y < -0.1f) {
                        vel.y = -0.1f;
                    }
                }
                else {
                    vel += Physics.gravity * (_fallGravityMultiplier - 1f) * Time.fixedDeltaTime;
                }

                _rigidbody.linearVelocity = vel;
            }
        }

        private void HandleJump(JumpInputEvent evt) {
            if (_paused || !_enableJump) return;
            if (!_isGrounded) return;

            Jump();
        }

        public void OnPause() { _paused = true; }
        public void OnResume() { _paused = false; }

        private void Jump() {
            if (!_isGrounded || _rigidbody == null) return;

            Vector3 vel = _rigidbody.linearVelocity;
            vel.y = _jumpUpVelocity;
            _rigidbody.linearVelocity = vel;

            _isGrounded = false;
            _isJumping = true;
            _airTime = 0f;

            if (_movement != null && _movement.IsCrouched && !_movement.HoldToCrouch) {
                _movement.StopCrouch();
            }
        }

        private void CheckGround() {
            if (_movement == null) return;

            Transform playerTransform = _movement.PlayerTransform;

            Vector3 origin = new Vector3(
                playerTransform.position.x,
                playerTransform.position.y - (playerTransform.localScale.y * 0.5f),
                playerTransform.position.z
            );

            Vector3 direction = playerTransform.TransformDirection(Vector3.down);
            float distance = _groundCheckDistance;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance)) {
                Debug.DrawRay(origin, direction * distance, Color.red);
                _isGrounded = true;
                return;
            }

            _isGrounded = false;
        }

        private void OnDrawGizmos() {
            if (_movement == null) return;

            Transform playerTransform = _movement.PlayerTransform;

            Vector3 origin = new Vector3(
                playerTransform.position.x,
                playerTransform.position.y - (playerTransform.localScale.y * 0.5f),
                playerTransform.position.z
            );

            Vector3 direction = playerTransform.TransformDirection(Vector3.down);
            float distance = _groundCheckDistance;

            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + direction * distance);
        }

        public bool IsGrounded => _isGrounded;

        public bool EnableJump => _enableJump;
        public float JumpUpVelocity => _jumpUpVelocity;
        public float HangTime => _hangTime;
        public float FallGravityMultiplier => _fallGravityMultiplier;
        public float GroundCheckDistance => _groundCheckDistance;

        #region Dev Tuning API

        public void SetEnableJump(bool value) {
            _enableJump = value;
        }

        public void SetJumpUpVelocity(float value) {
            _jumpUpVelocity = Mathf.Max(0f, value);
        }

        public void SetHangTime(float value) {
            _hangTime = Mathf.Max(0f, value);
        }

        public void SetFallGravityMultiplier(float value) {
            _fallGravityMultiplier = Mathf.Max(0.1f, value);
        }

        public void SetGroundCheckDistance(float value) {
            _groundCheckDistance = Mathf.Max(0.01f, value);
        }
        #endregion
    }
}
