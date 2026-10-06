// Autor: Murillo Gomes Yonamine
// Data: 14/02/2026

using FifthSemester.Core.Input;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FifthSemester.Core.Events {
    public class InputService : IInputService, IDisposable {
        private GameInput _gameInput;
        public GameState CurrentGameState { get; set; } = GameState.Gameplay;
        public bool LastPauseWasGamepad { get; private set; }
        public bool LastLookWasGamepad { get; private set; }

        private bool _ignoreNextDialogueAdvance = false;
        private bool _isInventoryOpen = false;

        private InputAction _move;
        private InputAction _look;
        private InputAction _jump;
        private InputAction _crouch;
        private InputAction _sprint;
        private InputAction _interact;
        private InputAction _zoom;
        private InputAction _flash;
        private InputAction _inventoryNavigate;
        private InputAction _openPause;
        private InputAction _dialogueAdvance;
        private InputAction _skipCutscene;
        private InputAction _uiReturn;
        private InputAction _uiNext;
        private InputAction _uiPrevious;
        private InputAction _uiScroll;


        public InputService() {
            Initialize();
        }

        public void Enable() {
            _gameInput?.Player.Enable();
            _gameInput?.UI.Enable();
        }

        public void Disable() {
            _gameInput?.Disable();
        }

        private void Initialize() {
            if (_gameInput != null) return;

            _gameInput = new GameInput();

            _move = _gameInput.Player.Move;
            _look = _gameInput.Player.Look;
            _jump = _gameInput.Player.Jump;
            _crouch = _gameInput.Player.Crouch;
            _sprint = _gameInput.Player.Sprint;
            _interact = _gameInput.Player.Interact;
            _zoom = _gameInput.Player.Zoom;
            _flash = _gameInput.Player.Flash;
            _inventoryNavigate = _gameInput.Player.InventoryNavigate;
            _openPause = _gameInput.Player.OpenPause;
            _dialogueAdvance = _gameInput.UI.Interact;
            _skipCutscene = _gameInput.Player.SkipCutscene;
            _uiReturn = _gameInput.UI.Return;
            _uiReturn.AddBinding("<Gamepad>/start");
            _uiNext = _gameInput.UI.Next;
            _uiPrevious = _gameInput.UI.Previous;
            _uiScroll = _gameInput.UI.ScrollWheel;

            _dialogueAdvance.started += HandleDialogueAdvance;

            _move.performed += HandleMovement;
            _move.canceled += HandleMovement;
            _look.performed += HandleLook;
            _look.canceled += HandleLook;
            _jump.performed += HandleJump;
            _crouch.performed += HandleCrouch;
            _crouch.canceled += HandleCrouch;
            _sprint.performed += HandleSprint;
            _sprint.canceled += HandleSprint;
            _interact.started += HandleInteract;
            _zoom.performed += HandleZoom;
            _zoom.canceled += HandleZoom;
            _flash.performed += HandleFlash;
            _flash.canceled += HandleFlash;
            _inventoryNavigate.performed += HandleInventoryNavigation;
            _openPause.performed += HandleOpenPause;
            _skipCutscene.started += HandleSkipCutscene;
            _uiReturn.started += HandleUiReturn;
            _uiNext.started += HandleUiNext;
            _uiPrevious.started += HandleUiPrevious;
            _uiScroll.performed += HandleUiScroll;

            ServiceLocator.Get<IEventBus>()?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
            ServiceLocator.Get<IEventBus>()?.Subscribe<InventoryToggledEvent>(OnInventoryToggled);
        }

        private void PublishEvent<T>(T evtStruct) {
            var eventBus = ServiceLocator.Get<IEventBus>();
            eventBus?.Publish(evtStruct);
        }

        public void HandleMovement(InputAction.CallbackContext context) {

            if (context.performed) {
                if (CurrentGameState != GameState.Gameplay) return;
                PublishEvent(new MoveInputEvent(context.ReadValue<Vector2>()));
            }
            else if (context.canceled) {
                PublishEvent(new MoveInputEvent(Vector2.zero));
            }
        }

        public void HandleLook(InputAction.CallbackContext context) {
            if (context.performed) {
                LastLookWasGamepad = context.control != null && context.control.device is Gamepad;
                if (CurrentGameState != GameState.Gameplay) return;
                PublishEvent(new LookInputEvent(context.ReadValue<Vector2>()));
            }
            else if (context.canceled) {
                PublishEvent(new LookInputEvent(Vector2.zero));
            }

        }

        public void HandleJump(InputAction.CallbackContext context) {
            if (CurrentGameState != GameState.Gameplay) return;

            if (context.performed) {
                PublishEvent(new JumpInputEvent());
            }
        }

        public void HandleCrouch(InputAction.CallbackContext context) {

            if (context.performed) {
                if (CurrentGameState != GameState.Gameplay) return;
                PublishEvent(new CrouchInputEvent(true));
            }
            else if (context.canceled) {
                PublishEvent(new CrouchInputEvent(false));
            }
        }

        public void HandleSprint(InputAction.CallbackContext context) {
            if (context.performed) {
                if (CurrentGameState != GameState.Gameplay) return;
                PublishEvent(new SprintInputEvent(true));
            }
            else if (context.canceled) {
                PublishEvent(new SprintInputEvent(false));
            }
        }

        public void HandleInteract(InputAction.CallbackContext context) {
            PublishEvent(new InteractInputEvent());
        }

        public void HandleZoom(InputAction.CallbackContext context) {

            if (context.performed) {
                if (CurrentGameState != GameState.Gameplay) return;
                PublishEvent(new ZoomInputEvent(true));
            }
            else if (context.canceled) {
                PublishEvent(new ZoomInputEvent(false));
            }
        }

        public void HandleFlash(InputAction.CallbackContext context) {
            if (context.performed) {
                if (CurrentGameState != GameState.Gameplay) return;
                PublishEvent(new FlashlightInputEvent(true));
            }
            else if (context.canceled) {
                PublishEvent(new FlashlightInputEvent(false));
            }
        }

        public void HandleInventoryNavigation(InputAction.CallbackContext context) {
            if (CurrentGameState != GameState.Gameplay) return;

            float direction = context.ReadValue<float>(); 

            if (!_isInventoryOpen) {
                PublishEvent(new InventoryToggledEvent(true));
                return;
            }

            if (direction > 0)
                PublishEvent(new NextInputEvent());
            else if (direction < 0)
                PublishEvent(new PreviousInputEvent());
        }
        private void OnInventoryToggled(InventoryToggledEvent evt) {
            _isInventoryOpen = evt.IsOpen;
            ApplyActionMaps();
        }

        private void HandleUiReturn(InputAction.CallbackContext context) {
            if (_isInventoryOpen) {
                PublishEvent(new InventoryToggledEvent(false));
            }
            else if (CurrentGameState == GameState.Paused) {
                LastPauseWasGamepad = context.control != null && context.control.device is Gamepad;
                PublishEvent(new PauseToggleRequestedEvent());
            }
        }

        private void HandleUiNext(InputAction.CallbackContext context) {
            if (_isInventoryOpen) PublishEvent(new NextInputEvent());
        }

        private void HandleUiPrevious(InputAction.CallbackContext context) {
            if (_isInventoryOpen) PublishEvent(new PreviousInputEvent());
        }

        private void HandleUiScroll(InputAction.CallbackContext context) {
            if (!_isInventoryOpen) return;
            float direction = context.ReadValue<Vector2>().y;
            if (direction > 0f) PublishEvent(new NextInputEvent());
            else if (direction < 0f) PublishEvent(new PreviousInputEvent());
        }
        public void HandleOpenPause(InputAction.CallbackContext context) {
            if (context.performed) {
                LastPauseWasGamepad = context.control != null && context.control.device is Gamepad;
                PublishEvent(new PauseToggleRequestedEvent());
            }
        }

        private void HandleDialogueAdvance(InputAction.CallbackContext context) {
            if (!context.started) return;

            if (CurrentGameState != GameState.Dialogue && CurrentGameState != GameState.Cutscene) return;

            if (_ignoreNextDialogueAdvance) {
                _ignoreNextDialogueAdvance = false;
                return;
            }

            PublishEvent(new DialogueAdvanceRequestedEvent());
        }

        private void HandleSkipCutscene(InputAction.CallbackContext context) {
            if (!context.started) return;
            if (CurrentGameState != GameState.Cutscene) return;

            PublishEvent(new SkipCutsceneRequestedEvent());
        }

        public void OnGameStateChanged(GameStateChangedEvent evt) {
            CurrentGameState = evt.CurrentState;
            if (CurrentGameState != GameState.Gameplay || _isInventoryOpen) {
                PublishEvent(new MoveInputEvent(Vector2.zero));
                PublishEvent(new SprintInputEvent(false));
                PublishEvent(new CrouchInputEvent(false));
            }
            ApplyActionMaps();
        }

        private void ApplyActionMaps() {
            bool gameplayInput = CurrentGameState == GameState.Gameplay && !_isInventoryOpen;
            if (gameplayInput) {
                _gameInput.UI.Disable();
                _gameInput.Player.Enable();
            }
            else {
                _gameInput.Player.Disable();
                _gameInput.UI.Enable();
                if (CurrentGameState == GameState.Cutscene) _skipCutscene.Enable();
            }
        }
        public void Dispose() {
            ServiceLocator.Get<IEventBus>()?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
            ServiceLocator.Get<IEventBus>()?.Unsubscribe<InventoryToggledEvent>(OnInventoryToggled);

            if (_gameInput == null) return;

            _move.performed -= HandleMovement;
            _move.canceled -= HandleMovement;
            _look.performed -= HandleLook;
            _look.canceled -= HandleLook;
            _jump.performed -= HandleJump;
            _crouch.performed -= HandleCrouch;
            _crouch.canceled -= HandleCrouch;
            _sprint.performed -= HandleSprint;
            _sprint.canceled -= HandleSprint;
            _interact.started -= HandleInteract;
            _zoom.performed -= HandleZoom;
            _zoom.canceled -= HandleZoom;
            _flash.performed -= HandleFlash;
            _flash.canceled -= HandleFlash;
            _inventoryNavigate.performed -= HandleInventoryNavigation;
            _openPause.performed -= HandleOpenPause;
            _dialogueAdvance.started -= HandleDialogueAdvance;
            _skipCutscene.started -= HandleSkipCutscene;
            _uiReturn.started -= HandleUiReturn;
            _uiNext.started -= HandleUiNext;
            _uiPrevious.started -= HandleUiPrevious;
            _uiScroll.performed -= HandleUiScroll;
        }
    }
}
