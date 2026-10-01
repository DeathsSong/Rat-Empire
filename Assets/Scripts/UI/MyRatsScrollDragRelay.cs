using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// Gives the My Rats viewport explicit ownership of roster drag gestures.
    /// It forwards EventSystem drag callbacks to the nested ScrollRect, keeping
    /// row buttons clickable on taps while preventing parent-page/world input
    /// or a live content refresh from stealing a vertical swipe.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MyRatsScrollDragRelay : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ScrollRect scrollRect;
        private bool isDragging;
        private bool pointerSequenceActive;
        private int activePointerId = int.MinValue;
        private bool loggedFirstDrag;

        public bool IsDragging { get { return isDragging; } }
        public bool IsPointerSequenceActive { get { return pointerSequenceActive; } }

        public void Configure(ScrollRect target)
        {
            scrollRect = target;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (!CanForward(eventData)) return;
            pointerSequenceActive = true;
            activePointerId = eventData.pointerId;
            scrollRect.OnInitializePotentialDrag(eventData);
            LogRoute("pointer-down/drag-candidate", eventData, false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!CanForward(eventData)) return;
            scrollRect.OnBeginDrag(eventData);
            isDragging = eventData.button == PointerEventData.InputButton.Left;
            loggedFirstDrag = false;
            if (isDragging) LogRoute("begin-drag", eventData, false);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging || !CanForward(eventData)) return;
            scrollRect.OnDrag(eventData);
            if (!loggedFirstDrag)
            {
                loggedFirstDrag = true;
                LogRoute("first-drag-delta=" + eventData.delta, eventData, true);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!isDragging) return;
            if (scrollRect != null) scrollRect.OnEndDrag(eventData);
            LogRoute("end-drag", eventData, false);
            isDragging = false;
            pointerSequenceActive = false;
            activePointerId = int.MinValue;
            loggedFirstDrag = false;
        }

        private void Update()
        {
            if (!pointerSequenceActive || PointerIsHeld(activePointerId)) return;
            // Pointer-up for a tap is dispatched to the row's Button/relay,
            // not this viewport drag target. Poll only during a captured
            // sequence so a tap cannot leave the roster rebuild guard stuck.
            pointerSequenceActive = false;
            isDragging = false;
            activePointerId = int.MinValue;
        }

        private static bool PointerIsHeld(int pointerId)
        {
            if (pointerId < 0) return Input.GetMouseButton(0);
            for (int index = 0; index < Input.touchCount; index++)
            {
                Touch touch = Input.GetTouch(index);
                if (touch.fingerId == pointerId)
                    return touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
            }
            return false;
        }

        private bool CanForward(PointerEventData eventData)
        {
            return eventData != null && scrollRect != null &&
                scrollRect.isActiveAndEnabled && scrollRect.content != null &&
                scrollRect.viewport != null;
        }

        private void OnDisable()
        {
            isDragging = false;
            pointerSequenceActive = false;
            activePointerId = int.MinValue;
            loggedFirstDrag = false;
        }

        private void LogRoute(string phase, PointerEventData eventData, bool includeScrollPosition)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Debug.isDebugBuild || eventData == null) return;

            string firstHit = eventData.pointerCurrentRaycast.gameObject == null
                ? "none"
                : eventData.pointerCurrentRaycast.gameObject.name;
            string pointerPress = eventData.pointerPress == null ? "none" : eventData.pointerPress.name;
            string pointerDrag = eventData.pointerDrag == null ? "none" : eventData.pointerDrag.name;
            string scrollPosition = includeScrollPosition && scrollRect != null && scrollRect.content != null
                ? " contentY=" + scrollRect.content.anchoredPosition.y.ToString("0.0")
                : string.Empty;

            EventSystem eventSystem = EventSystem.current;
            string hitStack = "none";
            if (eventSystem != null)
            {
                var diagnosticEvent = new PointerEventData(eventSystem)
                {
                    position = eventData.position,
                    pointerId = eventData.pointerId,
                };
                var hits = new List<RaycastResult>(8);
                eventSystem.RaycastAll(diagnosticEvent, hits);
                if (hits.Count > 0)
                {
                    var names = new System.Text.StringBuilder();
                    int count = Mathf.Min(6, hits.Count);
                    for (int index = 0; index < count; index++)
                    {
                        if (index > 0) names.Append(" > ");
                        names.Append(hits[index].gameObject == null ? "null" : hits[index].gameObject.name)
                            .Append('[').Append(hits[index].module == null ? "no-module" : hits[index].module.name).Append(']');
                    }
                    hitStack = names.ToString();
                }
            }

            RatPresenter presenter = Object.FindObjectOfType<RatPresenter>();
            Debug.Log("[My Rats Scroll Route] phase=" + phase +
                " frame=" + Time.frameCount +
                " pointerId=" + eventData.pointerId +
                " firstHit=" + firstHit +
                " hitStack=" + hitStack +
                " pointerPress=" + pointerPress +
                " pointerDrag=" + pointerDrag +
                " relay=" + name +
                " scrollRect=" + (scrollRect == null ? "none" : scrollRect.name) +
                " viewport=" + (scrollRect == null || scrollRect.viewport == null ? "none" : scrollRect.viewport.name) +
                " pinkies=" + (presenter == null ? "none" : presenter.PinkieInputDiagnostic()) +
                scrollPosition);
#endif
        }
    }
}
