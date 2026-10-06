// Autor: Murillo Gomes Yonamine
// Data: 05/05/2026

using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;
using FifthSemester.Core.Services;
using FifthSemester.Gameplay.Inventory;
using FifthSemester.Player;
using FifthSemester.Player.Components;
using FifthSemester.Gameplay.Missions;

namespace FifthSemester.Gameplay.Save {
    public class SaveLoaderRunner : MonoBehaviour { }

    public static class SaveLoader {
        private static SaveData _pending;

        public static bool IsPendingSave => _pending != null;

        public static void SetPendingSave(SaveData data) {
            _pending = data;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public static void ClearPendingSave() {
            _pending = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (_pending == null) {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                return;
            }

            GameObject runnerObj = new GameObject("SaveLoader_CoroutineRunner");
            var runner = runnerObj.AddComponent<SaveLoaderRunner>();
            runner.StartCoroutine(ApplySaveDelayed(runnerObj));
        }

        private static IEnumerator ApplySaveDelayed(GameObject runnerObj) {
            // Wait until scene components have completed Start (including MissionService).
            yield return null;
            PlayerController player = null;
            // Espera ate o player ser encontrado na cena (maximo de 10 frames)
            for (int i = 0; i < 10; i++) {
                player = Object.FindFirstObjectByType<PlayerController>();
                if (player != null) break;
                yield return null;
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

            IMissionService missionService = ServiceLocator.Get<IMissionService>();
            IInventoryService<Item> inventoryService = ServiceLocator.Get<IInventoryService<Item>>();

            if (player != null) {
                Vector3 targetPos = _pending.PlayerPosition.ToVector3();
                Quaternion targetRot = _pending.PlayerRotation.ToQuaternion();

                if (!_pending.HasPlayerPosition && targetPos == Vector3.zero) {
                    GameObject spawnPoint = GameObject.Find("PlayerSpawn");
                    if (spawnPoint != null) {
                        targetPos = spawnPoint.transform.position;
                        targetRot = spawnPoint.transform.rotation;
                    }
                    else {
                        targetPos = player.transform.position;
                        targetRot = player.transform.rotation;
                    }
                }

                player.transform.position = targetPos;
                player.transform.rotation = targetRot;

                if (player.Rigidbody != null) {
                    player.Rigidbody.position = targetPos;
                    player.Rigidbody.rotation = targetRot;
                    player.Rigidbody.linearVelocity = Vector3.zero;
                    player.Rigidbody.angularVelocity = Vector3.zero;
                }

                PlayerCamera playerCamera = player.PlayerCamera;
                if (playerCamera != null) {
                    Transform cameraTarget = playerCamera.GetCameraTarget();
                    if (cameraTarget != null) {
                        Vector3 camPos = _pending.CameraTargetPosition.ToVector3();
                        Quaternion camRot = _pending.CameraTargetRotation.ToQuaternion();
                        if (camPos == Vector3.zero && targetPos != Vector3.zero) {
                            camPos = cameraTarget.position;
                            camRot = cameraTarget.rotation;
                        }
                        cameraTarget.position = camPos;
                        cameraTarget.rotation = camRot;
                    }
                    playerCamera.SetRotation(_pending.CameraTargetRotation.ToQuaternion());
                }
            } 

            if (missionService != null && _pending.CurrentMissionIndex >= 0) {
                missionService.SkipToMission(_pending.CurrentMissionIndex);
            }

            if (inventoryService != null) {
                inventoryService.Clear();
                LoadInventoryItems(inventoryService, _pending.InventoryItemIds);
            }

            _pending = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (runnerObj != null) {
                Object.Destroy(runnerObj);
            }
        }

        private static void LoadInventoryItems(IInventoryService<Item> inventoryService, IReadOnlyList<string> itemIds) {
            Item[] sceneItems = Resources.FindObjectsOfTypeAll<Item>();
            HashSet<Item> restoredItems = new HashSet<Item>();

            for (int i = 0; i < itemIds.Count; i++) {
                string itemId = itemIds[i];
                if (string.IsNullOrWhiteSpace(itemId)) {
                    continue;
                }

                bool found = false;
                for (int j = 0; j < sceneItems.Length; j++) {
                    Item item = sceneItems[j];
                    if (item == null || !item.gameObject.scene.isLoaded || restoredItems.Contains(item) || item.Id != itemId) continue;
                    if (inventoryService.AddItem(item)) {
                        restoredItems.Add(item);
                        found = true;
                    }
                    break;
                }
                if (!found) {
                    Debug.LogWarning($"[SaveLoader] Could not restore item '{itemId}' from the loaded scene.");
                }
            }
        }
    }
}
