using System.Collections;
using FifthSemester.Core.Services;
using UnityEngine;

namespace FifthSemester.Gameplay.Map2 {
    public class Map2HorrorAmbientPlayer : MonoBehaviour, IPauseable {
        [Header("Ambient Clips")]
        [SerializeField] private AudioClip[] _ambientClips;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float _minDelay = 8f;
        [SerializeField, Min(0f)] private float _maxDelay = 20f;
        [SerializeField] private bool _playOnStart = true;

        [Header("Playback")]
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 1f;
        [SerializeField] private bool _avoidImmediateRepeat = true;

        private Coroutine _ambientRoutine;
        private int _lastClipIndex = -1;
        private IAudioService _audioService;
        private IPauseService _pauseService;
        private bool _paused;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            ServiceLocator.TryGet<IAudioService>(out _audioService);
        }

        private void OnEnable() { _pauseService.Register(this); }
        public void OnPause() { _paused = true; }
        public void OnResume() { _paused = false; }

        private void Start() {
            if (_playOnStart) {
                StartAmbientLoop();
            }
        }

        public void StartAmbientLoop() {
            if (_ambientRoutine != null) {
                StopCoroutine(_ambientRoutine);
            }


            _ambientRoutine = StartCoroutine(AmbientRoutine());
        }

        public void StopAmbientLoop() {
            if (_ambientRoutine != null) {
                StopCoroutine(_ambientRoutine);
                _ambientRoutine = null;
            }
        }

        private IEnumerator AmbientRoutine() {
            while (true) {
                yield return WaitForNextPlayback();

                if (!TryGetRandomClip(out AudioClip clip)) {
                    yield break;
                }

                PlayClip(clip);
            }
        }

        private IEnumerator WaitForNextPlayback() {
            float delay = Random.Range(_minDelay, Mathf.Max(_minDelay, _maxDelay));
            while (delay > 0f) {
                if (!_paused) delay -= Time.deltaTime;
                yield return null;
            }
            while (_paused) yield return null;
        }

        private bool TryGetRandomClip(out AudioClip clip) {
            clip = null;

            if (_ambientClips == null || _ambientClips.Length == 0) {
                return false;
            }

            int index = Random.Range(0, _ambientClips.Length);
            if (_avoidImmediateRepeat && _ambientClips.Length > 1 && index == _lastClipIndex) {
                index = (index + 1) % _ambientClips.Length;
            }

            clip = _ambientClips[index];
            if (clip == null) {
                return false;
            }



            _lastClipIndex = index;
            return true;
        }

        private void PlayClip(AudioClip clip) {
            if (clip == null) {
                return;
            }

            if (_audioService == null) {
                ServiceLocator.TryGet<IAudioService>(out _audioService);
            }

            _audioService?.PlaySFX(clip, volume: _volume, spatialBlend: 0f);
        }

        private void OnValidate() {
            if (_maxDelay < _minDelay) {
                _maxDelay = _minDelay;
            }
        }

        private void OnDisable() {
            _pauseService.Unregister(this);
            StopAmbientLoop();
        }
    }
}
