using System.Collections.Generic;
using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Gameplay.Inventory;
using FifthSemester.Gameplay.Enemy;
using FifthSemester.Player;
using FifthSemester.Player.Components;
using UnityEngine;
using UnityEngine.Playables;

namespace FifthSemester.Gameplay.Map2 {
    public class KeyService : MonoBehaviour, IMap2KeyService {
        [Header("Timeline")]
        [SerializeField] private PlayableDirector _allKeysCollectedTimeline;

        [Header("Trigger Key Configuration")]
        [Tooltip("A chave específica que, ao ser coletada, dispara a cutscene e libera o estado das chaves.")]
        [SerializeField] private Map2KeyDefinitionSO _triggerKeyDefinition;

        private List<Map2KeyItem> _registeredKeys = new List<Map2KeyItem>(); // Mantido para compatibilidade de assinatura, mas não utilizado
        private IInventoryService<Item> _inventoryService;
        private IEventBus _eventBus;
        private bool _played;

        public bool HasCollectedAllKeys { get; private set; }

        private void Awake() {
            ServiceLocator.Register<IMap2KeyService>(this);
            _eventBus = ServiceLocator.Get<IEventBus>();
            _inventoryService = ServiceLocator.Get<IInventoryService<Item>>();
        }

        private void Start() {
            _eventBus?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        }

        private void OnDestroy() {
            _eventBus?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
            ServiceLocator.TryGet<IMap2KeyService>(out var dummy);
        }

        public void RegisterKey(Map2KeyItem key) {
            // Ignorado intencionalmente para evitar bugs com GameObjects inativos
        }

        public void UnregisterKey(Map2KeyItem key) {
            // Ignorado intencionalmente
        }

        private void OnItemPickedUp(ItemPickedUpEvent evt) {
            if (_played) {
                Debug.Log("[KeyService] OnItemPickedUp: Event ignored because cutscene/all keys completed was already triggered (_played is true).");
                return;
            }
            if (evt.ItemGameObject == null) {
                Debug.LogWarning("[KeyService] OnItemPickedUp: Event received but ItemGameObject is null!");
                return;
            }

            Map2KeyItem picked = evt.ItemGameObject.GetComponent<Map2KeyItem>();
            if (picked == null) {
                Debug.Log($"[KeyService] OnItemPickedUp: Item '{evt.ItemGameObject.name}' is not a Map2KeyItem. Ignoring.");
                return;
            }

            Debug.Log($"[KeyService] OnItemPickedUp: Key '{picked.name}' (Definition: {(picked.KeyDefinition != null ? picked.KeyDefinition.name : "None")}) was picked up.");

            // Autosave ao pegar a chave no Mapa 2
            SaveMap2Progress();

            // Se uma chave gatilho foi configurada e esta chave corresponde a ela
            if (_triggerKeyDefinition != null) {
                if (picked.KeyDefinition == _triggerKeyDefinition) {
                    Debug.Log($"[KeyService] MATCH DETECTED! Key '{picked.name}' matches Trigger Key Definition '{_triggerKeyDefinition.name}'. Starting cutscene sequence.");
                    TriggerKeysCompleted(picked);
                } else {
                    Debug.Log($"[KeyService] Key '{picked.name}' does NOT match Trigger Key Definition '{_triggerKeyDefinition.name}'. Waiting for the correct trigger key.");
                }
            } else {
                Debug.LogWarning("[KeyService] OnItemPickedUp: Trigger Key Definition is NOT configured in the inspector! Cannot match keys.");
            }
        }

        public bool TryPrepareForLastKey(Map2KeyItem lastKey) {
            if (_played) {
                Debug.Log("[KeyService] TryPrepareForLastKey: Ignored because _played is true.");
                return false;
            }
            if (_inventoryService == null) {
                Debug.LogWarning("[KeyService] TryPrepareForLastKey: Inventory service is null!");
                return false;
            }
            if (lastKey == null) {
                Debug.LogWarning("[KeyService] TryPrepareForLastKey: Provided lastKey is null!");
                return false;
            }

            Debug.Log($"[KeyService] TryPrepareForLastKey: Checking key '{lastKey.name}' (Definition: {(lastKey.KeyDefinition != null ? lastKey.KeyDefinition.name : "None")}).");

            // Só preparamos se for a chave gatilho configurada
            if (_triggerKeyDefinition != null && lastKey.KeyDefinition == _triggerKeyDefinition) {
                Debug.Log($"[KeyService] TryPrepareForLastKey: MATCH! Key '{lastKey.name}' is the trigger key. Cleaning up inventory of other keys.");
                
                // Limpa as chaves anteriores do inventário
                var items = _inventoryService.GetItems();
                if (items != null) {
                    List<Map2KeyItem> keysInInventory = new List<Map2KeyItem>();
                    for (int i = 0; i < items.Count; i++) {
                        if (items[i] is Map2KeyItem keyItem && keyItem != lastKey) {
                            keysInInventory.Add(keyItem);
                        }
                    }

                    Debug.Log($"[KeyService] TryPrepareForLastKey: Found {keysInInventory.Count} other keys in inventory to clean up.");
                    for (int i = 0; i < keysInInventory.Count; i++) {
                        Debug.Log($"[KeyService] TryPrepareForLastKey: Removing and disabling auxiliary key '{keysInInventory[i].name}' from inventory.");
                        _inventoryService.RemoveItem(keysInInventory[i]);
                        if (keysInInventory[i] != null) {
                            keysInInventory[i].gameObject.SetActive(false);
                        }
                    }
                }

                _played = true;
                HasCollectedAllKeys = true;
                DeactivateNurse();

                if (_allKeysCollectedTimeline != null) {
                    Debug.Log("[KeyService] TryPrepareForLastKey: Playing 'all keys collected' cutscene timeline.");
                    try { _allKeysCollectedTimeline.Play(); } catch (System.Exception e) { Debug.LogError($"[KeyService] Error playing timeline: {e}"); }
                } else {
                    Debug.LogWarning("[KeyService] TryPrepareForLastKey: 'All Keys Collected Timeline' PlayableDirector is NOT assigned in the inspector!");
                }
                
                return true;
            }

            return false;
        }

        private void TriggerKeysCompleted(Map2KeyItem pickedKey) {
            Debug.Log($"[KeyService] TriggerKeysCompleted: Initializing key collection completion sequence with key '{pickedKey.name}'.");
            
            if (_inventoryService == null) {
                Debug.LogWarning("[KeyService] TriggerKeysCompleted: Inventory service is null, skipping inventory cleanup.");
            } else {
                var items = _inventoryService.GetItems();
                if (items != null) {
                    // Limpa as chaves anteriores, mantendo apenas a chave gatilho recém-adquirida
                    List<Map2KeyItem> keysToRemove = new List<Map2KeyItem>();
                    for (int i = 0; i < items.Count; i++) {
                        if (items[i] is Map2KeyItem keyItem && keyItem != pickedKey) {
                            keysToRemove.Add(keyItem);
                        }
                    }

                    Debug.Log($"[KeyService] TriggerKeysCompleted: Found {keysToRemove.Count} auxiliary keys to clean up from inventory.");
                    for (int i = 0; i < keysToRemove.Count; i++) {
                        Map2KeyItem keyToRemove = keysToRemove[i];
                        Debug.Log($"[KeyService] TriggerKeysCompleted: Removing and disabling auxiliary key '{keyToRemove.name}' from inventory.");
                        _inventoryService.RemoveItem(keyToRemove);
                        if (keyToRemove != null) {
                            keyToRemove.gameObject.SetActive(false);
                        }
                    }
                }
            }

            _played = true;
            HasCollectedAllKeys = true;
            DeactivateNurse();

            if (_allKeysCollectedTimeline != null) {
                Debug.Log("[KeyService] TriggerKeysCompleted: Playing 'all keys collected' cutscene timeline.");
                try { _allKeysCollectedTimeline.Play(); } catch (System.Exception e) { Debug.LogError($"[KeyService] Error playing timeline: {e}"); }
            } else {
                Debug.LogWarning("[KeyService] TriggerKeysCompleted: 'All Keys Collected Timeline' PlayableDirector is NOT assigned in the inspector!");
            }
        }

        private void DeactivateNurse() {
            var nurses = FindObjectsOfType<Nurse>();
            foreach (var nurse in nurses) {
                if (nurse != null) {
                    nurse.gameObject.SetActive(false);
                }
            }
        }

        public void CheatSetKeysCollected() {
            _played = true;
            HasCollectedAllKeys = true;
            DeactivateNurse();

            if (_inventoryService != null) {
                // Encontra a chave gatilho na cena
                Map2KeyItem triggerKey = null;
                var allKeys = FindObjectsOfType<Map2KeyItem>(true);
                foreach (var key in allKeys) {
                    if (key != null && key.KeyDefinition == _triggerKeyDefinition) {
                        triggerKey = key;
                        break;
                    }
                }

                if (triggerKey != null) {
                    // Limpa as outras chaves do inventário
                    var items = _inventoryService.GetItems();
                    if (items != null) {
                        List<Map2KeyItem> keysInInventory = new List<Map2KeyItem>();
                        for (int i = 0; i < items.Count; i++) {
                            if (items[i] is Map2KeyItem keyItem && keyItem != triggerKey) {
                                keysInInventory.Add(keyItem);
                            }
                        }
                        for (int i = 0; i < keysInInventory.Count; i++) {
                            _inventoryService.RemoveItem(keysInInventory[i]);
                            if (keysInInventory[i] != null) {
                                keysInInventory[i].gameObject.SetActive(false);
                            }
                        }
                    }

                    // Garante que o jogador tem a chave final no inventário
                    bool alreadyHasTriggerKey = false;
                    var currentItems = _inventoryService.GetItems();
                    if (currentItems != null) {
                        foreach (var item in currentItems) {
                            if (item == triggerKey) {
                                alreadyHasTriggerKey = true;
                                break;
                            }
                        }
                    }

                    if (!alreadyHasTriggerKey) {
                        bool added = _inventoryService.AddItem(triggerKey);
                        if (added) {
                            _eventBus?.Publish(new ItemPickedUpEvent(triggerKey.Id, triggerKey.gameObject));
                            triggerKey.Interact();
                        }
                    }
                }
            }

            Debug.Log("<color=cyan>[CHEAT]</color> Keys marked as collected. Nurse deactivated. No timeline played.");
        }

        private void SaveMap2Progress() {
            var saveService = ServiceLocator.Get<ISaveService>();
            if (saveService == null) return;

            SaveData saveData = saveService.LoadFromSlot("default") ?? new SaveData();

            saveData.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            saveData.CurrentMissionIndex = -1; // Sem missão ativa no Mapa 2

            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj == null) {
                var allPlayers = Resources.FindObjectsOfTypeAll<PlayerController>();
                foreach (var p in allPlayers) {
                    if (p != null && p.gameObject.scene.isLoaded) {
                        playerObj = p.gameObject;
                        break;
                    }
                }
            }

            if (playerObj != null) {
                saveData.PlayerPosition = new Vector3Data(playerObj.transform.position);
                saveData.PlayerRotation = new QuaternionData(playerObj.transform.rotation);
                saveData.HasPlayerPosition = true;

                var playerCamera = playerObj.GetComponentInChildren<PlayerCamera>();
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

            saveService.SaveToSlot("default", saveData);
            Debug.Log("[KeyService] Autosave concluído com sucesso no Mapa 2!");
        }
    }
}
