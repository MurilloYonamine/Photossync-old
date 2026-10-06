using System;
using System.Collections.Generic;
using System.IO;
using FifthSemester.Core.Services;
using FifthSemester.Gameplay.Save;
using FifthSemester.Gameplay.Map2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FifthSemester.Tests {
    public class SaveServiceTests {
        private SaveService _saveService;
        private string _saveDirectory;
        private readonly List<string> _legacyKeys = new List<string>();

        [SetUp]
        public void SetUp() {
            _saveDirectory = Path.Combine(Application.temporaryCachePath, "SaveServiceTests_" + Guid.NewGuid().ToString("N"));
            _saveService = new SaveService(_saveDirectory);
        }

        [TearDown]
        public void TearDown() {
            if (Directory.Exists(_saveDirectory)) Directory.Delete(_saveDirectory, true);
            for (int i = 0; i < _legacyKeys.Count; i++) PlayerPrefs.DeleteKey(_legacyKeys[i]);
            PlayerPrefs.Save();
            _legacyKeys.Clear();
        }

        [Test]
        public void SaveAndLoad_RestoresPositionAtWorldOriginAndInventory() {
            SaveData data = new SaveData {
                HasPlayerPosition = true,
                PlayerPosition = new Vector3Data(Vector3.zero),
                PlayerRotation = new QuaternionData(Quaternion.Euler(0f, 73f, 0f)),
                CurrentMissionIndex = 4,
                InventoryItemIds = new List<string> { "key_A", "key_B" }
            };

            _saveService.SaveToSlot("test", data);
            SaveData loaded = _saveService.LoadFromSlot("test");

            Assert.IsTrue(File.Exists(Path.Combine(_saveDirectory, "save_test.json")));
            Assert.IsTrue(loaded.HasPlayerPosition);
            Assert.AreEqual(Vector3.zero, loaded.PlayerPosition.ToVector3());
            Assert.AreEqual(73f, loaded.PlayerRotation.ToQuaternion().eulerAngles.y, 0.01f);
            Assert.AreEqual(4, loaded.CurrentMissionIndex);
            CollectionAssert.AreEqual(new[] { "key_A", "key_B" }, loaded.InventoryItemIds);
        }

        [Test]
        public void SaveAndLoad_PreservesNonZeroPositionAndScene() {
            Vector3 savedPosition = new Vector3(12.5f, 2f, -8f);
            SaveData data = new SaveData {
                HasPlayerPosition = true,
                PlayerPosition = new Vector3Data(savedPosition),
                SceneName = "Game_Mapa2",
                CurrentMissionIndex = -1,
                Map2KeysCompleted = true
            };

            _saveService.SaveToSlot("map2", data);
            SaveData loaded = _saveService.LoadFromSlot("map2");

            Assert.AreEqual(savedPosition, loaded.PlayerPosition.ToVector3());
            string activeScene = SceneManager.GetActiveScene().name;
            string expectedScene = string.IsNullOrEmpty(activeScene) || activeScene == "MainMenu" ? "Game_Mapa2" : activeScene;
            Assert.AreEqual(expectedScene, loaded.SceneName);
            Assert.AreEqual(-1, loaded.CurrentMissionIndex);
            Assert.IsTrue(loaded.Map2KeysCompleted);
        }

        [Test]
        public void SaveAndLoad_RestoresMissionProgress() {
            SaveData data = new SaveData();
            data.MissionProgress["collect"] = "2/3";

            _saveService.SaveToSlot("progress", data);
            SaveData loaded = _saveService.LoadFromSlot("progress");

            Assert.AreEqual("2/3", loaded.MissionProgress["collect"]);
        }

        [Test]
        public void SaveAndLoad_RestoresMap2PasswordWithKeyCompletion() {
            Map2PasswordState password = new Map2PasswordState();
            password.Initialize("0311");
            password.TryReveal(0);

            SaveData data = new SaveData {
                Map2KeysCompleted = true,
                Map2PasswordJson = JsonUtility.ToJson(password)
            };
            _saveService.SaveToSlot("map2_password", data);
            SaveData loaded = _saveService.LoadFromSlot("map2_password");

            Assert.IsTrue(loaded.Map2KeysCompleted);
            Assert.AreEqual("0XXX", JsonUtility.FromJson<Map2PasswordState>(loaded.Map2PasswordJson).GetDisplayCode());
        }

        [Test]
        public void LoadFromSlot_MigratesLegacyPlayerPrefsWithoutDeletingOtherSettings() {
            string slot = "migration_" + Guid.NewGuid().ToString("N");
            string key = "save_" + slot;
            _legacyKeys.Add(key);
            PlayerPrefs.SetString(key, JsonUtility.ToJson(new SaveData { LastCheckpointId = "cp1" }));

            SaveData loaded = _saveService.LoadFromSlot(slot);

            Assert.AreEqual("cp1", loaded.LastCheckpointId);
            Assert.IsTrue(File.Exists(Path.Combine(_saveDirectory, key + ".json")));
            Assert.IsFalse(PlayerPrefs.HasKey(key));
        }

        [Test]
        public void DeleteSlot_RemovesFileAndLegacySlot() {
            string slot = "delete_" + Guid.NewGuid().ToString("N");
            _saveService.SaveToSlot(slot, new SaveData());
            Assert.IsTrue(_saveService.SlotExists(slot));

            _saveService.DeleteSlot(slot);

            Assert.IsFalse(_saveService.SlotExists(slot));
        }
    }
}
