// Autor: Murillo Gomes Yonamine
// Data: 09/05/2026

using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using FifthSemester.Framework.BehaviourTrees;
using FifthSemester.Gameplay.Inventory;
using FifthSemester.Gameplay.Map2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;

namespace FifthSemester.Gameplay.Enemy {
    [RequireComponent(typeof(NavMeshAgent))]
    public class Nurse : MonoBehaviour {
        private const string PLAYER_TARGET_KEY = "PlayerTarget";
        private const string NAV_AGENT_KEY = "NavAgent";
        private const string ANIMATOR_KEY = "Animator";
        private const string JUMPSCARE_DIRECTOR_KEY = "JumpscareDirector";
        private const string CUTSCENE_ACTIVE_KEY = "CutsceneActive";
        private const string IS_FROZEN_KEY = "IsFrozen";
        private const string IS_OBSERVED_KEY = "HasLineOfSight";

        [Header("References")]
        [SerializeField] private Transform _target;
        [SerializeField] private Camera _playerCamera;

        [Header("Game Design Rules")]
        [Tooltip("Ative para a Fase 3, onde ela não ataca, apenas bloqueia o caminho.")]
        [SerializeField] private bool _isPhase3Passive = false;

        [Header("Vision (Weeping Angel Settings)")]
        [SerializeField] private Transform _eyeTransform;
        [SerializeField, Range(0f, 120f)] private float _viewDistance = 30f;
        [SerializeField, Range(0f, 360f)] private float _fovAngle = 90f;
        [SerializeField] private LayerMask _obstacleMask;
        [SerializeField] private float _loseTargetDistance = 9999f;

        [Header("Speed & Rotation Settings")]
        [SerializeField, Range(0f, 15f)] private float _rotationSpeed = 8f;
        [SerializeField] private float _patrolWaitTime = 2f;

        [Header("Jumpscare Settings")]
        [SerializeField, Range(0.1f, 5f)] private float _jumpscareTriggerDistance = 1.25f;

        private BehaviourTree _tree;
            private Blackboard _blackboard;
        private NavMeshAgent _agent;
        private Animator _animator;
        private IGameStateService _gameStateService;
        private bool _paused;
        private bool _agentWasStopped;
        private float _animatorSpeedBeforePause;
        private IAudioService _audioService;

        private float _footstepTimer;
        private bool _lastObservedState = false;

        [Header("Timeline")]
        [SerializeField] private PlayableDirector _jumpscareDirector;

        [Header("Unlock Gate")]
        [SerializeField] private Map2KeyDefinitionSO _unlockKeyDefinition;

        private readonly int _speedHash = Animator.StringToHash("Speed");

        [Header("Observation Speed Settings")]
        [SerializeField] private float _normalSpeed = 2.5f;
        [SerializeField] private float _observedSpeed = 0.6f;
        [SerializeField] private float _speedLerp = 5f;
        private bool _isObserved = false;
        private bool _isLockedByKey = false;
        [SerializeField] private bool _isAggressive = false;
        private IInventoryService<Item> _inventoryService;
        private bool _isRetreating = false;

        [Header("Stuck Recovery Settings")]
        [SerializeField] private float _stuckDurationThreshold = 3.5f;
        private Vector3 _lastPosition;
        private float _stuckTimeAccumulator;

        public float TargetSpeed {
            get => _desiredSpeed;
            set => _desiredSpeed = value;
        }
        private float _desiredSpeed = 2.5f;

        private void Awake() {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();

            // Try to cache inventory service early so RefreshUnlockState (called in OnEnable)
            // has a chance to validate lock state before first Update.
            ServiceLocator.TryGet<IInventoryService<Item>>(out _inventoryService);

            if(_target == null)
                _target = GameObject.FindGameObjectWithTag("Player")?.transform;

            if(_playerCamera == null)
                _playerCamera = Camera.main;

            if(_animator == null)
                _animator = GetComponentInChildren<Animator>();

            _agent.speed = _normalSpeed;
            _agent.updateRotation = false;
            _agent.stoppingDistance = _jumpscareTriggerDistance;

            SetupBlackboard();
        }
        private void Start() {
            _gameStateService = ServiceLocator.Get<IGameStateService>();
            if (_playerCamera == null) _playerCamera = Camera.main;
            ServiceLocator.TryGet<IInventoryService<Item>>(out _inventoryService);
            ServiceLocator.TryGet<IAudioService>(out _audioService);
            BuildBehaviourTree();
            RefreshUnlockState();

            if (_agent != null) {
                _desiredSpeed = _agent.speed;
            }

            _lastPosition = transform.position;
            _stuckTimeAccumulator = 0f;
        }

        private void OnEnable() {
            IEventBus eventBus = ServiceLocator.Get<IEventBus>();
            eventBus?.Subscribe<InventoryItemAddedEvent>(OnInventoryItemAdded);
            RefreshUnlockState();
        }

        private void OnDisable() {
            IEventBus eventBus = ServiceLocator.Get<IEventBus>();
            eventBus?.Unsubscribe<InventoryItemAddedEvent>(OnInventoryItemAdded);
        }

        private void SetupBlackboard() {
            _blackboard = new Blackboard();
            _blackboard.SetData(PLAYER_TARGET_KEY, _target);
            _blackboard.SetData(NAV_AGENT_KEY, _agent);
            _blackboard.SetData(ANIMATOR_KEY, _animator);
            _blackboard.SetData(JUMPSCARE_DIRECTOR_KEY, _jumpscareDirector);
            _blackboard.SetData(CUTSCENE_ACTIVE_KEY, false);
            _blackboard.SetData(IS_FROZEN_KEY, false);
            _blackboard.SetData(IS_OBSERVED_KEY, false);
            _blackboard.SetData("IsPlayerInRoom", false);

            _blackboard.SetData("PatrolWaitTime", _patrolWaitTime);

            // Populate vision parameters for BT nodes
            _blackboard.SetData("EyeTransform", _eyeTransform);
            _blackboard.SetData("ViewDistance", _viewDistance);
            _blackboard.SetData("FovAngle", _fovAngle);
            _blackboard.SetData("ObstacleMask", _obstacleMask);
            _blackboard.SetData("LoseTargetDistance", _loseTargetDistance);
            _blackboard.SetData("IsAggressive", _isAggressive);
        }

        private void BuildBehaviourTree() {
            RebuildBehaviourTree(includeChase: _isAggressive);
        }

        private void RebuildBehaviourTree(bool includeChase) {
            var root = new Selector("NurseRootBehavior");

            if (includeChase && !_isPhase3Passive) {
                var chaseSequence = new Sequence("AggressiveChase");
                chaseSequence.AddChild(new ActionChase(_blackboard, "Chase Player"));
                chaseSequence.AddChild(new ActionPlayJumpscare(_blackboard, "Jumpscare"));

                root.AddChild(chaseSequence);
            }

            var patrolSequence = new Sequence("SearchPatrol");
            patrolSequence.AddChild(new ActionPatrol(_blackboard, "Patrol Waypoints"));

            root.AddChild(patrolSequence);

            _tree = new BehaviourTree("Nurse Behaviour Tree", root);
        }

        private void Update() {
            if (_gameStateService != null && _gameStateService.CurrentState == GameState.Paused) {
                PauseEnemy();
                return;
            }
            ResumeEnemy();

            if (_isLockedByKey) {
                if (_agent != null && _agent.isOnNavMesh) {
                    _agent.isStopped = true;
                    _agent.ResetPath();
                }

                return;
            }

            bool isCutsceneActive = _blackboard != null && _blackboard.HasKey(CUTSCENE_ACTIVE_KEY) && _blackboard.GetData<bool>(CUTSCENE_ACTIVE_KEY);

            if (isCutsceneActive) {
                _isObserved = false;
                _lastObservedState = false;
                _blackboard.SetData(IS_FROZEN_KEY, false);
                _blackboard.SetData(IS_OBSERVED_KEY, false);
            }
            else {
                CheckIfObservedByPlayer();
                UpdateState();
            }

            if (!_isRetreating) {
                _tree?.Process();
            }
            if (!isCutsceneActive) {
                HandleRotation();
            }

            if (_animator != null && _agent != null && !isCutsceneActive) {
                _animator.SetFloat(_speedHash, _agent.velocity.magnitude);
            }

            HandleStuckDetection();
        }

        private void OnInventoryItemAdded(InventoryItemAddedEvent evt) {
            RefreshUnlockState();
        }

        private void RefreshUnlockState() {
            if (_unlockKeyDefinition == null) {
                _isLockedByKey = false;
                SetAggressive(true);
                return;
            }
            if (_inventoryService == null) {
                ServiceLocator.TryGet<IInventoryService<Item>>(out _inventoryService);
            }

            if (_inventoryService == null) {
                _isLockedByKey = false;
                SetAggressive(false);
                return;
            }

            IReadOnlyList<Item> items = _inventoryService.GetItems();
            if (items == null) {
                _isLockedByKey = false;
                SetAggressive(false);
                return;
            }

            for (int i = 0; i < items.Count; i++) {
                if (items[i] is Map2KeyItem keyItem && keyItem.KeyDefinition == _unlockKeyDefinition) {
                    // When the player obtains the key, nurse becomes aggressive (chase + jumpscare)
                    _isLockedByKey = false;
                    SetAggressive(true);
                    return;
                }
            }

            // Key not found - remain unlocked (patrolling), not aggressive
            _isLockedByKey = false;
            SetAggressive(false);
        }

        private void SetAggressive(bool aggressive) {
            bool changed = _isAggressive != aggressive;
            _isAggressive = aggressive;
            _blackboard?.SetData("IsAggressive", aggressive);
            if (changed && _tree != null) RebuildBehaviourTree(includeChase: aggressive);
        }

        private void PauseEnemy() {
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

        private void ResumeEnemy() {
            if (!_paused) return;
            _paused = false;
            if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = _agentWasStopped;
            if (_animator != null) _animator.speed = _animatorSpeedBeforePause;
        }

        private void CheckIfObservedByPlayer() {
            if (_playerCamera == null || _eyeTransform == null) {
                return;
            }

            Vector3 viewportPoint = _playerCamera.WorldToViewportPoint(_eyeTransform.position);
            bool inViewport = viewportPoint.x > 0 && viewportPoint.x < 1 &&
                              viewportPoint.y > 0 && viewportPoint.y < 1 &&
                              viewportPoint.z > 0;

            if (inViewport) {
                Vector3 origin = _eyeTransform.position;
                Vector3 dirToCamera = (_playerCamera.transform.position - origin).normalized;
                float distToCamera = Vector3.Distance(_playerCamera.transform.position, origin);

                RaycastHit hitInfo;

                // Primary check: raycast against configured obstacle mask
                bool blockedByObstacle = Physics.Raycast(origin, dirToCamera, out hitInfo, distToCamera, _obstacleMask);

                if (blockedByObstacle) {
                    // obstructed -> not observed
                    _isObserved = false;
                    _blackboard.SetData(IS_FROZEN_KEY, false);
                    _blackboard.SetData(IS_OBSERVED_KEY, false);
                    _lastObservedState = false;
                    return;
                }

                // Fallback: spherecast to catch thin obstacles or gaps the ray misses
                float sphereRadius = 0.18f;
                bool sphereBlocked = Physics.SphereCast(origin, sphereRadius, dirToCamera, out hitInfo, distToCamera, _obstacleMask);
                if (sphereBlocked) {
                    _isObserved = false;
                    _blackboard.SetData(IS_FROZEN_KEY, false);
                    _blackboard.SetData(IS_OBSERVED_KEY, false);
                    _lastObservedState = false;
                    return;
                }

                // No obstacle detected -> observed
                _isObserved = true;
                _blackboard.SetData(IS_FROZEN_KEY, false); // Do not freeze rigid in BT nodes
                _blackboard.SetData(IS_OBSERVED_KEY, true);
                _lastObservedState = true;
                return;
            }

            _isObserved = false;
            _blackboard.SetData(IS_FROZEN_KEY, false);
            _blackboard.SetData(IS_OBSERVED_KEY, false);
            _lastObservedState = false;
        }

        private void UpdateState() {
            if (_agent == null || !_agent.isOnNavMesh) {
                return;
            }

            // A velocidade alvo é a velocidade observada quando vista pelo player, ou a velocidade do diretor
            float targetSpeed = _isObserved ? _observedSpeed : TargetSpeed;

            // Transição suave de velocidade
            _agent.speed = Mathf.Lerp(_agent.speed, targetSpeed, Time.deltaTime * _speedLerp);

            // Garante que o agente não seja pausado fisicamente
            if (_agent.isStopped) {
                _agent.isStopped = false;
            }

            // Garante que o animator esteja despausado e rodando com a velocidade correta
            if (_animator != null) {
                _animator.speed = 1f;
            }
        }

        private void HandleRotation() {
            if (_target == null) return;

            Vector3 direction;
            if (_isObserved) {
                // Roda na direção exata do jogador para encará-lo
                direction = (_target.position - transform.position).normalized;
            }
            else {
                // Roda na direção do movimento desejado
                direction = _agent.desiredVelocity.normalized;
            }
            direction.y = 0;

            if (direction.sqrMagnitude < 0.01f) return;

            Quaternion targetRotation = Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * _rotationSpeed
            );
        }

        public void RetreatTo(Vector3 destination) {
            if (_agent != null && _agent.isOnNavMesh) {
                _agent.isStopped = false;
                _agent.SetDestination(destination);
                _isRetreating = true;
                _blackboard?.SetData("IsPlayerInRoom", true);
            }
        }

        public void ResumeFromRetreat() {
            _isRetreating = false;
            _blackboard?.SetData("IsPlayerInRoom", false);
        }

        private void HandleStuckDetection() {
            if (_isLockedByKey) {
                _stuckTimeAccumulator = 0f;
                _lastPosition = transform.position;
                return;
            }

            bool isCutsceneActive = _blackboard != null && _blackboard.HasKey(CUTSCENE_ACTIVE_KEY) && _blackboard.GetData<bool>(CUTSCENE_ACTIVE_KEY);
            if (isCutsceneActive || _isRetreating) {
                _stuckTimeAccumulator = 0f;
                _lastPosition = transform.position;
                return;
            }

            if (_agent != null && _agent.isOnNavMesh && _agent.hasPath && !_agent.isStopped && _desiredSpeed > 0.1f) {
                float distMoved = Vector3.Distance(transform.position, _lastPosition);
                _lastPosition = transform.position;

                if (_agent.velocity.magnitude < 0.1f && distMoved < 0.05f) {
                    _stuckTimeAccumulator += Time.deltaTime;
                }
                else {
                    _stuckTimeAccumulator = 0f;
                }

                if (_stuckTimeAccumulator >= _stuckDurationThreshold) {
                    ResolveStuckState();
                    _stuckTimeAccumulator = 0f;
                }
            }
            else {
                _stuckTimeAccumulator = 0f;
                _lastPosition = transform.position;
            }
        }

        private void ResolveStuckState() {
            if (_agent == null || !_agent.isOnNavMesh) return;

            Vector3 currentPos = transform.position;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(currentPos, out hit, 3.0f, NavMesh.AllAreas)) {
                _agent.Warp(hit.position);
                Debug.LogWarning($"[Nurse Unstuck] Nurse was stuck! Warped she to nearest NavMesh position: {hit.position}");
            }

            if (_agent.hasPath) {
                Vector3 dest = _agent.destination;
                _agent.ResetPath();
                _agent.SetDestination(dest);
            }
        }

        private void OnDrawGizmos() {
            if (_eyeTransform == null || _playerCamera == null) return;

            Gizmos.color = _isObserved ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(_eyeTransform.position, 0.3f);

            if (_isObserved) {
                Gizmos.DrawLine(_eyeTransform.position, _playerCamera.transform.position);
            }
        }
    }
}
