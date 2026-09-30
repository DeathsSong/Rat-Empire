using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// Owns Family Tree gestures directly. The tree is nested in the natural
    /// page layout, so relying on two competing ScrollRects makes pointer
    /// ownership dependent on EventSystem hierarchy order. This component is
    /// the only drag/zoom owner inside the tree viewport.
    /// </summary>
    public sealed class FamilyTreePanZoomController : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        private RectTransform viewport;
        private RectTransform content;
        private Text diagnostics;
        private Func<string> contextProvider;
        private float zoom = 1f;
        private Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
        private Vector2 lastScreenPosition;
        private Vector2 dragStartScreenPosition;
        private int activePointerId = int.MinValue;
        private bool pointerDown;
        private bool dragging;
        private bool pinching;
        private bool manualPointerActive;
        private int manualPointerId = int.MinValue;
        private Vector2 manualLastScreenPosition;
        private Vector2 manualStartScreenPosition;
        private bool eventSystemDragObserved;
        private float lastPinchDistance;
        private float diagnosticTimer;

        private const float DragThresholdPixels = 8f;
        private const float MinZoom = 0.35f;
        private const float MaxZoom = 2.2f;
        private const float SmallTreePanMargin = 120f;

        public float Zoom { get { return zoom; } }
        public bool IsDragging { get { return dragging; } }
        public Vector2 NormalizedPosition
        {
            get
            {
                normalizedPosition = CalculateNormalizedPosition();
                return normalizedPosition;
            }
        }

        public void Configure(RectTransform treeViewport, RectTransform treeContent,
            float initialZoom, Vector2 initialNormalizedPosition, Font diagnosticFont,
            Func<string> diagnosticContext)
        {
            viewport = treeViewport;
            content = treeContent;
            contextProvider = diagnosticContext;
            zoom = Mathf.Clamp(initialZoom, MinZoom, MaxZoom);
            normalizedPosition = new Vector2(
                Mathf.Clamp01(initialNormalizedPosition.x),
                Mathf.Clamp01(initialNormalizedPosition.y));
            pointerDown = false;
            dragging = false;
            pinching = false;
            manualPointerActive = false;
            manualPointerId = int.MinValue;
            eventSystemDragObserved = false;
            activePointerId = int.MinValue;
            lastPinchDistance = 0f;

            EnsureDiagnostics(diagnosticFont);
            ApplyView(false, viewport == null ? Vector2.zero : viewport.position);
        }

        public void SetZoom(float value)
        {
            zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
            ApplyView(false, viewport == null ? Vector2.zero : viewport.position);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData != null) eventData.useDragThreshold = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData)) return;
            pointerDown = true;
            dragging = false;
            activePointerId = eventData.pointerId;
            eventSystemDragObserved = false;
            lastScreenPosition = eventData.position;
            dragStartScreenPosition = eventData.position;
            ReportDiagnostic("pointer-down", eventData.position, eventData.pointerId);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData)) return;
            if (!pointerDown || activePointerId != eventData.pointerId)
            {
                pointerDown = true;
                activePointerId = eventData.pointerId;
                lastScreenPosition = eventData.pressPosition;
                dragStartScreenPosition = eventData.pressPosition;
            }

            if (Vector2.Distance(dragStartScreenPosition, eventData.position) < DragThresholdPixels)
                return;

            eventSystemDragObserved = true;
            dragging = true;
            // A node Button may be the pointer press target. Once the motion
            // passes the threshold it is a pan, never a node activation.
            eventData.eligibleForClick = false;
            eventData.pointerPress = null;
            eventData.rawPointerPress = null;
            ReportDiagnostic("drag-start", eventData.position, eventData.pointerId);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData) || pinching) return;
            if (!pointerDown || activePointerId != eventData.pointerId)
                OnBeginDrag(eventData);
            if (!dragging) return;

            // PointerEventData.delta is the EventSystem's authoritative drag
            // delta. Compensate for the parent Canvas scale so the same input
            // moves the tree consistently in the Game view and WebGL. The
            // local-point fallback only covers synthetic/editor events that
            // report a zero delta.
            Vector2 contentDelta = eventData.delta;
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.scaleFactor > 0.0001f)
                contentDelta /= parentCanvas.scaleFactor;

            if (contentDelta.sqrMagnitude < 0.000001f)
            {
                Vector2 previousLocal;
                Vector2 currentLocal;
                if (!TryViewportLocalPoint(lastScreenPosition, eventData.pressEventCamera, out previousLocal) ||
                    !TryViewportLocalPoint(eventData.position, eventData.pressEventCamera, out currentLocal))
                {
                    return;
                }

                contentDelta = currentLocal - previousLocal;
            }

            content.anchoredPosition += contentDelta;
            ClampContentPosition();
            normalizedPosition = CalculateNormalizedPosition();
            lastScreenPosition = eventData.position;
            ReportDiagnosticThrottled("drag", eventData.position, eventData.pointerId);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData) || activePointerId != eventData.pointerId) return;
            ReportDiagnostic("drag-end", eventData.position, eventData.pointerId);
            dragging = false;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!IsPrimaryPointer(eventData) || activePointerId != eventData.pointerId) return;
            ReportDiagnostic("pointer-up", eventData.position, eventData.pointerId);
            pointerDown = false;
            dragging = false;
            activePointerId = int.MinValue;
            manualPointerActive = false;
            manualPointerId = int.MinValue;
            eventSystemDragObserved = false;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (eventData == null || viewport == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(viewport, eventData.position,
                    eventData.pressEventCamera)) return;
            float factor = 1f + eventData.scrollDelta.y * 0.12f;
            if (Mathf.Abs(factor - 1f) < 0.0001f) return;
            SetZoomAtScreenPoint(zoom * factor, eventData.position, eventData.pressEventCamera);
            ReportDiagnostic("wheel-zoom", eventData.position, eventData.pointerId);
        }

        private void Update()
        {
            if (viewport == null || content == null || !viewport.gameObject.activeInHierarchy) return;
            UpdatePinch();
            if (!pinching) UpdateManualPointerInput();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            diagnosticTimer -= Time.unscaledDeltaTime;
#endif
        }

        /// <summary>
        /// Some generated family-node controls receive the pointer press and
        /// release themselves, so Unity does not always deliver the complete
        /// drag chain to the viewport handler. Polling here is still owned by
        /// the viewport controller (never InteractionManager/world input), and
        /// only becomes active for a pointer that began inside the viewport.
        /// When normal EventSystem drag callbacks arrive, this path stands
        /// down for that gesture instead of double-applying movement.
        /// </summary>
        private void UpdateManualPointerInput()
        {
            if (Input.touchCount > 0)
            {
                UpdateManualTouch(Input.GetTouch(0));
                return;
            }

            Vector2 position = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && IsInsideViewport(position))
            {
                manualPointerActive = true;
                manualPointerId = -1;
                manualLastScreenPosition = position;
                manualStartScreenPosition = position;
                eventSystemDragObserved = false;
            }

            if (manualPointerActive && Input.GetMouseButton(0))
            {
                Vector2 delta = position - manualLastScreenPosition;
                if (!dragging && Vector2.Distance(manualStartScreenPosition, position) >= DragThresholdPixels)
                {
                    dragging = true;
                    pointerDown = true;
                    activePointerId = manualPointerId;
                    ReportDiagnostic("poll-drag-start", position, manualPointerId);
                }
                if (dragging && !eventSystemDragObserved)
                    PanByScreenDelta(delta);
                manualLastScreenPosition = position;
            }

            if (manualPointerActive && Input.GetMouseButtonUp(0))
            {
                ReportDiagnostic("poll-pointer-up", position, manualPointerId);
                manualPointerActive = false;
                manualPointerId = int.MinValue;
                if (activePointerId == -1)
                {
                    pointerDown = false;
                    dragging = false;
                    activePointerId = int.MinValue;
                }
                eventSystemDragObserved = false;
            }
        }

        private void UpdateManualTouch(Touch touch)
        {
            if (touch.phase == TouchPhase.Began && IsInsideViewport(touch.position))
            {
                manualPointerActive = true;
                manualPointerId = touch.fingerId;
                manualLastScreenPosition = touch.position;
                manualStartScreenPosition = touch.position;
                eventSystemDragObserved = false;
            }

            if (!manualPointerActive || manualPointerId != touch.fingerId) return;

            if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
            {
                Vector2 delta = touch.position - manualLastScreenPosition;
                if (!dragging && Vector2.Distance(manualStartScreenPosition, touch.position) >= DragThresholdPixels)
                {
                    dragging = true;
                    pointerDown = true;
                    activePointerId = manualPointerId;
                    ReportDiagnostic("poll-touch-drag-start", touch.position, manualPointerId);
                }
                if (dragging && !eventSystemDragObserved)
                    PanByScreenDelta(delta);
                manualLastScreenPosition = touch.position;
            }

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                ReportDiagnostic("poll-touch-up", touch.position, manualPointerId);
                manualPointerActive = false;
                manualPointerId = int.MinValue;
                if (activePointerId == touch.fingerId)
                {
                    pointerDown = false;
                    dragging = false;
                    activePointerId = int.MinValue;
                }
                eventSystemDragObserved = false;
            }
        }

        private bool IsInsideViewport(Vector2 screenPoint)
        {
            return viewport != null && RectTransformUtility.RectangleContainsScreenPoint(
                viewport, screenPoint, null);
        }

        private void PanByScreenDelta(Vector2 screenDelta)
        {
            if (content == null || screenDelta.sqrMagnitude < 0.000001f) return;
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.scaleFactor > 0.0001f)
                screenDelta /= parentCanvas.scaleFactor;
            content.anchoredPosition += screenDelta;
            ClampContentPosition();
            normalizedPosition = CalculateNormalizedPosition();
            ReportDiagnosticThrottled("poll-drag", Input.mousePosition, manualPointerId);
        }

        private void UpdatePinch()
        {
            if (Input.touchCount < 2)
            {
                if (pinching)
                    ReportDiagnostic("pinch-end", Input.mousePosition, -1);
                pinching = false;
                lastPinchDistance = 0f;
                return;
            }

            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            Vector2 midpoint = (first.position + second.position) * 0.5f;
            bool firstInside = RectTransformUtility.RectangleContainsScreenPoint(viewport,
                first.position, null);
            bool secondInside = RectTransformUtility.RectangleContainsScreenPoint(viewport,
                second.position, null);
            if (!firstInside || !secondInside)
            {
                pinching = false;
                lastPinchDistance = 0f;
                return;
            }

            float distance = Vector2.Distance(first.position, second.position);
            if (!pinching)
            {
                pinching = true;
                pointerDown = false;
                dragging = false;
                activePointerId = int.MinValue;
                lastPinchDistance = distance;
                ReportDiagnostic("pinch-start", midpoint, -1);
                return;
            }

            if (lastPinchDistance > 0.01f && distance > 0.01f)
            {
                float factor = distance / lastPinchDistance;
                if (Mathf.Abs(factor - 1f) > 0.001f)
                    SetZoomAtScreenPoint(zoom * factor, midpoint, null);
            }
            lastPinchDistance = distance;
            ReportDiagnosticThrottled("pinch", midpoint, -1);
        }

        private void SetZoomAtScreenPoint(float value, Vector2 screenPoint, Camera eventCamera)
        {
            if (content == null || viewport == null) return;
            float previousZoom = zoom;
            Vector2 viewportPoint;
            bool hasPoint = TryViewportLocalPoint(screenPoint, eventCamera, out viewportPoint);
            Vector2 topLeft = new Vector2(viewport.rect.xMin, viewport.rect.yMax);
            Vector2 contentPoint = Vector2.zero;
            if (hasPoint && previousZoom > 0.0001f)
            {
                contentPoint = (viewportPoint - topLeft - content.anchoredPosition) / previousZoom;
            }

            zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
            content.localScale = Vector3.one * zoom;
            Canvas.ForceUpdateCanvases();
            if (hasPoint)
                content.anchoredPosition = viewportPoint - topLeft - contentPoint * zoom;
            ClampContentPosition();
            normalizedPosition = CalculateNormalizedPosition();
        }

        private void ApplyView(bool keepPointer, Vector2 screenPoint)
        {
            if (content == null || viewport == null) return;
            content.localScale = Vector3.one * zoom;
            Canvas.ForceUpdateCanvases();
            SetNormalizedPosition(normalizedPosition);
            ClampContentPosition();
        }

        private void SetNormalizedPosition(Vector2 value)
        {
            if (viewport == null || content == null) return;
            Vector2 min;
            Vector2 max;
            GetPositionBounds(out min, out max);
            content.anchoredPosition = new Vector2(
                Mathf.Lerp(max.x, min.x, Mathf.Clamp01(value.x)),
                Mathf.Lerp(min.y, max.y, Mathf.Clamp01(value.y)));
        }

        private Vector2 CalculateNormalizedPosition()
        {
            if (viewport == null || content == null) return normalizedPosition;
            Vector2 min;
            Vector2 max;
            GetPositionBounds(out min, out max);
            float x = Mathf.Abs(max.x - min.x) < 0.001f
                ? 0.5f : Mathf.InverseLerp(max.x, min.x, content.anchoredPosition.x);
            float y = Mathf.Abs(max.y - min.y) < 0.001f
                ? 0.5f : Mathf.InverseLerp(min.y, max.y, content.anchoredPosition.y);
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        private void GetPositionBounds(out Vector2 min, out Vector2 max)
        {
            Vector2 viewportSize = viewport == null ? Vector2.zero : viewport.rect.size;
            Vector2 contentSize = content == null ? Vector2.zero : content.rect.size * zoom;
            float minX = contentSize.x > viewportSize.x ? viewportSize.x - contentSize.x :
                (viewportSize.x - contentSize.x) * 0.5f;
            float minY = contentSize.y > viewportSize.y ? viewportSize.y - contentSize.y :
                (viewportSize.y - contentSize.y) * 0.5f;
            if (contentSize.x > viewportSize.x)
            {
                min.x = minX;
                max.x = 0f;
            }
            else
            {
                // Keep a small, bounded pan range even for a one-generation
                // tree. This makes the gesture path testable and lets the
                // player nudge a compact tree without allowing it to vanish.
                float centered = (viewportSize.x - contentSize.x) * 0.5f;
                min.x = centered - SmallTreePanMargin;
                max.x = centered + SmallTreePanMargin;
            }
            if (contentSize.y > viewportSize.y)
            {
                min.y = minY;
                max.y = 0f;
            }
            else
            {
                float centered = (viewportSize.y - contentSize.y) * 0.5f;
                min.y = centered - SmallTreePanMargin;
                max.y = centered + SmallTreePanMargin;
            }
        }

        private void ClampContentPosition()
        {
            if (content == null) return;
            Vector2 min;
            Vector2 max;
            GetPositionBounds(out min, out max);
            Vector2 position = content.anchoredPosition;
            position.x = Mathf.Clamp(position.x, min.x, max.x);
            position.y = Mathf.Clamp(position.y, min.y, max.y);
            content.anchoredPosition = position;
        }

        private bool TryViewportLocalPoint(Vector2 screenPoint, Camera eventCamera, out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, screenPoint, eventCamera, out local);
        }

        private static bool IsPrimaryPointer(PointerEventData eventData)
        {
            return eventData != null && eventData.button == PointerEventData.InputButton.Left;
        }

        private void EnsureDiagnostics(Font diagnosticFont)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnostics != null) return;
            GameObject objectRoot = new GameObject("Family Tree Gesture Diagnostics");
            objectRoot.transform.SetParent(transform, false);
            RectTransform rect = objectRoot.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -8f);
            rect.sizeDelta = new Vector2(-16f, 58f);
            diagnostics = objectRoot.AddComponent<Text>();
            diagnostics.font = diagnosticFont;
            diagnostics.fontSize = 11;
            diagnostics.alignment = TextAnchor.UpperLeft;
            diagnostics.horizontalOverflow = HorizontalWrapMode.Wrap;
            diagnostics.verticalOverflow = VerticalWrapMode.Overflow;
            diagnostics.color = new Color(1f, 0.86f, 0.48f, 0.95f);
            diagnostics.raycastTarget = false;
            diagnostics.text = "Family Tree input: ready";
#endif
        }

        private void ReportDiagnostic(string phase, Vector2 screenPoint, int pointerId)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnostics == null) return;
            EventSystem eventSystem = EventSystem.current;
            string firstHit = "none";
            string allHits = "none";
            if (eventSystem != null)
            {
                var eventData = new PointerEventData(eventSystem)
                {
                    position = screenPoint,
                    pointerId = pointerId,
                };
                var results = new List<RaycastResult>();
                eventSystem.RaycastAll(eventData, results);
                if (results.Count > 0 && results[0].gameObject != null)
                    firstHit = results[0].gameObject.name;
                if (results.Count > 0)
                {
                    var names = new System.Text.StringBuilder();
                    for (int index = 0; index < results.Count; index++)
                    {
                        if (index > 0) names.Append(" > ");
                        names.Append(results[index].gameObject == null
                            ? "null" : results[index].gameObject.name);
                    }
                    allHits = names.ToString();
                }
            }
            string context = contextProvider == null ? string.Empty : contextProvider();
            diagnostics.text = "Family Tree " + phase +
                "\nfirst=" + firstHit + " all=" + allHits +
                "\nzoom=" + zoom.ToString("0.###") + " pan=" + NormalizedPosition +
                "\n" + context;
            Debug.Log("[Rat UI FamilyTree] phase=" + phase +
                " firstHit=" + firstHit + " allHits=" + allHits +
                " zoom=" + zoom.ToString("0.###") + " pan=" + NormalizedPosition +
                " context=" + context);
#endif
        }

        private void ReportDiagnosticThrottled(string phase, Vector2 screenPoint, int pointerId)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnosticTimer > 0f) return;
            diagnosticTimer = 0.25f;
            ReportDiagnostic(phase, screenPoint, pointerId);
#endif
        }

        private void OnDisable()
        {
            pointerDown = false;
            dragging = false;
            pinching = false;
            activePointerId = int.MinValue;
        }
    }
}
