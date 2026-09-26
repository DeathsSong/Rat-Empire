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
        private RatVisualFactory visualFactory;
        private HabitatBuilder habitat;

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
            EnsureVisualFactory();
            if (save == null)
            {
                ClearRats();
                return;
            }

            // Placement is relationship-driven (pregnancy, dependent
            // Pinkies, and stage/sex). Reconcile it before reusing any stable
            // visual roots so a render never leaves a rat in its old zone.
            EnclosureSystem.RecalculateAssignments(save);
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

                controller.Configure(EnsureVisualFactory());
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
            }

            RemoveMissingRats(liveIds);
        }

        private void LateUpdate()
        {
            // Animator deformation and code-driven movement both occur before
            // this point in the frame. Keep the stable selection surfaces
            // aligned with the currently visible mesh instead of leaving a
            // stale pose-sized hitbox below or beside an animated rat.
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

                Bounds bounds;
                if (controller.TryGetSelectionBounds(out bounds))
                {
                    ConfigureRatCollider(root, controller.CurrentStage, controller);
                }

                if (liveRats.TryGetValue(item.Key, out rat) &&
                    controller.TryGetCurrentVisual(out currentVisual))
                {
                    // Keep animation-driven feet/tails from dipping below
                    // the actual Pairing cage floor after the render pass.
                    EnsureVisualFactory().KeepPairingVisualGrounded(currentVisual, rat);
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

        public bool BeginNursingInteraction(string motherId, string pupId, float durationSeconds)
        {
            RatHabitatBehavior behavior;
            Transform pupRoot;
            if (!TryGetRatBehavior(motherId, out behavior) || !TryGetRatRoot(pupId, out pupRoot) ||
                pupRoot == null) return false;
            return behavior.BeginNursingInteraction(pupRoot.position, durationSeconds);
        }

        /// <summary>
        /// Places a mother beside her enclosure's nest for the birth frame.
        /// Pinkies remain on the nest; adults are routed through the same
        /// logical exclusion used by normal movement so they cannot overlap
        /// the nest geometry.
        /// </summary>
        public bool PlaceRatBesideNest(string ratId, RatEnclosure enclosure)
        {
            GameObject root;
            if (string.IsNullOrEmpty(ratId) || !ratRoots.TryGetValue(ratId, out root) || root == null ||
                !EnclosureSystem.HasNest(enclosure)) return false;

            root.transform.position = EnclosureSystem.GetNestSidePosition(enclosure);
            return true;
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
            float floor = bounds.min.y;

            ConfigureSelectionBox(root, "Rat Selection Torso",
                new Vector3(width * 0.72f, height * 0.58f, length * 0.56f),
                new Vector3(bounds.center.x, floor + height * 0.54f, bounds.center.z));
            ConfigureSelectionBox(root, "Rat Selection Head",
                new Vector3(width * 0.56f, height * 0.54f, length * 0.26f),
                new Vector3(bounds.center.x, floor + height * 0.66f, bounds.center.z - length * 0.29f));
            ConfigureSelectionBox(root, "Rat Selection Feet",
                new Vector3(width * 0.58f, height * 0.24f, length * 0.42f),
                new Vector3(bounds.center.x, floor + height * 0.16f, bounds.center.z - length * 0.04f));
            ConfigureSelectionBox(root, "Rat Selection Tail",
                new Vector3(width * 0.28f, height * 0.22f, length * 0.30f),
                new Vector3(bounds.center.x, floor + height * 0.29f, bounds.center.z + length * 0.34f));
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

            Bounds localBounds;
            if (!controller.TryGetSelectionBounds(out localBounds)) return;

            Vector3 rootPosition = root.transform.position;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 localCorner = new Vector3(
                            x == 0 ? localBounds.min.x : localBounds.max.x,
                            y == 0 ? localBounds.min.y : localBounds.max.y,
                            z == 0 ? localBounds.min.z : localBounds.max.z);
                        Vector3 worldOffset = root.transform.TransformPoint(localCorner) - rootPosition;
                        minX = Mathf.Min(minX, worldOffset.x);
                        maxX = Mathf.Max(maxX, worldOffset.x);
                        minY = Mathf.Min(minY, worldOffset.y);
                        minZ = Mathf.Min(minZ, worldOffset.z);
                        maxZ = Mathf.Max(maxZ, worldOffset.z);
                    }
                }
            }

            // Clamp the complete rotated pinkie bounds, rather than just the
            // root point, so no limb or tail can leave the nest surface.
            rootPosition.x = ClampBoundedAxis(rootPosition.x,
                surfaceBounds.min.x - minX, surfaceBounds.max.x - maxX,
                (surfaceBounds.min.x + surfaceBounds.max.x - minX - maxX) * 0.5f);
            rootPosition.z = ClampBoundedAxis(rootPosition.z,
                surfaceBounds.min.z - minZ, surfaceBounds.max.z - maxZ,
                (surfaceBounds.min.z + surfaceBounds.max.z - minZ - maxZ) * 0.5f);

            // The renderer-derived top is the actual nest surface. The tiny
            // epsilon prevents z-fighting without making the pinkie float.
            rootPosition.y = surfaceBounds.max.y - minY + 0.012f;
            root.transform.position = rootPosition;
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
