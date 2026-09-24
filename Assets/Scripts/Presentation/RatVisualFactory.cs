using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Creates the replaceable visual for a rat life stage. RatData is never
    /// stored in a prefab: this factory receives the data and applies only the
    /// current presentation state to the instantiated visual.
    /// </summary>
    [DisallowMultipleComponent]
    public class RatVisualFactory : MonoBehaviour
    {
        private const string SpotShaderResourcePath = "HandPaintedRat/RatCoatSpotShader";
        private const string SpotMaskResourcePath = "HandPaintedRat/rat_spot_body_mask";
        private const string PinkieSkinResourcePath = "HandPaintedRat_PinkieSkin";
        private const string PinkieControllerResourcePath = "HandPaintedRat_Pinkie";
        private const string LegacyPinkieResourcePath = "Rat_Pinkie_Prototype";
        private const string ImportedPinkiePrefabName = "HandPaintedRat_Pinkie";
        private const string SpotShaderName = "Rat Habitat/Hand Painted Rat Coat";
        private const string PhenotypeMaterialMarker = "[Rat Habitat Phenotype]";

        [Header("Replaceable visual assets")]
        [Tooltip("Assign the imported TurboSquid Hand Painted Rat prefab here. The same prefab is used for Young Rat and Adult, with different scales.")]
        public GameObject handPaintedRatPrefab;

        [Tooltip("Optional explicit reference to the imported HandPaintedRat_Pinkie prefab. If empty, it is loaded from Resources.")]
        public GameObject pinkiePrototypePrefab;

        [Header("Runtime lookup")]
        [Tooltip("Resources path for the imported Hand Painted Rat prefab when no Inspector reference is assigned.")]
        public string handPaintedRatResourcePath = GameConfig.HandPaintedRatResourcePath;

        [Tooltip("Resources path for the imported pinkie prefab when no Inspector reference is assigned.")]
        public string pinkiePrototypeResourcePath = GameConfig.PinkiePrototypeResourcePath;

        [Header("Emergency fallback")]
        [Tooltip("Emergency-only fallback. Keep disabled during normal play so an invalid imported pinkie cannot silently become the old procedural blob.")]
        public bool allowProceduralPinkieFallback = false;

        private GameObject cachedHandPaintedPrefab;
        private GameObject cachedPinkiePrefab;
        private string cachedPinkieResourcePath;
        private bool pinkieResolutionLogged;
        private static Shader cachedSpotShader;
        private static Texture2D cachedSpotMask;
        private static bool spotResourcesResolved;
        private static Material cachedPinkieSkin;
        private static bool pinkieSkinLookupResolved;
        private static readonly Dictionary<string, Texture2D> organicSpotPatternCache =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private string lastPhenotypeAuditSignature;

        public bool UsesImportedHandPaintedRat
        {
            get { return ResolveHandPaintedPrefab() != null; }
        }

        public GameObject CreateStageVisual(Transform parent, RatData rat)
        {
            if (parent == null || rat == null) return null;
            if (rat.stage == RatStage.Pinkie) return CreatePinkieVisual(parent, rat);

            var importedPrefab = ResolveHandPaintedPrefab();
            if (importedPrefab != null)
            {
                var imported = UnityEngine.Object.Instantiate(importedPrefab, parent, false);
                imported.name = rat.name + " Hand Painted Rat Visual";
                if (imported.GetComponent<ImportedRatVisualMarker>() == null)
                {
                    imported.AddComponent<ImportedRatVisualMarker>();
                }
                DisableImportedColliders(imported);
                NormalizeImportedModel(imported);
                // The source mesh is authored facing the opposite local-Z
                // direction from the habitat's rat movement/selection space.
                // Rotate only this replaceable visual root; the stable rat
                // root, selection collider, ring, habitat, and camera remain
                // untouched.
                imported.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                ApplyPhenotype(imported, rat);
                return imported;
            }

            // The project remains runnable before the purchased FBX has been
            // imported. This fallback uses the same phenotype data and can be
            // replaced without touching the gameplay or selection systems.
            return CreateProceduralFurredVisual(parent, rat);
        }

        public void ApplyPhenotype(GameObject visual, RatData rat)
        {
            if (visual == null || rat == null) return;
            if (rat.stage == RatStage.Pinkie)
            {
                // A prefab can be rebuilt while the editor still has an
                // already-instantiated newborn in the scene. Reconcile that
                // existing visual here so it starts the authored clip without
                // creating a second model or changing the stable rat root.
                ApplyPinkieVisualYaw(visual, rat);
                ConfigurePinkieAnimation(visual, rat);
                return;
            }
            ApplyEnclosureVisualPlacement(visual, rat);
            if (rat.phenotype == null || !rat.phenotype.furRevealed) return;

            Color coat = ParseColor(rat.phenotype.coatColorHex, new Color(0.3f, 0.3f, 0.34f));
            Color accent = ParseColor(rat.phenotype.accentHex, new Color(0.68f, 0.68f, 0.7f));
            bool albino = rat.phenotype.coatColorId == "albino";
            bool spotted = rat.phenotype.spotted && !albino;
            bool importedVisual = IsImportedVisual(visual);
            // Keep the authored hand-painted map for albinos. The map contains
            // the eyes, mouth, whisker/tail shading, and other feature detail
            // on this asset's single skinned renderer. The albino shader
            // neutralizes only the fur color; replacing the map with
            // Texture2D.whiteTexture erases those details entirely.
            var coatTexture = importedVisual
                ? ResolveCoatTexture(albino ? "albino" : rat.phenotype.coatColorId)
                : null;
            Shader spotShader = null;
            Texture2D spotMask = null;
            // Albino uses the same hand-painted source texture through a
            // dedicated shader mode that removes the beige/tan cast while
            // preserving luminance detail. It is still a per-renderer
            // material instance, so no other rat is recolored.
            bool useCoatShader = importedVisual && (spotted || albino) && TryResolveSpotResources(out spotShader, out spotMask);
            Texture2D organicSpotPattern = useCoatShader && spotted
                ? GetOrCreateOrganicSpotPattern(rat)
                : null;
            string materialAudit = string.Empty;
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                // Use explicit per-renderer material instances for the
                // imported FBX. The source asset remains unchanged and this
                // avoids Renderer.materials edit-mode instantiation warnings.
                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0) continue;

                string rendererName = renderer.gameObject.name.ToLowerInvariant();
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null) continue;
                    if (!material.name.Contains(PhenotypeMaterialMarker))
                    {
                        material = new Material(material);
                        material.name = material.name + " " + PhenotypeMaterialMarker;
                        materials[materialIndex] = material;
                    }
                    string materialName = material.name.ToLowerInvariant();
                    bool featureMaterial = IsFeatureMaterial(rendererName, materialName);
                    // A separate feature renderer/material slot must remain
                    // on its authored texture. This protects eyes, mouth,
                    // ears, feet, whiskers, and segmented tail materials when
                    // an imported variant is split into multiple renderers.
                    // The current FBX has one rat_mesh material, so the shader
                    // below preserves its baked feature pixels instead.
                    bool albinoFeature = albino && featureMaterial;
                    if (featureMaterial && !albinoFeature) continue;

                    if (useCoatShader && !featureMaterial)
                    {
                        material.shader = spotShader;
                    }

                    bool albinoPinkFeature = albinoFeature && IsAlbinoPinkFeature(rendererName, materialName);
                    Color target = IsAccentMaterial(rendererName, materialName) || albinoPinkFeature
                        ? accent
                        : (albinoFeature ? Color.white : coat);
                    // Keep the hand-painted texture's detail while making the
                    // genetics result the dominant color signal.
                    target = Color.Lerp(Color.white, target, 0.94f);
                    renderer.SetPropertyBlock(null, materialIndex);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", target);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", target);
                    if (importedVisual && !featureMaterial && coatTexture != null)
                    {
                        // Always restore the intended source map on a cloned
                        // material. This also repairs an already-instantiated
                        // visual that was created by the old albino path and
                        // still has Texture2D.whiteTexture assigned.
                        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", coatTexture);
                        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", coatTexture);
                    }
                    if (material.shader != null && material.shader.name == SpotShaderName && !featureMaterial)
                    {
                        material.SetTexture("_SpotMask", spotMask);
                        material.SetTexture("_SpotPattern", organicSpotPattern == null
                            ? Texture2D.blackTexture
                            : organicSpotPattern);
                        material.SetFloat("_SpotSeed", SpotSeed01(string.IsNullOrEmpty(rat.id) ? rat.name : rat.id));
                        material.SetColor("_SpotColor", Color.white);
                        material.SetFloat("_SpotStrength", spotted ? 1f : 0f);
                        if (material.HasProperty("_AlbinoMode")) material.SetFloat("_AlbinoMode", albino ? 1f : 0f);
                        if (material.HasProperty("_AlbinoBodyColor")) material.SetColor("_AlbinoBodyColor", new Color(0.98f, 0.965f, 0.92f, 1f));
                    }
                    renderer.sharedMaterials = materials;
                    if (materialAudit.Length > 0) materialAudit += "; ";
                    materialAudit += renderer.gameObject.name + "=" + material.name + " instanceId=" + material.GetInstanceID() +
                        " shader=" + (material.shader == null ? "none" : material.shader.name) +
                        " texture=" + TexturePropertySummary(material) +
                        " color=" + ColorPropertySummary(material) +
                        " albinoMode=" + AlbinoModeSummary(material);
                }
            }
            if (materialAudit.Length == 0) materialAudit = "none";

            string auditSignature = (string.IsNullOrEmpty(rat.id) ? rat.name : rat.id) + "|" +
                GenotypeSummary(rat.genotype) + "|" + rat.phenotype.coatColorId + "|" +
                rat.phenotype.coatColorHex + "|" + rat.phenotype.accentHex + "|" + spotted + "|" + materialAudit;
            if (auditSignature != lastPhenotypeAuditSignature)
            {
                lastPhenotypeAuditSignature = auditSignature;
                Debug.Log("[Rat Habitat] Phenotype material audit: rat=" + rat.name +
                    " genotype=" + GenotypeSummary(rat.genotype) +
                    " coatColorId=" + rat.phenotype.coatColorId +
                    " coatColorHex=" + rat.phenotype.coatColorHex +
                    " accentHex=" + rat.phenotype.accentHex +
                    " spotted=" + rat.phenotype.spotted +
                    " selectedTexture=" + (coatTexture == null ? "<null>" : coatTexture.name) +
                    " selectedMaterial=" + materialAudit);
            }
        }

        private static bool IsImportedVisual(GameObject visual)
        {
            return visual != null &&
                   (visual.GetComponent<ImportedRatVisualMarker>() != null ||
                    visual.GetComponentInParent<ImportedRatVisualMarker>() != null);
        }

        private static void DisableImportedColliders(GameObject visual)
        {
            if (visual == null) return;
            var colliders = visual.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider != null) collider.enabled = false;
            }
        }

        private static Texture2D ResolveCoatTexture(string coatColorId)
        {
            string resourceName = "rat_bege_psd";
            if (coatColorId == "black" || coatColorId == "diluted-black") resourceName = "rat_grey";
            else if (coatColorId == "brown" || coatColorId == "diluted-brown") resourceName = "rat_khaki";

            return LoadCoatTexture(resourceName);
        }

        private static Texture2D LoadCoatTexture(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            string resourcePath = "HandPaintedRat/" + resourceName;
            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Debug.LogWarning("[Rat Habitat] Coat texture Resources.Load<Texture2D> returned null for '" + resourcePath + "'.");
            }
            return texture;
        }

        private static string TexturePropertySummary(Material material)
        {
            if (material == null) return "<null>";
            Texture texture = null;
            string property = "none";
            if (material.HasProperty("_MainTex"))
            {
                texture = material.GetTexture("_MainTex");
                property = "_MainTex";
            }
            if (texture == null && material.HasProperty("_BaseMap"))
            {
                texture = material.GetTexture("_BaseMap");
                property = "_BaseMap";
            }
            return property + "=" + (texture == null ? "<null>" : texture.name);
        }

        private static string ColorPropertySummary(Material material)
        {
            if (material == null) return "<null>";
            if (material.HasProperty("_Color")) return "_Color=" + material.GetColor("_Color").ToString();
            if (material.HasProperty("_BaseColor")) return "_BaseColor=" + material.GetColor("_BaseColor").ToString();
            return "none";
        }

        private static string AlbinoModeSummary(Material material)
        {
            return material != null && material.HasProperty("_AlbinoMode")
                ? material.GetFloat("_AlbinoMode").ToString("0.##")
                : "n/a";
        }

        private static string GenotypeSummary(GenotypeData genotype)
        {
            if (genotype == null) return "B/B C/C D/D s/s";
            return GeneticsSystem.FormatPair(genotype, "B") + " " +
                GeneticsSystem.FormatPair(genotype, "C") + " " +
                GeneticsSystem.FormatPair(genotype, "D") + " " +
                GeneticsSystem.FormatPair(genotype, "S");
        }

        private static bool TryResolveSpotResources(out Shader shader, out Texture2D mask)
        {
            if (!spotResourcesResolved)
            {
                cachedSpotShader = Resources.Load<Shader>(SpotShaderResourcePath);
                cachedSpotMask = Resources.Load<Texture2D>(SpotMaskResourcePath);
                spotResourcesResolved = true;
            }
            shader = cachedSpotShader;
            mask = cachedSpotMask;
            return shader != null && mask != null;
        }

        private static float SpotSeed01(string value)
        {
            return StableSpotSeed(value ?? string.Empty) / 2147483647f;
        }

        private const int OrganicSpotMaskWidth = 128;
        private const int OrganicSpotMaskHeight = 64;
        private static readonly Vector2[] OrganicSpotAnchors =
        {
            new Vector2(0.19f, 0.16f),
            new Vector2(0.34f, 0.27f),
            new Vector2(0.48f, 0.15f),
            new Vector2(0.59f, 0.28f),
        };

        private static readonly Vector2[] HoodedSpotAnchors =
        {
            new Vector2(0.14f, 0.18f), new Vector2(0.22f, 0.28f),
            new Vector2(0.31f, 0.17f), new Vector2(0.38f, 0.25f),
        };
        private static readonly Vector2[] BerkshireSpotAnchors =
        {
            new Vector2(0.39f, 0.14f), new Vector2(0.50f, 0.27f),
            new Vector2(0.62f, 0.16f),
        };
        private static readonly Vector2[] CappedSpotAnchors =
        {
            new Vector2(0.13f, 0.18f), new Vector2(0.21f, 0.25f),
        };
        private static readonly Vector2[] BarebackSpotAnchors =
        {
            new Vector2(0.29f, 0.16f), new Vector2(0.43f, 0.25f),
        };
        private static readonly Vector2[] IrishSpotAnchors =
        {
            new Vector2(0.31f, 0.08f), new Vector2(0.50f, 0.10f),
        };

        /// <summary>
        /// Creates a stable body-UV mask once for a rat's ID/genetics. Each
        /// patch is a deliberately irregular 8-14 point polygon rather than a
        /// repeated circle. The resulting texture is sampled by the coat
        /// shader, so no random/noise generation runs during rendering.
        /// </summary>
        private static Texture2D GetOrCreateOrganicSpotPattern(RatData rat)
        {
            string identity = (rat == null ? string.Empty :
                (string.IsNullOrEmpty(rat.id) ? rat.name : rat.id)) + "|" +
                (rat == null ? string.Empty : GenotypeSummary(rat.genotype)) + "|" +
                (rat == null ? string.Empty : rat.markingFamily) + "|organic-patterns-v4";
            if (organicSpotPatternCache.TryGetValue(identity, out Texture2D cached) && cached != null)
                return cached;

            var pattern = new Texture2D(OrganicSpotMaskWidth, OrganicSpotMaskHeight,
                TextureFormat.RGBA32, false, true)
            {
                name = "Organic Rat Spot Pattern " + StableSpotSeed(identity),
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };

            Color32[] pixels = new Color32[OrganicSpotMaskWidth * OrganicSpotMaskHeight];
            var random = new OrganicSpotRandom((uint)StableSpotSeed(identity));
            string family = rat == null ? string.Empty : rat.markingFamily;
            switch (family)
            {
                case "Hooded":
                    // Keep a colored dorsal stripe while whitening the belly
                    // and side bands. The irregular edges prevent a hard
                    // machine-cut transition around the shoulders.
                    PaintJitteredRegion(pixels, 0.03f, 0.72f, 0.055f, 0.19f, ref random);
                    PaintJitteredRegion(pixels, 0.03f, 0.72f, 0.32f, 0.395f, ref random);
                    break;
                case "Mismarked hooded":
                    PaintJitteredRegion(pixels, 0.03f, 0.72f, 0.055f, 0.20f, ref random);
                    PaintJitteredRegion(pixels, 0.03f, 0.72f, 0.31f, 0.395f, ref random);
                    // A mismarked hooded rat has one or two broken white
                    // intrusions into the otherwise continuous stripe.
                    PaintJitteredRegion(pixels, 0.24f, 0.36f, 0.205f, 0.285f, ref random);
                    PaintJitteredRegion(pixels, 0.50f, 0.61f, 0.205f, 0.275f, ref random);
                    break;
                case "Bareback":
                    // Colored head/shoulders transition into a mostly white
                    // body at a naturally uneven neck line.
                    PaintJitteredRegion(pixels, 0.23f, 0.72f, 0.055f, 0.395f, ref random);
                    PaintJitteredRegion(pixels, 0.18f, 0.31f, 0.08f, 0.18f, ref random);
                    break;
                case "Capped":
                    // The body is white; the head/cap remains colored because
                    // this mask is clipped to the body UV island.
                    PaintJitteredRegion(pixels, 0.02f, 0.72f, 0.045f, 0.405f, ref random);
                    break;
                case "Berkshire":
                    // White lower belly plus a chest/foot sweep, leaving the
                    // back and sides in the selected coat color.
                    PaintJitteredRegion(pixels, 0.02f, 0.72f, 0.05f, 0.17f, ref random);
                    PaintJitteredRegion(pixels, 0.02f, 0.19f, 0.14f, 0.36f, ref random);
                    break;
                case "Irish":
                    // A compact chest patch and two uneven lower foot marks.
                    PaintJitteredRegion(pixels, 0.04f, 0.20f, 0.12f, 0.28f, ref random);
                    PaintJitteredRegion(pixels, 0.28f, 0.39f, 0.055f, 0.13f, ref random);
                    PaintJitteredRegion(pixels, 0.49f, 0.59f, 0.05f, 0.12f, ref random);
                    break;
                case "Blaze":
                    // The front-most body island carries the lower end of the
                    // face blaze; the same UV mask keeps it attached to skin.
                    PaintJitteredRegion(pixels, 0.04f, 0.18f, 0.22f, 0.38f, ref random);
                    PaintJitteredRegion(pixels, 0.09f, 0.16f, 0.13f, 0.24f, ref random);
                    break;
                case "Variegated":
                    PaintJitteredRegion(pixels, 0.08f, 0.22f, 0.09f, 0.32f, ref random);
                    PaintJitteredRegion(pixels, 0.26f, 0.42f, 0.19f, 0.38f, ref random);
                    PaintJitteredRegion(pixels, 0.45f, 0.60f, 0.07f, 0.27f, ref random);
                    PaintJitteredRegion(pixels, 0.57f, 0.70f, 0.22f, 0.37f, ref random);
                    break;
                case "Dalmatian-style":
                    PaintIrregularSpotPatches(pixels, OrganicSpotAnchors, 6, ref random);
                    break;
                case "Solid":
                    // Solid coats intentionally have no white overlay.
                    break;
                default:
                    PaintIrregularSpotPatches(pixels, OrganicSpotAnchors, 4, ref random);
                    break;
            }

            pattern.SetPixels32(pixels);
            pattern.Apply(false, true);
            organicSpotPatternCache[identity] = pattern;
            return pattern;
        }

        private static Vector2[] GetOrganicSpotAnchors(string family)
        {
            switch (family)
            {
                case "Hooded":
                case "Mismarked hooded": return HoodedSpotAnchors;
                case "Berkshire": return BerkshireSpotAnchors;
                case "Capped": return CappedSpotAnchors;
                case "Bareback": return BarebackSpotAnchors;
                case "Irish": return IrishSpotAnchors;
                case "Blaze": return CappedSpotAnchors;
                case "Variegated": return OrganicSpotAnchors;
                case "Dalmatian-style": return OrganicSpotAnchors;
                default: return OrganicSpotAnchors;
            }
        }

        private static void PaintIrregularSpotPatches(
            Color32[] pixels,
            Vector2[] anchors,
            int minimumCount,
            ref OrganicSpotRandom random)
        {
            if (anchors == null || anchors.Length == 0) return;
            int patchCount = minimumCount + random.NextInt(0, 3);
            for (int patchIndex = 0; patchIndex < patchCount; patchIndex++)
            {
                Vector2 anchor = anchors[patchIndex % anchors.Length];
                Vector2 center = new Vector2(
                    Mathf.Clamp(anchor.x + random.Range(-0.035f, 0.035f), 0.06f, 0.70f),
                    Mathf.Clamp(anchor.y + random.Range(-0.035f, 0.035f), 0.055f, 0.385f));
                Vector2 radius = new Vector2(random.Range(0.035f, 0.085f), random.Range(0.022f, 0.055f));
                float rotation = random.Range(-0.6f, 0.6f);
                int pointCount = random.NextInt(8, 15);
                Vector2[] polygon = new Vector2[pointCount];
                float cos = Mathf.Cos(rotation);
                float sin = Mathf.Sin(rotation);
                for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    float angle = (pointIndex / (float)pointCount) * Mathf.PI * 2f + random.Range(-0.12f, 0.12f);
                    float radialJitter = random.Range(0.72f, 1.24f);
                    Vector2 local = new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y) * radialJitter;
                    polygon[pointIndex] = center + new Vector2(
                        local.x * cos - local.y * sin,
                        local.x * sin + local.y * cos);
                }
                PaintOrganicPolygon(pixels, polygon);
            }
        }

        private static void PaintJitteredRegion(
            Color32[] pixels,
            float minX,
            float maxX,
            float minY,
            float maxY,
            ref OrganicSpotRandom random)
        {
            float xMid = Mathf.Lerp(minX, maxX, random.Range(0.40f, 0.60f));
            float yMid = Mathf.Lerp(minY, maxY, random.Range(0.40f, 0.60f));
            float xJitter = Mathf.Min(0.025f, (maxX - minX) * 0.12f);
            float yJitter = Mathf.Min(0.018f, (maxY - minY) * 0.12f);
            Vector2[] polygon =
            {
                new Vector2(minX, minY + random.Range(-yJitter, yJitter)),
                new Vector2(xMid, minY + random.Range(-yJitter, yJitter)),
                new Vector2(maxX, minY + random.Range(-yJitter, yJitter)),
                new Vector2(maxX + random.Range(-xJitter, xJitter), yMid),
                new Vector2(maxX, maxY + random.Range(-yJitter, yJitter)),
                new Vector2(xMid, maxY + random.Range(-yJitter, yJitter)),
                new Vector2(minX, maxY + random.Range(-yJitter, yJitter)),
                new Vector2(minX + random.Range(-xJitter, xJitter), yMid),
            };
            PaintOrganicPolygon(pixels, polygon);
        }

        private static void PaintOrganicPolygon(Color32[] pixels, Vector2[] polygon)
        {
            if (pixels == null || polygon == null || polygon.Length < 3) return;
            for (int y = 0; y < OrganicSpotMaskHeight; y++)
            {
                for (int x = 0; x < OrganicSpotMaskWidth; x++)
                {
                    // Two-by-two supersampling leaves the uneven polygon
                    // edge softly antialiased when the UV mask is filtered.
                    int covered = 0;
                    for (int sampleY = 0; sampleY < 2; sampleY++)
                    {
                        for (int sampleX = 0; sampleX < 2; sampleX++)
                        {
                            Vector2 uv = new Vector2(
                                (x + (sampleX + 0.5f) * 0.5f) / OrganicSpotMaskWidth,
                                (y + (sampleY + 0.5f) * 0.5f) / OrganicSpotMaskHeight);
                            if (PointInsidePolygon(uv, polygon)) covered++;
                        }
                    }

                    if (covered == 0) continue;
                    int index = y * OrganicSpotMaskWidth + x;
                    byte value = (byte)(covered * 255 / 4);
                    if (value > pixels[index].r)
                        pixels[index] = new Color32(value, value, value, 255);
                }
            }
        }

        private static bool PointInsidePolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                Vector2 currentPoint = polygon[i];
                Vector2 previousPoint = polygon[previous];
                bool crossesRay = (currentPoint.y > point.y) != (previousPoint.y > point.y);
                if (crossesRay)
                {
                    float intersectionX = (previousPoint.x - currentPoint.x) *
                        (point.y - currentPoint.y) /
                        (previousPoint.y - currentPoint.y) + currentPoint.x;
                    if (point.x < intersectionX) inside = !inside;
                }
            }
            return inside;
        }

        private struct OrganicSpotRandom
        {
            private uint state;

            public OrganicSpotRandom(uint seed)
            {
                state = seed == 0u ? 1u : seed;
            }

            public float Next01()
            {
                state = state * 1664525u + 1013904223u;
                return (state & 0x00ffffffu) / 16777216f;
            }

            public float Range(float minimum, float maximum)
            {
                return Mathf.Lerp(minimum, maximum, Next01());
            }

            public int NextInt(int minimumInclusive, int maximumExclusive)
            {
                if (maximumExclusive <= minimumInclusive) return minimumInclusive;
                return minimumInclusive + Mathf.FloorToInt(Next01() * (maximumExclusive - minimumInclusive));
            }
        }

        private GameObject CreatePinkieVisual(Transform parent, RatData rat)
        {
            var prefab = ResolvePinkiePrefab();
            if (prefab == null)
            {
                return HandlePinkieFailure(parent, rat,
                    "Resources.Load<GameObject> returned no valid imported pinkie prefab.", "<unresolved>");
            }

            try
            {
                // Keep the safe non-generic Instantiate validation so a stale
                // Unity object can never reach an unsafe GameObject cast.
                UnityEngine.Object instance = UnityEngine.Object.Instantiate(
                    (UnityEngine.Object)prefab, parent, false);
                var pinkie = instance as GameObject;
                if (pinkie == null)
                {
                    DestroyPinkieObject(instance);
                    return HandlePinkieFailure(parent, rat,
                        "The resolved prefab did not instantiate as a GameObject.", prefab.name);
                }

                // A replacement FBX may have been imported before the editor
                // setup rebuilt its prefab. Remove any stale authoring cameras,
                // lights, and listeners immediately so a newborn can never
                // take over the habitat Game view.
                RemoveImportedAuxiliaryComponents(pinkie);

                // The prefab asset has already passed the marker/identity
                // validation in LoadPinkiePrefab. The instantiated clone can
                // have Unity's generated clone name, so validate its actual
                // imported renderer structure here instead of using a name
                // test for the runtime instance.
                if (!ValidateImportedPinkieInstance(pinkie, out string validationError))
                {
                    DestroyPinkieObject(pinkie);
                    return HandlePinkieFailure(parent, rat, validationError, prefab.name);
                }

                // Repair a prefab instance that was loaded from Unity's stale
                // in-memory import before the generated marker components were
                // reserialized. The exact Resources identity plus a valid mesh
                // has already been checked above; add the markers back to this
                // runtime instance so all downstream systems see an imported
                // pinkie positively, not as a procedural fallback.
                if (pinkie.GetComponent<ImportedRatVisualMarker>() == null)
                {
                    pinkie.AddComponent<ImportedRatVisualMarker>();
                }
                if (pinkie.GetComponent<HandPaintedRatPinkieMarker>() == null)
                {
                    pinkie.AddComponent<HandPaintedRatPinkieMarker>();
                }

                pinkie.name = rat.name + " Hand Painted Rat Pinkie Visual";

                DisableImportedColliders(pinkie);
                ApplyPinkieSkin(pinkie);
                NormalizeImportedModel(pinkie);
                ApplyPinkieVisualYaw(pinkie, rat);
                // Apply the authored visual-only height correction after
                // normalization and facing setup. The stable rat root,
                // Rat Visual Stage, nest, floor, colliders, and camera remain
                // unchanged; every newly created/reloaded pinkie receives
                // this same local offset exactly once.
                Vector3 pinkieLocalPosition = pinkie.transform.localPosition;
                pinkieLocalPosition.y = GameConfig.PinkieVisualVerticalOffset;
                pinkie.transform.localPosition = pinkieLocalPosition;

                // Configure playback after the final imported hierarchy,
                // normalization, facing, and visual offset are in place. This
                // guarantees the controller binds to the exact hierarchy that
                // is rendered in the habitat, while newborn roots remain
                // nest-bound and root-motion-free.
                ConfigurePinkieAnimation(pinkie, rat);

                if (!ValidateImportedPinkieInstance(pinkie, out validationError) || !HasPinkieSkin(pinkie))
                {
                    DestroyPinkieObject(pinkie);
                    return HandlePinkieFailure(parent, rat, string.IsNullOrEmpty(validationError)
                        ? "The instantiated FBX pinkie has no HandPaintedRat_PinkieSkin material/texture."
                        : validationError, prefab.name);
                }

                LogPinkieRuntimeAudit(rat, prefab.name, pinkie, "ImportedFBX");
                return pinkie;
            }
            catch (Exception exception)
            {
                return HandlePinkieFailure(parent, rat,
                    "Pinkie prefab instantiation failed: " + exception.Message,
                    prefab == null ? "<unresolved>" : prefab.name);
            }
        }

        private GameObject HandlePinkieFailure(Transform parent, RatData rat, string reason, string resolvedPrefabName)
        {
            if (allowProceduralPinkieFallback)
            {
                Debug.LogWarning("[Rat Habitat] Pinkie emergency fallback enabled: " + reason);
                var fallback = CreateProceduralPinkieFallback(parent, rat);
                LogPinkieRuntimeAudit(rat, resolvedPrefabName, fallback, "ProceduralFallback");
                return fallback;
            }

            Debug.LogError("[Rat Habitat] Pinkie imported-model validation failed for rat '" +
                (rat == null ? "<null>" : rat.name) + "' at Resources path '" +
                pinkiePrototypeResourcePath + "': " + reason +
                ". Procedural fallback is disabled.");
            var missing = new GameObject((rat == null ? "Pinkie" : rat.name) + " Pinkie Visual Missing");
            missing.transform.SetParent(parent, false);
            LogPinkieRuntimeAudit(rat, resolvedPrefabName, missing, "Missing");
            return missing;
        }

        private GameObject CreateProceduralPinkieFallback(Transform parent, RatData rat)
        {
            var fallback = new GameObject(rat.name + " Procedural Pinkie Fallback");
            fallback.transform.SetParent(parent, false);
            var prototype = fallback.AddComponent<RatPinkiePrototype>();
            prototype.Build();
            return fallback;
        }

        private static void DestroyPinkieObject(UnityEngine.Object instance)
        {
            if (instance == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }

        private static void ConfigurePinkieAnimation(GameObject visual, RatData rat)
        {
            if (visual == null) return;
            RuntimeAnimatorController controller = Resources.Load<RuntimeAnimatorController>(PinkieControllerResourcePath);
            if (controller == null)
            {
                Debug.LogError("[Rat Habitat] Pinkie animation controller was not found at Resources path '" +
                    PinkieControllerResourcePath + "'. The pinkie will hold its reference pose.");
                return;
            }

            AnimationClip[] clips = controller.animationClips;
            AnimationClip kickClip = null;
            if (clips != null)
            {
                foreach (AnimationClip clip in clips)
                {
                    if (clip != null && string.Equals(clip.name, "HandPaintedRat_PinkieKick", StringComparison.Ordinal))
                    {
                        kickClip = clip;
                        break;
                    }
                }
            }
            if (kickClip == null || kickClip.length <= 0f)
            {
                Debug.LogError("[Rat Habitat] Pinkie runtime animation audit failed: controller '" +
                    controller.name + "' has no usable HandPaintedRat_PinkieKick clip for rat '" +
                    (rat == null ? "<null>" : rat.name) + "'.");
                return;
            }

            Animator animator = visual.GetComponent<Animator>();
            if (animator == null)
            {
                foreach (Animator childAnimator in visual.GetComponentsInChildren<Animator>(true))
                {
                    if (childAnimator != null)
                    {
                        animator = childAnimator;
                        break;
                    }
                }
            }
            if (animator == null) animator = visual.AddComponent<Animator>();
            HandPaintedRatPinkieMarker pinkieMarker = visual.GetComponent<HandPaintedRatPinkieMarker>();
            if (pinkieMarker == null) pinkieMarker = visual.AddComponent<HandPaintedRatPinkieMarker>();
            foreach (Animator childAnimator in visual.GetComponentsInChildren<Animator>(true))
            {
                if (childAnimator != null && childAnimator != animator)
                {
                    childAnimator.enabled = false;
                    childAnimator.runtimeAnimatorController = null;
                }
            }
            bool phaseAlreadyApplied = pinkieMarker.animationPhaseApplied &&
                pinkieMarker.animationStartPhase >= 0f &&
                pinkieMarker.animationStartPhase < 1f &&
                animator.runtimeAnimatorController == controller && animator.enabled;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.speed = 1f;
            animator.enabled = true;
            int stateHash = Animator.StringToHash("Base Layer.HandPaintedRat_PinkieKick");
            if (!animator.HasState(0, stateHash))
            {
                stateHash = Animator.StringToHash("HandPaintedRat_PinkieKick");
            }
            if (!animator.HasState(0, stateHash))
            {
                Debug.LogError("[Rat Habitat] Pinkie runtime animation audit failed: Animator has no state HandPaintedRat_PinkieKick for rat '" +
                    (rat == null ? "<null>" : rat.name) + "'.");
                return;
            }

            if (phaseAlreadyApplied)
            {
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
                if (current.fullPathHash == stateHash || current.shortNameHash == stateHash)
                {
                    // The existing live visual is already using the imported
                    // clip. Keep its current normalized time so an Apply call
                    // cannot visibly restart every newborn in sync.
                    return;
                }
            }

            animator.Rebind();
            animator.Update(0f);
            float startPhase = PinkieAnimationStartPhase(rat == null ? null : rat.id);
            animator.Play(stateHash, 0, startPhase);
            animator.Update(0f);
            pinkieMarker.animationPhaseApplied = true;
            pinkieMarker.animationStartPhase = startPhase;
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            Debug.Log("[Rat Habitat] Pinkie animation runtime audit: rat='" +
                (rat == null ? "<null>" : rat.name) + "' animator='" + animator.name +
                "' enabled=" + animator.enabled + " controller='" + controller.name +
                "' clip='" + kickClip.name + "' length=" + kickClip.length.ToString("0.###") +
                "s state='" + stateInfo.shortNameHash + "' normalizedTime=" +
                stateInfo.normalizedTime.ToString("0.###") + "' looping=true rootMotion=" +
                animator.applyRootMotion + " avatar=" +
                (animator.avatar == null ? "<null>" : animator.avatar.name) +
                " startPhase=" + startPhase.ToString("0.###") + ".");
        }

        private static float PinkieAnimationStartPhase(string ratId)
        {
            int seed = StableSpotSeed((ratId ?? string.Empty) + "|pinkie-kick");
            return seed / 2147483647f;
        }

        private static void ApplyEnclosureVisualPlacement(GameObject visual, RatData rat)
        {
            if (visual == null || rat == null || rat.stage == RatStage.Pinkie) return;
            var importedMarker = visual.GetComponent<ImportedRatVisualMarker>();
            if (importedMarker == null) return;

            if (!importedMarker.hasNormalizedLocalPosition)
            {
                importedMarker.normalizedLocalPosition = visual.transform.localPosition;
                importedMarker.hasNormalizedLocalPosition = true;
            }

            Vector3 placement = importedMarker.normalizedLocalPosition;
            if (rat.enclosure == RatEnclosure.Pairing)
                placement.y += GameConfig.PairingAdultVisualVerticalOffset;
            visual.transform.localPosition = placement;
        }

        private static void ApplyPinkieVisualYaw(GameObject visual, RatData rat)
        {
            if (visual == null) return;
            int seed = StableSpotSeed((rat == null ? string.Empty : rat.id) + "|pinkie-yaw");
            float yawOffset = Mathf.Lerp(-45f, 45f, seed / 2147483647f);
            visual.transform.localRotation = Quaternion.Euler(0f, 180f + yawOffset, 0f);
        }

        private static void RemoveImportedAuxiliaryComponents(GameObject visual)
        {
            if (visual == null) return;
            foreach (Camera camera in visual.GetComponentsInChildren<Camera>(true))
            {
                if (camera != null)
                {
                    camera.enabled = false;
                    DestroyPinkieObject(camera);
                }
            }
            foreach (Light light in visual.GetComponentsInChildren<Light>(true))
            {
                if (light != null)
                {
                    light.enabled = false;
                    DestroyPinkieObject(light);
                }
            }
            foreach (AudioListener listener in visual.GetComponentsInChildren<AudioListener>(true))
            {
                if (listener != null)
                {
                    listener.enabled = false;
                    DestroyPinkieObject(listener);
                }
            }
        }

        private GameObject ResolveHandPaintedPrefab()
        {
            if (handPaintedRatPrefab != null) return handPaintedRatPrefab;
            if (cachedHandPaintedPrefab != null) return cachedHandPaintedPrefab;

            cachedHandPaintedPrefab = LoadResource<GameObject>(handPaintedRatResourcePath);
            if (cachedHandPaintedPrefab != null) return cachedHandPaintedPrefab;

            string[] alternatePaths =
            {
                "HandPaintedRat",
                "Hand Painted Rat",
                "Low Poly Hand Painted Rat",
                "Rat_HandPainted",
            };
            foreach (var path in alternatePaths)
            {
                cachedHandPaintedPrefab = LoadResource<GameObject>(path);
                if (cachedHandPaintedPrefab != null) return cachedHandPaintedPrefab;
            }

            // Do not scan every object in Resources. An imported FBX or other
            // asset can contain a stale GameObject/Prefab pointer; Unity then
            // reports "PPtr cast failed ... GameObject to Prefab" while the
            // scene is starting. Explicit paths above remain supported and
            // the procedural visual is a safe fallback until an imported rat
            // prefab is deliberately assigned.
            return null;
        }

        private GameObject ResolvePinkiePrefab()
        {
            // A scene that was already open when the old prefab was removed
            // can retain its serialized inspector value in memory. Migrate it
            // before any lookup so Play Mode cannot resolve the deleted legacy
            // path, even before the scene is re-saved.
            if (string.Equals(pinkiePrototypeResourcePath, LegacyPinkieResourcePath,
                StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[Rat Habitat] Migrating stale pinkie resource path '" +
                    pinkiePrototypeResourcePath + "' to '" + GameConfig.PinkiePrototypeResourcePath + "'.");
                pinkiePrototypeResourcePath = GameConfig.PinkiePrototypeResourcePath;
            }

            // A serialized scene reference can survive a resource rename.
            // Reject it before it can reach Instantiate, then clear the stale
            // field for this runtime session.
            if (pinkiePrototypePrefab != null && !IsValidPinkiePrefab(pinkiePrototypePrefab))
            {
                string validationError;
                ValidateImportedPinkieVisual(pinkiePrototypePrefab, out validationError);
                Debug.LogWarning("[Rat Habitat] Ignoring pinkiePrototypePrefab '" +
                    pinkiePrototypePrefab.name + "' because validation failed: " + validationError);
                pinkiePrototypePrefab = null;
            }

            if (pinkiePrototypePrefab != null)
            {
                LogPinkieResolution(pinkiePrototypePrefab, "explicit prefab");
                return pinkiePrototypePrefab;
            }

            // Clear a cache made with an older Resources key. This also makes
            // changing the serialized path deterministic without a domain
            // reload or a stale cached object.
            if (!string.Equals(cachedPinkieResourcePath, pinkiePrototypeResourcePath, StringComparison.Ordinal))
            {
                cachedPinkiePrefab = null;
                cachedPinkieResourcePath = pinkiePrototypeResourcePath;
                pinkieResolutionLogged = false;
            }

            if (cachedPinkiePrefab != null && !IsValidPinkiePrefab(cachedPinkiePrefab))
            {
                string validationError;
                ValidateImportedPinkieVisual(cachedPinkiePrefab, out validationError);
                Debug.LogError("[Rat Habitat] Cached pinkie prefab failed validation: " + validationError);
                cachedPinkiePrefab = null;
            }

            if (cachedPinkiePrefab != null)
            {
                LogPinkieResolution(cachedPinkiePrefab, "cached prefab");
                return cachedPinkiePrefab;
            }

            cachedPinkiePrefab = LoadPinkiePrefab(pinkiePrototypeResourcePath);
            if (cachedPinkiePrefab != null)
            {
                LogPinkieResolution(cachedPinkiePrefab, "Resources.Load at '" + pinkiePrototypeResourcePath + "'");
            }
            return cachedPinkiePrefab;
        }

        private static bool IsValidPinkiePrefab(GameObject prefab)
        {
            string validationError;
            return ValidateImportedPinkieVisual(prefab, out validationError);
        }

        private static bool ValidateImportedPinkieVisual(GameObject visual, out string error)
        {
            error = string.Empty;
            if (visual == null)
            {
                error = "The prefab or instance is null.";
                return false;
            }

            if (visual.GetComponent<RatPinkiePrototype>() != null)
            {
                error = "The object contains the legacy RatPinkiePrototype component.";
                return false;
            }

            if (visual.GetComponent<HandPaintedRatPinkieMarker>() == null &&
                !IsKnownImportedPinkiePrefab(visual))
            {
                error = "The imported prefab is missing HandPaintedRatPinkieMarker.";
                return false;
            }

            if (FindPinkieRenderer(visual) == null)
            {
                error = "The imported prefab has no enabled MeshRenderer/SkinnedMeshRenderer with a non-null mesh.";
                return false;
            }

            return true;
        }

        private static bool ValidateImportedPinkieInstance(GameObject visual, out string error)
        {
            error = string.Empty;
            if (visual == null)
            {
                error = "The instantiated pinkie is null.";
                return false;
            }

            if (visual.GetComponent<RatPinkiePrototype>() != null)
            {
                error = "The instantiated object contains the legacy RatPinkiePrototype component.";
                return false;
            }

            if (FindPinkieRenderer(visual) == null)
            {
                error = "The instantiated pinkie has no enabled MeshRenderer/SkinnedMeshRenderer with a non-null mesh.";
                return false;
            }

            return true;
        }

        private static bool IsKnownImportedPinkiePrefab(GameObject visual)
        {
            if (visual == null) return false;
            string name = visual.name;
            if (string.IsNullOrEmpty(name)) return false;
            return name.Equals(ImportedPinkiePrefabName, StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(ImportedPinkiePrefabName + " (Clone)", StringComparison.OrdinalIgnoreCase);
        }

        private static Renderer FindPinkieRenderer(GameObject visual)
        {
            if (visual == null) return null;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsUsablePinkieRenderer(renderer)) continue;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) return renderer;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) return renderer;
            }
            return null;
        }

        private static bool IsUsablePinkieRenderer(Renderer renderer)
        {
            return renderer != null && renderer.enabled && renderer.gameObject.activeSelf;
        }

        private static Mesh GetPinkieRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static Texture GetPinkieTexture(Material material)
        {
            if (material == null) return null;
            Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            if (texture == null && material.HasProperty("_BaseMap")) texture = material.GetTexture("_BaseMap");
            return texture;
        }

        private static bool HasPinkieSkin(GameObject visual)
        {
            Material expected = Resources.Load<Material>(PinkieSkinResourcePath);
            Texture expectedTexture = GetPinkieTexture(expected);
            if (expected == null || expectedTexture == null) return false;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsUsablePinkieRenderer(renderer)) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    if (material == expected) return true;
                    Texture texture = GetPinkieTexture(material);
                    if (texture != null && (texture == expectedTexture ||
                        texture.name.Equals("HandPaintedRat_PinkieSkin", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string DescribePinkieSkin(GameObject visual)
        {
            if (visual == null) return "none";
            string description = string.Empty;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsUsablePinkieRenderer(renderer)) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    Texture texture = GetPinkieTexture(material);
                    string textureName = texture == null ? "<none>" : texture.name;
                    if (description.Length > 0) description += " | ";
                    description += material.name + " texture='" + textureName + "'";
                }
            }
            return description.Length == 0 ? "none" : description;
        }

        private static string DescribePinkieRenderers(GameObject visual)
        {
            if (visual == null) return "none";
            string description = string.Empty;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsUsablePinkieRenderer(renderer)) continue;
                Mesh mesh = GetPinkieRendererMesh(renderer);
                if (mesh == null) continue;
                if (description.Length > 0) description += " | ";
                description += renderer.GetType().Name + " on '" + renderer.gameObject.name +
                    "' mesh='" + mesh.name + "' layer=" + renderer.gameObject.layer;
            }
            return description.Length == 0 ? "none" : description;
        }

        private void LogPinkieRuntimeAudit(RatData rat, string resolvedPrefabName, GameObject visual, string source)
        {
            bool marker = visual != null && visual.GetComponent<HandPaintedRatPinkieMarker>() != null;
            bool legacy = visual != null && visual.GetComponent<RatPinkiePrototype>() != null;
            bool importedIdentity = string.Equals(resolvedPrefabName, ImportedPinkiePrefabName,
                StringComparison.OrdinalIgnoreCase);
            Debug.Log("[Rat Habitat] Pinkie runtime audit: rat='" + (rat == null ? "<null>" : rat.name) +
                "' id='" + (rat == null ? "<null>" : rat.id) + "' resolvedPrefab='" + resolvedPrefabName +
                "' resourcePath='" + pinkiePrototypeResourcePath + "' source=" + source +
                " marker=" + marker + " importedPrefabIdentity=" + importedIdentity +
                " legacyProcedural=" + legacy +
                " renderers=[" + DescribePinkieRenderers(visual) + "]" +
                " skin=[" + DescribePinkieSkin(visual) + "]");
        }

        private void LogPinkieResolution(GameObject prefab, string source)
        {
            if (pinkieResolutionLogged || prefab == null) return;
            pinkieResolutionLogged = true;
            Debug.Log("[Rat Habitat] Pinkie prefab resolved via " + source + ": GameObject='" +
                prefab.name + "', renderers=[" + DescribePinkieRenderers(prefab) +
                "]. Adult HandPaintedRat is not used for pinkies.");
        }

        private static GameObject LoadPinkiePrefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                // Use a typed lookup because the dedicated prefab and its
                // controller share the Resources stem. Resources.Load<T>
                // returns null for the wrong asset type; it does not perform
                // the unsafe PPtr cast that caused the original exception.
                GameObject prefab = Resources.Load<GameObject>(path);
                if (prefab == null)
                {
                    UnityEngine.Object untyped = Resources.Load(path);
                    if (untyped != null && !(untyped is GameObject))
                    {
                        Debug.LogWarning("[Rat Habitat] Pinkie Resources path '" + path + "' resolved to '" +
                            untyped.GetType().Name + "', not a GameObject prefab.");
                    }
                    else
                    {
                        Debug.LogWarning("[Rat Habitat] Pinkie prefab was not found at Resources path '" + path + "'.");
                    }
                    return null;
                }

                string validationError;
                if (!ValidateImportedPinkieVisual(prefab, out validationError))
                {
                    Debug.LogWarning("[Rat Habitat] Pinkie prefab at Resources path '" + path +
                        "' is GameObject '" + prefab.name + "' but failed validation: " + validationError);
                    return null;
                }

                return prefab;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Rat Habitat] Pinkie resource could not be loaded at '" + path +
                    "': " + exception.Message);
                return null;
            }
        }

        private static void ApplyPinkieSkin(GameObject visual)
        {
            if (visual == null) return;
            if (!pinkieSkinLookupResolved)
            {
                pinkieSkinLookupResolved = true;
                cachedPinkieSkin = Resources.Load<Material>(PinkieSkinResourcePath);
                if (cachedPinkieSkin == null)
                {
                    Debug.LogWarning("[Rat Habitat] Pinkie skin material was not found at Resources path '" +
                        PinkieSkinResourcePath + "'; preserving the imported pinkie material.");
                }
            }

            if (cachedPinkieSkin == null) return;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0) continue;
                for (int index = 0; index < materials.Length; index++) materials[index] = cachedPinkieSkin;
                renderer.sharedMaterials = materials;
            }
        }

        private static T LoadResource<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                return Resources.Load<T>(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Rat Habitat] Visual resource could not be loaded at '" + path + "': " + exception.Message);
                return null;
            }
        }

        private static void NormalizeImportedModel(GameObject model)
        {
            model.transform.localScale = Vector3.one * GameConfig.ImportedRatModelScale;
            Bounds bounds;
            if (!TryGetWorldBounds(model, out bounds) || bounds.size.y < 0.001f) return;

            float targetHeight = Mathf.Max(0.01f, GameConfig.ImportedRatTargetHeight);
            float scaleFactor = targetHeight / bounds.size.y;
            model.transform.localScale *= scaleFactor;

            if (TryGetWorldBounds(model, out bounds))
            {
                // Keep the imported rat standing on the same local ground
                // plane as the procedural fallback.
                float groundY = model.transform.parent == null ? 0f : model.transform.parent.position.y;
                model.transform.position += Vector3.up * (groundY - bounds.min.y);
            }
        }

        private static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = new Bounds(root.transform.position, Vector3.zero);
            bool found = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
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
            return found;
        }

        private static GameObject CreateProceduralFurredVisual(Transform parent, RatData rat)
        {
            var root = new GameObject(rat.name + " Procedural Furred Rat Visual");
            root.transform.SetParent(parent, false);
            var palette = PaletteFor(rat);

            AddPart(root.transform, PrimitiveType.Capsule, "Rounded Body", new Vector3(0f, 0.72f, 0f), new Vector3(0.84f, 0.66f, 1.5f), palette.body, new Vector3(90f, 0f, 0f));
            AddPart(root.transform, PrimitiveType.Sphere, "Rounded Head", new Vector3(0f, 0.82f, -1.05f), new Vector3(0.76f, 0.68f, 0.82f), palette.body, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Muzzle", new Vector3(0f, 0.76f, -1.63f), new Vector3(0.48f, 0.32f, 0.34f), new Color(0.88f, 0.69f, 0.58f), Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Left Ear", new Vector3(-0.43f, 1.22f, -1.08f), new Vector3(0.38f, 0.18f, 0.38f), palette.accent, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Right Ear", new Vector3(0.43f, 1.22f, -1.08f), new Vector3(0.38f, 0.18f, 0.38f), palette.accent, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Left Ear Inner", new Vector3(-0.43f, 1.22f, -1.25f), new Vector3(0.22f, 0.08f, 0.22f), new Color(1f, 0.67f, 0.68f), Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Right Ear Inner", new Vector3(0.43f, 1.22f, -1.25f), new Vector3(0.22f, 0.08f, 0.22f), new Color(1f, 0.67f, 0.68f), Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Left Eye White", new Vector3(-0.28f, 0.98f, -1.68f), new Vector3(0.2f, 0.2f, 0.16f), Color.white, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Right Eye White", new Vector3(0.28f, 0.98f, -1.68f), new Vector3(0.2f, 0.2f, 0.16f), Color.white, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Left Pupil", new Vector3(-0.28f, 0.98f, -1.79f), new Vector3(0.095f, 0.115f, 0.08f), new Color(0.08f, 0.06f, 0.10f), Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Right Pupil", new Vector3(0.28f, 0.98f, -1.79f), new Vector3(0.095f, 0.115f, 0.08f), new Color(0.08f, 0.06f, 0.10f), Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Left Eye Shine", new Vector3(-0.31f, 1.03f, -1.86f), new Vector3(0.035f, 0.035f, 0.025f), Color.white, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Right Eye Shine", new Vector3(0.25f, 1.03f, -1.86f), new Vector3(0.035f, 0.035f, 0.025f), Color.white, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Nose", new Vector3(0f, 0.78f, -1.86f), new Vector3(0.22f, 0.16f, 0.18f), new Color(1f, 0.48f, 0.52f), Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Left Paw", new Vector3(-0.32f, 0.37f, -0.7f), new Vector3(0.28f, 0.18f, 0.35f), palette.accent, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Sphere, "Right Paw", new Vector3(0.32f, 0.37f, -0.7f), new Vector3(0.28f, 0.18f, 0.35f), palette.accent, Vector3.zero);
            AddPart(root.transform, PrimitiveType.Capsule, "Tail", new Vector3(0f, 0.54f, 1.32f), new Vector3(0.10f, 0.9f, 0.10f), palette.accent, new Vector3(90f, 0f, 0f));
            AddWhiskers(root.transform);
            return root;
        }

        private struct Palette
        {
            public Color body;
            public Color accent;
        }

        private static Palette PaletteFor(RatData rat)
        {
            Color body = new Color(0.3f, 0.3f, 0.34f);
            Color accent = new Color(0.68f, 0.68f, 0.7f);
            if (rat != null && rat.phenotype != null && rat.phenotype.furRevealed)
            {
                body = ParseColor(rat.phenotype.coatColorHex, body);
                accent = ParseColor(rat.phenotype.accentHex, accent);
                body = Color.Lerp(body, Color.white, 0.08f);
                accent = Color.Lerp(accent, Color.white, 0.05f);
            }
            return new Palette { body = body, accent = accent };
        }

        private static Color ParseColor(string html, Color fallback)
        {
            Color parsed;
            return ColorUtility.TryParseHtmlString(html, out parsed) ? parsed : fallback;
        }

        private static bool IsFeatureMaterial(string rendererName, string materialName)
        {
            string name = rendererName + " " + materialName;
            return name.Contains("eye") || name.Contains("pupil") || name.Contains("iris") ||
                name.Contains("teeth") || name.Contains("tooth") || name.Contains("mouth") ||
                name.Contains("nose") || name.Contains("ear") || name.Contains("paw") ||
                name.Contains("foot") || name.Contains("whisker") || name.Contains("tail") ||
                name.Contains("skin") || name.Contains("detail");
        }

        private static bool IsAccentMaterial(string rendererName, string materialName)
        {
            string name = rendererName + " " + materialName;
            return name.Contains("ear") || name.Contains("tail") || name.Contains("paw") || name.Contains("foot") || name.Contains("skin");
        }

        private static bool IsAlbinoPinkFeature(string rendererName, string materialName)
        {
            string name = rendererName + " " + materialName;
            return name.Contains("eye") || name.Contains("iris") || name.Contains("nose") ||
                name.Contains("ear") || name.Contains("paw") || name.Contains("foot") ||
                name.Contains("skin") || name.Contains("tail");
        }

        private static int StableSpotSeed(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < value.Length; index++)
                {
                    hash = hash * 31 + value[index];
                }
                return hash & 0x7fffffff;
            }
        }

        private static void AddWhiskers(Transform parent)
        {
            Color whisker = new Color(0.96f, 0.91f, 0.80f);
            AddPart(parent, PrimitiveType.Cylinder, "Left Whisker Upper", new Vector3(-0.22f, 0.82f, -1.78f), new Vector3(0.018f, 0.38f, 0.018f), whisker, new Vector3(0f, 0f, 76f));
            AddPart(parent, PrimitiveType.Cylinder, "Left Whisker Lower", new Vector3(-0.22f, 0.72f, -1.78f), new Vector3(0.018f, 0.38f, 0.018f), whisker, new Vector3(0f, 0f, 100f));
            AddPart(parent, PrimitiveType.Cylinder, "Right Whisker Upper", new Vector3(0.22f, 0.82f, -1.78f), new Vector3(0.018f, 0.38f, 0.018f), whisker, new Vector3(0f, 0f, -76f));
            AddPart(parent, PrimitiveType.Cylinder, "Right Whisker Lower", new Vector3(0.22f, 0.72f, -1.78f), new Vector3(0.018f, 0.38f, 0.018f), whisker, new Vector3(0f, 0f, -100f));
        }

        private static GameObject AddPart(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color, Vector3 rotation)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.transform.localEulerAngles = rotation;
            MaterialFactory.Apply(part.GetComponent<Renderer>(), color);
            var collider = part.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            return part;
        }
    }

}
