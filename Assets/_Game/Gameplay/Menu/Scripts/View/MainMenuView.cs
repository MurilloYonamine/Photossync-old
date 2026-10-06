using FifthSemester.Core.Enums;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using FifthSemester.Gameplay.Save;
using FifthSemester.Gameplay.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FifthSemester.Gameplay.Menu {

    public class MainMenuView : MenuViewBase {

        [Header("Buttons")]
        [SerializeField] private Button _newGameButton;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _creditsButton;
        [SerializeField] private Button _quitButton;

        [Header("Continue UI")]
        [SerializeField] private GameObject _continueButtonContainer;

        private IGameStateService _gameState;
        private const string DEFAULT_SLOT = "default";

        protected override MenuScreen MenuScreenType => MenuScreen.MainMenu;

        protected override void Start() {
            _gameState = ServiceLocator.Get<IGameStateService>();
            base.Start();

            _menuService.Show(MenuScreen.MainMenu);

            // Garante que o cursor do mouse esteja visível e destravado no Menu Principal
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            _newGameButton.onClick.AddListener(OnNewGame);
            _continueButton.onClick.AddListener(OnContinue);
            _settingsButton.onClick.AddListener(OnSettings);
            _creditsButton.onClick.AddListener(OnCredits);
            _quitButton.onClick.AddListener(OnQuit);

            UpdateContinueVisibility();
        }

        protected override void OnEnable() {
            base.OnEnable();
            UpdateContinueVisibility();

            // Garante que o cursor esteja liberado sempre que o painel for habilitado
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void OnPlay() {
            PlayMenuPrimarySfx();
            _menuService.Hide();
            _gameState.ChangeState(GameState.Gameplay);
            SceneManager.LoadScene("Game");
        }

        public void OnNewGame() {
            var saveService = ServiceLocator.Get<ISaveService>();
            if (saveService != null) {
                saveService.DeleteSlot(DEFAULT_SLOT);
            }

            // Discard the legacy password only when explicitly starting a new game.
            PlayerPrefs.DeleteKey("Map2PasswordState");
            PlayerPrefs.Save();

            var inventoryService = ServiceLocator.Get<IInventoryService<Item>>();
            inventoryService?.Clear();

            SaveLoader.ClearPendingSave();
            PlayMenuPrimarySfx();

            var transitioner = GetComponent<GameTransitioner>();
            if (transitioner != null) {
                _menuService.Hide();
                _gameState.ChangeState(GameState.Cutscene);
                transitioner.StartGameSequence();
            }
            else {
                _menuService.Hide();
                _gameState.ChangeState(GameState.Gameplay);
                SceneManager.LoadScene("Game");
            }
        }
        public void OnContinue() {
            ISaveService saveService = ServiceLocator.Get<ISaveService>();
            if (saveService == null || !saveService.SlotExists(DEFAULT_SLOT)) {
                OnNewGame();
                return;
            }

            SaveData saveData = saveService.LoadFromSlot(DEFAULT_SLOT);
            if (saveData == null) {
                OnNewGame();
                return;
            }

            PlayMenuPrimarySfx();
            SaveLoader.SetPendingSave(saveData);
            _menuService.Hide();
            _gameState.ChangeState(GameState.Gameplay);
            
            string sceneToLoad = string.IsNullOrEmpty(saveData.SceneName) ? "Game" : saveData.SceneName;
            SceneManager.LoadScene(sceneToLoad);
        }

        public void UpdateContinueVisibility() {
            if (_continueButtonContainer == null) return;

            ISaveService saveService = ServiceLocator.Get<ISaveService>();
            _continueButtonContainer.SetActive(saveService != null && saveService.SlotExists(DEFAULT_SLOT));
        }

        public void OnSettings() {
            PlayMenuSecondarySfx();
            _menuService.Show(MenuScreen.Settings);
        }

        public void OnCredits() {
            PlayMenuSecondarySfx();
            _menuService.Show(MenuScreen.Credits);
        }
        public void OnQuit() {
            PlayMenuSecondarySfx();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
