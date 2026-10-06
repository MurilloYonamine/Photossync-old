using UnityEngine;
using UnityEngine.AI;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;

namespace FifthSemester.Gameplay.NPC {
    [RequireComponent(typeof(NavMeshAgent))]
    public class NPCMovement : MonoBehaviour, IPauseable {
        [SerializeField] private float walkRadius = 20f;
        [SerializeField] private float minWaitTime = 2f;
        [SerializeField] private float maxWaitTime = 5f;

        private NavMeshAgent _agent;
        private Animator _animator;
        private IGameStateService _gameStateService;
        private IPauseService _pauseService;
        private bool _paused;
        private bool _agentWasStopped;
        private float _animatorSpeedBeforePause;
        private float _waitTimer;
        private bool _waiting;

        private readonly int _speedParameter = Animator.StringToHash("Speed");

        [SerializeField] private float _lookAtPlayerRange = 10f; 
        private Transform _playerTransform; 

        private bool _isLookingAtPlayer;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable() { _pauseService.Register(this); }
        private void OnDisable() { _pauseService.Unregister(this); }

        private void Start() {
            _gameStateService = ServiceLocator.Get<IGameStateService>();
            if (!_paused) GoToRandomPoint();

            GameObject player = GameObject.FindWithTag("Player");
            _playerTransform = player.transform;
        }

        private void Update() {
            if (_paused || _gameStateService == null || _gameStateService.CurrentState != GameState.Gameplay) {
                return;
            }

            UpdateAnimation();

            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance) {
                if (!_waiting) {
                    _waiting = true;
                    _waitTimer = Random.Range(minWaitTime, maxWaitTime);
                }

                _waitTimer -= Time.deltaTime;

                if (_waitTimer <= 0f) {
                    _waiting = false;
                    GoToRandomPoint();
                }
            }
        }

        public void OnPause() {
            if (_paused) return;
            _paused = true;
            if (_agent.enabled && _agent.isOnNavMesh) {
                _agentWasStopped = _agent.isStopped;
                _agent.isStopped = true;
            }
            if (_animator != null) {
                _animatorSpeedBeforePause = _animator.speed;
                _animator.speed = 0f;
            }
        }

        public void OnResume() {
            if (!_paused) return;
            _paused = false;
            if (_agent.enabled && _agent.isOnNavMesh) {
                _agent.isStopped = _agentWasStopped;
                if (!_agent.hasPath && !_waiting) GoToRandomPoint();
            }
            if (_animator != null) _animator.speed = _animatorSpeedBeforePause;
        }

        private void GoToRandomPoint() {
            Vector3 randomDirection = Random.insideUnitSphere * walkRadius;
            randomDirection += transform.position;

            NavMeshHit hit;

            if (NavMesh.SamplePosition(randomDirection, out hit, walkRadius, UnityEngine.AI.NavMesh.AllAreas)) {
                _agent.SetDestination(hit.position);
            }
        }
        private void UpdateAnimation() {
            if (_animator == null) return;

            float speed = _agent.velocity.magnitude;

            _animator.SetFloat(_speedParameter, speed);
        }
    }
}
