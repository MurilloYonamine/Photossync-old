using System.Collections.Generic;
using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;

namespace FifthSemester.Gameplay {
    public class PauseService : IPauseService {
        private const string TAG = "<color=yellow><b>[PauseService]</b></color>";
        private readonly HashSet<IPauseable> _pauseables = new();
        private readonly List<IPauseable> _snapshot = new();

        public bool IsPaused { get; private set; }

        public PauseService(IEventBus eventBus) {
            eventBus.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        }

        public void Register(IPauseable pauseable) {
            if (_pauseables.Add(pauseable) && IsPaused) pauseable.OnPause();
        }

        public void Unregister(IPauseable pauseable) {
            if (_pauseables.Remove(pauseable) && IsPaused) pauseable.OnResume();
        }

        public void PauseGame() {
            if (ServiceLocator.TryGet<IGameStateService>(out var state) && state.CurrentState == GameState.Gameplay) {
                state.ChangeState(GameState.Paused);
            }
        }

        public void ResumeGame() {
            if (ServiceLocator.TryGet<IGameStateService>(out var state) && state.CurrentState == GameState.Paused) {
                state.ChangeState(GameState.Gameplay);
            }
        }

        public void TogglePause() {
            if (IsPaused) ResumeGame();
            else PauseGame();
        }

        private void OnGameStateChanged(GameStateChangedEvent evt) {
            bool shouldPause = evt.CurrentState == GameState.Paused;
            if (IsPaused == shouldPause) return;
            IsPaused = shouldPause;
            _snapshot.Clear();
            foreach (IPauseable pauseable in _pauseables) _snapshot.Add(pauseable);
            for (int i = 0; i < _snapshot.Count; i++) {
                IPauseable pauseable = _snapshot[i];
                if (!_pauseables.Contains(pauseable)) continue;
                if (shouldPause) pauseable.OnPause();
                else pauseable.OnResume();
            }
            _snapshot.Clear();
        }
    }
}
