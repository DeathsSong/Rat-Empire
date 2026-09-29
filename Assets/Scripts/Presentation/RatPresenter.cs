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
        private readonly Dictionary<string, int> configuredSelectionBoundsVersions = new Dictionary<string, int>();
        private RatVisualFactory visualFactory;
        private HabitatBuilder habitat;
        private float groundingRefreshTimer;
        private const float GroundingRefreshIntervalSeconds = 0.075f;
        private float presentationCullingTimer;
        private const float PresentationCullingIntervalSeconds = 0.20f;
        // A malformed saved rat or an optional visual asset must not abort the
        // entire presentation pass. Keep the warning once per stable ID so a
        // late-game save cannot flood the WebGL console every frame.
        private readonly HashSet<string> presentationFailureWarnings = new HashSet<string>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly HashSet<string> pinkiePlacementDiagnostics = new HashSet<string>();
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
                            // Pinkies are nest-bound. Reapply their deterministic
                            // litter arrangement on every render so an old saved
                            // grid position cannot survive a refresh or reload.
                            root.transform.position = position;
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

                    controller.Configure(visualFactory);
                    // A controller keeps the visual child between renders. When a
                    // saved stage changes, it performs the configured smooth
                    // pinkie->young or young->adult transition.
                    controller.Apply(rat, true);
                    ConfigureRatCollider(root, rat.stage, controller);
                    // Pinkies are nest-bound and never receive a behavior
                    // component. Attach/configure movement only after the visual
                    // stage has been confirmed to be Young or Adult.
                    if (rat.stage == RatStage.Pinkie)
                    {
                        ApplyDeterministicPinkiePose(rat, root, controller);
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
            RestoreSavedNursingInteractions(save);
        }

        private void LateUpdate()
        {
            // Animator deformation and code-driven movement both occur before
            // this point in the frame. Keep the stable selection surfaces
            // aligned with the currently visible mesh instead of leaving a
            // stale pose-sized hitbox below or beside an animated rat.
            groundingRefreshTimer -= Time.unscaledDeltaTime;
            bool refreshGrounding = groundingRefreshTimer <= 0f;
            if (refreshGrounding) groundingRefreshTimer = GroundingRefreshIntervalSeconds;
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

                RatData rat;
                GameObject currentVisual;
                if (liveRats.TryGetValue(item.Key, out rat) &&
                    controller.TryGetCurrentVisual(out currentVisual))
                {
                    // Age changes every simulation tick, not only when a
                    // stage label changes. Apply the shared age curve before
                    // bounds/grounding work so the live model grows smoothly
                    // and its selection surface follows the same scale.
                    controller.ApplyAgeScale(rat);
                }

                if (liveRats.TryGetValue(item.Key, out rat) &&
                    controller.TryGetCurrentVisual(out currentVisual))
                {
                    if (rat.stage == RatStage.Pinkie)
                    {
                        // Pinkies are rendered as independent nest occupants.
                        // Re-ground the complete rendered bounds after an
                        // animation/growth update every rendered frame so a
                        // visual child offset, pose, or root transform write
                        // can never lift the pup onto the mother.
                        PlacePinkieOnNest(rat, root, controller);
                    }
                    else if (refreshGrounding)
                    {
                        // Keep animation-driven feet/tails from dipping below
                        // the actual Pairing cage floor after the render pass.
                        // This changes only the visual child and never the
                        // stable gameplay root.
                        visualFactory.KeepPairingVisualGrounded(currentVisual, rat, controller);
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
                    Bounds bounds;
                    if (controller.TryGetSelectionBounds(out bounds))
                    {
                        ConfigureRatCollider(root, controller.CurrentStage, controller);
                        configuredSelectionBoundsVersions[item.Key] = controller.SelectionBoundsVersion;
                    }
                }
            }
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
            liveRats.Clear();
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
                visual.transform.localRotation = visual.transform.localRotation *
                    Quaternion.Euler(pitch, yaw, roll);
            }

            PlacePinkieOnNest(rat, root, controller);
        }

        private void PlacePinkieOnNest(RatData rat, GameObject root, RatVisualController controller)
        {
            if (rat == null || root == null || controller == null || habitat == null ||
                !habitat.TryGetNestSurfaceBounds(rat.enclosure, out Bounds surfaceBounds)) return;

            // Pinkie roots are owned by this presenter. Nursing only reads a
            // pup position to route the mother; it must never parent or move
            // the pup. Repair an unexpected hierarchy mutation while keeping
            // the current world position intact.
            if (root.transform.parent != transform)
                root.transform.SetParent(transform, true);

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
                renderedBounds = finalBounds;
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
#endif
        }

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
