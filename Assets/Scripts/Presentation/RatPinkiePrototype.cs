using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Runtime pinkie visual using the generated hairless pink skin texture.
    /// The gameplay RatData remains the source of truth and the visual is
    /// replaced when the rat becomes a Young Rat.
    /// </summary>
    [DisallowMultipleComponent]
    public class RatPinkiePrototype : MonoBehaviour
    {
        [SerializeField] private bool buildOnAwake = true;
        private bool built;
        private Texture2D skinTexture;
        private Transform leftHindFoot;
        private Transform rightHindFoot;
        private Vector3 leftHindFootBase;
        private Vector3 rightHindFootBase;

        private void Awake()
        {
            if (buildOnAwake) Build();
        }

        public void Build()
        {
            if (built) return;
            skinTexture = Resources.Load<Texture2D>("HandPaintedRat/HandPaintedRat_PinkieSkin");
            // This also lets the editor-generated prefab contain its primitive
            // children without duplicating them when instantiated at runtime.
            if (transform.Find("Pinkie Body") != null)
            {
                ApplySkinToExistingParts();
                CacheAnimationParts();
                ApplyBackPose();
                built = true;
                return;
            }
            built = true;

            // The texture supplies the pale center, mauve creases, and subtle
            // vascular variation. These tints only separate soft extremities.
            Color skin = Color.white;
            Color softSkin = new Color(1f, 0.9f, 0.92f);
            Color eye = new Color(0.17f, 0.04f, 0.07f);

            AddPart(PrimitiveType.Sphere, "Pinkie Body", new Vector3(0f, 0.48f, 0f), new Vector3(1.28f, 0.68f, 1.5f), new Vector3(90f, 0f, 0f), skin, true);
            AddPart(PrimitiveType.Sphere, "Pinkie Head", new Vector3(0f, 0.58f, -0.78f), new Vector3(0.82f, 0.62f, 0.84f), Vector3.zero, skin, true);
            AddPart(PrimitiveType.Sphere, "Pinkie Muzzle", new Vector3(0f, 0.48f, -1.17f), new Vector3(0.36f, 0.24f, 0.28f), Vector3.zero, softSkin, true);
            AddPart(PrimitiveType.Sphere, "Pinkie Nose", new Vector3(0f, 0.49f, -1.36f), new Vector3(0.16f, 0.12f, 0.12f), Vector3.zero, softSkin, true);

            AddPart(PrimitiveType.Sphere, "Tiny Left Ear", new Vector3(-0.42f, 0.96f, -0.72f), new Vector3(0.27f, 0.11f, 0.28f), Vector3.zero, softSkin, true);
            AddPart(PrimitiveType.Sphere, "Tiny Right Ear", new Vector3(0.42f, 0.96f, -0.72f), new Vector3(0.27f, 0.11f, 0.28f), Vector3.zero, softSkin, true);

            AddPart(PrimitiveType.Sphere, "Left Eye", new Vector3(-0.25f, 0.75f, -1.17f), new Vector3(0.11f, 0.11f, 0.08f), Vector3.zero, eye, false);
            AddPart(PrimitiveType.Sphere, "Right Eye", new Vector3(0.25f, 0.75f, -1.17f), new Vector3(0.11f, 0.11f, 0.08f), Vector3.zero, eye, false);

            AddPart(PrimitiveType.Sphere, "Left Front Foot", new Vector3(-0.3f, 0.18f, -0.68f), new Vector3(0.25f, 0.13f, 0.34f), Vector3.zero, softSkin, true);
            AddPart(PrimitiveType.Sphere, "Right Front Foot", new Vector3(0.3f, 0.18f, -0.68f), new Vector3(0.25f, 0.13f, 0.34f), Vector3.zero, softSkin, true);
            AddPart(PrimitiveType.Sphere, "Left Hind Foot", new Vector3(-0.44f, 0.2f, 0.62f), new Vector3(0.28f, 0.14f, 0.42f), Vector3.zero, softSkin, true);
            AddPart(PrimitiveType.Sphere, "Right Hind Foot", new Vector3(0.44f, 0.2f, 0.62f), new Vector3(0.28f, 0.14f, 0.42f), Vector3.zero, softSkin, true);

            AddPart(PrimitiveType.Capsule, "Tiny Tail", new Vector3(0f, 0.38f, 1.2f), new Vector3(0.07f, 0.7f, 0.07f), new Vector3(90f, 0f, 0f), softSkin, true);
            CacheAnimationParts();
            ApplyBackPose();
        }

        private void ApplySkinToExistingParts()
        {
            Color skin = Color.white;
            Color softSkin = new Color(1f, 0.9f, 0.92f);
            Color eye = new Color(0.17f, 0.04f, 0.07f);

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                string partName = renderer.gameObject.name;
                bool isEye = partName.IndexOf("Eye", System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSoftSkin = partName.IndexOf("Muzzle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.IndexOf("Nose", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.IndexOf("Ear", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.IndexOf("Foot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.IndexOf("Tail", System.StringComparison.OrdinalIgnoreCase) >= 0;

                MaterialFactory.Apply(
                    renderer,
                    isEye ? eye : (isSoftSkin ? softSkin : skin),
                    true,
                    isEye ? null : skinTexture);
            }
        }

        private void Update()
        {
            if (!built || leftHindFoot == null || rightHindFoot == null) return;
            float phase = Time.time * 4.6f;
            Kick(leftHindFoot, leftHindFootBase, Mathf.Sin(phase));
            Kick(rightHindFoot, rightHindFootBase, Mathf.Sin(phase + Mathf.PI));
        }

        private void CacheAnimationParts()
        {
            leftHindFoot = transform.Find("Left Hind Foot");
            rightHindFoot = transform.Find("Right Hind Foot");
            if (leftHindFoot != null) leftHindFootBase = leftHindFoot.localPosition;
            if (rightHindFoot != null) rightHindFootBase = rightHindFoot.localPosition;
        }

        private void ApplyBackPose()
        {
            // The newborn lies belly-up in the nest; the animated feet then
            // make small alternating kicks above its body.
            transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static void Kick(Transform foot, Vector3 basePosition, float amount)
        {
            foot.localPosition = basePosition + new Vector3(0f, amount * 0.045f, amount * 0.10f);
            foot.localRotation = Quaternion.Euler(0f, amount * 10f, amount * 18f);
        }

        private GameObject AddPart(PrimitiveType type, string partName, Vector3 position, Vector3 scale, Vector3 rotation, Color color, bool texturedSkin)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.transform.SetParent(transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.transform.localEulerAngles = rotation;
            MaterialFactory.Apply(part.GetComponent<Renderer>(), color, true, texturedSkin ? skinTexture : null);

            // The selectable rat root owns one stable interaction collider. Do
            // not leave temporary primitive colliders on the pinkie children.
            var collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }
            return part;
        }
    }
}
