using System.Collections;
using System.Collections.Generic;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using FifthSemester.Gameplay.Inventory;
using FifthSemester.Gameplay.Shared;
using TMPro;
using ThirdParty.QuickOutline;
using UnityEngine;
using UnityEngine.Video;

namespace FifthSemester.Gameplay.Map2 {
    [RequireComponent(typeof(Outline))]
    public class Map2GoodEndingDeliveryPoint : MonoBehaviour, IInteractable {
        private const string TAG = "<color=green>[GoodEndingDelivery]</color>";

        [Header("Configurações do Item")]
        [Tooltip("ID do item necessário para concluir o final bom (ex: gasoline_canister).")]
        [SerializeField] private string _requiredItemId = "gasoline_canister";

        [Header("Final Bom (Cutscene)")]
        [Tooltip("Prefab contendo a RawImage e o VideoPlayer para o Final Bom.")]
        [SerializeField] private GameObject _goodEndingPrefab;
        [SerializeField] private float _fadeDuration = 2f;

        [Header("Configurações Visuais e UI")]
        [SerializeField] private TextMeshPro _interactionPromptText;
        [SerializeField] private string _deliverPromptText = "Entregar Gasolina";
        [SerializeField] private string _completedPromptText = "Área Limpa";

        [Header("Áudio")]
        [SerializeField] private AudioClip _successSound;
        [SerializeField] private AudioClip _failureSound;

        private Outline _outline;
        private IInventoryService<Item> _inventoryService;
        private IAudioService _audioService;
        private Color _unlockedColor;
        private bool _isCompleted = false;

        public bool IsInteractable => !_isCompleted;
        public string Id => gameObject.name;

        private void Awake() {
            _outline = GetComponent<Outline>();
            if (_outline != null) {
                _outline.enabled = false;
            }
            _unlockedColor = new Color32(105, 255, 144, 255);
            UpdateInteractionPrompt();
        }

        private void Start() {
            ServiceLocator.TryGet<IInventoryService<Item>>(out _inventoryService);
            ServiceLocator.TryGet<IAudioService>(out _audioService);
            UpdateInteractionPrompt();
        }

        public void Highlight(bool value) {
            if (_outline != null) {
                _outline.enabled = value;
                _outline.OutlineColor = HasRequiredItem() ? _unlockedColor : Color.red;
            }

            if (_interactionPromptText != null) {
                _interactionPromptText.gameObject.SetActive(value);
                _interactionPromptText.color = HasRequiredItem() ? _unlockedColor : Color.red;
            }
        }

        public void Interact() {
            Debug.Log($"{TAG} Interact called. isCompleted={_isCompleted}");
            if (_isCompleted) {
                Debug.Log($"{TAG} Interaction ignored: already completed.");
                return;
            }

            Debug.Log($"{TAG} Attempting to consume required item: '{_requiredItemId}'");
            if (TryConsumeRequiredItem()) {
                _isCompleted = true;
                Debug.Log($"{TAG} Successfully consumed item '{_requiredItemId}'. Completing delivery point.");
                UpdateInteractionPrompt();
                PlaySound(_successSound);
                TriggerGoodEnding();
            }
            else {
                Debug.LogWarning($"{TAG} Interaction failed. Player does not possess the required item: '{_requiredItemId}'");
                PlaySound(_failureSound);
            }
        }

        public void StopInteract() {
            Debug.Log($"{TAG} StopInteract called.");
        }

        private bool HasRequiredItem() {
            if (_inventoryService == null) {
                Debug.LogWarning($"{TAG} HasRequiredItem check: Inventory service is null!");
                return false;
            }

            IReadOnlyList<Item> items = _inventoryService.GetItems();
            if (items == null) {
                Debug.LogWarning($"{TAG} HasRequiredItem check: Inventory item list is null!");
                return false;
            }

            Debug.Log($"{TAG} Checking inventory of size {items.Count} for required item ID: '{_requiredItemId}'");
            for (int i = 0; i < items.Count; i++) {
                if (items[i] != null) {
                    Debug.Log($"{TAG} Inventory slot {i}: ID='{items[i].Id}', Name='{items[i].name}'");
                    if (items[i].Id == _requiredItemId) {
                        Debug.Log($"{TAG} Match found for required item ID '{_requiredItemId}' at slot {i}!");
                        return true;
                    }
                }
            }

            Debug.Log($"{TAG} Required item ID '{_requiredItemId}' NOT found in inventory.");
            return false;
        }

        private bool TryConsumeRequiredItem() {
            if (_inventoryService == null) {
                Debug.LogWarning($"{TAG} TryConsumeRequiredItem: Inventory service is null!");
                return false;
            }

            IReadOnlyList<Item> items = _inventoryService.GetItems();
            if (items == null) {
                Debug.LogWarning($"{TAG} TryConsumeRequiredItem: Inventory item list is null!");
                return false;
            }

            for (int i = 0; i < items.Count; i++) {
                var item = items[i];
                if (item != null && item.Id == _requiredItemId) {
                    Debug.Log($"{TAG} TryConsumeRequiredItem: Removing item '{item.Id}' from inventory.");
                    _inventoryService.RemoveItem(item);
                    return true;
                }
            }

            return false;
        }

        private void UpdateInteractionPrompt() {
            if (_interactionPromptText == null) {
                Debug.LogWarning($"{TAG} UpdateInteractionPrompt: _interactionPromptText text reference is null!");
                return;
            }
            _interactionPromptText.text = _isCompleted ? _completedPromptText : _deliverPromptText;
            Debug.Log($"{TAG} Prompt text updated to: '{_interactionPromptText.text}'");
        }

        private void PlaySound(AudioClip clip) {
            if (clip == null) {
                Debug.LogWarning($"{TAG} PlaySound: Clip is null!");
                return;
            }
            if (_audioService == null) {
                Debug.LogWarning($"{TAG} PlaySound: Audio service is null!");
                return;
            }
            _audioService.PlaySFX(clip);
        }

        private void TriggerGoodEnding() {
            Debug.Log($"{TAG} TriggerGoodEnding: Initializing Good Ending sequence.");
            if (_goodEndingPrefab == null) {
                Debug.LogError($"{TAG} Good Ending Prefab is NOT assigned in the inspector!");
                LoadMainMenu();
                return;
            }

            if (ServiceLocator.TryGet<IGameStateService>(out var gameStateService)) {
                Debug.Log($"{TAG} TriggerGoodEnding: Changing GameState to Cutscene.");
                gameStateService.ChangeState(GameState.Cutscene);
            } else {
                Debug.LogWarning($"{TAG} TriggerGoodEnding: GameStateService not available!");
            }

            Debug.Log($"{TAG} TriggerGoodEnding: Instantiating Good Ending Canvas Prefab.");
            GameObject instance = Instantiate(_goodEndingPrefab);
            VideoPlayer videoPlayer = instance.GetComponentInChildren<VideoPlayer>();

            if (videoPlayer != null) {
                Debug.Log($"{TAG} TriggerGoodEnding: VideoPlayer component found. Starting playback coroutine.");
                StartCoroutine(PlayGoodEndingVideo(videoPlayer, instance));
            }
            else {
                Debug.LogError($"{TAG} TriggerGoodEnding: Instantiated prefab does NOT contain a VideoPlayer component!");
                LoadMainMenu();
            }
        }

        private IEnumerator PlayGoodEndingVideo(VideoPlayer videoPlayer, GameObject instance) {
            yield return Map2EndingVideoPlayback.Play(videoPlayer, _fadeDuration);
            Debug.Log($"{TAG} PlayGoodEndingVideo: Destroying cutscene instance and returning to main menu.");
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
