// Autor: Murillo Gomes Yonamine
// Data: 28/04/2026

using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;
using FifthSemester.Framework.BehaviourTrees;
using UnityEngine.SceneManagement;

namespace FifthSemester.Gameplay.Enemy {
    public class ActionPlayJumpscare : Node {
        private const float MIN_TRIGGER_DISTANCE = 0.25f;
        private const string NAV_AGENT_KEY = "NavAgent";
        private const string JUMPSCARE_DIRECTOR_KEY = "JumpscareDirector";
        private const string PLAYER_TARGET_KEY = "PlayerTarget";
        private const string CUTSCENE_ACTIVE_KEY = "CutsceneActive";
        private const string MAIN_MENU_SCENE_NAME = "MainMenu";

        private readonly Blackboard _blackboard;
        private readonly ConditionLineOfSight _lineOfSight;
        private NavMeshAgent _agent;
        private PlayableDirector _director;
        private Transform _target;

        private bool _started = false;
        private bool _finished = false;

        private IGameStateService _gameStateService;

        public ActionPlayJumpscare(Blackboard blackboard, string name = "PlayJumpscare") : base(name, blackboard) {
            _blackboard = blackboard;
            _lineOfSight = new ConditionLineOfSight(blackboard);
            _gameStateService = ServiceLocator.Get<IGameStateService>();
        }

        public override Status Process() {
            CacheReferences();

            if (_agent == null || _director == null || _target == null) {
                return Status.Failure;
            }

            if (!_started) {
                if (_blackboard.GetData<bool>("PounceRequiresSafeLight") &&
                    (!_blackboard.GetData<bool>("IsPlayerInSafeLight") || _lineOfSight.Process() != Status.Success)) {
                    return Status.Failure;
                }
                if (CanStartJumpscare()) {
                    StartJumpscare();
                    return Status.Running;
                }

                // Se o jumpscare não começou e a distância aumentou (o jogador recuou/fugiu ou congelou a Nurse),
                // falha este nó para que o Behavior Tree retorne para o estado de perseguição ativa (ActionChase)
                // e continue seguindo o jogador, em vez de ficar travada infinitamente.
                return Status.Failure;
            }

            // Fallback robusto para quando a Timeline chega ao fim (evita travar na tela caso o Wrap Mode esteja em Hold)
            if (_started && !_finished && _director.duration > 0f && _director.time >= _director.duration - 0.05f) {
                _finished = true;
                SceneManager.LoadScene(MAIN_MENU_SCENE_NAME);
            }

            if (_finished) {
                FinalizeJumpscare();
                return Status.Success;
            }

            return Status.Running;
        }

        private void CacheReferences() {
            _agent ??= _blackboard.GetData<NavMeshAgent>(NAV_AGENT_KEY);
            _director ??= _blackboard.GetData<PlayableDirector>(JUMPSCARE_DIRECTOR_KEY);
            _target ??= _blackboard.GetData<Transform>(PLAYER_TARGET_KEY);
        }

        private bool CanStartJumpscare() {
            if (_agent == null || _target == null) return false;

            // If the agent has a direct destination near the player, allow jumpscare when close
            float triggerDistance = Mathf.Max(MIN_TRIGGER_DISTANCE, _agent.stoppingDistance);

            // If agent is still calculating a path do not start yet
            if (_agent.pathPending) {
                return false;
            }

            // Prefer NavMeshAgent remainingDistance when there's an active path
            if (_agent.hasPath) {
                return _agent.remainingDistance <= triggerDistance;
            }

            // If there's no path (e.g. agent was teleported on landing), fallback to direct distance check
            float directDistance = Vector3.Distance(_agent.transform.position, _target.position);
            return directDistance <= triggerDistance;
        }

        private void StartJumpscare() {
            if (_agent.isOnNavMesh) {
                _agent.isStopped = true;
                _agent.ResetPath();
            }

            _director.stopped += OnDirectorStopped;
            _gameStateService?.ChangeState(GameState.Cutscene);
            _blackboard.SetData(CUTSCENE_ACTIVE_KEY, true);
            _director.Play();
            _started = true;
        }

        private void FinalizeJumpscare() {
            _director.stopped -= OnDirectorStopped;
            _blackboard.SetData(CUTSCENE_ACTIVE_KEY, false);
            _gameStateService?.ChangeState(GameState.Gameplay);
        }

        private void OnDirectorStopped(PlayableDirector pd) {
            if (_director != null && pd != _director) {
                return;
            }

            _finished = true;
            SceneManager.LoadScene(MAIN_MENU_SCENE_NAME);
        }

        public override void Reset() {
            base.Reset();
            if (_director != null) _director.stopped -= OnDirectorStopped;
            if (_blackboard != null) {
                _blackboard.SetData(CUTSCENE_ACTIVE_KEY, false);
            }
            _started = false;
            _finished = false;
        }
    }
}

