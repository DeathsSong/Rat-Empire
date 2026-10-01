using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Keeps stable selectable rat roots while delegating stage visuals to the
    /// replaceable RatVisualFactory. Stable roots are important: the existing
    /// InteractionManager and its colliders continue to work while a visual
    /// child is replaced or animated.
    /// </summary>
    public class RatPresenter : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> ratRoots = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, RatVisualController> visualControllers = new Dictionary<string, RatVisualController>();
        private readonly Dictionary<string, RatHabitatBehavior> behaviors = new Dictionary<string, RatHabitatBehavior>();
        private readonly Dictionary<string, RatData> liveRats = new Dictionary<string, RatData>();
        // Pinkies are static nest occupants.  Keep a runtime world-space
        // anchor per stable rat ID so animation evaluation, nursing checks,
        // and harmless UI/presentation refreshes cannot re-solve their X/Z
        // position and make them drift around the bedding.
        private readonly Dictionary<string, Vector3> pinkieNestAnchors = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, RatEnclosure> pinkieNestAnchorEnclosures =
            new Dictionary<string, RatEnclosure>();
        // Pinkies are static nest occupants. Their rendered bounds are only
        // needed when the visual changes or when an external system has
        // actually moved the root. Avoid doing a full skinned-renderer bounds
        // solve for every pinkie on every rendered frame.
        private readonly Dictionary<string, int> pinkieGroundedSelectionBoundsVersions =
            new Dictionary<string, int>();
        // The imported pinkie visual has an authored local facing correction.
        // Keep the deterministic litter pose keyed to the actual visual object
        // so a render refresh does not multiply the same rotation again.
        private readonly Dictionary<string, GameObject> pinkiePoseVisuals = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, int> configuredSelectionBoundsVersions = new Dictionary<string, int>();
        private RatVisualFactory visualFactory;
        private HabitatBuilder habitat;
        private float groundingRefreshTimer;
        private const float GroundingRefreshIntervalSeconds = 0.075f;
        private float pinkieGroundingRefreshTimer;
        private const float PinkieGroundingRefreshIntervalSeconds = 0.35f;
        private float presentationCullingTimer;
        private const float PresentationCullingIntervalSeconds = 0.20f;
        private float ageScaleRefreshTimer;
        private const float AgeScaleRefreshIntervalSeconds = 0.10f;
        // A malformed saved rat or an optional visual asset must not abort the
        // entire presentation pass. Keep the warning once per stable ID so a
        // late-game save cannot flood the WebGL console every frame.
        private readonly HashSet<string> presentationFailureWarnings = new HashSet<string>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly HashSet<string> pinkiePlacementDiagnostics = new HashSet<string>();
        private readonly HashSet<string> pinkiePlacementWarnings = new HashSet<string>();
        private readonly HashSet<string> pinkieInputIsolationAudits = new HashSet<string>();
        private readonly Dictionary<string, Vector3> lastPinkieGroundedPositions = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, int> lastPinkieGroundedFrames = new Dictionary<string, int>();
#endif

        public void ConfigureHabitat(HabitatBuilder builder)
        {
            habitat = builder;
            foreach (var item in behaviors)
            {
                if (item.Value != null) item.Value.ConfigureHabitat(habitat);
            }
        }

        public void ConfigureVisualFactory(RatVisualFactory factory)
        {
            visualFactory = factory;
            foreach (var item in visualControllers)
            {
                if (item.Value != null) item.Value.Configure(EnsureVisualFactory());
            }
        }

        public void Render(ColonySaveData save, Vector3 nestPosition)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long performanceSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.RatPresentationBuild);
            try { RenderCore(save, nestPosition); }
            finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.RatPresentationBuild, performanceSample); }
#else
            RenderCore(save, nestPosition);
#endif
        }

        private void RenderCore(ColonySaveData save, Vector3 nestPosition)
        {
            try
            {
                EnsureVisualFactory();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[Rat Habitat] Rat visual factory could not initialize; habitat geometry and UI will remain available. " + exception);
                return;
            }
            if (save == null)
            {
                ClearRats();
                return;
            }

            // Placement is relationship-driven (pregnancy, dependent
            // Pinkies, and stage/sex). Reconcile it before reusing any stable
            // visual roots so a render never leaves a rat in its old zone.
            try
            {
                EnclosureSystem.RecalculateAssignments(save);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[Rat Habitat] Rat enclosure reconciliation failed; preserving saved placements for this render. " + exception);
            }
            var liveIds = new HashSet<string>();
            var pinkieSlotById = BuildPinkieSlotMap(save.rats);
            int pinkieIndex = 0;
            int maleIndex = 0;
            int femaleIndex = 0;
            int nurseryIndex = 0;
            int breedingIndex = 0;
            int pairingIndex = 0;
            foreach (var rat in save.rats)
            {
                if (rat == null || string.IsNullOrEmpty(rat.id)) continue;
                liveIds.Add(rat.id);
                try
                {
                    liveRats[rat.id] = rat;
                    Vector3 position = GetPosition(rat, nestPosition, pinkieSlotById, ref pinkieIndex, ref maleIndex, ref femaleIndex, ref nurseryIndex, ref breedingIndex, ref pairingIndex);

                    GameObject root;
                    RatVisualController controller;
                    if (!ratRoots.TryGetValue(rat.id, out root) || root == null || !visualControllers.TryGetValue(rat.id, out controller) || controller == null)
                    {
                        CreateRatRoot(rat, position, out root, out controller);
                    }
                    else
                    {
                        // Rendering can be requested by a simulation-only update,
                        // such as the Pairing Habitat's 30-second pregnancy
                        // check. Preserve the live root position while the rat
                        // remains in its current enclosure so a UI/world refresh
                        // cannot teleport every rat back to its spawn slot.
                        // Reposition only when the current root is outside the
                        // authoritative enclosure (for example, after a manual
                        // move, pregnancy relocation, birth, or growth).
                        if (rat.stage == RatStage.Pinkie)
                        {
                            // Pinkies are nest-bound.  Use the deterministic
                            // slot only until the first renderer-bounds placement
                            // settles the pup, then preserve that exact world
                            // anchor across later renders.
                            Vector3 pinkieAnchor;
                            if (TryGetPinkieNestAnchor(rat, out pinkieAnchor))
                                ResetPinkieRootTransform(root, pinkieAnchor, rat);
                            else
                                ResetPinkieRootTransform(root, position, rat);
                        }
                        else if (!EnclosureSystem.IsInside(rat.enclosure, root.transform.position, 0f))
                        {
                            // Do not snap an adult or young rat out of the nest
                            // during a harmless render refresh. RatHabitatBehavior
                            // steers its next movement around the nest instead.
                            root.transform.position = position;
                        }
                        else if (Mathf.Abs(root.transform.position.y - position.y) > 0.02f)
                        {
                            // Correct only the enclosure-specific vertical plane
                            // reference. Preserve the live X/Z position so a
                            // pairing check or UI refresh cannot reset movement.
                            root.transform.position = new Vector3(
                                root.transform.position.x,
                                position.y,
                                root.transform.position.z);
                        }
                        root.name = rat.name + " Rat";
                        ConfigureRatCollider(root, rat.stage);
                        RemoveLegacySelectionMarker(root);
                    }

                    if (rat.stage != RatStage.Pinkie)
                    {
                        // A pinkie anchor is presentation-only and must not
                        // survive the exact 7-day transition into a moving
                        // Young Rat, or be reused if an old save migrates a
                        // rat back into the pinkie stage later.
                        pinkieNestAnchors.Remove(rat.id);
                        pinkieNestAnchorEnclosures.Remove(rat.id);
                        pinkieGroundedSelectionBoundsVersions.Remove(rat.id);
                    }

                    controller.Configure(visualFactory);
                    // A controller keeps the visual child between renders. When a
                    // saved stage changes, it performs the configured smooth
                    // pinkie->young or young->adult transition.
                    controller.Apply(rat, true);
                    if (rat.stage == RatStage.Pinkie)
                        EnsurePinkieInputIsolation(root, rat);
                    ConfigureRatCollider(root, rat.stage, controller);
                    // Pinkies are nest-bound and never receive a behavior
                    // component. Attach/configure movement only after the visual
                    // stage has been confirmed to be Young or Adult.
                    if (rat.stage == RatStage.Pinkie)
                    {
                        ApplyDeterministicPinkiePose(rat, root, controller);
                        // Ground the newborn before NursingSystem or any other
                        // birth-frame consumer can read its transform. The
                        // late pass repeats this after Animator evaluation.
                        PlacePinkieOnNest(rat, root, controller);
                    }
                    EnsureBehaviorForStage(root, rat);
                    configuredSelectionBoundsVersions[rat.id] = controller.SelectionBoundsVersion;
                }
                catch (System.Exception exception)
                {
                    if (presentationFailureWarnings.Add(rat.id))
                        Debug.LogError("[Rat Habitat] Rat presentation failed for '" + (rat.name ?? rat.id) + "' (" + rat.id + "). The saved rat was kept and the remaining colony will continue rendering. " + exception);
                }
            }

            RemoveMissingRats(liveIds);
            GrowthSystem.SetBehaviorParticipantCount(behaviors.Count);
            RestoreSavedNursingInteractions(save);
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long performanceSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.RatPresenterLateUpdate);
            try
            {
                LateUpdateCore();
            }
            finally
            {
                RuntimePerformanceDiagnostics.End(PerformanceProbeArea.RatPresenterLateUpdate, performanceSample);
            }
#else
            LateUpdateCore();
#endif
        }

        private void LateUpdateCore()
        {
            // Animator deformation and code-driven movement both occur before
            // this point in the frame. Keep the stable selection surfaces
            // aligned with the currently visible mesh instead of leaving a
            // stale pose-sized hitbox below or beside an animated rat.
            groundingRefreshTimer -= Time.unscaledDeltaTime;
            bool refreshGrounding = groundingRefreshTimer <= 0f;
            if (refreshGrounding) groundingRefreshTimer = GroundingRefreshIntervalSeconds;
            pinkieGroundingRefreshTimer -= Time.unscaledDeltaTime;
            bool refreshPinkieGrounding = pinkieGroundingRefreshTimer <= 0f;
            if (refreshPinkieGrounding) pinkieGroundingRefreshTimer = PinkieGroundingRefreshIntervalSeconds;
            ageScaleRefreshTimer -= Time.unscaledDeltaTime;
            bool refreshAgeScale = ageScaleRefreshTimer <= 0f;
            if (refreshAgeScale) ageScaleRefreshTimer = AgeScaleRefreshIntervalSeconds;
            presentationCullingTimer -= Time.unscaledDeltaTime;
            bool refreshPresentationCulling = presentationCullingTimer <= 0f;
            if (refreshPresentationCulling) presentationCullingTimer = PresentationCullingIntervalSeconds;

            Camera presentationCamera = refreshPresentationCulling ? Camera.main : null;
            Plane[] presentationPlanes = presentationCamera == null
                ? null
                : GeometryUtility.CalculateFrustumPlanes(presentationCamera);

            foreach (var item in visualControllers)
            {
                RatVisualController controller = item.Value;
                GameObject root;
                if (controller == null || !ratRoots.TryGetValue(item.Key, out root) || root == null) continue;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                controller.SetPerformanceIsolation(
                    RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.RatRendering),
                    RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.RatShadows));
#endif

                RatData rat;
                GameObject currentVisual;
                if (refreshAgeScale && liveRats.TryGetValue(item.Key, out rat) &&
                    controller.TryGetCurrentVisual(out currentVisual))
                {
                    // Age changes every simulation tick, not only when a
                    // stage label changes. Apply the shared age curve before
                    // bounds/grounding work so the live model grows smoothly
                    // and its selection surface follows the same scale.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long boundsSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.GroundingAndBounds);
                    try { controller.ApplyAgeScale(rat); }
                    finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.GroundingAndBounds, boundsSample); }
#else
                    controller.ApplyAgeScale(rat);
#endif
                }

                if (liveRats.TryGetValue(item.Key, out rat) &&
                    controller.TryGetCurrentVisual(out currentVisual))
                {
                    if (rat.stage == RatStage.Pinkie)
                    {
                        // Pinkies are static nest occupants. Re-ground on a
                        // bounded cadence, on a visual-size change, or when
                        // another system has actually changed their root.
                        // This preserves the runtime safety check without
                        // forcing every pinkie through multiple skinned
                        // Renderer.bounds/overlap passes every frame.
                        if (refreshPinkieGrounding || NeedsPinkiePlacementRefresh(rat, root, controller))
                        {
                            AuditPinkieRootBeforePlacement(rat, root);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            long boundsSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.GroundingAndBounds);
                            try { PlacePinkieOnNest(rat, root, controller); }
                            finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.GroundingAndBounds, boundsSample); }
#else
                            PlacePinkieOnNest(rat, root, controller);
#endif
                        }
                    }
                    else if (refreshGrounding)
                    {
                        // Keep animation-driven feet/tails from dipping below
                        // the actual Pairing cage floor after the render pass.
                        // This changes only the visual child and never the
                        // stable gameplay root.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        long boundsSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.GroundingAndBounds);
                        try { visualFactory.KeepPairingVisualGrounded(currentVisual, rat, controller); }
                        finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.GroundingAndBounds, boundsSample); }
#else
                        visualFactory.KeepPairingVisualGrounded(currentVisual, rat, controller);
#endif
                    }
                }

                if (refreshPresentationCulling)
                    controller.UpdatePresentationCulling(presentationCamera, presentationPlanes);

                int configuredBoundsVersion;
                bool boundsVersionChanged = !configuredSelectionBoundsVersions.TryGetValue(item.Key, out configuredBoundsVersion) ||
                    configuredBoundsVersion != controller.SelectionBoundsVersion;

                // Animation can change renderer bounds without changing the
                // visual/stage version. Refresh at the same bounded cadence as
                // grounding so the hitbox follows the current visible pose,
                // while avoiding a GetComponents/Collider rebuild every frame.
                // The grounding pass above is intentionally first so a visual
                // lift can never leave its hitbox behind on the floor.
                if (refreshGrounding || boundsVersionChanged)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long boundsSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.GroundingAndBounds);
                    try
                    {
                        Bounds bounds;
                        if (controller.TryGetSelectionBounds(out bounds))
                        {
                            ConfigureRatCollider(root, controller.CurrentStage, controller);
                            configuredSelectionBoundsVersions[item.Key] = controller.SelectionBoundsVersion;
                        }
                    }
                    finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.GroundingAndBounds, boundsSample); }
#else
                    Bounds bounds;
                    if (controller.TryGetSelectionBounds(out bounds))
                    {
                        ConfigureRatCollider(root, controller.CurrentStage, controller);
                        configuredSelectionBoundsVersions[item.Key] = controller.SelectionBoundsVersion;
                    }
#endif
                }
            }
        }

        public void ApplyPerformanceIsolationMode()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool hideRenderers = RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.RatRendering);
            bool disableShadows = RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.RatShadows);
            foreach (RatVisualController controller in visualControllers.Values)
                if (controller != null) controller.SetPerformanceIsolation(hideRenderers, disableShadows);
#endif
        }

        public void SetSelected(string id)
        {
            // Selection is intentionally data-only. Keep this compatibility
            // entry point because GameBootstrap uses it to synchronize the
            // selected rat, but never create or show a world marker.
        }

        public void SetSelectedGroup(IEnumerable<string> ids)
        {
            // Multi-select uses the UI list/checkmarks and stable IDs. It does
            // not add a persistent visual effect to the habitat rats.
        }

        public RatVisualFactory GetVisualFactory()
        {
            return EnsureVisualFactory();
        }

        /// <summary>
        /// Pinkies are world-only presentation objects. Imported FBX variants
        /// can carry stale authoring components or helper colliders even when
        /// the source prefab does not show them in the Inspector. Quarantine
        /// those components at the stable rat root so they can never become a
        /// second UI/event-input path while a newborn is alive.
        /// </summary>
        private void EnsurePinkieInputIsolation(GameObject root, RatData rat)
        {
            if (root == null || rat == null || rat.stage != RatStage.Pinkie) return;

            int disabledUiComponents = 0;
            int disabledColliders = 0;
            int disabledRaycasters = 0;
            int uiLayer = LayerMask.NameToLayer("UI");
            foreach (UnityEngine.UI.Graphic graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                if (graphic == null) continue;
                if (graphic.raycastTarget)
                {
                    graphic.raycastTarget = false;
                    disabledUiComponents++;
                }
            }
            foreach (UnityEngine.UI.GraphicRaycaster raycaster in root.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true))
            {
                if (raycaster == null) continue;
                if (raycaster.enabled)
                {
                    raycaster.enabled = false;
                    disabledUiComponents++;
                }
            }
            foreach (UnityEngine.EventSystems.EventTrigger trigger in root.GetComponentsInChildren<UnityEngine.EventSystems.EventTrigger>(true))
            {
                if (trigger == null) continue;
                if (trigger.enabled)
                {
                    trigger.enabled = false;
                    disabledUiComponents++;
                }
            }
            // A runtime-created pinkie must never introduce another EventSystem
            // raycaster. In particular, a PhysicsRaycaster on an imported or
            // diagnostic child can sort ahead of the Canvas raycaster and
            // intercept a roster gesture even though the pinkie is not UI.
            foreach (UnityEngine.EventSystems.BaseRaycaster raycaster in
                     root.GetComponentsInChildren<UnityEngine.EventSystems.BaseRaycaster>(true))
            {
                if (raycaster == null) continue;
                if (raycaster.enabled)
                {
                    raycaster.enabled = false;
                    disabledRaycasters++;
                }
            }
            foreach (UnityEngine.Canvas canvas in root.GetComponentsInChildren<UnityEngine.Canvas>(true))
            {
                if (canvas == null) continue;
                if (canvas.enabled)
                {
                    canvas.enabled = false;
                    disabledUiComponents++;
                }
            }
            foreach (UnityEngine.CanvasGroup group in root.GetComponentsInChildren<UnityEngine.CanvasGroup>(true))
            {
                if (group == null) continue;
                if (group.blocksRaycasts || group.interactable)
                {
                    group.blocksRaycasts = false;
                    group.interactable = false;
                    disabledUiComponents++;
                }
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.GetComponent<RatSelectionCollider>() != null) continue;
                if (collider.enabled)
                {
                    collider.enabled = false;
                    disabledColliders++;
                }
            }

            // A stale imported child on the UI layer is not a valid gameplay
            // input surface. Keep the existing world layer for normal meshes,
            // but repair only the accidental UI-layer case.
            if (uiLayer >= 0)
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child != null && child.gameObject.layer == uiLayer)
                        child.gameObject.layer = root.layer;
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Debug.isDebugBuild && pinkieInputIsolationAudits.Add(rat.id ?? string.Empty))
            {
                Debug.Log("[Rat Habitat] Pinkie input isolation: ratId=" + rat.id +
                    " name=" + rat.name +
                    " uiDisabled=" + disabledUiComponents +
                    " helperCollidersDisabled=" + disabledColliders +
                    " raycastersDisabled=" + disabledRaycasters +
                    " selectionColliders=" + root.GetComponentsInChildren<RatSelectionCollider>(true).Length +
                    " parent=" + (root.transform.parent == null ? "<none>" : root.transform.parent.name));
            }
#endif
        }

        /// <summary>
        /// Development-only snapshot used by UI pointer diagnostics. It is
        /// intentionally read-only and reports the live pinkie hierarchy,
        /// collider surface, and any accidental UI components.
        /// </summary>
        public string PinkieInputDiagnostic()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int activePinkies = 0;
            int graphics = 0;
            int raycastTargets = 0;
            int canvases = 0;
            int eventTriggers = 0;
            int raycasters = 0;
            int enabledColliders = 0;
            foreach (RatData rat in liveRats.Values)
            {
                if (rat == null || rat.stage != RatStage.Pinkie) continue;
                activePinkies++;
                GameObject root;
                if (!ratRoots.TryGetValue(rat.id, out root) || root == null) continue;
                UnityEngine.UI.Graphic[] pinkieGraphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                graphics += pinkieGraphics.Length;
                foreach (UnityEngine.UI.Graphic graphic in pinkieGraphics)
                    if (graphic != null && graphic.raycastTarget) raycastTargets++;
                canvases += root.GetComponentsInChildren<UnityEngine.Canvas>(true).Length;
                eventTriggers += root.GetComponentsInChildren<UnityEngine.EventSystems.EventTrigger>(true).Length;
                foreach (UnityEngine.EventSystems.BaseRaycaster raycaster in
                         root.GetComponentsInChildren<UnityEngine.EventSystems.BaseRaycaster>(true))
                    if (raycaster != null && raycaster.isActiveAndEnabled) raycasters++;
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    if (collider != null && collider.enabled) enabledColliders++;
            }
            return "count=" + activePinkies + " graphics=" + graphics +
                " raycastTargets=" + raycastTargets + " canvases=" + canvases +
                " eventTriggers=" + eventTriggers + " activeRaycasters=" + raycasters +
                " enabledColliders=" + enabledColliders;
#else
            return string.Empty;
#endif
        }

        public bool TryGetRatRoot(string ratId, out Transform root)
        {
            root = null;
            GameObject ratRoot;
            if (string.IsNullOrEmpty(ratId) || !ratRoots.TryGetValue(ratId, out ratRoot) || ratRoot == null) return false;
            root = ratRoot.transform;
            return true;
        }

        public bool TryGetRatBehavior(string ratId, out RatHabitatBehavior behavior)
        {
            behavior = null;
            if (string.IsNullOrEmpty(ratId) || !behaviors.TryGetValue(ratId, out behavior) || behavior == null || !behavior.isActiveAndEnabled)
            {
                behavior = null;
                return false;
            }
            return true;
        }

        public bool BeginNursingInteraction(string motherId, string pupId, string interactionId,
            float durationSeconds)
        {
            RatHabitatBehavior behavior;
            Transform pupRoot;
            if (!TryGetRatBehavior(motherId, out behavior) || !TryGetRatRoot(pupId, out pupRoot) ||
                pupRoot == null) return false;
            RefreshPinkiePlacementBeforeMotherApproach(pupId);
            return behavior.BeginNursingInteraction(pupRoot.position, interactionId, durationSeconds);
        }

        private void RefreshPinkiePlacementBeforeMotherApproach(string pupId)
        {
            RatData pup;
            RatVisualController pupController;
            GameObject pupRoot;
            if (string.IsNullOrEmpty(pupId) || !liveRats.TryGetValue(pupId, out pup) || pup == null ||
                pup.stage != RatStage.Pinkie || !ratRoots.TryGetValue(pupId, out pupRoot) ||
                pupRoot == null || !visualControllers.TryGetValue(pupId, out pupController) ||
                pupController == null) return;

            // Nursing routes the mother toward this position. Re-evaluate the
            // pup first so a save restore, a render refresh, or a hierarchy
            // mutation cannot make the mother approach an obsolete/floating
            // transform.
            PlacePinkieOnNest(pup, pupRoot, pupController);
        }

        private void RestoreSavedNursingInteractions(ColonySaveData save)
        {
            if (save == null || save.rats == null || save.clock == null) return;
            long gameTime = save.clock.gameTimeMs;
            for (int index = 0; index < save.rats.Count; index++)
            {
                RatData mother = save.rats[index];
                if (mother == null || string.IsNullOrEmpty(mother.id) || !mother.nursing ||
                    string.IsNullOrEmpty(mother.nursingPupId) ||
                    mother.nursingInteractionUntil <= gameTime) continue;

                RatHabitatBehavior behavior;
                Transform pupRoot;
                if (!TryGetRatBehavior(mother.id, out behavior) || behavior == null ||
                    behavior.NursingInteractionActive || !TryGetRatRoot(mother.nursingPupId, out pupRoot) ||
                    pupRoot == null) continue;

                RefreshPinkiePlacementBeforeMotherApproach(mother.nursingPupId);

                string interactionId = NursingSystem.NormalizeInteractionId(mother.nursingInteractionType);
                float remainingSeconds = NursingSystem.BehaviorSecondsFromGameMilliseconds(
                    mother.nursingInteractionUntil - gameTime);
                behavior.ResumeNursingInteraction(pupRoot.position, interactionId, remainingSeconds);
            }
        }

        private RatVisualFactory EnsureVisualFactory()
        {
            if (visualFactory == null) visualFactory = GetComponent<RatVisualFactory>();
            if (visualFactory == null) visualFactory = gameObject.AddComponent<RatVisualFactory>();
            return visualFactory;
        }

        private void CreateRatRoot(RatData rat, Vector3 position, out GameObject root, out RatVisualController controller)
        {
            root = new GameObject(rat.name + " Rat");
            root.transform.SetParent(transform, false);
            root.transform.position = position;

            var selectable = root.AddComponent<SelectableEntity>();
            selectable.Configure(SelectableKind.Rat, rat.id, ColonyFactory.DisplayName(rat));
            ConfigureRatCollider(root, rat.stage);

            controller = root.AddComponent<RatVisualController>();
            controller.Configure(EnsureVisualFactory());

            ratRoots[rat.id] = root;
            visualControllers[rat.id] = controller;
        }

        private void EnsureBehaviorForStage(GameObject root, RatData rat)
        {
            if (root == null || rat == null || string.IsNullOrEmpty(rat.id)) return;

            RatHabitatBehavior behavior;
            if (rat.stage == RatStage.Pinkie)
            {
                // This also handles a defensive backwards stage change: stop
                // the component before removing it so no movement or target
                // selection can run on a newborn.
                if (behaviors.TryGetValue(rat.id, out behavior))
                {
                    if (behavior != null) behavior.enabled = false;
                    if (behavior != null) Destroy(behavior);
                    behaviors.Remove(rat.id);
                }
                return;
            }

            if (!behaviors.TryGetValue(rat.id, out behavior) || behavior == null)
            {
                behavior = root.GetComponent<RatHabitatBehavior>();
                if (behavior == null) behavior = root.AddComponent<RatHabitatBehavior>();
                behaviors[rat.id] = behavior;
            }
            behavior.Configure(habitat, rat);
        }

        private static void ConfigureRatCollider(GameObject root, RatStage stage)
        {
            ConfigureRatCollider(root, stage, null);
        }

        private static void ConfigureRatCollider(GameObject root, RatStage stage, RatVisualController controller)
        {
            var selectable = root.GetComponent<SelectableEntity>();
            if (selectable == null) return;

            // The stable root identifies the rat but must not provide a broad
            // hit volume. Leave any legacy root collider disabled.
            foreach (var legacy in root.GetComponents<Collider>())
            {
                if (legacy != null) legacy.enabled = false;
            }

            Bounds bounds;
            if (controller != null && controller.TryGetSelectionBounds(out bounds))
            {
                ConfigureSelectionColliders(root, bounds);
                return;
            }

            // The first render can occur before the visual has built its
            // renderer bounds. This temporary shape is replaced immediately
            // after RatVisualController.Apply().
            float stageRatio = stage == RatStage.YoungRat
                ? GameConfig.YoungVisualScale / Mathf.Max(0.0001f, GameConfig.AdultVisualScale)
                : stage == RatStage.Pinkie ? 0.5f : 1f;
            var fallbackBounds = new Bounds(
                new Vector3(0f, stage == RatStage.Pinkie ? 0.42f : 0.62f * stageRatio, 0f),
                new Vector3(1.45f, 1.05f, 1.75f) * stageRatio);
            ConfigureSelectionColliders(root, fallbackBounds);
        }

        private static void ConfigureSelectionColliders(GameObject root, Bounds bounds)
        {
            Vector3 size = bounds.size;
            float width = Mathf.Max(0.12f, size.x);
            float height = Mathf.Max(0.12f, size.y);
            float length = Mathf.Max(0.12f, size.z);

            // One collider around the live renderer is more predictable than
            // several pose-independent fragments. The previous feet and tail
            // boxes could remain below an animated/grounded visual for a short
            // time and win a raycast in empty floor space. Keep only a small,
            // bounded forgiveness margin around the actual visible bounds.
            float widthPadding = Mathf.Min(0.08f, width * 0.06f);
            float heightPadding = Mathf.Min(0.05f, height * 0.04f);
            float lengthPadding = Mathf.Min(0.08f, length * 0.06f);
            ConfigureSelectionBox(root, "Rat Selection Collider",
                new Vector3(width + widthPadding * 2f, height + heightPadding * 2f, length + lengthPadding * 2f),
                bounds.center);

            // Disable older segmented selection surfaces left on a stable root
            // by an earlier runtime. Do not leave duplicate colliders active.
            DisableLegacySelectionBox(root, "Rat Selection Torso");
            DisableLegacySelectionBox(root, "Rat Selection Head");
            DisableLegacySelectionBox(root, "Rat Selection Feet");
            DisableLegacySelectionBox(root, "Rat Selection Tail");
        }

        private static void DisableLegacySelectionBox(GameObject root, string name)
        {
            if (root == null) return;
            Transform legacy = root.transform.Find(name);
            if (legacy == null) return;
            var collider = legacy.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        private static void ConfigureSelectionBox(GameObject root, string name, Vector3 size, Vector3 center)
        {
            Transform child = root.transform.Find(name);
            if (child == null)
            {
                var childObject = new GameObject(name);
                childObject.transform.SetParent(root.transform, false);
                child = childObject.transform;
            }
            child.localPosition = center;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            if (child.GetComponent<RatSelectionCollider>() == null) child.gameObject.AddComponent<RatSelectionCollider>();

            var collider = child.GetComponent<BoxCollider>();
            if (collider == null) collider = child.gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = size;
            collider.isTrigger = false;
            collider.enabled = true;
        }

        private static void RemoveLegacySelectionMarker(GameObject root)
        {
            if (root == null) return;
            Transform legacyRing = root.transform.Find("Selection Ring");
            if (legacyRing != null)
            {
                // Remove markers left behind by an older running assembly or
                // scene snapshot. Selection is now data-only and must never
                // add a persistent renderer beneath a rat.
                UnityEngine.Object.Destroy(legacyRing.gameObject);
            }
        }

        private void RemoveMissingRats(HashSet<string> liveIds)
        {
            var removed = new List<string>();
            foreach (var item in ratRoots)
            {
                if (!liveIds.Contains(item.Key)) removed.Add(item.Key);
            }
            foreach (var id in removed)
            {
                RatHabitatBehavior behavior;
                if (behaviors.TryGetValue(id, out behavior) && behavior != null) behavior.PlayDyingOnce();
                if (ratRoots[id] != null) Destroy(ratRoots[id]);
                ratRoots.Remove(id);
                visualControllers.Remove(id);
                behaviors.Remove(id);
                liveRats.Remove(id);
                pinkiePoseVisuals.Remove(id);
                pinkieNestAnchors.Remove(id);
                pinkieNestAnchorEnclosures.Remove(id);
                pinkieGroundedSelectionBoundsVersions.Remove(id);
                configuredSelectionBoundsVersions.Remove(id);
                presentationFailureWarnings.Remove(id);
            }
        }

        private void ClearRats()
        {
            foreach (var item in ratRoots)
            {
                if (item.Value != null) Destroy(item.Value);
            }
            ratRoots.Clear();
            visualControllers.Clear();
            behaviors.Clear();
            GrowthSystem.SetBehaviorParticipantCount(0);
            liveRats.Clear();
            pinkiePoseVisuals.Clear();
            pinkieNestAnchors.Clear();
            pinkieNestAnchorEnclosures.Clear();
            pinkieGroundedSelectionBoundsVersions.Clear();
            configuredSelectionBoundsVersions.Clear();
            presentationFailureWarnings.Clear();
        }

        private static Dictionary<string, int> BuildPinkieSlotMap(List<RatData> rats)
        {
            var pinkies = new List<RatData>();
            if (rats != null)
            {
                foreach (var rat in rats)
                {
                    if (rat != null && rat.stage == RatStage.Pinkie && !string.IsNullOrEmpty(rat.id))
                    {
                        pinkies.Add(rat);
                    }
                }
            }

            pinkies.Sort((left, right) => string.CompareOrdinal(left.id, right.id));
            var slots = new Dictionary<string, int>();
            var nextSlotByEnclosure = new Dictionary<RatEnclosure, int>();
            for (int index = 0; index < pinkies.Count; index++)
            {
                RatEnclosure enclosure = pinkies[index].enclosure;
                int slot;
                if (!nextSlotByEnclosure.TryGetValue(enclosure, out slot)) slot = 0;
                slots[pinkies[index].id] = slot;
                nextSlotByEnclosure[enclosure] = slot + 1;
            }
            return slots;
        }

        private Vector3 GetPosition(RatData rat, Vector3 nestPosition, Dictionary<string, int> pinkieSlotById,
            ref int pinkieIndex,
            ref int maleIndex, ref int femaleIndex, ref int nurseryIndex, ref int breedingIndex, ref int pairingIndex)
        {
            if (rat.stage == RatStage.Pinkie)
            {
                int pinkieSlot;
                if (!pinkieSlotById.TryGetValue(rat.id, out pinkieSlot)) pinkieSlot = pinkieIndex;
                pinkieIndex++;
                string litterSeed = string.IsNullOrEmpty(rat.litterId) ? rat.motherId : rat.litterId;
                return EnclosureSystem.GetPinkiePosition(rat.enclosure, rat.id, litterSeed,
                    pinkieSlot, nestPosition);
            }

            int slot;
            switch (rat.enclosure)
            {
                case RatEnclosure.MaleColony:
                    slot = maleIndex++;
                    break;
                case RatEnclosure.Nursery:
                    slot = nurseryIndex++;
                    break;
                case RatEnclosure.Breeding:
                    slot = breedingIndex++;
                    break;
                case RatEnclosure.Pairing:
                    slot = pairingIndex++;
                    break;
                default:
                    slot = femaleIndex++;
                    break;
            }
            return EnclosureSystem.GetSpawnPosition(rat.enclosure, slot);
        }

        private void ApplyDeterministicPinkiePose(RatData rat, GameObject root, RatVisualController controller)
        {
            if (rat == null || root == null || controller == null) return;

            GameObject visual;
            if (controller.TryGetCurrentVisual(out visual) && visual != null)
            {
                string litterKey = string.IsNullOrEmpty(rat.litterId)
                    ? (string.IsNullOrEmpty(rat.motherId) ? rat.id : rat.motherId)
                    : rat.litterId;
                string seedKey = (litterKey ?? "unassigned-litter") + "|pinkie-pose|" + (rat.id ?? string.Empty);
                float yaw = StablePinkieUnit(seedKey + "|yaw") * 360f;
                float pitch = Mathf.Lerp(-6f, 6f, StablePinkieUnit(seedKey + "|pitch"));
                float roll = Mathf.Lerp(-8f, 8f, StablePinkieUnit(seedKey + "|roll"));

                // RatVisualFactory reapplies the authored pinkie-facing
                // correction first. Add only a deterministic organic pose so
                // the imported orientation is preserved and sibling pinkies do
                // not all face the same way.
                GameObject appliedVisual;
                if (!pinkiePoseVisuals.TryGetValue(rat.id, out appliedVisual) || appliedVisual != visual)
                {
                    visual.transform.localRotation = visual.transform.localRotation *
                        Quaternion.Euler(pitch, yaw, roll);
                    pinkiePoseVisuals[rat.id] = visual;
                }
            }

            PlacePinkieOnNest(rat, root, controller);
        }

        private void PlacePinkieOnNest(RatData rat, GameObject root, RatVisualController controller)
        {
            if (rat == null || root == null || controller == null || habitat == null ||
                !habitat.TryGetNestSurfaceBounds(rat.enclosure, out Bounds surfaceBounds)) return;

            // Pinkie roots are owned by this presenter. Nursing only reads a
            // pup position to route the mother; it must never parent or move
            // the pup. Repair an unexpected hierarchy mutation without
            // preserving a mother-relative local transform.
            Vector3 lockedAnchor;
            if (TryGetPinkieNestAnchor(rat, out lockedAnchor))
            {
                // Once settled, do not call the overlap resolvers again.  A
                // moving/animating mother must never cause a pup to change
                // spots.  Keep the root independent and restore the exact
                // anchored position in case an external presentation pass
                // attempted to write to it.
                ResetPinkieRootTransform(root, lockedAnchor, rat);
                return;
            }

            ResetPinkieRootTransform(root, root.transform.position, rat);

            Vector3 rootPosition = root.transform.position;
            Vector3 positionBeforePlacement = rootPosition;
            Bounds renderedBounds;
            if (!controller.TryGetWorldBounds(out renderedBounds) || renderedBounds.size.sqrMagnitude <= 0.000001f)
                return;

            // Use the complete world-space bounds of every visible pinkie
            // renderer. Selection bounds intentionally use one representative
            // renderer and are therefore not safe for grounding: the imported
            // pinkie has a separately offset body/skin hierarchy. Computing
            // offsets from the evaluated render bounds accounts for the model
            // pivot, local offset, scale, rotation, animation, feet, and tail
            // without ever consulting the mother's transform.
            float minX = renderedBounds.min.x - rootPosition.x;
            float maxX = renderedBounds.max.x - rootPosition.x;
            float minY = renderedBounds.min.y - rootPosition.y;
            float minZ = renderedBounds.min.z - rootPosition.z;
            float maxZ = renderedBounds.max.z - rootPosition.z;

            // Clamp the complete rotated pinkie bounds, rather than just the
            // root point, so no limb or tail can leave the nest surface.
            rootPosition.x = ClampBoundedAxis(rootPosition.x,
                surfaceBounds.min.x - minX, surfaceBounds.max.x - maxX,
                (surfaceBounds.min.x + surfaceBounds.max.x - minX - maxX) * 0.5f);
            rootPosition.z = ClampBoundedAxis(rootPosition.z,
                surfaceBounds.min.z - minZ, surfaceBounds.max.z - maxZ,
                (surfaceBounds.min.z + surfaceBounds.max.z - minZ - maxZ) * 0.5f);

            // Place the actual rendered bottom exactly on the bedding surface.
            // This is deliberately derived from Renderer.bounds after the
            // current scale/rotation/animation has been evaluated; no mother
            // height and no guessed Y offset participates in the result.
            float beddingY = surfaceBounds.max.y;
            rootPosition.y = beddingY - minY;
            if ((root.transform.position - rootPosition).sqrMagnitude > 0.0000001f)
                root.transform.position = rootPosition;

            // Re-read the final bounds after the root write. This catches
            // import pivots and floating-point rounding without relying on a
            // second arbitrary lift.
            if (controller.TryGetWorldBounds(out Bounds finalBounds) &&
                Mathf.Abs(finalBounds.min.y - beddingY) > 0.0001f)
            {
                root.transform.position += Vector3.up * (beddingY - finalBounds.min.y);
                controller.TryGetWorldBounds(out finalBounds);
            }

            // A mother is allowed to care for the litter, but a pinkie must
            // never be spawned on her rendered body. Resolve an actual X/Z
            // renderer overlap against deterministic bedding positions. This
            // is deliberately based on evaluated Renderer.bounds rather than
            // a guessed Y offset or the mother's transform height.
            AvoidMotherOverlap(rat, root, controller, surfaceBounds, beddingY);
            // Resolve sibling overlap from the evaluated bounds as well. A
            // deterministic source position is not enough when imported
            // pinkie meshes have different pivots/scales; use the already
            // grounded sibling roots as the authoritative spacing surfaces.
            AvoidOtherPinkieOverlap(rat, root, controller, surfaceBounds, beddingY);

            // Spacing can change the root after the first bounds sample. The
            // imported pinkie is skinned and its evaluated bounds may be
            // refreshed by that transform write, so finish with one more
            // authoritative bottom-to-bedding solve. This is still derived
            // from Renderer.bounds; it is not a fixed visual offset.
            GroundPinkieRendererBottom(root, controller, beddingY);

            if (controller.TryGetWorldBounds(out finalBounds))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (finalBounds.min.y > beddingY + 0.001f)
                    WarnPinkiePlacement(rat, "rendered bottom is above bedding after final grounding");
                GameObject motherRoot;
                RatVisualController motherController;
                if (!string.IsNullOrEmpty(rat.motherId) &&
                    ratRoots.TryGetValue(rat.motherId, out motherRoot) && motherRoot != null &&
                    visualControllers.TryGetValue(rat.motherId, out motherController) &&
                    motherController != null && motherController.TryGetSelectionWorldBounds(out Bounds motherBounds) &&
                    BoundsOverlapXZ(finalBounds, motherBounds, 0.001f))
                {
                    WarnPinkiePlacement(rat, "rendered bounds still overlap mother after separation");
                }
#endif
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Debug.isDebugBuild && !pinkiePlacementDiagnostics.Contains(rat.id))
            {
                pinkiePlacementDiagnostics.Add(rat.id);
                Vector3 motherPosition = Vector3.zero;
                string motherName = "<none>";
                GameObject motherRoot;
                if (!string.IsNullOrEmpty(rat.motherId) &&
                    ratRoots.TryGetValue(rat.motherId, out motherRoot) && motherRoot != null)
                {
                    motherPosition = motherRoot.transform.position;
                    RatData mother;
                    if (liveRats.TryGetValue(rat.motherId, out mother) && mother != null)
                        motherName = mother.name;
                }

                Bounds diagnosticBounds;
                if (!controller.TryGetWorldBounds(out diagnosticBounds))
                    diagnosticBounds = renderedBounds;
                Debug.Log("[Rat Habitat] Pinkie placement: ratId=" + rat.id +
                    " world=" + root.transform.position.ToString("F3") +
                    " rendererY=[" + diagnosticBounds.min.y.ToString("F3") + "," +
                    diagnosticBounds.max.y.ToString("F3") + "]" +
                    " beddingY=" + beddingY.ToString("F3") +
                    " mother=" + motherName + " motherWorld=" + motherPosition.ToString("F3") +
                    " parent=" + (root.transform.parent == null ? "<none>" : root.transform.parent.name) +
                    " adjustment=" + (root.transform.position - positionBeforePlacement).ToString("F3"));
            }
            lastPinkieGroundedPositions[rat.id] = root.transform.position;
            lastPinkieGroundedFrames[rat.id] = Time.frameCount;
#endif

            // The final position is authoritative only after the complete
            // Renderer.bounds grounding and the one-time initial separation
            // pass.  Persisting this anchor for the lifetime of the pinkie
            // prevents nursing, mother movement, animation, or UI refreshes
            // from relocating the pup on subsequent frames.
            pinkieNestAnchors[rat.id] = root.transform.position;
            pinkieNestAnchorEnclosures[rat.id] = rat.enclosure;
            pinkieGroundedSelectionBoundsVersions[rat.id] = controller.SelectionBoundsVersion;
        }

        private bool NeedsPinkiePlacementRefresh(RatData rat, GameObject root, RatVisualController controller)
        {
            if (rat == null || root == null || controller == null || string.IsNullOrEmpty(rat.id)) return true;
            if (root.transform.parent != transform) return true;

            Vector3 anchor;
            if (!TryGetPinkieNestAnchor(rat, out anchor)) return true;

            int groundedVersion;
            if (!pinkieGroundedSelectionBoundsVersions.TryGetValue(rat.id, out groundedVersion) ||
                groundedVersion != controller.SelectionBoundsVersion)
                return true;

            // This is intentionally a cheap transform comparison. If a
            // nursing, save/load, animation-root, or enclosure pass writes a
            // new position, the next frame repairs it from Renderer.bounds.
            return (root.transform.position - anchor).sqrMagnitude > 0.000004f;
        }

        private bool TryGetPinkieNestAnchor(RatData rat, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            if (rat == null || string.IsNullOrEmpty(rat.id)) return false;

            RatEnclosure anchorEnclosure;
            if (!pinkieNestAnchors.TryGetValue(rat.id, out anchor) ||
                !pinkieNestAnchorEnclosures.TryGetValue(rat.id, out anchorEnclosure) ||
                anchorEnclosure != rat.enclosure)
            {
                pinkieNestAnchors.Remove(rat.id);
                pinkieNestAnchorEnclosures.Remove(rat.id);
                anchor = Vector3.zero;
                return false;
            }

            return true;
        }

        private void ResetPinkieRootTransform(GameObject root, Vector3 worldPosition, RatData rat)
        {
            if (root == null) return;
            GameObject motherRoot = null;
            if (rat != null && !string.IsNullOrEmpty(rat.motherId))
                ratRoots.TryGetValue(rat.motherId, out motherRoot);

            bool parentWasMother = motherRoot != null &&
                (root.transform == motherRoot.transform || root.transform.IsChildOf(motherRoot.transform));
            if (root.transform.parent != transform || parentWasMother)
                root.transform.SetParent(transform, false);

            // The stable root is a world-space placement anchor. Any scale or
            // rotation inherited from a mistaken parent would also move the
            // evaluated Renderer.bounds above the nest.
            root.transform.localScale = Vector3.one;
            root.transform.localRotation = Quaternion.identity;
            root.transform.position = worldPosition;

            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            for (int index = 0; index < animators.Length; index++)
            {
                Animator animator = animators[index];
                if (animator != null && animator.applyRootMotion)
                {
                    animator.applyRootMotion = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    WarnPinkiePlacement(rat, "an Animator had root motion enabled; it was disabled");
#endif
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (parentWasMother)
                WarnPinkiePlacement(rat, "pinkie root was parented to its mother and was reparented");
#endif
        }

        private void AvoidMotherOverlap(RatData rat, GameObject root, RatVisualController controller,
            Bounds surfaceBounds, float beddingY)
        {
            if (rat == null || string.IsNullOrEmpty(rat.motherId) || root == null || controller == null) return;
            GameObject motherRoot;
            RatVisualController motherController;
            if (!ratRoots.TryGetValue(rat.motherId, out motherRoot) || motherRoot == null ||
                !visualControllers.TryGetValue(rat.motherId, out motherController) || motherController == null) return;

            Bounds motherBounds;
            Bounds pupBounds;
            if (!motherController.TryGetSelectionWorldBounds(out motherBounds) ||
                !controller.TryGetWorldBounds(out pupBounds) || !BoundsOverlapXZ(pupBounds, motherBounds, 0.001f)) return;

            float clearance = 0.16f;
            float requiredX = motherBounds.extents.x + pupBounds.extents.x + clearance;
            float requiredZ = motherBounds.extents.z + pupBounds.extents.z + clearance;
            Vector3 original = root.transform.position;
            Vector3[] candidates =
            {
                original + Vector3.right * requiredX,
                original + Vector3.left * requiredX,
                original + Vector3.forward * requiredZ,
                original + Vector3.back * requiredZ,
                original + new Vector3(requiredX * 0.72f, 0f, requiredZ * 0.72f),
                original + new Vector3(-requiredX * 0.72f, 0f, requiredZ * 0.72f),
                original + new Vector3(requiredX * 0.72f, 0f, -requiredZ * 0.72f),
                original + new Vector3(-requiredX * 0.72f, 0f, -requiredZ * 0.72f),
            };

            float minX = pupBounds.min.x - original.x;
            float maxX = pupBounds.max.x - original.x;
            float minZ = pupBounds.min.z - original.z;
            float maxZ = pupBounds.max.z - original.z;
            for (int index = 0; index < candidates.Length; index++)
            {
                Vector3 candidate = candidates[index];
                candidate.x = ClampBoundedAxis(candidate.x,
                    surfaceBounds.min.x - minX, surfaceBounds.max.x - maxX,
                    surfaceBounds.center.x - (minX + maxX) * 0.5f);
                candidate.z = ClampBoundedAxis(candidate.z,
                    surfaceBounds.min.z - minZ, surfaceBounds.max.z - maxZ,
                    surfaceBounds.center.z - (minZ + maxZ) * 0.5f);
                candidate.y = beddingY - minYForRoot(pupBounds, original);
                root.transform.position = candidate;
                Bounds candidateBounds;
                if (controller.TryGetWorldBounds(out candidateBounds) &&
                    !BoundsOverlapXZ(candidateBounds, motherBounds, 0.001f)) return;
            }

            // If the nest is too small for a full clearance, restore the
            // deterministic bedding position and leave a diagnostic rather
            // than pushing the pup through a nest wall.
            root.transform.position = original;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnPinkiePlacement(rat, "nest surface is too small to clear the mother without leaving its bounds");
#endif
        }

        private void AvoidOtherPinkieOverlap(RatData rat, GameObject root,
            RatVisualController controller, Bounds surfaceBounds, float beddingY)
        {
            if (rat == null || root == null || controller == null) return;

            Bounds pupBounds;
            if (!controller.TryGetWorldBounds(out pupBounds)) return;

            bool hasSibling = false;
            foreach (var entry in liveRats)
            {
                RatData other = entry.Value;
                if (other == null || other.id == rat.id || other.stage != RatStage.Pinkie) continue;
                bool sameLitter = !string.IsNullOrEmpty(rat.litterId) &&
                    string.Equals(rat.litterId, other.litterId, System.StringComparison.Ordinal);
                bool sameMother = !string.IsNullOrEmpty(rat.motherId) &&
                    string.Equals(rat.motherId, other.motherId, System.StringComparison.Ordinal);
                if (!sameLitter && !sameMother) continue;
                GameObject siblingRoot;
                RatVisualController siblingController;
                if (!ratRoots.TryGetValue(other.id, out siblingRoot) || siblingRoot == null ||
                    !visualControllers.TryGetValue(other.id, out siblingController) || siblingController == null ||
                    !siblingController.TryGetWorldBounds(out Bounds siblingBounds)) continue;
                if (!BoundsOverlapXZ(pupBounds, siblingBounds, 0.045f)) continue;

                hasSibling = true;
                float requiredX = pupBounds.extents.x + siblingBounds.extents.x + 0.08f;
                float requiredZ = pupBounds.extents.z + siblingBounds.extents.z + 0.08f;
                Vector3 original = root.transform.position;
                Vector3[] candidates =
                {
                    original + Vector3.right * requiredX,
                    original + Vector3.left * requiredX,
                    original + Vector3.forward * requiredZ,
                    original + Vector3.back * requiredZ,
                    original + new Vector3(requiredX * 0.72f, 0f, requiredZ * 0.72f),
                    original + new Vector3(-requiredX * 0.72f, 0f, requiredZ * 0.72f),
                    original + new Vector3(requiredX * 0.72f, 0f, -requiredZ * 0.72f),
                    original + new Vector3(-requiredX * 0.72f, 0f, -requiredZ * 0.72f),
                };

                float minX = pupBounds.min.x - original.x;
                float maxX = pupBounds.max.x - original.x;
                float minZ = pupBounds.min.z - original.z;
                float maxZ = pupBounds.max.z - original.z;
                bool placed = false;
                for (int index = 0; index < candidates.Length; index++)
                {
                    Vector3 candidate = candidates[index];
                    candidate.x = ClampBoundedAxis(candidate.x,
                        surfaceBounds.min.x - minX, surfaceBounds.max.x - maxX,
                        surfaceBounds.center.x - (minX + maxX) * 0.5f);
                    candidate.z = ClampBoundedAxis(candidate.z,
                        surfaceBounds.min.z - minZ, surfaceBounds.max.z - maxZ,
                        surfaceBounds.center.z - (minZ + maxZ) * 0.5f);
                    candidate.y = beddingY - minYForRoot(pupBounds, original);
                    root.transform.position = candidate;

                    Bounds candidateBounds;
                    if (!controller.TryGetWorldBounds(out candidateBounds)) continue;
                    bool clear = true;
                    foreach (var siblingEntry in liveRats)
                    {
                        RatData sibling = siblingEntry.Value;
                        if (sibling == null || sibling.id == rat.id || sibling.stage != RatStage.Pinkie) continue;
                        bool siblingSameLitter = !string.IsNullOrEmpty(rat.litterId) &&
                            string.Equals(rat.litterId, sibling.litterId, System.StringComparison.Ordinal);
                        bool siblingSameMother = !string.IsNullOrEmpty(rat.motherId) &&
                            string.Equals(rat.motherId, sibling.motherId, System.StringComparison.Ordinal);
                        if (!siblingSameLitter && !siblingSameMother) continue;
                        RatVisualController siblingVisual;
                        if (visualControllers.TryGetValue(sibling.id, out siblingVisual) && siblingVisual != null &&
                            siblingVisual.TryGetWorldBounds(out Bounds siblingVisualBounds) &&
                            BoundsOverlapXZ(candidateBounds, siblingVisualBounds, 0.045f))
                        {
                            clear = false;
                            break;
                        }
                    }
                    if (clear)
                    {
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    root.transform.position = original;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    WarnPinkiePlacement(rat, "pinkie siblings overlap within the available nest bedding");
#endif
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (hasSibling && controller.TryGetWorldBounds(out Bounds finalBounds) &&
                finalBounds.min.y > beddingY + 0.001f)
                WarnPinkiePlacement(rat, "sibling separation changed the rendered bottom above bedding");
#endif
        }

        private static void GroundPinkieRendererBottom(GameObject root,
            RatVisualController controller, float beddingY)
        {
            if (root == null || controller == null) return;
            Bounds bounds;
            if (!controller.TryGetWorldBounds(out bounds)) return;

            float correction = beddingY - bounds.min.y;
            if (Mathf.Abs(correction) > 0.00001f)
            {
                root.transform.position += Vector3.up * correction;
                if (controller.TryGetWorldBounds(out bounds))
                {
                    correction = beddingY - bounds.min.y;
                    if (Mathf.Abs(correction) > 0.00001f)
                        root.transform.position += Vector3.up * correction;
                }
            }
        }

        private static float minYForRoot(Bounds bounds, Vector3 rootPosition)
        {
            return bounds.min.y - rootPosition.y;
        }

        private static bool BoundsOverlapXZ(Bounds first, Bounds second, float padding)
        {
            return first.max.x > second.min.x - padding && first.min.x < second.max.x + padding &&
                first.max.z > second.min.z - padding && first.min.z < second.max.z + padding;
        }

        private void AuditPinkieRootBeforePlacement(RatData rat, GameObject root)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Debug.isDebugBuild || rat == null || root == null) return;
            Vector3 previous;
            int previousFrame;
            if (lastPinkieGroundedPositions.TryGetValue(rat.id, out previous) &&
                lastPinkieGroundedFrames.TryGetValue(rat.id, out previousFrame) &&
                previousFrame < Time.frameCount &&
                (root.transform.position - previous).sqrMagnitude > 0.0025f)
            {
                WarnPinkiePlacement(rat, "root position changed after the previous final grounding pass");
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void WarnPinkiePlacement(RatData rat, string reason)
        {
            if (rat == null || !Debug.isDebugBuild) return;
            string key = (rat.id ?? rat.name ?? "pinkie") + "|" + reason;
            if (pinkiePlacementWarnings.Add(key))
                Debug.LogWarning("[Rat Habitat] Pinkie placement assertion: " + reason +
                    " ratId=" + rat.id + " name=" + rat.name + ".");
        }
#endif

        private static float ClampBoundedAxis(float value, float minimum, float maximum, float fallback)
        {
            return minimum <= maximum ? Mathf.Clamp(value, minimum, maximum) : fallback;
        }

        private static float StablePinkieUnit(string value)
        {
            unchecked
            {
                int hash = 23;
                if (!string.IsNullOrEmpty(value))
                {
                    for (int index = 0; index < value.Length; index++) hash = hash * 31 + value[index];
                }
                hash &= 0x7fffffff;
                return (hash % 100000) / 99999f;
            }
        }
    }
}
