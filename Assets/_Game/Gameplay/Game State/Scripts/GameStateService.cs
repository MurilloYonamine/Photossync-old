using UnityEngine;
using FifthSemester.Core.Services;
using FifthSemester.Core.Events;
using FifthSemester.Core.States;
using FifthSemester.Core.Input;

namespace FifthSemester.Gameplay {
    public class GameStateService : MonoBehaviour, IGameStateService {
        private const string TAG = "<color=yellow><b>[GameStateService]</b></color>";
        public GameState CurrentState { get; set; } = GameState.Gameplay;
        private GameState _previousState;

        private IEventBus _eventBus;
        private IInputService _inputService;

        private void Awake() {
            ServiceLocator.Register<IGameStateService>(this);
            _eventBus = ServiceLocator.Get<IEventBus>();
            _inputService = ServiceLocator.Get<IInputService>();
        }

        private void Start() {
            Time.timeScale = 1f;
            CurrentState = GameState.Gameplay;

            if (_eventBus == null) return;

            _eventBus.Subscribe<DialogueStartedEvent>(OnDialogueStarted);
            _eventBus.Subscribe<DialogueEndedEvent>(OnDialogueEnded);
            _eventBus.Subscribe<PauseToggleRequestedEvent>(OnPauseToggled);

            // Força a publicação do estado inicial para sincronizar serviços persistentes (como InputService)
            _eventBus.Publish(new GameStateChangedEvent(GameState.Gameplay, GameState.Gameplay));
        }

        public void ChangeState(GameState newState) {
            if (CurrentState == newState) return;

            _previousState = CurrentState;
            CurrentState = newState;
            Time.timeScale = 1f;

            if (CurrentState == GameState.Paused) {
                bool pauseRequestedByGamepad = _inputService != null && _inputService.LastPauseWasGamepad;
                Cursor.visible = !pauseRequestedByGamepad;
                Cursor.lockState = pauseRequestedByGamepad ? CursorLockMode.Locked : CursorLockMode.None;
            }
            else {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            Debug.Log($"{TAG} Mudou de {_previousState} para {CurrentState}");
            _eventBus.Publish(new GameStateChangedEvent(_previousState, CurrentState));
        }

        // ============ REAGINDO AOS EVENTOS ============

        private void OnDialogueStarted(DialogueStartedEvent evt) {
            if (CurrentState == GameState.Cutscene) return;
            ChangeState(GameState.Dialogue);
        }

        private void OnDialogueEnded(DialogueEndedEvent evt) {
            if (CurrentState == GameState.Cutscene) return;
            ChangeState(GameState.Gameplay);
        }

        private void OnPauseToggled(PauseToggleRequestedEvent evt) {
            if (CurrentState == GameState.Paused) {
                ChangeState(GameState.Gameplay);
            }
            else if (CurrentState == GameState.Gameplay) {
                ChangeState(GameState.Paused);
            }
        }

        private void OnDestroy() {
            if (_eventBus != null) {
                _eventBus.Unsubscribe<DialogueStartedEvent>(OnDialogueStarted);
                _eventBus.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
                _eventBus.Unsubscribe<PauseToggleRequestedEvent>(OnPauseToggled);
            }
        }
    }
}
