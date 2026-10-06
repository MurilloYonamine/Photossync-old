// Autor: Murillo Gomes Yonamine
// Data: 09/05/2026

using FifthSemester.Core.Audio;
using UnityEngine;

namespace FifthSemester.Core.Services {
    public class WhiteNoiseService : MonoBehaviour, IWhiteNoiseService, IPauseable {
        [Header("Audio")]
        [SerializeField] private AudioClip _whiteNoiseClip;

        [SerializeField, Range(0f, 1f)]
        private float _maxVolume = 0.5f;

        [Header("Visual")]
        [SerializeField, Range(0f, 1f)]
        private float _maxOpacity = 0.4f;

        [Header("Behaviour")]
        [SerializeField]
        private float _fadeSpeed = 3f;

        private IAudioService _audioService;
        private IPauseService _pauseService;
        private bool _paused;

        private AudioTrack _track;

        private float _currentIntensity;
        private float _requestedIntensity;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            ServiceLocator.Register<IWhiteNoiseService>(this);

            _audioService = ServiceLocator.Get<IAudioService>();

            Shader.SetGlobalFloat("_NoiseOpacity", 0f);
        }

        private void OnEnable() { _pauseService.Register(this); }
        private void OnDisable() { _pauseService.Unregister(this); }

        public void OnPause() {
            _paused = true;
            _currentIntensity = 0f;
            _requestedIntensity = 0f;
            UpdateShader();
            StopAudio();
        }

        public void OnResume() { _paused = false; }

        private void OnDestroy() {
            ServiceLocator.Unregister<IWhiteNoiseService>();

            Shader.SetGlobalFloat("_NoiseOpacity", 0f);
        }

        private void Update() {
            if (_paused) return;
            _currentIntensity = Mathf.MoveTowards(
                _currentIntensity,
                _requestedIntensity,
                Time.deltaTime * _fadeSpeed
            );

            UpdateShader();
            UpdateAudio();

            _requestedIntensity = 0f;
        }

        public void RequestIntensity(float intensity) {
            if (_paused) return;
            if (intensity > _requestedIntensity) {
                _requestedIntensity = intensity;
            }
        }

        public void ResetIntensity() {
            _requestedIntensity = 0f;
        }

        private void UpdateShader() {
            Shader.SetGlobalFloat(
                "_NoiseOpacity",
                _currentIntensity * _maxOpacity
            );
        }

        private void UpdateAudio() {
            if (_currentIntensity <= 0.01f) {
                StopAudio();
                return;
            }

            if (_track == null) {
                _track = _audioService.PlayAmbience(
                    _whiteNoiseClip,
                    loop: true,
                    startingVolume: 0f,
                    volumeCap: _maxVolume
                );
            }

            _track.Volume = _currentIntensity * _maxVolume;
        }

        private void StopAudio() {
            if (_track == null) {
                return;
            }

            _audioService.StopAmbience(_whiteNoiseClip);

            _track = null;
        }
    }
}
