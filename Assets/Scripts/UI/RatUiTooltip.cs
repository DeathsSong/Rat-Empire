using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Human-readable label for generated controls. Unity's WebGL canvas does
    /// not expose native tooltip metadata by default, so the label is kept on
    /// the control and mirrored in its object name for accessibility tooling.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RatUiTooltip : MonoBehaviour
    {
        [SerializeField] private string label;

        public string Label
        {
            get { return label; }
            set { label = value ?? string.Empty; }
        }
    }
}
