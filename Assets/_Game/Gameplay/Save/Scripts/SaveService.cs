using System;
using System.Collections.Generic;
using System.IO;
using FifthSemester.Core.Services;
using FifthSemester.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FifthSemester.Gameplay.Save {
    public class SaveService : ISaveService {
        private const string TAG = "<color=yellow><b>[SaveService]</b></color>";
        private const string SAVE_PREFIX = "save_";
        private const string AUTOSAVE_SLOT = "default";
        private readonly string _saveDirectory;

        public event Action<string> OnSaveCompleted;

        public SaveService() : this(Path.Combine(Application.persistentDataPath, "Saves")) { }

        public SaveService(string saveDirectory) {
            _saveDirectory = saveDirectory;
        }

        public void SaveToSlot(string slotId, SaveData data) {
            if (data == null) return;

            data.Timestamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            string activeScene = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(activeScene) && activeScene != "MainMenu") {
                data.SceneName = activeScene;
                CaptureMissingPlayerPosition(data);
            }
            else if (string.IsNullOrEmpty(data.SceneName)) {
                data.SceneName = "Game";
            }

            CopyProgressToEntries(data);
            if (WriteFile(GetPath(slotId), JsonUtility.ToJson(data))) {
                OnSaveCompleted?.Invoke(slotId);
            }
        }

        public SaveData LoadFromSlot(string slotId) {
            string path = GetPath(slotId);
            if (File.Exists(path)) {
                SaveData saved = ReadFile(path);
                if (saved != null) return saved;

                return ReadFile(path + ".bak");
            }

            if (File.Exists(path + ".bak")) return ReadFile(path + ".bak");

            string legacyKey = SAVE_PREFIX + slotId;
            if (!PlayerPrefs.HasKey(legacyKey)) return null;

            try {
                SaveData legacy = Normalize(JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(legacyKey)));
                if (legacy != null) {
                    // Legacy saves have no position flag; a non-zero position was explicitly captured.
                    legacy.HasPlayerPosition = legacy.PlayerPosition.ToVector3() != Vector3.zero;
                    CopyProgressToEntries(legacy);
                    if (WriteFile(path, JsonUtility.ToJson(legacy))) {
                        PlayerPrefs.DeleteKey(legacyKey);
                        PlayerPrefs.Save();
                    }
                }
                return legacy;
            }
            catch (Exception exception) {
                Debug.LogError($"{TAG} Could not migrate slot '{slotId}': {exception}");
                return null;
            }
        }

        public void DeleteSlot(string slotId) {
            string path = GetPath(slotId);
            DeleteFile(path);
            DeleteFile(path + ".bak");
            DeleteFile(path + ".tmp");
            PlayerPrefs.DeleteKey(SAVE_PREFIX + slotId);
            PlayerPrefs.Save();
        }

        public bool SlotExists(string slotId) {
            string path = GetPath(slotId);
            return File.Exists(path) || File.Exists(path + ".bak") || PlayerPrefs.HasKey(SAVE_PREFIX + slotId);
        }

        public string[] ListSlots() {
            return SlotExists(AUTOSAVE_SLOT) ? new[] { AUTOSAVE_SLOT } : Array.Empty<string>();
        }

        public void SaveCheckpoint(string checkpointId, SaveData data) {
            data.LastCheckpointId = checkpointId;
            SaveToSlot(checkpointId, data);
        }

        private string GetPath(string slotId) {
            return Path.Combine(_saveDirectory, SAVE_PREFIX + Uri.EscapeDataString(slotId) + ".json");
        }

        private bool WriteFile(string path, string json) {
            try {
                Directory.CreateDirectory(_saveDirectory);
                string temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(path)) {
                    File.Replace(temporaryPath, path, path + ".bak");
                }
                else {
                    File.Move(temporaryPath, path);
                }
                return true;
            }
            catch (Exception exception) {
                Debug.LogError($"{TAG} Could not write save file '{path}': {exception}");
                return false;
            }
        }

        private static SaveData ReadFile(string path) {
            if (!File.Exists(path)) return null;
            try {
                SaveData data = Normalize(JsonUtility.FromJson<SaveData>(File.ReadAllText(path)));
                if (data == null || string.IsNullOrEmpty(data.SceneName)) {
                    Debug.LogError($"{TAG} Invalid save file: {path}");
                    return null;
                }
                return data;
            }
            catch (Exception exception) {
                Debug.LogError($"{TAG} Could not read save file '{path}': {exception}");
                return null;
            }
        }

        private static void DeleteFile(string path) {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void CaptureMissingPlayerPosition(SaveData data) {
            if (data.HasPlayerPosition) return;
            PlayerController player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player == null) {
                PlayerController[] players = Resources.FindObjectsOfTypeAll<PlayerController>();
                for (int i = 0; i < players.Length; i++) {
                    if (players[i] != null && players[i].gameObject.scene.isLoaded) {
                        player = players[i];
                        break;
                    }
                }
            }
            if (player == null) return;

            data.PlayerPosition = new Vector3Data(player.transform.position);
            data.PlayerRotation = new QuaternionData(player.transform.rotation);
            data.HasPlayerPosition = true;
        }

        private static void CopyProgressToEntries(SaveData data) {
            data.MissionProgressEntries ??= new List<MissionProgressEntry>();
            data.MissionProgressEntries.Clear();
            foreach (KeyValuePair<string, string> progress in data.MissionProgress) {
                data.MissionProgressEntries.Add(new MissionProgressEntry {
                    MissionId = progress.Key,
                    Progress = progress.Value
                });
            }
        }

        private static SaveData Normalize(SaveData data) {
            if (data == null) return null;
            data.MissionProgress ??= new Dictionary<string, string>();
            data.MissionProgressEntries ??= new List<MissionProgressEntry>();
            for (int i = 0; i < data.MissionProgressEntries.Count; i++) {
                MissionProgressEntry entry = data.MissionProgressEntries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.MissionId)) {
                    data.MissionProgress[entry.MissionId] = entry.Progress;
                }
            }
            data.InventoryItemIds ??= new List<string>();
            data.PlayerPosition ??= new Vector3Data();
            data.PlayerRotation ??= new QuaternionData();
            data.CameraTargetPosition ??= new Vector3Data();
            data.CameraTargetRotation ??= new QuaternionData();
            if (string.IsNullOrEmpty(data.SceneName)) data.SceneName = "Game";
            return data;
        }
    }
}
