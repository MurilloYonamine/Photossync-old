using System;
using FifthSemester.Core.Enums;
using FifthSemester.Core.Services;
using FifthSemester.Gameplay.Shared;
using Sirenix.OdinInspector;
using TMPro;
using ThirdParty.QuickOutline;
using UnityEngine;

namespace FifthSemester.Doors {
    [RequireComponent(typeof(Outline))]
    public class Door : MonoBehaviour, IInteractable, IPauseable {
        [Header("Configurações Visuais")]
        [SerializeField] private Outline _outline;
        [SerializeField] private TextMeshPro _textLocal;

        [Header("Configurações de Movimento")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private float _openAngle = 90f;
        [SerializeField] private float _speed = 5f;
        [SerializeField] private bool _useDoubleDoor;

        [Header("Audio")]
        [SerializeField] private AudioClip[] _doorSfx;

        [ShowIf(nameof(_useDoubleDoor))]
        [SerializeField] private Transform[] _doorMeshes;

        [HideIf(nameof(_useDoubleDoor))]
        [SerializeField] private Transform _doorMesh;

        [Header("Map Registry")]
        [SerializeField] private DoorType _doorType = DoorType.None;

        private IMapService _mapService;
        private Quaternion[] _closedRotations;
        private Quaternion[] _targetRotations;
        private Transform[] _activeDoorMeshes;
        private bool _isLocked = false;
        private Color _unlockedColor;
        private IAudioService _audioService;
        private IPauseService _pauseService;
        private bool _paused;

        public bool IsInteractable { get; private set; } = true;

        public string Id => gameObject.name;
        private string _defaultText;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            _outline = GetComponent<Outline>();
            _outline.enabled = false;

            if (_textLocal != null) {
                _textLocal.gameObject.SetActive(false);
                _defaultText = _textLocal.text;
            }

            if(_doorMesh == null)
                _doorMesh = gameObject.transform;

            CacheDoorMeshes();
            InitializeRotations();

            _unlockedColor = new Color32(105, 255, 144, 255); // 69FF90
        }

        private void OnEnable() { _pauseService.Register(this); }
        private void OnDisable() { _pauseService.Unregister(this); }
        public void OnPause() { _paused = true; }
        public void OnResume() { _paused = false; }

        private void Start() {
            ServiceLocator.TryGet<IAudioService>(out _audioService);

            if (_doorType == DoorType.None) return;

            _mapService = ServiceLocator.Get<IMapService>();
            _mapService?.Register(_doorType, gameObject);
        }

        private void OnDestroy() {
            if (_doorType == DoorType.None || _mapService == null) return;
            _mapService.Unregister(_doorType);
        }

        private void Update() {
            if (_paused || _activeDoorMeshes == null || _targetRotations == null) return;

            for (int i = 0; i < _activeDoorMeshes.Length; i++) {
                Transform doorMesh = _activeDoorMeshes[i];
                if (doorMesh == null) continue;

                doorMesh.localRotation = Quaternion.Lerp(doorMesh.localRotation, _targetRotations[i], Time.deltaTime * _speed);
            }
        }

        public void Interact() {
            if (_paused || _isLocked) return;

            _isOpen = !_isOpen;
            PlayDoorSound();
            UpdateTargetRotations();
        }

        public void StopInteract() { }

        public void Highlight(bool value) {
            if (_outline != null)
                _outline.enabled = value;

            if (_textLocal != null)
                _textLocal.gameObject.SetActive(value);
        }

        public void Lock() {
            _isLocked = true;

            if (_isOpen) {
                _isOpen = false;
                UpdateTargetRotations();
            }

            if (_outline != null)
                _outline.OutlineColor = Color.red;

            if (_textLocal != null) {
                _textLocal.color = Color.red;
            }
        }

        public void Unlock() {
            _isLocked = false;

            if (_outline != null)
                _outline.OutlineColor = _unlockedColor;

            if (_textLocal != null) {
                _textLocal.color = _unlockedColor;
                _textLocal.text = _defaultText;
            }
        }

        private void CacheDoorMeshes() {
            if (_useDoubleDoor) {
                _activeDoorMeshes = _doorMeshes ?? Array.Empty<Transform>();
                return;
            }

            _activeDoorMeshes = _doorMesh != null ? new[] { _doorMesh } : Array.Empty<Transform>();
        }

        private void InitializeRotations() {
            if (_activeDoorMeshes == null) {
                _closedRotations = Array.Empty<Quaternion>();
                _targetRotations = Array.Empty<Quaternion>();
                return;
            }

            _closedRotations = new Quaternion[_activeDoorMeshes.Length];
            _targetRotations = new Quaternion[_activeDoorMeshes.Length];

            for (int i = 0; i < _activeDoorMeshes.Length; i++) {
                Transform doorMesh = _activeDoorMeshes[i];
                if (doorMesh == null) continue;

                _closedRotations[i] = doorMesh.localRotation;
                _targetRotations[i] = _closedRotations[i];
            }
        }

        private void UpdateTargetRotations() {
            if (_activeDoorMeshes == null || _targetRotations == null) return;

            for (int i = 0; i < _activeDoorMeshes.Length; i++) {
                if (_activeDoorMeshes[i] == null) continue;

                if (_isOpen) {
                    float direction = _useDoubleDoor && i % 2 == 1 ? -_openAngle : _openAngle;
                    _targetRotations[i] = _closedRotations[i] * Quaternion.Euler(0f, direction, 0f);
                }
                else {
                    _targetRotations[i] = _closedRotations[i];
                }
            }
        }

        private void PlayDoorSound() {
            if (_audioService == null || _doorSfx == null || _doorSfx.Length == 0) {
                return;
            }

            int randomIndex = UnityEngine.Random.Range(0, _doorSfx.Length);
            AudioClip clip = _doorSfx[randomIndex];
            if (clip == null) {
                return;
            }

            _audioService.PlaySFX(clip);
        }
    }
}
