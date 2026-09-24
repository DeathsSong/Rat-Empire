using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Presentation-only preview rig for the imported rat animations. It is
    /// deliberately independent of the colony, RatPresenter, selection, and
    /// habitat behavior systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RatAnimationShowcase : MonoBehaviour
    {
        // Keep the showcase camera separate from RatPortraitPreview's layer.
        // Both systems can exist at the same time while the UI is rebuilt.
        private const int PreviewLayer = 30;
        private const string RuntimeAuditResourcePath = "HandPaintedRat/HandPaintedRatAnimationAudit";
        private const float TrackHalfLength = 1.65f;

        private sealed class AnimationDefinition
        {
            public readonly string label;
            public readonly string clipName;
            public readonly bool loop;
            public readonly bool travels;
            public readonly float travelCycleSeconds;

            public AnimationDefinition(string label, string clipName, bool loop, bool travels, float travelCycleSeconds)
            {
                this.label = label;
                this.clipName = clipName;
                this.loop = loop;
                this.travels = travels;
                this.travelCycleSeconds = travelCycleSeconds;
            }
        }

        private static readonly AnimationDefinition[] Definitions =
        {
            new AnimationDefinition("Walk", "HandPaintedRat_Walk", true, true, 3.2f),
            new AnimationDefinition("Run", "HandPaintedRat_Run", true, true, 1.8f),
            new AnimationDefinition("Idle Reaction / Sniff", "HandPaintedRat_IdleReaction", false, false, 0f),
            new AnimationDefinition("Sniffing / Interaction", "HandPaintedRat_Sniffing", false, false, 0f),
            new AnimationDefinition("Jump", "HandPaintedRat_Jump", false, false, 0f),
            new AnimationDefinition("Attack", "HandPaintedRat_Attack", false, false, 0f),
            new AnimationDefinition("Dying", "HandPaintedRat_Dying", false, false, 0f),
        };

        private RatVisualFactory factory;
        private RatAnimationClipAudit audit;
        private GameObject rig;
        private Transform visualRoot;
        private GameObject previewVisual;
        private Renderer bodyRenderer;
        private Animator animator;
        private Camera previewCamera;
        private RenderTexture previewTexture;
        private AnimationClip[] clips;
        private RatAnimationClipAudit.Entry[] auditEntries;
        private Bounds bodyBounds;
        private float baseOrthographicSize = 3f;
        private float trackClock;
        private float oneShotPauseRemaining;
        private float playbackSpeed = 1f;
        private int currentIndex;
        private bool playing;
        private bool configured;
        private bool importedPrefabAvailable;
        private string setupMessage = string.Empty;

        public Texture PreviewTexture { get { return previewTexture; } }
        public int ClipCount { get { return Definitions.Length; } }
        public int CurrentIndex { get { return currentIndex; } }
        public bool IsPlaying { get { return playing; } }
        public float PlaybackSpeed { get { return playbackSpeed; } }
        public string CurrentClipLabel { get { return Definitions[currentIndex].label; } }
        public string CurrentClipName { get { return Definitions[currentIndex].clipName; } }

        public void Configure(RatVisualFactory sourceFactory, RatData sample)
        {
            if (configured && factory == sourceFactory && previewVisual != null) return;

            DestroyPreviewRig();
            factory = sourceFactory;
            configured = true;
            importedPrefabAvailable = factory != null && factory.UsesImportedHandPaintedRat;
            audit = Resources.Load<RatAnimationClipAudit>(RuntimeAuditResourcePath);
            auditEntries = new RatAnimationClipAudit.Entry[Definitions.Length];
            setupMessage = string.Empty;

            if (!importedPrefabAvailable)
            {
                setupMessage = "Imported Hand Painted Rat prefab is unavailable.";
                return;
            }
            if (sample == null)
            {
                setupMessage = "No non-Pinkie sample rat is available.";
                return;
            }

            EnsurePreviewRig();
            previewVisual = factory.CreateStageVisual(visualRoot, sample);
            if (previewVisual == null)
            {
                setupMessage = "RatVisualFactory could not create the imported preview visual.";
                return;
            }

            SetLayerRecursively(previewVisual, PreviewLayer);
            RemoveGameplayComponents(previewVisual);
            bodyRenderer = FindMainBodyRenderer(previewVisual);
            animator = previewVisual.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                setupMessage = "Imported preview has no Animator.";
                previewVisual.SetActive(false);
                return;
            }

            animator.enabled = true;
            animator.applyRootMotion = false;
            LoadAnimationClips();
            if (bodyRenderer == null)
            {
                setupMessage = "Imported preview has no visible rat_mesh SkinnedMeshRenderer.";
                previewVisual.SetActive(false);
                return;
            }

            bodyBounds = bodyRenderer.bounds;
            ConfigurePreviewCamera(bodyBounds);
            currentIndex = FindFirstUsableClip();
            if (IsClipUsable(currentIndex)) PlayCurrent();
            else RenderPreview();
        }

        public string GetClipLabel(int index)
        {
            return IsValidIndex(index) ? Definitions[index].label : string.Empty;
        }

        public string GetClipName(int index)
        {
            return IsValidIndex(index) ? Definitions[index].clipName : string.Empty;
        }

        public bool IsClipUsable(int index)
        {
            if (!IsValidIndex(index) || !importedPrefabAvailable) return false;
            return clips != null && clips[index] != null &&
                   (audit == null || auditEntries[index] != null && auditEntries[index].animatedBodyBindings > 0);
        }

        public string GetClipStatus(int index)
        {
            if (!IsValidIndex(index)) return "Unavailable";
            if (!importedPrefabAvailable) return "Unavailable — imported prefab not assigned";
            AnimationClip clip = clips == null ? null : clips[index];
            if (clip == null) return "Unavailable — clip not in imported controller";

            RatAnimationClipAudit.Entry entry = auditEntries == null ? null : auditEntries[index];
            if (audit != null)
            {
                if (entry == null || !entry.available || entry.animatedBodyBindings <= 0)
                {
                    return "Unavailable — no verified animated bone curves";
                }
                return "Available — bone curves verified (" + entry.animatedBodyBindings + ")";
            }

            return "Available — bone curves unverified (run the animation rebuild menu)";
        }

        public string GetClipRowText(int index)
        {
            if (!IsValidIndex(index)) return "Unavailable";
            if (!importedPrefabAvailable) return GetClipLabel(index) + "\nUnavailable";
            AnimationClip clip = clips == null ? null : clips[index];
            if (clip == null) return GetClipLabel(index) + "\nUnavailable";

            RatAnimationClipAudit.Entry entry = auditEntries == null ? null : auditEntries[index];
            if (audit != null && (entry == null || !entry.available || entry.animatedBodyBindings <= 0))
            {
                return GetClipLabel(index) + "\nUnavailable • no bone curves";
            }
            return GetClipLabel(index) + (audit == null ? "\nAvailable • curves unverified" : "\nAvailable • curves verified");
        }

        public string SetupMessage { get { return setupMessage; } }

        public void SelectAnimation(int index)
        {
            if (!IsValidIndex(index)) return;
            currentIndex = index;
            if (IsClipUsable(index)) PlayCurrent();
            else
            {
                playing = false;
                if (animator != null) animator.speed = 0f;
                RenderPreview();
            }
        }

        public void TogglePlayPause()
        {
            if (!IsClipUsable(currentIndex)) return;
            playing = !playing;
            if (animator != null) animator.speed = playing ? playbackSpeed : 0f;
        }

        public void Restart()
        {
            if (IsClipUsable(currentIndex)) PlayCurrent();
        }

        public void Previous()
        {
            SelectAnimation((currentIndex + Definitions.Length - 1) % Definitions.Length);
        }

        public void Next()
        {
            SelectAnimation((currentIndex + 1) % Definitions.Length);
        }

        public void SetPlaybackSpeed(float speed)
        {
            playbackSpeed = Mathf.Clamp(speed, 0.05f, 4f);
            if (animator != null) animator.speed = playing ? playbackSpeed : 0f;
        }

        private void Update()
        {
            if (!Application.isPlaying || animator == null || previewCamera == null || previewVisual == null) return;

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f) return;

            if (playing)
            {
                AnimationDefinition definition = Definitions[currentIndex];
                if (definition.travels)
                {
                    trackClock += deltaTime * playbackSpeed;
                    float trackPosition = Mathf.PingPong(trackClock / definition.travelCycleSeconds, 1f);
                    previewVisual.transform.localPosition = new Vector3(
                        Mathf.Lerp(-TrackHalfLength, TrackHalfLength, trackPosition), 0f, 0f);
                }

                if (!definition.loop)
                {
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                    if (state.IsName(definition.clipName) && state.normalizedTime >= 0.99f)
                    {
                        animator.speed = 0f;
                        if (oneShotPauseRemaining <= 0f)
                        {
                            oneShotPauseRemaining = 0.35f;
                        }
                        else
                        {
                            oneShotPauseRemaining -= deltaTime;
                            if (oneShotPauseRemaining <= 0f) PlayCurrent();
                        }
                    }
                }
            }

            RenderPreview();
        }

        private void PlayCurrent()
        {
            if (animator == null || !IsClipUsable(currentIndex)) return;
            AnimationDefinition definition = Definitions[currentIndex];
            animator.enabled = true;
            animator.speed = playbackSpeed;
            animator.Play(definition.clipName, 0, 0f);
            animator.Update(0f);
            playing = true;
            trackClock = definition.travels ? 0f : trackClock;
            oneShotPauseRemaining = 0f;
            if (!definition.travels) previewVisual.transform.localPosition = Vector3.zero;
            RenderPreview();
        }

        private int FindFirstUsableClip()
        {
            for (int i = 0; i < Definitions.Length; i++)
            {
                if (IsClipUsable(i)) return i;
            }
            return 0;
        }

        private void LoadAnimationClips()
        {
            clips = new AnimationClip[Definitions.Length];
            if (animator == null || animator.runtimeAnimatorController == null) return;

            AnimationClip[] controllerClips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < Definitions.Length; i++)
            {
                string expectedName = Definitions[i].clipName;
                for (int clipIndex = 0; clipIndex < controllerClips.Length; clipIndex++)
                {
                    AnimationClip candidate = controllerClips[clipIndex];
                    if (candidate != null && string.Equals(candidate.name, expectedName, StringComparison.Ordinal))
                    {
                        clips[i] = candidate;
                        break;
                    }
                }

                auditEntries[i] = audit == null ? null : audit.Find(expectedName);
            }
        }

        private void EnsurePreviewRig()
        {
            rig = new GameObject("Rat Animation Showcase Preview Rig");
            rig.transform.SetParent(transform, false);
            rig.hideFlags = HideFlags.HideAndDontSave;
            SetLayerRecursively(rig, PreviewLayer);

            var cameraObject = new GameObject("Rat Animation Showcase Camera");
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.layer = PreviewLayer;
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.055f, 0.085f, 0.09f, 1f);
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 100f;
            previewCamera.useOcclusionCulling = false;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;

            var lightObject = new GameObject("Rat Animation Showcase Light");
            lightObject.transform.SetParent(rig.transform, false);
            lightObject.layer = PreviewLayer;
            lightObject.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            Light previewLight = lightObject.AddComponent<Light>();
            previewLight.type = LightType.Directional;
            previewLight.intensity = 1.1f;
            previewLight.color = new Color(1f, 0.93f, 0.82f);
            previewLight.cullingMask = 1 << PreviewLayer;

            var visualObject = new GameObject("Rat Animation Showcase Visual");
            visualObject.transform.SetParent(rig.transform, false);
            visualObject.layer = PreviewLayer;
            visualRoot = visualObject.transform;

            previewTexture = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            previewTexture.name = "Rat Animation Showcase Preview";
            previewTexture.hideFlags = HideFlags.HideAndDontSave;
            previewTexture.Create();
        }

        private void ConfigurePreviewCamera(Bounds bounds)
        {
            if (previewCamera == null || previewTexture == null) return;

            Vector3 target = bounds.center;
            float distance = Mathf.Max(8f, bounds.size.y * 1.8f);
            Vector3 cameraOffset = new Vector3(-distance * 0.55f, distance * 0.42f, -distance * 0.78f);
            previewCamera.transform.position = target + cameraOffset;
            previewCamera.transform.LookAt(target);
            previewCamera.orthographic = true;
            previewCamera.targetTexture = previewTexture;
            previewCamera.aspect = (float)previewTexture.width / Mathf.Max(1, previewTexture.height);

            float horizontalExtent = 0f;
            float verticalExtent = 0f;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                        Vector3 cameraSpace = previewCamera.transform.InverseTransformPoint(corner);
                        horizontalExtent = Mathf.Max(horizontalExtent, Mathf.Abs(cameraSpace.x));
                        verticalExtent = Mathf.Max(verticalExtent, Mathf.Abs(cameraSpace.y));
                    }
                }
            }

            float requiredHalfHeight = Mathf.Max(verticalExtent, horizontalExtent / previewCamera.aspect);
            float trackHalfHeight = TrackHalfLength / previewCamera.aspect;
            baseOrthographicSize = Mathf.Max(0.1f, Mathf.Max(requiredHalfHeight, trackHalfHeight) * 1.22f);
            previewCamera.orthographicSize = baseOrthographicSize;
            previewCamera.targetTexture = previewTexture;
            RenderPreview();
        }

        private void RenderPreview()
        {
            if (previewCamera == null || previewTexture == null || previewVisual == null || !previewVisual.activeInHierarchy) return;
            previewCamera.targetTexture = previewTexture;
            previewCamera.Render();
        }

        private static SkinnedMeshRenderer FindMainBodyRenderer(GameObject root)
        {
            SkinnedMeshRenderer fallback = null;
            float largestArea = 0f;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (string.Equals(renderer.gameObject.name, "rat_mesh", StringComparison.OrdinalIgnoreCase)) return renderer;
                float area = renderer.bounds.size.sqrMagnitude;
                if (fallback == null || area > largestArea)
                {
                    fallback = renderer;
                    largestArea = area;
                }
            }
            return fallback;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static void RemoveGameplayComponents(GameObject root)
        {
            if (root == null) return;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null) Destroy(collider);
            }
            foreach (SelectableEntity selectable in root.GetComponentsInChildren<SelectableEntity>(true))
            {
                if (selectable != null) Destroy(selectable);
            }
            foreach (RatHabitatBehavior behavior in root.GetComponentsInChildren<RatHabitatBehavior>(true))
            {
                if (behavior != null) Destroy(behavior);
            }
        }

        private void DestroyPreviewRig()
        {
            if (previewTexture != null)
            {
                previewTexture.Release();
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
            if (rig != null) DestroyImmediate(rig);
            rig = null;
            previewVisual = null;
            visualRoot = null;
            animator = null;
            bodyRenderer = null;
            previewCamera = null;
            clips = null;
            auditEntries = null;
        }

        private void OnDestroy()
        {
            DestroyPreviewRig();
        }

        private static bool IsValidIndex(int index)
        {
            return index >= 0 && index < Definitions.Length;
        }
    }
}
