using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Runtime marker used by InteractionManager to distinguish a selectable
    /// gameplay target from room geometry. The marker is deliberately kept
    /// separate from the saved rat/object data and from its visual prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractableObject : MonoBehaviour
    {
        [SerializeField] private SelectableEntity selectableEntity;

        public void Configure(SelectableEntity entity)
        {
            selectableEntity = entity;
        }

        public SelectableEntity ResolveSelection()
        {
            if (selectableEntity != null && selectableEntity.isActiveAndEnabled)
            {
                return selectableEntity;
            }

            return GetComponentInParent<SelectableEntity>();
        }
    }
}
