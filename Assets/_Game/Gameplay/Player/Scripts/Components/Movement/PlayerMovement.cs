// Autor: Murillo Gomes Yonamine
// Data: 14/02/2026

using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using Sirenix.OdinInspector;
using UnityEngine;

namespace FifthSemester.Player.Components {
    public class PlayerMovement : MonoBehaviour, IPauseable {

        private Rigidbody _rigidbody;
        private MovementState _currentState;
        private PlayerController _player;
        private IEventBus _eventBus;
        private IAudioService _audioService;
        private IPauseService _pauseService;
        private bool _paused;
        private bool _wasKinematic;
        private Vector3 _storedVelocity;
        private Vector3 _storedAngularVelocity;

        [Header("Movement")]
        [FoldoutGroup("Movement")]
        [SerializeField] private bool _playerCanMove = true;
        [FoldoutGroup("Movement")]
        [SerializeField] private float _walkSpeed = 5f;
        [FoldoutGroup("Movement")]
        [SerializeField] private float _maxVelocityChange = 10f;

        private Vector2 _moveInput;
        private bool _isWalking;


        [Header("Sprint")]
        [FoldoutGroup("Sprint")]
        [SerializeField] private bool _enableSprint = true;
        [FoldoutGroup("Sprint"), ShowIf("_enableSprint")]
        [SerializeField] private bool _unlimitedSprint = false;
        [FoldoutGroup("Sprint"), ShowIf("_enableSprint")]
        [SerializeField] private float _sprintSpeed = 7f;
        [FoldoutGroup("Sprint"), ShowIf("@_enableSprint && !_unlimitedSprint")]
        [SerializeField] private float _sprintDuration = 5f;
        [FoldoutGroup("Sprint"), ShowIf("@_enableSprint && !_unlimitedSprint")]
        [SerializeField] private float _sprintCooldownSeconds = 0.5f;


        [Header("Crouch")]
        [FoldoutGroup("Crouch")]
        [SerializeField] private bool _enableCrouch = true;
        [FoldoutGroup("Crouch"), ShowIf("_enableCrouch")]
        [SerializeField] private bool _holdToCrouch = false;
        [FoldoutGroup("Crouch"), ShowIf("_enableCrouch")]
        [SerializeField] private float _crouchHeight = 0.75f;
        [FoldoutGroup("Crouch"), ShowIf("_enableCrouch")]
        [SerializeField] private float _speedReduction = 0.5f;


        [Header("Sprint State")]
        private bool _isSprinting;
        private float _sprintRemaining;
        private bool _isSprintCooldown;
        private float _sprintCooldownRemaining;
        private bool _hasPlayedExhaustedSfx;
        private AudioSource _exhaustedAudioSource;


        [Header("Crouch State")]
        private bool _isCrouched;
        private Vector3 _originalScale;

        [Header("Footsteps")]
        [FoldoutGroup("Footsteps")]
        [SerializeField] private AudioClip[] _footstepClips;
        [FoldoutGroup("Footsteps")]
        [SerializeField] private float _walkFootstepInterval = 0.5f;
        
        [FoldoutGroup("Footsteps")]
        [SerializeField] private float _minFootstepSpeed = 0.1f;
        private float _footstepTimer;

        [Header("Sprint Exhausted")]
        [SerializeField] private AudioClip _exhaustedSfx;
        [SerializeField, Range(0f, 1f)] private float _exhaustedThreshold = 0.15f;

        #region Unity Lifecycle

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            _player = GetComponent<PlayerController>();
            _rigidbody = GetComponent<Rigidbody>();

            _originalScale = _player.transform.localScale;

            if (!_unlimitedSprint) {
                _sprintRemaining = _sprintDuration;
            }

            ChangeState(new PlayerWalkingState(this));
        }

        private void OnEnable() {
            _pauseService.Register(this);
        }

        private void Start() {
            _audioService = ServiceLocator.Get<IAudioService>();

            _eventBus = ServiceLocator.Get<IEventBus>();
            _eventBus?.Subscribe<MoveInputEvent>(HandleMove);
            _eventBus?.Subscribe<SprintInputEvent>(HandleSprint);
            _eventBus?.Subscribe<CrouchInputEvent>(HandleCrouch);
        }

        private void OnDisable() {
            _pauseService.Unregister(this);
            _eventBus?.Unsubscribe<MoveInputEvent>(HandleMove);
            _eventBus?.Unsubscribe<SprintInputEvent>(HandleSprint);
            _eventBus?.Unsubscribe<CrouchInputEvent>(HandleCrouch);
        }

        private void Update() {
            if (_paused) return;
            HandleSprintStamina();
            HandleSprintExhaustedSfx();
            _currentState?.Tick();
        }

        private void FixedUpdate() {
            if (_paused || !PlayerCanMove || Rigidbody == null) return;

            Vector2 moveInput = MoveInput;
            Vector3 input = new Vector3(moveInput.x, 0f, moveInput.y);
            UpdateFootsteps();

            bool isActuallyMoving = _rigidbody.linearVelocity.sqrMagnitude > 0.1f;

            if (input.sqrMagnitude > 0.0001f) {
                SetIsWalking(true);
            }
            else {
                SetIsWalking(false);
                SetIsSprinting(false);
            }

            float currentSpeed = _currentState?.GetCurrentSpeed() ?? 0f;
            Vector3 targetVelocity = PlayerTransform.TransformDirection(input) * currentSpeed;

            ApplyVelocity(targetVelocity);
        }

        public void OnPause() {
            if (_paused) return;
            _paused = true;
            _moveInput = Vector2.zero;
            _wasKinematic = _rigidbody.isKinematic;
            if (!_wasKinematic) {
                _storedVelocity = _rigidbody.linearVelocity;
                _storedAngularVelocity = _rigidbody.angularVelocity;
                _rigidbody.isKinematic = true;
            }
        }

        public void OnResume() {
            if (!_paused) return;
            _paused = false;
            if (!_wasKinematic) {
                _rigidbody.isKinematic = false;
                _rigidbody.linearVelocity = _storedVelocity;
                _rigidbody.angularVelocity = _storedAngularVelocity;
            }
        }

        public void UpdateFootsteps() {
            if (Rigidbody == null || _footstepClips.Length == 0 || !_player.IsGrounded) return;

            Vector3 horizontalVelocity = new Vector3(Rigidbody.linearVelocity.x, 0f, Rigidbody.linearVelocity.z);
            float speed = horizontalVelocity.magnitude;

            if (speed < _minFootstepSpeed) {
                _footstepTimer = _walkFootstepInterval;
                return;
            }

            float interval = _walkFootstepInterval;
            _footstepTimer += Time.fixedDeltaTime;

            if (_footstepTimer >= interval) {
                _footstepTimer -= interval;
                AudioClip clip = _footstepClips[UnityEngine.Random.Range(0, _footstepClips.Length)];
                _audioService?.PlaySFX(clip, volume: 0.5f);
            }
        }

        #endregion

        #region Input Handlers & State Management
        public void ChangeState(MovementState newState) {
            _currentState?.Exit();
            _currentState = newState;
            _currentState?.Enter();
        }

        private void HandleMove(MoveInputEvent evt) {
            _moveInput = evt.Value;
            _currentState?.HandleMove(evt.Value);
        }

        private void HandleSprint(SprintInputEvent evt) {
            _currentState?.HandleSprint(evt.IsPressed);
            
            _eventBus?.Publish(new PlayerSprintChangedEvent(evt.IsPressed));
        }

        private void HandleCrouch(CrouchInputEvent evt) {
            _currentState?.HandleCrouch(evt.IsPressed);
        }

        #endregion

        #region Crouch Logic

        private void ToggleCrouchState() {
            if (_isCrouched) {
                StopCrouch();
            }
            else {
                StartCrouch();
            }
        }

        public void StartCrouch() {
            if (_isCrouched) return;

            _player.transform.localScale = new Vector3(_originalScale.x, _crouchHeight, _originalScale.z);
            _walkSpeed *= _speedReduction;
            _isCrouched = true;
        }

        public void StopCrouch() {
            if (!_isCrouched) return;

            _player.transform.localScale = _originalScale;
            _walkSpeed /= _speedReduction;
            _isCrouched = false;
        }

        #endregion

        #region Sprint Logic

        private void HandleSprintStamina() {
            if (!_enableSprint || _unlimitedSprint) return;

            if (_isSprinting) {
                _sprintRemaining -= Time.deltaTime;
                if (_sprintRemaining <= 0f) {
                    _sprintRemaining = 0f;
                    _isSprinting = false;
                    _isSprintCooldown = true;
                    _sprintCooldownRemaining = _sprintCooldownSeconds;
                }
            }
            else if (!_isSprintCooldown) {
                _sprintRemaining = Mathf.Clamp(_sprintRemaining + Time.deltaTime, 0f, _sprintDuration);
            }

            if (_isSprintCooldown) {
                _sprintCooldownRemaining -= Time.deltaTime;
                if (_sprintCooldownRemaining <= 0f) {
                    _isSprintCooldown = false;
                }
            }
        }

        private void HandleSprintExhaustedSfx() {
            if (!_enableSprint || _unlimitedSprint || _exhaustedSfx == null || _audioService == null) {
                return;
            }

            if (SprintPercent <= _exhaustedThreshold) {
                if (_hasPlayedExhaustedSfx) {
                    return;
                }

                // Evita que o som ofegante seja sobreposto se o mesmo som já estiver sendo reproduzido
                if (_exhaustedAudioSource != null && _exhaustedAudioSource.isPlaying) {
                    return;
                }

                _exhaustedAudioSource = _audioService.PlaySFX(_exhaustedSfx, volume: 1f);
                _hasPlayedExhaustedSfx = true;
                return;
            }

            _hasPlayedExhaustedSfx = false;
        }

        public bool TryStartSprint() {
            if (!_enableSprint) {
                _isSprinting = false;
                return false;
            }

            if (_isSprintCooldown) {
                _isSprinting = false;
                return false;
            }

            if (!_unlimitedSprint && _sprintRemaining <= 0f) {
                _isSprinting = false;
                _isSprintCooldown = true;
                _sprintCooldownRemaining = _sprintCooldownSeconds;
                return false;
            }

            if (_isCrouched) {
                StopCrouch();
            }

            _isSprinting = true;
            return true;
        }

        public void StopSprint() {
            _isSprinting = false;
        }

        #endregion

        #region Public API / Properties

        public bool PlayerCanMove => _playerCanMove;
        public Vector2 MoveInput => _moveInput;
        public Rigidbody Rigidbody => _rigidbody;
        public Transform PlayerTransform => _player.transform;
        public float WalkSpeed => _walkSpeed;
        public float SprintSpeed => _sprintSpeed;
        public float MaxVelocityChange => _maxVelocityChange;

        public float SprintDuration => _sprintDuration;
        public float SprintCooldownSeconds => _sprintCooldownSeconds;

        public float CrouchHeight => _crouchHeight;

        public bool EnableCrouch => _enableCrouch;
        public bool HoldToCrouch => _holdToCrouch;
        public bool IsCrouched => _isCrouched;

        public void SetIsWalking(bool value) {
            _isWalking = value;
        }

        public void SetIsSprinting(bool value) {
            _isSprinting = value;
        }

        public void ApplyVelocity(Vector3 targetVelocity) {
            if (_rigidbody == null) return;

            Vector3 velocity = _rigidbody.linearVelocity;
            Vector3 velocityChange = targetVelocity - velocity;
            velocityChange.x = Mathf.Clamp(velocityChange.x, -_maxVelocityChange, _maxVelocityChange);
            velocityChange.z = Mathf.Clamp(velocityChange.z, -_maxVelocityChange, _maxVelocityChange);
            velocityChange.y = 0f;

            _rigidbody.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        public bool IsSprinting => _isSprinting;
        public bool IsSprintOnCooldown => _isSprintCooldown;
        public bool UsesSprintStamina => _enableSprint && !_unlimitedSprint;
        public bool IsWalking => _isWalking;
        public float SpeedReduction => _speedReduction;
        public float SprintPercent => _unlimitedSprint || _sprintDuration <= 0f
            ? 1f
            : Mathf.Clamp01(_sprintRemaining / _sprintDuration);

        #endregion
    }
}
