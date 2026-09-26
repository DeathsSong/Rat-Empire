using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Renders cached thumbnails with the same RatVisualFactory used by the
    /// live colony. Cached preview visuals are presentation-only: they have no
    /// SelectableEntity, no gameplay root, and no colliders. The full selected
    /// rat profile is deliberately not rendered here; its portrait opening
    /// shows the existing habitat camera behind the UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RatPortraitPreview : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private const int PortraitResolution = 256;
        private const float PortraitElevationDegrees = 35f;
        private const float PortraitSideAngleDegrees = 40f;
        private const float PortraitSpinDegreesPerSecond = 10f;
        // CalculateFramingSize returns the projected half-extent of the
        // filtered body bounds. Keep the established fit margin and
        // presentation zoom unchanged while adding only preview rotation.
        private const float PortraitPadding = 3.80f;
        private const float PortraitFramingScale = (0.34f / 2.4f) / 0.8f;

        private sealed class PortraitEntry
        {
            public GameObject visual;
            public RenderTexture texture;
            public RatData rat;
            public float normalizedBaseScale;
            public float appliedGrowthScale;
        }

        private readonly Dictionary<string, PortraitEntry> portraits = new Dictionary<string, PortraitEntry>();
        private RatVisualFactory factory;
        private GameObject rig;
        private Camera previewCamera;
        private Transform visualRoot;
        public void Configure(RatVisualFactory sourceFactory)
        {
            if (factory != null && factory != sourceFactory)
            {
                ClearPortraitCache();
            }
            factory = sourceFactory;
            EnsureRig();
        }

        public Texture GetPortrait(RatData rat)
        {
            if (rat == null || factory == null) return null;

            EnsureRig();

            string key = PortraitKey(rat);
            // A phenotype or stage change creates a new cache key. Remove the
            // old entry for the same rat first so refreshes cannot leave stale
            // visual roots in the shared portrait rig.
            RemoveStaleEntriesForRat(rat.id, key);
            PortraitEntry cached;
            if (portraits.TryGetValue(key, out cached))
            {
                DeactivateAllPreviewVisuals();
                if (cached != null && cached.texture != null && cached.visual != null) return cached.texture;
                DestroyPortraitEntry(cached);
                portraits.Remove(key);
            }

            DeactivateAllPreviewVisuals();
            GameObject visual = factory.CreateStageVisual(visualRoot, rat);
            if (visual == null) return null;
            SetLayerRecursively(visual, PreviewLayer);
            RemoveGameplayComponents(visual);
            // The factory normalizes the imported root uniformly. Preserve
            // that proportion when applying the presentation-only stage scale
            // instead of allowing any preview compensation to skew an axis.
            float normalizedScale = UniformBaseScale(visual.transform.localScale);
            float growthScale = GrowthSystem.VisualScaleForAge(rat);
            visual.transform.localScale = Vector3.one * (normalizedScale * growthScale);

            Bounds bounds;
            if (!TryGetBounds(visual, out bounds, true))
            {
                Object.DestroyImmediate(visual);
                return null;
            }

            RenderTexture target = new RenderTexture(PortraitResolution, PortraitResolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.name = "Rat Portrait " + rat.id;
            target.hideFlags = HideFlags.HideAndDontSave;
            target.Create();

            var entry = new PortraitEntry
            {
                visual = visual,
                texture = target,
                rat = rat,
                normalizedBaseScale = normalizedScale,
                appliedGrowthScale = growthScale,
            };
            portraits[key] = entry;
            RenderEntry(entry, bounds);
            return target;
        }

        private void Update()
        {
            if (!Application.isPlaying || previewCamera == null || portraits.Count == 0) return;

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f) return;

            foreach (var item in portraits)
            {
                PortraitEntry entry = item.Value;
                if (entry == null || entry.visual == null || entry.texture == null) continue;

                // Keep cached portraits live as a rat grows within the same
                // biological stage. RawImages share this RenderTexture, so
                // updating the cached visual updates Store, My Rats, family
                // tree, breeding, and other previews without rebuilding UI.
                if (entry.rat != null)
                {
                    float growthScale = GrowthSystem.VisualScaleForAge(entry.rat);
                    if (Mathf.Abs(growthScale - entry.appliedGrowthScale) > 0.0001f)
                    {
                        entry.appliedGrowthScale = growthScale;
                        entry.visual.transform.localScale = Vector3.one *
                            (entry.normalizedBaseScale * growthScale);
                    }
                }

                // Each cached preview visual is presentation-only and lives
                // under the hidden rig, so this rotation cannot affect the
                // live colony root, selection collider, or gameplay state.
                entry.visual.transform.Rotate(Vector3.up, PortraitSpinDegreesPerSecond * deltaTime, Space.Self);
                entry.visual.SetActive(true);
                Bounds bounds;
                if (TryGetBounds(entry.visual, out bounds, false)) RenderEntry(entry, bounds);
                else entry.visual.SetActive(false);
            }
        }

        private void RenderEntry(PortraitEntry entry, Bounds bounds)
        {
            if (entry == null || entry.visual == null || entry.texture == null || previewCamera == null) return;

            // Only one preview visual is enabled while the shared hidden
            // camera renders. Cached entries remain reusable and are released
            // together in OnDestroy.
            DeactivateAllPreviewVisuals();
            entry.visual.SetActive(true);
            int activeRootCount = CountActivePreviewVisualRoots();
            Debug.Assert(activeRootCount == 1,
                "[Rat Habitat] Portrait preview expected exactly one active visual root before rendering, found " + activeRootCount + ".");
            Debug.Assert(previewCamera.cullingMask == (1 << PreviewLayer),
                "[Rat Habitat] Portrait preview camera culling mask is not isolated to the portrait layer.");
            Vector3 lookPoint = bounds.center;
            float horizontalDistance = Mathf.Max(2.5f, bounds.size.y * 3.2f);
            float elevation = PortraitElevationDegrees * Mathf.Deg2Rad;
            float sideAngle = PortraitSideAngleDegrees * Mathf.Deg2Rad;
            // The imported root faces toward local -Z after the factory's
            // existing 180-degree visual-root rotation. An x-negative camera
            // offset puts that head in the lower-right of the cached portrait
            // and the tail in the upper-left, while preserving the visual.
            Vector3 cameraOffset = new Vector3(
                -Mathf.Sin(sideAngle) * horizontalDistance,
                Mathf.Tan(elevation) * horizontalDistance,
                -Mathf.Cos(sideAngle) * horizontalDistance);
            previewCamera.transform.position = lookPoint + cameraOffset;
            previewCamera.transform.LookAt(lookPoint);
            previewCamera.orthographic = true;
            previewCamera.targetTexture = entry.texture;
            previewCamera.aspect = (float)entry.texture.width / Mathf.Max(1, entry.texture.height);
            float framingSize = CalculateFramingSize(bounds, previewCamera, previewCamera.aspect, PortraitPadding);
            // This is presentation-only zoom. The visual remains uniformly
            // scaled; only the preview camera framing changes.
            previewCamera.orthographicSize = framingSize * PortraitFramingScale;
            previewCamera.ResetProjectionMatrix();
            ClearRenderTexture(entry.texture);
            previewCamera.Render();
            previewCamera.targetTexture = null;
            entry.visual.SetActive(false);
        }

        private void EnsureRig()
        {
            if (rig != null && previewCamera != null && visualRoot != null) return;

            rig = new GameObject("Rat Portrait Preview Rig");
            rig.transform.SetParent(transform, false);
            rig.hideFlags = HideFlags.HideAndDontSave;
            rig.SetActive(true);
            SetLayerRecursively(rig, PreviewLayer);

            var cameraObject = new GameObject("Rat Portrait Preview Camera");
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.layer = PreviewLayer;
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.clear;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 100f;
            previewCamera.useOcclusionCulling = false;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;

            var visualObject = new GameObject("Rat Portrait Visual");
            visualObject.transform.SetParent(rig.transform, false);
            visualObject.layer = PreviewLayer;
            visualRoot = visualObject.transform;
        }

        private void DeactivateAllPreviewVisuals()
        {
            if (visualRoot == null) return;
            for (int i = 0; i < visualRoot.childCount; i++)
            {
                Transform child = visualRoot.GetChild(i);
                if (child != null) child.gameObject.SetActive(false);
            }
        }

        private int CountActivePreviewVisualRoots()
        {
            if (visualRoot == null) return 0;
            int count = 0;
            for (int i = 0; i < visualRoot.childCount; i++)
            {
                Transform child = visualRoot.GetChild(i);
                if (child != null && child.gameObject.activeInHierarchy) count++;
            }
            return count;
        }

        private void RemoveStaleEntriesForRat(string ratId, string keepKey)
        {
            if (string.IsNullOrEmpty(ratId)) return;
            var staleKeys = new List<string>();
            foreach (var item in portraits)
            {
                if (item.Key == keepKey || !item.Key.StartsWith(ratId + "|", System.StringComparison.Ordinal)) continue;
                staleKeys.Add(item.Key);
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                PortraitEntry stale;
                if (!portraits.TryGetValue(staleKeys[i], out stale)) continue;
                DestroyPortraitEntry(stale);
                portraits.Remove(staleKeys[i]);
            }
        }

        private void ClearPortraitCache()
        {
            foreach (var item in portraits) DestroyPortraitEntry(item.Value);
            portraits.Clear();
            DeactivateAllPreviewVisuals();
        }

        private static void ClearRenderTexture(RenderTexture target)
        {
            if (target == null) return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previous;
        }

        private void OnDestroy()
        {
            ClearPortraitCache();
            if (rig != null) Object.DestroyImmediate(rig);
        }

        private static void DestroyPortraitEntry(PortraitEntry entry)
        {
            if (entry == null) return;
            if (entry.texture != null)
            {
                entry.texture.Release();
                Object.DestroyImmediate(entry.texture);
                entry.texture = null;
            }
            if (entry.visual != null)
            {
                entry.visual.SetActive(false);
                Object.DestroyImmediate(entry.visual);
                entry.visual = null;
            }
        }

        private static string PortraitKey(RatData rat)
        {
            var phenotype = rat.phenotype;
            string phenotypeKey = phenotype == null
                ? string.Empty
                : phenotype.coatColorId + ":" + phenotype.coatColorHex + ":" + phenotype.accentHex + ":" + phenotype.spotted + ":" + phenotype.furRevealed;
            return rat.id + "|" + rat.stage + "|" + phenotypeKey;
        }

        private static float UniformBaseScale(Vector3 source)
        {
            return Mathf.Max(0.0001f,
                (Mathf.Abs(source.x) + Mathf.Abs(source.y) + Mathf.Abs(source.z)) / 3f);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static void RemoveGameplayComponents(GameObject root)
        {
            if (root == null) return;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null) Object.DestroyImmediate(collider);
            }
            foreach (var selectable in root.GetComponentsInChildren<SelectableEntity>(true))
            {
                if (selectable != null) Object.DestroyImmediate(selectable);
            }
        }

        private static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            return TryGetBounds(root, out bounds, true);
        }

        private static bool TryGetBounds(GameObject root, out Bounds bounds, bool logAudit)
        {
            bounds = new Bounds(root.transform.position, Vector3.zero);
            bool found = false;
            bool importedVisual = root.GetComponent<ImportedRatVisualMarker>() != null ||
                                   root.GetComponentInChildren<ImportedRatVisualMarker>(true) != null;
            SkinnedMeshRenderer importedBody = importedVisual ? FindImportedBodyRenderer(root) : null;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                string reason;
                bool include = IsPortraitBodyRenderer(renderer, importedVisual, importedBody, out reason);
                if (logAudit)
                {
                    Debug.Log("[Rat Habitat] Portrait renderer audit: root=" + root.name +
                        " renderer=" + renderer.gameObject.name +
                        " active=" + renderer.gameObject.activeInHierarchy +
                        " type=" + renderer.GetType().Name +
                        " boundsSize=" + renderer.bounds.size.ToString("0.###") +
                        " layer=" + renderer.gameObject.layer +
                        " included=" + include +
                        " reason=" + reason);
                }
                if (!include) continue;
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

        private static SkinnedMeshRenderer FindImportedBodyRenderer(GameObject root)
        {
            SkinnedMeshRenderer largestValidBody = null;
            float largestBoundsArea = 0f;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (string.Equals(renderer.gameObject.name, "rat_mesh", System.StringComparison.OrdinalIgnoreCase)) return renderer;

                float boundsArea = renderer.bounds.size.sqrMagnitude;
                if (largestValidBody == null || boundsArea > largestBoundsArea)
                {
                    largestValidBody = renderer;
                    largestBoundsArea = boundsArea;
                }
            }
            return largestValidBody;
        }

        private static bool IsPortraitBodyRenderer(Renderer renderer, bool importedVisual, SkinnedMeshRenderer importedBody, out string reason)
        {
            reason = "body renderer";
            if (renderer == null)
            {
                reason = "null renderer";
                return false;
            }
            if (!renderer.gameObject.activeInHierarchy)
            {
                reason = "inactive GameObject";
                return false;
            }
            if (!renderer.enabled)
            {
                reason = "renderer disabled";
                return false;
            }
            if (renderer.bounds.size.sqrMagnitude <= 0.0001f)
            {
                reason = "empty world bounds";
                return false;
            }

            string objectName = renderer.gameObject.name.ToLowerInvariant();
            if (IsPortraitHelperName(objectName))
            {
                reason = "helper/marking/selection geometry";
                return false;
            }
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            {
                reason = "non-body effect renderer";
                return false;
            }

            // The imported hand-painted asset has one body renderer named
            // rat_mesh. Use that exact visible SkinnedMeshRenderer only. If a
            // future imported hierarchy renames it, FindImportedBodyRenderer
            // deterministically falls back to the largest active skinned
            // renderer instead of allowing helper geometry into the bounds.
            if (importedVisual && renderer != importedBody)
            {
                reason = "not the selected imported rat body renderer";
                return false;
            }
            if (importedVisual)
            {
                reason = string.Equals(renderer.gameObject.name, "rat_mesh", System.StringComparison.OrdinalIgnoreCase)
                    ? "rat_mesh body renderer"
                    : "fallback main skinned body renderer";
            }
            return true;
        }

        private static bool IsPortraitHelperName(string objectName)
        {
            return objectName.Contains("genetics spotting markings") ||
                   objectName.Contains("body mark") ||
                   objectName.Contains("spotting overlay") ||
                   objectName.Contains("selection ring") ||
                   objectName.Contains("overlay") ||
                   objectName.Contains("helper") ||
                   objectName.Contains("debug") ||
                   objectName.Contains("highlight");
        }

        private static float CalculateFramingSize(Bounds bounds, Camera camera, float aspect, float padding)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float horizontalExtent = 0f;
            float verticalExtent = 0f;
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 cameraSpace = camera.transform.InverseTransformPoint(corner);
                        horizontalExtent = Mathf.Max(horizontalExtent, Mathf.Abs(cameraSpace.x));
                        verticalExtent = Mathf.Max(verticalExtent, Mathf.Abs(cameraSpace.y));
                    }
                }
            }

            float safeAspect = Mathf.Max(0.01f, aspect);
            float requiredHalfHeight = Mathf.Max(verticalExtent, horizontalExtent / safeAspect);
            return Mathf.Max(0.01f, requiredHalfHeight * Mathf.Max(1f, padding));
        }
    }
}
