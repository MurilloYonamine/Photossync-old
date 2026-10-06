namespace FifthSemester.Core.Services {
    public interface IPauseable {
        void OnPause();
        void OnResume();
    }

    public interface IPauseService {
        void PauseGame();
        void ResumeGame();
        void TogglePause();
        bool IsPaused { get; }
        void Register(IPauseable pauseable);
        void Unregister(IPauseable pauseable);
    }
}
