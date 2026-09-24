using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Compatibility shell for scenes or prefabs saved before the single
    /// InteractionManager path was introduced. It never polls input or calls
    /// physics; InteractionManager disables and removes any stale instance.
    /// </summary>
    public class SelectionController : MonoBehaviour
    {
        private void Awake()
        {
            enabled = false;
        }

        public void Configure(Camera camera, System.Action<SelectableEntity> onSelected, System.Action onEscape, System.Action<string> onDiagnostic = null)
        {
            enabled = false;
        }
    }
}
