using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Temporary, developer-only material isolation for diagnosing coat seams.
    /// It never changes RatData or the saved phenotype. The original material
    /// arrays are restored when the mode returns to Normal or the tools close.
    /// </summary>
    public enum RatVisualDiagnosticMode
    {
        Normal,
        PlainCoat,
        FlatUnlit,
        MaterialIds,
        Normals,
        Tangents,
    }

    public static class RatVisualDiagnostics
    {
        private const string ShaderResourcePath = "HandPaintedRat/RatVisualDiagnosticShader";
        private const string ShaderName = "Rat Habitat/Diagnostic Rat Visual";

        private static readonly Dictionary<Renderer, Material[]> originalMaterials =
            new Dictionary<Renderer, Material[]>();
        private static readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        private static Shader diagnosticShader;
        private static bool shaderResolved;
        private static Material diagnosticMaterial;
        private static RatVisualDiagnosticMode mode = RatVisualDiagnosticMode.Normal;

        public static RatVisualDiagnosticMode Mode
        {
            get { return mode; }
        }

        public static string ModeLabel
        {
            get
            {
                switch (mode)
                {
                    case RatVisualDiagnosticMode.PlainCoat: return "Plain coat (markings disabled)";
                    case RatVisualDiagnosticMode.FlatUnlit: return "Flat unlit";
                    case RatVisualDiagnosticMode.MaterialIds: return "Material / renderer IDs";
                    case RatVisualDiagnosticMode.Normals: return "World normals";
                    case RatVisualDiagnosticMode.Tangents: return "World tangents";
                    default: return "Normal coat + markings";
                }
            }
        }

        public static void SetMode(RatVisualDiagnosticMode requested)
        {
            if (mode == requested && requested != RatVisualDiagnosticMode.Normal)
            {
                ApplyToAllLiveVisuals();
                return;
            }

            RestoreOriginalMaterials();
            mode = requested;
            if (mode != RatVisualDiagnosticMode.Normal)
                ApplyToAllLiveVisuals();

            Debug.Log("[Rat Visual Diagnostics] mode=" + ModeLabel +
                " restoredOriginals=" + (mode == RatVisualDiagnosticMode.Normal) +
                " liveRenderers=" + originalMaterials.Count);
        }

        public static void Reset()
        {
            SetMode(RatVisualDiagnosticMode.Normal);
        }

        /// <summary>
        /// Called after a normal phenotype refresh so newly created stage
        /// visuals also participate while a diagnostic mode is active.
        /// </summary>
        public static void ApplyToVisual(GameObject visual)
        {
            if (mode == RatVisualDiagnosticMode.Normal || visual == null) return;
            ApplyToRenderers(visual.GetComponentsInChildren<Renderer>(true));
        }

        private static void ApplyToAllLiveVisuals()
        {
            var controllers = UnityEngine.Object.FindObjectsOfType<RatVisualController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                RatVisualController controller = controllers[i];
                if (controller == null) continue;
                GameObject visual;
                if (controller.TryGetCurrentVisual(out visual)) ApplyToVisual(visual);
            }
        }

        private static void ApplyToRenderers(Renderer[] renderers)
        {
            if (renderers == null) return;
            Material debug = ResolveDiagnosticMaterial();
            if (debug == null) return;

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || !renderer.enabled) continue;

                Material[] originals;
                if (!originalMaterials.TryGetValue(renderer, out originals))
                {
                    originals = renderer.sharedMaterials;
                    if (originals == null || originals.Length == 0) continue;
                    originalMaterials.Add(renderer, originals);
                }

                var debugMaterials = new Material[originals.Length];
                for (int materialIndex = 0; materialIndex < debugMaterials.Length; materialIndex++)
                {
                    debugMaterials[materialIndex] = debug;
                    propertyBlock.Clear();
                    propertyBlock.SetFloat("_Mode", ShaderMode(mode));
                    propertyBlock.SetColor("_Color", ColorFor(renderer, materialIndex, originals[materialIndex]));
                    renderer.SetPropertyBlock(propertyBlock, materialIndex);
                }
                renderer.sharedMaterials = debugMaterials;
            }
        }

        private static void RestoreOriginalMaterials()
        {
            foreach (KeyValuePair<Renderer, Material[]> entry in originalMaterials)
            {
                Renderer renderer = entry.Key;
                if (renderer == null) continue;
                renderer.sharedMaterials = entry.Value;
                for (int materialIndex = 0; materialIndex < entry.Value.Length; materialIndex++)
                    renderer.SetPropertyBlock(null, materialIndex);
            }
            originalMaterials.Clear();
        }

        private static Material ResolveDiagnosticMaterial()
        {
            if (diagnosticMaterial != null) return diagnosticMaterial;
            if (!shaderResolved)
            {
                shaderResolved = true;
                diagnosticShader = Resources.Load<Shader>(ShaderResourcePath);
                if (diagnosticShader == null) diagnosticShader = Shader.Find(ShaderName);
            }
            if (diagnosticShader == null)
            {
                Debug.LogError("[Rat Visual Diagnostics] Diagnostic shader could not be loaded.");
                return null;
            }
            diagnosticMaterial = new Material(diagnosticShader)
            {
                name = "Rat Visual Diagnostic Material"
            };
            return diagnosticMaterial;
        }

        private static float ShaderMode(RatVisualDiagnosticMode diagnosticMode)
        {
            switch (diagnosticMode)
            {
                case RatVisualDiagnosticMode.FlatUnlit: return 1f;
                case RatVisualDiagnosticMode.MaterialIds: return 2f;
                case RatVisualDiagnosticMode.Normals: return 3f;
                case RatVisualDiagnosticMode.Tangents: return 4f;
                default: return 0f;
            }
        }

        private static Color ColorFor(Renderer renderer, int materialIndex, Material original)
        {
            if (mode == RatVisualDiagnosticMode.MaterialIds)
            {
                int hash = StableHash((renderer == null ? string.Empty : renderer.name) +
                    ":" + materialIndex + ":" + (original == null ? string.Empty : original.name));
                float hue = (hash & 0x7fffffff) / (float)int.MaxValue;
                return Color.HSVToRGB(Mathf.Repeat(hue, 1f), 0.82f, 0.96f);
            }

            if (original != null)
            {
                if (original.HasProperty("_Color")) return original.GetColor("_Color");
                if (original.HasProperty("_BaseColor")) return original.GetColor("_BaseColor");
            }
            return new Color(0.48f, 0.48f, 0.48f, 1f);
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                return hash;
            }
        }
    }
}
