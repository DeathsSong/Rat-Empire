using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Runtime marker for the imported visual root. Keeping this type in its
    /// own script gives Unity a stable MonoScript asset to serialize in FBX
    /// prefab variants.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ImportedRatVisualMarker : MonoBehaviour
    {
        [HideInInspector] public Vector3 normalizedLocalPosition;
        [HideInInspector] public bool hasNormalizedLocalPosition;
    }
}
