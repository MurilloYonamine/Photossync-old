// Autor: Murillo Gomes Yonamine
// Data: 11/05/2026

using UnityEngine;
using UnityEngine.AI;
using FifthSemester.Framework.BehaviourTrees;
using FifthSemester.Core.Services;

namespace FifthSemester.Gameplay.Enemy {
    public class ActionStareAndPounce : Node {
        private const string NAV_AGENT_KEY = "NavAgent";
        private const string PLAYER_TARGET_KEY = "PlayerTarget";
        private const string LAND_POSITION_KEY = "SafeLightLandPosition";
        private const string ANIMATOR_KEY = "Animator";

        private readonly Blackboard _blackboard;
        private readonly ConditionLineOfSight _lineOfSight;
        private NavMeshAgent _agent;
        private Transform _target;
        private Animator _animator;
        private IAudioService _audioService;

        // --- Timers e Configurações ---
        private float _stareTimeRequired = 4f; // Tempo encarando
        private float _jumpUpAnimDuration = 2f; // Duração exata da animação dele subindo
        private float _timeInAir = 1.5f; // Tempo de suspense lá no teto
        private float _landAnimDuration = 2f; // Duração da animação dele caindo/aterrissando
        private float _postLandDelay = 1f; // Tempo que o jogador tem para reagir após o land

        [Header("Audio")]
        [SerializeField] private AudioClip _jumpSound;
        [SerializeField] private AudioClip _landSound;

        private enum PounceState { Staring, JumpingUp, HoveringInAir, Landing }
        private PounceState _currentState = PounceState.Staring;

        private float _currentTimer = 0f;
        private bool _attackCancelled;

        public ActionStareAndPounce(Blackboard blackboard, string name = "Stare And Pounce") : base(name, blackboard) {
            _blackboard = blackboard;
            _lineOfSight = new ConditionLineOfSight(blackboard);
            ServiceLocator.TryGet<IAudioService>(out _audioService);
        }

        public override Status Process() {
            if (_agent == null) _agent = _blackboard.GetData<NavMeshAgent>(NAV_AGENT_KEY);
            if (_target == null) _target = _blackboard.GetData<Transform>(PLAYER_TARGET_KEY);
            if (_animator == null) _animator = _blackboard.GetData<Animator>(ANIMATOR_KEY);

            if (_agent == null || _target == null) return Status.Failure;

            if (_currentState == PounceState.Staring && !CanAttackFromSafeLight()) {
                return Status.Failure;
            }
            if (_currentState != PounceState.Staring && !_blackboard.GetData<bool>("IsPlayerInSafeLight")) {
                _attackCancelled = true;
            }

            switch (_currentState) {
                case PounceState.Staring:
                    return ProcessStaring();
                case PounceState.JumpingUp:
                    return ProcessJumpingUp();
                case PounceState.HoveringInAir:
                    return ProcessHoveringInAir();
                case PounceState.Landing:
                    return ProcessLanding();
            }

            return Status.Running;
        }

        private Status ProcessStaring() {
            _agent.isStopped = true;

            Vector3 direction = (_target.position - _agent.transform.position).normalized;
            direction.y = 0;
            _agent.transform.rotation = Quaternion.Slerp(_agent.transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 5f);

            _currentTimer += Time.deltaTime;

            if (_currentTimer >= _stareTimeRequired) {
                _currentState = PounceState.JumpingUp;
                _agent.enabled = false; 

                if (_animator != null) _animator.SetTrigger("Jump");
                PlaySfx(_jumpSound);

                _currentTimer = 0f;
            }

            return Status.Running;
        }

        private Status ProcessJumpingUp() {
            _currentTimer += Time.deltaTime;

            if (_currentTimer >= _jumpUpAnimDuration) {
                _currentState = PounceState.HoveringInAir;
                _currentTimer = 0f;
            }
            return Status.Running;
        }

        private Status ProcessHoveringInAir() {
            _currentTimer += Time.deltaTime;

            if (_currentTimer >= _timeInAir) {
                _currentState = PounceState.Landing;
                _currentTimer = 0f;

                Vector3 landingTarget = _blackboard.HasKey(LAND_POSITION_KEY)
                    ? _blackboard.GetData<Vector3>(LAND_POSITION_KEY)
                    : _target.position;

                if (!NavMesh.SamplePosition(landingTarget, out NavMeshHit landingHit, 4f, NavMesh.AllAreas) &&
                    !NavMesh.SamplePosition(_agent.transform.position, out landingHit, 4f, NavMesh.AllAreas)) {
                    return Status.Failure;
                }

                _agent.transform.position = landingHit.position;
                _agent.enabled = true;
                if (!_agent.isOnNavMesh || !_agent.Warp(landingHit.position)) return Status.Failure;
                _agent.isStopped = true;
                _agent.ResetPath();

                Vector3 directionToPlayer = (_target.position - _agent.transform.position).normalized;
                directionToPlayer.y = 0;
                if (directionToPlayer != Vector3.zero) {
                    _agent.transform.rotation = Quaternion.LookRotation(directionToPlayer);
                }

                if (_animator != null) _animator.SetTrigger("Land");
                PlaySfx(_landSound);
            }
            return Status.Running;
        }

        private Status ProcessLanding() {
            _currentTimer += Time.deltaTime;

            // Wait for landing animation to finish
            if (_currentTimer < _landAnimDuration) {
                return Status.Running;
            }

            // After landing animation, give the player a short reaction window
            if (_currentTimer < _landAnimDuration + _postLandDelay) {
                return Status.Running;
            }

            if (_attackCancelled || !CanAttackFromSafeLight()) return Status.Failure;

            if (!_agent.isOnNavMesh) return Status.Failure;
            _agent.isStopped = false;
            _agent.ResetPath();
            _agent.SetDestination(_target.position);
            _blackboard.SetData("PounceRequiresSafeLight", true);
            return Status.Success;
        }

        public override void Reset() {
            base.Reset();
            _currentState = PounceState.Staring;
            _currentTimer = 0f;
            _attackCancelled = false;
            _blackboard.SetData("PounceRequiresSafeLight", false);
            // Re-enable agent if it was disabled during the jump
            if (_agent != null && !_agent.enabled) {
                _agent.enabled = true;
            }
        }

        private bool CanAttackFromSafeLight() {
            return _blackboard.GetData<bool>("IsPlayerInSafeLight") &&
                   _lineOfSight.Process() == Status.Success;
        }

        private void PlaySfx(AudioClip clip) {
            if (clip == null || _audioService == null) {
                return;
            }

            _audioService.PlaySFX(clip);
        }
    }
}
