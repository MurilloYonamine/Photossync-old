// Autor: Murillo Gomes Yonamine
// Data: 18/05/2026

using NUnit.Framework;
using UnityEngine;
using FifthSemester.Gameplay.Menu;
using FifthSemester.Core.Enums;
using FifthSemester.Core.Services;

namespace FifthSemester.Tests {
    public class SettingsTests {
        private static readonly string[] AudioKeys = {
            "Settings_MasterVolume", "Settings_MusicVolume", "Settings_SFXVolume", "Settings_AmbienceVolume"
        };
        private const string MigrationKey = "Settings_AudioVolumeMigratedTo100";
        private readonly bool[] _hadAudioKey = new bool[4];
        private readonly float[] _savedAudioValues = new float[4];
        private bool _hadMigrationKey;
        private int _savedMigrationValue;
        private bool _hadLanguageKey;
        private int _savedLanguage;
        private bool _hadInvertKey;
        private int _savedInvert;
        private SettingsService _settingsService;

        [SetUp]
        public void Setup() {
            for (int i = 0; i < AudioKeys.Length; i++) {
                _hadAudioKey[i] = PlayerPrefs.HasKey(AudioKeys[i]);
                if (_hadAudioKey[i]) _savedAudioValues[i] = PlayerPrefs.GetFloat(AudioKeys[i]);
                PlayerPrefs.DeleteKey(AudioKeys[i]);
            }
            _hadMigrationKey = PlayerPrefs.HasKey(MigrationKey);
            if (_hadMigrationKey) _savedMigrationValue = PlayerPrefs.GetInt(MigrationKey);
            _hadLanguageKey = PlayerPrefs.HasKey("Settings_Language");
            if (_hadLanguageKey) _savedLanguage = PlayerPrefs.GetInt("Settings_Language");
            _hadInvertKey = PlayerPrefs.HasKey("Settings_InvertY");
            if (_hadInvertKey) _savedInvert = PlayerPrefs.GetInt("Settings_InvertY");
            PlayerPrefs.DeleteKey(MigrationKey);
            PlayerPrefs.DeleteKey("Settings_Language");
            PlayerPrefs.DeleteKey("Settings_InvertY");
            _settingsService = new SettingsService();
        }

        [TearDown]
        public void TearDown() {
            for (int i = 0; i < AudioKeys.Length; i++) {
                if (_hadAudioKey[i]) PlayerPrefs.SetFloat(AudioKeys[i], _savedAudioValues[i]);
                else PlayerPrefs.DeleteKey(AudioKeys[i]);
            }
            if (_hadMigrationKey) PlayerPrefs.SetInt(MigrationKey, _savedMigrationValue);
            else PlayerPrefs.DeleteKey(MigrationKey);
            if (_hadLanguageKey) PlayerPrefs.SetInt("Settings_Language", _savedLanguage);
            else PlayerPrefs.DeleteKey("Settings_Language");
            if (_hadInvertKey) PlayerPrefs.SetInt("Settings_InvertY", _savedInvert);
            else PlayerPrefs.DeleteKey("Settings_InvertY");
            PlayerPrefs.Save();
        }

        [Test]
        public void AudioVolume_WithoutSavedValues_DefaultsToFullScale() {
            Assert.AreEqual(100f, _settingsService.MasterVolume);
            Assert.AreEqual(100f, _settingsService.MusicVolume);
            Assert.AreEqual(100f, _settingsService.SFXVolume);
            Assert.AreEqual(100f, _settingsService.AmbienceVolume);
        }

        [Test]
        public void AudioVolume_MigratesLegacyOneOnceAndPreservesOtherValues() {
            PlayerPrefs.DeleteKey(MigrationKey);
            PlayerPrefs.SetFloat(AudioKeys[0], 1f);
            PlayerPrefs.SetFloat(AudioKeys[1], 0f);
            PlayerPrefs.SetFloat(AudioKeys[2], 75f);
            PlayerPrefs.SetFloat(AudioKeys[3], 1f);

            var migrated = new SettingsService();
            Assert.AreEqual(100f, migrated.MasterVolume);
            Assert.AreEqual(0f, migrated.MusicVolume);
            Assert.AreEqual(75f, migrated.SFXVolume);
            Assert.AreEqual(100f, migrated.AmbienceVolume);

            migrated.MasterVolume = 1f;
            Assert.AreEqual(1f, new SettingsService().MasterVolume);
        }

        [Test]
        public void MasterVolume_SetAndGet_ReturnsCorrectValue() {
            float testValue = 75f;

            _settingsService.MasterVolume = testValue;

            Assert.AreEqual(75f, _settingsService.MasterVolume, 0.01f);
        }

        [Test]
        public void Language_Change_UpdatesPlayerPrefs() {
            _settingsService.Language = Language.English;

            int savedValue = PlayerPrefs.GetInt("Settings_Language");
            Assert.AreEqual((int)Language.English, savedValue);
        }

        [Test]
        public void InvertYAxis_Toggle_PersistsCorrectly() {
            _settingsService.InvertYAxis = true;

            Assert.IsTrue(_settingsService.InvertYAxis);
            Assert.AreEqual(1, PlayerPrefs.GetInt("Settings_InvertY"));
        }
    }
}
