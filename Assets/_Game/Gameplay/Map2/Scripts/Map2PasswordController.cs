using UnityEngine;

namespace FifthSemester.Gameplay.Map2 {
    public class Map2PasswordController : MonoBehaviour {
        private const string DEFAULT_SAVE_KEY = "Map2PasswordState";

        [Header("Configuração da Senha")]
        [SerializeField] private string _targetCode = "0311";
        [SerializeField] private string _saveKey = DEFAULT_SAVE_KEY;

        [Header("View")]
        [SerializeField] private Map2PasswordView _passwordView;

        [Header("Feedback")]
        [SerializeField] private string _successMessage = "Senha liberada";
        [SerializeField] private string _failureMessage = "Nada aconteceu";

        private Map2PasswordState _state;

        public bool IsComplete => _state != null && _state.IsComplete;

        private void Awake() {
            _state = Map2PasswordState.LoadOrCreate(_saveKey, _targetCode);
        }

        private void Start() {
            RefreshView();
        }

        public bool CanRevealDigit(int digit) {
            return _state != null && _state.CanRevealDigit(digit);
        }

        public bool TryRevealDigit(int digit) {
            if (_state == null) {
                return false;
            }

            bool revealed = _state.TryReveal(digit);
            if (!revealed) {
                SetFeedback(_failureMessage);
                return false;
            }

            _state.Save(_saveKey);
            RefreshView();

            if (_state.IsComplete) {
                SetFeedback(_successMessage);
            }

            return true;
        }

        public string GetCurrentDisplayCode() {
            return _state != null ? _state.GetDisplayCode() : string.Empty;
        }

        public void CheatForceComplete() {
            if (_state != null) {
                _state.ForceRevealAll();
                _state.Save(_saveKey);
                RefreshView();
            }
        }

        private void RefreshView() {
            if (_passwordView == null || _state == null) {
                return;
            }

            _passwordView.SetPassword(_state.GetDisplayCode());
            _passwordView.SetSolved(_state.IsComplete);
            _passwordView.Show();
        }

        private void SetFeedback(string message) {
            if (_passwordView == null) {
                return;
            }

            _passwordView.SetMessage(message);
            _passwordView.Show();
        }
    }
}
