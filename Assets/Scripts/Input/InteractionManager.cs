using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// The one world-selection path for the vertical slice. It polls the
    /// legacy Input API so the same code works with Unity 2022.3's Editor and
    /// Android without requiring a second input package or a PhysicsRaycaster.
    /// Physics is queried only after a completed mouse click or touch tap.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionManager : MonoBehaviour
    {
        private static InteractionManager activeManager;

        private Camera targetCamera;
        private Func<SelectableEntity, bool> selectionHandler;
        private Func<bool> panelVisibilityHandler;
        private Action escapeHandler;
        private Action<string> diagnosticHandler;
        private Action<float> zoomHandler;
        private Action<Vector3> emptyWorldTapHandler;
        private Func<int, bool> habitatSwipeHandler;
        private Func<bool> modalOverlayHandler;
        private VerticalSliceUI pageUi;
        private Vector3 cameraTarget;
        private Vector2 touchStart;
        private int touchFingerId = -1;
        private bool touchStartedOnUi;
        private bool touchStartedOnRatProfile;
        private Vector2 mousePressPosition;
        private bool mousePressStartedOnUi;
        private bool mousePressStartedOnRatProfile;
        private bool mouseDragDetected;
        private bool pinchStartedOnUi;
        private bool pinchZooming;
        private float lastPinchDistance;
        private int lastPointerFrame = -1;
        private Vector2 lastPointerPosition;
        private bool configured;
        private int clickableLayerMask = ~0;

        private const float MaxSelectionDistance = 100f;
        private const float TouchMoveThreshold = 24f;
        private const float MouseMoveThreshold = 8f;
        private const float HorizontalGestureDominance = 1.25f;
        private const float MinimumRayDirectionMagnitude = 0.0001f;

        private bool managerReady;
        private string startupError = string.Empty;
        private bool pointerReceived;
        private Vector2 lastScreenPosition;
        private bool rayCreated;
        private bool raycastOccurred;
        private string raycastResult = "NONE";
        private bool interactableFound;
        private string selectedObject = "NONE";
        private string selectionCallback = "NOT RUN";
        private string informationPanel = "NOT RUN";
        private string caseLabel = "A. No pointer input received yet.";
        private int raycastHitCount;

        private readonly List<HighlightRecord> highlightRecords = new List<HighlightRecord>();
        private SelectableEntity hoveredEntity;

        private struct HighlightRecord
        {
            public Renderer renderer;
            public Color color;
            public bool useBaseColor;
            public bool useLegacyColor;
        }

        private void Awake()
        {
            if (activeManager != null && activeManager != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            activeManager = this;
            DisableLegacySelectionControllers();
        }

        private void OnDestroy()
        {
            ClearHighlight();
            if (activeManager == this) activeManager = null;
        }

        public void Configure(
            Camera camera,
            Func<SelectableEntity, bool> onSelected,
            Func<bool> panelVisible,
            Action onEscape,
            Action<string> onDiagnostic = null,
            Action<float> onZoom = null,
            Action<Vector3> onEmptyWorldTap = null,
            Func<int, bool> onHabitatSwipe = null,
            Func<bool> isModalOverlayOpen = null)
        {
            targetCamera = camera != null ? camera : Camera.main;
            selectionHandler = onSelected;
            panelVisibilityHandler = panelVisible;
            escapeHandler = onEscape;
            diagnosticHandler = onDiagnostic;
            zoomHandler = onZoom;
            emptyWorldTapHandler = onEmptyWorldTap;
            habitatSwipeHandler = onHabitatSwipe;
            modalOverlayHandler = isModalOverlayOpen;
            cameraTarget = new Vector3(0f, 0f, 1f);
            clickableLayerMask = ~0;
            startupError = string.Empty;
            configured = targetCamera != null;
            managerReady = configured;

            DisableDuplicateManagers(this);
            DisableLegacySelectionControllers();
            SceneVisibilityGuard.DisableBuiltInPhysicsRaycasters(targetCamera);
            EnsureInteractablesAndColliders();

            if (!configured)
            {
                SetStartupError("Main Camera is unavailable.");
                return;
            }

            Debug.Log("[Rat Habitat] InteractionManager READY: one manual Main Camera mouse/touch path is active.");
        }

        public void SetStartupError(string message)
        {
            managerReady = false;
            startupError = string.IsNullOrEmpty(message) ? "Interaction startup failed." : message;
            caseLabel = "Startup error: " + startupError;
            ReportDiagnosticMessage(BuildDiagnosticMessage());
        }

        private void Update()
        {
            if (!configured || !managerReady || !isActiveAndEnabled) return;

            // Modal panels are owned by the UI EventSystem.  Do not poll
            // world input while one is open: polling here would allow a
            // touch pinch, mouse hover, or an already-started drag to leak
            // through the invisible modal blocker into the habitat camera.
            if (IsModalOverlayOpen())
            {
                CancelWorldPointerState();
                ClearHoverPreview();
                HandleDesktopCamera();
                return;
            }

            HandleTouch();
            HandleMouse();
            HandleDesktopCamera();
        }

        private bool IsModalOverlayOpen()
        {
            return modalOverlayHandler != null && modalOverlayHandler();
        }

        private void CancelWorldPointerState()
        {
            touchFingerId = -1;
            touchStartedOnUi = false;
            touchStartedOnRatProfile = false;
            pinchStartedOnUi = false;
            pinchZooming = false;
            lastPinchDistance = 0f;
            mousePressStartedOnUi = true;
            mousePressStartedOnRatProfile = false;
            mouseDragDetected = false;
        }

        private void HandleTouch()
        {
            if (IsModalOverlayOpen())
            {
                CancelWorldPointerState();
                return;
            }

            int touchCount = Input.touchCount;
            if (touchCount >= 2)
            {
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                touchFingerId = -1;

                bool pinchBeganOnUi = (first.phase == TouchPhase.Began && IsPointerOverInteractiveUi(first.position, first.fingerId)) ||
                    (second.phase == TouchPhase.Began && IsPointerOverInteractiveUi(second.position, second.fingerId));
                if (pinchBeganOnUi ||
                    IsPointerOverInteractiveUi(first.position, first.fingerId) ||
                    IsPointerOverInteractiveUi(second.position, second.fingerId))
                {
                    pinchStartedOnUi = true;
                }

                if (pinchStartedOnUi)
                {
                    pinchZooming = false;
                    lastPinchDistance = 0f;
                    return;
                }

                float distance = Vector2.Distance(first.position, second.position);
                if (!pinchZooming) pinchZooming = true;
                else AdjustZoom((distance - lastPinchDistance) * 0.012f);
                lastPinchDistance = distance;
                return;
            }

            if (pinchZooming)
            {
                pinchZooming = false;
                lastPinchDistance = 0f;
                touchFingerId = -1;
                pinchStartedOnUi = false;
                return;
            }

            if (touchCount == 0)
            {
                pinchStartedOnUi = false;
                return;
            }

            for (int i = 0; i < touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    touchFingerId = touch.fingerId;
                    touchStart = touch.position;
                    touchStartedOnRatProfile = IsPointerOverRatProfile(touch.position);
                    touchStartedOnUi = IsPointerOverInteractiveUi(touch.position, touch.fingerId);
                    if (touchStartedOnUi) pinchStartedOnUi = true;
                }
                else if (touch.phase == TouchPhase.Ended && touch.fingerId == touchFingerId)
                {
                    RecordPointerReceived(touch.position);
                    bool moved = Vector2.Distance(touchStart, touch.position) > TouchMoveThreshold;
                    bool overUi = IsPointerOverInteractiveUi(touch.position, touch.fingerId);
                    bool pageControlHandled = !moved && TryInvokePageControl(touch.position);
                    if (pageControlHandled)
                    {
                        RecordNoWorldRay("page UI control handled");
                    }
                    else if (touchStartedOnRatProfile)
                    {
                        // The profile ScrollRect owns this gesture. Never
                        // pass its release into the habitat swipe/raycast.
                        RecordNoWorldRay("rat profile scroll handled");
                    }
                    else if (touchStartedOnUi || overUi)
                    {
                        RecordNoWorldRay("UI blocked");
                    }
                    else if (moved)
                    {
                        if (TryInvokeHabitatSwipe(touch.position - touchStart))
                            RecordNoWorldRay("habitat swipe handled");
                        else
                            RecordNoWorldRay("touch drag ignored");
                    }
                    else
                    {
                        ProcessPointer(touch.position);
                    }

                    touchFingerId = -1;
                    touchStartedOnUi = false;
                    touchStartedOnRatProfile = false;
                }
                else if (touch.phase == TouchPhase.Canceled && touch.fingerId == touchFingerId)
                {
                    RecordPointerReceived(touch.position);
                    RecordNoWorldRay("touch canceled");
                    touchFingerId = -1;
                    touchStartedOnUi = false;
                    touchStartedOnRatProfile = false;
                }
            }
        }

        private void HandleMouse()
        {
            if (IsModalOverlayOpen())
            {
                CancelWorldPointerState();
                return;
            }

            Vector2 pointerPosition = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                RecordPointerReceived(pointerPosition);
                mousePressPosition = pointerPosition;
                mousePressStartedOnRatProfile = IsPointerOverRatProfile(pointerPosition);
                mousePressStartedOnUi = IsPointerOverInteractiveUi(pointerPosition, -1);
                mouseDragDetected = false;
                return;
            }

            if (Input.GetMouseButton(0))
            {
                if (!mousePressStartedOnUi && Vector2.Distance(mousePressPosition, pointerPosition) > MouseMoveThreshold)
                {
                    mouseDragDetected = true;
                }
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                RecordPointerReceived(pointerPosition);
                bool moved = mouseDragDetected || Vector2.Distance(mousePressPosition, pointerPosition) > MouseMoveThreshold;
                bool overUi = IsPointerOverInteractiveUi(pointerPosition, -1);
                bool pageControlHandled = !moved && TryInvokePageControl(pointerPosition);
                if (pageControlHandled)
                {
                    RecordNoWorldRay("page UI control handled");
                }
                else if (mousePressStartedOnRatProfile)
                {
                    RecordNoWorldRay("rat profile scroll handled");
                }
                else if (mousePressStartedOnUi || overUi)
                {
                    RecordNoWorldRay("UI blocked");
                }
                else if (moved)
                {
                    if (TryInvokeHabitatSwipe(pointerPosition - mousePressPosition))
                        RecordNoWorldRay("habitat swipe handled");
                    else
                        RecordNoWorldRay("mouse drag ignored");
                }
                else
                {
                    ProcessPointer(pointerPosition);
                }

                mousePressStartedOnUi = false;
                mousePressStartedOnRatProfile = false;
                mouseDragDetected = false;
                return;
            }

            if (!IsPointerOverInteractiveUi(pointerPosition, -1)) UpdateHover(pointerPosition);
            else ClearHoverPreview();
        }

        private bool TryInvokePageControl(Vector2 screenPosition)
        {
            if (pageUi == null) pageUi = FindObjectOfType<VerticalSliceUI>();
            return pageUi != null && pageUi.TryInvokePageControlAt(screenPosition);
        }

        private bool IsPointerOverRatProfile(Vector2 screenPosition)
        {
            if (pageUi == null) pageUi = FindObjectOfType<VerticalSliceUI>();
            return pageUi != null && pageUi.IsPointerOverRatProfileScroll(screenPosition);
        }

        private bool TryInvokeHabitatSwipe(Vector2 delta)
        {
            // A page change is only a horizontal gesture. Vertical drags are
            // left to the page ScrollRect or ignored by the world path. The
            // completed drag is consumed before ProcessPointer, so it can
            // never select or move the rat underneath the gesture.
            if (habitatSwipeHandler == null) return false;
            if (delta.magnitude < TouchMoveThreshold) return false;
            // Only a clearly horizontal gesture belongs to the habitat
            // carousel. This leaves diagonal and vertical drags to a nested
            // profile/list ScrollRect instead of firing both systems.
            if (Mathf.Abs(delta.x) < Mathf.Abs(delta.y) * HorizontalGestureDominance) return false;
            int direction = delta.x < 0f ? 1 : -1;
            return habitatSwipeHandler(direction);
        }

        private void UpdateHover(Vector2 screenPosition)
        {
            if (IsModalOverlayOpen())
            {
                ClearHoverPreview();
                return;
            }

            Ray ray;
            string failure;
            if (!TryCreatePointerRay(screenPosition, out ray, out failure))
            {
                ClearHoverPreview();
                return;
            }

            EnsureInteractablesAndColliders();
            Physics.SyncTransforms();
            RaycastHit[] hits = Physics.RaycastAll(ray, MaxSelectionDistance, clickableLayerMask, QueryTriggerInteraction.Ignore);
            SelectableEntity entity;
            Collider ignoredCollider;
            Collider ignoredAnyCollider;
            bool ignoredMarker;
            if (!TryResolveNearestEntity(hits, out entity, out ignoredCollider, out ignoredAnyCollider, out ignoredMarker))
            {
                ClearHoverPreview();
                return;
            }

            if (hoveredEntity == entity) return;
            hoveredEntity = entity;
            ApplyHighlight(entity);
        }

        private void ClearHoverPreview()
        {
            if (hoveredEntity == null) return;
            hoveredEntity = null;
            ClearHighlight();
        }

        private static bool IsPointerOverInteractiveUi(Vector2 screenPoint, int pointerId)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            // Keep the native EventSystem pointer-over result as the first
            // guard for both mouse (-1) and Android touch finger IDs.  The
            // explicit GraphicRaycaster pass below remains as a fallback for
            // the project's manually-polled UI controls.
            bool pointerOverEventSystemUi = pointerId < 0
                ? eventSystem.IsPointerOverGameObject()
                : eventSystem.IsPointerOverGameObject(pointerId);
            if (pointerOverEventSystemUi) return true;

            // Do not call EventSystem.RaycastAll here. That method invokes
            // every registered BaseRaycaster, including a stale
            // PhysicsRaycaster that may be present in an imported scene. The
            // production world path is manual, so only inspect actual UI
            // GraphicRaycasters when deciding whether a pointer is over a
            // blocking button/card/modal.
            var eventData = new PointerEventData(eventSystem)
            {
                position = screenPoint,
                pointerId = pointerId,
            };
            var raycasters = UnityEngine.Object.FindObjectsOfType<GraphicRaycaster>(true);
            foreach (var raycaster in raycasters)
            {
                if (raycaster == null || !raycaster.isActiveAndEnabled || raycaster.gameObject == null ||
                    !raycaster.gameObject.activeInHierarchy) continue;

                var results = new List<RaycastResult>();
                raycaster.Raycast(eventData, results);
                foreach (var result in results)
                {
                    if (result.gameObject == null || !result.gameObject.activeInHierarchy) continue;

                    var graphic = result.gameObject.GetComponent<Graphic>();
                    if (graphic != null && !graphic.raycastTarget) continue;
                    if (graphic != null && graphic.raycastTarget && graphic.color.a > 0.001f) return true;

                    var selectable = result.gameObject.GetComponentInParent<Selectable>();
                    if (selectable != null && selectable.isActiveAndEnabled) return true;

                    var scrollRect = result.gameObject.GetComponentInParent<ScrollRect>();
                    if (scrollRect != null && scrollRect.isActiveAndEnabled)
                    {
                        // Visible scroll content is an intentional UI surface.
                        // The natural page viewport itself remains transparent
                        // to input so empty habitat space can still be used for
                        // world selection and camera gestures.
                        return true;
                    }
                }
            }

            return false;
        }

        private void ProcessPointer(Vector2 screenPosition)
        {
            if (IsModalOverlayOpen())
            {
                RecordNoWorldRay("modal overlay blocked");
                return;
            }
            // Do not process the same mouse/touch event twice when a desktop
            // platform also reports a touch-like event for it.
            if (lastPointerFrame == Time.frameCount && Vector2.Distance(lastPointerPosition, screenPosition) < 1f) return;
            lastPointerFrame = Time.frameCount;
            lastPointerPosition = screenPosition;

            raycastOccurred = false;
            rayCreated = false;
            raycastResult = "NONE";
            raycastHitCount = 0;
            interactableFound = false;
            selectedObject = "NONE";
            selectionCallback = "NOT RUN";
            informationPanel = "NOT RUN";

            Ray ray;
            string rayFailure;
            if (!TryCreatePointerRay(screenPosition, out ray, out rayFailure))
            {
                caseLabel = "Pointer received; world raycast not run (" + rayFailure + ").";
                ReportDiagnosticMessage(BuildDiagnosticMessage());
                return;
            }

            rayCreated = true;
            EnsureInteractablesAndColliders();
            Physics.SyncTransforms();

            RaycastHit[] hits;
            try
            {
                // This is the only world physics query in the project. The
                // Ray overload is used only after TryCreatePointerRay has
                // validated and normalized the direction.
                hits = Physics.RaycastAll(ray, MaxSelectionDistance, clickableLayerMask, QueryTriggerInteraction.Ignore);
            }
            catch (Exception exception)
            {
                caseLabel = "B. Raycast could not run safely: " + exception.Message;
                ReportDiagnosticMessage(BuildDiagnosticMessage());
                Debug.LogWarning("[Rat Habitat] InteractionManager skipped a rejected world raycast: " + exception.Message);
                return;
            }

            raycastOccurred = true;
            raycastHitCount = hits == null ? 0 : hits.Length;
            SelectableEntity nearestEntity;
            Collider nearestInteractableCollider;
            Collider nearestAnyCollider;
            bool anyInteractableMarker;
            TryResolveNearestEntity(hits, out nearestEntity, out nearestInteractableCollider, out nearestAnyCollider, out anyInteractableMarker);
            interactableFound = nearestEntity != null;

            Collider reportedCollider = nearestInteractableCollider != null ? nearestInteractableCollider : nearestAnyCollider;
            raycastResult = reportedCollider == null ? "NONE" : reportedCollider.gameObject.name;

            if (nearestEntity != null && selectionHandler != null)
            {
                try
                {
                    bool callbackSucceeded = selectionHandler(nearestEntity);
                    selectionCallback = callbackSucceeded ? "SUCCESS" : "FAILURE";
                    selectedObject = nearestEntity.displayName;
                    ApplyHighlight(nearestEntity);
                    hoveredEntity = nearestEntity;
                    informationPanel = panelVisibilityHandler != null && panelVisibilityHandler() ? "OPEN" : "CLOSED";
                }
                catch (Exception exception)
                {
                    selectionCallback = "FAILURE";
                    selectedObject = nearestEntity.displayName;
                    informationPanel = "CLOSED";
                    caseLabel = "E. Selection callback failed; information panel did not open.";
                    ReportDiagnosticMessage(BuildDiagnosticMessage());
                    Debug.LogWarning("[Rat Habitat] Selection callback failed safely: " + exception.Message);
                    return;
                }
            }
            else
            {
                hoveredEntity = null;
                ClearHighlight();
                Vector3 worldPoint;
                if (!TryGetNearestWorldPoint(hits, out worldPoint))
                {
                    // A tap on the dark space around the habitat may not hit
                    // a collider, but it can still be projected onto the
                    // habitat plane so the nearest enclosure can be focused.
                    if (!TryProjectToHabitatPlane(ray, out worldPoint))
                    {
                        worldPoint = Vector3.zero;
                    }
                }

                if (emptyWorldTapHandler != null)
                {
                    try
                    {
                        emptyWorldTapHandler(worldPoint);
                        selectionCallback = "SUCCESS";
                    }
                    catch (Exception exception)
                    {
                        selectionCallback = "FAILURE";
                        Debug.LogWarning("[Rat Habitat] Empty-world focus callback failed safely: " + exception.Message);
                    }
                }
                else if (selectionHandler != null)
                {
                    try
                    {
                        bool callbackSucceeded = selectionHandler(null);
                        selectionCallback = callbackSucceeded ? "SUCCESS" : "FAILURE";
                    }
                    catch (Exception exception)
                    {
                        selectionCallback = "FAILURE";
                        Debug.LogWarning("[Rat Habitat] Empty-floor selection callback failed safely: " + exception.Message);
                    }
                }

                informationPanel = panelVisibilityHandler != null && panelVisibilityHandler() ? "OPEN" : "CLOSED";
            }

            if (hits == null || hits.Length == 0)
            {
                caseLabel = "B. Pointer received; raycast hit nothing.";
            }
            else if (!anyInteractableMarker)
            {
                caseLabel = IsFloorOrGeometry(raycastResult)
                    ? "C. Raycast hit floor/geometry but no interactable."
                    : "D. Collider hit but no InteractableObject found.";
            }
            else if (nearestEntity == null)
            {
                caseLabel = "D. InteractableObject marker found, but no active SelectableEntity parent was found.";
            }
            else if (selectionCallback != "SUCCESS")
            {
                caseLabel = "E. Selection callback failed; information panel did not open.";
            }
            else if (informationPanel != "OPEN")
            {
                caseLabel = "E. Selection callback succeeded but information panel is not open.";
            }
            else
            {
                caseLabel = "Selection succeeded.";
            }

            ReportDiagnosticMessage(BuildDiagnosticMessage());
        }

        private static bool TryGetNearestWorldPoint(RaycastHit[] hits, out Vector3 point)
        {
            point = Vector3.zero;
            if (hits == null || hits.Length == 0) return false;

            float nearestDistance = float.MaxValue;
            bool found = false;
            foreach (var hit in hits)
            {
                Collider collider = hit.collider;
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if (hit.distance >= nearestDistance) continue;
                nearestDistance = hit.distance;
                point = hit.point;
                found = true;
            }
            return found;
        }

        private static bool TryProjectToHabitatPlane(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            if (Mathf.Abs(ray.direction.y) < MinimumRayDirectionMagnitude) return false;

            float distance = (0.45f - ray.origin.y) / ray.direction.y;
            if (distance <= 0f || float.IsNaN(distance) || float.IsInfinity(distance)) return false;
            point = ray.origin + ray.direction * distance;
            return true;
        }

        private static bool TryResolveNearestEntity(
            RaycastHit[] hits,
            out SelectableEntity nearestEntity,
            out Collider nearestInteractableCollider,
            out Collider nearestAnyCollider,
            out bool anyInteractableMarker)
        {
            nearestEntity = null;
            nearestInteractableCollider = null;
            nearestAnyCollider = null;
            anyInteractableMarker = false;
            float nearestInteractableDistance = float.MaxValue;
            float nearestAnyDistance = float.MaxValue;

            if (hits == null) return false;
            foreach (var hit in hits)
            {
                Collider collider = hit.collider;
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;

                if (hit.distance < nearestAnyDistance)
                {
                    nearestAnyDistance = hit.distance;
                    nearestAnyCollider = collider;
                }

                // Resolve only the gameplay marker attached to the stable
                // selectable root. Renderer/helper geometry without that
                // marker cannot win the selection race.
                InteractableObject marker = collider.GetComponentInParent<InteractableObject>();
                if (marker == null) continue;
                anyInteractableMarker = true;
                SelectableEntity entity = marker.ResolveSelection();
                if (entity == null || !entity.isActiveAndEnabled) continue;

                if (hit.distance < nearestInteractableDistance)
                {
                    nearestEntity = entity;
                    nearestInteractableCollider = collider;
                    nearestInteractableDistance = hit.distance;
                }
            }
            return nearestEntity != null;
        }

        private void RecordPointerReceived(Vector2 position)
        {
            pointerReceived = true;
            lastScreenPosition = position;
        }

        private void RecordNoWorldRay(string reason)
        {
            rayCreated = false;
            raycastOccurred = false;
            raycastHitCount = 0;
            raycastResult = "NONE";
            interactableFound = false;
            selectedObject = "NONE";
            selectionCallback = "NOT RUN";
            informationPanel = "NOT RUN";
            caseLabel = "Pointer received; world raycast not run (" + reason + ").";
            ReportDiagnosticMessage(BuildDiagnosticMessage());
        }

        private bool TryCreatePointerRay(Vector2 pointerPosition, out Ray ray, out string failure)
        {
            ray = default(Ray);
            failure = "invalid pointer coordinates";
            if (!IsFinite(pointerPosition.x) || !IsFinite(pointerPosition.y)) return false;
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                failure = "invalid screen size";
                return false;
            }
            if (pointerPosition.x < 0f || pointerPosition.y < 0f ||
                pointerPosition.x > Screen.width || pointerPosition.y > Screen.height)
            {
                failure = "pointer is outside the screen";
                return false;
            }

            if (targetCamera == null || !targetCamera.enabled || !targetCamera.gameObject.activeInHierarchy)
            {
                failure = "Main Camera unavailable";
                return false;
            }
            if (!targetCamera.CompareTag("MainCamera"))
            {
                failure = "camera is not tagged MainCamera";
                return false;
            }
            if (targetCamera.pixelWidth <= 0 || targetCamera.pixelHeight <= 0)
            {
                failure = "invalid camera viewport";
                return false;
            }

            Rect cameraPixelRect = targetCamera.pixelRect;
            if (cameraPixelRect.width <= 0f || cameraPixelRect.height <= 0f ||
                pointerPosition.x < cameraPixelRect.xMin || pointerPosition.y < cameraPixelRect.yMin ||
                pointerPosition.x > cameraPixelRect.xMax || pointerPosition.y > cameraPixelRect.yMax)
            {
                failure = "pointer is outside the Main Camera viewport";
                return false;
            }

            Vector3 cameraPosition = targetCamera.transform.position;
            if (!IsFinite(cameraPosition.x) || !IsFinite(cameraPosition.y) || !IsFinite(cameraPosition.z))
            {
                failure = "invalid camera position";
                return false;
            }

            Ray generatedRay;
            try
            {
                // Both mouse and touch pass their actual Game View screen
                // coordinates; use the configured Main Camera directly.
                generatedRay = targetCamera.ScreenPointToRay(pointerPosition);
            }
            catch (Exception)
            {
                failure = "Main Camera ray creation failed";
                return false;
            }

            Vector3 origin = generatedRay.origin;
            Vector3 direction = generatedRay.direction;
            if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(origin.z))
            {
                failure = "invalid ray origin";
                return false;
            }
            if (!IsFinite(direction.x) || !IsFinite(direction.y) || !IsFinite(direction.z))
            {
                failure = "invalid ray direction";
                return false;
            }

            float squaredMagnitude = direction.sqrMagnitude;
            if (!IsFinite(squaredMagnitude) || squaredMagnitude < MinimumRayDirectionMagnitude * MinimumRayDirectionMagnitude)
            {
                failure = "zero or invalid ray direction";
                return false;
            }

            float magnitude = Mathf.Sqrt(squaredMagnitude);
            if (!IsFinite(magnitude) || magnitude < MinimumRayDirectionMagnitude)
            {
                failure = "zero or invalid ray direction";
                return false;
            }

            direction /= magnitude;
            float normalizedMagnitude = direction.sqrMagnitude;
            if (!IsFinite(normalizedMagnitude) || Mathf.Abs(normalizedMagnitude - 1f) > 0.0001f)
            {
                failure = "ray direction could not be normalized";
                return false;
            }

            ray = new Ray(origin, direction);
            Vector3 validatedDirection = ray.direction;
            float validatedMagnitude = validatedDirection.sqrMagnitude;
            if (!IsFinite(validatedMagnitude) || validatedMagnitude < 0.9999f || validatedMagnitude > 1.0001f)
            {
                ray = default(Ray);
                failure = "validated ray direction is not normalized";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFloorOrGeometry(string objectName)
        {
            string value = string.IsNullOrEmpty(objectName) ? string.Empty : objectName.ToLowerInvariant();
            return value.Contains("floor") || value.Contains("wall") || value.Contains("rug") ||
                value.Contains("trim") || value.Contains("geometry") || value.Contains("render test") ||
                value.Contains("fallback");
        }

        private static void DisableDuplicateManagers(InteractionManager keep)
        {
            var managers = FindObjectsOfType<InteractionManager>(true);
            foreach (var manager in managers)
            {
                if (manager == null || manager == keep) continue;
                manager.enabled = false;
                Destroy(manager);
            }

            if (keep != null) activeManager = keep;
        }

        private static void DisableLegacySelectionControllers()
        {
            var controllers = FindObjectsOfType<SelectionController>(true);
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                controller.enabled = false;
                Destroy(controller);
            }
        }

        private static void EnsureInteractablesAndColliders()
        {
            var entities = FindObjectsOfType<SelectableEntity>(true);
            foreach (var entity in entities)
            {
                if (entity == null || !entity.gameObject.activeInHierarchy) continue;

                var marker = entity.GetComponent<InteractableObject>();
                if (marker == null) marker = entity.gameObject.AddComponent<InteractableObject>();
                marker.Configure(entity);

                var childColliders = entity.GetComponentsInChildren<Collider>(true);
                bool hasEnabledCollider = false;
                foreach (var childCollider in childColliders)
                {
                    if (childCollider == null) continue;
                    if (entity.kind == SelectableKind.Rat)
                    {
                        // Imported mesh/helper colliders are not selection
                        // surfaces. RatPresenter owns only the marker-tagged
                        // torso/head/feet/tail hitboxes.
                        childCollider.enabled = childCollider.GetComponent<RatSelectionCollider>() != null;
                    }
                    else
                    {
                        // Habitat objects are represented by their selectable
                        // root. Do not let decorative child geometry become a
                        // second, displaced interaction surface.
                        childCollider.enabled = childCollider.transform == entity.transform;
                    }
                    if (childCollider.enabled && childCollider.gameObject.activeInHierarchy) hasEnabledCollider = true;
                }

                if (!hasEnabledCollider)
                {
                    if (entity.kind == SelectableKind.Rat) continue;
                    var box = entity.GetComponent<BoxCollider>();
                    if (box == null) box = entity.gameObject.AddComponent<BoxCollider>();
                    Bounds visibleBounds;
                    if (TryGetVisibleLocalBounds(entity, out visibleBounds))
                    {
                        box.center = visibleBounds.center;
                        box.size = visibleBounds.size;
                    }
                    else
                    {
                        box.center = new Vector3(0f, 0.5f, 0f);
                        box.size = new Vector3(1.5f, 1.2f, 1.5f);
                    }
                    box.isTrigger = false;
                    box.enabled = true;
                }
            }
        }

        private static bool TryGetVisibleLocalBounds(SelectableEntity entity, out Bounds localBounds)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
            if (entity == null) return false;

            Renderer selected = null;
            float largestArea = 0f;
            var renderers = entity.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                string name = renderer.gameObject.name.ToLowerInvariant();
                if (name.Contains("selection") || name.Contains("overlay") || name.Contains("mark") || name.Contains("helper")) continue;

                float area = renderer.bounds.size.sqrMagnitude;
                if (selected == null || area > largestArea)
                {
                    selected = renderer;
                    largestArea = area;
                }
            }

            if (selected == null || selected.bounds.size.sqrMagnitude <= 0.0001f) return false;
            Bounds worldBounds = selected.bounds;
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
                        Vector3 localCorner = entity.transform.InverseTransformPoint(corner);
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

        private void ApplyHighlight(SelectableEntity entity)
        {
            ClearHighlight();
            if (entity == null) return;

            var renderers = entity.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.material == null) continue;
                Material material = renderer.material;
                bool useBaseColor = material.HasProperty("_BaseColor");
                bool useLegacyColor = material.HasProperty("_Color");
                if (!useBaseColor && !useLegacyColor) continue;

                highlightRecords.Add(new HighlightRecord
                {
                    renderer = renderer,
                    color = useBaseColor ? material.GetColor("_BaseColor") : material.GetColor("_Color"),
                    useBaseColor = useBaseColor,
                    useLegacyColor = useLegacyColor,
                });

                Color highlight = Color.Lerp(highlightRecords[highlightRecords.Count - 1].color, Color.white, 0.55f);
                if (useBaseColor) material.SetColor("_BaseColor", highlight);
                if (useLegacyColor) material.SetColor("_Color", highlight);
            }
        }

        private void ClearHighlight()
        {
            foreach (var record in highlightRecords)
            {
                if (record.renderer == null || record.renderer.material == null) continue;
                Material material = record.renderer.material;
                if (record.useBaseColor) material.SetColor("_BaseColor", record.color);
                if (record.useLegacyColor) material.SetColor("_Color", record.color);
            }
            highlightRecords.Clear();
        }

        private string BuildDiagnosticMessage()
        {
            string managerState = managerReady ? "READY" : "ERROR — " + startupError;
            string pointer = pointerReceived ? "YES" : "NO";
            string coordinates = pointerReceived
                ? "(" + lastScreenPosition.x.ToString("0") + ", " + lastScreenPosition.y.ToString("0") + ")"
                : "—";
            string created = rayCreated ? "YES" : "NO";
            string occurred = raycastOccurred ? "YES" : "NO";
            string raycast = raycastOccurred
                ? raycastResult + " (" + raycastHitCount + " hits)"
                : "NONE";

            return "Interaction Manager: " + managerState +
                "  •  Pointer received: " + pointer +
                "  •  Pointer screen: " + coordinates +
                "  •  Ray created: " + created +
                "  •  Raycast occurred: " + occurred +
                "  •  Raycast result: " + raycast +
                "  •  Interactable found: " + (interactableFound ? "YES" : "NO") +
                "  •  Selection callback: " + selectionCallback +
                "  •  Information panel: " + informationPanel +
                "  •  Selected: " + selectedObject +
                "  •  Case: " + caseLabel;
        }

        private void ReportDiagnosticMessage(string message)
        {
            if (diagnosticHandler != null) diagnosticHandler(message);
            // Pointer diagnostics are internal telemetry, not player alerts.
            // Routine taps, empty-floor focus, and UI-blocked input should not
            // flood the Console. Raycast and callback failures are logged at
            // their call sites with the appropriate warning severity.
        }

        private void HandleDesktopCamera()
        {
            if (targetCamera == null) return;

            if (IsModalOverlayOpen())
            {
                ClearHoverPreview();
                if (Input.GetKeyDown(KeyCode.Escape) && escapeHandler != null) escapeHandler();
                return;
            }

            float horizontal = 0f;
            float vertical = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
            if (Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f)
            {
                cameraTarget += new Vector3(horizontal, 0f, vertical) * (4.5f * Time.unscaledDeltaTime);
                cameraTarget.x = Mathf.Clamp(cameraTarget.x, -3.5f, 3.5f);
                cameraTarget.z = Mathf.Clamp(cameraTarget.z, -2f, 5f);
                PositionCamera();
            }

            // Let an interactive UI control, especially the breeding
            // ScrollRect, consume mouse-wheel input. Keyboard zoom remains
            // available regardless of pointer location, while scrolling
            // outside UI continues to control the habitat camera.
            float wheelZoom = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheelZoom) > 0.001f && IsPointerOverInteractiveUi(Input.mousePosition, -1)) wheelZoom = 0f;
            float zoom = wheelZoom;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) zoom += 0.2f;
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) zoom -= 0.2f;
            if (Mathf.Abs(zoom) > 0.001f) AdjustZoom(zoom * 3f);

            if (Input.GetKeyDown(KeyCode.Escape) && escapeHandler != null) escapeHandler();
        }

        private void PositionCamera()
        {
            targetCamera.transform.position = cameraTarget + new Vector3(0f, 14f, -15f);
            targetCamera.transform.LookAt(cameraTarget + new Vector3(0f, 0.1f, 0f));
        }

        private void AdjustZoom(float amount)
        {
            if (IsModalOverlayOpen()) return;

            if (zoomHandler != null)
            {
                zoomHandler(amount);
                return;
            }
            if (targetCamera == null) return;
            targetCamera.orthographicSize = Mathf.Clamp(
                targetCamera.orthographicSize - amount,
                GameConfig.CameraMinimumOrthographicSize,
                GameConfig.CameraMaximumOrthographicSize);
        }

        private void OnGUI()
        {
            // Diagnostics remain available in the Unity Console. The normal
            // player-facing UI no longer draws a large bottom debug panel.
        }
    }
}
