using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// A deliberately isolated input/camera/physics/UI smoke test. It does
    /// not reference GameBootstrap, rats, habitat data, or InteractionManager.
    /// Open InteractionSmokeTest.unity to test the Unity foundations alone.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionSmokeTest : MonoBehaviour
    {
        private const float TouchMoveThreshold = 24f;
        private const float MinimumRayDirectionMagnitude = 0.0001f;

        private Camera activeCamera;
        private GameObject testCube;
        private Renderer cubeRenderer;
        private Material cubeMaterial;
        private Color cubeIdleColor = new Color(1f, 0.08f, 0.88f, 1f);
        private Color cubeSuccessColor = new Color(0.08f, 1f, 0.26f, 1f);

        private Canvas canvas;
        private RectTransform uiButtonRect;
        private Text titleText;
        private Text stateText;
        private Text hitText;
        private Text pointerText;
        private Text helpText;
        private Text startupText;

        private bool initialized;
        private string startupState = "InteractionSmokeTest: STARTING";
        private string state = "NO INPUT RECEIVED";
        private string hitState = "Raycast result: NONE";
        private string pointerState = "Pointer screen: —";
        private string helpState = "Click or tap the bright cube. Click the UI button first to test EventSystem input.";
        private Vector2 lastPointerPosition;
        private bool hasPointerPosition;

        private int lastProcessedFrame = -1;
        private Vector2 lastProcessedPosition;
        private Vector2 touchStartPosition;
        private int touchFingerId = -1;
        private bool touchStartedOnUi;

        private void Awake()
        {
            try
            {
                EnsureActiveCamera();
                EnsureSingleEventSystem();
                CreateTestCube();
                BuildDiagnosticUi();
                initialized = true;
                startupState = "InteractionSmokeTest: READY";
                UpdateDiagnosticUi();
                Debug.Log("[Rat Habitat] InteractionSmokeTest READY: camera, cube collider, Canvas, Button, and EventSystem are independent of the game systems.");
            }
            catch (Exception exception)
            {
                initialized = false;
                startupState = "InteractionSmokeTest ERROR: " + exception.Message;
                Debug.LogException(exception);
            }
        }

        private void Update()
        {
            if (!initialized) return;
            HandleTouchInput();
            HandleMouseInput();
        }

        private void EnsureActiveCamera()
        {
            activeCamera = Camera.main;
            if (activeCamera == null)
            {
                var cameras = FindObjectsOfType<Camera>(true);
                foreach (var candidate in cameras)
                {
                    if (candidate != null && candidate.gameObject.activeInHierarchy)
                    {
                        activeCamera = candidate;
                        break;
                    }
                }
            }

            if (activeCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                activeCamera = cameraObject.AddComponent<Camera>();
            }

            activeCamera.gameObject.SetActive(true);
            activeCamera.enabled = true;
            activeCamera.gameObject.tag = "MainCamera";
            activeCamera.orthographic = true;
            activeCamera.orthographicSize = 6f;
            activeCamera.nearClipPlane = 0.1f;
            activeCamera.farClipPlane = 100f;
            activeCamera.cullingMask = ~0;
            activeCamera.clearFlags = CameraClearFlags.SolidColor;
            activeCamera.backgroundColor = new Color(0.025f, 0.07f, 0.12f, 1f);
            activeCamera.rect = new Rect(0f, 0f, 1f, 1f);
            activeCamera.targetTexture = null;
            activeCamera.transform.position = new Vector3(0f, 0f, -10f);
            activeCamera.transform.rotation = Quaternion.identity;
        }

        private static void EnsureSingleEventSystem()
        {
            var systems = FindObjectsOfType<EventSystem>(true);
            EventSystem selected = EventSystem.current;
            if (selected == null || !selected.gameObject.activeInHierarchy)
            {
                selected = null;
                foreach (var candidate in systems)
                {
                    if (candidate != null && candidate.gameObject.activeInHierarchy)
                    {
                        selected = candidate;
                        break;
                    }
                }
            }

            if (selected == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                selected = eventSystemObject.AddComponent<EventSystem>();
            }

            foreach (var candidate in systems)
            {
                if (candidate == null || candidate == selected) continue;
                candidate.enabled = false;
                Destroy(candidate.gameObject);
            }

            selected.gameObject.SetActive(true);
            selected.enabled = true;

            var inputModule = selected.GetComponent<StandaloneInputModule>();
            if (inputModule == null) inputModule = selected.gameObject.AddComponent<StandaloneInputModule>();
            inputModule.enabled = true;

            var modules = selected.GetComponents<BaseInputModule>();
            foreach (var module in modules)
            {
                if (module == null || module == inputModule) continue;
                module.enabled = false;
                Destroy(module);
            }

            Debug.Log("[Rat Habitat] InteractionSmokeTest EventSystem ready: exactly one EventSystem and one StandaloneInputModule.");
        }

        private void CreateTestCube()
        {
            testCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            testCube.name = "Interaction Smoke Cube";
            testCube.transform.position = Vector3.zero;
            testCube.transform.localScale = new Vector3(4.4f, 3.8f, 1.8f);

            cubeRenderer = testCube.GetComponent<Renderer>();
            cubeMaterial = CreateUnlitMaterial(cubeIdleColor);
            cubeRenderer.material = cubeMaterial;

            var boxCollider = testCube.GetComponent<BoxCollider>();
            if (boxCollider == null) boxCollider = testCube.AddComponent<BoxCollider>();
            boxCollider.enabled = true;
            boxCollider.isTrigger = false;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("No compatible cube material shader was found.");

            var material = new Material(shader);
            ApplyMaterialColor(material, color);
            return material;
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private void BuildDiagnosticUi()
        {
            var canvasObject = new GameObject("Interaction Smoke Canvas");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            canvasObject.AddComponent<GraphicRaycaster>();

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(540f, 960f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasObject.GetComponent<RectTransform>();

            titleText = CreateText(root, "3D INPUT SMOKE TEST", 21, Color.white, TextAnchor.MiddleCenter);
            SetRect(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(430f, 34f));
            titleText.fontStyle = FontStyle.Bold;

            helpText = CreateText(root, helpState, 13, new Color(0.78f, 0.9f, 0.88f, 1f), TextAnchor.MiddleCenter);
            SetRect(helpText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(500f, 44f));

            var buttonObject = new GameObject("Smoke Test UI Button");
            buttonObject.transform.SetParent(root, false);
            uiButtonRect = buttonObject.AddComponent<RectTransform>();
            SetRect(uiButtonRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(340f, 72f));
            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.12f, 0.62f, 0.45f, 1f);
            buttonImage.raycastTarget = true;
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(OnSmokeUiButtonPressed);

            var buttonLabel = CreateText(buttonObject.transform, "UI BUTTON TEST", 18, Color.white, TextAnchor.MiddleCenter);
            SetRect(buttonLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            buttonLabel.rectTransform.offsetMin = new Vector2(8f, 4f);
            buttonLabel.rectTransform.offsetMax = new Vector2(-8f, -4f);

            var panelObject = new GameObject("Smoke Diagnostic Panel");
            panelObject.transform.SetParent(root, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            SetRect(panelRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(500f, 186f));
            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.02f, 0.08f, 0.1f, 0.9f);
            panelImage.raycastTarget = false;

            stateText = CreateText(panelObject.transform, state, 20, new Color(1f, 0.84f, 0.3f, 1f), TextAnchor.MiddleCenter);
            SetRect(stateText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(470f, 38f));
            stateText.fontStyle = FontStyle.Bold;

            hitText = CreateText(panelObject.transform, hitState, 14, Color.white, TextAnchor.MiddleCenter);
            SetRect(hitText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(470f, 32f));

            pointerText = CreateText(panelObject.transform, pointerState, 14, new Color(0.62f, 0.94f, 1f, 1f), TextAnchor.MiddleCenter);
            SetRect(pointerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(470f, 30f));

            startupText = CreateText(panelObject.transform, startupState, 12, new Color(0.64f, 1f, 0.72f, 1f), TextAnchor.MiddleCenter);
            SetRect(startupText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(470f, 24f));
        }

        private Text CreateText(Transform parent, string value, int fontSize, Color color, TextAnchor alignment)
        {
            var textObject = new GameObject("Smoke Text");
            textObject.transform.SetParent(parent, false);
            var textRect = textObject.AddComponent<RectTransform>();
            var text = textObject.AddComponent<Text>();
            Font font = null;
            try
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Rat Habitat] InteractionSmokeTest could not load LegacyRuntime.ttf: " + exception.Message);
            }
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = false;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private void HandleMouseInput()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            Vector2 pointerPosition = Input.mousePosition;
            RecordPointer(pointerPosition);
            if (IsPointerOverUi(pointerPosition, -1)) return;
            ProcessWorldPointer(pointerPosition);
        }

        private void HandleTouchInput()
        {
            if (Input.touchCount == 0) return;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    touchFingerId = touch.fingerId;
                    touchStartPosition = touch.position;
                    touchStartedOnUi = IsPointerOverUi(touch.position, touch.fingerId);
                }
                else if (touch.phase == TouchPhase.Ended && touch.fingerId == touchFingerId)
                {
                    RecordPointer(touch.position);
                    bool moved = Vector2.Distance(touchStartPosition, touch.position) > TouchMoveThreshold;
                    bool overUi = IsPointerOverUi(touch.position, touch.fingerId);
                    if (!touchStartedOnUi && !overUi && !moved) ProcessWorldPointer(touch.position);
                    touchFingerId = -1;
                    touchStartedOnUi = false;
                }
                else if (touch.phase == TouchPhase.Canceled && touch.fingerId == touchFingerId)
                {
                    RecordPointer(touch.position);
                    touchFingerId = -1;
                    touchStartedOnUi = false;
                }
            }
        }

        private bool IsPointerOverUi(Vector2 pointerPosition, int pointerId)
        {
            bool insideTestButton = uiButtonRect != null && RectTransformUtility.RectangleContainsScreenPoint(uiButtonRect, pointerPosition, null);
            if (insideTestButton) return true;
            if (EventSystem.current == null) return false;
            return pointerId < 0
                ? EventSystem.current.IsPointerOverGameObject()
                : EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        private void RecordPointer(Vector2 pointerPosition)
        {
            hasPointerPosition = true;
            lastPointerPosition = pointerPosition;
            pointerState = "Pointer screen: (" + pointerPosition.x.ToString("0") + ", " + pointerPosition.y.ToString("0") + ")";
            UpdateDiagnosticUi();
        }

        private void ProcessWorldPointer(Vector2 pointerPosition)
        {
            if (lastProcessedFrame == Time.frameCount && Vector2.Distance(lastProcessedPosition, pointerPosition) < 1f) return;
            lastProcessedFrame = Time.frameCount;
            lastProcessedPosition = pointerPosition;

            state = "INPUT RECEIVED — NO COLLIDER HIT";
            hitState = "Raycast result: NONE";
            helpState = "Input received. Creating a ray from Camera.main...";
            ApplyCubeColor(cubeIdleColor);

            Camera mainCamera = Camera.main;
            if (mainCamera == null || !mainCamera.enabled || !mainCamera.gameObject.activeInHierarchy || !mainCamera.CompareTag("MainCamera"))
            {
                helpState = "Camera error: active MainCamera is unavailable.";
                UpdateDiagnosticUi();
                return;
            }

            Ray ray;
            string rayError;
            if (!TryCreateValidatedRay(mainCamera, pointerPosition, out ray, out rayError))
            {
                helpState = "Ray error: " + rayError;
                UpdateDiagnosticUi();
                return;
            }

            Physics.SyncTransforms();
            RaycastHit hit;
            bool hitSomething;
            try
            {
                // No custom layer mask is supplied. This intentionally uses
                // Unity's unrestricted default Physics.Raycast overload for
                // the isolated smoke test.
                hitSomething = Physics.Raycast(ray, out hit, Mathf.Infinity);
            }
            catch (Exception exception)
            {
                helpState = "Physics raycast error: " + exception.Message;
                UpdateDiagnosticUi();
                Debug.LogWarning("[Rat Habitat] InteractionSmokeTest skipped a rejected raycast: " + exception.Message);
                return;
            }

            if (!hitSomething || hit.collider == null)
            {
                state = "INPUT RECEIVED — NO COLLIDER HIT";
                hitState = "Raycast result: NONE";
                helpState = "Input received, but Physics.Raycast found no collider.";
                UpdateDiagnosticUi();
                return;
            }

            string hitName = hit.collider.gameObject.name;
            hitState = "INPUT RECEIVED — HIT: " + hitName;
            if (hit.collider.gameObject == testCube || hit.collider.transform.IsChildOf(testCube.transform))
            {
                state = "INTERACTION SUCCESS";
                helpState = "Cube collider hit. The isolated 3D input path works.";
                ApplyCubeColor(cubeSuccessColor);
            }
            else
            {
                state = "INPUT RECEIVED — HIT: " + hitName;
                helpState = "A collider was hit, but it was not the smoke-test cube.";
            }
            UpdateDiagnosticUi();
        }

        private bool TryCreateValidatedRay(Camera camera, Vector2 pointerPosition, out Ray ray, out string error)
        {
            ray = default(Ray);
            error = "invalid pointer or camera ray";
            if (!IsFinite(pointerPosition.x) || !IsFinite(pointerPosition.y)) return false;
            if (Screen.width <= 0 || Screen.height <= 0) return false;
            if (pointerPosition.x < 0f || pointerPosition.y < 0f || pointerPosition.x > Screen.width || pointerPosition.y > Screen.height)
            {
                error = "pointer is outside the Game view";
                return false;
            }

            Ray generatedRay;
            try
            {
                generatedRay = camera.ScreenPointToRay(pointerPosition);
            }
            catch (Exception exception)
            {
                error = "ScreenPointToRay failed: " + exception.Message;
                return false;
            }

            Vector3 origin = generatedRay.origin;
            Vector3 direction = generatedRay.direction;
            if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(origin.z))
            {
                error = "ray origin is invalid";
                return false;
            }
            if (!IsFinite(direction.x) || !IsFinite(direction.y) || !IsFinite(direction.z))
            {
                error = "ray direction is invalid";
                return false;
            }

            float squaredMagnitude = direction.sqrMagnitude;
            if (!IsFinite(squaredMagnitude) || squaredMagnitude < MinimumRayDirectionMagnitude * MinimumRayDirectionMagnitude)
            {
                error = "ray direction is zero";
                return false;
            }

            float magnitude = Mathf.Sqrt(squaredMagnitude);
            if (!IsFinite(magnitude) || magnitude < MinimumRayDirectionMagnitude)
            {
                error = "ray direction could not be normalized";
                return false;
            }

            direction /= magnitude;
            ray = new Ray(origin, direction);
            float normalizedMagnitude = ray.direction.sqrMagnitude;
            if (!IsFinite(normalizedMagnitude) || normalizedMagnitude < 0.9999f || normalizedMagnitude > 1.0001f)
            {
                ray = default(Ray);
                error = "normalized ray direction is invalid";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void OnSmokeUiButtonPressed()
        {
            state = "UI BUTTON WORKS";
            hitState = "EventSystem callback: SUCCESS";
            helpState = "The Canvas GraphicRaycaster and exactly one EventSystem are working.";
            ApplyCubeColor(cubeIdleColor);
            UpdateDiagnosticUi();
            Debug.Log("[Rat Habitat] InteractionSmokeTest UI BUTTON WORKS.");
        }

        private void ApplyCubeColor(Color color)
        {
            if (cubeMaterial == null) return;
            ApplyMaterialColor(cubeMaterial, color);
        }

        private void UpdateDiagnosticUi()
        {
            if (stateText != null) stateText.text = state;
            if (hitText != null) hitText.text = hitState;
            if (pointerText != null) pointerText.text = hasPointerPosition ? pointerState : "Pointer screen: —";
            if (helpText != null) helpText.text = helpState;
            if (startupText != null) startupText.text = startupState;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnGUI()
        {
            if (initialized) return;
            GUI.Box(new Rect(12f, 12f, Mathf.Min(620f, Mathf.Max(280f, Screen.width - 24f)), 64f), startupState);
        }
    }
}
