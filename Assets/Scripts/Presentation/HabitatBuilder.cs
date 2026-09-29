using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RatHabitat
{
    public class HabitatBuilder : MonoBehaviour
    {
        // Temporary presentation mode for habitat props. The saved object
        // records and their gameplay roots remain intact; only the visual
        // representation is reduced to a small, floor-grounded marker.
        private const float ObjectMarkerDiameter = 0.78f;
        private const float ObjectMarkerSpacing = 0.14f;
        private const float ObjectMarkerFloorClearance = 0.008f;
        private const int ObjectMarkerSegments = 24;

        private readonly Dictionary<string, GameObject> objectRoots = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, HabitatObjectType> objectTypes = new Dictionary<string, HabitatObjectType>();
        private readonly Dictionary<string, Vector3> markerPositionCache = new Dictionary<string, Vector3>();
        private readonly List<PlacedMarker> placedMarkers = new List<PlacedMarker>();
        private readonly Dictionary<HabitatObjectType, Material> markerMaterials = new Dictionary<HabitatObjectType, Material>();
        private Transform geometryRoot;
        private Transform maleCageRoot;
        private Transform femaleCageRoot;
        private Transform breedingCageRoot;
        private Transform pairingCageRoot;
        private Bounds pairingNestSurfaceBounds;
        private bool pairingNestSurfaceBoundsValid;
        private bool built;
        private Mesh flatMarkerMesh;

        private sealed class PlacedMarker
        {
            public RatEnclosure enclosure;
            public Vector3 position;
            public string key;
        }

        public Vector3 NestPosition { get; private set; }

        /// <summary>
        /// Returns the world-space bounds of the actual upper nest surface.
        /// Pinkie placement uses this renderer-derived surface instead of a
        /// habitat-floor height or a generic Y offset.
        /// </summary>
        public bool TryGetNestSurfaceBounds(RatEnclosure enclosure, out Bounds surfaceBounds)
        {
            surfaceBounds = new Bounds();
            if (enclosure == RatEnclosure.Pairing && pairingNestSurfaceBoundsValid)
            {
                surfaceBounds = pairingNestSurfaceBounds;
                return surfaceBounds.size.sqrMagnitude > 0.0001f;
            }

            // The removed Nursery has no runtime surface. Mothers and pups
            // use the legacy saved nest in Female Cage, while Pairing uses
            // the imported nest model.
            Transform enclosureRoot = enclosure == RatEnclosure.Pairing
                ? pairingCageRoot
                : enclosure == RatEnclosure.FemaleColony ? femaleCageRoot : null;
            if (enclosureRoot == null) return false;

            string innerName = enclosure == RatEnclosure.Pairing
                ? "Nest_Bedding_Layer"
                : "Nest Inner";
            Transform surface = FindGeneratedChild(enclosureRoot, innerName);
            if (surface == null) surface = FindGeneratedChildIgnoreCase(enclosureRoot, innerName);
            if (surface == null && enclosure == RatEnclosure.Pairing)
                surface = FindGeneratedChildContainingIgnoreCase(enclosureRoot, "bedding");
            if (surface == null)
            {
                surface = FindGeneratedChild(enclosureRoot,
                    enclosure == RatEnclosure.Pairing ? "Pairing Nest Imported" : "Nest");
            }
            if (surface == null)
            {
                surface = FindGeneratedChildIgnoreCase(enclosureRoot,
                    enclosure == RatEnclosure.Pairing ? "Pairing Nest Imported" : "Nest");
            }
            if (surface == null) return false;

            // The imported Nest_Bedding_Layer is the one authoritative pinkie
            // surface. Prefer its direct renderer instead of collecting the
            // wooden walls/rims, which would put pinkies on the wrong height.
            Renderer directRenderer = surface.GetComponent<Renderer>();
            if (directRenderer != null && directRenderer.enabled)
            {
                surfaceBounds = directRenderer.bounds;
                bool valid = surfaceBounds.size.sqrMagnitude > 0.0001f;
                if (valid && enclosure == RatEnclosure.Pairing)
                {
                    pairingNestSurfaceBounds = surfaceBounds;
                    pairingNestSurfaceBoundsValid = true;
                }
                return valid;
            }

            // Keep the fallback for older imported nest hierarchies.
            Renderer[] renderers = surface.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled) continue;
                surfaceBounds = renderer.bounds;
                bool valid = surfaceBounds.size.sqrMagnitude > 0.0001f;
                if (valid && enclosure == RatEnclosure.Pairing)
                {
                    pairingNestSurfaceBounds = surfaceBounds;
                    pairingNestSurfaceBoundsValid = true;
                }
                return valid;
            }
            return false;
        }

        public void Build(ColonySaveData save)
        {
            if (built) return;
            built = true;
            placedMarkers.Clear();
            geometryRoot = new GameObject("Habitat Geometry").transform;
            geometryRoot.SetParent(transform, false);

            CreateEnclosureLayout();
            NestPosition = EnclosureSystem.PairingNestPosition;
            CreateDecorativeTunnel(femaleCageRoot, RatEnclosure.FemaleColony, "Mint Tunnel",
                EnclosureSystem.PointInEnclosure(RatEnclosure.FemaleColony, -1.45f, 4.05f, 0.78f),
                3.2f, 0.58f, new Color(0.23f, 0.68f, 0.64f));
            CreateDecorativeTunnel(femaleCageRoot, RatEnclosure.FemaleColony, "Peach Tunnel",
                EnclosureSystem.PointInEnclosure(RatEnclosure.FemaleColony, -1.10f, 1.00f, 0.62f),
                2.45f, 0.46f, new Color(0.97f, 0.60f, 0.42f));
            CreateDecorativeTunnel(breedingCageRoot, RatEnclosure.Breeding, "Breeding Tunnel",
                EnclosureSystem.PointInEnclosure(RatEnclosure.Breeding, 1.05f, -1.50f, 0.56f),
                2.0f, 0.40f, new Color(0.86f, 0.45f, 0.62f));
            CreatePairingNest();

            if (save == null || save.habitatObjects == null) return;

            // A stable ID order means the deterministic spacing pass produces
            // the same layout after save/load and does not depend on the order
            // in which a JSON array happened to be serialized.
            var objects = new List<HabitatObjectData>(save.habitatObjects);
            objects.Sort(delegate (HabitatObjectData left, HabitatObjectData right)
            {
                string leftId = left == null ? string.Empty : left.id ?? string.Empty;
                string rightId = right == null ? string.Empty : right.id ?? string.Empty;
                return string.CompareOrdinal(leftId, rightId);
            });

            foreach (var item in objects)
            {
                if (item == null) continue;
                CreateObject(item);
            }

            // Make the newly created trigger markers available to the manual
            // world raycast immediately, including on the first frame after a
            // save/load rebuild.
            Physics.SyncTransforms();
        }

        public void Rebuild(ColonySaveData save)
        {
            if (geometryRoot != null) Object.Destroy(geometryRoot.gameObject);
            objectRoots.Clear();
            objectTypes.Clear();
            geometryRoot = null;
            pairingNestSurfaceBounds = new Bounds();
            pairingNestSurfaceBoundsValid = false;
            built = false;
            Build(save);
        }

        public List<RatBehaviorTarget> GetBehaviorTargets()
        {
            var targets = new List<RatBehaviorTarget>();
            foreach (var item in objectRoots)
            {
                if (item.Value == null) continue;
                HabitatObjectType type;
                if (!objectTypes.TryGetValue(item.Key, out type)) continue;
                targets.Add(new RatBehaviorTarget
                {
                    id = item.Key,
                    label = item.Value.name,
                    kind = BehaviorKindFor(type),
                    position = GroundBehaviorPosition(item.Value.transform.position),
                });
            }

            // These tunnels are decorative geometry rather than saved
            // HabitatObjectData, so detect their actual generated transforms
            // instead of duplicating their positions in the rat controller.
            AddDecorativeTarget(targets, "Mint Tunnel", RatBehaviorTargetKind.Tunnel);
            AddDecorativeTarget(targets, "Peach Tunnel", RatBehaviorTargetKind.Tunnel);
            return targets;
        }

        public List<RatBehaviorTarget> GetBehaviorTargets(RatEnclosure enclosure)
        {
            var targets = new List<RatBehaviorTarget>();
            foreach (var target in GetBehaviorTargets())
            {
                if (target != null && EnclosureSystem.IsBehaviorPointAllowed(enclosure, target.position)) targets.Add(target);
            }

            // A zone always has local points even when the saved object layout
            // has no prop of a particular kind. These are behavior targets,
            // not extra gameplay entities or selectable objects.
            foreach (var target in EnclosureSystem.GetZoneTargets(enclosure))
            {
                if (target != null && EnclosureSystem.IsBehaviorPointAllowed(enclosure, target.position)) targets.Add(target);
            }
            return targets;
        }

        public void UpdateObjectCondition(string id, float condition)
        {
            if (string.IsNullOrEmpty(id) || !objectRoots.ContainsKey(id)) return;
            var renderer = objectRoots[id].GetComponent<Renderer>();
            if (renderer == null) return;
            Color color = renderer.material.color;
            renderer.material.color = Color.Lerp(new Color(0.32f, 0.18f, 0.12f), color, Mathf.Clamp01(condition / 100f));
        }

        private void CreateEnclosureLayout()
        {
            var enclosureRoot = new GameObject("Rat Enclosures").transform;
            enclosureRoot.SetParent(geometryRoot, false);

            // These are the only direct children of Rat Enclosures. Each
            // child is a complete cage with its own floor, walls, front
            // barrier, colliders, sign, and local behavior targets.
            maleCageRoot = CreateIndependentCage(enclosureRoot, RatEnclosure.MaleColony);
            femaleCageRoot = CreateIndependentCage(enclosureRoot, RatEnclosure.FemaleColony);
            breedingCageRoot = CreateIndependentCage(enclosureRoot, RatEnclosure.Breeding);
            pairingCageRoot = CreateIndependentCage(enclosureRoot, RatEnclosure.Pairing);
        }

        private void CreatePairingNest()
        {
            if (pairingCageRoot == null) return;
            EnclosureSystem.ClearPairingNestBounds();
            pairingNestSurfaceBounds = new Bounds();
            pairingNestSurfaceBoundsValid = false;
            const string resourcePath = "PairingNest/rat_nest_box";
            GameObject nestAsset = Resources.Load<GameObject>(resourcePath);
            if (nestAsset == null)
            {
                Debug.LogError("[Rat Habitat] Pairing nest model is missing at Resources/" + resourcePath);
                return;
            }

            // Instantiate exactly one imported hierarchy. The source FBX is
            // authored in world units; centering and floor alignment use its
            // evaluated renderer bounds rather than a guessed model offset.
            GameObject nest = Object.Instantiate(nestAsset, pairingCageRoot);
            nest.name = "Pairing Nest Imported";
            nest.transform.localScale = Vector3.one;
            nest.transform.localRotation = Quaternion.identity;
            nest.transform.localPosition = Vector3.zero;

            Renderer[] renderers = nest.GetComponentsInChildren<Renderer>(true);
            Bounds modelBounds;
            if (!TryGetRendererBounds(renderers, out modelBounds))
            {
                Object.Destroy(nest);
                Debug.LogError("[Rat Habitat] Pairing nest model has no renderable mesh bounds.");
                return;
            }

            Vector3 target = EnclosureSystem.PairingNestPosition;
            // The generated cage floor is a 0.24-unit slab centered at 0.115,
            // so its upper surface is 0.235. Keep the model's lowest wooden
            // vertex just above that surface to avoid z-fighting.
            const float floorTop = 0.235f;
            const float floorClearance = 0.002f;
            nest.transform.localPosition = new Vector3(
                target.x - modelBounds.center.x,
                floorTop - modelBounds.min.y + floorClearance,
                target.z - modelBounds.center.z);

            // Remove every imported/source collider before creating the one
            // gameplay footprint below. Disable first so a Destroy() queued
            // for the end of the frame cannot briefly create a second obstacle.
            Collider[] sourceColliders = nest.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < sourceColliders.Length; index++)
            {
                if (sourceColliders[index] == null) continue;
                sourceColliders[index].enabled = false;
                Object.Destroy(sourceColliders[index]);
            }

            Bounds placedBounds = new Bounds();
            if (!TryGetRendererBounds(renderers, out placedBounds)) return;

            // Cache the evaluated bedding renderer once, after the imported
            // hierarchy has been positioned. The pinkie surface must never be
            // inferred from the mother's height or from the outer wooden nest
            // bounds. Older FBX imports occasionally vary capitalization, so
            // resolve the authored bedding child case-insensitively as well.
            Transform bedding = FindGeneratedChild(nest.transform, "Nest_Bedding_Layer");
            if (bedding == null) bedding = FindGeneratedChildIgnoreCase(nest.transform, "Nest_Bedding_Layer");
            if (bedding == null) bedding = FindGeneratedChildContainingIgnoreCase(nest.transform, "bedding");
            if (bedding != null)
            {
                Renderer beddingRenderer = bedding.GetComponent<Renderer>();
                if (beddingRenderer != null && beddingRenderer.enabled)
                {
                    pairingNestSurfaceBounds = beddingRenderer.bounds;
                    pairingNestSurfaceBoundsValid = pairingNestSurfaceBounds.size.sqrMagnitude > 0.0001f;
                }
                if (!pairingNestSurfaceBoundsValid)
                {
                    Renderer[] beddingRenderers = bedding.GetComponentsInChildren<Renderer>(true);
                    if (TryGetRendererBounds(beddingRenderers, out pairingNestSurfaceBounds))
                        pairingNestSurfaceBoundsValid = true;
                }
            }
            if (!pairingNestSurfaceBoundsValid)
                Debug.LogWarning("[Rat Habitat] Pairing nest bedding renderer was not found; pinkie placement will use the explicit surface lookup fallback.");

            EnclosureSystem.RegisterPairingNestBounds(placedBounds);
            var collisionRoot = new GameObject("Pairing Nest Collision");
            collisionRoot.transform.SetParent(nest.transform, false);
            var nestCollider = collisionRoot.AddComponent<BoxCollider>();
            // Keep one real solid footprint for physics/debug inspection. The
            // Ignore Raycast layer keeps it separate from rat-selection
            // markers, while EnclosureSystem remains the authoritative
            // movement/pathfinding boundary.
            nestCollider.isTrigger = false;
            nestCollider.center = nest.transform.InverseTransformPoint(placedBounds.center);
            nestCollider.size = new Vector3(
                Mathf.Max(0.1f, placedBounds.size.x),
                Mathf.Max(0.1f, placedBounds.size.y),
                Mathf.Max(0.1f, placedBounds.size.z));
            // Keep the imported hierarchy as the logical nest surface and
            // collision source, but temporarily remove its presentation.
            // Pinkie placement still uses the cached bedding renderer bounds;
            // players see and select only the small floor marker below.
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null) renderers[index].enabled = false;
            }

            GameObject nestMarker = CreateFlatObjectMarker(
                pairingCageRoot,
                "Pairing Nest Marker",
                EnclosureSystem.PairingNestPosition,
                RatEnclosure.Pairing,
                HabitatObjectType.Nest,
                "pairing_nest",
                "Pairing Nest");
            if (nestMarker == null)
            {
                Debug.LogWarning("[Rat Habitat] Pairing nest marker could not be created; the logical nest remains active.");
            }
        }

        private static bool TryGetRendererBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = new Bounds();
            bool found = false;
            if (renderers == null) return false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled) continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found && bounds.size.sqrMagnitude > 0.0001f;
        }

        private Transform CreateIndependentCage(Transform enclosureRoot, RatEnclosure enclosure)
        {
            EnclosureSystem.Definition definition = EnclosureSystem.GetDefinition(enclosure);
            var cageRoot = new GameObject(definition.label).transform;
            cageRoot.SetParent(enclosureRoot, false);

            CreateRoundedBox(cageRoot, definition.label + " Floor",
                new Vector3(definition.Center.x, 0.115f, definition.Center.z),
                new Vector3(definition.Width - 0.08f, 0.24f, definition.Depth - 0.08f),
                definition.floorColor, 0.22f);
            var floorCollider = cageRoot.gameObject.AddComponent<BoxCollider>();
            floorCollider.center = new Vector3(definition.Center.x, 0f, definition.Center.z);
            floorCollider.size = new Vector3(definition.Width - 0.34f, 0.22f, definition.Depth - 0.34f);

            float wallHeight = 2.35f;
            float wallY = wallHeight * 0.5f;
            CreateVisualPrimitive(cageRoot, PrimitiveType.Cube, definition.label + " Back Wall",
                new Vector3(definition.Center.x, wallY, definition.maxZ + 0.14f),
                new Vector3(definition.Width + 0.28f, wallHeight, 0.28f), Vector3.zero,
                definition.barrierColor, true);
            CreateVisualPrimitive(cageRoot, PrimitiveType.Cube, definition.label + " Left Wall",
                new Vector3(definition.minX - 0.14f, wallY, definition.Center.z),
                new Vector3(0.28f, wallHeight, definition.Depth), Vector3.zero,
                definition.barrierColor, true);
            CreateVisualPrimitive(cageRoot, PrimitiveType.Cube, definition.label + " Right Wall",
                new Vector3(definition.maxX + 0.14f, wallY, definition.Center.z),
                new Vector3(0.28f, wallHeight, definition.Depth), Vector3.zero,
                definition.barrierColor, true);
            CreateSeeThroughFrontBarrier(cageRoot, definition.label + " Front Barrier",
                new Vector3(definition.Center.x, wallY, definition.minZ - 0.14f),
                new Vector3(definition.Width + 0.28f, wallHeight, 0.28f), Vector3.zero,
                definition.barrierColor, true);

            CreateEnclosureLabel(cageRoot, definition);
            CreateZoneTargetMarkers(cageRoot, enclosure, definition);
            return cageRoot;
        }

        private void CreateZoneTargetMarkers(Transform parent, RatEnclosure enclosure, EnclosureSystem.Definition definition)
        {
            foreach (var target in EnclosureSystem.GetZoneTargets(enclosure))
            {
                if (target == null || !EnclosureSystem.IsBehaviorPointAllowed(enclosure, target.position)) continue;
                CreateVisualPrimitive(parent, PrimitiveType.Cylinder,
                    definition.label + " Target " + target.id,
                    new Vector3(target.position.x, 0.18f, target.position.z),
                    new Vector3(0.11f, 0.025f, 0.11f), Vector3.zero,
                    Color.Lerp(definition.barrierColor, Color.white, 0.25f), false);
            }
        }

        private static void CreateEnclosureLabel(Transform parent, EnclosureSystem.Definition definition)
        {
            string signText = definition.enclosure == RatEnclosure.MaleColony ? "Males" :
                definition.enclosure == RatEnclosure.FemaleColony ? "Females" :
                definition.enclosure == RatEnclosure.Nursery ? "Nursery" : "Breeding";
            if (definition.enclosure == RatEnclosure.Pairing) signText = "Pairing Habitat";
            float signHeight = 1.82f;
            // Every player-facing sign uses the same plaque dimensions as the
            // Pairing Habitat so each full-size enclosure reads as an equal
            // page in the horizontal carousel.
            float signWidth = 4.15f;
            const float signHeightSize = 0.551f;
            const float signDepth = 0.1296f;
            // Place the plaque on the inside face of the back wall. The
            // camera is on the min-Z side, so the readable face points back
            // toward -Z without crossing the center gap or front barrier.
            float signBackZ = definition.maxZ - 0.20f;

            // The plaque is physically mounted on the inside of the back wall.
            // It has no collider of its own, so it cannot compete with rat or
            // habitat-object selection through the transparent front.
            // Keep the sign root at unit scale. The board itself is
            // non-uniformly sized, but the TextMesh must not be a child of
            // that scaled board or Unity will stretch the letters into the
            // long horizontal bands seen in the broken Game view.
            var signRoot = new GameObject(definition.label + " Sign").transform;
            signRoot.SetParent(parent, false);
            signRoot.localPosition = new Vector3(definition.Center.x, signHeight, signBackZ);
            signRoot.localRotation = Quaternion.identity;
            signRoot.localScale = Vector3.one;

            // A slightly larger, darker raised frame makes the plaque itself
            // visible against the back wall and gives the lettering a clear
            // physical boundary.
            CreateVisualPrimitive(signRoot, PrimitiveType.Cube,
                definition.label + " Sign Raised Frame",
                new Vector3(0f, 0f, 0.018f),
                new Vector3(signWidth + 0.12f, signHeightSize + 0.12f, signDepth * 0.82f),
                Vector3.zero, new Color(0.075f, 0.040f, 0.022f), false);

            CreateVisualPrimitive(signRoot, PrimitiveType.Cube,
                definition.label + " Signboard",
                Vector3.zero,
                new Vector3(signWidth, signHeightSize, signDepth),
                Vector3.zero, new Color(0.18f, 0.11f, 0.075f), false);

            var labelObject = new GameObject(definition.label + " Sign Text");
            labelObject.transform.SetParent(signRoot, false);
            labelObject.transform.localPosition = new Vector3(0f, 0f, -(signDepth * 0.5f + 0.006f));
            labelObject.transform.localScale = Vector3.one;
            Camera camera = Camera.main;
            var text = labelObject.AddComponent<TextMesh>();
            text.text = signText;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            // Keep the lettering comfortably inside the enlarged plaque,
            // including the longer Females/Nursery/Breeding labels.
            text.characterSize = 0.075f;
            text.fontSize = 64;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 0.91f, 0.63f);

            // TextMesh's readable face is its -Z side. Match the camera's
            // orientation directly so every plaque has the same level,
            // unskewed presentation; aiming separately from each sign caused
            // the visible per-sign tilt in the Game view.
            labelObject.transform.rotation = camera == null
                ? Quaternion.Euler(0f, 180f, 0f)
                : camera.transform.rotation;
        }

        private void CreateDecorativeTunnel(Transform parent, RatEnclosure enclosure, string name, Vector3 position, float length, float radius, Color color)
        {
            // Decorative tunnels are behavior targets, not saved gameplay
            // objects. Preserve their named transform and target position,
            // but use the same flat marker presentation so no large prop
            // obscures the habitat while inspecting rat behavior.
            CreateFlatMarker(
                parent == null ? geometryRoot : parent,
                name,
                position,
                enclosure,
                HabitatObjectType.Hide,
                null,
                name,
                Color.Lerp(color, Color.white, 0.08f));
        }

        private GameObject CreateExerciseWheel()
        {
            return CreateFlatObjectMarker(
                femaleCageRoot == null ? geometryRoot : femaleCageRoot,
                "Exercise Wheel",
                EnclosureSystem.PointInEnclosure(RatEnclosure.FemaleColony, 0.95f, 0.05f, 0.45f),
                RatEnclosure.FemaleColony,
                HabitatObjectType.ExerciseWheel,
                null,
                "Exercise Wheel");
        }

        private void CreateObject(HabitatObjectData data)
        {
            if (data == null) return;
            objectTypes[data.id] = data.type;
            if (data.type == HabitatObjectType.ExerciseWheel)
            {
                var wheel = CreateFlatObjectMarker(
                    femaleCageRoot == null ? geometryRoot : femaleCageRoot,
                    data.label,
                    EnclosureSystem.PointInEnclosure(RatEnclosure.FemaleColony, 0.95f, 0.05f, 0.45f),
                    RatEnclosure.FemaleColony,
                    data.type,
                    data.id,
                    data.label);
                objectRoots[data.id] = wheel;
                return;
            }

            Transform objectParent = ParentForObject(data.type);
            Vector3 position;
            RatEnclosure enclosure;
            switch (data.type)
            {
                case HabitatObjectType.Food:
                    position = EnclosureSystem.PointInEnclosure(RatEnclosure.MaleColony, -1.40f, -4.30f, 0.45f);
                    enclosure = RatEnclosure.MaleColony;
                    break;
                case HabitatObjectType.Water:
                    position = EnclosureSystem.PointInEnclosure(RatEnclosure.FemaleColony, 0.70f, 1.45f, 0.45f);
                    enclosure = RatEnclosure.FemaleColony;
                    break;
                case HabitatObjectType.Nest:
                    position = EnclosureSystem.GetNestPosition(RatEnclosure.FemaleColony);
                    position.y = 0.45f;
                    enclosure = RatEnclosure.FemaleColony;
                    break;
                default:
                    position = EnclosureSystem.PointInEnclosure(RatEnclosure.FemaleColony, 1.35f, 2.30f, 0.45f);
                    enclosure = RatEnclosure.FemaleColony;
                    break;
            }

            var root = CreateFlatObjectMarker(
                objectParent == null ? geometryRoot : objectParent,
                data.label,
                position,
                enclosure,
                data.type,
                data.id,
                data.label);
            objectRoots[data.id] = root;
        }

        private GameObject CreateFlatObjectMarker(Transform parent, string objectName,
            Vector3 preferredPosition, RatEnclosure enclosure, HabitatObjectType type,
            string entityId, string displayName)
        {
            return CreateFlatMarker(parent, objectName, preferredPosition, enclosure,
                type, entityId, displayName, null);
        }

        /// <summary>
        /// Creates the temporary presentation marker without changing the
        /// underlying HabitatObjectData. The trigger collider is deliberately
        /// thin and non-solid; it exists only for world selection.
        /// </summary>
        private GameObject CreateFlatMarker(Transform parent, string objectName,
            Vector3 preferredPosition, RatEnclosure enclosure, HabitatObjectType type,
            string entityId, string displayName, Color? overrideColor)
        {
            if (parent == null) parent = geometryRoot;
            if (parent == null) return null;

            string key = string.IsNullOrEmpty(entityId)
                ? "decorative:" + (objectName ?? string.Empty)
                : entityId;
            Vector3 markerPosition = FindMarkerPosition(key, preferredPosition, enclosure);

            var root = new GameObject(string.IsNullOrEmpty(objectName) ? "Habitat Object Marker" : objectName);
            root.transform.SetParent(parent, false);
            root.transform.position = markerPosition;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * ObjectMarkerDiameter;

            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = GetFlatMarkerMesh();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = GetMarkerMaterial(type, overrideColor);

            if (!string.IsNullOrEmpty(entityId))
            {
                var selectable = root.AddComponent<SelectableEntity>();
                selectable.Configure(SelectableKind.HabitatObject, entityId,
                    string.IsNullOrEmpty(displayName) ? objectName : displayName);

                var selectionCollider = root.AddComponent<BoxCollider>();
                selectionCollider.center = new Vector3(0f, 0.018f, 0f);
                selectionCollider.size = new Vector3(1.02f, 0.08f, 1.02f);
                selectionCollider.isTrigger = true;
            }
            return root;
        }

        private Vector3 FindMarkerPosition(string key, Vector3 preferredPosition, RatEnclosure enclosure)
        {
            float floorY = GetHabitatFloorY(enclosure);
            Vector3 cached;
            if (markerPositionCache.TryGetValue(key, out cached))
            {
                cached.y = floorY + ObjectMarkerFloorClearance;
                if (IsMarkerCandidateAvailable(cached, enclosure))
                {
                    RememberMarker(key, cached, enclosure);
                    return cached;
                }
            }

            Vector3 preferred = preferredPosition;
            preferred.y = floorY + ObjectMarkerFloorClearance;
            preferred = ClampMarkerPosition(preferred, enclosure);
            Vector3 candidate = preferred;
            uint seed = StableMarkerHash(key);

            // First try the requested point, then deterministic expanding
            // rings. This handles duplicate legacy positions without moving
            // objects between frames or depending on Unity's random state.
            for (int attempt = 0; attempt < 320; attempt++)
            {
                if (attempt > 0)
                {
                    int ring = 1 + (attempt - 1) / 16;
                    int slot = (attempt - 1) % 16;
                    float angle = (seed % 360u) * Mathf.Deg2Rad + slot * (Mathf.PI * 2f / 16f);
                    float radius = (ObjectMarkerDiameter + ObjectMarkerSpacing) *
                        (0.72f + ring * 0.72f);
                    candidate = preferred + new Vector3(Mathf.Cos(angle) * radius, 0f,
                        Mathf.Sin(angle) * radius);
                    candidate.y = floorY + ObjectMarkerFloorClearance;
                    candidate = ClampMarkerPosition(candidate, enclosure);
                }

                if (IsMarkerCandidateAvailable(candidate, enclosure))
                {
                    RememberMarker(key, candidate, enclosure);
                    return candidate;
                }
            }

            // Habitats are large enough for normal layouts, but retain a
            // bounded deterministic fallback rather than stacking markers if
            // a malformed save contains hundreds of identical objects.
            candidate = ClampMarkerPosition(preferred, enclosure);
            RememberMarker(key, candidate, enclosure);
            return candidate;
        }

        private void RememberMarker(string key, Vector3 position, RatEnclosure enclosure)
        {
            markerPositionCache[key] = position;
            for (int index = placedMarkers.Count - 1; index >= 0; index--)
            {
                if (placedMarkers[index].key == key) placedMarkers.RemoveAt(index);
            }
            placedMarkers.Add(new PlacedMarker
            {
                key = key,
                enclosure = enclosure,
                position = position,
            });
        }

        private bool IsMarkerCandidateAvailable(Vector3 candidate, RatEnclosure enclosure)
        {
            EnclosureSystem.Definition definition = EnclosureSystem.GetDefinition(enclosure);
            float inset = ObjectMarkerDiameter * 0.5f + ObjectMarkerSpacing * 0.5f;
            if (candidate.x < definition.minX + inset || candidate.x > definition.maxX - inset ||
                candidate.z < definition.minZ + inset || candidate.z > definition.maxZ - inset)
                return false;

            float minimumDistance = ObjectMarkerDiameter + ObjectMarkerSpacing;
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            for (int index = 0; index < placedMarkers.Count; index++)
            {
                PlacedMarker placed = placedMarkers[index];
                if (placed == null || placed.enclosure != enclosure) continue;
                Vector2 delta = new Vector2(candidate.x - placed.position.x,
                    candidate.z - placed.position.z);
                if (delta.sqrMagnitude < minimumDistanceSquared) return false;
            }
            return true;
        }

        private Vector3 ClampMarkerPosition(Vector3 position, RatEnclosure enclosure)
        {
            EnclosureSystem.Definition definition = EnclosureSystem.GetDefinition(enclosure);
            float inset = ObjectMarkerDiameter * 0.5f + ObjectMarkerSpacing * 0.5f;
            position.x = Mathf.Clamp(position.x, definition.minX + inset, definition.maxX - inset);
            position.z = Mathf.Clamp(position.z, definition.minZ + inset, definition.maxZ - inset);
            position.y = GetHabitatFloorY(enclosure) + ObjectMarkerFloorClearance;
            return position;
        }

        private float GetHabitatFloorY(RatEnclosure enclosure)
        {
            Transform cage = enclosure == RatEnclosure.MaleColony ? maleCageRoot :
                enclosure == RatEnclosure.Breeding ? breedingCageRoot :
                enclosure == RatEnclosure.Pairing ? pairingCageRoot : femaleCageRoot;
            if (cage != null)
            {
                Transform floor = FindGeneratedChild(cage,
                    EnclosureSystem.Label(enclosure) + " Floor");
                if (floor != null)
                {
                    Renderer floorRenderer = floor.GetComponent<Renderer>();
                    if (floorRenderer != null && floorRenderer.enabled)
                        return floorRenderer.bounds.max.y;
                }
            }

            // CreateIndependentCage's floor is centered at 0.115 with a
            // 0.24-unit height. Keep this only as a pre-render fallback.
            return 0.235f;
        }

        private Material GetMarkerMaterial(HabitatObjectType type, Color? overrideColor)
        {
            if (overrideColor.HasValue)
            {
                Material custom = MaterialFactory.CreateUnlit(overrideColor.Value);
                if (custom != null) custom.name = "Habitat Object Marker (decorative)";
                return custom;
            }

            Material material;
            if (markerMaterials.TryGetValue(type, out material) && material != null)
                return material;

            Color color;
            switch (type)
            {
                case HabitatObjectType.Food:
                    color = new Color(0.78f, 0.31f, 0.28f);
                    break;
                case HabitatObjectType.Water:
                    color = new Color(0.20f, 0.49f, 0.76f);
                    break;
                case HabitatObjectType.Nest:
                    color = new Color(0.69f, 0.48f, 0.22f);
                    break;
                case HabitatObjectType.ExerciseWheel:
                    color = new Color(0.82f, 0.39f, 0.27f);
                    break;
                default:
                    color = new Color(0.39f, 0.27f, 0.34f);
                    break;
            }

            material = MaterialFactory.CreateUnlit(color);
            if (material != null)
            {
                material.name = "Habitat Object Marker - " + type;
                markerMaterials[type] = material;
            }
            return material;
        }

        private Mesh GetFlatMarkerMesh()
        {
            if (flatMarkerMesh != null) return flatMarkerMesh;
            var vertices = new Vector3[ObjectMarkerSegments + 1];
            var uv = new Vector2[vertices.Length];
            vertices[0] = new Vector3(0f, 0.012f, 0f);
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int index = 0; index < ObjectMarkerSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / ObjectMarkerSegments;
                vertices[index + 1] = new Vector3(Mathf.Cos(angle) * 0.5f,
                    0.004f, Mathf.Sin(angle) * 0.5f);
                uv[index + 1] = new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f,
                    Mathf.Sin(angle) * 0.5f + 0.5f);
            }

            var triangles = new int[ObjectMarkerSegments * 3];
            for (int index = 0; index < ObjectMarkerSegments; index++)
            {
                int next = (index + 1) % ObjectMarkerSegments;
                int triangle = index * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = next + 1;
                triangles[triangle + 2] = index + 1;
            }

            flatMarkerMesh = new Mesh { name = "Habitat Object Flat Circle" };
            flatMarkerMesh.vertices = vertices;
            flatMarkerMesh.uv = uv;
            flatMarkerMesh.triangles = triangles;
            flatMarkerMesh.RecalculateNormals();
            flatMarkerMesh.RecalculateBounds();
            return flatMarkerMesh;
        }

        private static uint StableMarkerHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (value != null)
                {
                    for (int index = 0; index < value.Length; index++)
                    {
                        hash ^= value[index];
                        hash *= 16777619u;
                    }
                }
                return hash;
            }
        }

        private void AddDecorativeTarget(List<RatBehaviorTarget> targets, string objectName, RatBehaviorTargetKind kind)
        {
            if (targets == null || geometryRoot == null) return;
            Transform target = FindGeneratedChild(geometryRoot, objectName);
            if (target == null) return;
            targets.Add(new RatBehaviorTarget
            {
                id = "decorative_" + objectName.Replace(" ", "_").ToLowerInvariant(),
                label = objectName,
                kind = kind,
                position = GroundBehaviorPosition(target.position),
            });
        }

        private Transform ParentForObject(HabitatObjectType type)
        {
            switch (type)
            {
                case HabitatObjectType.Food:
                    return maleCageRoot;
                case HabitatObjectType.Nest:
                    return femaleCageRoot;
                case HabitatObjectType.Hide:
                    return femaleCageRoot;
                case HabitatObjectType.Water:
                case HabitatObjectType.ExerciseWheel:
                    return femaleCageRoot;
                default:
                    return geometryRoot;
            }
        }

        private static Transform FindGeneratedChild(Transform root, string objectName)
        {
            if (root == null) return null;
            if (root.name == objectName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindGeneratedChild(root.GetChild(i), objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindGeneratedChildIgnoreCase(Transform root, string objectName)
        {
            if (root == null || string.IsNullOrEmpty(objectName)) return null;
            if (string.Equals(root.name, objectName, System.StringComparison.OrdinalIgnoreCase)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindGeneratedChildIgnoreCase(root.GetChild(i), objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindGeneratedChildContainingIgnoreCase(Transform root, string fragment)
        {
            if (root == null || string.IsNullOrEmpty(fragment)) return null;
            if (root.name != null && root.name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindGeneratedChildContainingIgnoreCase(root.GetChild(i), fragment);
                if (found != null) return found;
            }
            return null;
        }

        private static RatBehaviorTargetKind BehaviorKindFor(HabitatObjectType type)
        {
            switch (type)
            {
                case HabitatObjectType.Food: return RatBehaviorTargetKind.Food;
                case HabitatObjectType.Water: return RatBehaviorTargetKind.Water;
                case HabitatObjectType.Nest: return RatBehaviorTargetKind.Nest;
                case HabitatObjectType.Hide: return RatBehaviorTargetKind.Hide;
                default: return RatBehaviorTargetKind.Toy;
            }
        }

        private static Vector3 GroundBehaviorPosition(Vector3 position)
        {
            return new Vector3(position.x, 0.45f, position.z);
        }

        private static GameObject CreateRoundedBox(Transform parent, string objectName, Vector3 position, Vector3 size, Color color, float radius)
        {
            return CreateRoundedBox(parent, objectName, position, size, color, radius, null);
        }

        private static GameObject CreateRoundedBox(Transform parent, string objectName, Vector3 position,
            Vector3 size, Color color, float radius, Texture texture)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;

            // A rounded box used to be assembled from overlapping cubes and
            // cylinders. That was acceptable for flat-color floors, but it
            // assigned the bedding texture to two coplanar center cubes on
            // the Pairing nest. The result was z-fighting and duplicated
            // texture layers. Keep each rounded box as one closed mesh with
            // one renderer and one material so the top UVs are continuous.
            var meshFilter = root.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildRoundedBoxMesh(objectName + " Mesh", size, radius);
            var meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            MaterialFactory.Apply(meshRenderer, color, false, texture);
            return root;
        }

        private static Mesh BuildRoundedBoxMesh(string meshName, Vector3 size, float radius)
        {
            const int cornerSegments = 6;
            float halfX = Mathf.Max(0.025f, size.x * 0.5f);
            float halfZ = Mathf.Max(0.025f, size.z * 0.5f);
            float halfY = Mathf.Max(0.005f, size.y * 0.5f);
            float cornerRadius = Mathf.Clamp(radius, 0.001f, Mathf.Min(halfX, halfZ));
            int outlineCount = cornerSegments * 4;

            var outline = new Vector3[outlineCount];
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 cornerCenter;
                switch (corner)
                {
                    case 0:
                        cornerCenter = new Vector2(halfX - cornerRadius, halfZ - cornerRadius);
                        break;
                    case 1:
                        cornerCenter = new Vector2(-halfX + cornerRadius, halfZ - cornerRadius);
                        break;
                    case 2:
                        cornerCenter = new Vector2(-halfX + cornerRadius, -halfZ + cornerRadius);
                        break;
                    default:
                        cornerCenter = new Vector2(halfX - cornerRadius, -halfZ + cornerRadius);
                        break;
                }

                float startAngle = corner * 90f;
                for (int segment = 0; segment < cornerSegments; segment++)
                {
                    float angle = (startAngle + segment * 90f / (cornerSegments - 1)) * Mathf.Deg2Rad;
                    int index = corner * cornerSegments + segment;
                    outline[index] = new Vector3(
                        cornerCenter.x + Mathf.Cos(angle) * cornerRadius,
                        0f,
                        cornerCenter.y + Mathf.Sin(angle) * cornerRadius);
                }
            }

            int topCenter = 0;
            int topOutline = 1;
            int bottomCenter = topOutline + outlineCount;
            int bottomOutline = bottomCenter + 1;
            var vertices = new Vector3[bottomOutline + outlineCount];
            vertices[topCenter] = new Vector3(0f, halfY, 0f);
            vertices[bottomCenter] = new Vector3(0f, -halfY, 0f);
            for (int index = 0; index < outlineCount; index++)
            {
                Vector3 point = outline[index];
                vertices[topOutline + index] = new Vector3(point.x, halfY, point.z);
                vertices[bottomOutline + index] = new Vector3(point.x, -halfY, point.z);
            }

            var triangles = new List<int>(outlineCount * 12);
            for (int index = 0; index < outlineCount; index++)
            {
                int next = (index + 1) % outlineCount;
                int topCurrent = topOutline + index;
                int topNext = topOutline + next;
                int bottomCurrent = bottomOutline + index;
                int bottomNext = bottomOutline + next;

                // The outline is counter-clockwise when viewed from above.
                // Reverse the fan order for an upward-facing top and use the
                // opposite order for the underside.
                triangles.Add(topCenter);
                triangles.Add(topNext);
                triangles.Add(topCurrent);
                triangles.Add(bottomCenter);
                triangles.Add(bottomCurrent);
                triangles.Add(bottomNext);

                // One outward-facing quad per perimeter segment.
                triangles.Add(bottomCurrent);
                triangles.Add(topCurrent);
                triangles.Add(topNext);
                triangles.Add(bottomCurrent);
                triangles.Add(topNext);
                triangles.Add(bottomNext);
            }

            var uv = new Vector2[vertices.Length];
            uv[topCenter] = new Vector2(0.5f, 0.5f);
            uv[bottomCenter] = new Vector2(0.5f, 0.5f);
            for (int index = 0; index < outlineCount; index++)
            {
                Vector3 point = outline[index];
                Vector2 mapped = new Vector2(
                    Mathf.Clamp01(point.x / size.x + 0.5f),
                    Mathf.Clamp01(point.z / size.z + 0.5f));
                uv[topOutline + index] = mapped;
                uv[bottomOutline + index] = mapped;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject CreateVisualPrimitive(Transform parent, PrimitiveType type, string objectName, Vector3 position, Vector3 scale, Vector3 rotation, Color color, bool keepCollider)
        {
            return CreateVisualPrimitive(parent, type, objectName, position, scale, rotation, color, keepCollider, null);
        }

        private static GameObject CreateVisualPrimitive(Transform parent, PrimitiveType type, string objectName,
            Vector3 position, Vector3 scale, Vector3 rotation, Color color, bool keepCollider, Texture texture)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = objectName;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.transform.localEulerAngles = rotation;
            MaterialFactory.Apply(item.GetComponent<Renderer>(), color, false, texture);
            if (!keepCollider)
            {
                var collider = item.GetComponent<Collider>();
                if (collider != null) Object.Destroy(collider);
            }
            return item;
        }

        private static GameObject CreateSeeThroughFrontBarrier(Transform parent, string objectName,
            Vector3 position, Vector3 scale, Vector3 rotation, Color color, bool keepCollider)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = objectName;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = position;
            panel.transform.localScale = scale;
            panel.transform.localEulerAngles = rotation;
            MaterialFactory.ApplyTransparent(panel.GetComponent<Renderer>(), color, 0.14f);

            // Keep one full panel collider for the physical boundary, but use
            // only a narrow visible frame so the front reads as acrylic rather
            // than an opaque wall. The frame pieces are decorative and have no
            // colliders, so they cannot compete with rat selection.
            float frame = Mathf.Clamp(scale.y * 0.035f, 0.055f, 0.09f);
            float frameDepth = Mathf.Max(0.08f, scale.z * 0.7f);
            CreateVisualPrimitive(parent, PrimitiveType.Cube, objectName + " Frame Left",
                position + Vector3.left * (scale.x * 0.5f - frame * 0.5f),
                new Vector3(frame, scale.y, frameDepth), rotation, color, false);
            CreateVisualPrimitive(parent, PrimitiveType.Cube, objectName + " Frame Right",
                position + Vector3.right * (scale.x * 0.5f - frame * 0.5f),
                new Vector3(frame, scale.y, frameDepth), rotation, color, false);
            CreateVisualPrimitive(parent, PrimitiveType.Cube, objectName + " Frame Top",
                position + Vector3.up * (scale.y * 0.5f - frame * 0.5f),
                new Vector3(scale.x, frame, frameDepth), rotation, color, false);
            CreateVisualPrimitive(parent, PrimitiveType.Cube, objectName + " Frame Bottom",
                position + Vector3.down * (scale.y * 0.5f - frame * 0.5f),
                new Vector3(scale.x, frame, frameDepth), rotation, color, false);

            if (!keepCollider)
            {
                var collider = panel.GetComponent<Collider>();
                if (collider != null) Object.Destroy(collider);
            }
            return panel;
        }
    }
}
