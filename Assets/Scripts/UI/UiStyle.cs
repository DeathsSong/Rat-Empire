using UnityEngine;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// Small runtime-generated UI skin. It keeps the project asset-free while
    /// giving cards and buttons a soft rounded silhouette on portrait screens.
    /// </summary>
    public static class UiStyle
    {
        private static Sprite roundedSprite;

        public static Sprite RoundedSprite
        {
            get
            {
                if (roundedSprite == null) roundedSprite = CreateRoundedSprite();
                return roundedSprite;
            }
        }

        public static void ApplyRounded(Image image, Color color, bool shadow)
        {
            if (image == null) return;
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.fillCenter = true;
            image.raycastTarget = true;
            if (shadow && image.GetComponent<Shadow>() == null)
            {
                var drop = image.gameObject.AddComponent<Shadow>();
                drop.effectColor = new Color(0f, 0f, 0f, 0.28f);
                drop.effectDistance = new Vector2(0f, -3f);
                drop.useGraphicAlpha = true;
            }
        }

        private static Sprite CreateRoundedSprite()
        {
            const int size = 64;
            const float radius = 15f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.name = "Runtime Rounded UI Sprite";
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nearestX = Mathf.Clamp(x, radius, size - 1f - radius);
                    float nearestY = Mathf.Clamp(y, radius, size - 1f - radius);
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(nearestX, nearestY));
                    float alpha = Mathf.Clamp01(radius + 1f - distance);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.name = "Runtime Rounded UI Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
