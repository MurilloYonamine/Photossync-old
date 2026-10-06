using System.Collections;
using FifthSemester.Core.Events;
using FifthSemester.Core.Services;
using UnityEngine;
using UnityEngine.AI;

namespace FifthSemester.Gameplay.Enemy {
    [DisallowMultipleComponent]
    public class NurseDirector : MonoBehaviour, IPauseable {
        [Header("Retreat Settings")]
        [SerializeField] private float retreatDistance = 20f;
        [SerializeField] private Transform[] retreatWaypoints;

        [Header("Pursuit Settings")]
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float flashlightPursuitSpeed = 4f;

        private NavMeshAgent _agent;
        [SerializeField] private Nurse _nurseComponent;
        private float _baseSpeed;
        private IEventBus _eventBus;
        private IPauseService _pauseService;
        private bool _paused;

        private bool _isPlayerSprinting = false;
        private bool _isFlashlightTargeted = false;

        private void Awake() {
            _pauseService = ServiceLocator.Get<IPauseService>();
            _agent = _nurseComponent != null ? _nurseComponent.GetComponent<NavMeshAgent>() : GetComponent<NavMeshAgent>();
            _baseSpeed = _agent != null ? _agent.speed : 3f;
        }

        private void OnEnable() {
            _pauseService.Register(this);
            _eventBus = ServiceLocator.Get<IEventBus>();
            _eventBus?.Subscribe<PlayerSprintChangedEvent>(OnPlayerSprintChanged);
            _eventBus?.Subscribe<FlashlightTargetedEvent>(OnFlashlightTargeted);
            _eventBus?.Subscribe<PlayerEnteredRoomEvent>(OnPlayerEnteredRoom);
            _eventBus?.Subscribe<PlayerExitedRoomEvent>(OnPlayerExitedRoom);
        }

        private void OnDisable() {
            _pauseService.Unregister(this);
            _eventBus?.Unsubscribe<PlayerSprintChangedEvent>(OnPlayerSprintChanged);
            _eventBus?.Unsubscribe<FlashlightTargetedEvent>(OnFlashlightTargeted);
            _eventBus?.Unsubscribe<PlayerEnteredRoomEvent>(OnPlayerEnteredRoom);
            _eventBus?.Unsubscribe<PlayerExitedRoomEvent>(OnPlayerExitedRoom);
        }

        private void Start() {
            if (_agent == null) {
                _agent = _nurseComponent != null ? _nurseComponent.GetComponent<NavMeshAgent>() : GetComponent<NavMeshAgent>();
            }
            if (_agent != null) {
                _baseSpeed = _agent.speed;
            }
        }

        private float CalculateDesiredSpeed() {
            if (_isFlashlightTargeted) {
                return flashlightPursuitSpeed;
            }
            if (_isPlayerSprinting) {
                return _baseSpeed * sprintMultiplier;
            }
            return _baseSpeed;
        }

        private void UpdateAgentSpeed() {
            if (_nurseComponent == null) return;
            float desiredSpeed = CalculateDesiredSpeed();
            _nurseComponent.TargetSpeed = desiredSpeed;
        }

        private void OnPlayerSprintChanged(PlayerSprintChangedEvent evt) {
            if (_paused) return;
            _isPlayerSprinting = evt.IsSprinting;
            UpdateAgentSpeed();
        }

        private void OnFlashlightTargeted(FlashlightTargetedEvent evt) {
            if (_paused) return;
            if (evt.Target == gameObject) {
                _isFlashlightTargeted = evt.IsIlluminated;
                UpdateAgentSpeed();
            }
        }

        private void OnPlayerEnteredRoom(PlayerEnteredRoomEvent evt) {
            if (_paused) return;
            if (evt.Player == null || _agent == null || _nurseComponent == null) return;

            Vector3 bestRetreatPoint = transform.position;
            float maxDistToPlayer = -1f;
            bool foundPoint = false;

            if (retreatWaypoints != null && retreatWaypoints.Length > 0) {
                // Use manual waypoints
                for (int i = 0; i < retreatWaypoints.Length; i++) {
                    Transform waypoint = retreatWaypoints[i];
                    if (waypoint == null) continue;

                    float distToPlayer = Vector3.Distance(waypoint.position, evt.Player.position);
                    if (distToPlayer > maxDistToPlayer) {
                        maxDistToPlayer = distToPlayer;
                        bestRetreatPoint = waypoint.position;
                        foundPoint = true;
                    }
                }
            }
            else {
                // 100% Automatic Scan Mode on the NavMesh
                for (int i = 0; i < 12; i++) {
                    Vector2 randomOffset = UnityEngine.Random.insideUnitCircle.normalized * retreatDistance;
                    Vector3 candidatePos = transform.position + new Vector3(randomOffset.x, 0f, randomOffset.y);

                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(candidatePos, out hit, 10f, NavMesh.AllAreas)) {
                        float distToPlayer = Vector3.Distance(hit.position, evt.Player.position);
                        if (distToPlayer > maxDistToPlayer) {
                            maxDistToPlayer = distToPlayer;
                            bestRetreatPoint = hit.position;
                            foundPoint = true;
                        }
                    }
                }
            }

            // Fallback safety check
            if (!foundPoint) {
                Vector3 dir = (evt.Player.position - transform.position).normalized;
                Vector3 desired = transform.position - dir * retreatDistance;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(desired, out hit, retreatDistance, NavMesh.AllAreas)) {
                    bestRetreatPoint = hit.position;
                }
                else {
                    bestRetreatPoint = desired;
                }
            }

            // Command Nurse to retreat and pause BT
            _nurseComponent.RetreatTo(bestRetreatPoint);
        }

        private void OnPlayerExitedRoom(PlayerExitedRoomEvent evt) {
            if (_paused) return;
            if (_nurseComponent != null) {
                _nurseComponent.ResumeFromRetreat();
                UpdateAgentSpeed();
            }
        }

        public void OnPause() { _paused = true; }
        public void OnResume() {
            _paused = false;
            UpdateAgentSpeed();
        }
    }
}
