using System.Collections;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Owns only the live visual children for one rat. Stage transitions are
    /// presentation animations; the persisted RatData stays in GameBootstrap.
    /// </summary>
    [DisallowMultipleComponent]
    public class RatVisualController : MonoBehaviour
    {
        private RatVisualFactory factory;
        private Transform visualRoot;
        private GameObject currentVisual;
        private GameObject transitioningOutVisual;
        private Coroutine transitionRoutine;
        private RatStage currentStage;
        private bool hasVisual;

        public RatStage CurrentStage
        {
            get { return currentStage; }
        }

        /// <summary>
        /// Returns the visual that represents the current stage. During a
        /// stage transition the outgoing visual remains briefly active, so
        /// callers that need the live rat (such as the profile portrait) must
        /// use this reference rather than selecting an arbitrary active child.
        /// </summary>
        public bool TryGetCurrentVisual(out GameObject visual)
        {
            visual = currentVisual;
            return visual != null && visual.activeInHierarchy;
        }

        public bool TryGetSelectionBounds(out Bounds localBounds)
        {
            localBounds = new Bounds(transform.position, Vector3.zero);
            if (currentVisual == null || !currentVisual.activeInHierarchy) return false;

            Renderer selectedRenderer = null;
            ImportedRatVisualMarker importedMarker = currentVisual.GetComponent<ImportedRatVisualMarker>();
            if (importedMarker != null)
            {
                float largestBounds = 0f;
                foreach (var renderer in currentVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (string.Equals(renderer.gameObject.name, "rat_mesh", System.StringComparison.OrdinalIgnoreCase))
                    {
                        selectedRenderer = renderer;
                        break;
                    }

                    float area = renderer.bounds.size.sqrMagnitude;
                    if (selectedRenderer == null || area > largestBounds)
                    {
                        selectedRenderer = renderer;
                        largestBounds = area;
                    }
                }
            }
            else
            {
                float largestBounds = 0f;
                foreach (var renderer in currentVisual.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    float area = renderer.bounds.size.sqrMagnitude;
                    if (selectedRenderer == null || area > largestBounds)
                    {
                        selectedRenderer = renderer;
                        largestBounds = area;
                    }
                }
            }

            if (selectedRenderer == null || selectedRenderer.bounds.size.sqrMagnitude <= 0.0001f) return false;

            Bounds worldBounds = selectedRenderer.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            bool found = false;
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 localCorner = transform.InverseTransformPoint(corner);
                        if (!found)
                        {
                            localBounds = new Bounds(localCorner, Vector3.zero);
                            found = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localCorner);
                        }
                    }
                }
            }
            return found && localBounds.size.sqrMagnitude > 0.0001f;
        }

        public void Configure(RatVisualFactory visualFactory)
        {
            factory = visualFactory;
            EnsureVisualRoot();
        }

        public void Apply(RatData rat, bool animateStageChange)
        {
            if (rat == null) return;
            if (factory == null) factory = GetComponentInParent<RatVisualFactory>();
            if (factory == null)
            {
                factory = gameObject.GetComponent<RatVisualFactory>();
                if (factory == null) factory = gameObject.AddComponent<RatVisualFactory>();
            }
            EnsureVisualRoot();

            if (!hasVisual)
            {
                ReplaceImmediate(rat);
                return;
            }

            if (currentStage != rat.stage)
            {
                if (animateStageChange && Application.isPlaying)
                {
                    BeginStageTransition(rat, currentStage);
                }
                else
                {
                    ReplaceImmediate(rat);
                }
                return;
            }

            factory.ApplyPhenotype(currentVisual, rat);
        }

        private void EnsureVisualRoot()
        {
            if (visualRoot != null) return;
            var existing = transform.Find("Rat Visual Stage");
            if (existing != null)
            {
                visualRoot = existing;
                return;
            }
            var root = new GameObject("Rat Visual Stage");
            root.transform.SetParent(transform, false);
            visualRoot = root.transform;
        }

        private void ReplaceImmediate(RatData rat)
        {
            StopTransitionAndClearChildren();
            currentVisual = factory.CreateStageVisual(visualRoot, rat);
            if (currentVisual == null) return;
            Vector3 baseScale = currentVisual.transform.localScale;
            SetStageScale(currentVisual, baseScale, ScaleForStage(rat.stage));
            currentStage = rat.stage;
            hasVisual = true;
        }

        private void BeginStageTransition(RatData rat, RatStage fromStage)
        {
            StopTransitionOnly();
            if (transitioningOutVisual != null)
            {
                UnityEngine.Object.Destroy(transitioningOutVisual);
                transitioningOutVisual = null;
            }

            var previous = currentVisual;
            var next = factory.CreateStageVisual(visualRoot, rat);
            if (next == null)
            {
                ReplaceImmediate(rat);
                return;
            }

            float fromScale = ScaleForStage(fromStage);
            float toScale = ScaleForStage(rat.stage);
            Vector3 previousBaseScale = previous == null
                ? Vector3.one
                : previous.transform.localScale / Mathf.Max(0.0001f, fromScale);
            Vector3 nextBaseScale = next.transform.localScale;
            SetStageScale(next, nextBaseScale, fromScale);
            currentVisual = next;
            currentStage = rat.stage;
            hasVisual = true;
            transitioningOutVisual = previous;
            transitionRoutine = StartCoroutine(AnimateStageChange(previous, next, previousBaseScale, nextBaseScale, fromScale, toScale, DurationFor(fromStage, rat.stage)));
        }

        private IEnumerator AnimateStageChange(GameObject previous, GameObject next, Vector3 previousBaseScale, Vector3 nextBaseScale, float fromScale, float toScale, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                float nextStageScale = Mathf.Lerp(fromScale, toScale, eased);
                float previousStageScale = Mathf.Lerp(fromScale, 0.02f, eased);
                SetStageScale(next, nextBaseScale, nextStageScale);
                SetStageScale(previous, previousBaseScale, previousStageScale);
                yield return null;
            }

            SetStageScale(next, nextBaseScale, toScale);
            if (previous != null) UnityEngine.Object.Destroy(previous);
            transitioningOutVisual = null;
            transitionRoutine = null;
        }

        private static void SetStageScale(GameObject visual, Vector3 baseScale, float stageScale)
        {
            if (visual == null) return;
            visual.transform.localScale = baseScale * stageScale;
        }

        private void StopTransitionOnly()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }
        }

        private void StopTransitionAndClearChildren()
        {
            StopTransitionOnly();
            for (int i = visualRoot == null ? -1 : visualRoot.childCount - 1; i >= 0; i--)
            {
                var child = visualRoot.GetChild(i);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }
            currentVisual = null;
            transitioningOutVisual = null;
            hasVisual = false;
        }

        private static float ScaleForStage(RatStage stage)
        {
            switch (stage)
            {
                case RatStage.Pinkie: return GameConfig.PinkieVisualScale;
                case RatStage.YoungRat: return GameConfig.YoungVisualScale;
                default: return GameConfig.AdultVisualScale;
            }
        }

        private static float DurationFor(RatStage fromStage, RatStage toStage)
        {
            if (fromStage == RatStage.Pinkie && toStage == RatStage.YoungRat) return GameConfig.PinkieToYoungVisualTransitionSeconds;
            if (fromStage == RatStage.YoungRat && toStage == RatStage.Adult) return GameConfig.YoungToAdultVisualTransitionSeconds;
            return 0.75f;
        }
    }
}
