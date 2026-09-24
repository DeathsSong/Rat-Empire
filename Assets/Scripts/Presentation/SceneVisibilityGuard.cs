using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace RatHabitat
{
    /// <summary>
    /// A small startup guard for the first vertical slice. It makes camera,
    /// clipping, clear color, and ambient lighting explicit and leaves a
    /// visible diagnostic/fallback if runtime world construction stops early.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SceneVisibilityGuard : MonoBehaviour
    {
        private Camera targetCamera;
        private string diagnostic = "3D scene booting...";
        private bool fallbackBuilt;
        private bool checkedScene;
        private bool builtInCameraComponentsAudited;
        private float checkTimer;
        private GUIStyle style;

        private void Awake()
        {
            Debug.Log("[Rat Habitat] SceneVisibilityGuard.Awake started.");
            try
            {
                targetCamera = GetComponent<Camera>();
                if (targetCamera == null) targetCamera = Camera.main;
                // EventSystem processes registered raycasters during its own
                // update. Remove every built-in world raycaster before that
                // happens; manual InteractionManager is the only world
                // selection path in this slice.
                DisableBuiltInCameraComponents(targetCamera);
                ValidateCamera();
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.34f, 0.40f, 0.35f);
                RenderSettings.reflectionIntensity = 0.35f;
                RenderSettings.fog = false;
                QualitySettings.shadowDistance = 50f;
                EnsureBootstrapIsPresentAndActive();
                diagnostic = "3D scene ready — camera test active.";
                Debug.Log("[Rat Habitat] SceneVisibilityGuard.Awake completed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                diagnostic = "3D scene error — check Console.";
            }
        }

        private void Update()
        {
            // A stale scene object or another startup component can add a
            // camera-side raycaster/flare component after Awake. Audit once
            // before the first normal frame update so EventSystem and the
            // camera cannot invoke those built-in paths. This is an audit
            // only; it never performs a physics query.
            if (!builtInCameraComponentsAudited)
            {
                DisableBuiltInCameraComponents(targetCamera);
                builtInCameraComponentsAudited = true;
            }

            // Keep this as a normal frame check instead of a coroutine. If any
            // other startup component throws, the diagnostic still gets a
            // deterministic completion path on the next rendered frame.
            if (checkedScene || fallbackBuilt) return;
            checkTimer += Time.unscaledDeltaTime;
            if (checkTimer < 0.25f) return;
            checkedScene = true;

            try
            {
                int ratCount = CountSceneRats();
                if (ratCount >= 2 && GameObject.Find("Habitat Presentation") != null)
                {
                    diagnostic = "3D scene ready — " + ratCount + " rats loaded.";
                    Debug.Log("[Rat Habitat] SceneVisibilityGuard confirmed the habitat and " + ratCount + " rats before render.");
                }
                else
                {
                    BuildFallbackHabitat();
                    diagnostic = "3D diagnostic fallback visible — check Console for the startup error.";
                    Debug.LogError("[Rat Habitat] Startup did not create the habitat and two rat presenters. The visible fallback was created.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                try
                {
                    BuildFallbackHabitat();
                }
                catch (Exception fallbackException)
                {
                    Debug.LogException(fallbackException);
                }
                diagnostic = "3D scene error — check Console.";
            }
        }

        private static int CountSceneRats()
        {
            int count = 0;
            var entities = FindObjectsOfType<SelectableEntity>();
            foreach (var entity in entities)
            {
                if (entity != null && entity.kind == SelectableKind.Rat) count++;
            }
            return count;
        }

        private static void EnsureBootstrapIsPresentAndActive()
        {
            var bootstrap = FindObjectOfType<GameBootstrap>();
            if (bootstrap != null)
            {
                if (!bootstrap.gameObject.activeSelf) bootstrap.gameObject.SetActive(true);
                return;
            }

            var bootstrapObject = GameObject.Find("Game Bootstrap");
            if (bootstrapObject == null) bootstrapObject = new GameObject("Game Bootstrap");
            bootstrapObject.SetActive(true);
            bootstrapObject.AddComponent<GameBootstrap>();
            Debug.LogWarning("[Rat Habitat] Game Bootstrap was missing from the scene and was installed by the visibility guard.");
        }

        private void ValidateCamera()
        {
            if (targetCamera == null) return;
            DisableBuiltInCameraComponents(targetCamera);
            targetCamera.enabled = true;
            targetCamera.gameObject.tag = "MainCamera";
            targetCamera.orthographic = true;
            targetCamera.orthographicSize = GameConfig.CameraDefaultOrthographicSize;
            targetCamera.nearClipPlane = 0.1f;
            targetCamera.farClipPlane = 100f;
            targetCamera.cullingMask = ~0;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = new Color(0.025f, 0.075f, 0.11f);
            targetCamera.rect = new Rect(0f, 0f, 1f, 1f);
            targetCamera.targetTexture = null;
            targetCamera.useOcclusionCulling = false;
            targetCamera.transform.position = new Vector3(0f, 14f, -15f);
            targetCamera.transform.LookAt(new Vector3(0f, 0.1f, 1f));
        }

        public static void DisableBuiltInPhysicsRaycasters(Camera camera)
        {
            // Keep the old public method name for existing callers, but make
            // the audit cover the camera FlareLayer too. In Unity 2022.3 a
            // stale FlareLayer can emit the IsNormalized(dir, ...) assertion
            // before any user pointer input is received.
            int removed = DisableBuiltInCameraComponents(camera);
            if (removed > 0)
            {
                Debug.Log("[Rat Habitat] Disabled " + removed + " built-in camera raycaster/flare component(s); manual Main Camera selection is active.");
            }
        }

        private static int DisableBuiltInCameraComponents(Camera camera)
        {
            int removed = DisableAllBuiltInPhysicsRaycasters();
            removed += DisableAllFlareLayers();
            return removed;
        }

        private static int DisableAllBuiltInPhysicsRaycasters()
        {
            int removed = 0;
            // Include inactive scene objects and cameras, not just Main
            // Camera. A duplicate PhysicsRaycaster anywhere in the scene is
            // enough for EventSystem.RaycastAll to invoke Unity's internal
            // Physics.Raycast path before a user click.
            foreach (var raycaster in UnityEngine.Object.FindObjectsOfType<PhysicsRaycaster>(true))
            {
                if (raycaster == null) continue;
                raycaster.enabled = false;
                UnityEngine.Object.Destroy(raycaster);
                removed++;
            }
            foreach (var raycaster in UnityEngine.Object.FindObjectsOfType<Physics2DRaycaster>(true))
            {
                if (raycaster == null) continue;
                raycaster.enabled = false;
                UnityEngine.Object.Destroy(raycaster);
                removed++;
            }
            return removed;
        }

        private static int DisableAllFlareLayers()
        {
            int removed = 0;
            // FlareLayer is a legacy camera component. It is not part of the
            // game's presentation and must not be allowed to run alongside
            // the manual selection path.
            foreach (var flareLayer in UnityEngine.Object.FindObjectsOfType<FlareLayer>(true))
            {
                if (flareLayer == null) continue;
                flareLayer.enabled = false;
                UnityEngine.Object.Destroy(flareLayer);
                removed++;
            }
            return removed;
        }

        private void BuildFallbackHabitat()
        {
            if (fallbackBuilt) return;
            fallbackBuilt = true;
            var root = new GameObject("Scene Visibility Diagnostic Fallback");
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Fallback Floor", new Vector3(0f, -0.25f, 1f), new Vector3(12f, 0.5f, 16f), new Color(0.18f, 0.52f, 0.30f));
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Fallback Back Wall", new Vector3(0f, 1f, 8.7f), new Vector3(12f, 2.5f, 0.35f), new Color(0.64f, 0.36f, 0.20f));
            CreatePrimitive(root.transform, PrimitiveType.Sphere, "Fallback Mabel", new Vector3(-2.1f, 0.9f, -0.4f), new Vector3(1.6f, 1.0f, 2.0f), new Color(0.9f, 0.12f, 0.18f));
            CreatePrimitive(root.transform, PrimitiveType.Sphere, "Fallback Otto", new Vector3(1.3f, 0.9f, 0.4f), new Vector3(1.6f, 1.0f, 2.0f), new Color(0.14f, 0.35f, 0.95f));
            CreatePrimitive(root.transform, PrimitiveType.Cylinder, "Fallback Food", new Vector3(-3.7f, 0.45f, -2.8f), new Vector3(1.1f, 0.45f, 1.1f), new Color(1f, 0.65f, 0.08f));
            CreatePrimitive(root.transform, PrimitiveType.Cylinder, "Fallback Water", new Vector3(3.6f, 1.1f, -2.5f), new Vector3(0.7f, 1.5f, 0.7f), new Color(0.05f, 0.65f, 1f));
            CreatePrimitive(root.transform, PrimitiveType.Cylinder, "Fallback Nest", new Vector3(-3.4f, 0.4f, 4.2f), new Vector3(2f, 0.4f, 1.5f), new Color(0.9f, 0.55f, 0.08f));
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Fallback Hide", new Vector3(3.3f, 0.8f, 4.3f), new Vector3(2.4f, 1.2f, 2f), new Color(0.75f, 0.35f, 0.08f));
        }

        private static GameObject CreatePrimitive(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            MaterialFactory.Apply(item.GetComponent<Renderer>(), color, true);
            return item;
        }

        private void OnGUI()
        {
            // Startup diagnostics remain in the Unity Console. Drawing a
            // legacy OnGUI box here would sit on top of the new compact nav
            // and could obscure the habitat in the normal player view.
        }
    }
}
