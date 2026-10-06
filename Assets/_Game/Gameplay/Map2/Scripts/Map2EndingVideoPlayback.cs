using System.Collections;
using FifthSemester.Core.Services;
using UnityEngine;
using UnityEngine.Video;

namespace FifthSemester.Gameplay.Map2 {
    internal static class Map2EndingVideoPlayback {
        private const float PREPARE_TIMEOUT = 15f;
        private const float FADE_TIMEOUT_PADDING = 5f;

        public static IEnumerator Play(VideoPlayer videoPlayer, float fadeDuration) {
            bool finished = false;
            bool failed = false;
            VideoPlayer.EventHandler onFinished = player => finished = true;
            VideoPlayer.ErrorEventHandler onError = (player, message) => {
                failed = true;
                Debug.LogError($"[Map2EndingVideoPlayback] Video playback failed: {message}");
            };

            videoPlayer.loopPointReached += onFinished;
            videoPlayer.errorReceived += onError;

            try {
                videoPlayer.Prepare();
                float prepareDeadline = Time.realtimeSinceStartup + PREPARE_TIMEOUT;
                while (!videoPlayer.isPrepared && !failed && Time.realtimeSinceStartup < prepareDeadline) {
                    yield return null;
                }

                if (failed) yield break;
                if (!videoPlayer.isPrepared) {
                    Debug.LogError("[Map2EndingVideoPlayback] Timed out preparing the ending video.");
                    yield break;
                }

                ServiceLocator.TryGet<IFadeService>(out var fadeService);
                if (fadeService != null) {
                    yield return Fade(fadeService, true, fadeDuration / 2f);
                }

                videoPlayer.Play();
                if (fadeService != null) {
                    yield return Fade(fadeService, false, fadeDuration / 2f);
                }

                float playbackTimeout = Mathf.Max(60f, (float)videoPlayer.length + 15f);
                float playbackDeadline = Time.realtimeSinceStartup + playbackTimeout;
                while (!finished && !failed && Time.realtimeSinceStartup < playbackDeadline) {
                    yield return null;
                }

                if (failed) yield break;
                if (!finished) {
                    Debug.LogError("[Map2EndingVideoPlayback] Timed out waiting for the ending video to finish.");
                    yield break;
                }

                if (fadeService != null) {
                    yield return Fade(fadeService, true, fadeDuration);
                }
            }
            finally {
                videoPlayer.loopPointReached -= onFinished;
                videoPlayer.errorReceived -= onError;
            }
        }

        private static IEnumerator Fade(IFadeService fadeService, bool toBlack, float duration) {
            bool completed = false;
            if (toBlack) fadeService.FadeOut(duration, () => completed = true);
            else fadeService.FadeIn(duration, () => completed = true);

            float deadline = Time.realtimeSinceStartup + duration + FADE_TIMEOUT_PADDING;
            while (!completed && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }

            if (!completed) {
                Debug.LogWarning("[Map2EndingVideoPlayback] Ending fade was interrupted or timed out.");
            }
        }
    }
}
