using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    public enum RatBehaviorState
    {
        Idle,
        Wander,
        WalkToTarget,
        Run,
        Investigate,
        Interact,
        Jump,
        Recover,
        Dying,
    }

    public enum RatBehaviorTargetKind
    {
        Food,
        Water,
        Nest,
        Hide,
        Tunnel,
        Toy,
    }

    [Serializable]
    public class RatBehaviorTarget
    {
        public string id;
        public string label;
        public RatBehaviorTargetKind kind;
        public Vector3 position;
    }

    /// <summary>
    /// Drives ambient movement for one live rat. Movement is applied to the
    /// stable selectable rat root, while animation playback remains on the
    /// imported visual child. This keeps selection, rings, portraits, and
    /// stage transitions independent from the behavior state machine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RatHabitatBehavior : MonoBehaviour
    {
        private static readonly List<RatHabitatBehavior> activeBehaviors = new List<RatHabitatBehavior>();

        private HabitatBuilder habitat;
        private RatData rat;
        private Animator animator;
        private System.Random random;
        private readonly Dictionary<string, float> targetCooldowns = new Dictionary<string, float>();
        private RatBehaviorTarget currentTarget;
        private RatStage configuredStage;
        private RatBehaviorState state;
        private Vector3 targetPosition;
        private Quaternion investigateRotation;
        private float stateTimer;
        private float travelTimer;
        private float movementSpeed;
        private float nextInvestigationAllowedAt;
        private float behaviorClockSeconds;
        private float foodNeed;
        private float waterNeed;
        private float restNeed;
        private float exploreNeed;
        private float foodPersonality;
        private float waterPersonality;
        private float restPersonality;
        private float explorePersonality;
        private int seed;
        private bool configured;
        private bool clipAuditLogged;
        private Transform microAnimationRoot;
        private Transform headMicroBone;
        private Transform leftEarMicroBone;
        private Transform rightEarMicroBone;
        private Transform tailMicroBone;
        private Transform chestMicroBone;
        private Quaternion headMicroOffset = Quaternion.identity;
        private Quaternion leftEarMicroOffset = Quaternion.identity;
        private Quaternion rightEarMicroOffset = Quaternion.identity;
        private Quaternion tailMicroOffset = Quaternion.identity;
        private Quaternion chestMicroOffset = Quaternion.identity;
        private float microAnimationPhase;
        private bool microAnimationBound;
        private Vector3 previousPositionForFacing;
        private bool hasPreviousPositionForFacing;
        private bool deathPoseHeld;
        private RatEnclosure configuredEnclosure;
        private bool pairingApproachActive;
        private bool pairingApproachArrived;
        private bool pairingInteractionActive;
        private bool pairingInteractionComplete;
        private Vector3 pairingApproachTarget;
        private Vector3 pairingFacingPoint;
        private readonly List<Vector3> pairingRouteWaypoints = new List<Vector3>();
        private int pairingRouteWaypointIndex;
        private readonly List<Vector3> nestDetourWaypoints = new List<Vector3>();
        private int nestDetourWaypointIndex;
        private float pairingInteractionRemaining;
        private float pairingSpeedMultiplier = 1f;
        private Vector3 diagnosticsPreviousPosition;
        private bool diagnosticsHavePreviousPosition;
        private float lastActualWorldMovementSpeed;

        private const float MinimumWalkSpeed = 0.55f;
        private const float MaximumWalkSpeed = 0.92f;
        private const float WalkMovementScale = 0.513f;
        private const float WalkAnimationPlaybackScale = 0.513f;
        private const float RunSpeed = 1.65f;
        private const float ArrivalDistance = 0.24f;
        private const float MinimumRatSpacing = 0.82f;
        private const float MovementFacingThreshold = 0.0025f;
        private const float MovementTurnSpeed = 360f;
        private const float IdleDwellScale = 1.75f;
        private const float MinimumSniffDuration = 1.0f;
        private const float MaximumSniffDuration = 3.0f;

        public RatBehaviorState State { get { return state; } }
        public bool PairingApproachAtTarget { get { return pairingApproachActive && pairingApproachArrived; } }
        public bool PairingInteractionComplete { get { return pairingInteractionActive && pairingInteractionComplete; } }
        public float BaseWorldMovementSpeed { get { return movementSpeed; } }
        public float TargetWorldMovementSpeed
        {
            get { return movementSpeed * GrowthSystem.RuntimeSimulationSpeed; }
        }
        public float ActualWorldMovementSpeed { get { return lastActualWorldMovementSpeed; } }

        public static string GetMovementDiagnosticReadout()
        {
            for (int index = 0; index < activeBehaviors.Count; index++)
            {
                RatHabitatBehavior candidate = activeBehaviors[index];
                if (candidate == null || !candidate.configured || candidate.rat == null ||
                    candidate.rat.stage == RatStage.Pinkie) continue;

                return string.Format(
                "Rat {0}: simulation {1:0.#}x | actual {2:0.00} u/s | target {3:0.00} u/s | time=scaled movement delta",
                ColonyFactory.DisplayName(candidate.rat),
                    GrowthSystem.RuntimeSimulationSpeed,
                    candidate.ActualWorldMovementSpeed,
                    candidate.TargetWorldMovementSpeed);
            }

            return string.Format(
                "No moving rat sampled | simulation {0:0.#}x | time=scaled movement delta",
                GrowthSystem.RuntimeSimulationSpeed);
        }

        /// <summary>
        /// Coarse, player-facing activity derived from the existing movement
        /// state machine. GameBootstrap records transitions only, so this is
        /// intentionally not a per-frame event source.
        /// </summary>
        public string ActivityKey
        {
            get
            {
                if (pairingApproachActive) return "breeding";
                if (state == RatBehaviorState.Dying) return "deceased";
                if (currentTarget != null)
                {
                    switch (currentTarget.kind)
                    {
                        case RatBehaviorTargetKind.Food: return "eating";
                        case RatBehaviorTargetKind.Water: return "drinking";
                        case RatBehaviorTargetKind.Hide: return "sleeping";
                    }
                }
                if (state == RatBehaviorState.Idle || state == RatBehaviorState.Recover)
                    return "sleeping";
                return "exploring";
            }
        }

        public string ActivityLabel
        {
            get
            {
                if (pairingApproachActive) return "Breeding";
                if (state == RatBehaviorState.Dying) return "Deceased";
                if (currentTarget != null)
                {
                    switch (currentTarget.kind)
                    {
                        case RatBehaviorTargetKind.Food: return "Eating";
                        case RatBehaviorTargetKind.Water: return "Drinking";
                        case RatBehaviorTargetKind.Hide: return "Sleeping";
                    }
                }
                if (state == RatBehaviorState.Idle || state == RatBehaviorState.Recover)
                    return state == RatBehaviorState.Recover ? "Recovering" : "Sleeping";
                return "Exploring";
            }
        }

        private void OnEnable()
        {
            if (!activeBehaviors.Contains(this)) activeBehaviors.Add(this);
        }

        private void OnDisable()
        {
            activeBehaviors.Remove(this);
        }

        private void OnDestroy()
        {
            activeBehaviors.Remove(this);
        }

        public void Configure(HabitatBuilder builder, RatData data)
        {
            habitat = builder;
            rat = data;
            if (rat == null) return;

            if (rat.stage == RatStage.Pinkie)
            {
                // Presenter normally does not add this component for Pinkies;
                // this guard keeps a stale component inert during any unusual
                // stage rollback or transition race.
                currentTarget = null;
                state = RatBehaviorState.Idle;
                configured = false;
                ClearMicroAnimationOffsets();
                animator = null;
                microAnimationRoot = null;
                microAnimationBound = false;
                return;
            }

            animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                // Ambient movement is code-driven. Keep the imported
                // controller running for limb animation without allowing its
                // root curves to move the stable selectable root.
                animator.enabled = true;
                animator.applyRootMotion = false;
            }

            if (!configured)
            {
                seed = StableSeed(rat.id);
                random = new System.Random(seed);
                previousPositionForFacing = transform.position;
                hasPreviousPositionForFacing = true;
                foodPersonality = NextFloat(0.75f, 1.35f);
                waterPersonality = NextFloat(0.75f, 1.35f);
                restPersonality = NextFloat(0.75f, 1.35f);
                explorePersonality = NextFloat(0.75f, 1.35f);
                foodNeed = NextFloat(0.12f, 0.55f);
                waterNeed = NextFloat(0.12f, 0.55f);
                restNeed = NextFloat(0.12f, 0.55f);
                exploreNeed = NextFloat(0.15f, 0.7f);
                microAnimationPhase = NextFloat(0f, Mathf.PI * 2f);
                behaviorClockSeconds = 0f;
                nextInvestigationAllowedAt = behaviorClockSeconds + NextFloat(0.7f, 2.4f);
                configuredStage = rat.stage;
                configuredEnclosure = rat.enclosure;
                configured = true;
                deathPoseHeld = false;
                diagnosticsPreviousPosition = transform.position;
                diagnosticsHavePreviousPosition = true;
                lastActualWorldMovementSpeed = 0f;
                if (animator != null) animator.speed = GrowthSystem.RuntimeSimulationSpeed;
                // Skip the initial stationary hold so new adults begin with
                // the configured movement behavior.
                BeginTravel();
            }
            else if (configuredStage != rat.stage)
            {
                configuredStage = rat.stage;
                currentTarget = null;
                deathPoseHeld = false;
                if (animator != null) animator.speed = GrowthSystem.RuntimeSimulationSpeed;
                EnterState(RatBehaviorState.Idle, NextIdleDuration(0.8f, 1.8f));
            }
            else if (configuredEnclosure != rat.enclosure)
            {
                // Pregnancy, nursing completion, deletion, and growth can
                // change the authoritative zone. Presenter has already moved
                // the stable root; reset only this behavior's local target so
                // it cannot continue toward the previous enclosure.
                configuredEnclosure = rat.enclosure;
                currentTarget = null;
                transform.position = ClampToAssignedEnclosure(transform.position);
                BeginTravel();
            }

            BindMicroAnimationBones();
            AuditAnimatorOnce();
        }

        public void ConfigureHabitat(HabitatBuilder builder)
        {
            habitat = builder;
        }

        /// <summary>
        /// Temporarily takes over the ambient movement state for one automatic
        /// Pairing Habitat courtship. The stable rat root still moves through
        /// the normal enclosure clamp, so the approach cannot leave the cage.
        /// </summary>
        public bool BeginPairingApproach(Vector3 target, Vector3 facePoint, float speedMultiplier)
        {
            return BeginPairingApproach(target, facePoint, speedMultiplier, null);
        }

        public bool BeginPairingApproach(Vector3 target, Vector3 facePoint, float speedMultiplier,
            IList<Vector3> routeWaypoints)
        {
            if (!configured || rat == null || rat.stage != RatStage.Adult || rat.enclosure != RatEnclosure.Pairing) return false;

            pairingApproachActive = true;
            pairingApproachArrived = false;
            pairingInteractionActive = false;
            pairingInteractionComplete = false;
            pairingApproachTarget = ClampToNestSafePosition(new Vector3(target.x, transform.position.y, target.z));
            pairingFacingPoint = facePoint;
            pairingRouteWaypoints.Clear();
            if (routeWaypoints != null)
            {
                for (int index = 0; index < routeWaypoints.Count; index++)
                {
                    Vector3 waypoint = routeWaypoints[index];
                    waypoint.y = transform.position.y;
                    if (!EnclosureSystem.IsBehaviorPointAllowed(RatEnclosure.Pairing, waypoint)) continue;
                    if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, waypoint, 0.04f)) continue;
                    if (pairingRouteWaypoints.Count == 0 ||
                        Vector3.Distance(pairingRouteWaypoints[pairingRouteWaypoints.Count - 1], waypoint) > 0.12f)
                    {
                        pairingRouteWaypoints.Add(waypoint);
                    }
                }
            }
            pairingRouteWaypointIndex = 0;
            nestDetourWaypoints.Clear();
            nestDetourWaypointIndex = 0;
            pairingSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.75f, 3f);
            currentTarget = null;
            stateTimer = 60f;
            EnterState(RatBehaviorState.WalkToTarget, stateTimer);
            movementSpeed = Mathf.Max(0.15f, movementSpeed * pairingSpeedMultiplier);
            return true;
        }

        public void BeginPairingInteraction(Vector3 facePoint, float durationSeconds)
        {
            if (!pairingApproachActive) return;
            pairingApproachArrived = true;
            pairingInteractionActive = true;
            pairingInteractionComplete = false;
            pairingInteractionRemaining = Mathf.Max(0.75f, durationSeconds);
            pairingFacingPoint = facePoint;
            EnterState(RatBehaviorState.Interact, pairingInteractionRemaining);
        }

        public void FinishPairingInteraction()
        {
            if (!pairingApproachActive) return;
            pairingApproachActive = false;
            pairingApproachArrived = false;
            pairingInteractionActive = false;
            pairingInteractionComplete = false;
            pairingInteractionRemaining = 0f;
            currentTarget = null;
            pairingRouteWaypoints.Clear();
            pairingRouteWaypointIndex = 0;
            nestDetourWaypoints.Clear();
            nestDetourWaypointIndex = 0;
            BeginTravel();
        }

        public void CancelPairingApproach()
        {
            if (!pairingApproachActive) return;
            pairingApproachActive = false;
            pairingApproachArrived = false;
            pairingInteractionActive = false;
            pairingInteractionComplete = false;
            pairingInteractionRemaining = 0f;
            currentTarget = null;
            pairingRouteWaypoints.Clear();
            pairingRouteWaypointIndex = 0;
            nestDetourWaypoints.Clear();
            nestDetourWaypointIndex = 0;
            BeginTravel();
        }

        /// <summary>
        /// Called only when the live rat is actually removed. There is no
        /// normal-life path into Dying, and the state never loops.
        /// </summary>
        public void PlayDyingOnce()
        {
            if (!configured || state == RatBehaviorState.Dying) return;
            currentTarget = null;
            deathPoseHeld = false;
            if (animator != null) animator.speed = GrowthSystem.RuntimeSimulationSpeed;
            EnterState(RatBehaviorState.Dying, 0.75f);
        }

        private void Update()
        {
            if (!configured || rat == null || habitat == null) return;
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
                microAnimationBound = false;
                AuditAnimatorOnce();
            }

            float deltaTime = GrowthSystem.SimulationBehaviorDeltaSeconds(Time.unscaledDeltaTime);
            behaviorClockSeconds += deltaTime;
            ApplySimulationAnimationSpeed();

            if (state == RatBehaviorState.Dying)
            {
                HoldDyingPoseAtEnd();
                return;
            }

            BindMicroAnimationBones();

            if (rat.stage == RatStage.Pinkie)
            {
                return;
            }

            if (rat.enclosure != configuredEnclosure)
            {
                if (pairingApproachActive) CancelPairingApproach();
                configuredEnclosure = rat.enclosure;
                currentTarget = null;
                transform.position = ClampToAssignedEnclosure(transform.position);
                BeginTravel();
            }

            // Nest targets can still exist in older/generated habitat data,
            // but the nest is a Pinkie-only space. Drop a stale target before
            // it can make an adult or young rat walk back into the nest.
            if (currentTarget != null && currentTarget.kind == RatBehaviorTargetKind.Nest)
            {
                currentTarget = null;
                BeginTravel();
            }

            if (pairingApproachActive)
            {
                if (pairingInteractionActive) UpdatePairingInteraction(deltaTime);
                else UpdatePairingApproach(deltaTime);
                return;
            }

            UpdateNeeds(deltaTime);
            stateTimer -= deltaTime;

            switch (state)
            {
                case RatBehaviorState.Idle:
                    if (stateTimer <= 0f) BeginTravel();
                    break;
                case RatBehaviorState.Wander:
                case RatBehaviorState.WalkToTarget:
                case RatBehaviorState.Run:
                    UpdateTravel(deltaTime);
                    break;
                case RatBehaviorState.Investigate:
                    UpdateInvestigation(deltaTime);
                    break;
                case RatBehaviorState.Interact:
                    UpdateInteraction(deltaTime);
                    break;
                case RatBehaviorState.Jump:
                    UpdateJump(deltaTime);
                    break;
                case RatBehaviorState.Recover:
                    if (stateTimer <= 0f) EnterState(RatBehaviorState.Idle, NextIdleDuration(1.2f, 4.2f));
                    break;
            }
        }

        private void LateUpdate()
        {
            if (!configured || rat == null || animator == null) return;
            float realDelta = Time.unscaledDeltaTime;
            if (diagnosticsHavePreviousPosition && realDelta > 0.0001f)
            {
                lastActualWorldMovementSpeed = Vector3.Distance(
                    transform.position, diagnosticsPreviousPosition) / realDelta;
            }
            diagnosticsPreviousPosition = transform.position;
            diagnosticsHavePreviousPosition = true;
            UpdateMovementFacingFromPositionDelta();
            BindMicroAnimationBones();

            // A removed rat does not receive a fake death loop. Clear the
            // additive offsets so removal remains the only dying behavior.
            if (state == RatBehaviorState.Dying || rat.stage == RatStage.Pinkie)
            {
                ClearMicroAnimationOffsets();
                return;
            }

            if (state == RatBehaviorState.Idle || state == RatBehaviorState.Recover)
            {
                ClearMicroAnimationOffsets();
                return;
            }

            // Imported animation bones are left alone while the stable root is
            // travelling. This prevents a procedural head turn from disagreeing
            // with the body direction or with the actual movement vector.
            ClearMicroAnimationOffsets();
        }

        private bool UpdateMovementFacingFromPositionDelta()
        {
            Vector3 currentPosition = transform.position;
            if (!hasPreviousPositionForFacing)
            {
                previousPositionForFacing = currentPosition;
                hasPreviousPositionForFacing = true;
                return false;
            }

            Vector3 movementDelta = currentPosition - previousPositionForFacing;
            previousPositionForFacing = currentPosition;
            movementDelta.y = 0f;
            if (movementDelta.sqrMagnitude <= MovementFacingThreshold * MovementFacingThreshold) return false;

            Vector3 travelDirection = movementDelta.normalized;
            Quaternion desiredRotation = RotationFacingWorldDirection(travelDirection);
            float deltaTime = GrowthSystem.SimulationBehaviorDeltaSeconds(Time.unscaledDeltaTime);
            if (deltaTime <= 0f) return false;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRotation, MovementTurnSpeed * deltaTime);
            return true;
        }

        private void BindMicroAnimationBones()
        {
            Transform root = animator == null ? null : animator.transform;
            if (microAnimationBound && microAnimationRoot == root) return;

            ClearMicroAnimationOffsets();
            microAnimationRoot = root;
            headMicroBone = null;
            leftEarMicroBone = null;
            rightEarMicroBone = null;
            tailMicroBone = null;
            chestMicroBone = null;
            microAnimationBound = true;
            if (root == null) return;

            // These are bones from the inspected Hand Painted Rat prefab, not
            // gameplay roots or generated marking/selection objects. Exact
            // names are preferred, with conservative fragment fallbacks for a
            // future reimport that changes only a separator or suffix.
            headMicroBone = FindMicroBone(root, "Head", "head", "neck");
            leftEarMicroBone = FindMicroBone(root, "Left_Ear", "left_ear", "ear_l");
            rightEarMicroBone = FindMicroBone(root, "Right_Ear", "right_ear", "ear_r");
            tailMicroBone = FindMicroBone(root, "Tail_2", "tail_2", "tail");
            chestMicroBone = FindMicroBone(root, "Chest", "chest", "body");
        }

        private static Transform FindMicroBone(Transform root, string exactName, params string[] fragments)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(transforms[i].name, exactName, StringComparison.OrdinalIgnoreCase)) return transforms[i];
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                string lowerName = transforms[i].name.ToLowerInvariant();
                for (int j = 0; j < fragments.Length; j++)
                {
                    if (!string.IsNullOrEmpty(fragments[j]) && lowerName.Contains(fragments[j].ToLowerInvariant())) return transforms[i];
                }
            }
            return null;
        }

        private void ApplyMicroAnimations()
        {
            float time = behaviorClockSeconds;
            float lookYaw = Mathf.Sin(time * 0.72f + microAnimationPhase) * 4.5f;
            float lookPitch = Mathf.Sin(time * 0.91f + microAnimationPhase * 0.63f) * 1.8f;
            float sniff = Mathf.Max(0f, Mathf.Sin(time * 1.55f + microAnimationPhase)) * 1.4f;
            ApplyAdditiveRotation(headMicroBone, ref headMicroOffset, new Vector3(lookPitch + sniff, lookYaw, Mathf.Sin(time * 0.58f + microAnimationPhase) * 1.2f));

            float earMotion = Mathf.Sin(time * 1.35f + microAnimationPhase * 1.2f) * 2.8f;
            ApplyAdditiveRotation(leftEarMicroBone, ref leftEarMicroOffset, new Vector3(0f, 0f, earMotion));
            ApplyAdditiveRotation(rightEarMicroBone, ref rightEarMicroOffset, new Vector3(0f, 0f, -earMotion * 0.86f));

            float tailSway = Mathf.Sin(time * 0.62f + microAnimationPhase * 0.8f) * 3.6f;
            ApplyAdditiveRotation(tailMicroBone, ref tailMicroOffset, new Vector3(Mathf.Sin(time * 0.84f + microAnimationPhase) * 1.3f, tailSway, 0f));

            // A very small chest rotation reads as breathing while avoiding
            // scale changes that could distort the live imported model.
            float breath = Mathf.Sin(time * 1.42f + microAnimationPhase) * 0.8f;
            ApplyAdditiveRotation(chestMicroBone, ref chestMicroOffset, new Vector3(breath, 0f, 0f));
        }

        private static void ApplyAdditiveRotation(Transform bone, ref Quaternion previousOffset, Vector3 eulerAngles)
        {
            if (bone == null) return;
            bone.localRotation *= Quaternion.Inverse(previousOffset);
            Quaternion nextOffset = Quaternion.Euler(eulerAngles);
            bone.localRotation *= nextOffset;
            previousOffset = nextOffset;
        }

        private void ClearMicroAnimationOffsets()
        {
            RemoveAdditiveRotation(headMicroBone, ref headMicroOffset);
            RemoveAdditiveRotation(leftEarMicroBone, ref leftEarMicroOffset);
            RemoveAdditiveRotation(rightEarMicroBone, ref rightEarMicroOffset);
            RemoveAdditiveRotation(tailMicroBone, ref tailMicroOffset);
            RemoveAdditiveRotation(chestMicroBone, ref chestMicroOffset);
        }

        private static void RemoveAdditiveRotation(Transform bone, ref Quaternion previousOffset)
        {
            if (bone != null) bone.localRotation *= Quaternion.Inverse(previousOffset);
            previousOffset = Quaternion.identity;
        }

        private void UpdateNeeds(float deltaTime)
        {
            foodNeed = Mathf.Clamp01(foodNeed + deltaTime * 0.0042f * foodPersonality);
            waterNeed = Mathf.Clamp01(waterNeed + deltaTime * 0.005f * waterPersonality);
            restNeed = Mathf.Clamp01(restNeed + deltaTime * 0.0031f * restPersonality);
            exploreNeed = Mathf.Clamp01(exploreNeed + deltaTime * 0.0026f * explorePersonality);
        }

        private void BeginTravel()
        {
            nestDetourWaypoints.Clear();
            nestDetourWaypointIndex = 0;
            currentTarget = ChooseTarget();
            if (currentTarget == null)
            {
                targetPosition = RandomAssignedPosition();
                EnterState(ShouldRun() ? RatBehaviorState.Run : RatBehaviorState.Wander, NextFloat(6f, 14f));
                return;
            }

            float angle = NextFloat(0f, Mathf.PI * 2f);
            float radius = currentTarget.kind == RatBehaviorTargetKind.Tunnel ? 1.0f : NextFloat(0.65f, 1.15f);
            Vector3 approach = currentTarget.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            targetPosition = ClampToNestSafePosition(approach);
            travelTimer = 0f;
            EnterState(ShouldRun() ? RatBehaviorState.Run : RatBehaviorState.WalkToTarget, NextFloat(7f, 18f));
        }

        private RatBehaviorTarget ChooseTarget()
        {
            if (habitat == null) return null;
            List<RatBehaviorTarget> targets = habitat.GetBehaviorTargets(rat == null ? RatEnclosure.FemaleColony : rat.enclosure);
            if (targets == null || targets.Count == 0) return null;

            RatBehaviorTarget best = null;
            float bestScore = float.MinValue;
            Vector3 current = transform.position;
            foreach (var candidate in targets)
            {
                if (candidate == null) continue;
                // Pinkies have no movement behavior. Therefore any Nest
                // target is exclusively reserved for their fixed birth
                // placement and must never be selected by this controller.
                if (candidate.kind == RatBehaviorTargetKind.Nest) continue;
                float distance = Vector3.Distance(new Vector3(current.x, 0f, current.z), new Vector3(candidate.position.x, 0f, candidate.position.z));
                float need = NeedFor(candidate.kind);
                float score = need * 2.6f - distance * 0.075f + NextFloat(-0.22f, 0.22f) * (0.5f + exploreNeed);
                float availableAt;
                if (!string.IsNullOrEmpty(candidate.id) && targetCooldowns.TryGetValue(candidate.id, out availableAt) && behaviorClockSeconds < availableAt)
                {
                    score -= 0.8f;
                }
                if (currentTarget != null && candidate.id == currentTarget.id) score -= 0.45f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        private float NeedFor(RatBehaviorTargetKind kind)
        {
            switch (kind)
            {
                case RatBehaviorTargetKind.Food: return foodNeed * foodPersonality;
                case RatBehaviorTargetKind.Water: return waterNeed * waterPersonality;
                case RatBehaviorTargetKind.Nest: return restNeed * restPersonality;
                case RatBehaviorTargetKind.Hide: return (restNeed + exploreNeed) * 0.5f * restPersonality;
                case RatBehaviorTargetKind.Toy: return exploreNeed * explorePersonality;
                default: return exploreNeed * explorePersonality;
            }
        }

        private void UpdateTravel(float deltaTime)
        {
            travelTimer += deltaTime;
            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= ArrivalDistance * ArrivalDistance)
            {
                if (currentTarget == null)
                {
                    if (ShouldStartAmbientInvestigation(0.42f)) BeginAmbientInvestigation();
                    else EnterState(RatBehaviorState.Idle, NextIdleDuration(1.1f, 3.8f));
                }
                else
                {
                    BeginInvestigation();
                }
                return;
            }
            if (stateTimer <= 0f)
            {
                currentTarget = null;
                if (ShouldStartAmbientInvestigation(0.24f)) BeginAmbientInvestigation();
                else EnterState(RatBehaviorState.Idle, NextIdleDuration(1.1f, 3.8f));
                return;
            }

            Vector3 direction = toTarget.normalized;

            // The inspected prefab has Apply Root Motion disabled. If a future
            // imported controller enables it, code movement stops here so the
            // animator remains the sole source of translation.
            bool usesRootMotion = animator != null && animator.applyRootMotion;
            if (!usesRootMotion)
            {
                // Use the selected simulation delta for the real position
                // write, but clamp the step so a 2x/3x frame can never jump
                // past the destination and start oscillating around it.
                float step = GrowthSystem.SimulationMovementStep(
                    movementSpeed, Time.unscaledDeltaTime);
                Vector3 nextPosition = Vector3.MoveTowards(
                    transform.position,
                    targetPosition,
                    step);
                transform.position = MoveTowardAvoidingNest(transform.position, nextPosition);
            }
            ResolveSpacing();
        }

        private void UpdatePairingApproach(float deltaTime)
        {
            FacePairingPoint(deltaTime);
            Vector3 currentDestination = pairingRouteWaypointIndex < pairingRouteWaypoints.Count
                ? pairingRouteWaypoints[pairingRouteWaypointIndex]
                : pairingApproachTarget;
            Vector3 toTarget = currentDestination - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= ArrivalDistance * ArrivalDistance)
            {
                if (pairingRouteWaypointIndex < pairingRouteWaypoints.Count)
                {
                    pairingRouteWaypointIndex++;
                    return;
                }
                transform.position = ClampToAssignedEnclosure(new Vector3(
                    pairingApproachTarget.x,
                    transform.position.y,
                    pairingApproachTarget.z));
                pairingApproachArrived = true;
                return;
            }

            Vector3 direction = toTarget.normalized;
            // Pairing routes use the same centralized simulation delta as
            // ambient travel. Clamp each step to the active waypoint so an
            // immediate speed change cannot overshoot the face-to-face point.
            float step = GrowthSystem.SimulationMovementStep(
                movementSpeed, Time.unscaledDeltaTime);
            Vector3 nextPosition = Vector3.MoveTowards(
                transform.position,
                currentDestination,
                step);
            transform.position = MoveTowardAvoidingNest(transform.position, nextPosition);
        }

        /// <summary>
        /// Used only by route recovery after a blocked pairing path. This is
        /// deliberately a safe floor relocation, never a normal selection or
        /// habitat-transfer action.
        /// </summary>
        public void RecoverAtSafeOpenFloor()
        {
            if (rat == null || rat.stage == RatStage.Pinkie) return;
            pairingApproachActive = false;
            pairingApproachArrived = false;
            pairingInteractionActive = false;
            pairingInteractionComplete = false;
            pairingInteractionRemaining = 0f;
            pairingRouteWaypoints.Clear();
            pairingRouteWaypointIndex = 0;
            currentTarget = null;
            transform.position = EnclosureSystem.GetNearestOpenFloorPosition(
                rat.enclosure, transform.position, 0.78f);
            BeginTravel();
        }

        private void UpdatePairingInteraction(float deltaTime)
        {
            FacePairingPoint(deltaTime);
            pairingInteractionRemaining -= deltaTime;
            if (pairingInteractionRemaining <= 0f) pairingInteractionComplete = true;
        }

        private void FacePairingPoint(float deltaTime)
        {
            Vector3 toPartner = pairingFacingPoint - transform.position;
            toPartner.y = 0f;
            if (toPartner.sqrMagnitude <= 0.001f) return;
            // The imported rat mesh faces opposite the stable root's forward
            // axis. Use the same correction as ordinary movement so the nose,
            // rather than the tail, points toward the partner.
            Quaternion facing = RotationFacingWorldDirection(toPartner.normalized);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                facing,
                MovementTurnSpeed * Mathf.Max(0f, deltaTime));
        }

        private static Quaternion RotationFacingWorldDirection(Vector3 direction)
        {
            return Quaternion.LookRotation(direction, Vector3.up) *
                Quaternion.Euler(0f, GameConfig.ImportedRatMovementFacingOffsetDegrees, 0f);
        }

        private void BeginInvestigation()
        {
            if (!CanStartInvestigation())
            {
                // The rat still performs a short target interaction, but the
                // longer investigation animation is throttled per rat.
                EnterState(RatBehaviorState.Interact, NextSniffDuration());
                return;
            }

            MarkInvestigationStarted();
            travelTimer = 0f;
            Vector3 toTarget = currentTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f) investigateRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            EnterState(RatBehaviorState.Investigate, NextSniffDuration());
        }

        private void BeginAmbientInvestigation()
        {
            MarkInvestigationStarted();
            currentTarget = null;
            nestDetourWaypoints.Clear();
            nestDetourWaypointIndex = 0;
            targetPosition = RandomAssignedPosition();
            Vector3 toPoint = targetPosition - transform.position;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude <= 0.001f)
            {
                float angle = NextFloat(0f, Mathf.PI * 2f);
                toPoint = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            investigateRotation = Quaternion.LookRotation(toPoint.normalized, Vector3.up);
            travelTimer = 0f;
            EnterState(RatBehaviorState.Investigate, NextSniffDuration());
        }

        private void UpdateInvestigation(float deltaTime)
        {
            float sweep = Mathf.Sin((stateTimer + travelTimer) * 3.2f) * 24f;
            transform.rotation = Quaternion.Slerp(transform.rotation, investigateRotation * Quaternion.Euler(0f, sweep, 0f), Mathf.Clamp01(deltaTime * 5f));
            travelTimer += deltaTime;
            if (stateTimer <= 0f)
            {
                EnterState(currentTarget == null ? RatBehaviorState.Recover : RatBehaviorState.Interact,
                    currentTarget == null ? NextFloat(0.5f, 1.2f) : NextSniffDuration());
            }
        }

        private void UpdateJump(float deltaTime)
        {
            // The imported jump clip contains the hop/landing pose. Root
            // motion remains disabled, so the stable gameplay root stays put
            // while this short presentation-only action plays.
            if (stateTimer <= 0f)
            {
                EnterState(RatBehaviorState.Recover, NextFloat(0.6f, 1.8f));
            }
        }

        private void UpdateInteraction(float deltaTime)
        {
            if (currentTarget == null)
            {
                EnterState(RatBehaviorState.Recover, NextFloat(0.5f, 1.2f));
                return;
            }

            Vector3 toTarget = currentTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f)
            {
                Quaternion facing = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, facing, Mathf.Clamp01(deltaTime * 4f));
            }

            if (stateTimer <= 0f)
            {
                ReduceNeed(currentTarget.kind);
                if (!string.IsNullOrEmpty(currentTarget.id)) targetCooldowns[currentTarget.id] = behaviorClockSeconds + NextFloat(6f, 16f);
                currentTarget = null;
                bool shouldJump = random != null && random.NextDouble() < 0.08;
                EnterState(shouldJump ? RatBehaviorState.Jump : RatBehaviorState.Recover,
                    shouldJump ? NextFloat(0.75f, 1.15f) : NextFloat(0.6f, 1.8f));
            }
        }

        private void ReduceNeed(RatBehaviorTargetKind kind)
        {
            switch (kind)
            {
                case RatBehaviorTargetKind.Food: foodNeed *= 0.22f; break;
                case RatBehaviorTargetKind.Water: waterNeed *= 0.18f; break;
                case RatBehaviorTargetKind.Nest:
                case RatBehaviorTargetKind.Hide: restNeed *= 0.28f; break;
                default: exploreNeed *= 0.35f; break;
            }
        }

        private void ResolveSpacing()
        {
            if (rat == null || rat.stage == RatStage.Pinkie) return;
            if (pairingApproachActive) return;
            float minimum = rat.stage == RatStage.Adult ? MinimumRatSpacing : MinimumRatSpacing * 0.78f;
            foreach (var other in activeBehaviors)
            {
                if (other == null || other == this || other.rat == null || other.rat.stage == RatStage.Pinkie) continue;
                if (other.pairingApproachActive) continue;
                Vector3 offset = transform.position - other.transform.position;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance <= 0.001f || distance >= minimum) continue;
                if (other.rat.enclosure != rat.enclosure) continue;
                float spacingDelta = GrowthSystem.SimulationMovementDeltaSeconds(Time.unscaledDeltaTime);
                float correction = (minimum - distance) * Mathf.Clamp01(spacingDelta * 5f);
                transform.position = ClampToAssignedEnclosure(
                    transform.position + offset.normalized * correction);
            }
        }

        private void EnterState(RatBehaviorState next, float duration)
        {
            state = next;
            stateTimer = Mathf.Max(0.05f, duration);
            travelTimer = 0f;
            movementSpeed = next == RatBehaviorState.Run
                ? NextFloat(RunSpeed * 0.88f, RunSpeed * 1.08f)
                : IsWalkingState(next)
                    ? NextFloat(MinimumWalkSpeed, MaximumWalkSpeed) * WalkMovementScale
                    : NextFloat(MinimumWalkSpeed, MaximumWalkSpeed);
            switch (next)
            {
                case RatBehaviorState.Idle:
                    // The logical stationary state now uses the imported
                    // reaction/sniff motion. Pinkies never reach this method
                    // because they are excluded in Configure and by Presenter.
                    PlayMotionFromStart("HandPaintedRat_IdleReaction", "IdleReaction");
                    break;
                case RatBehaviorState.Wander:
                case RatBehaviorState.WalkToTarget:
                    PlayAvailableMotion("HandPaintedRat_Walk", 0.12f, WalkAnimationPlaybackScale, "Walk");
                    break;
                case RatBehaviorState.Run:
                    PlayAvailableMotion("HandPaintedRat_Run", 0.1f, 1f, "Run", "HandPaintedRat_Walk", "Walk");
                    break;
                case RatBehaviorState.Investigate:
                    PlayMotionFromStart("HandPaintedRat_IdleReaction", "IdleReaction");
                    break;
                case RatBehaviorState.Interact:
                    PlayMotionFromStart("HandPaintedRat_Sniffing", "Sniffing");
                    break;
                case RatBehaviorState.Jump:
                    PlayMotionFromStart("HandPaintedRat_Jump", "Jump");
                    break;
                case RatBehaviorState.Dying:
                    // The imported death range ends at source frame 192. The
                    // state is held on that final pose after the one-shot.
                    PlayMotionFromStart("HandPaintedRat_Dying", "Dying");
                    break;
                case RatBehaviorState.Recover:
                    HoldStaticPose();
                    break;
            }
        }

        private bool IsWalkingState(RatBehaviorState behaviorState)
        {
            return behaviorState == RatBehaviorState.Wander || behaviorState == RatBehaviorState.WalkToTarget;
        }

        private float NextIdleDuration(float minimum, float maximum)
        {
            return NextFloat(minimum * IdleDwellScale, maximum * IdleDwellScale);
        }

        private float NextSniffDuration()
        {
            return NextFloat(MinimumSniffDuration, MaximumSniffDuration);
        }

        private bool CanStartInvestigation()
        {
            return behaviorClockSeconds >= nextInvestigationAllowedAt;
        }

        private bool ShouldStartAmbientInvestigation(float chance)
        {
            return CanStartInvestigation() && random != null && random.NextDouble() < chance;
        }

        private void MarkInvestigationStarted()
        {
            nextInvestigationAllowedAt = behaviorClockSeconds + NextFloat(4.5f, 10.5f);
        }

        private void ApplySimulationAnimationSpeed()
        {
            if (animator == null || !animator.enabled) return;

            float statePlaybackScale = state == RatBehaviorState.Wander ||
                state == RatBehaviorState.WalkToTarget
                ? WalkAnimationPlaybackScale
                : 1f;
            animator.speed = statePlaybackScale * GrowthSystem.RuntimeSimulationSpeed;
        }

        private void PlayAvailableMotion(string preferredState, float blendSeconds, float playbackSpeed, params string[] fallbacks)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.enabled = true;
            string[] names = new string[1 + (fallbacks == null ? 0 : fallbacks.Length)];
            names[0] = preferredState;
            if (fallbacks != null)
            {
                for (int i = 0; i < fallbacks.Length; i++) names[i + 1] = fallbacks[i];
            }

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                int fullHash = Animator.StringToHash("Base Layer." + name);
                int shortHash = Animator.StringToHash(name);
                if (animator.HasState(0, fullHash))
                {
                    animator.speed = playbackSpeed * GrowthSystem.RuntimeSimulationSpeed;
                    // Movement clips must always begin at their first frame so
                    // source-range changes remain observable in gameplay.
                    animator.CrossFadeInFixedTime(fullHash, blendSeconds, 0, 0f);
                    LogAnimationStart(name, 0f);
                    return;
                }
                if (animator.HasState(0, shortHash))
                {
                    animator.speed = playbackSpeed * GrowthSystem.RuntimeSimulationSpeed;
                    animator.CrossFadeInFixedTime(shortHash, blendSeconds, 0, 0f);
                    LogAnimationStart(name, 0f);
                    return;
                }
            }

            HoldStaticPose();
        }

        private void PlayMotionFromStart(string preferredState, params string[] fallbacks)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.enabled = true;
            string[] names = new string[1 + (fallbacks == null ? 0 : fallbacks.Length)];
            names[0] = preferredState;
            if (fallbacks != null)
            {
                for (int i = 0; i < fallbacks.Length; i++) names[i + 1] = fallbacks[i];
            }

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                int fullHash = Animator.StringToHash("Base Layer." + name);
                int shortHash = Animator.StringToHash(name);
                if (animator.HasState(0, fullHash))
                {
                    animator.speed = GrowthSystem.RuntimeSimulationSpeed;
                    animator.Play(fullHash, 0, 0f);
                    LogAnimationStart(name, 0f);
                    return;
                }
                if (animator.HasState(0, shortHash))
                {
                    animator.speed = GrowthSystem.RuntimeSimulationSpeed;
                    animator.Play(shortHash, 0, 0f);
                    LogAnimationStart(name, 0f);
                    return;
                }
            }

            HoldStaticPose();
        }

        private void LogAnimationStart(string clipName, float normalizedStart)
        {
            // Animation transitions are routine presentation work, not player
            // alerts. Keep this hook for future opt-in animation diagnostics
            // without flooding the Console during normal play.
        }

        private void HoldStaticPose()
        {
            if (animator == null) return;
            // Rebind before pausing so a rat that just left Walk/Run does not
            // freeze halfway through a stride. This is a static reference pose,
            // not a renamed or substituted animation clip.
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            animator.enabled = false;
            animator.speed = 0f;
            ClearMicroAnimationOffsets();
        }

        private void HoldDyingPoseAtEnd()
        {
            if (deathPoseHeld || animator == null || animator.runtimeAnimatorController == null) return;
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (!IsDyingState(stateInfo) || stateInfo.normalizedTime < 0.999f) return;

            // A normalized time just under one samples the final imported
            // frame (source frame 191/192) without advancing into a loop.
            animator.Play(stateInfo.fullPathHash, 0, 0.999999f);
            animator.Update(0f);
            animator.speed = 0f;
            deathPoseHeld = true;
        }

        private static bool IsDyingState(AnimatorStateInfo stateInfo)
        {
            return stateInfo.IsName("HandPaintedRat_Dying") || stateInfo.IsName("Dying") ||
                stateInfo.IsName("Base Layer.HandPaintedRat_Dying") || stateInfo.IsName("Base Layer.Dying");
        }

        private void AuditAnimatorOnce()
        {
            if (clipAuditLogged || animator == null || animator.runtimeAnimatorController == null) return;
            clipAuditLogged = true;
            var clips = animator.runtimeAnimatorController.animationClips;
            var names = new List<string>();
            if (clips != null)
            {
                foreach (var clip in clips)
                {
                    if (clip != null && !names.Contains(clip.name)) names.Add(clip.name);
                }
            }
            var missing = new List<string>();
            string[] optional =
            {
                "HandPaintedRat_Walk", "HandPaintedRat_Run", "HandPaintedRat_IdleReaction",
                "HandPaintedRat_Sniffing", "HandPaintedRat_Jump", "HandPaintedRat_Attack", "HandPaintedRat_Dying"
            };
            foreach (string optionalName in optional)
            {
                if (!names.Contains(optionalName)) missing.Add(optionalName);
            }
            Debug.Log("[Rat Habitat] Imported rat animation audit: rat=" + (rat == null ? "unknown" : rat.id) +
                " rootMotion=" + animator.applyRootMotion + " usableClips=" + string.Join(", ", names.ToArray()) +
                " missingOptional=" + string.Join(", ", missing.ToArray()));
        }

        private bool ShouldRun()
        {
            return random != null && random.NextDouble() < 0.12;
        }

        private Vector3 RandomAssignedPosition()
        {
            RatEnclosure enclosure = rat == null ? RatEnclosure.FemaleColony : rat.enclosure;
            EnclosureSystem.Definition definition = EnclosureSystem.GetDefinition(enclosure);
            Vector3 position = new Vector3(
                NextFloat(definition.minX + 0.48f, definition.maxX - 0.48f),
                transform.position.y,
                NextFloat(definition.minZ + 0.48f, definition.maxZ - 0.48f));
            return ClampToNestSafePosition(position);
        }

        private Vector3 ClampToAssignedEnclosure(Vector3 position)
        {
            return EnclosureSystem.ClampToEnclosureBounds(
                rat == null ? RatEnclosure.FemaleColony : rat.enclosure, position);
        }

        private Vector3 ClampToNestSafePosition(Vector3 position)
        {
            return EnclosureSystem.ClampToEnclosure(
                rat == null ? RatEnclosure.FemaleColony : rat.enclosure, position);
        }

        /// <summary>
        /// Keeps ambient and courtship movement outside the nest without
        /// correcting the current root position. If a direct step would enter
        /// the nest, the rat receives a short waypoint around the nearest side
        /// and continues walking there naturally.
        /// </summary>
        private Vector3 MoveTowardAvoidingNest(Vector3 current, Vector3 desired)
        {
            RatEnclosure enclosure = rat == null ? RatEnclosure.FemaleColony : rat.enclosure;
            if (!EnclosureSystem.HasNest(enclosure)) return ClampToAssignedEnclosure(desired);

            if (nestDetourWaypointIndex < nestDetourWaypoints.Count)
            {
                Vector3 waypoint = nestDetourWaypoints[nestDetourWaypointIndex];
                Vector3 toWaypoint = waypoint - current;
                toWaypoint.y = 0f;
                if (toWaypoint.sqrMagnitude <= ArrivalDistance * ArrivalDistance)
                {
                    nestDetourWaypointIndex++;
                    if (nestDetourWaypointIndex >= nestDetourWaypoints.Count)
                    {
                        nestDetourWaypoints.Clear();
                        nestDetourWaypointIndex = 0;
                        return ClampToAssignedEnclosure(desired);
                    }
                    waypoint = nestDetourWaypoints[nestDetourWaypointIndex];
                    toWaypoint = waypoint - current;
                    toWaypoint.y = 0f;
                }
                if (toWaypoint.sqrMagnitude <= 0.0001f) return ClampToAssignedEnclosure(current);
                float detourStep = GrowthSystem.SimulationMovementStep(
                    movementSpeed, Time.unscaledDeltaTime);
                return ClampToAssignedEnclosure(current + toWaypoint.normalized * Mathf.Min(detourStep, toWaypoint.magnitude));
            }

            bool currentInside = EnclosureSystem.IsInsideAdultNestExclusion(enclosure, current, 0.04f);
            Vector3 finalDestination = targetPosition;
            finalDestination.y = current.y;
            bool destinationInside = EnclosureSystem.IsInsideAdultNestExclusion(enclosure, finalDestination, 0.04f);
            if (!currentInside && !destinationInside &&
                EnclosureSystem.IsNestSafeRoute(enclosure, current, finalDestination, 0.04f))
            {
                return ClampToAssignedEnclosure(desired);
            }

            if (currentInside)
            {
                // A stale save or a previously rejected route may leave the
                // root on the obstacle. Walk to the nearest open floor edge;
                // do not snap it to a habitat spawn point.
                nestDetourWaypoints.Add(EnclosureSystem.GetNearestOpenFloorPosition(
                    enclosure, current, 0.48f));
            }
            else if (!TryBuildNestDetour(enclosure, current, finalDestination))
            {
                // A defensive fallback keeps the rat moving along a safe
                // edge even if a future nest or enclosure is too constrained
                // for a two-corner route.
                nestDetourWaypoints.Add(EnclosureSystem.GetNearestOpenFloorPosition(
                    enclosure, current, 0.48f));
            }

            nestDetourWaypointIndex = 0;
            if (nestDetourWaypoints.Count == 0) return ClampToAssignedEnclosure(desired);
            Vector3 firstWaypoint = nestDetourWaypoints[0];
            Vector3 toFirstWaypoint = firstWaypoint - current;
            toFirstWaypoint.y = 0f;
            if (toFirstWaypoint.sqrMagnitude <= 0.0001f) return ClampToAssignedEnclosure(current);
            float step = GrowthSystem.SimulationMovementStep(
                movementSpeed, Time.unscaledDeltaTime);
            return ClampToAssignedEnclosure(current + toFirstWaypoint.normalized * Mathf.Min(step, toFirstWaypoint.magnitude));
        }

        private bool TryBuildNestDetour(RatEnclosure enclosure, Vector3 start, Vector3 end)
        {
            Vector3 nest;
            float radiusX;
            float radiusZ;
            GetNestExclusion(enclosure, out nest, out radiusX, out radiusZ);
            Bounds bounds = new Bounds(nest, new Vector3(radiusX * 2f, 1f, radiusZ * 2f));
            if (enclosure == RatEnclosure.Pairing)
            {
                Bounds actualBounds;
                if (EnclosureSystem.TryGetPairingNestAvoidanceBounds(0.24f, out actualBounds)) bounds = actualBounds;
            }
            else
            {
                bounds.Expand(new Vector3(0.24f, 0f, 0.24f));
            }

            Vector3[] rawCorners =
            {
                new Vector3(bounds.min.x, start.y, bounds.min.z),
                new Vector3(bounds.min.x, start.y, bounds.max.z),
                new Vector3(bounds.max.x, start.y, bounds.min.z),
                new Vector3(bounds.max.x, start.y, bounds.max.z),
            };
            Vector3[] corners = new Vector3[rawCorners.Length];
            for (int index = 0; index < rawCorners.Length; index++)
            {
                corners[index] = EnclosureSystem.ClampToEnclosureBounds(enclosure, rawCorners[index], 0.48f);
            }

            var best = new List<Vector3>();
            float bestLength = float.MaxValue;
            for (int first = 0; first < corners.Length; first++)
            {
                if (!IsNestDetourPointAllowed(enclosure, corners[first])) continue;
                if (EnclosureSystem.IsNestSafeRoute(enclosure, start, corners[first], 0.02f) &&
                    EnclosureSystem.IsNestSafeRoute(enclosure, corners[first], end, 0.02f))
                {
                    ConsiderNestDetour(new List<Vector3> { corners[first] }, start, end, ref best, ref bestLength);
                }
                for (int second = 0; second < corners.Length; second++)
                {
                    if (first == second || !IsNestDetourPointAllowed(enclosure, corners[second])) continue;
                    if (!EnclosureSystem.IsNestSafeRoute(enclosure, start, corners[first], 0.02f) ||
                        !EnclosureSystem.IsNestSafeRoute(enclosure, corners[first], corners[second], 0.02f) ||
                        !EnclosureSystem.IsNestSafeRoute(enclosure, corners[second], end, 0.02f)) continue;
                    ConsiderNestDetour(new List<Vector3> { corners[first], corners[second] },
                        start, end, ref best, ref bestLength);
                }
            }

            if (best.Count == 0) return false;
            nestDetourWaypoints.AddRange(best);
            return true;
        }

        private static void ConsiderNestDetour(List<Vector3> candidate, Vector3 start, Vector3 end,
            ref List<Vector3> best, ref float bestLength)
        {
            float length = Vector3.Distance(start, candidate[0]);
            for (int index = 1; index < candidate.Count; index++)
                length += Vector3.Distance(candidate[index - 1], candidate[index]);
            length += Vector3.Distance(candidate[candidate.Count - 1], end);
            if (length < bestLength)
            {
                bestLength = length;
                best = candidate;
            }
        }

        private static bool IsNestDetourPointAllowed(RatEnclosure enclosure, Vector3 point)
        {
            return EnclosureSystem.IsInside(enclosure, point, 0.48f) &&
                !EnclosureSystem.IsInsideAdultNestExclusion(enclosure, point, 0.03f);
        }

        private static void GetNestExclusion(RatEnclosure enclosure, out Vector3 center,
            out float radiusX, out float radiusZ)
        {
            center = EnclosureSystem.GetNestPosition(enclosure);
            radiusX = enclosure == RatEnclosure.Pairing
                ? EnclosureSystem.PairingNestAdultExclusionRadiusX
                : EnclosureSystem.NestAdultExclusionRadiusX;
            radiusZ = enclosure == RatEnclosure.Pairing
                ? EnclosureSystem.PairingNestAdultExclusionRadiusZ
                : EnclosureSystem.NestAdultExclusionRadiusZ;

            Bounds bounds;
            if (enclosure == RatEnclosure.Pairing &&
                EnclosureSystem.TryGetPairingNestAvoidanceBounds(0f, out bounds))
            {
                center = bounds.center;
                radiusX = bounds.extents.x + 0.02f;
                radiusZ = bounds.extents.z + 0.02f;
            }
        }

        private float NextFloat(float minimum, float maximum)
        {
            if (random == null) return (minimum + maximum) * 0.5f;
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        private static int StableSeed(string value)
        {
            unchecked
            {
                int hash = 17;
                string source = string.IsNullOrEmpty(value) ? "rat" : value;
                for (int i = 0; i < source.Length; i++) hash = hash * 31 + source[i];
                return hash & 0x7fffffff;
            }
        }
    }
}
