using FifthSemester.Core.Enums;
using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Features.Localization;
using UnityEngine;

namespace FifthSemester.Gameplay.Menu {
    public class SettingsService : ISettingsService {
        private const string TAG = "<color=yellow><b>[SettingsService]</b></color>";
        private const string AUDIO_VOLUME_MIGRATION_KEY = "Settings_AudioVolumeMigratedTo100";
        private const string MASTER_VOLUME_KEY = "Settings_MasterVolume";
        private const string MUSIC_VOLUME_KEY = "Settings_MusicVolume";
        private const string SFX_VOLUME_KEY = "Settings_SFXVolume";
        private const string AMBIENCE_VOLUME_KEY = "Settings_AmbienceVolume";

        private bool GetBool(string key, bool defaultValue = false) {
            return PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) == 1;
        }

        private void SetBool(string key, bool value) {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public SettingsService() {
            MigrateAudioVolumes();
            ApplyStartupScreenSettings();
        }

        private void MigrateAudioVolumes() {
            if (PlayerPrefs.HasKey(AUDIO_VOLUME_MIGRATION_KEY)) return;

            MigrateAudioVolume(MASTER_VOLUME_KEY);
            MigrateAudioVolume(MUSIC_VOLUME_KEY);
            MigrateAudioVolume(SFX_VOLUME_KEY);
            MigrateAudioVolume(AMBIENCE_VOLUME_KEY);

            PlayerPrefs.SetInt(AUDIO_VOLUME_MIGRATION_KEY, 1);
            PlayerPrefs.Save();
        }

        private void MigrateAudioVolume(string key) {
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetFloat(key) == 1f) {
                PlayerPrefs.SetFloat(key, 100f);
            }
        }

        private void ApplyStartupScreenSettings() {
            Application.targetFrameRate = FrameRate;

            if (ResolutionIndex >= 0 && ResolutionIndex < AvailableResolutions.Length) {
                Vector2Int resolution = AvailableResolutions[ResolutionIndex];
                Screen.SetResolution(resolution.x, resolution.y, IsFullscreen);
            }
            else {
                Screen.fullScreen = IsFullscreen;
            }
        }

        // ====== Audio ======
        public float MasterVolume {
            get => PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 100f);
            set { PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, value); PlayerPrefs.Save(); }
        }
        public float MusicVolume {
            get => PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 100f);
            set { PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, value); PlayerPrefs.Save(); }
        }
        public float SFXVolume {
            get => PlayerPrefs.GetFloat(SFX_VOLUME_KEY, 100f);
            set { PlayerPrefs.SetFloat(SFX_VOLUME_KEY, value); PlayerPrefs.Save(); }
        }
        public float AmbienceVolume {
            get => PlayerPrefs.GetFloat(AMBIENCE_VOLUME_KEY, 100f);
            set { PlayerPrefs.SetFloat(AMBIENCE_VOLUME_KEY, value); PlayerPrefs.Save(); }
        }
        public bool ForceMonoAudio {
            get => GetBool("Settings_ForceMonoAudio", false);
            set => SetBool("Settings_ForceMonoAudio", value);
        }

        // ====== PSX Shaders ======
        public bool BarrelDistortion {
            get => GetBool("Settings_BarrelDistortion", true);
            set => SetBool("Settings_BarrelDistortion", value);
        }
        public bool Dithering {
            get => GetBool("Settings_Dithering", true);
            set => SetBool("Settings_Dithering", value);
        }
        public bool Pixelation {
            get => GetBool("Settings_Pixelation", true);
            set => SetBool("Settings_Pixelation", value);
        }
        public bool RollingBands {
            get => GetBool("Settings_RollingBands", true);
            set => SetBool("Settings_RollingBands", value);
        }
        public bool Scanlines {
            get => GetBool("Settings_Scanlines", true);
            set => SetBool("Settings_Scanlines", value);
        }
        public bool VHSEffect {
            get => GetBool("Settings_VHSEffect", true);
            set => SetBool("Settings_VHSEffect", value);
        }

        // ===== Screen & Window ======
        public int FrameRate {
            get => PlayerPrefs.GetInt("Settings_FrameRate", 24);
            set {
                PlayerPrefs.SetInt("Settings_FrameRate", value);
                Application.targetFrameRate = value;
                PlayerPrefs.Save();
            }
        }
        public bool IsFullscreen {
            get => GetBool("Settings_Fullscreen", true);
            set {
                SetBool("Settings_Fullscreen", value);
                Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                Screen.fullScreen = value;
            }
        }
        public int ResolutionIndex {
            get => PlayerPrefs.GetInt("Settings_ResolutionIndex", AvailableResolutions.Length > 0 ? AvailableResolutions.Length - 1 : 0);
            set {
                PlayerPrefs.SetInt("Settings_ResolutionIndex", value);
                if (value >= 0 && value < AvailableResolutions.Length) {
                    Vector2Int res = AvailableResolutions[value];
                    Screen.SetResolution(res.x, res.y, IsFullscreen); // Aplica a resolução dinamicamente
                }
                PlayerPrefs.Save();
            }
        }
        public Vector2Int[] AvailableResolutions { get; private set; } = new Vector2Int[] {
            new Vector2Int(320, 240),
            new Vector2Int(640, 480),
            new Vector2Int(800, 600),
        };

        // ===== Gameplay ======
        public Language Language {
            get => (Language)PlayerPrefs.GetInt("Settings_Language");
            set {
                PlayerPrefs.SetInt("Settings_Language", (int)value);
                PlayerPrefs.Save();

                var localization = ServiceLocator.Get<ILocalizationService>();
                localization?.SetLanguage(value);

                var eventBus = ServiceLocator.Get<IEventBus>();
                eventBus?.Publish(new LanguageChangedEvent(value));
            }
        }
        public bool InvertYAxis {
            get => GetBool("Settings_InvertY", false);
            set => SetBool("Settings_InvertY", value);
        }
        public float Sensibility {
            get => Mathf.Clamp(PlayerPrefs.GetFloat("Settings_Sensibility", 1f), 0f, 2f);
            set { PlayerPrefs.SetFloat("Settings_Sensibility", Mathf.Clamp(value, 0f, 2f)); PlayerPrefs.Save(); }
        }
    }
}
