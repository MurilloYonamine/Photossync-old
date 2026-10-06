using UnityEngine;
using FifthSemester.Core.Services;

namespace FifthSemester.Gameplay.Map2 {
    public class Map2CheatController : MonoBehaviour, IPauseable {
        public static bool IsCheatActive { get; private set; }

        [Header("Cheat Settings")]
        [SerializeField] private KeyCode _cheatKey = KeyCode.Alpha9;
        private IPauseService _pauseService;
        private bool _paused;

        private void Awake() { _pauseService = ServiceLocator.Get<IPauseService>(); }
        private void OnEnable() { _pauseService.Register(this); }
        private void OnDisable() { _pauseService.Unregister(this); }
        public void OnPause() { _paused = true; }
        public void OnResume() { _paused = false; }

        private void Update() {
            if (_paused) return;
            if (Input.GetKeyDown(_cheatKey)) {
                TriggerCheat();
            }
        }

        private void TriggerCheat() {
            IsCheatActive = true;
            Debug.Log("<color=cyan>[CHEAT]</color> Ativando cheat para pular Map 2!");

            // 1. Resolver senhas
            var passwordController = FindObjectOfType<Map2PasswordController>();
            if (passwordController != null) {
                passwordController.CheatForceComplete();
                Debug.Log("<color=cyan>[CHEAT]</color> Senhas resolvidas com sucesso!");
            } else {
                Debug.LogWarning("[CHEAT] Map2PasswordController não encontrado na cena!");
            }

            // 2. Desativar enfermeira, limpar chaves anteriores e dar a chave final
            if (ServiceLocator.TryGet<IMap2KeyService>(out var keyService)) {
                keyService.CheatSetKeysCollected();
            } else {
                // Fallback por Find se o ServiceLocator não tiver registrado
                var keyServiceFallback = FindObjectOfType<KeyService>();
                if (keyServiceFallback != null) {
                    keyServiceFallback.CheatSetKeysCollected();
                } else {
                    Debug.LogWarning("[CHEAT] KeyService (IMap2KeyService) não encontrado!");
                }
            }
        }
    }
}
