using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using FifthSemester.Gameplay;
using NUnit.Framework;

namespace FifthSemester.Tests {
    public class PauseServiceTests {
        private EventBus _eventBus;
        private PauseService _pauseService;

        [SetUp]
        public void SetUp() {
            _eventBus = new EventBus();
            _pauseService = new PauseService(_eventBus);
        }

        [Test]
        public void PausingTwice_NotifiesRegisteredComponentsOnce() {
            var component = new TestPauseable();
            _pauseService.Register(component);
            _pauseService.Register(component);

            _eventBus.Publish(new GameStateChangedEvent(GameState.Gameplay, GameState.Paused));
            _eventBus.Publish(new GameStateChangedEvent(GameState.Gameplay, GameState.Paused));

            Assert.IsTrue(_pauseService.IsPaused);
            Assert.AreEqual(1, component.PauseCount);

            _eventBus.Publish(new GameStateChangedEvent(GameState.Paused, GameState.Gameplay));
            Assert.IsFalse(_pauseService.IsPaused);
            Assert.AreEqual(1, component.ResumeCount);
        }

        [Test]
        public void RegisterDuringPauseAndUnregister_RestoresComponent() {
            _eventBus.Publish(new GameStateChangedEvent(GameState.Gameplay, GameState.Paused));
            var component = new TestPauseable();

            _pauseService.Register(component);
            Assert.AreEqual(1, component.PauseCount);

            _pauseService.Unregister(component);
            Assert.AreEqual(1, component.ResumeCount);

            _eventBus.Publish(new GameStateChangedEvent(GameState.Paused, GameState.Gameplay));
            Assert.AreEqual(1, component.ResumeCount);
        }

        private class TestPauseable : IPauseable {
            public int PauseCount { get; private set; }
            public int ResumeCount { get; private set; }
            public void OnPause() { PauseCount++; }
            public void OnResume() { ResumeCount++; }
        }
    }
}
