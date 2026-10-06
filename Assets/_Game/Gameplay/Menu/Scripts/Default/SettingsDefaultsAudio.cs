using UnityEngine;

namespace FifthSemester.Gameplay.Menu {
    [CreateAssetMenu(fileName = "SettingsDefaultsAudio", menuName = "Settings/Defaults/Audio")]
    public class SettingsDefaultsAudio : ScriptableObject {
        [Header("Audio")]
        public float MasterVolume = 100f;
        public float MusicVolume = 100f;
        public float SFXVolume = 100f;
        public float AmbienceVolume = 100f;
        public bool ForceMonoAudio = false;
    }
}
