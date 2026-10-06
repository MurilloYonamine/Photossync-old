// Autor: Murillo Gomes Yonamine
// Data: 05/05/2026

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using FifthSemester.Core.Enums;
using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Gameplay.Inventory;
using FifthSemester.Gameplay.Map2;
using FifthSemester.Doors;
using FifthSemester.Gameplay.Map;
using FifthSemester.Player;
using FifthSemester.Player.Components;

namespace FifthSemester.Gameplay.Missions {
    public class MissionService : MonoBehaviour, IMissionService {
        private const float START_FADE_DURATION = 1f;
        private const string AUTOSAVE_SLOT = "default";
        [SerializeField] private MissionSequenceSO _defaultSequence;
        [SerializeField] private AudioClip _missionCompleteSFX;

        private IEventBus _eventBus;
        private IFadeService _fadeService;
        private ISaveService _saveService;
        private IAudioService _audioService;
        private IMapService _mapService;
        private IInventoryService<Item> _inventoryService;
        private IMission _currentMission;
        private MissionSequenceSO _activeSequence;
        private int _sequenceIndex = -1;
        private MissionDefinition _currentDefinition;
        private PlayerController _playerController;
        private Coroutine _autosaveRoutine;
        private bool _isAutosaving;
        private bool _autosaveRequestedAgain;
        public int CurrentIndex { get; private set; } = -1;
        private void Awake() {
            ServiceLocator.Register<IMissionService>(this);
            
            _eventBus = ServiceLocator.Get<IEventBus>();
            _saveService = ServiceLocator.Get<ISaveService>();
            ServiceLocator.TryGet<IAudioService>(out _audioService);
            ServiceLocator.TryGet<IMapService>(out _mapService);
            ServiceLocator.TryGet<IInventoryService<Item>>(out _inventoryService);
        }

        private void Start() {
            _playerController = UnityEngine.Object.FindFirstObjectByType<PlayerController>();

            _eventBus?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);

            SaveData saveData = _saveService?.LoadFromSlot(AUTOSAVE_SLOT);
            int startIndex = saveData?.CurrentMissionIndex ?? 0;
            if (_defaultSequence != null && startIndex >= 0) {
                StartSequence(_defaultSequence);
                if (startIndex > 0) {
                    SkipToMission(startIndex);
                }
            }
        }

        private void OnDestroy() {
            _eventBus?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
            CleanupCurrentMission();
        }

        private void OnItemPickedUp(ItemPickedUpEvent evt) {
            if (_isAutosaving || evt.ItemGameObject == null) {
                return;
            }

            if (evt.ItemGameObject.GetComponent<Map2KeyItem>() == null) {
                return;
            }

            RequestAutosave();
        }

        public void StartMission(MissionDefinition mission) {
            if (mission == null) {
                Debug.LogError("[MissionService] Tentativa de iniciar uma missão nula.");
                return;
            }

            if (_activeSequence != null && _activeSequence.Sequence != null) {
                int idx = _activeSequence.Sequence.IndexOf(mission);
                if (idx == -1) {
                    Debug.LogWarning("[MissionService] Mission not found in active sequence, starting standalone.");
                    StartStandaloneMission(mission);
                    return;
                }

                SetCurrentMission(idx);
                return;
            }

            StartStandaloneMission(mission);
        }

        private void StartStandaloneMission(MissionDefinition mission) {
            CleanupCurrentMission();
            CurrentIndex = -1;
            _currentDefinition = mission;
            _currentMission = MissionFactory.CreateMission(mission);
            if (_currentMission != null) {
                _currentMission.Initialize(mission, _eventBus, _saveService);
                if (_currentMission is MissionBase missionBase) missionBase.OnMissionComplete += OnMissionComplete;
                _currentMission.StartMission();
                PublishMissionUpdate();
                PlayStartFadeIfNeeded(mission);
            }
        }

        public MissionDefinition GetCurrentMission() {
            if (_currentDefinition != null) return _currentDefinition;
            if (_activeSequence != null && _sequenceIndex >= 0 && _sequenceIndex < _activeSequence.Sequence.Count) {
                return _activeSequence.Sequence[_sequenceIndex];
            }
            return null;
        }

        private void SetCurrentMission(int index) {
            if (_eventBus == null) return;
            if (_activeSequence == null || _activeSequence.Sequence == null) return;
            if (index < 0 || index >= _activeSequence.Sequence.Count) return;

            CleanupCurrentMission();

            _sequenceIndex = index;
            CurrentIndex = index;
            MissionDefinition def = _activeSequence.Sequence[index];
            _currentDefinition = def;
            _currentMission = MissionFactory.CreateMission(def);

            if (_currentMission != null) {
                _currentMission.Initialize(def, _eventBus, _saveService);
                if (_currentMission is MissionBase missionBase) {
                    missionBase.OnMissionComplete += OnMissionComplete;
                }
                _currentMission.StartMission();
                PublishMissionUpdate();
                PlayStartFadeIfNeeded(def);
            }
        }

        private void CleanupCurrentMission() {
            if (_currentMission == null) return;

            UnityEngine.Object missionObject = _currentMission as UnityEngine.Object;
            if (missionObject == null) {
                _currentMission = null;
                return;
            }

            if (_currentMission is MissionBase missionBase) {
                missionBase.OnMissionComplete -= OnMissionComplete;
            }

            _currentMission.Cleanup();
            _currentMission = null;
        }

        private void OnMissionComplete() {
            CompleteCurrentMission();
        }

        public void CompleteCurrentMission() {
            PlayMissionCompleteSFX();
            if (_activeSequence != null && _activeSequence.Sequence != null) {
                _sequenceIndex++;
                if (_sequenceIndex >= _activeSequence.Sequence.Count) {
                    _activeSequence = null;
                    _sequenceIndex = -1;
                    CurrentIndex = -1;
                    _currentDefinition = null;
                    RequestAutosave();
                    return;
                }

                SetCurrentMission(_sequenceIndex);
                RequestAutosave();
                return;
            }

            CleanupCurrentMission();
            CurrentIndex = -1;
            _currentDefinition = null;
            RequestAutosave();
        }

        private void PlayMissionCompleteSFX() {
            if (_missionCompleteSFX == null) return;
            if (_audioService == null) ServiceLocator.TryGet<IAudioService>(out _audioService);
            _audioService?.PlaySFX(clip: _missionCompleteSFX);
        }

        void IMissionService.PlayMissionCompleteSFX() {
            PlayMissionCompleteSFX();
        }

        void IMissionService.UpdateCollectAndDeliverDoorState(MissionDefinition definition, int deliveredCount) {
            UpdateCollectAndDeliverDoorState(definition, deliveredCount);
        }

        private void UpdateCollectAndDeliverDoorState(MissionDefinition definition, int deliveredCount) {
            if (definition == null || definition.DeliveryPointIds == null || definition.DeliveryPointIds.Length == 0) {
                return;
            }

            if (_mapService == null) {
                ServiceLocator.TryGet<IMapService>(out _mapService);
            }

            if (_mapService == null) {
                return;
            }

            List<DoorType> orderedDoors = new List<DoorType>();

            for (int i = 0; i < definition.DeliveryPointIds.Length; i++) {
                DoorType doorType = GetDoorTypeForDeliveryPoint(definition.DeliveryPointIds[i]);
                if (doorType == DoorType.None || orderedDoors.Contains(doorType)) {
                    continue;
                }

                orderedDoors.Add(doorType);
            }

            if (orderedDoors.Count == 0) {
                return;
            }

            if (orderedDoors.Count == 0) {
                return;
            }

            if (deliveredCount < 0) {
                for (int i = 0; i < orderedDoors.Count; i++) {
                    GameObject doorObject = _mapService.Get(orderedDoors[i]);
                    Door door = doorObject != null ? doorObject.GetComponent<Door>() : null;
                    door?.Lock();
                }

                GameObject firstDoorObj = _mapService.Get(orderedDoors[0]);
                Door firstDoor = firstDoorObj != null ? firstDoorObj.GetComponent<Door>() : null;
                firstDoor?.Unlock();

                GameObject corredorObjInit = _mapService.Get(DoorType.Door_Corredor);
                Door corredorInit = corredorObjInit != null ? corredorObjInit.GetComponent<Door>() : null;
                corredorInit?.Lock();

                return;
            }

            for (int i = 0; i < orderedDoors.Count; i++) {
                GameObject doorObject = _mapService.Get(orderedDoors[i]);
                Door door = doorObject != null ? doorObject.GetComponent<Door>() : null;
                if (door == null) {
                    continue;
                }

                if (i <= deliveredCount) {
                    door.Unlock();
                }
                else {
                    door.Lock();
                }
            }

            GameObject corredorObj = _mapService.Get(DoorType.Door_Corredor);
            Door corredor = corredorObj != null ? corredorObj.GetComponent<Door>() : null;
            if (deliveredCount >= orderedDoors.Count) {
                corredor?.Unlock();
            }
            else {
                corredor?.Lock();
            }
        }

        private static DoorType GetDoorTypeForDeliveryPoint(string deliveryPointId) {
            if (string.IsNullOrWhiteSpace(deliveryPointId)) {
                return DoorType.None;
            }

            string suffix = deliveryPointId;
            int underscoreIndex = deliveryPointId.LastIndexOf('_');
            if (underscoreIndex >= 0 && underscoreIndex < deliveryPointId.Length - 1) {
                suffix = deliveryPointId.Substring(underscoreIndex + 1);
            }

            return suffix.ToUpperInvariant() switch {
                "A" => DoorType.Door_RoomA,
                "B" => DoorType.Door_RoomB,
                "C" => DoorType.Door_RoomC,
                "E" => DoorType.Door_RoomE,
                "P" => DoorType.Door_RoomP,
                _ => DoorType.None
            };
        }

        public void SkipToMission(int missionIndex) {
            if (_activeSequence == null || _activeSequence.Sequence == null) {
                Debug.LogWarning("[MissionService] No active sequence to skip within.");
                return;
            }

            if (missionIndex < 0 || missionIndex >= _activeSequence.Sequence.Count) {
                Debug.LogWarning($"[MissionService] Invalid mission index: {missionIndex}");
                return;
            }

            for (int i = 0; i <= missionIndex; i++) {
                MissionDefinition def = _activeSequence.Sequence[i];
                if (def == null || def.DebugSetupEvents == null) continue;

                foreach (string debugEvent in def.DebugSetupEvents) {
                    if (string.IsNullOrWhiteSpace(debugEvent)) continue;

                    if (debugEvent.StartsWith("Item:", System.StringComparison.OrdinalIgnoreCase)) {
                        string itemName = debugEvent.Substring(5);
                        _eventBus?.Publish(new ItemPickedUpEvent(itemName, null));
                    }
                    else {
                        _eventBus?.Publish(new GenericGameEvent(debugEvent));
                    }
                }
            }

            SetCurrentMission(missionIndex);
        }

        private void PublishMissionUpdate() {
            MissionDefinition current = GetCurrentMission();
            if (current != null) {
                _eventBus?.Publish(new MissionUpdatedEvent(current.MissionId, current.Title, current.Description));
            }
        }

        private void RequestAutosave() {
            if (_saveService == null) {
                return;
            }

            if (_isAutosaving) {
                _autosaveRequestedAgain = true;
                return;
            }

            _autosaveRoutine = StartCoroutine(AutosaveRoutine());
        }

        private IEnumerator AutosaveRoutine() {
            _isAutosaving = true;
            _eventBus?.Publish(new AutosaveStartedEvent());

            yield return new WaitForEndOfFrame();

            SaveData saveData = _saveService.LoadFromSlot(AUTOSAVE_SLOT) ?? new SaveData();
            PopulateSaveData(saveData);

            yield return CaptureScreenshot(saveData);

            _saveService.SaveToSlot(AUTOSAVE_SLOT, saveData);
            _eventBus?.Publish(new AutosaveCompletedEvent());

            _autosaveRoutine = null;
            _isAutosaving = false;
            if (_autosaveRequestedAgain) {
                _autosaveRequestedAgain = false;
                RequestAutosave();
            }
        }

        private void PopulateSaveData(SaveData saveData) {
            saveData.CurrentMissionIndex = CurrentIndex;

            if (_currentDefinition != null && !string.IsNullOrWhiteSpace(_currentDefinition.MissionId)) {
                saveData.LastCheckpointId = _currentDefinition.MissionId;
            }

            PlayerController player = _playerController;
            if (player == null) {
                player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            }
            if (player == null) {
                var allPlayers = Resources.FindObjectsOfTypeAll<PlayerController>();
                foreach (var p in allPlayers) {
                    if (p != null && p.gameObject.scene.isLoaded) {
                        player = p;
                        break;
                    }
                }
            }

            if (player != null) {
                saveData.PlayerPosition = new Vector3Data(player.transform.position);
                saveData.PlayerRotation = new QuaternionData(player.transform.rotation);
                saveData.HasPlayerPosition = true;

                PlayerCamera playerCamera = player.PlayerCamera;
                if (playerCamera != null) {
                    Transform cameraTarget = playerCamera.GetCameraTarget();
                    if (cameraTarget != null) {
                        saveData.CameraTargetPosition = new Vector3Data(cameraTarget.position);
                        saveData.CameraTargetRotation = new QuaternionData(cameraTarget.rotation);
                    }
                }
            }
            else {
                GameObject spawnPoint = GameObject.Find("PlayerSpawn");
                if (spawnPoint != null) {
                    saveData.PlayerPosition = new Vector3Data(spawnPoint.transform.position);
                    saveData.PlayerRotation = new QuaternionData(spawnPoint.transform.rotation);
                    saveData.HasPlayerPosition = true;
                    saveData.CameraTargetPosition = new Vector3Data(spawnPoint.transform.position);
                    saveData.CameraTargetRotation = new QuaternionData(spawnPoint.transform.rotation);
                }
                else {
                    if (saveData.PlayerPosition == null || saveData.PlayerPosition.ToVector3() == Vector3.zero) {
                        GameObject taggedPlayer = GameObject.FindWithTag("Player");
                        if (taggedPlayer != null) {
                            saveData.PlayerPosition = new Vector3Data(taggedPlayer.transform.position);
                            saveData.PlayerRotation = new QuaternionData(taggedPlayer.transform.rotation);
                            saveData.HasPlayerPosition = true;
                        }
                    }
                }
            }

            if (_inventoryService != null) {
                IReadOnlyList<Item> items = _inventoryService.GetItems();
                saveData.InventoryItemIds.Clear();

                for (int i = 0; i < items.Count; i++) {
                    if (items[i] != null) {
                        saveData.InventoryItemIds.Add(items[i].Id);
                    }
                }
            }
        }

        private IEnumerator CaptureScreenshot(SaveData saveData) {
            Texture2D tex = null;

            try {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex != null) {
                    byte[] png = tex.EncodeToPNG();
                    if (png != null && png.Length > 0) {
                        saveData.ScreenshotBase64 = Convert.ToBase64String(png);
                    }
                }
            }
            catch {
                // Keep the autosave even if screenshot capture fails.
            }
            finally {
                if (tex != null) {
                    Destroy(tex);
                }
            }

            yield return null;
        }

        private void PlayStartFadeIfNeeded(MissionDefinition mission) {
            if (mission == null || !mission.UseFadeOnStart) return;

            if (_fadeService == null) {
                ServiceLocator.TryGet<IFadeService>(out _fadeService);
            }

            if (_fadeService == null) return;

            _fadeService.FadeIn(START_FADE_DURATION);
        }

        public void StartSequence(MissionSequenceSO sequence) {
            if (sequence == null || sequence.Sequence == null || sequence.Sequence.Count == 0) {
                Debug.LogWarning("[MissionService] Attempted to start an empty or null sequence.");
                return;
            }

            _activeSequence = sequence;
            _sequenceIndex = 0;
            StartMission(_activeSequence.Sequence[_sequenceIndex]);
        }

    }
}
