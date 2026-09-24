using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Identifies the imported newborn model. This remains distinct from the
    /// adult visual marker so adult phenotype coat logic cannot treat a pinkie
    /// prefab as an adult rat visual.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandPaintedRatPinkieMarker : MonoBehaviour
    {
        // Runtime-only state. The prefab's Animator starts in its default
        // state, so this flag distinguishes that untouched state from a
        // phase that was deliberately applied for this rat instance.
        [System.NonSerialized] public bool animationPhaseApplied;
        [System.NonSerialized] public float animationStartPhase;
    }
}
