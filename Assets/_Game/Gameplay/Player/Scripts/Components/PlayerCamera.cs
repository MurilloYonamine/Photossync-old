// Autor: Murillo Gomes Yonamine
// Data: 28/04/2026

using UnityEngine;
using FifthSemester.Core.Services;
using FifthSemester.Core.Input;
using FifthSemester.Core.Events;
using FifthSemester.Core.States;
using Sirenix.OdinInspector;
using Unity.Cinemachine;

namespace FifthSemester.Player.Components {
    public class PlayerCamera : MonoBehaviour, IPauseable {
        private const float GAMEPAD_SENSITIVITY_MULTIPLIER = 2.5f;

        [Header("Cinemachine References")]
        [SerializeField] private CinemachineCamera _vCam;
        [SerializeField, Required] private Transform _cameraTarget;

        [Header("Settings")]
        [SerializeField] private bool _cameraCanMove = true;
        [SerializeField, Range(0f, 1f)] private float _mouseSensitivity = 0.5f;

        [Header("Zoom")]
        [SerializeField] private bool _enableZoom = true;
        [FoldoutGroup("Zoom"), ShowIf("_enableZoom")]
        [SerializeField] private bool _holdToZoom = false;
        [FoldoutGroup("Zoom"), ShowIf("_enableZoom")]
        [SerializeField] private float _zoomFov = 30f;
        [FoldoutGroup("Zoom"), ShowIf("_enableZoom")]
        [SerializeField] private float _zoomStepTime = 5f;

        [Header("Head Bob")]
        [SerializeField] private bool _enableHeadBob = true;
        [FoldoutGroup("Head Bob"), ShowIf("_enableHeadBob")]
        [SerializeField] private float _bobSpeed = 10f;
        [FoldoutGroup("Head Bob"), ShowIf("_enableHeadBob")]
        [SerializeField] private Vector3 _bobAmount = new Vector3(0.15f, 0.05f, 0f);

        private float _defaultFov;
        private bool _isZoomed;
        private bool _zoomPressed;
        private bool _zoomPrevPressed;

        private Vector2 _lookInput;
        private PlayerMovement _movement;
        private PlayerController _player;
        private IEventBus _eventBus;
        private IGameplayService _gameplayService;
        private IInputService _inputService;
        private IGameStateService _gameStateService;
        private IPauseService _pauseService;
        private bool _paused;

        private CinemachinePanTilt _panTilt;

        private float _bobTimer;
        private Vector3 _targetOriginalPos;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            _player = GetComponent<PlayerController>();
            _movement = GetComponent<PlayerMovement>();

            if (_vCam != null) {
                _defaultFov = _vCam.Lens.FieldOfView;
                _panTilt = _vCam.GetComponent<CinemachinePanTilt>();
            }

            if (_cameraTarget != null) {
                _targetOriginalPos = _cameraTarget.localPosition;
            }
        }

        private void OnEnable() {
            _pauseService.Register(this);
        }

        private void Start() {
            _gameplayService = ServiceLocator.Get<IGameplayService>();
            _inputService = ServiceLocator.Get<IInputService>();
            _gameStateService = ServiceLocator.Get<IGameStateService>();

            _eventBus = ServiceLocator.Get<IEventBus>();
            _eventBus?.Subscribe<LookInputEvent>(HandleLookInput);
            _eventBus?.Subscribe<ZoomInputEvent>(HandleZoomInput);
            _eventBus?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);

            ApplyGameState(_gameStateService != null ? _gameStateService.CurrentState : GameState.Gameplay);

            Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnDisable() {
            _pauseService.Unregister(this);
            _eventBus?.Unsubscribe<LookInputEvent>(HandleLookInput);
            _eventBus?.Unsubscribe<ZoomInputEvent>(HandleZoomInput);
            _eventBus?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
        }

        public Transform GetCameraTarget() => _cameraTarget;

        public void SetRotation(Quaternion rotation) {
            if (_panTilt == null) return;

            Vector3 euler = rotation.eulerAngles;
            float yaw = euler.y;
            float pitch = euler.x;
            if (pitch > 180f) pitch -= 360f;

            _panTilt.PanAxis.Value = yaw;
            _panTilt.TiltAxis.Value = pitch;
        }

        private void Update() {
            if (_paused || !_cameraCanMove || _vCam == null || _player == null) return;

            ApplyRotation();

            float cameraYaw = _vCam.State.RawOrientation.eulerAngles.y;
            _player.transform.rotation = Quaternion.Euler(0, cameraYaw, 0);

            HandleZoom();

            HandleHeadBob();
        }

        public void OnPause() { _paused = true; }
        public void OnResume() { _paused = false; }

        private void HandleLookInput(LookInputEvent evt) {
            _lookInput = evt.Value;
        }

        private void HandleZoomInput(ZoomInputEvent evt) {
            _zoomPressed = evt.IsPressed;
        }

        private void OnGameStateChanged(GameStateChangedEvent evt) {
            ApplyGameState(evt.CurrentState);
        }

        private void ApplyGameState(GameState currentState) {
            if (_vCam == null) return;

            bool isGameplay = currentState == GameState.Gameplay;
            _vCam.Priority = isGameplay ? 10 : 1;

            if (isGameplay && _cameraTarget != null) {
                _cameraTarget.localPosition = _targetOriginalPos;
            }
        }

        private void ApplyRotation() {
            if (_panTilt == null) return;

            if (_gameplayService == null) {
                _gameplayService = ServiceLocator.Get<IGameplayService>();
            }

            if (_inputService == null) {
                _inputService = ServiceLocator.Get<IInputService>();
            }

            float sensitivity = _mouseSensitivity;
            if (_gameplayService != null) {
                sensitivity *= _gameplayService.Sensibility;
            }

            if (_inputService != null && _inputService.LastLookWasGamepad) {
                sensitivity *= GAMEPAD_SENSITIVITY_MULTIPLIER;
            }

            float invertY = _gameplayService != null && _gameplayService.InvertYAxis ? 1f : -1f;
            _panTilt.PanAxis.Value += _lookInput.x * sensitivity;
            _panTilt.TiltAxis.Value += _lookInput.y * sensitivity * invertY;
        }

        private void HandleZoom() {
            if (!_enableZoom) return;

            bool isSprinting = _movement != null && _movement.IsSprinting;

            if (isSprinting) _isZoomed = false;
            else if (_holdToZoom) _isZoomed = _zoomPressed;
            else if (_zoomPressed && !_zoomPrevPressed) _isZoomed = !_isZoomed;

            _zoomPrevPressed = _zoomPressed;

            float targetFov = _isZoomed ? _zoomFov : _defaultFov;

            var lens = _vCam.Lens;
            lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFov, _zoomStepTime * Time.deltaTime);
            _vCam.Lens = lens;
        }

        private void HandleHeadBob() {
            if (!_enableHeadBob || _cameraTarget == null || _movement == null) return;

            if (_movement.IsWalking) {
                if (_movement.IsSprinting) {
                    _bobTimer += Time.deltaTime * (_bobSpeed + _movement.SprintSpeed);
                }
                else if (_movement.IsCrouched) {
                    _bobTimer += Time.deltaTime * (_bobSpeed * _movement.SpeedReduction);
                }
                else {
                    _bobTimer += Time.deltaTime * _bobSpeed;
                }

                Vector3 offset = new Vector3(
                    Mathf.Sin(_bobTimer) * _bobAmount.x,
                    Mathf.Sin(_bobTimer) * _bobAmount.y,
                    Mathf.Sin(_bobTimer) * _bobAmount.z
                );

                _cameraTarget.localPosition = _targetOriginalPos + offset;
            }
            else {
                _bobTimer = 0f;
                _cameraTarget.localPosition = new Vector3(
                    Mathf.Lerp(_cameraTarget.localPosition.x, _targetOriginalPos.x, Time.deltaTime * _bobSpeed),
                    Mathf.Lerp(_cameraTarget.localPosition.y, _targetOriginalPos.y, Time.deltaTime * _bobSpeed),
                    Mathf.Lerp(_cameraTarget.localPosition.z, _targetOriginalPos.z, Time.deltaTime * _bobSpeed)
                );
            }
        }
    }
}
