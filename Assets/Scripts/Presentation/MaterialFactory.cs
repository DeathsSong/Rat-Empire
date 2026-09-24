using UnityEngine;
using UnityEngine.Rendering;

namespace RatHabitat
{
    public static class MaterialFactory
    {
        private const string TransparentBarrierResourcePath = "RatHabitat/TransparentBarrier";
        private static Shader transparentBarrierShader;
        private static bool transparentBarrierShaderResolved;

        public static Material Create(Color color)
        {
            return Create(color, false);
        }

        public static Material CreateUnlit(Color color)
        {
            return Create(color, true);
        }

        private static Material Create(Color color, bool unlit)
        {
            Shader shader = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find(unlit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            }
            if (shader == null) shader = Shader.Find(unlit ? "Unlit/Color" : "Standard");
            if (shader == null) shader = Shader.Find(unlit ? "Standard" : "Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("Rat Habitat could not find a compatible 3D material shader.");
                return null;
            }

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.22f);
            return material;
        }

        public static void Apply(Renderer renderer, Color color)
        {
            Apply(renderer, color, false);
        }

        public static void Apply(Renderer renderer, Color color, bool unlit)
        {
            Apply(renderer, color, unlit, null);
        }

        public static void Apply(Renderer renderer, Color color, bool unlit, Texture texture)
        {
            if (renderer == null) return;
            var material = Create(color, unlit);
            if (material == null) return;
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            }
            renderer.material = material;
        }

        /// <summary>
        /// Creates a restrained see-through material for enclosure fronts.
        /// The renderer keeps its normal collider, while the material avoids
        /// depth-writing an opaque slab over the rats behind it.
        /// </summary>
        public static void ApplyTransparent(Renderer renderer, Color color, float alpha)
        {
            if (renderer == null) return;
            Color transparentColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
            var shader = ResolveTransparentBarrierShader();
            var material = shader == null ? Create(transparentColor, false) : new Material(shader);
            if (material == null) return;

            material.name = "See-Through Enclosure Barrier";
            if (material.HasProperty("_Color")) material.SetColor("_Color", transparentColor);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", transparentColor);

            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            renderer.material = material;
        }

        private static Shader ResolveTransparentBarrierShader()
        {
            if (!transparentBarrierShaderResolved)
            {
                transparentBarrierShaderResolved = true;
                transparentBarrierShader = Resources.Load<Shader>(TransparentBarrierResourcePath);
                if (transparentBarrierShader == null)
                    transparentBarrierShader = Shader.Find("Rat Habitat/Transparent Barrier");
            }
            return transparentBarrierShader;
        }
    }
}
