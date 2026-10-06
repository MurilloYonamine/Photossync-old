using System;
using UnityEngine;
using FifthSemester.Core.Services;
using FifthSemester.Gameplay.Inventory;
using FifthSemester.Player;
using FifthSemester.Player.Components;

namespace FifthSemester.Gameplay.Map2 {
    [Serializable]
    public class Map2PasswordState {
        private const string DEFAULT_SLOT = "default";
        [SerializeField] private string _targetCode = string.Empty;
        [SerializeField] private bool[] _revealedPositions = Array.Empty<bool>();

        public string TargetCode => _targetCode;

        public int Length => string.IsNullOrEmpty(_targetCode) ? 0 : _targetCode.Length;

        public bool IsComplete {
            get {
                if (_revealedPositions == null || _revealedPositions.Length == 0) {
                    return false;
                }

                for (int i = 0; i < _revealedPositions.Length; i++) {
                    if (!_revealedPositions[i]) {
                        return false;
                    }
                }

                return true;
            }
        }

        public void Initialize(string targetCode) {
            string safeTargetCode = targetCode ?? string.Empty;

            if (_targetCode != safeTargetCode || _revealedPositions == null || _revealedPositions.Length != safeTargetCode.Length) {
                _revealedPositions = new bool[safeTargetCode.Length];
            }

            _targetCode = safeTargetCode;
        }

        public void ForceRevealAll() {
            if (_revealedPositions != null) {
                for (int i = 0; i < _revealedPositions.Length; i++) {
                    _revealedPositions[i] = true;
                }
            }
        }

        public bool CanRevealDigit(int digit) {
            if (_revealedPositions == null || _revealedPositions.Length != Length) {
                _revealedPositions = new bool[Length];
            }

            for (int i = 0; i < Length; i++) {
                char expectedDigit = _targetCode[i];
                if (_revealedPositions[i]) {
                    continue;
                }

                if (char.IsDigit(expectedDigit) && expectedDigit - '0' == digit) {
                    return true;
                }
            }

            return false;
        }

        public bool IsRevealed(int index) {
            if (index < 0 || _revealedPositions == null || index >= _revealedPositions.Length) {
                return false;
            }

            return _revealedPositions[index];
        }

        public bool TryReveal(int digit) {
            if (_revealedPositions == null || _revealedPositions.Length != Length) {
                _revealedPositions = new bool[Length];
            }

            for (int i = 0; i < Length; i++) {
                if (_revealedPositions[i]) {
                    continue;
                }

                char expectedDigit = _targetCode[i];
                if (!char.IsDigit(expectedDigit) || expectedDigit - '0' != digit) {
                    continue;
                }

                _revealedPositions[i] = true;
                return true;
            }

            return false;
        }

        public string GetDisplayCode() {
            if (string.IsNullOrEmpty(_targetCode)) {
                return string.Empty;
            }

            char[] display = _targetCode.ToCharArray();

            for (int i = 0; i < display.Length; i++) {
                if (!IsRevealed(i)) {
                    display[i] = 'X';
                }
            }

            return new string(display);
        }

        public static Map2PasswordState LoadOrCreate(string prefsKey, string targetCode) {
            Map2PasswordState state = Load(prefsKey);

            if (state == null) {
                state = new Map2PasswordState();
            }

            state.Initialize(targetCode);
            return state;
        }

        public static Map2PasswordState Load(string prefsKey) {
            ISaveService saveService = ServiceLocator.Get<ISaveService>();
            SaveData saveData = saveService.LoadFromSlot(DEFAULT_SLOT);
            if (saveData != null && !string.IsNullOrEmpty(saveData.Map2PasswordJson)) {
                return JsonUtility.FromJson<Map2PasswordState>(saveData.Map2PasswordJson);
            }

            if (saveData == null || !PlayerPrefs.HasKey(prefsKey)) return null;

            string json = PlayerPrefs.GetString(prefsKey);
            if (string.IsNullOrWhiteSpace(json)) return null;

            Map2PasswordState legacy = JsonUtility.FromJson<Map2PasswordState>(json);
            if (legacy == null) return null;

            saveData.Map2PasswordJson = json;
            saveService.SaveToSlot(DEFAULT_SLOT, saveData);
            DeleteMigratedLegacy(prefsKey, json, saveService);
            return legacy;
        }

        public void Save(string prefsKey) {
            ISaveService saveService = ServiceLocator.Get<ISaveService>();
            SaveData saveData = saveService.LoadFromSlot(DEFAULT_SLOT) ?? new SaveData();
            string json = JsonUtility.ToJson(this);
            saveData.Map2PasswordJson = json;

            PlayerController player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player != null) {
                saveData.PlayerPosition = new Vector3Data(player.transform.position);
                saveData.PlayerRotation = new QuaternionData(player.transform.rotation);
                saveData.HasPlayerPosition = true;

                PlayerCamera playerCamera = player.PlayerCamera;
                if (playerCamera != null && playerCamera.GetCameraTarget() != null) {
                    Transform cameraTarget = playerCamera.GetCameraTarget();
                    saveData.CameraTargetPosition = new Vector3Data(cameraTarget.position);
                    saveData.CameraTargetRotation = new QuaternionData(cameraTarget.rotation);
                }
            }

            if (ServiceLocator.TryGet<IInventoryService<Item>>(out var inventory)) {
                var items = inventory.GetItems();
                saveData.InventoryItemIds.Clear();
                for (int i = 0; i < items.Count; i++) {
                    if (items[i] != null) saveData.InventoryItemIds.Add(items[i].Id);
                }
            }

            saveService.SaveToSlot(DEFAULT_SLOT, saveData);
            DeleteMigratedLegacy(prefsKey, json, saveService);
        }

        private static void DeleteMigratedLegacy(string prefsKey, string json, ISaveService saveService) {
            if (!PlayerPrefs.HasKey(prefsKey)) return;
            SaveData stored = saveService.LoadFromSlot(DEFAULT_SLOT);
            if (stored == null || stored.Map2PasswordJson != json) return;

            PlayerPrefs.DeleteKey(prefsKey);
            PlayerPrefs.Save();
        }
    }
}
