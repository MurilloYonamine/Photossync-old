// Autor: Murillo Gomes Yonamine
// Data: 28/04/2026

using UnityEngine;
using System.Collections;
using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Core.Enums;
using FifthSemester.Gameplay.Enemy;
using FifthSemester.Gameplay.Dialogue;
using FifthSemester.Features.Localization;

namespace FifthSemester.Player.Components {
    public class PlayerFlashlight : MonoBehaviour, IPauseable {
        [Header("References")]
        [SerializeField] private Light _spotLight;
        [SerializeField, Tooltip("Transform used as origin for the flashlight (usually the player camera)")] private Transform _lightOrigin;

        [Header("Aim")]
        [SerializeField, Tooltip("Fallback distance when raycast hits nothing")] private float _aimDistance = 30f;
        [SerializeField, Tooltip("Smoothing speed for flashlight rotation")] private float _aimSmooth = 10f;
        [SerializeField] private LayerMask _aimCollisionMask = ~0;
        [SerializeField, Tooltip("Radius for sphere cast (thickness of the flashlight beam)")]
        private float _aimRadius = 0.4f;

        [SerializeField, Tooltip("Seconds of continuous illumination required to consider the target illuminated")]
        private float _illuminationDelay = 0.12f;

        private float _illuminationTimer = 0f;

        [Header("Audio")] 
        [SerializeField] private AudioClip _toggleSound;

        [SerializeField] private bool _isOn = false;
        [SerializeField] private bool _hasFlashlight = false;

        [Header("No Flashlight Warning")]
        [SerializeField] private LocalizedTextAsset _noFlashlightDialogue;
        [SerializeField] private CaptionView _captionView;
        [SerializeField] private int _pressThresholdForWarning = 3;
        [SerializeField] private float _warningTimeWindow = 2f;
        [SerializeField] private float _warningDisplayDuration = 3f;

        private int _noFlashlightPressCount = 0;
        private float _lastNoFlashlightPressTime = 0f;
        private Coroutine _hideCaptionCoroutine;

        private IEventBus _eventBus;
        private IAudioService _audioService;
        private Camera _camera;
        private Vector2 _lookInput;
        private GameObject _currentIlluminatedTarget;
        private IPauseService _pauseService;
        private bool _paused;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            if (_spotLight == null) _spotLight = GetComponentInChildren<Light>();

            if (_lightOrigin == null && Camera.main != null) {
                _lightOrigin = Camera.main.transform;
            }

            if (_spotLight != null && _lightOrigin != null) {
                _spotLight.transform.SetParent(_lightOrigin, false);
                _spotLight.enabled = false;
            }
        }

        private void OnEnable() {
            _pauseService.Register(this);
        }

        private void OnDisable() {
            _pauseService.Unregister(this);
        }

        private void Start() {
            _eventBus = ServiceLocator.Get<IEventBus>();
            _audioService = ServiceLocator.Get<IAudioService>();

            _eventBus.Subscribe<FlashlightInputEvent>(HandleFlashlightInput);
            _eventBus.Subscribe<LookInputEvent>(HandleLookInput);

            _camera = Camera.main;

            if (_captionView == null) {
                _captionView = FindAnyObjectByType<CaptionView>();
            }
        }

        private void OnDestroy() {
            _eventBus?.Unsubscribe<FlashlightInputEvent>(HandleFlashlightInput);
            _eventBus?.Unsubscribe<LookInputEvent>(HandleLookInput);

            if (_hideCaptionCoroutine != null) {
                StopCoroutine(_hideCaptionCoroutine);
            }
        }

        private void HandleLookInput(LookInputEvent evt) {
            _lookInput = evt.Value;
        }

        private void HandleFlashlightInput(FlashlightInputEvent evt) {
            if (_paused) return;
            if (evt.IsPressed && !_hasFlashlight) {
                if (Time.time - _lastNoFlashlightPressTime > _warningTimeWindow) {
                    _noFlashlightPressCount = 0;
                }
                _lastNoFlashlightPressTime = Time.time;
                _noFlashlightPressCount++;

                if (_noFlashlightPressCount >= _pressThresholdForWarning) {
                    TriggerNoFlashlightDialogue();
                    _noFlashlightPressCount = 0;
                }
                return;
            }

            if (!evt.IsPressed || !_hasFlashlight) return;
            
            _isOn = !_isOn;
            UpdateLightEnabled();
            PlayToggleSound();

            if (!_isOn) {
                ClearIlluminatedTarget();
                _illuminationTimer = 0f;
            }
        }

        private void UpdateLightEnabled() {
            if (_spotLight == null) return;
            _spotLight.enabled = _isOn;
            if (_spotLight.enabled && _lightOrigin != null) {
                _spotLight.transform.position = _lightOrigin.position;
            }
        }

        private void LateUpdate() {
            if (_isOn && !_paused) AimLightTowardsPointer();
        }

        public void OnPause() {
            _paused = true;
            if (_eventBus != null) ClearIlluminatedTarget();
        }

        public void OnResume() { _paused = false; }

        private void AimLightTowardsPointer() {
            if (_spotLight == null || _lightOrigin == null || _camera == null) return;

            Ray ray = _camera.ScreenPointToRay(GetAimScreenPoint());
            Vector3 targetPoint = GetTargetPoint(ray);
            Vector3 origin = _lightOrigin.position;
            Vector3 desiredDir = (targetPoint - origin).normalized;

            UpdateLightTransform(origin, desiredDir);

            GameObject detectedSeeker = DetectLightSeeker(origin, desiredDir);

            UpdateIlluminationState(detectedSeeker);
        }

        private Vector3 GetAimScreenPoint() {
            if (Cursor.lockState == CursorLockMode.Locked) {
                return new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            }

            return Input.mousePosition;
        }

        private Vector3 GetTargetPoint(Ray ray) {
            if (Physics.Raycast(ray, out RaycastHit hit, _aimDistance, _aimCollisionMask)) {
                return hit.point;
            }

            return ray.GetPoint(_aimDistance);
        }

        private void UpdateLightTransform(Vector3 origin, Vector3 desiredDir) {
            _spotLight.transform.position = origin;
            Quaternion desiredRot = Quaternion.LookRotation(desiredDir, _lightOrigin.up);
            _spotLight.transform.rotation = Quaternion.Slerp(_spotLight.transform.rotation, desiredRot, Time.deltaTime * _aimSmooth);
        }

        private GameObject DetectLightSeeker(Vector3 origin, Vector3 desiredDir) {
            if (!Physics.SphereCast(origin, _aimRadius, desiredDir, out RaycastHit seekerHit, _aimDistance, _aimCollisionMask)) {
                return null;
            }

            if (seekerHit.collider == null) {
                return null;
            }

            LightSeeker seeker = seekerHit.collider.GetComponentInParent<LightSeeker>();
            if (seeker == null) {
                return null;
            }

            Debug.Log($"[PlayerFlashlight] spherecast hit LightSeeker: {seeker.gameObject.name}");
            return seeker.gameObject;
        }

        private void UpdateIlluminationState(GameObject detectedSeeker) {
            if (detectedSeeker != null) {
                HandleDetectedSeeker(detectedSeeker);
                return;
            }

            ClearIlluminatedTarget();
            _illuminationTimer = 0f;
        }

        private void HandleDetectedSeeker(GameObject detectedSeeker) {
            if (_currentIlluminatedTarget == detectedSeeker) {
                _illuminationTimer = _illuminationDelay;
                return;
            }

            _illuminationTimer += Time.deltaTime;
            if (_illuminationTimer < _illuminationDelay) {
                return;
            }

            ClearIlluminatedTarget();
            Debug.Log($"[PlayerFlashlight] Começou a iluminar: {detectedSeeker.name}");
            _eventBus.Publish(new FlashlightTargetedEvent(detectedSeeker, true));
            _currentIlluminatedTarget = detectedSeeker;
            _illuminationTimer = _illuminationDelay;
        }

        private void ClearIlluminatedTarget() {
            if (_currentIlluminatedTarget == null) {
                return;
            }

            Debug.Log($"[PlayerFlashlight] Parou de iluminar: {_currentIlluminatedTarget.name}");
            _eventBus.Publish(new FlashlightTargetedEvent(_currentIlluminatedTarget, false));
            _currentIlluminatedTarget = null;
        }

        private void PlayToggleSound() {
            if (_toggleSound == null || _audioService == null) return;
            _audioService.PlaySFX(_toggleSound);
        }

        private void TriggerNoFlashlightDialogue() {
            if (_captionView == null) {
                _captionView = FindAnyObjectByType<CaptionView>();
            }

            if (_captionView != null) {
                Language currentLanguage = Language.Portuguese;
                var settingsService = ServiceLocator.Get<ISettingsService>();
                if (settingsService != null) {
                    currentLanguage = settingsService.Language;
                }

                TextAsset asset = _noFlashlightDialogue.GetAsset(currentLanguage);
                if (asset != null) {
                    string parsedText = CaptionParser.Parse(asset);
                    _captionView.Show();
                    _captionView.SetCaption(parsedText, () => {
                        if (_hideCaptionCoroutine != null) {
                            StopCoroutine(_hideCaptionCoroutine);
                        }
                        _hideCaptionCoroutine = StartCoroutine(HideCaptionAfterDelay(_warningDisplayDuration));
                    });
                }
            }
        }

        private IEnumerator HideCaptionAfterDelay(float delay) {
            yield return new WaitForSeconds(delay);
            if (_captionView != null) {
                _captionView.Hide();
            }
            _hideCaptionCoroutine = null;
        }

        public void EnableFlashlight() {
            _hasFlashlight = true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            if (!_isOn || _lightOrigin == null) return;
            Gizmos.color = Color.cyan;
            Vector3 origin = _lightOrigin.position;
            Vector3 dir = _spotLight != null ? _spotLight.transform.forward : _lightOrigin.forward;
            Gizmos.DrawRay(origin, dir * _aimDistance);
        }
#endif
    }
}
