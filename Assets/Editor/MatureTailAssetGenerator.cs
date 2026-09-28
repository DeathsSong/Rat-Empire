using System.IO;
using UnityEditor;
using UnityEngine;

namespace RatHabitat.Editor
{
    /// <summary>
    /// Creates the shared mature-tail resource used by the imported young and
    /// adult mesh. The texture is deliberately an authored project asset so
    /// tail pixels do not depend on a runtime white fallback or on the body
    /// texture's incompatible UV layout.
    /// </summary>
    public static class MatureTailAssetGenerator
    {
        private const string TexturePath = "Assets/Resources/HandPaintedRat/HandPaintedRat_MatureTailSkin.png";
        private const string MaterialPath = "Assets/Resources/HandPaintedRat/HandPaintedRat_MatureTail.mat";
        private const string ShaderName = "Rat Habitat/Mature Rat Tail Skin";

        public static void Generate()
        {
            const int width = 256;
            const int height = 128;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float rings = Mathf.Sin(v * Mathf.PI * 2f * 44f +
                        Mathf.Sin(u * Mathf.PI * 2f) * 0.75f) * 0.5f + 0.5f;
                    float broad = Mathf.Sin(v * Mathf.PI * 2f * 4f + u * 5.3f) * 0.5f + 0.5f;
                    float pores = Mathf.Sin(v * Mathf.PI * 2f * 103f + u * 41f) * 0.5f + 0.5f;
                    float value = Mathf.Clamp01(0.44f + rings * 0.30f + broad * 0.14f + pores * 0.05f);
                    pixels[y * width + x] = new Color(value, value * 0.93f, value * 0.89f, 1f);
                }
            }

            string directory = Path.GetDirectoryName(TexturePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(TexturePath, EncodePng(pixels, width, height));
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            var shader = Shader.Find(ShaderName);
            if (texture == null || shader == null)
                throw new System.InvalidOperationException("Mature tail texture or shader could not be imported.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.name = "HandPaintedRat_MatureTailSkin";
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Rat Habitat] Generated dedicated mature tail assets: " + TexturePath + " and " + MaterialPath);
        }

        private static byte[] EncodePng(Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }
    }
}
