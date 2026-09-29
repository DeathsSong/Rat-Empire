using UnityEngine;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// Small, allocation-free dice icon drawn directly into the Unity UI mesh.
    /// It stays crisp at the phone reference scale and does not need a texture
    /// or a per-button material.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DiceIconGraphic : Graphic
    {
        [SerializeField] private float lineThickness = 2.1f;
        [SerializeField] private float pipRadius = 1.8f;

        protected override void OnEnable()
        {
            base.OnEnable();
            // These icons are created after the canvas has already been
            // enabled. Explicitly dirty the graphic here so Unity does not
            // retain the empty mesh produced while the runtime RectTransform
            // still had its default zero-sized rect.
            SetAllDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            float side = Mathf.Min(rect.width, rect.height);
            if (side <= 1f)
            {
                // Layout can briefly report a zero rect while a naming row is
                // being rebuilt. The next dimensions callback will rebuild
                // the visible mesh once the button receives its final size.
                return;
            }

            float half = side * 0.23f;
            float stroke = Mathf.Max(1.2f, Mathf.Min(lineThickness, side * 0.14f));
            float pip = Mathf.Max(1.3f, Mathf.Min(pipRadius, side * 0.12f));

            DrawDie(vertexHelper, new Vector2(-side * 0.10f, -side * 0.06f), half,
                -0.18f, stroke, pip, new Vector2(-0.30f, -0.30f), new Vector2(0.30f, 0.30f));
            DrawDie(vertexHelper, new Vector2(side * 0.12f, side * 0.10f), half,
                0.16f, stroke, pip, new Vector2(-0.30f, 0.30f), new Vector2(0.30f, -0.30f));
        }

        private void DrawDie(VertexHelper vertexHelper, Vector2 center, float half, float angle,
            float stroke, float pip, Vector2 firstPip, Vector2 secondPip)
        {
            Vector2[] corners =
            {
                Rotate(new Vector2(-half, -half), angle) + center,
                Rotate(new Vector2(half, -half), angle) + center,
                Rotate(new Vector2(half, half), angle) + center,
                Rotate(new Vector2(-half, half), angle) + center
            };
            for (int index = 0; index < corners.Length; index++)
                AddSegment(vertexHelper, corners[index], corners[(index + 1) % corners.Length], stroke);

            AddCircle(vertexHelper, center + Rotate(new Vector2(firstPip.x * half, firstPip.y * half), angle), pip);
            AddCircle(vertexHelper, center + Rotate(new Vector2(secondPip.x * half, secondPip.y * half), angle), pip);
        }

        private void AddSegment(VertexHelper vertexHelper, Vector2 start, Vector2 end, float thickness)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude < 0.0001f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
            AddQuad(vertexHelper, start + normal, start - normal, end - normal, end + normal);
        }

        private void AddCircle(VertexHelper vertexHelper, Vector2 center, float radius)
        {
            const int segments = 8;
            for (int index = 0; index < segments; index++)
            {
                float startAngle = (index / (float)segments) * Mathf.PI * 2f;
                float endAngle = ((index + 1) / (float)segments) * Mathf.PI * 2f;
                AddTriangle(vertexHelper, center,
                    center + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * radius,
                    center + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * radius);
            }
        }

        private void AddQuad(VertexHelper vertexHelper, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = vertexHelper.currentVertCount;
            vertexHelper.AddVert(MakeVertex(a));
            vertexHelper.AddVert(MakeVertex(b));
            vertexHelper.AddVert(MakeVertex(c));
            vertexHelper.AddVert(MakeVertex(d));
            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start + 2, start + 3, start);
        }

        private void AddTriangle(VertexHelper vertexHelper, Vector2 a, Vector2 b, Vector2 c)
        {
            int start = vertexHelper.currentVertCount;
            vertexHelper.AddVert(MakeVertex(a));
            vertexHelper.AddVert(MakeVertex(b));
            vertexHelper.AddVert(MakeVertex(c));
            vertexHelper.AddTriangle(start, start + 1, start + 2);
        }

        private UIVertex MakeVertex(Vector2 position)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = position;
            return vertex;
        }

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(value.x * cosine - value.y * sine, value.x * sine + value.y * cosine);
        }
    }
}
