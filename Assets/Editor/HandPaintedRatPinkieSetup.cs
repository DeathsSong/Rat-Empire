#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RatHabitat.Editor
{
    /// <summary>
    /// Imports the Blender-authored pinkie model, assigns its pink skin, and
    /// saves a dedicated animated prefab under Resources. The authored
    /// animation is played in place with root motion disabled; newborn roots
    /// remain nest-bound and do not receive habitat movement behavior.
    /// </summary>
    public static class HandPaintedRatPinkieSetup
    {
        private const string ModelPath = "Assets/Art/HandPaintedRat/HandPaintedRat_Pinkie.fbx";
        private const string MaterialPath = "Assets/Resources/HandPaintedRat_PinkieSkin.mat";
        private const string PrefabPath = "Assets/Resources/HandPaintedRat_Pinkie.prefab";
        private const string SourceMaterialPath = "Assets/Prefabs/HandPaintedRat/HandPaintedRat_TextureBase.mat";
        private const string SkinTexturePath = "Assets/Art/HandPaintedRat/HandPaintedRat_PinkieSkin.png";
        private const string ControllerPath = "Assets/Resources/HandPaintedRat_Pinkie.controller";
        private const string ClipName = "HandPaintedRat_PinkieKick";
        private const string TakeName = "Armature|HandPaintedRat_PinkieKick";
        // The supplied pinkie.blend action is Blender frames 0-200. The
        // exported FBX exposes the same action as Unity source frames 1-201.
        private const int FirstFrame = 1;
        private const int LastFrame = 201;
        private const string AutoBuildSessionKey = "RatHabitat.HandPaintedRatPinkieSetup.AutoBuildComplete.v9";

        [InitializeOnLoadMethod]
        private static void QueueBuildAfterEditorReload()
        {
            if (SessionState.GetBool(AutoBuildSessionKey, false)) return;
            EditorApplication.delayCall += BuildOnceAfterEditorReload;
        }

        private static void BuildOnceAfterEditorReload()
        {
            if (SessionState.GetBool(AutoBuildSessionKey, false)) return;
            SessionState.SetBool(AutoBuildSessionKey, true);
            Build();
        }

        [MenuItem("Rat Empire/Visuals/Build Hand Painted Rat Pinkie")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void BuildFromBatchMode()
        {
            Build();
        }

        private static void Build()
        {
            string modelDiskPath = System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName,
                ModelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(modelDiskPath))
            {
                Debug.LogError("[Rat Habitat] Pinkie FBX is missing at " + ModelPath + ". Run the Blender pinkie build first.");
                return;
            }

            ConfigureImporter();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            AnimationClip kickClip = LoadPinkieClip();
            if (kickClip == null)
            {
                Debug.LogError("[Rat Habitat] Pinkie FBX imported, but the authored " + ClipName +
                    " clip could not be resolved. The prefab was not rebuilt so no placeholder movement is created.");
                return;
            }

            AnimatorController controller = RebuildController(kickClip);
            Material skin = CreatePinkieSkin();
            CreatePinkiePrefab(skin, controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Rat Habitat] Built imported pinkie prefab at " + PrefabPath +
                " with authored clip " + ClipName + " (frames " + FirstFrame + "-" + LastFrame +
                ") from the supplied pinkie animation source. Root motion remains disabled.");
        }

        private static void ConfigureImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Could not access ModelImporter at " + ModelPath + ".");

            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.optimizeGameObjects = false;
            // The FBX contains authoring cameras/lights. They are not part of
            // the pinkie visual and must never become runtime cameras when a
            // newborn is instantiated into the habitat.
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.clipAnimations = new[]
            {
                new ModelImporterClipAnimation
                {
                    name = ClipName,
                    takeName = TakeName,
                    firstFrame = FirstFrame,
                    lastFrame = LastFrame,
                    loop = true,
                    loopTime = true,
                    cycleOffset = 0f,
                    keepOriginalOrientation = true,
                    keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true,
                    heightFromFeet = false,
                    mirror = false
                }
            };
            importer.SaveAndReimport();
        }

        private static AnimationClip LoadPinkieClip()
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .Where(clip => clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(clip => clip.name == ClipName);

            if (clip == null) return null;

            // A named sub-asset can still be stale or empty after a model
            // reimport. Do not build a prefab/controller that looks valid in
            // the Project window but cannot deform the pinkie at runtime.
            EditorCurveBinding[] transformCurves = AnimationUtility.GetCurveBindings(clip)
                .Where(binding => binding.type == typeof(Transform))
                .ToArray();
            int objectReferenceCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip).Length;
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            int matchedTransformCurves = 0;
            if (model != null)
            {
                var transformPaths = model.GetComponentsInChildren<Transform>(true)
                    .Select(transform => AnimationUtility.CalculateTransformPath(transform, model.transform))
                    .ToHashSet(StringComparer.Ordinal);
                matchedTransformCurves = transformCurves.Count(binding => transformPaths.Contains(binding.path));
            }
            Debug.Log("[Rat Habitat] Pinkie clip audit: name=" + clip.name +
                " length=" + clip.length.ToString("0.###") + "s" +
                " frameRate=" + clip.frameRate.ToString("0.###") +
                " transformCurves=" + transformCurves.Length +
                " matchedTransformCurves=" + matchedTransformCurves +
                " objectReferenceCurves=" + objectReferenceCurves);
            if (clip.length <= 0f || transformCurves.Length == 0 || matchedTransformCurves == 0)
            {
                Debug.LogError("[Rat Habitat] Pinkie clip '" + ClipName +
                    "' is empty, has no Transform animation curves, or its curves do not match the imported armature hierarchy. The prefab was not rebuilt.");
                return null;
            }

            return clip;
        }

        private static AnimatorController RebuildController(AnimationClip kickClip)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            AnimatorControllerLayer[] layers = controller.layers;
            if (layers == null || layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                layers = controller.layers;
            }

            AnimatorStateMachine stateMachine = layers[0].stateMachine;
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state != null) stateMachine.RemoveState(child.state);
            }

            AnimatorState state = stateMachine.AddState(ClipName);
            state.motion = kickClip;
            state.speed = 1f;
            stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[Rat Habitat] Rebuilt pinkie Animator Controller: state=" +
                ClipName + " motion=" + kickClip.name + " length=" +
                kickClip.length.ToString("0.###") + "s.");
            return controller;
        }

        private static Material CreatePinkieSkin()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader);
                material.name = "HandPaintedRat_PinkieSkin";
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
            Texture2D pinkieTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SkinTexturePath);
            if (pinkieTexture != null)
            {
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", pinkieTexture);
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", pinkieTexture);
            }
            else if (source != null && source.HasProperty("_MainTex") && material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", source.GetTexture("_MainTex"));
            }

            // The generated texture carries the mauve creases and subtle
            // vascular variation; leave the material tint nearly neutral so
            // those details remain visible in Unity.
            Color skin = new Color(1f, 0.96f, 0.97f, 1f);
            if (material.HasProperty("_Color")) material.SetColor("_Color", skin);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", skin);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.32f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.32f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreatePinkiePrefab(Material skin, AnimatorController controller)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("Could not load imported pinkie model at " + ModelPath + ".");

            GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (instance == null) throw new InvalidOperationException("Imported pinkie FBX did not instantiate as a GameObject.");
            try
            {
                instance.name = "HandPaintedRat_Pinkie";
                instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                if (instance.GetComponent<ImportedRatVisualMarker>() == null)
                {
                    instance.AddComponent<ImportedRatVisualMarker>();
                }
                if (instance.GetComponent<HandPaintedRatPinkieMarker>() == null)
                {
                    instance.AddComponent<HandPaintedRatPinkieMarker>();
                }

                // Remove stale components if the FBX or an earlier generated
                // prefab supplied them. Newborns are nest-bound and must not
                // receive code-driven movement or child hitboxes; their
                // authored in-place animation is configured below.
                foreach (RatPinkiePrototype prototype in instance.GetComponentsInChildren<RatPinkiePrototype>(true))
                {
                    if (prototype != null) UnityEngine.Object.DestroyImmediate(prototype);
                }
                if (controller == null)
                {
                    throw new InvalidOperationException("Pinkie Animator Controller is missing at " + ControllerPath + ".");
                }
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                foreach (Animator childAnimator in instance.GetComponentsInChildren<Animator>(true))
                {
                    if (childAnimator != null && childAnimator != animator)
                    {
                        UnityEngine.Object.DestroyImmediate(childAnimator);
                    }
                }
                animator.runtimeAnimatorController = controller;
                Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                    .OfType<Avatar>()
                    .FirstOrDefault();
                if (avatar != null)
                {
                    animator.avatar = avatar;
                    Debug.Log("[Rat Habitat] Assigned imported pinkie Avatar '" + avatar.name + "' to the generated Animator.");
                }
                else
                {
                    Debug.LogWarning("[Rat Habitat] No imported Avatar sub-asset was found for the pinkie FBX; keeping the Generic Animator avatar empty.");
                }
                animator.applyRootMotion = false;
                animator.enabled = true;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                }
                RemoveImportedAuxiliaryComponents(instance);

                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    Material[] materials = renderer.sharedMaterials;
                    if (materials == null || materials.Length == 0) continue;
                    for (int i = 0; i < materials.Length; i++) materials[i] = skin;
                    renderer.sharedMaterials = materials;
                }

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void RemoveImportedAuxiliaryComponents(GameObject instance)
        {
            if (instance == null) return;
            foreach (Camera camera in instance.GetComponentsInChildren<Camera>(true))
            {
                if (camera != null) UnityEngine.Object.DestroyImmediate(camera);
            }
            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            {
                if (light != null) UnityEngine.Object.DestroyImmediate(light);
            }
            foreach (AudioListener listener in instance.GetComponentsInChildren<AudioListener>(true))
            {
                if (listener != null) UnityEngine.Object.DestroyImmediate(listener);
            }
        }
    }
}
#endif
