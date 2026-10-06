// Autor: Murillo Gomes Yonamine
// Data: 06/05/2026

using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using FifthSemester.Core.States;
using FifthSemester.Framework.BehaviourTrees;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;

namespace FifthSemester.Gameplay.Enemy {
    [RequireComponent(typeof(NavMeshAgent))]
    public class LightSeeker : MonoBehaviour {
        private const string PLAYER_TARGET_KEY = "PlayerTarget";
        private const string NAV_AGENT_KEY = "NavAgent";
        private const string ANIMATOR_KEY = "Animator";
        private const string PATROL_WAIT_TIME_KEY = "PatrolWaitTime";
        private const string JUMPSCARE_DIRECTOR_KEY = "JumpscareDirector";
        private const string IS_STUNNED_KEY = "IsStunnedByFlashlight";
        private const string HAS_LINE_OF_SIGHT_KEY = "HasLineOfSight";

        [SerializeField] private Transform _target;
        [SerializeField] private float _patrolWaitTime = 2f;

        [Header("Vision Settings")]
        [SerializeField] private Transform _eyeTransform;
        [SerializeField, Range(0, 120)] private float _viewDistance = 15f;
        [SerializeField] private float _loseTargetDistance = 25f;
        [SerializeField, Range(0, 360)] private float _fovAngle = 90f;
        [SerializeField] private LayerMask _obstacleMask;

        private BehaviourTree _tree;
        public Blackboard Blackboard { get; private set; }
        private NavMeshAgent _agent;
        private Animator _animator;
        private IGameStateService _gameStateService;
        private bool _paused;
        private bool _agentWasStopped;
        private float _animatorSpeedBeforePause;

        [Header("Timeline")]
        [SerializeField] private PlayableDirector _jumpscareDirector;

        [Header("Jumpscare Settings")]
        [SerializeField, Range(0.1f, 5f)] private float _jumpscareTriggerDistance = 1.25f;

        [Header("Catch Up Settings")]
        [SerializeField] private float _catchUpDistanceThreshold = 25f;
        [SerializeField] private AudioClip _jumpSound;
        [SerializeField] private AudioClip _landSound;

        [Header("Speed Settings")]
        [SerializeField, Range(0f, 10f)] private float _speed = 1.5f;
        [SerializeField, Range(0f, 10f)] private float _sprint = 3f;
        [SerializeField, Range(0f, 10f)] private float _slowSpeed = 0.5f;

        private readonly int _speedHash = Animator.StringToHash("Speed");

        private bool _isIlluminated = false;
        private bool _isPlayerSprinting = false;

        private void Awake() {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();

            Shader.SetGlobalFloat("_NoiseOpacity", 0);

            if (_jumpscareDirector == null) {
                _jumpscareDirector = GetComponentInChildren<PlayableDirector>(true);
            }

            _agent.speed = _speed;
            _agent.stoppingDistance = _jumpscareTriggerDistance;

            SetupBlackboard();
        }

        private void SetupBlackboard() {
            if(_target == null ) {
                _target = GameObject.FindGameObjectWithTag("Player")?.transform;
            }

            Blackboard = new Blackboard();
            Blackboard.SetData(PLAYER_TARGET_KEY, _target);
            Blackboard.SetData(NAV_AGENT_KEY, _agent);
            Blackboard.SetData(ANIMATOR_KEY, _animator);
            Blackboard.SetData(PATROL_WAIT_TIME_KEY, _patrolWaitTime);
            Blackboard.SetData(JUMPSCARE_DIRECTOR_KEY, _jumpscareDirector);
            Blackboard.SetData(IS_STUNNED_KEY, false);

            // Vision parameters
            Blackboard.SetData("EyeTransform", _eyeTransform);
            Blackboard.SetData("ViewDistance", _viewDistance);
            Blackboard.SetData("FovAngle", _fovAngle);
            Blackboard.SetData("ObstacleMask", _obstacleMask);
            Blackboard.SetData("LoseTargetDistance", _loseTargetDistance);
            Blackboard.SetData(HAS_LINE_OF_SIGHT_KEY, false);

            // Audio parameters
            Blackboard.SetData("JumpSound", _jumpSound);
            Blackboard.SetData("LandSound", _landSound);
        }

        private void Start() {
            ServiceLocator.TryGet<IGameStateService>(out _gameStateService);
            BuildBehaviourTree();
        }

        private void BuildBehaviourTree() {
            var catchUpSequence = new Sequence("CatchUpTeleport");
            catchUpSequence.AddChild(new ConditionPlayerTooFar(Blackboard, "Is Player Too Far?", _catchUpDistanceThreshold));
            catchUpSequence.AddChild(new ActionCatchUpTeleport(Blackboard, "Catch Up Teleport"));

            var stareAndJumpSequence = new Sequence("StareAndJumpSequence");
            stareAndJumpSequence.AddChild(new ConditionPlayerInSafeLight(Blackboard, "Is Player in Light?"));
            stareAndJumpSequence.AddChild(new ConditionLineOfSight(Blackboard, "Line of Sight Check"));
            stareAndJumpSequence.AddChild(new ActionStareAndPounce(Blackboard, "Stare and Pounce"));
            stareAndJumpSequence.AddChild(new ActionPlayJumpscare(Blackboard, "Jumpscare"));

            var chaseSequence = new Sequence("AggressiveChase");
            chaseSequence.AddChild(new ActionChase(Blackboard, "Chase Player"));
            chaseSequence.AddChild(new ActionPlayJumpscare(Blackboard, "Jumpscare"));

            var root = new Selector("RootBehavior");

            root.AddChild(catchUpSequence);
            root.AddChild(stareAndJumpSequence);
            root.AddChild(chaseSequence);

            _tree = new BehaviourTree("LightSeeker Behaviour Tree", root);
        }

        private void Update() {
            if (_gameStateService != null && _gameStateService.CurrentState == GameState.Paused) {
                PauseEnemy();
                return;
            }
            ResumeEnemy();

            bool isCutscene = (_gameStateService != null && _gameStateService.CurrentState == GameState.Cutscene) ||
                              (Blackboard != null && Blackboard.HasKey("CutsceneActive") && Blackboard.GetData<bool>("CutsceneActive"));

            if (isCutscene) {
                if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped) {
                    _agent.isStopped = true;
                    _agent.velocity = Vector3.zero;
                }
                if (_animator != null) {
                    _animator.SetFloat(_speedHash, 0f);
                }
                return;
            }

            // Update Blackboard's HasLineOfSight key so actions (like white noise in ActionChase) work correctly
            if (Blackboard != null) {
                Blackboard.SetData(HAS_LINE_OF_SIGHT_KEY, IsPlayerInFOV());
            }

            _tree?.Process();

            if (_animator != null && _agent.enabled && _agent.isOnNavMesh) {
                _animator.SetFloat(_speedHash, _agent.velocity.magnitude);
            }
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

        private void OnEnable() {
            var eventBus = ServiceLocator.Get<IEventBus>();
            eventBus?.Subscribe<PlayerSprintChangedEvent>(HandleSprint);
            eventBus?.Subscribe<FlashlightTargetedEvent>(HandleFlashlightTargeted);
        }

        private void OnDisable() {
            var eventBus = ServiceLocator.Get<IEventBus>();
            eventBus?.Unsubscribe<PlayerSprintChangedEvent>(HandleSprint);
            eventBus?.Unsubscribe<FlashlightTargetedEvent>(HandleFlashlightTargeted);
        }

        private void HandleSprint(PlayerSprintChangedEvent evt) {
            _isPlayerSprinting = evt.IsSprinting;
            UpdateSpeed();
        }

        private void HandleFlashlightTargeted(FlashlightTargetedEvent evt) {
            if (_agent == null || evt.Target != this.gameObject) {
                return;
            }

            _isIlluminated = evt.IsIlluminated;
            Blackboard?.SetData(IS_STUNNED_KEY, _isIlluminated);

            UpdateSpeed();
        }

        private void UpdateSpeed() {
            if (_agent == null || !_agent.isOnNavMesh) return;

            if (_isIlluminated) {
                _agent.speed = _slowSpeed;
            }
            else if (_isPlayerSprinting) {
                _agent.speed = _sprint;
            }
            else {
                _agent.speed = _speed;
            }

            _agent.acceleration = _agent.speed * 2f;
        }

        private bool IsPlayerInFOV() {
            if (_target == null || _eyeTransform == null) {
                return false;
            }

            Vector3 eyePos = _eyeTransform.position;
            Vector3 dirToTarget = _target.position - eyePos;
            float distance = dirToTarget.magnitude;

            if (distance > _viewDistance) {
                return false;
            }

            Vector3 dirToTargetNormalized = dirToTarget.normalized;
            float angle = Vector3.Angle(_eyeTransform.forward, dirToTargetNormalized);

            if (angle > _fovAngle * 0.5f) {
                return false;
            }

            if (Physics.Raycast(eyePos, dirToTargetNormalized, distance, _obstacleMask)) {
                return false;
            }

            return true;
        }

        private void OnDrawGizmosSelected() {
            DrawFOVGizmos();
        }
        private void OnDrawGizmos() {
            DrawFOVGizmos();
        }

        private void DrawFOVGizmos() {
            Transform eye = _eyeTransform != null ? _eyeTransform : transform;
            Vector3 pos = eye.position;
            bool inFov = IsPlayerInFOV();
            Gizmos.color = inFov ? Color.red : Color.yellow;
            Vector3 forward = eye.forward;
            int steps = 10;
            float halfFov = _fovAngle * 0.5f;
            Vector3 prevPoint = Vector3.zero;

            for (int i = 0; i <= steps; i++) {
                float t = i / (float)steps;
                float angle = Mathf.Lerp(-halfFov, halfFov, t);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
                Vector3 point = pos + dir * _viewDistance;
                Gizmos.DrawLine(pos, point);
                if (i > 0) Gizmos.DrawLine(prevPoint, point);
                prevPoint = point;
            }

            if (_target != null) {
                Gizmos.color = inFov ? Color.red : Color.green;
                Gizmos.DrawLine(pos, _target.position);
            }
        }
    }
}
