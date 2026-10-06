using System.Collections;
using System.Collections.Generic;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using FifthSemester.Gameplay.Inventory;
using FifthSemester.Gameplay.Shared;
using Sirenix.OdinInspector;
using TMPro;
using ThirdParty.QuickOutline;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Video;
using System;

namespace FifthSemester.Gameplay.Map2 {
    [RequireComponent(typeof(Outline))]
    public class Map2KeyDoor : MonoBehaviour, IInteractable {
        [Header("Configurações Visuais")]
        private Outline _outline;
        private TextMeshPro _textLocal;
        private Collider _collider;

        [Header("Configuração da Chave")]
        [SerializeField] private Map2KeyDefinitionSO _requiredKey;
        [SerializeField] private bool _requiresKey = true;
        [SerializeField] private bool _canBeOpenedByNurse = false;

        [Header("Final Ruim")]
        [SerializeField] private bool _isBadEndingDoor = false;
        [SerializeField] private Map2PasswordDeliveryPoint _passwordDeliveryPoint;
        [SerializeField] private GameObject _badEndingPrefab;
        [SerializeField] private float _fadeDuration = 2f;

        [Header("Configurações de Movimento")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private float _openAngle = 90f;
        [SerializeField] private float _slideDistance = 1.5f;
        [SerializeField] private float _speed = 5f;
        [SerializeField] private bool _useDoubleDoor;
        [SerializeField] private Vector3 _slideAxis = new Vector3(1f, 0f, 0f);

        [Header("Audio")]
        [SerializeField] private AudioClip[] _doorSfx;
        [SerializeField] private AudioClip[] _lockedSounds;
        [SerializeField] private NavMeshObstacle _navMeshObstacle;

        [ShowIf(nameof(_useDoubleDoor))]
        [SerializeField] private Transform[] _doorMeshes;

        [HideIf(nameof(_useDoubleDoor))]
        [SerializeField] private Transform _doorMesh;

        private IInventoryService<Item> _inventoryService;
        private IMap2KeyService _map2KeyService;
        private Quaternion[] _closedRotations;
        private Quaternion[] _targetRotations;
        private Vector3[] _closedPositions;
        private Vector3[] _targetPositions;
        private Transform[] _activeDoorMeshes;
        private IAudioService _audioService;
        private Color _unlockedColor;
        private string _defaultText;
        private bool _endingStarted;

        public bool IsInteractable => !_endingStarted;
        public bool CanBeOpenedByNurse => _canBeOpenedByNurse;

        public string Id => gameObject.name;

        private void Awake() {
            _outline = GetComponent<Outline>();
            _outline.enabled = false;
            _collider = GetComponent<Collider>();

            _textLocal = GetComponentInChildren<TextMeshPro>();
            if (_textLocal != null) {
                _defaultText = _textLocal.text;
                _textLocal.gameObject.SetActive(false);
            }

            if(_doorMesh == null)
                _doorMesh = gameObject.transform;

            CacheDoorMeshes();
            InitializeRotations();
            _unlockedColor = new Color32(105, 255, 144, 255);
        }

        private void Start() {
            ServiceLocator.TryGet<IInventoryService<Item>>(out _inventoryService);
            ServiceLocator.TryGet<IAudioService>(out _audioService);
            ServiceLocator.TryGet<IMap2KeyService>(out _map2KeyService);

            UpdateDoorVisuals();
        }

        private void Update() {
            if (_activeDoorMeshes == null || _targetRotations == null || _targetPositions == null) {
                return;
            }

            for (int i = 0; i < _activeDoorMeshes.Length; i++) {
                Transform doorMesh = _activeDoorMeshes[i];
                if (doorMesh == null) {
                    continue;
                }

                doorMesh.localRotation = Quaternion.Lerp(doorMesh.localRotation, _targetRotations[i], Time.deltaTime * _speed);
                doorMesh.localPosition = Vector3.Lerp(doorMesh.localPosition, _targetPositions[i], Time.deltaTime * _speed);
            }
        }

        public void Interact() {
            if (_endingStarted) return;
            if (_isBadEndingDoor && !HasDeliveryCutscenePlayed() && !Map2CheatController.IsCheatActive) {
                if (_map2KeyService != null && _map2KeyService.HasCollectedAllKeys) {
                    TriggerBadEnding();
                    return;
                }
            }

            if (_requiresKey && !IsDoorUnlocked()) {
                PlayRandomLockedSound();
                return;
            }

            _isOpen = !_isOpen;
            PlayDoorSound();
            UpdateTargetRotations();
        }

        public void StopInteract() {
        }

        public void TryOpenByAI() {
            Debug.Log($"[Map2KeyDoor] TryOpenByAI called on '{gameObject.name}'. _canBeOpenedByNurse={_canBeOpenedByNurse}, _isOpen={_isOpen}");
            
            if (!_canBeOpenedByNurse) {
                Debug.LogWarning($"[Map2KeyDoor] TryOpenByAI aborted: '_canBeOpenedByNurse' is false on door '{gameObject.name}'!");
                return;
            }

            if (!_isOpen) {
                _isOpen = true;
                PlayDoorSound();
                UpdateTargetRotations();
                Debug.Log($"[Map2KeyDoor] TryOpenByAI: Door '{gameObject.name}' successfully opened by Nurse!");
            }

            if (_navMeshObstacle != null) {
                _navMeshObstacle.carving = false;
                Debug.Log($"[Map2KeyDoor] TryOpenByAI: Disabled carving on '{gameObject.name}' NavMeshObstacle.");
            }
        }

        public void TryCloseByAI() {
            if (!_canBeOpenedByNurse) {
                return;
            }

            if (_isOpen) {
                _isOpen = false;
                PlayDoorSound();
                UpdateTargetRotations();
            }

            if (_navMeshObstacle != null) {
                _navMeshObstacle.carving = true;
            }
        }

        public void Highlight(bool value) {
            if (_outline != null) {
                _outline.enabled = value;
                _outline.OutlineColor = IsDoorUnlocked() ? _unlockedColor : Color.red;
            }

            if (_textLocal != null) {
                _textLocal.gameObject.SetActive(value);
                _textLocal.color = IsDoorUnlocked() ? _unlockedColor : Color.red;
                _textLocal.text = _defaultText;
            }
        }

        private bool HasRequiredKey() {
            if (_requiredKey == null || _inventoryService == null) {
                return false;
            }

            IReadOnlyList<Item> items = _inventoryService.GetItems();
            if (items == null) {
                return false;
            }

            for (int i = 0; i < items.Count; i++) {
                if (items[i] is Map2KeyItem keyItem && keyItem.KeyDefinition == _requiredKey) {
                    return true;
                }
            }

            return false;
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
                _closedPositions = Array.Empty<Vector3>();
                _targetPositions = Array.Empty<Vector3>();
                return;
            }

            _closedRotations = new Quaternion[_activeDoorMeshes.Length];
            _targetRotations = new Quaternion[_activeDoorMeshes.Length];
            _closedPositions = new Vector3[_activeDoorMeshes.Length];
            _targetPositions = new Vector3[_activeDoorMeshes.Length];

            for (int i = 0; i < _activeDoorMeshes.Length; i++) {
                Transform doorMesh = _activeDoorMeshes[i];
                if (doorMesh == null) {
                    continue;
                }

                _closedRotations[i] = doorMesh.localRotation;
                _targetRotations[i] = _closedRotations[i];
                _closedPositions[i] = doorMesh.localPosition;
                _targetPositions[i] = _closedPositions[i];
            }
        }

        private void UpdateTargetRotations() {
            if (_activeDoorMeshes == null || _targetRotations == null || _targetPositions == null) {
                return;
            }

            if (_navMeshObstacle != null) {
                _navMeshObstacle.carving = !_isOpen;
            }

            if (_collider != null) {
                _collider.isTrigger = _isOpen;
            }

            for (int i = 0; i < _activeDoorMeshes.Length; i++) {
                if (_activeDoorMeshes[i] == null) {
                    continue;
                }

                if (_isOpen) {
                    if (_useDoubleDoor) {
                        float slideDirection = i % 2 == 0 ? -_slideDistance : _slideDistance;
                        _targetPositions[i] = _closedPositions[i] + _slideAxis * slideDirection;
                        _targetRotations[i] = _closedRotations[i];
                    }
                    else {
                        float direction = _openAngle;
                        _targetRotations[i] = _closedRotations[i] * Quaternion.Euler(0f, direction, 0f);
                        _targetPositions[i] = _closedPositions[i];
                    }
                }
                else {
                    _targetRotations[i] = _closedRotations[i];
                    _targetPositions[i] = _closedPositions[i];
                }
            }
        }

        private void UpdateDoorVisuals() {
            if (_outline != null) {
                _outline.OutlineColor = IsDoorUnlocked() ? _unlockedColor : Color.red;
            }

            if (_textLocal != null) {
                _textLocal.color = IsDoorUnlocked() ? _unlockedColor : Color.red;
            }
        }

        private bool IsDoorUnlocked() {
            if (_isBadEndingDoor && !HasDeliveryCutscenePlayed() && !Map2CheatController.IsCheatActive) {
                return false;
            }

            if (!_requiresKey) {
                return true;
            }

            if (_map2KeyService != null && _map2KeyService.HasCollectedAllKeys) {
                return true;
            }

            return HasRequiredKey();
        }

        private bool HasDeliveryCutscenePlayed() {
            return _passwordDeliveryPoint != null && _passwordDeliveryPoint.HasPlayedDeliveryCutscene;
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

        private void PlayDoorSound(AudioClip clip) {
            if (clip == null || _audioService == null) {
                return;
            }

            _audioService.PlaySFX(clip);
        }

        private void PlayRandomLockedSound() {
            if (_lockedSounds == null || _lockedSounds.Length == 0 || _audioService == null) return;

            int idx = UnityEngine.Random.Range(0, _lockedSounds.Length);
            AudioClip clip = _lockedSounds[idx];
            if (clip != null) _audioService.PlaySFX(clip);
        }

        private void TriggerBadEnding() {
            if (_endingStarted) return;
            if (_badEndingPrefab == null) {
                Debug.LogError("[Map2KeyDoor] Prefab de Final Ruim não configurado!");
                return;
            }

            _endingStarted = true;

            if (ServiceLocator.TryGet<IGameStateService>(out var gameStateService)) {
                gameStateService.ChangeState(GameState.Cutscene);
            }

            GameObject instance = Instantiate(_badEndingPrefab);
            VideoPlayer videoPlayer = instance.GetComponentInChildren<VideoPlayer>();

            if (videoPlayer != null) {
                StartCoroutine(PlayBadEndingVideo(videoPlayer, instance));
            }
            else {
                Debug.LogError("[Map2KeyDoor] Prefab de Final Ruim não contém um VideoPlayer!");
                LoadMainMenu();
            }
        }

        private IEnumerator PlayBadEndingVideo(VideoPlayer videoPlayer, GameObject instance) {
            yield return Map2EndingVideoPlayback.Play(videoPlayer, _fadeDuration);
            Destroy(instance);
            LoadMainMenu();
        }

        private void LoadMainMenu() {
            if (ServiceLocator.TryGet<IFadeService>(out var fadeService)) {
                fadeService.FadeIn(0f);
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}
