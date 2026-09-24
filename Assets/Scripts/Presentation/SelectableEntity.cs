using UnityEngine;

namespace RatHabitat
{
    public class SelectableEntity : MonoBehaviour
    {
        public SelectableKind kind;
        public string entityId;
        public string displayName;

        public bool HasCollider
        {
            get { return GetComponentInChildren<Collider>(true) != null; }
        }

        public void Configure(SelectableKind newKind, string id, string label)
        {
            kind = newKind;
            entityId = id;
            displayName = label;
        }

        public Collider EnsureCollider(Vector3 center, Vector3 size)
        {
            var collider = GetComponent<Collider>();
            if (collider != null) return collider;
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return box;
        }
    }

    /// <summary>
    /// Marker for the small, stable colliders that represent a rat's visible
    /// interaction surface. Imported mesh/helper colliders are never valid rat
    /// selection colliders just because they are children of the rat root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RatSelectionCollider : MonoBehaviour
    {
    }
}
