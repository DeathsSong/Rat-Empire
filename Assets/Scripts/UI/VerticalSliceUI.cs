using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// Runtime-built UI keeps this vertical slice dependency-free. The page is a
    /// natural-height ScrollRect with a fixed safe-area header; the world camera
    /// reserves that header and renders the habitat below it.
    /// </summary>
    public class VerticalSliceUI : MonoBehaviour
    {
        private const float ReferenceWidth = 540f;
        private const float ReferenceHeight = 960f;
        // Keep the 540 reference layout on phones, but allow the centered UI
        // column to grow on tablets and desktop instead of leaving the game
        // pinned to a narrow phone-width strip.
        private const float MaximumUiColumnWidth = 1120f;
        private const float MinimumSideMargin = 12f;
        // The header has a title/status strip, navigation strip, and exactly
        // three persistent simulation-speed controls. The world camera uses
        // the same reservation so the habitat never renders under the header.
        private const float HeaderHeight = 150f;
        private const float PageTopInset = HeaderHeight + 4f;
        // The profile is a fixed phone-safe frame: the live habitat remains
        // visible above it, while the opaque information surface is anchored
        // to the lower edge and grows upward when expanded.
        private const float RatProfileFrameHeight = 730f;
        // The collapsed profile must contain the live activity, stats,
        // reproductive countdown, and More Information control without
        // making the player scroll. The reproductive line can wrap on the
        // phone-width reference layout, so reserve that extra line here.
        private const float RatProfileCollapsedInformationHeight = 252f;
        private const float RatProfileExpandedInformationHeight = 406f;
        private const float RatProfileBottomPadding = 16f;
        private const float ModalMaximumWidth = 760f;
        private const int PageBottomSafePadding = 104;

        private GameBootstrap game;
        private Canvas canvas;
        private Canvas headerCanvas;
        private GraphicRaycaster headerRaycaster;
        private CanvasScaler canvasScaler;
        private RectTransform safeRoot;
        private RectTransform headerContent;
        private RectTransform welcomeOverlay;
        private RectTransform welcomeCard;
        private RectTransform settingsOverlay;
        private RectTransform settingsCard;
        private RectTransform settingsViewport;
        private ScrollRect settingsScroll;
        private Button keepScreenAwakeButton;
        private Text keepScreenAwakeStatusText;
        private RectTransform customMaleNamesList;
        private RectTransform customFemaleNamesList;
        private RectTransform customMaleNameInputRow;
        private RectTransform customFemaleNameInputRow;
        private InputField customMaleNameEntryInput;
        private InputField customFemaleNameEntryInput;
        private bool customMaleNameInputOpen;
        private bool customFemaleNameInputOpen;
        private Text customNamesStatusText;
        private Button clearCustomNamesButton;
        private Button confirmClearCustomNamesButton;
        private Button cancelClearCustomNamesButton;
        private readonly Dictionary<string, Button> alertPreferenceButtons = new Dictionary<string, Button>();
        private Text alertPreferencesStatusText;
        private RectTransform developerToolsOverlay;
        private RectTransform developerToolsViewport;
        private RectTransform developerToolsCard;
        private RectTransform ratAnimationShowcaseOverlay;
        private RectTransform ratAnimationShowcaseCard;
        private RectTransform eventLogOverlay;
        private RectTransform eventLogCard;
        private RectTransform eventLogContent;
        private RectTransform renameOverlay;
        private RectTransform renameCard;
        private InputField renameInput;
        private Text renameStatusText;
        private string renameRatId;
        private bool renameOpen;
        private RectTransform namingOverlay;
        private RectTransform namingCard;
        private RectTransform namingContent;
        private ScrollRect namingScroll;
        private Text namingStatusText;
        private bool namingOpen;
        private readonly Dictionary<string, InputField> pendingNamingInputs = new Dictionary<string, InputField>();
        private Text clockText;
        private Text walletText;
        private Text liveEventText;
        private Button eventLogToggleButton;
        private ScrollRect eventLogScroll;
        private ScrollRect pageScroll;
        private ScrollRect mateListScroll;
        private ScrollRect ratRosterScroll;
        private RectTransform ratRosterContent;
        private string lastRosterSortSignature;
        private ScrollRect storeRatListScroll;
        private ScrollRect familyTreeScroll;
        private ScrollRect ratProfileScroll;
        private RectTransform familyTreeViewport;
        private RectTransform familyTreeContent;
        private RectTransform familyTreeInputBlocker;
        private FamilyTreePanZoomController familyTreePanZoom;
        // The previous default was 1.0. A 0.72 scale starts the tree 28%
        // farther out while keeping the authored layout and connector geometry.
        private const float FamilyTreeInitialZoom = 0.72f;
        private float familyTreeZoom = FamilyTreeInitialZoom;
        private string familyTreeRenderedSubjectId;
        private bool familyTreeViewResetRequested = true;
        private Vector2 familyTreeNormalizedPosition = new Vector2(0.5f, 0.5f);
        private float familyTreeLastPinchDistance;
        private bool familyTreePinching;
        private bool familyTreeMouseGestureActive;
        private Vector2 familyTreeMouseGesturePosition;
        private int familyTreeTouchGestureFingerId = -1;
        private Vector2 familyTreeTouchGesturePosition;
        private float familyTreeGestureDiagnosticTimer;
        private RectTransform myRatsInputBlocker;
        private RectTransform content;
        private VerticalLayoutGroup contentLayout;
        private string lastSignature;
        private string lastFamilyTreeStructureSignature;
        private bool ready;
        private static Font builtInUiFont;
        private static bool uiFontWarningLogged;
        private const string BundledUiFontResourcePath = "UI/NotoSansJP-Regular";
        private int layoutScreenWidth = -1;
        private int layoutScreenHeight = -1;
        // World interaction is available as soon as the scene renders unless
        // this save is a genuinely new/reset colony waiting for its first
        // welcome acknowledgement.
        private bool welcomeOpen;
        private bool developerToolsOpen;
        private bool ratAnimationShowcaseOpen;
        private bool settingsOpen;
        private bool eventLogOpen;
        private string lastEventLogSignature;
        private enum StoreCategory
        {
            Buy,
            Sell,
            Euthanize,
        }
        private StoreCategory storeCategory = StoreCategory.Buy;
        private DirectUiClickRelay fallbackMouseRelay;
        private DirectUiClickRelay fallbackTouchRelay;
        private Vector2 fallbackMouseDownPosition;
        private Vector2 fallbackTouchDownPosition;
        private int fallbackTouchFingerId = -1;
        private readonly HashSet<int> activeUiPointerIds = new HashSet<int>();
        private int lastUiActionPointerId = int.MinValue;
        private int lastUiActionFrame = -1;
        private int deferredRefreshFrame = -1;
        private bool deferredRefreshPending;
        private bool deferredRefreshForce;
        private bool storePurchaseInProgress;
        private bool storePurchaseSucceeded;
        private string storePurchaseListingId;
        private readonly Dictionary<string, Button> storePurchaseButtons = new Dictionary<string, Button>();
        private RatPortraitPreview portraitPreview;
        private RatAnimationShowcase ratAnimationShowcase;
        private RawImage animationShowcasePreviewImage;
        private Text animationShowcaseCurrentText;
        private Text animationShowcaseStatusText;
        private Text animationShowcaseSpeedText;
        private Button animationShowcasePlayPauseButton;
        private readonly List<Text> animationShowcaseRowLabels = new List<Text>();
        private readonly List<Button> animationShowcaseRowButtons = new List<Button>();
        private readonly List<Action> liveTimedTextUpdates = new List<Action>();
        private const float LiveUiRefreshIntervalSeconds = 0.15f;
        private float liveUiRefreshTimer;
        private readonly Dictionary<MainPanel, Button> topNavigationButtons = new Dictionary<MainPanel, Button>();
        private readonly Dictionary<MainPanel, Image> topNavigationImages = new Dictionary<MainPanel, Image>();
        private readonly Dictionary<MainPanel, Text> topNavigationLabels = new Dictionary<MainPanel, Text>();
        private readonly Dictionary<int, Button> simulationSpeedButtons = new Dictionary<int, Button>();
        private readonly Dictionary<int, Image> simulationSpeedImages = new Dictionary<int, Image>();
        private readonly Dictionary<int, Text> simulationSpeedLabels = new Dictionary<int, Text>();
        private Text eventLogLabel;
        // A generated page control can be reached by both Unity's normal
        // Button/EventSystem path and InteractionManager's manual fallback
        // path in the same frame. Refreshing the page destroys the old relay,
        // so a per-relay guard alone is not enough: the replacement relay
        // could invoke the same action a second time. Keep one UI action per
        // frame across regenerated controls.
        private MainPanel activeMainPanel = MainPanel.None;
        private RosterSortField rosterSortField = RosterSortField.Name;
        private bool rosterSortAscending = true;
        private RosterSexFilter rosterSexFilter = RosterSexFilter.All;
        private string expandedMyRatsId;
        private string familyTreeSubjectId;
        private string profileMoreInformationRatId;
        private bool profileMoreInformationExpanded;
        // Profile arrows keep the same view open while changing only the
        // subject. The context is captured when the profile is opened so
        // My Rats uses its filtered/sorted list and habitat inspection uses
        // the current enclosure.
        private bool profileNavigationFromMyRats;
        private bool profileNavigationPreserveState;
        private string ratProfileScrollRatId;
        private float ratProfileScrollNormalized = 1f;
        private bool profileScrollResetRequested;
        private bool profileRefreshDeferred;
        private bool immediateRefreshRequested;
        private string lastProfileStructureSignature;
        private string liveProfileRatId;
        private Text liveProfileActivityText;
        private Text liveProfileAgeText;
        private Text liveProfileStatsText;
        private Text liveProfileHabitatText;
        private Text liveProfileReproductiveText;
        private RectTransform liveProfileActivityHistoryPanel;
        private LayoutElement liveProfileActivityHistoryLayout;
        private string liveProfileActivityHistorySignature;
        private string liveActivityHistoryRatId;
        private bool profileActivityHistoryDeferred;

        private enum MainPanel
        {
            None,
            Habitat,
            MyRats,
            Breeding,
            Store,
            Upgrades,
            FamilyTree,
            Settings,
            DeveloperTools,
        }

        private enum RosterSortField
        {
            Name,
            Age,
            Size,
            Health,
            Fertility,
            Sex,
            Pregnancy,
            Breeding,
            // Retained only so older in-memory callers can still compile. The
            // visible Generation sort was renamed to Breeding.
            Generation,
        }

        private enum RosterSexFilter
        {
            All,
            Males,
            Females,
        }

        public void Initialize(GameBootstrap owner)
        {
            game = owner;
            LoadRosterPreferences();
            portraitPreview = GetComponent<RatPortraitPreview>();
            if (portraitPreview == null) portraitPreview = gameObject.AddComponent<RatPortraitPreview>();
            portraitPreview.Configure(game.RatVisualFactory);
            ratAnimationShowcase = GetComponent<RatAnimationShowcase>();
            if (ratAnimationShowcase == null) ratAnimationShowcase = gameObject.AddComponent<RatAnimationShowcase>();
            ratAnimationShowcase.Configure(game.RatVisualFactory, FindAnimationShowcaseSample());
            if (canvas == null)
                BuildShell();
            else
                EnsureHeaderNavigationReady();
            welcomeOpen = game.WelcomePopupPending;
            if (!welcomeOpen && game.HasPendingLitterNaming) OpenPendingLitterNaming();
            GrowthSystem.SetSimulationPaused(welcomeOpen || game.HasPendingLitterNaming);
            ready = true;
            Refresh(true);
        }

        public bool IsWelcomeOpen { get { return welcomeOpen; } }

        /// <summary>
        /// Newborn naming is a real modal state, not just a visual panel. The
        /// bootstrap uses this value to keep the authoritative simulation
        /// paused until the naming transaction has been approved.
        /// </summary>
        public bool IsPendingLitterNamingOpen { get { return namingOpen; } }

        /// <summary>
        /// Page controls live inside a nested ScrollRect and are generated at
        /// runtime. On some Unity 2022 Android/editor input paths the
        /// ScrollRect receives the pointer but does not complete the child's
        /// Button click. Keep the standard EventSystem path for the fixed
        /// header, while using the final screen-position hit for generated
        /// page controls so touch and mouse activation remain deterministic.
        /// </summary>
        public void ProcessFallbackUiInput()
        {
            if (!ready) return;
            if (Input.touchCount > 0)
            {
                for (int index = 0; index < Input.touchCount; index++)
                {
                    Touch touch = Input.GetTouch(index);
                    if (touch.phase == TouchPhase.Began)
                    {
                        fallbackTouchFingerId = touch.fingerId;
                        fallbackTouchDownPosition = touch.position;
                        fallbackTouchRelay = FindFallbackRelay(touch.position);
                        LogNavigationDiagnostics("touch-down", touch.position, fallbackTouchRelay);
                        RegisterUiPointerDown(touch.fingerId);
                    }
                    else if (touch.phase == TouchPhase.Ended && touch.fingerId == fallbackTouchFingerId)
                    {
                        DirectUiClickRelay relay = fallbackTouchRelay;
                        bool moved = Vector2.Distance(fallbackTouchDownPosition, touch.position) > 24f;
                        fallbackTouchRelay = null;
                        fallbackTouchFingerId = -1;
                        RegisterUiPointerUp(touch.fingerId);
                        if (!moved)
                        {
                            // Birth/pinkie presentation can trigger a page
                            // rebuild between pointer-down and pointer-up.
                            // Never keep using the destroyed relay captured on
                            // pointer-down; resolve the current control first.
                            DirectUiClickRelay currentRelay = FindFallbackRelay(touch.position);
                            if (currentRelay != null) relay = currentRelay;
                            if (relay != null && relay.IsFallbackInteractable && relay.ContainsScreenPoint(touch.position))
                                relay.InvokeFallback(touch.fingerId);
                        }
                    }
                    else if (touch.phase == TouchPhase.Canceled && touch.fingerId == fallbackTouchFingerId)
                    {
                        fallbackTouchRelay = null;
                        fallbackTouchFingerId = -1;
                        RegisterUiPointerUp(touch.fingerId);
                    }
                }
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                fallbackMouseDownPosition = Input.mousePosition;
                fallbackMouseRelay = FindFallbackRelay(fallbackMouseDownPosition);
                LogNavigationDiagnostics("mouse-down", fallbackMouseDownPosition, fallbackMouseRelay);
                RegisterUiPointerDown(-1);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                DirectUiClickRelay relay = fallbackMouseRelay;
                bool moved = Vector2.Distance(fallbackMouseDownPosition, Input.mousePosition) > 8f;
                fallbackMouseRelay = null;
                RegisterUiPointerUp(-1);
                if (!moved)
                {
                    // Re-resolve after any birth/pinkie-driven rebuild. This
                    // keeps a valid page button clickable even if its old
                    // DirectUiClickRelay was destroyed while the pointer was
                    // held down.
                    DirectUiClickRelay currentRelay = FindFallbackRelay(Input.mousePosition);
                    if (currentRelay != null) relay = currentRelay;
                    if (relay != null && relay.IsFallbackInteractable && relay.ContainsScreenPoint(Input.mousePosition))
                        relay.InvokeFallback(-1);
                }
            }
        }

        /// <summary>
        /// Clears the manual UI pointer capture used by generated controls.
        /// Navigation can replace the page hierarchy while a touch or mouse
        /// sequence is still finishing; retaining a relay from the old page
        /// would make the next tap target a destroyed or unrelated control.
        /// </summary>
        public void ClearUiPointerState()
        {
            fallbackMouseRelay = null;
            fallbackTouchRelay = null;
            fallbackMouseDownPosition = Vector2.zero;
            fallbackTouchDownPosition = Vector2.zero;
            fallbackTouchFingerId = -1;
            activeUiPointerIds.Clear();
        }

        private void RegisterUiPointerDown(int pointerId)
        {
            activeUiPointerIds.Add(pointerId);
        }

        private void RegisterUiPointerUp(int pointerId)
        {
            activeUiPointerIds.Remove(pointerId);
        }

        private bool CanInvokeUiAction(int pointerId)
        {
            // This is the final one-action-per-pointer guard. The relay also
            // guards its own EventSystem callbacks, but this owner-level guard
            // survives a page rebuild that replaces the relay GameObject.
            if (lastUiActionFrame == Time.frameCount) return false;
            if (lastUiActionPointerId == pointerId && pointerId != int.MinValue &&
                lastUiActionFrame >= Time.frameCount - 1)
                return false;
            return true;
        }

        private void RecordUiAction(int pointerId)
        {
            lastUiActionPointerId = pointerId;
            lastUiActionFrame = Time.frameCount;
        }

        public void RequestRefreshAfterPointerRelease(bool force)
        {
            deferredRefreshPending = true;
            deferredRefreshForce |= force;
            // Always wait one rendered frame after pointer-up. This prevents
            // the new listing hierarchy from receiving the pointer sequence
            // that purchased the previous listing.
            deferredRefreshFrame = Mathf.Max(deferredRefreshFrame, Time.frameCount + 1);
        }

        public bool BeginStorePurchaseAttempt(string listingId)
        {
            if (storePurchaseInProgress) return false;
            storePurchaseInProgress = true;
            storePurchaseSucceeded = false;
            storePurchaseListingId = listingId;
            Button button;
            if (!string.IsNullOrEmpty(listingId) && storePurchaseButtons.TryGetValue(listingId, out button) && button != null)
                button.interactable = false;
            return true;
        }

        public void CompleteStorePurchaseAttempt(string listingId, bool succeeded)
        {
            if (!storePurchaseInProgress || storePurchaseListingId != listingId) return;
            if (!succeeded)
            {
                Button button;
                if (!string.IsNullOrEmpty(listingId) && storePurchaseButtons.TryGetValue(listingId, out button) && button != null)
                    button.interactable = true;
                storePurchaseInProgress = false;
                storePurchaseListingId = null;
                storePurchaseSucceeded = false;
                return;
            }
            // Keep this transaction active until the pointer sequence has
            // released and the deferred Store rebuild runs. This prevents a
            // replacement listing button from inheriting the current tap.
            storePurchaseSucceeded = true;
        }

        private DirectUiClickRelay FindFallbackRelay(Vector2 screenPoint)
        {
            DirectUiClickRelay[] relays = GetComponentsInChildren<DirectUiClickRelay>(false);
            DirectUiClickRelay best = null;
            int bestDepth = int.MinValue;
            bool pointerOverHeader = headerContent != null &&
                RectTransformUtility.RectangleContainsScreenPoint(headerContent, screenPoint, null);
            for (int index = 0; index < relays.Length; index++)
            {
                DirectUiClickRelay relay = relays[index];
                if (relay == null || !relay.IsFallbackInteractable || !relay.ContainsScreenPoint(screenPoint)) continue;
                if (IsModalOverlayOpen && !IsRelayInsideActiveModal(relay)) continue;
                int depth = 0;
                Transform current = relay.transform;
                while (current != null && current != transform)
                {
                    depth++;
                    current = current.parent;
                }
                // The profile is generated later in the page hierarchy and
                // can extend beyond its visual frame while layout is settling.
                // If the pointer is over the header column, the header control
                // must win even if an oversized profile graphic also contains
                // that screen point. This mirrors the dedicated header Canvas
                // used by the normal EventSystem raycaster below.
                if (pointerOverHeader && relay.transform.IsChildOf(headerContent))
                    depth += 100000;
                if (best == null || depth >= bestDepth)
                {
                    best = relay;
                    bestDepth = depth;
                }
            }
            return best;
        }

        // InteractionManager calls this before attempting a world raycast.
        // It is deliberately independent of EventSystem dispatch so page
        // controls still work when a ScrollRect consumes the pointer sequence.
        public bool TryInvokePageControlAt(Vector2 screenPoint)
        {
            if (!ready) return false;
            DirectUiClickRelay relay = FindFallbackRelay(screenPoint);
            if (relay == null)
                relay = FindPageControlFromEventSystem(screenPoint);
            LogNavigationDiagnostics("page-release", screenPoint, relay);
            if (relay == null) return IsModalOverlayOpen;
            relay.InvokeFallback(int.MinValue);
            return true;
        }

        /// <summary>
        /// Finds a current page Button through GraphicRaycaster results when a
        /// generated relay was replaced during the same touch sequence. The
        /// method deliberately ignores the two world-input shields and never
        /// invokes a button that is not active/interactable.
        /// </summary>
        private DirectUiClickRelay FindPageControlFromEventSystem(Vector2 screenPoint)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return null;

            var eventData = new PointerEventData(eventSystem)
            {
                position = screenPoint,
                pointerId = -1,
            };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(eventData, results);
            for (int index = 0; index < results.Count; index++)
            {
                GameObject hit = results[index].gameObject;
                if (hit == null || !hit.activeInHierarchy ||
                    (myRatsInputBlocker != null && hit.transform.IsChildOf(myRatsInputBlocker)) ||
                    (familyTreeInputBlocker != null && hit.transform.IsChildOf(familyTreeInputBlocker))) continue;

                Button button = hit.GetComponentInParent<Button>();
                if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) continue;
                DirectUiClickRelay relay = button.GetComponent<DirectUiClickRelay>();
                if (relay != null && relay.IsFallbackInteractable) return relay;

                // Every generated button normally has a relay. If a legacy
                // or optional control does not, keep it in the UI path rather
                // than letting the same tap fall through to a pinkie/world
                // physics raycast. AddButtonTo supplies the relay on all
                // current controls; this branch is a guarded compatibility
                // fallback for older saved/runtime hierarchies.
                if (CanInvokeUiAction(int.MinValue))
                {
                    lastUiActionFrame = Time.frameCount;
                    lastUiActionPointerId = int.MinValue;
                    button.onClick.Invoke();
                }
                return null;
            }
            return null;
        }

        /// <summary>
        /// Development-only navigation/input diagnostics. The fixed header is
        /// intentionally kept out of the generated page hierarchy, so this
        /// reports both the EventSystem's top hit and the relay selected by the
        /// manual WebGL/mobile fallback. It is silent in release builds.
        /// </summary>
        private void LogNavigationDiagnostics(string context, Vector2 screenPoint, DirectUiClickRelay fallbackRelay)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (canvas == null) return;

            string eventSystemHit = "none";
            string raycastHits = "none";
            bool pointerOverEventSystemUi = false;
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                pointerOverEventSystemUi = eventSystem.IsPointerOverGameObject();
                var eventData = new PointerEventData(eventSystem)
                {
                    position = screenPoint,
                    pointerId = -1,
                };
                var results = new List<RaycastResult>();
                eventSystem.RaycastAll(eventData, results);
                if (results.Count > 0 && results[0].gameObject != null)
                    eventSystemHit = results[0].gameObject.name;
                if (results.Count > 0)
                {
                    var hitNames = new System.Text.StringBuilder();
                    int hitCount = Mathf.Min(8, results.Count);
                    for (int index = 0; index < hitCount; index++)
                    {
                        RaycastResult result = results[index];
                        if (index > 0) hitNames.Append(" > ");
                        if (result.gameObject == null)
                        {
                            hitNames.Append("null");
                            continue;
                        }
                        Graphic graphic = result.gameObject.GetComponent<Graphic>();
                        CanvasGroup group = result.gameObject.GetComponentInParent<CanvasGroup>();
                        hitNames.Append(result.gameObject.name)
                            .Append("[").Append(result.module == null ? "no-module" : result.module.name).Append("]")
                            .Append(graphic != null && graphic.raycastTarget ? ":ray" : ":no-ray");
                        if (group != null)
                            hitNames.Append("{cg blocks=").Append(group.blocksRaycasts)
                                .Append(" interactable=").Append(group.interactable).Append('}');
                    }
                    raycastHits = hitNames.ToString();
                }
            }

            var activeOverlays = new List<string>();
            AddActiveOverlayDiagnostic(activeOverlays, "welcome", welcomeOverlay, welcomeOpen);
            AddActiveOverlayDiagnostic(activeOverlays, "settings", settingsOverlay, settingsOpen);
            AddActiveOverlayDiagnostic(activeOverlays, "developer", developerToolsOverlay, developerToolsOpen);
            AddActiveOverlayDiagnostic(activeOverlays, "animation", ratAnimationShowcaseOverlay, ratAnimationShowcaseOpen);
            AddActiveOverlayDiagnostic(activeOverlays, "events", eventLogOverlay, eventLogOpen);
            AddActiveOverlayDiagnostic(activeOverlays, "rename", renameOverlay, renameOpen);
            AddActiveOverlayDiagnostic(activeOverlays, "naming", namingOverlay, namingOpen);
            string overlayState = activeOverlays.Count == 0 ? "none" : string.Join(",", activeOverlays.ToArray());
            string relayName = fallbackRelay == null ? "none" : fallbackRelay.gameObject.name;
            string headerState = BuildHeaderDiagnosticState();
            RatPresenter presenter = FindObjectOfType<RatPresenter>();
            Debug.Log("[Rat UI Navigation] " + context +
                " pointer=" + screenPoint +
                " pointerOverUi=" + pointerOverEventSystemUi +
                " eventSystemHit=" + eventSystemHit +
                " raycastHits=" + raycastHits +
                " fallbackHit=" + relayName +
                " panel=" + activeMainPanel +
                " overlays=" + overlayState +
                " myRatsBlocker=" + IsBlockerActive(myRatsInputBlocker) +
                " familyTreeBlocker=" + IsBlockerActive(familyTreeInputBlocker) +
                " inputLayer=" + BuildInputLayerDiagnostic() +
                " pinkies=" + (presenter == null ? "none" : presenter.PinkieInputDiagnostic()) +
                " header=" + headerState);
#endif
        }

        private string BuildInputLayerDiagnostic()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var state = new System.Text.StringBuilder();
            AppendInputObjectDiagnostic(state, myRatsInputBlocker, "myRats");
            AppendInputObjectDiagnostic(state, familyTreeInputBlocker, "familyTree");
            AppendInputObjectDiagnostic(state, pageScroll == null ? null : pageScroll.transform as RectTransform, "page");
            AppendInputObjectDiagnostic(state, namingOverlay, "naming");
            AppendInputObjectDiagnostic(state, settingsOverlay, "settings");
            return state.Length == 0 ? "none" : state.ToString();
#else
            return string.Empty;
#endif
        }

        private static void AppendInputObjectDiagnostic(System.Text.StringBuilder state, RectTransform rect, string label)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (rect == null) return;
            if (state.Length > 0) state.Append('|');
            CanvasGroup group = rect.GetComponent<CanvasGroup>();
            state.Append(label).Append("=")
                .Append(rect.gameObject.activeInHierarchy ? 'A' : 'I')
                .Append("#").Append(rect.GetSiblingIndex())
                .Append("@ray=").Append(rect.GetComponent<Graphic>() != null && rect.GetComponent<Graphic>().raycastTarget);
            if (group != null)
                state.Append("/cg=").Append(group.blocksRaycasts).Append(',').Append(group.interactable);
#endif
        }

        private static void AddActiveOverlayDiagnostic(List<string> output, string label, RectTransform overlay, bool state)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (state || (overlay != null && overlay.gameObject.activeInHierarchy)) output.Add(label);
#endif
        }

        private static bool IsBlockerActive(RectTransform blocker)
        {
            return blocker != null && blocker.gameObject.activeInHierarchy;
        }

        private string BuildHeaderDiagnosticState()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (topNavigationButtons == null || topNavigationButtons.Count == 0) return "none";
            var state = new System.Text.StringBuilder();
            foreach (var entry in topNavigationButtons)
            {
                if (state.Length > 0) state.Append('|');
                Button button = entry.Value;
                state.Append(entry.Key).Append(':')
                    .Append(button != null && button.gameObject.activeInHierarchy ? 'A' : 'I')
                    .Append(button != null && button.IsInteractable() ? 'E' : 'D')
                    .Append('@').Append(button == null ? -1 : button.transform.GetSiblingIndex());
            }
            state.Append(";canvas=").Append(headerCanvas == null ? -1 : headerCanvas.sortingOrder);
            return state.ToString();
#else
            return string.Empty;
#endif
        }

        /// <summary>
        /// A world rat tap can rebuild the profile beneath the pointer during
        /// the same frame. Suppress generated UI relays for that frame so the
        /// newly-created Move/Sell/Breed controls cannot consume the original
        /// selection tap as an action.
        /// </summary>
        public void SuppressGeneratedUiActionsThisFrame()
        {
            lastUiActionFrame = Time.frameCount;
        }

        public bool IsModalOverlayOpen
        {
            get
            {
                return welcomeOpen || settingsOpen || developerToolsOpen || ratAnimationShowcaseOpen || eventLogOpen || namingOpen || renameOpen ||
                    (activeMainPanel != MainPanel.None && activeMainPanel != MainPanel.Habitat &&
                     !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                     !ratAnimationShowcaseOpen && !eventLogOpen && !namingOpen && !renameOpen);
            }
        }

        /// <summary>
        /// The profile is a live inspection surface over the habitat. Expose
        /// its viewport to the manual input path so a vertical drag can never
        /// be mistaken for a habitat page swipe.
        /// </summary>
        public bool IsPointerOverRatProfileScroll(Vector2 screenPoint)
        {
            if (ratProfileScroll == null || !ratProfileScroll.isActiveAndEnabled) return false;
            RectTransform viewport = ratProfileScroll.viewport != null
                ? ratProfileScroll.viewport
                : ratProfileScroll.GetComponent<RectTransform>();
            return viewport != null && RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPoint, null);
        }

        private bool IsRatProfileScrollMoving()
        {
            if (ratProfileScroll == null || !ratProfileScroll.isActiveAndEnabled) return false;
            if (Mathf.Abs(ratProfileScroll.velocity.y) > 1.5f) return true;
            if (Input.GetMouseButton(0) && IsPointerOverRatProfileScroll(Input.mousePosition)) return true;
            for (int index = 0; index < Input.touchCount; index++)
            {
                Touch touch = Input.GetTouch(index);
                if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled &&
                    IsPointerOverRatProfileScroll(touch.position)) return true;
            }
            return false;
        }

        private void CaptureRatProfileScrollPosition()
        {
            if (ratProfileScroll == null || !ratProfileScroll.isActiveAndEnabled) return;
            if (string.IsNullOrEmpty(ratProfileScrollRatId)) return;
            ratProfileScrollNormalized = Mathf.Clamp01(ratProfileScroll.verticalNormalizedPosition);
        }

        private string GetProfileStructureSignature(RatData rat)
        {
            if (game == null || rat == null) return string.Empty;
            string reason;
            bool canBreed = BreedingSystem.IsBreedEligible(game.Save, rat, game.GameTime, out reason);
            PregnancyData pregnancy = FindPregnancyForFemale(rat);
            string pregnancyKey = pregnancy == null
                ? string.Empty
                : pregnancy.id + ":" + pregnancy.status + ":" + pregnancy.dueAt;
            string phenotypeKey = rat.phenotype == null
                ? string.Empty
                : (rat.phenotype.coatColorLabel ?? string.Empty) + ":" + (rat.phenotype.markingsLabel ?? string.Empty);
            return game.UiStructureSignature + "|profile:" + rat.id + ":" +
                (rat.name ?? string.Empty) + ":" + rat.stage + ":" + rat.enclosure + ":" +
                rat.reproductiveState + ":" + rat.nursing + ":" + rat.pregnancyId + ":" +
                pregnancyKey + ":" + phenotypeKey + ":breed=" + canBreed + ":" +
                (reason ?? string.Empty) + ":more=" + profileMoreInformationExpanded;
        }

        private void RefreshLiveRatProfile()
        {
            if (game == null) return;
            RatData rat = string.IsNullOrEmpty(liveProfileRatId)
                ? null
                : BreedingSystem.FindHistoricalRat(game.Save, liveProfileRatId);
            RatData historyRat = string.IsNullOrEmpty(liveActivityHistoryRatId)
                ? rat
                : BreedingSystem.FindHistoricalRat(game.Save, liveActivityHistoryRatId);
            if (rat == null && historyRat == null) return;

            if (rat != null && liveProfileActivityText != null)
                liveProfileActivityText.text = "Current activity: " + game.CurrentRatActivityLabel(rat);
            if (rat != null && liveProfileAgeText != null)
                liveProfileAgeText.text = "Age: " + GrowthSystem.FormatAge(rat.ageDays);
            if (rat != null && liveProfileStatsText != null)
            {
                TraitData traits = rat.traits ?? new TraitData();
                liveProfileStatsText.text = "Size " + traits.size.ToString("0") +
                    "  •  Health " + traits.health.ToString("0") +
                    "  •  Fertility " + traits.fertility.ToString("0");
            }
            if (rat != null && liveProfileHabitatText != null)
                liveProfileHabitatText.text = "Current habitat: " + EnclosureSystem.Label(rat.enclosure);
            if (rat != null && liveProfileReproductiveText != null)
                liveProfileReproductiveText.text = "Reproductive state: " + ReproductiveStateLabel(rat);

            string historySignature = BuildRatActivityHistorySignature(historyRat);
            if (historyRat != null && liveProfileActivityHistoryPanel != null &&
                historySignature != liveProfileActivityHistorySignature)
            {
                if (IsRatProfileScrollMoving())
                {
                    profileActivityHistoryDeferred = true;
                    return;
                }

                RebuildRatActivityHistoryRows(liveProfileActivityHistoryPanel, historyRat);
                liveProfileActivityHistorySignature = historySignature;
                profileActivityHistoryDeferred = false;
            }
        }

        private static string BuildRatActivityHistorySignature(RatData rat)
        {
            if (rat == null || rat.activity == null || rat.activity.history == null || rat.activity.history.Count == 0)
                return string.Empty;
            int count = Mathf.Min(RatActivitySystem.MaximumHistoryEntries, rat.activity.history.Count);
            string signature = count.ToString();
            for (int index = 0; index < count; index++)
            {
                RatActivityEntryData entry = rat.activity.history[index];
                if (entry == null) continue;
                signature += "|" + entry.gameTimeMs + ":" + entry.activityKey + ":" + entry.message;
            }
            return signature;
        }

        private void RebuildRatActivityHistoryRows(RectTransform historyPanel, RatData rat)
        {
            if (historyPanel == null || rat == null) return;
            float previousNormalized = ratProfileScroll == null
                ? 1f
                : Mathf.Clamp01(ratProfileScroll.verticalNormalizedPosition);
            Vector2 previousContentPosition = ratProfileScroll == null || ratProfileScroll.content == null
                ? Vector2.zero
                : ratProfileScroll.content.anchoredPosition;

            for (int index = historyPanel.childCount - 1; index >= 0; index--)
            {
                GameObject oldRow = historyPanel.GetChild(index).gameObject;
                oldRow.SetActive(false);
                Destroy(oldRow);
            }

            int count = rat.activity == null || rat.activity.history == null
                ? 0
                : Mathf.Min(RatActivitySystem.MaximumHistoryEntries, rat.activity.history.Count);
            if (liveProfileActivityHistoryLayout != null)
            {
                liveProfileActivityHistoryLayout.minHeight = 28f;
                liveProfileActivityHistoryLayout.preferredHeight = 28f + count * 19f;
            }

            if (count == 0)
            {
                AddText(historyPanel, "No recent activity recorded.", 11,
                    new Color(0.70f, 0.78f, 0.74f), TextAnchor.UpperLeft);
            }
            else
            {
                for (int index = 0; index < count; index++)
                {
                    RatActivityEntryData entry = rat.activity.history[index];
                    if (entry == null) continue;
                    AddText(historyPanel, game.FormatRatActivityEntry(entry), 11,
                        new Color(0.80f, 0.87f, 0.83f), TextAnchor.UpperLeft);
                }
            }

            // The profile content is intentionally kept in place. Reapply the
            // same viewport position after the small history sub-tree changes
            // instead of allowing a new row to snap the whole ScrollRect to its
            // default top position.
            Canvas.ForceUpdateCanvases();
            if (ratProfileScroll != null && ratProfileScroll.content != null)
            {
                ratProfileScroll.content.anchoredPosition = previousContentPosition;
                // At the top there is no useful anchored offset to restore;
                // explicitly preserving the normalized value also lets Unity
                // clamp a newly shorter history cleanly.
                if (Mathf.Abs(previousContentPosition.y) < 0.001f)
                    ratProfileScroll.verticalNormalizedPosition = previousNormalized;
            }
        }

        private bool IsRelayInsideActiveModal(DirectUiClickRelay relay)
        {
            if (relay == null) return false;
            // The fixed header is the one navigation layer that remains
            // reachable above ordinary page/modal content. Critical first-run
            // and newborn-naming dialogs intentionally keep it blocked until
            // their required acknowledgement is complete.
            if (headerContent != null && relay.transform.IsChildOf(headerContent) &&
                !welcomeOpen && !namingOpen)
                return true;
            RectTransform activeOverlay = null;
            if (welcomeOpen) activeOverlay = welcomeOverlay;
            else if (settingsOpen) activeOverlay = settingsOverlay;
            else if (developerToolsOpen) activeOverlay = developerToolsOverlay;
            else if (ratAnimationShowcaseOpen) activeOverlay = ratAnimationShowcaseOverlay;
            else if (eventLogOpen) activeOverlay = eventLogOverlay;
            else if (namingOpen) activeOverlay = namingOverlay;
            else if (renameOpen) activeOverlay = renameOverlay;
            if (activeOverlay == null && activeMainPanel == MainPanel.MyRats &&
                !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                !ratAnimationShowcaseOpen && !eventLogOpen && !namingOpen && !renameOpen)
            {
                // My Rats is a modal page from the world's point of view, but
                // its own generated controls still need the normal UI fallback
                // path when EventSystem dispatch is unavailable on Android.
                return (pageScroll != null && relay.transform.IsChildOf(pageScroll.transform)) ||
                    (headerContent != null && relay.transform.IsChildOf(headerContent));
            }
            if (activeOverlay == null && activeMainPanel == MainPanel.FamilyTree &&
                !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                !ratAnimationShowcaseOpen && !eventLogOpen && !namingOpen && !renameOpen)
            {
                return (pageScroll != null && relay.transform.IsChildOf(pageScroll.transform)) ||
                    (headerContent != null && relay.transform.IsChildOf(headerContent));
            }
            if (activeOverlay == null && activeMainPanel != MainPanel.None &&
                activeMainPanel != MainPanel.Habitat &&
                !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                !ratAnimationShowcaseOpen && !eventLogOpen && !namingOpen && !renameOpen)
            {
                // Store, Upgrades, and the breeding page are page-owned
                // interaction surfaces too. A missed/generated relay must
                // not turn a page-button tap into a habitat tap that closes
                // the page underneath it.
                return (pageScroll != null && relay.transform.IsChildOf(pageScroll.transform)) ||
                    (headerContent != null && relay.transform.IsChildOf(headerContent));
            }
            return activeOverlay != null && relay.transform.IsChildOf(activeOverlay);
        }

        public void Refresh(bool force)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long performanceSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.UiRefresh);
            try { RefreshCore(force); }
            finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.UiRefresh, performanceSample); }
#else
            RefreshCore(force);
#endif
        }

        private void RefreshCore(bool force)
        {
            if (!ready || game == null) return;
            // A birth can finish between two normal UI refreshes. Reconcile
            // the persisted naming queue before rebuilding page content so a
            // stale or hidden modal can never keep a full-screen blocker alive
            // without its controls, and a pending litter can never be shown
            // without its naming surface.
            if (namingOpen && !game.HasPendingLitterNaming)
            {
                namingOpen = false;
                ClearUiPointerState();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                SetOverlayVisibility();
            }
            else if (!welcomeOpen && game.HasPendingLitterNaming && !namingOpen)
            {
                OpenPendingLitterNaming();
            }
            bool immediateRefresh = immediateRefreshRequested;
            immediateRefreshRequested = false;
            RefreshHeader();
            if (game.BreedingOpen && activeMainPanel == MainPanel.None)
            {
                activeMainPanel = MainPanel.Breeding;
            }
            RefreshTopNavigationState();
            if (storePurchaseInProgress && activeMainPanel == MainPanel.Store && storeCategory == StoreCategory.Buy)
            {
                RequestRefreshAfterPointerRelease(force);
                return;
            }
            string signature = game.UiSignature;
            if (!force && signature == lastSignature) return;

            // The full UI signature includes the live clock so profiles and
            // countdown labels can refresh. Family Tree geometry has no
            // clock-driven content, however. Rebuilding it on every clock
            // tick destroys the active viewport/controller in the middle of
            // a drag, which makes zoom appear to work while panning fails.
            // Only rebuild the tree when its subject or family/reproductive
            // structure actually changes.
            if (!force && activeMainPanel == MainPanel.FamilyTree &&
                familyTreePanZoom != null &&
                string.Equals(game.UiStructureSignature, lastFamilyTreeStructureSignature,
                    StringComparison.Ordinal))
            {
                lastSignature = signature;
                UpdateFamilyTreeZoomInput();
                return;
            }

            // Do not destroy the profile ScrollRect while Unity is processing
            // a drag or its inertial tail. Defer the data refresh until the
            // gesture settles; otherwise a harmless clock/activity update can
            // replace the content under the user's finger.
            bool sameProfile = ratProfileScroll != null &&
                game.SelectedRat != null &&
                ratProfileScrollRatId == game.SelectedRat.id;

            // Clock ticks, activity transitions, and countdowns are live
            // values. They must update the existing profile hierarchy rather
            // than replacing the ScrollRect while the player is reading it.
            // Structural changes (a different rat, stage/enclosure change,
            // pregnancy/action state, or More Information toggle) still take
            // the normal rebuild path below.
            if (sameProfile &&
                string.Equals(GetProfileStructureSignature(game.SelectedRat), lastProfileStructureSignature,
                    StringComparison.Ordinal))
            {
                lastSignature = signature;
                RefreshLiveRatProfile();
                RefreshTopNavigationState();
                return;
            }
            if (sameProfile && IsRatProfileScrollMoving() && !immediateRefresh)
            {
                profileRefreshDeferred = true;
                return;
            }

            profileRefreshDeferred = false;
            string profileTargetId = game.SelectedRat != null
                ? game.SelectedRat.id
                : familyTreeSubjectId;
            bool profileTargetChanged = ratProfileScroll != null &&
                !string.IsNullOrEmpty(profileTargetId) &&
                !string.Equals(ratProfileScrollRatId, profileTargetId, StringComparison.Ordinal);
            if ((profileScrollResetRequested || profileTargetChanged) && !profileNavigationPreserveState)
            {
                // Only an intentional expansion or a different profile gets
                // a fresh view. Live clock/activity/countdown refreshes keep
                // the user's current position in the nested ScrollRect.
                ratProfileScrollNormalized = 1f;
                profileScrollResetRequested = false;
            }
            else
            {
                CaptureRatProfileScrollPosition();
            }
            float previousNormalized = string.IsNullOrEmpty(lastSignature) ? 1f : pageScroll.verticalNormalizedPosition;
            float previousMateNormalized = mateListScroll == null ? 1f : mateListScroll.verticalNormalizedPosition;
            float previousRosterNormalized = ratRosterScroll == null ? 1f : ratRosterScroll.verticalNormalizedPosition;
            float previousStoreNormalized = storeRatListScroll == null ? 1f : storeRatListScroll.verticalNormalizedPosition;
            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                long rebuildSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.UiRebuildAndLayout);
                try { RebuildContent(); }
                finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.UiRebuildAndLayout, rebuildSample); }
#else
                RebuildContent();
#endif
                // The one-shot flag is consumed by AddRatProfile during this
                // rebuild. Future clock/activity refreshes use the ordinary
                // profile-preservation path without unexpectedly carrying the
                // navigation transition into another rebuild.
                profileNavigationPreserveState = false;
                if (developerToolsOpen) RebuildDeveloperToolsContent();
                if (ratAnimationShowcaseOpen) RefreshAnimationShowcasePanel();
            }
            catch (Exception exception)
            {
                HandlePageRebuildFailure(exception);
            }
            lastSignature = signature;
            lastFamilyTreeStructureSignature = activeMainPanel == MainPanel.FamilyTree
                ? game.UiStructureSignature
                : null;
            if (ratProfileScroll != null && game.SelectedRat != null && ratProfileScrollRatId == game.SelectedRat.id)
                lastProfileStructureSignature = GetProfileStructureSignature(game.SelectedRat);
            else
                lastProfileStructureSignature = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long layoutSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.UiRebuildAndLayout);
            try { Canvas.ForceUpdateCanvases(); }
            finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.UiRebuildAndLayout, layoutSample); }
#else
            Canvas.ForceUpdateCanvases();
#endif
            // Rebuilds preserve the user's current page position. No focus or
            // viewport jump is requested by a selection or clock refresh.
            pageScroll.verticalNormalizedPosition = previousNormalized;
            if (mateListScroll != null) mateListScroll.verticalNormalizedPosition = previousMateNormalized;
            if (ratRosterScroll != null) ratRosterScroll.verticalNormalizedPosition = previousRosterNormalized;
            if (storeRatListScroll != null) storeRatListScroll.verticalNormalizedPosition = previousStoreNormalized;
            if (ratProfileScroll != null && ratProfileScrollRatId == (game.SelectedRat == null ? string.Empty : game.SelectedRat.id))
                ratProfileScroll.verticalNormalizedPosition = ratProfileScrollNormalized;
            RefreshLiveRatProfile();
            // Even when a page-specific factory throws, the fixed header is
            // independent of that hierarchy and must remain a usable escape
            // hatch to every top-level tab.
            EnsureHeaderNavigationReady();
            ReassertPageInputLayerOrder();
            RefreshTopNavigationState();
        }

        private void HandlePageRebuildFailure(Exception exception)
        {
            Debug.LogException(exception);
            try
            {
                if (content != null)
                {
                    for (int index = content.childCount - 1; index >= 0; index--)
                    {
                        GameObject child = content.GetChild(index).gameObject;
                        child.SetActive(false);
                        Destroy(child);
                    }

                    RectTransform warning = CreateCard("Page refresh warning");
                    AddText(warning, "This page could not refresh, but navigation is still available.", 14,
                        new Color(1f, 0.68f, 0.40f), TextAnchor.UpperLeft);
                    AddText(warning, "See the Unity Console for the original exception.", 12,
                        new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
                }
            }
            catch (Exception fallbackException)
            {
                // Preserve the original exception as the useful diagnostic;
                // the fallback UI must never turn a recoverable page error
                // into a second unhandled exception.
                Debug.LogException(fallbackException);
            }
            finally
            {
                EnsureHeaderNavigationReady();
                ReassertPageInputLayerOrder();
                RefreshTopNavigationState();
            }
        }

        private bool IsFamilyTreePanDragging
        {
            get
            {
                return activeMainPanel == MainPanel.FamilyTree &&
                    familyTreePanZoom != null && familyTreePanZoom.IsDragging;
            }
        }

        /// <summary>
        /// Performs a structural UI refresh immediately, even when the action
        /// was tapped inside the profile ScrollRect. Destructive-action
        /// confirmations must replace their button during the same pointer
        /// gesture instead of being mistaken for a profile drag.
        /// </summary>
        public void RefreshImmediate()
        {
            immediateRefreshRequested = true;
            Refresh(true);
        }

        /// <summary>
        /// Updates the always-visible header without rebuilding the page.
        /// The simulation clock can advance many times per frame at the
        /// fastest speed, so rebuilding the ScrollRect content here would
        /// continuously destroy and recreate its buttons before Unity can
        /// deliver their click events.
        /// </summary>
        public void RefreshHeader()
        {
            if (!ready || game == null) return;
            UpdateResponsiveLayoutIfNeeded();
            string clockLabel = game.ClockLabel;
            if (clockText != null && clockText.text != clockLabel) clockText.text = clockLabel;
            if (walletText != null)
            {
                int balance = game.Save == null ? 0 : game.Save.colonyCredits;
                string walletLabel = "Wallet  $" + balance.ToString("N0");
                if (walletText.text != walletLabel) walletText.text = walletLabel;
            }
            if (liveEventText != null)
            {
                // The habitat itself supplies the large in-world sign. Keep
                // the header slot for real colony events, using the newest
                // approved event as a quiet fallback after its live banner
                // has expired. Do not repeat the habitat name and page count.
                string liveMessage = game.LiveEventMessage;
                if (string.IsNullOrEmpty(liveMessage))
                    liveMessage = game.LatestEnabledEventMessage;
                if (string.IsNullOrEmpty(liveMessage) && game.AllAlertCategoriesDisabled)
                    liveMessage = "Alerts muted";
                if (liveEventText.text != liveMessage) liveEventText.text = liveMessage;
            }
            if (eventLogLabel != null)
            {
                string eventLabelText = game.RecentEventCount > 0
                    ? "Events (" + game.RecentEventCount + ")"
                    : "Events";
                if (eventLogLabel.text != eventLabelText) eventLogLabel.text = eventLabelText;
            }
            string eventLogSignature = game.EventLogSignature;
            if (eventLogOpen && eventLogSignature != lastEventLogSignature)
                RefreshEventLogPanel();
            // These labels are simulation-time displays, not render-time
            // displays. Updating every bound roster/profile label every frame
            // caused repeated reproductive-status calculations and text/layout
            // work in late colonies. Keep the header clock live, but refresh
            // the detailed live labels on a short bounded interval.
            liveUiRefreshTimer -= Time.unscaledDeltaTime;
            if (liveUiRefreshTimer <= 0f)
            {
                for (int index = 0; index < liveTimedTextUpdates.Count; index++)
                {
                    liveTimedTextUpdates[index]?.Invoke();
                }
                RefreshRosterSortIfNeeded();
                RefreshLiveRatProfile();
                liveUiRefreshTimer = LiveUiRefreshIntervalSeconds;
            }
            RefreshTopNavigationState();
        }

        private void BuildShell()
        {
            var canvasObject = new GameObject("UI Canvas");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 50;
            canvasObject.AddComponent<GraphicRaycaster>();

            canvasScaler = canvasObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Vertical sizing keeps the mobile reference readable on narrow
            // screens and prevents a wide desktop window from magnifying the
            // phone UI. Horizontal space is handled by the centered column
            // below, not by stretching the UI scale.
            canvasScaler.matchWidthOrHeight = 1f;

            safeRoot = CreateRect("Safe Area", canvas.transform);
            ApplySafeArea();

            var header = CreateRect("Fixed Header", safeRoot);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(0f, -HeaderHeight);
            header.offsetMax = Vector2.zero;
            var headerImage = header.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(headerImage, new Color(0.055f, 0.12f, 0.15f, 0.97f), true);
            headerImage.raycastTarget = false;

            // The header background can span the screen, but its content uses
            // the same centered portrait column as the page. This prevents a
            // landscape desktop Game view from magnifying or clipping text.
            headerContent = CreateRect("Header Content", header);
            headerContent.anchorMin = new Vector2(0.5f, 0f);
            headerContent.anchorMax = new Vector2(0.5f, 1f);
            headerContent.pivot = new Vector2(0.5f, 0.5f);
            headerContent.anchoredPosition = Vector2.zero;
            headerContent.sizeDelta = new Vector2(MaximumUiColumnWidth, 0f);

            // Page cards and profile ScrollRects are generated after the
            // header and share the root canvas. Give the fixed navigation its
            // own sorting canvas so a profile that is temporarily oversized
            // during a layout rebuild can never intercept a header tap. The
            // separate raycaster also gives the EventSystem an unambiguous
            // hit target on touch and desktop WebGL.
            headerCanvas = header.gameObject.AddComponent<Canvas>();
            headerCanvas.overrideSorting = true;
            headerCanvas.sortingOrder = canvas.sortingOrder + 20;
            headerRaycaster = header.gameObject.AddComponent<GraphicRaycaster>();

            var title = AddText(headerContent, "RAT EMPIRE", 16, Color.white, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 0.73f);
            title.rectTransform.anchorMax = new Vector2(0.34f, 1f);
            title.rectTransform.offsetMin = new Vector2(12f, 1f);
            title.rectTransform.offsetMax = new Vector2(0f, -1f);
            title.fontStyle = FontStyle.Bold;

            walletText = AddText(headerContent, "$0", 12, new Color(1f, 0.82f, 0.38f), TextAnchor.MiddleLeft);
            walletText.rectTransform.anchorMin = new Vector2(0f, 0.59f);
            walletText.rectTransform.anchorMax = new Vector2(0.34f, 0.74f);
            walletText.rectTransform.offsetMin = new Vector2(12f, 0f);
            walletText.rectTransform.offsetMax = new Vector2(0f, -1f);

            clockText = AddText(headerContent, "Day 1", 14, new Color(0.73f, 0.9f, 0.78f), TextAnchor.UpperLeft);
            clockText.rectTransform.anchorMin = new Vector2(0.34f, 0.59f);
            clockText.rectTransform.anchorMax = new Vector2(0.55f, 1f);
            clockText.rectTransform.offsetMin = new Vector2(2f, 1f);
            clockText.rectTransform.offsetMax = new Vector2(0f, -1f);

            eventLogToggleButton = AddButtonTo(headerContent, "Events", true, ToggleEventLog,
                new Color(0.11f, 0.25f, 0.25f, 1f), 30f);
            eventLogLabel = eventLogToggleButton.GetComponentInChildren<Text>();
            RectTransform eventLogButtonRect = eventLogToggleButton.GetComponent<RectTransform>();
            eventLogButtonRect.anchorMin = new Vector2(0.55f, 0.59f);
            eventLogButtonRect.anchorMax = new Vector2(1f, 0.77f);
            eventLogButtonRect.offsetMin = new Vector2(0f, 1f);
            eventLogButtonRect.offsetMax = new Vector2(-12f, -1f);

            liveEventText = AddText(headerContent, string.Empty, 10,
                new Color(1f, 0.82f, 0.38f), TextAnchor.MiddleRight);
            liveEventText.rectTransform.anchorMin = new Vector2(0.55f, 0.77f);
            liveEventText.rectTransform.anchorMax = new Vector2(1f, 1f);
            liveEventText.rectTransform.offsetMin = new Vector2(0f, 1f);
            liveEventText.rectTransform.offsetMax = new Vector2(-12f, -1f);
            liveEventText.horizontalOverflow = HorizontalWrapMode.Wrap;
            liveEventText.verticalOverflow = VerticalWrapMode.Truncate;

            var navigation = CreateRect("Top Navigation", headerContent);
            navigation.anchorMin = new Vector2(0f, 0.33f);
            navigation.anchorMax = new Vector2(1f, 0.58f);
            navigation.offsetMin = new Vector2(8f, 1f);
            navigation.offsetMax = new Vector2(-8f, -3f);
            var navigationLayout = navigation.gameObject.AddComponent<HorizontalLayoutGroup>();
            navigationLayout.spacing = 4f;
            navigationLayout.padding = new RectOffset(0, 0, 0, 0);
            navigationLayout.childAlignment = TextAnchor.MiddleCenter;
            navigationLayout.childControlWidth = true;
            navigationLayout.childControlHeight = true;
            navigationLayout.childForceExpandWidth = true;
            navigationLayout.childForceExpandHeight = false;

            topNavigationButtons.Clear();
            topNavigationImages.Clear();
            topNavigationLabels.Clear();
            AddTopNavigationButton(navigation, MainPanel.Habitat, "Habitat");
            AddTopNavigationButton(navigation, MainPanel.MyRats, "My Rats");
            AddTopNavigationButton(navigation, MainPanel.Store, "Store");
            AddTopNavigationButton(navigation, MainPanel.Upgrades, "Upgrades");
            AddTopNavigationButton(navigation, MainPanel.Settings, "Settings");

            var speedRow = CreateRect("Simulation Speed Controls", headerContent);
            speedRow.anchorMin = new Vector2(0f, 0f);
            speedRow.anchorMax = new Vector2(1f, 0.32f);
            speedRow.offsetMin = new Vector2(82f, 2f);
            speedRow.offsetMax = new Vector2(-82f, -2f);
            var speedLayout = speedRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            speedLayout.spacing = 6f;
            speedLayout.childAlignment = TextAnchor.MiddleCenter;
            speedLayout.childControlWidth = true;
            speedLayout.childControlHeight = true;
            speedLayout.childForceExpandWidth = true;
            speedLayout.childForceExpandHeight = false;
            simulationSpeedButtons.Clear();
            AddSimulationSpeedButton(speedRow, 1f);
            AddSimulationSpeedButton(speedRow, 2f);
            AddSimulationSpeedButton(speedRow, 3f);
            EnsureHeaderNavigationReady();
            RefreshTopNavigationState();

            var scrollObject = new GameObject("Natural Page Scroll");
            scrollObject.transform.SetParent(safeRoot, false);
            pageScroll = scrollObject.AddComponent<ScrollRect>();
            pageScroll.horizontal = false;
            pageScroll.vertical = true;
            pageScroll.inertia = true;
            pageScroll.movementType = ScrollRect.MovementType.Elastic;
            pageScroll.scrollSensitivity = 30f;
            var scrollRect = scrollObject.GetComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0f, -PageTopInset);

            // Keep the page controls interactive while consuming every pointer
            // or touch in the page area so the same input cannot fall through
            // to a rat, habitat object, camera drag, or world zoom.
            myRatsInputBlocker = CreateRect("My Rats Input Blocker", safeRoot);
            myRatsInputBlocker.anchorMin = Vector2.zero;
            myRatsInputBlocker.anchorMax = Vector2.one;
            myRatsInputBlocker.offsetMin = Vector2.zero;
            myRatsInputBlocker.offsetMax = new Vector2(0f, -PageTopInset);
            var blockerImage = myRatsInputBlocker.gameObject.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.001f);
            blockerImage.raycastTarget = true;
            // This is a world-input shield, not a page overlay. Keep it at
            // the bottom of the safe-root sibling stack so every generated
            // page control (including My Rats buttons and ScrollRects) stays
            // above it in the same GraphicRaycaster. The fixed header has its
            // own higher sorting Canvas as an additional guard.
            myRatsInputBlocker.SetSiblingIndex(0);

            // Family Tree is a page-level modal interaction surface. Keep a
            // separate blocker so empty tree/card space cannot send the same
            // pointer through to a live rat or camera gesture underneath it.
            familyTreeInputBlocker = CreateRect("Family Tree Input Blocker", safeRoot);
            familyTreeInputBlocker.anchorMin = Vector2.zero;
            familyTreeInputBlocker.anchorMax = Vector2.one;
            familyTreeInputBlocker.offsetMin = Vector2.zero;
            familyTreeInputBlocker.offsetMax = new Vector2(0f, -PageTopInset);
            var familyTreeBlockerImage = familyTreeInputBlocker.gameObject.AddComponent<Image>();
            familyTreeBlockerImage.color = new Color(0f, 0f, 0f, 0.001f);
            familyTreeBlockerImage.raycastTarget = true;
            familyTreeInputBlocker.SetSiblingIndex(0);

            var viewport = CreateRect("Page Viewport", scrollRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            // The viewport itself stays transparent to input so empty habitat
            // space remains available for world selection, mouse-wheel zoom,
            // and pinch zoom. Actual generated controls are the UI hit
            // surfaces and are routed directly before world selection.
            viewportImage.raycastTarget = false;
            viewport.gameObject.AddComponent<RectMask2D>();
            pageScroll.viewport = viewport;

            content = CreateRect("Page Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = 10f;
            contentLayout.padding = new RectOffset((int)MinimumSideMargin, (int)MinimumSideMargin, 14, PageBottomSafePadding);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pageScroll.content = content;

            BuildWelcomeModal();
            BuildSettingsPopup();
            BuildDeveloperToolsPopup();
            BuildRatAnimationShowcasePopup();
            BuildEventLogPanel();
            BuildRenamePopup();
            BuildPendingNamingPopup();

            // Force the first width calculation before the first content
            // rebuild so the initial selected-rat card is already constrained.
            Canvas.ForceUpdateCanvases();
            ApplyResponsiveLayout();
            // The runtime Canvas may not have received its final safe-area
            // rectangle until the first layout pass. Let the first Refresh
            // recalculate from the actual screen dimensions.
            layoutScreenWidth = -1;
            layoutScreenHeight = -1;
        }

        private void ApplySafeArea()
        {
            if (safeRoot == null) return;
            Rect safe = Screen.safeArea;
            safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        private void UpdateResponsiveLayoutIfNeeded()
        {
            if (safeRoot == null || contentLayout == null) return;
            if (layoutScreenWidth == Screen.width && layoutScreenHeight == Screen.height) return;

            ApplySafeArea();
            Canvas.ForceUpdateCanvases();
            ApplyResponsiveLayout();
            layoutScreenWidth = Screen.width;
            layoutScreenHeight = Screen.height;
        }

        private void ApplyResponsiveLayout()
        {
            if (safeRoot == null) return;
            float availableWidth = Mathf.Max(0f, safeRoot.rect.width - (MinimumSideMargin * 2f));
            float columnWidth = Mathf.Min(MaximumUiColumnWidth, availableWidth);
            float sideMargin = Mathf.Max(MinimumSideMargin, (safeRoot.rect.width - columnWidth) * 0.5f);

            if (headerContent != null)
            {
                headerContent.sizeDelta = new Vector2(columnWidth, 0f);
            }
            float modalWidth = Mathf.Min(ModalMaximumWidth, columnWidth);
            if (welcomeCard != null)
            {
                welcomeCard.sizeDelta = new Vector2(modalWidth, 0f);
            }
            if (settingsCard != null)
            {
                settingsCard.sizeDelta = settingsViewport == null
                    ? new Vector2(modalWidth, 0f)
                    : Vector2.zero;
            }
            if (developerToolsCard != null)
            {
                developerToolsCard.sizeDelta = Vector2.zero;
            }
            if (ratAnimationShowcaseCard != null)
            {
                ratAnimationShowcaseCard.sizeDelta = new Vector2(modalWidth, 0f);
            }
            if (eventLogCard != null)
            {
                float cardHeight = Mathf.Min(400f, Mathf.Max(300f, safeRoot.rect.height * 0.46f));
                eventLogCard.sizeDelta = new Vector2(modalWidth, cardHeight);
                eventLogCard.anchoredPosition = new Vector2(0f, -HeaderHeight - 8f);
            }
            if (renameCard != null) renameCard.sizeDelta = new Vector2(modalWidth, 0f);
            if (namingCard != null) namingCard.sizeDelta = new Vector2(modalWidth, 0f);
            if (developerToolsViewport != null)
            {
                float viewportHeight = Mathf.Min(720f, Mathf.Max(360f, safeRoot.rect.height - 40f));
                developerToolsViewport.sizeDelta = new Vector2(modalWidth, viewportHeight);
            }
            if (settingsViewport != null)
            {
                float viewportHeight = Mathf.Min(760f, Mathf.Max(360f, safeRoot.rect.height - 40f));
                settingsViewport.sizeDelta = new Vector2(modalWidth, viewportHeight);
            }
            if (contentLayout != null)
            {
                int margin = Mathf.Max((int)MinimumSideMargin, Mathf.RoundToInt(sideMargin));
                contentLayout.padding.left = margin;
                contentLayout.padding.right = margin;
            }
        }

        private void BuildWelcomeModal()
        {
            welcomeOverlay = CreateModalOverlay("Welcome Modal", new Color(0.01f, 0.03f, 0.04f, 0.74f), out welcomeCard);
            string femaleName = "your female rat";
            string maleName = "your male rat";
            if (game != null && game.Save != null && game.Save.rats != null)
            {
                foreach (var rat in game.Save.rats)
                {
                    if (rat == null) continue;
                    if (rat.sex == RatSex.Female) femaleName = ColonyFactory.DisplayName(rat);
                    else if (rat.sex == RatSex.Male) maleName = ColonyFactory.DisplayName(rat);
                }
            }
            AddText(welcomeCard, "Welcome to Rat Empire!", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(welcomeCard, "You're starting with " + femaleName + " and " + maleName + " in a small habitat. Take some time to get to know them, watch their health and fertility, and move them into the Pairing Habitat when you're ready to begin breeding.", 15, Color.white, TextAnchor.UpperLeft);
            AddText(welcomeCard, "You can earn money by managing your colony, improve it with upgrades, and watch your rat family grow over time. Have fun building your little empire!", 14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddButtonTo(welcomeCard, "Start Playing", true, CloseWelcome, new Color(0.16f, 0.38f, 0.33f), 46f);
        }

        private void BuildSettingsPopup()
        {
            settingsOverlay = CreateModalOverlay("Settings Popup", new Color(0.01f, 0.03f, 0.04f, 0.74f), out settingsCard);
            AddText(settingsCard, "Settings", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(settingsCard, "Local save", 17, Color.white, TextAnchor.UpperLeft);
            AddText(settingsCard, "Save the colony, pregnancy state, litters, growth, genes, traits, and timestamps locally on this device.", 14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddText(settingsCard, "Pairing Habitat: " + (GameConfig.PairingPregnancyChance * 100f).ToString("0") +
                "% pregnancy chance per " + (GameConfig.PairingCheckIntervalMs / 1000L).ToString() + " in-game seconds.",
                14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            keepScreenAwakeButton = AddButtonTo(settingsCard, "Keep Screen Awake", true,
                game.ToggleKeepScreenAwake, new Color(0.16f, 0.38f, 0.33f), 46f);
            keepScreenAwakeStatusText = AddText(settingsCard, string.Empty, 12,
                new Color(0.72f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            AddText(settingsCard, "Top Screen Alerts", 17, Color.white, TextAnchor.UpperLeft);
            AddText(settingsCard, "Choose which important colony events appear near the header. The full Events history is kept separately.",
                13, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            alertPreferenceButtons.Clear();
            for (int index = 0; index < EventLogPolicy.CategoryIds.Length; index++)
                AddAlertPreferenceButton(settingsCard, EventLogPolicy.CategoryIds[index]);
            alertPreferencesStatusText = AddText(settingsCard, string.Empty, 12,
                new Color(0.72f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            AddButtonTo(settingsCard, "Reset Alert Preferences", true,
                game.ResetAlertPreferences, new Color(0.12f, 0.27f, 0.29f), 40f);
            AddText(settingsCard, "Custom Rat Names", 17, Color.white, TextAnchor.UpperLeft);
            AddText(settingsCard, "Add names one at a time. Custom names join the built-in pools for future rats and Randomize buttons.",
                13, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            BuildCustomNameList(settingsCard, RatSex.Male);
            BuildCustomNameList(settingsCard, RatSex.Female);
            customNamesStatusText = AddText(settingsCard, string.Empty, 12,
                new Color(0.72f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            clearCustomNamesButton = AddButtonTo(settingsCard, "Clear Custom Names", true,
                game.RequestClearCustomNameLists, new Color(0.12f, 0.27f, 0.29f), 40f);
            confirmClearCustomNamesButton = AddButtonTo(settingsCard, "Confirm Clear Custom Names", true,
                game.ConfirmClearCustomNameLists, new Color(0.55f, 0.16f, 0.14f), 40f);
            cancelClearCustomNamesButton = AddButtonTo(settingsCard, "Cancel", true,
                game.CancelClearCustomNameLists, new Color(0.14f, 0.22f, 0.25f), 40f);
            AddButtonTo(settingsCard, "Save now", true, game.SaveNow, new Color(0.16f, 0.38f, 0.33f), 46f);
            AddButtonTo(settingsCard, "Developer Tools", true, OpenDeveloperTools, new Color(0.12f, 0.27f, 0.29f), 46f);
            AddButtonTo(settingsCard, "Close Settings", true, CloseSettings, new Color(0.14f, 0.22f, 0.25f), 46f);

            // Settings owns its own scroll surface so the data-driven alert
            // list remains usable on phone layouts without clipping the
            // lower controls. The modal blocker still covers the full screen.
            settingsViewport = CreateRect("Settings Viewport", settingsOverlay);
            settingsViewport.anchorMin = new Vector2(0.5f, 0.5f);
            settingsViewport.anchorMax = new Vector2(0.5f, 0.5f);
            settingsViewport.pivot = new Vector2(0.5f, 0.5f);
            var viewportImage = settingsViewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;
            settingsViewport.gameObject.AddComponent<RectMask2D>();
            settingsScroll = settingsViewport.gameObject.AddComponent<ScrollRect>();
            settingsScroll.horizontal = false;
            settingsScroll.vertical = true;
            settingsScroll.inertia = true;
            settingsScroll.movementType = ScrollRect.MovementType.Clamped;
            settingsScroll.scrollSensitivity = 30f;
            settingsScroll.viewport = settingsViewport;
            settingsCard.SetParent(settingsViewport, false);
            settingsCard.anchorMin = new Vector2(0f, 1f);
            settingsCard.anchorMax = new Vector2(1f, 1f);
            settingsCard.pivot = new Vector2(0.5f, 1f);
            settingsCard.anchoredPosition = Vector2.zero;
            settingsCard.sizeDelta = new Vector2(0f, 0f);
            settingsScroll.content = settingsCard;
            RefreshWakeLockControls();
            RefreshAlertPreferenceControls();
            RefreshCustomNameControls();
            SetOverlayVisibility();
        }

        private void AddAlertPreferenceButton(Transform parent, string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            Button button = AddButtonTo(parent, EventLogPolicy.CategoryLabel(category), true,
                () => game.ToggleAlertCategory(category), new Color(0.16f, 0.38f, 0.33f), 38f);
            button.gameObject.name = "Alert Preference " + category;
            alertPreferenceButtons[category] = button;
        }

        private void BuildRenamePopup()
        {
            renameOverlay = CreateModalOverlay("Rename Rat Modal", new Color(0.01f, 0.03f, 0.04f, 0.74f), out renameCard);
            AddText(renameCard, "Rename Rat", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(renameCard, "Choose a name for this rat. It will stay until you rename it again.", 14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            renameInput = AddInputFieldTo(renameCard, string.Empty, 48f, false);
            renameStatusText = AddText(renameCard, string.Empty, 12, new Color(1f, 0.63f, 0.42f), TextAnchor.UpperLeft);
            RectTransform actions = CreateRect("Rename Rat Actions", renameCard);
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 8f;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = false;
            Button saveButton = AddButtonTo(actions, "Save", true, ConfirmRename, new Color(0.16f, 0.38f, 0.33f), 44f);
            SetRenameActionWidth(saveButton, 112f);
            AddDiceButtonTo(actions, RandomizeRename, true, 44f);
            Button cancelButton = AddButtonTo(actions, "Cancel", true, CloseRenameModal, new Color(0.14f, 0.22f, 0.25f), 44f);
            SetRenameActionWidth(cancelButton, 112f);
            SetOverlayVisibility();
        }

        private static void SetRenameActionWidth(Button button, float width)
        {
            if (button == null) return;
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout == null) layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 1f;
        }

        private void BuildPendingNamingPopup()
        {
            namingOverlay = CreateModalOverlay("Newborn Naming Modal", new Color(0.01f, 0.03f, 0.04f, 0.80f), out namingCard);
            AddText(namingCard, "Name the new pinkies", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(namingCard, "The game is paused while you name this litter. Each pup needs its own name.", 14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            var scrollObject = new GameObject("Newborn Naming Scroll");
            scrollObject.transform.SetParent(namingCard, false);
            namingScroll = scrollObject.AddComponent<ScrollRect>();
            namingScroll.horizontal = false;
            namingScroll.vertical = true;
            namingScroll.movementType = ScrollRect.MovementType.Clamped;
            namingScroll.inertia = true;
            namingScroll.scrollSensitivity = 30f;
            var scrollLayout = scrollObject.AddComponent<LayoutElement>();
            scrollLayout.preferredHeight = 330f;
            scrollLayout.minHeight = 180f;
            var scrollRect = scrollObject.GetComponent<RectTransform>();
            var viewport = CreateRect("Newborn Naming Viewport", scrollRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.04f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            namingContent = CreateRect("Newborn Naming Content", viewport);
            namingContent.anchorMin = new Vector2(0f, 1f);
            namingContent.anchorMax = new Vector2(1f, 1f);
            namingContent.pivot = new Vector2(0.5f, 1f);
            namingContent.anchoredPosition = Vector2.zero;
            namingContent.sizeDelta = new Vector2(0f, 0f);
            var contentLayout = namingContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 7f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            var contentFitter = namingContent.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            namingScroll.viewport = viewport;
            namingScroll.content = namingContent;
            namingStatusText = AddText(namingCard, string.Empty, 12, new Color(1f, 0.63f, 0.42f), TextAnchor.UpperLeft);
            RectTransform actions = CreateRect("Newborn Naming Actions", namingCard);
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 8f;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = true;
            AddButtonTo(actions, "Randomize All", true, game.RandomizeAllPendingPupNames, new Color(0.14f, 0.29f, 0.29f), 44f);
            AddButtonTo(actions, "Continue", true, ApprovePendingNaming, new Color(0.16f, 0.38f, 0.33f), 44f);
            SetOverlayVisibility();
        }

        private InputField AddInputFieldTo(Transform parent, string value, float height, bool multiline)
        {
            var root = new GameObject("Name Input");
            root.transform.SetParent(parent, false);
            var image = root.AddComponent<Image>();
            image.color = new Color(0.09f, 0.17f, 0.18f, 1f);
            image.raycastTarget = true;
            var layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            var input = root.AddComponent<InputField>();
            input.targetGraphic = image;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.Standard;
            input.characterLimit = multiline ? 0 : RatNameSystem.MaximumNameLength;
            var textObject = new GameObject("Input Text");
            textObject.transform.SetParent(root.transform, false);
            var text = textObject.AddComponent<Text>();
            text.font = ResolveUiFont();
            text.fontSize = UiFontSize(14);
            text.color = Color.white;
            text.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(10f, 5f);
            text.rectTransform.offsetMax = new Vector2(-10f, -5f);
            input.textComponent = text;
            input.text = value ?? string.Empty;
            return input;
        }

        private void BuildCustomNameList(Transform parent, RatSex sex)
        {
            string sexLabel = sex == RatSex.Female ? "Female names" : "Male names";
            RectTransform section = CreateRect(sexLabel + " Custom Name Section", parent);
            var sectionLayout = section.gameObject.AddComponent<VerticalLayoutGroup>();
            sectionLayout.childControlWidth = true;
            sectionLayout.childControlHeight = true;
            sectionLayout.childForceExpandWidth = true;
            sectionLayout.childForceExpandHeight = false;
            sectionLayout.spacing = 5f;
            sectionLayout.padding = new RectOffset(0, 0, 2, 2);
            var sectionFitter = section.gameObject.AddComponent<ContentSizeFitter>();
            sectionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform heading = CreateRect(sexLabel + " Heading", section);
            var headingLayout = heading.gameObject.AddComponent<HorizontalLayoutGroup>();
            headingLayout.childControlWidth = true;
            headingLayout.childControlHeight = true;
            headingLayout.childForceExpandWidth = false;
            headingLayout.childForceExpandHeight = true;
            headingLayout.spacing = 8f;
            Text headingText = AddTextTo(heading, sexLabel, 13,
                new Color(0.98f, 0.78f, 0.32f), TextAnchor.MiddleLeft);
            LayoutElement headingTextLayout = headingText.gameObject.GetComponent<LayoutElement>();
            if (headingTextLayout != null) headingTextLayout.flexibleWidth = 1f;
            Button addButton = AddButtonTo(heading, "+", true,
                () => ToggleCustomNameEntry(sex), new Color(0.14f, 0.29f, 0.29f), 42f);
            ConfigureCompactNameButton(addButton, 42f, "Add custom " + sexLabel.ToLowerInvariant().Replace(" names", " name"));

            RectTransform list = CreateRect(sexLabel + " Custom Name List", section);
            var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            listLayout.spacing = 4f;
            var listFitter = list.gameObject.AddComponent<ContentSizeFitter>();
            listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform inputRow = CreateRect(sexLabel + " Custom Name Input", section);
            var inputLayout = inputRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            inputLayout.childControlWidth = true;
            inputLayout.childControlHeight = true;
            inputLayout.childForceExpandWidth = false;
            inputLayout.childForceExpandHeight = true;
            inputLayout.spacing = 6f;
            InputField input = AddInputFieldTo(inputRow, string.Empty, 42f, false);
            LayoutElement inputLayoutElement = input.GetComponent<LayoutElement>();
            if (inputLayoutElement != null)
            {
                inputLayoutElement.flexibleWidth = 1f;
                inputLayoutElement.minWidth = 80f;
            }
            Button confirm = AddButtonTo(inputRow, "✓", true,
                () => CommitCustomName(sex), new Color(0.16f, 0.38f, 0.33f), 42f);
            ConfigureCompactNameButton(confirm, 42f, "Add custom " + sexLabel.ToLowerInvariant().Replace(" names", " name"));

            if (sex == RatSex.Female)
            {
                customFemaleNamesList = list;
                customFemaleNameInputRow = inputRow;
                customFemaleNameEntryInput = input;
            }
            else
            {
                customMaleNamesList = list;
                customMaleNameInputRow = inputRow;
                customMaleNameEntryInput = input;
            }
            inputRow.gameObject.SetActive(sex == RatSex.Female ? customFemaleNameInputOpen : customMaleNameInputOpen);
            RebuildCustomNameList(sex);
        }

        private static void ConfigureCompactNameButton(Button button, float size, string tooltip)
        {
            if (button == null) return;
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = size;
                layout.minWidth = size;
                layout.flexibleWidth = 0f;
            }
            button.gameObject.name = tooltip;
            RatUiTooltip label = button.gameObject.GetComponent<RatUiTooltip>();
            if (label == null) label = button.gameObject.AddComponent<RatUiTooltip>();
            label.Label = tooltip;
        }

        private void ToggleCustomNameEntry(RatSex sex)
        {
            if (sex == RatSex.Female)
            {
                customFemaleNameInputOpen = !customFemaleNameInputOpen;
                if (customFemaleNameInputRow != null) customFemaleNameInputRow.gameObject.SetActive(customFemaleNameInputOpen);
                if (customFemaleNameInputOpen && customFemaleNameEntryInput != null)
                {
                    customFemaleNameEntryInput.text = string.Empty;
                    customFemaleNameEntryInput.ActivateInputField();
                }
            }
            else
            {
                customMaleNameInputOpen = !customMaleNameInputOpen;
                if (customMaleNameInputRow != null) customMaleNameInputRow.gameObject.SetActive(customMaleNameInputOpen);
                if (customMaleNameInputOpen && customMaleNameEntryInput != null)
                {
                    customMaleNameEntryInput.text = string.Empty;
                    customMaleNameEntryInput.ActivateInputField();
                }
            }
        }

        private void CommitCustomName(RatSex sex)
        {
            if (game == null) return;
            InputField input = sex == RatSex.Female ? customFemaleNameEntryInput : customMaleNameEntryInput;
            string error;
            if (!game.TryAddCustomName(sex, input == null ? string.Empty : input.text, out error))
            {
                if (customNamesStatusText != null) customNamesStatusText.text = error;
                return;
            }
            if (sex == RatSex.Female)
            {
                customFemaleNameInputOpen = false;
                if (customFemaleNameInputRow != null) customFemaleNameInputRow.gameObject.SetActive(false);
                if (customFemaleNameEntryInput != null) customFemaleNameEntryInput.text = string.Empty;
            }
            else
            {
                customMaleNameInputOpen = false;
                if (customMaleNameInputRow != null) customMaleNameInputRow.gameObject.SetActive(false);
                if (customMaleNameEntryInput != null) customMaleNameEntryInput.text = string.Empty;
            }
            if (customNamesStatusText != null) customNamesStatusText.text = "Custom name added.";
            RefreshCustomNameControls();
        }

        private void RemoveCustomName(RatSex sex, string name)
        {
            if (game == null) return;
            if (game.RemoveCustomName(sex, name))
            {
                if (customNamesStatusText != null) customNamesStatusText.text = "Custom name removed.";
                RefreshCustomNameControls();
            }
        }

        private void RebuildCustomNameList(RatSex sex)
        {
            RectTransform list = sex == RatSex.Female ? customFemaleNamesList : customMaleNamesList;
            if (list == null || game == null) return;
            for (int index = list.childCount - 1; index >= 0; index--)
                Destroy(list.GetChild(index).gameObject);

            List<string> names = game.CustomNames(sex);
            if (names == null || names.Count == 0)
            {
                AddTextTo(list, "No custom names yet.", 12,
                    new Color(0.60f, 0.70f, 0.68f), TextAnchor.MiddleLeft);
                return;
            }
            for (int index = 0; index < names.Count; index++)
            {
                string name = names[index];
                RectTransform row = CreateRect("Custom Name " + name, list);
                var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;
                rowLayout.spacing = 6f;
                Text nameText = AddTextTo(row, name, 14, Color.white, TextAnchor.MiddleLeft);
                LayoutElement nameLayout = nameText.gameObject.GetComponent<LayoutElement>();
                if (nameLayout != null) nameLayout.flexibleWidth = 1f;
                Button remove = AddButtonTo(row, "×", true,
                    () => RemoveCustomName(sex, name), new Color(0.20f, 0.27f, 0.28f), 36f);
                ConfigureCompactNameButton(remove, 36f, "Remove custom name " + name);
            }
        }

        public void RefreshCustomNameControls()
        {
            if (game == null) return;
            RebuildCustomNameList(RatSex.Male);
            RebuildCustomNameList(RatSex.Female);
            if (customMaleNameInputRow != null) customMaleNameInputRow.gameObject.SetActive(customMaleNameInputOpen);
            if (customFemaleNameInputRow != null) customFemaleNameInputRow.gameObject.SetActive(customFemaleNameInputOpen);
            bool confirm = game.CustomNamesClearConfirmationPending;
            if (clearCustomNamesButton != null) clearCustomNamesButton.gameObject.SetActive(!confirm);
            if (confirmClearCustomNamesButton != null) confirmClearCustomNamesButton.gameObject.SetActive(confirm);
            if (cancelClearCustomNamesButton != null) cancelClearCustomNamesButton.gameObject.SetActive(confirm);
            ForceSettingsLayoutRefresh();
        }

        private void ForceSettingsLayoutRefresh()
        {
            if (settingsCard == null) return;
            // Name-list rows and the confirmation controls change the card's
            // height at runtime. Rebuild before returning from the click so
            // the visible button rectangles and their raycasts stay aligned.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(settingsCard);
            if (settingsViewport != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(settingsViewport);
            Canvas.ForceUpdateCanvases();
        }

        private void OpenRenameModal(string ratId)
        {
            RatData rat = BreedingSystem.FindHistoricalRat(game.Save, ratId);
            if (rat == null) return;
            renameRatId = rat.id;
            renameOpen = true;
            if (renameInput != null) renameInput.text = rat.name;
            if (renameStatusText != null) renameStatusText.text = string.Empty;
            SetOverlayVisibility();
        }

        private void ConfirmRename()
        {
            if (game == null || string.IsNullOrEmpty(renameRatId)) return;
            string error;
            if (!game.TryRenameRat(renameRatId, renameInput == null ? string.Empty : renameInput.text, out error))
            {
                if (renameStatusText != null) renameStatusText.text = error;
                return;
            }
            CloseRenameModal();
        }

        private void RandomizeRename()
        {
            if (game == null || string.IsNullOrEmpty(renameRatId)) return;
            string name = game.RandomizeRatName(renameRatId);
            if (renameInput != null) renameInput.text = name;
            if (renameStatusText != null) renameStatusText.text = "Random name selected. Save to keep it.";
        }

        private void CloseRenameModal()
        {
            renameOpen = false;
            renameRatId = null;
            SetOverlayVisibility();
            Refresh(true);
        }

        public void OpenPendingLitterNaming()
        {
            if (game == null || !game.HasPendingLitterNaming) return;
            namingOpen = true;
            welcomeOpen = false;
            settingsOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            GrowthSystem.SetSimulationPaused(true);
            try
            {
                RefreshPendingNamingPopup();
            }
            catch (Exception exception)
            {
                // Keep the first exception visible in the Unity/WebGL log and
                // keep the durable naming modal usable even if one optional
                // newborn row failed to rebuild. The generated names remain
                // in the save, so Continue can still approve the litter and
                // release the authoritative simulation pause.
                Debug.LogException(exception);
                if (namingStatusText != null)
                    namingStatusText.text = "Some names could not be displayed. The generated names are still saved.";
            }
            SetOverlayVisibility();
            LogBirthTransitionState("naming-open");
        }

        public void RefreshPendingNamingPopup()
        {
            if (namingContent == null || game == null) return;
            for (int index = namingContent.childCount - 1; index >= 0; index--)
                DestroyImmediate(namingContent.GetChild(index).gameObject);
            pendingNamingInputs.Clear();
            foreach (RatData pup in game.PendingNamingPups)
            {
                if (pup == null) continue;
                RectTransform row = CreateRect("Newborn Name " + pup.id, namingContent);
                var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 6f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                Text label = AddTextTo(row, ColonyFactory.DisplayName(pup), 13, Color.white, TextAnchor.MiddleLeft);
                var labelLayout = label.gameObject.AddComponent<LayoutElement>();
                labelLayout.preferredWidth = 105f;
                InputField input = AddInputFieldTo(row, pup.name, 44f, false);
                input.GetComponent<LayoutElement>().flexibleWidth = 1f;
                pendingNamingInputs[pup.id] = input;
                string pupId = pup.id;
                AddDiceButtonTo(row, () =>
                {
                    string randomized = game.RandomizePendingPupName(pupId);
                    if (pendingNamingInputs.ContainsKey(pupId)) pendingNamingInputs[pupId].text = randomized;
                }, true, 44f);
            }
            Canvas.ForceUpdateCanvases();
            if (namingScroll != null) namingScroll.verticalNormalizedPosition = 1f;
        }

        private void ApprovePendingNaming()
        {
            if (game == null) return;
            foreach (KeyValuePair<string, InputField> entry in pendingNamingInputs)
            {
                string error;
                if (!game.TryRenamePendingPup(entry.Key, entry.Value == null ? string.Empty : entry.Value.text, out error))
                {
                    if (namingStatusText != null) namingStatusText.text = error;
                    return;
                }
            }
            if (!game.ApprovePendingLitterNames()) return;
            if (!game.HasPendingLitterNaming) namingOpen = false;
            if (namingOpen) RefreshPendingNamingPopup();
            SetOverlayVisibility();
        }

        public void ClosePendingLitterNaming()
        {
            namingOpen = false;
            ClearUiPointerState();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            GrowthSystem.SetSimulationPaused(game != null &&
                (game.WelcomePopupPending || game.HasPendingLitterNaming));
            SetOverlayVisibility();
            Refresh(true);
            RestoreInputStateAfterBirthTransition();
            LogBirthTransitionState("naming-closed");
        }

        /// <summary>
        /// Development-only snapshot used at the birth boundary. This is
        /// intentionally generated from the live hierarchy rather than from
        /// cached UI state, so a first exception or stale blocker is visible.
        /// </summary>
        public void LogBirthTransitionState(string phase)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Debug.isDebugBuild) return;
            EnsureHeaderNavigationReady();
            int activeRaycasters = 0;
            GraphicRaycaster[] raycasters = GetComponentsInChildren<GraphicRaycaster>(true);
            for (int index = 0; index < raycasters.Length; index++)
                if (raycasters[index] != null && raycasters[index].isActiveAndEnabled) activeRaycasters++;
            Debug.Log("[Rat UI BirthTransition] phase=" + phase +
                " namingOpen=" + namingOpen +
                " namingOverlayActive=" + (namingOverlay != null && namingOverlay.gameObject.activeInHierarchy) +
                " namingCardActive=" + (namingCard != null && namingCard.gameObject.activeInHierarchy) +
                " pending=" + (game != null && game.HasPendingLitterNaming) +
                " overlayModal=" + IsModalOverlayOpen +
                " activePanel=" + activeMainPanel +
                " headerCanvas=" + (headerCanvas == null ? -1 : headerCanvas.sortingOrder) +
                " raycasters=" + activeRaycasters +
                " header=" + BuildHeaderDiagnosticState());
#endif
        }

        public void RefreshAlertPreferenceControls()
        {
            if (game == null) return;
            foreach (string category in EventLogPolicy.CategoryIds)
            {
                Button button;
                if (!alertPreferenceButtons.TryGetValue(category, out button) || button == null) continue;
                bool enabled = game.IsAlertCategoryEnabled(category);
                SetButtonLabel(button, (enabled ? "On  " : "Off  ") + EventLogPolicy.CategoryLabel(category));
                Image image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = enabled
                        ? new Color(0.16f, 0.38f, 0.33f)
                        : new Color(0.18f, 0.25f, 0.27f);
                }
            }
            if (alertPreferencesStatusText != null)
            {
                alertPreferencesStatusText.text = game.AllAlertCategoriesDisabled
                    ? "Alerts muted. Events history is still saved."
                    : "Top alerts update immediately and are saved locally.";
            }
        }

        public void RefreshWakeLockControls()
        {
            if (game == null) return;
            if (keepScreenAwakeButton != null)
            {
                SetButtonLabel(keepScreenAwakeButton,
                    game.KeepScreenAwakeEnabled ? "Keep Screen Awake: On" : "Keep Screen Awake: Off");
                Image image = keepScreenAwakeButton.GetComponent<Image>();
                if (image != null)
                {
                    image.color = game.KeepScreenAwakeEnabled
                        ? new Color(0.16f, 0.38f, 0.33f)
                        : new Color(0.18f, 0.25f, 0.27f);
                }
            }
            if (keepScreenAwakeStatusText != null)
                keepScreenAwakeStatusText.text = game.KeepScreenAwakeStatusMessage;
        }

        private void BuildDeveloperToolsPopup()
        {
            developerToolsOverlay = CreateModalOverlay("Developer Tools Popup", new Color(0.01f, 0.03f, 0.04f, 0.74f), out developerToolsCard);
            developerToolsViewport = CreateRect("Developer Tools Viewport", developerToolsOverlay);
            developerToolsViewport.anchorMin = new Vector2(0.5f, 0.5f);
            developerToolsViewport.anchorMax = new Vector2(0.5f, 0.5f);
            developerToolsViewport.pivot = new Vector2(0.5f, 0.5f);
            developerToolsViewport.sizeDelta = new Vector2(ModalMaximumWidth, 720f);
            var viewportImage = developerToolsViewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;
            developerToolsViewport.gameObject.AddComponent<RectMask2D>();
            var scroll = developerToolsViewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = developerToolsViewport;
            scroll.content = developerToolsCard;
            developerToolsCard.SetParent(developerToolsViewport, false);
            developerToolsCard.anchorMin = new Vector2(0f, 1f);
            developerToolsCard.anchorMax = new Vector2(1f, 1f);
            developerToolsCard.pivot = new Vector2(0.5f, 1f);
            developerToolsCard.anchoredPosition = Vector2.zero;
            developerToolsCard.sizeDelta = Vector2.zero;
            RebuildDeveloperToolsContent();
            SetOverlayVisibility();
        }

        private void BuildRatAnimationShowcasePopup()
        {
            ratAnimationShowcaseOverlay = CreateModalOverlay("Rat Animation Showcase Popup", new Color(0.01f, 0.03f, 0.04f, 0.82f), out ratAnimationShowcaseCard);
            AddText(ratAnimationShowcaseCard, "Rat Animation Showcase", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(ratAnimationShowcaseCard, "Presentation-only preview of the imported Hand Painted Rat controller. Preview objects are not colony rats and cannot be selected.", 12, new Color(0.72f, 0.82f, 0.78f), TextAnchor.UpperLeft);

            var body = CreateRect("Animation Showcase Body", ratAnimationShowcaseCard);
            var bodyElement = body.gameObject.AddComponent<LayoutElement>();
            bodyElement.preferredHeight = 310f;
            bodyElement.minHeight = 310f;
            var bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            bodyLayout.spacing = 10f;
            bodyLayout.childAlignment = TextAnchor.UpperCenter;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = false;
            bodyLayout.childForceExpandHeight = false;

            var previewColumn = CreateRect("Animation Showcase Preview Column", body);
            var previewColumnElement = previewColumn.gameObject.AddComponent<LayoutElement>();
            previewColumnElement.preferredWidth = 268f;
            previewColumnElement.minWidth = 220f;
            previewColumnElement.flexibleWidth = 0f;
            var previewColumnLayout = previewColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            previewColumnLayout.spacing = 6f;
            previewColumnLayout.childControlWidth = true;
            previewColumnLayout.childControlHeight = true;
            previewColumnLayout.childForceExpandWidth = true;
            previewColumnLayout.childForceExpandHeight = false;

            AddTextTo(previewColumn, "Preview track", 13, Color.white, TextAnchor.MiddleCenter);
            var previewFrame = CreateRect("Animation Showcase Preview Frame", previewColumn);
            var previewFrameElement = previewFrame.gameObject.AddComponent<LayoutElement>();
            previewFrameElement.preferredHeight = 220f;
            previewFrameElement.minHeight = 180f;
            var previewFrameImage = previewFrame.gameObject.AddComponent<Image>();
            previewFrameImage.color = new Color(0.03f, 0.06f, 0.07f, 1f);
            previewFrameImage.raycastTarget = false;
            var preview = CreateRect("Animation Showcase Preview Image", previewFrame);
            preview.anchorMin = new Vector2(0.5f, 0.5f);
            preview.anchorMax = new Vector2(0.5f, 0.5f);
            preview.pivot = new Vector2(0.5f, 0.5f);
            preview.anchoredPosition = Vector2.zero;
            preview.sizeDelta = new Vector2(268f, 151f);
            animationShowcasePreviewImage = preview.gameObject.AddComponent<RawImage>();
            animationShowcasePreviewImage.texture = ratAnimationShowcase == null ? null : ratAnimationShowcase.PreviewTexture;
            animationShowcasePreviewImage.color = Color.white;
            animationShowcasePreviewImage.raycastTarget = false;

            animationShowcaseCurrentText = AddTextTo(previewColumn, "Current: Walk", 12, new Color(1f, 0.82f, 0.38f), TextAnchor.MiddleCenter);
            animationShowcaseCurrentText.alignment = TextAnchor.MiddleCenter;
            animationShowcaseStatusText = AddTextTo(previewColumn, "", 11, new Color(0.72f, 0.82f, 0.78f), TextAnchor.MiddleCenter);
            animationShowcaseStatusText.alignment = TextAnchor.MiddleCenter;

            var animationList = CreateRect("Animation Showcase Clip List", body);
            var animationListElement = animationList.gameObject.AddComponent<LayoutElement>();
            animationListElement.flexibleWidth = 1f;
            animationListElement.minWidth = 170f;
            var animationListLayout = animationList.gameObject.AddComponent<VerticalLayoutGroup>();
            animationListLayout.spacing = 3f;
            animationListLayout.childControlWidth = true;
            animationListLayout.childControlHeight = true;
            animationListLayout.childForceExpandWidth = true;
            animationListLayout.childForceExpandHeight = false;
            AddTextTo(animationList, "Imported clips", 13, Color.white, TextAnchor.MiddleLeft);
            animationShowcaseRowLabels.Clear();
            animationShowcaseRowButtons.Clear();
            for (int index = 0; index < (ratAnimationShowcase == null ? 0 : ratAnimationShowcase.ClipCount); index++)
            {
                int clipIndex = index;
                bool usable = ratAnimationShowcase != null && ratAnimationShowcase.IsClipUsable(clipIndex);
                Button rowButton = AddButtonTo(animationList,
                    ratAnimationShowcase == null ? "Unavailable" : ratAnimationShowcase.GetClipRowText(clipIndex),
                    usable, () => ratAnimationShowcase.SelectAnimation(clipIndex),
                    usable ? new Color(0.14f, 0.28f, 0.27f) : new Color(0.14f, 0.16f, 0.17f), 31f);
                rowButton.gameObject.name = "Animation Clip " + (ratAnimationShowcase == null ? clipIndex.ToString() : ratAnimationShowcase.GetClipName(clipIndex));
                Text rowLabel = rowButton.GetComponentInChildren<Text>();
                if (rowLabel != null)
                {
                    rowLabel.fontSize = UiFontSize(10);
                    animationShowcaseRowLabels.Add(rowLabel);
                }
                animationShowcaseRowButtons.Add(rowButton);
            }

            var transport = CreateRect("Animation Showcase Transport", ratAnimationShowcaseCard);
            var transportLayout = transport.gameObject.AddComponent<HorizontalLayoutGroup>();
            transportLayout.spacing = 6f;
            transportLayout.childControlWidth = true;
            transportLayout.childControlHeight = true;
            transportLayout.childForceExpandWidth = true;
            transportLayout.childForceExpandHeight = false;
            AddButtonTo(transport, "Previous", true, () => ratAnimationShowcase.Previous(), new Color(0.14f, 0.25f, 0.28f), 40f);
            animationShowcasePlayPauseButton = AddButtonTo(transport, "Pause", true, () => ratAnimationShowcase.TogglePlayPause(), new Color(0.16f, 0.38f, 0.33f), 40f);
            AddButtonTo(transport, "Restart", true, () => ratAnimationShowcase.Restart(), new Color(0.14f, 0.25f, 0.28f), 40f);
            AddButtonTo(transport, "Next", true, () => ratAnimationShowcase.Next(), new Color(0.14f, 0.25f, 0.28f), 40f);

            var speedRow = CreateRect("Animation Showcase Speed Controls", ratAnimationShowcaseCard);
            var speedLayout = speedRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            speedLayout.spacing = 5f;
            speedLayout.childControlWidth = true;
            speedLayout.childControlHeight = true;
            speedLayout.childForceExpandWidth = true;
            speedLayout.childForceExpandHeight = false;
            AddTextTo(speedRow, "Speed", 12, new Color(0.78f, 0.86f, 0.82f), TextAnchor.MiddleLeft);
            AddButtonTo(speedRow, "0.1×", true, () => ratAnimationShowcase.SetPlaybackSpeed(0.1f), new Color(0.22f, 0.30f, 0.32f), 34f);
            AddButtonTo(speedRow, "0.25×", true, () => ratAnimationShowcase.SetPlaybackSpeed(0.25f), new Color(0.14f, 0.25f, 0.28f), 34f);
            AddButtonTo(speedRow, "0.5×", true, () => ratAnimationShowcase.SetPlaybackSpeed(0.5f), new Color(0.14f, 0.25f, 0.28f), 34f);
            AddButtonTo(speedRow, "1×", true, () => ratAnimationShowcase.SetPlaybackSpeed(1f), new Color(0.16f, 0.38f, 0.33f), 34f);
            AddButtonTo(speedRow, "2×", true, () => ratAnimationShowcase.SetPlaybackSpeed(2f), new Color(0.14f, 0.25f, 0.28f), 34f);
            animationShowcaseSpeedText = AddTextTo(speedRow, "1×", 12, new Color(1f, 0.82f, 0.38f), TextAnchor.MiddleRight);

            AddButtonTo(ratAnimationShowcaseCard, "Back to Developer Tools", true, CloseRatAnimationShowcase, new Color(0.14f, 0.22f, 0.25f), 44f);
            RefreshAnimationShowcasePanel();
            SetOverlayVisibility();
        }

        private void BuildEventLogPanel()
        {
            eventLogOverlay = CreateRect("Event Log Overlay", canvas.transform);
            eventLogOverlay.anchorMin = Vector2.zero;
            eventLogOverlay.anchorMax = Vector2.one;
            eventLogOverlay.offsetMin = Vector2.zero;
            eventLogOverlay.offsetMax = Vector2.zero;

            // This full-screen surface is deliberately raycastable. The log
            // is a modal panel when expanded, so world selection, camera
            // dragging, zoom, and habitat buttons cannot receive the same
            // pointer or touch event behind it.
            var blockerImage = eventLogOverlay.gameObject.AddComponent<Image>();
            blockerImage.color = new Color(0.01f, 0.03f, 0.04f, 0.48f);
            blockerImage.raycastTarget = true;

            eventLogCard = CreateRect("Event Log Card", eventLogOverlay);
            eventLogCard.anchorMin = new Vector2(0.5f, 1f);
            eventLogCard.anchorMax = new Vector2(0.5f, 1f);
            eventLogCard.pivot = new Vector2(0.5f, 1f);
            eventLogCard.anchoredPosition = new Vector2(0f, -HeaderHeight - 8f);
            eventLogCard.sizeDelta = new Vector2(ModalMaximumWidth, 360f);
            var cardImage = eventLogCard.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(cardImage, new Color(0.045f, 0.10f, 0.12f, 0.99f), true);
            var cardLayout = eventLogCard.gameObject.AddComponent<VerticalLayoutGroup>();
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;
            cardLayout.spacing = 6f;
            cardLayout.padding = new RectOffset(14, 14, 12, 12);

            var heading = CreateRect("Event Log Heading", eventLogCard);
            var headingElement = heading.gameObject.AddComponent<LayoutElement>();
            headingElement.preferredHeight = 36f;
            headingElement.minHeight = 36f;
            var headingLayout = heading.gameObject.AddComponent<HorizontalLayoutGroup>();
            headingLayout.spacing = 6f;
            headingLayout.childControlWidth = true;
            headingLayout.childControlHeight = true;
            headingLayout.childForceExpandWidth = false;
            headingLayout.childForceExpandHeight = false;
            var headingText = AddTextTo(heading, "Recent events", 16, new Color(0.98f, 0.78f, 0.32f), TextAnchor.MiddleLeft);
            var headingTextElement = headingText.GetComponent<LayoutElement>();
            if (headingTextElement != null) headingTextElement.flexibleWidth = 1f;
            Button collapseButton = AddButtonTo(heading, "Collapse", true, CollapseEventLog,
                new Color(0.14f, 0.25f, 0.28f), 34f);
            var collapseElement = collapseButton.GetComponent<LayoutElement>();
            if (collapseElement != null)
            {
                collapseElement.minWidth = 94f;
                collapseElement.preferredWidth = 94f;
                collapseElement.flexibleWidth = 0f;
            }

            var scrollObject = new GameObject("Event Log Scroll");
            scrollObject.transform.SetParent(eventLogCard, false);
            eventLogScroll = scrollObject.AddComponent<ScrollRect>();
            eventLogScroll.horizontal = false;
            eventLogScroll.vertical = true;
            eventLogScroll.inertia = true;
            eventLogScroll.movementType = ScrollRect.MovementType.Clamped;
            eventLogScroll.scrollSensitivity = 30f;
            var scrollImage = scrollObject.AddComponent<Image>();
            scrollImage.color = new Color(0f, 0f, 0f, 0.08f);
            scrollImage.raycastTarget = true;
            var scrollElement = scrollObject.AddComponent<LayoutElement>();
            scrollElement.preferredHeight = 282f;
            scrollElement.minHeight = 210f;

            var viewport = CreateRect("Event Log Viewport", scrollObject.transform);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            eventLogScroll.viewport = viewport;

            eventLogContent = CreateRect("Event Log Content", viewport);
            eventLogContent.anchorMin = new Vector2(0f, 1f);
            eventLogContent.anchorMax = new Vector2(1f, 1f);
            eventLogContent.pivot = new Vector2(0.5f, 1f);
            eventLogContent.anchoredPosition = Vector2.zero;
            eventLogContent.sizeDelta = Vector2.zero;
            var contentLayout = eventLogContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = 4f;
            contentLayout.padding = new RectOffset(2, 2, 2, 4);
            var contentFitter = eventLogContent.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            eventLogScroll.content = eventLogContent;

            eventLogOverlay.SetAsLastSibling();
            eventLogOpen = false;
            SetOverlayVisibility();
        }

        private void RefreshEventLogPanel()
        {
            if (!eventLogOpen || eventLogContent == null || game == null) return;
            for (int index = eventLogContent.childCount - 1; index >= 0; index--)
            {
                DestroyImmediate(eventLogContent.GetChild(index).gameObject);
            }

            List<ColonyEventData> events = game.RecentEvents;
            if (events == null || events.Count == 0)
            {
                Text empty = AddTextTo(eventLogContent, "No events yet.", 12,
                    new Color(0.72f, 0.82f, 0.78f), TextAnchor.MiddleLeft);
                LayoutElement emptyElement = empty.GetComponent<LayoutElement>();
                if (emptyElement != null)
                {
                    emptyElement.minHeight = 30f;
                    emptyElement.preferredHeight = 30f;
                }
            }
            else
            {
                int count = Mathf.Min(10, events.Count);
                for (int index = 0; index < count; index++)
                {
                    Text row = AddTextTo(eventLogContent, game.FormatEventLogEntry(events[index]), 11,
                        index == 0 ? Color.white : new Color(0.78f, 0.86f, 0.82f), TextAnchor.MiddleLeft);
                    row.alignment = TextAnchor.MiddleLeft;
                    LayoutElement rowElement = row.GetComponent<LayoutElement>();
                    if (rowElement != null)
                    {
                        rowElement.minHeight = 28f;
                        rowElement.preferredHeight = 28f;
                    }
                }
            }

            Canvas.ForceUpdateCanvases();
            if (eventLogScroll != null) eventLogScroll.verticalNormalizedPosition = 1f;
            lastEventLogSignature = game.EventLogSignature;
        }

        private void ToggleEventLog()
        {
            eventLogOpen = !eventLogOpen;
            if (eventLogOpen) RefreshEventLogPanel();
            SetOverlayVisibility();
            RefreshHeader();
        }

        private void CollapseEventLog()
        {
            eventLogOpen = false;
            SetOverlayVisibility();
            RefreshHeader();
        }

        private RectTransform CreateModalOverlay(string name, Color dimColor, out RectTransform card)
        {
            var overlay = CreateRect(name, canvas.transform);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            // Cover the complete canvas so no world raycast, drag, or zoom
            // can leak through a modal. The overlay is placed above page and
            // profile content; the dedicated header Canvas still remains above
            // it as the reliable navigation layer.
            overlay.offsetMax = Vector2.zero;
            var dimmer = overlay.gameObject.AddComponent<Image>();
            dimmer.color = dimColor;
            dimmer.raycastTarget = false;

            // Keep the modal input surface effectively invisible while still
            // registering with EventSystem for mouse, touch, and pinch checks.
            var blocker = CreateRect(name + " Input Blocker", overlay);
            blocker.anchorMin = Vector2.zero;
            blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero;
            blocker.offsetMax = Vector2.zero;
            var blockerImage = blocker.gameObject.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.01f);
            blockerImage.raycastTarget = true;
            overlay.SetAsLastSibling();

            card = CreateRect(name + " Card", overlay);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(ModalMaximumWidth, 0f);
            var cardImage = card.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(cardImage, new Color(0.045f, 0.10f, 0.12f, 0.98f), true);
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 9f;
            layout.padding = new RectOffset(18, 18, 18, 18);
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return overlay;
        }

        private Button AddTopNavigationButton(Transform parent, MainPanel panel, string label)
        {
            var objectRoot = new GameObject(label + " Navigation Button");
            objectRoot.transform.SetParent(parent, false);
            objectRoot.AddComponent<RectTransform>();
            var element = objectRoot.AddComponent<LayoutElement>();
            element.minHeight = 34f;
            element.preferredHeight = 34f;
            element.flexibleWidth = 1f;

            var image = objectRoot.AddComponent<Image>();
            UiStyle.ApplyRounded(image, new Color(0.11f, 0.25f, 0.25f, 1f), true);
            var button = objectRoot.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            // Keep the fixed header on the same guarded input path as all
            // generated controls. This avoids a rebuild leaving the header on
            // a separate direct-listener path while a page relay is still
            // holding the pointer sequence. Configure adds the one listener
            // exactly once and also gives the manual WebGL/mobile fallback a
            // stable hit target.
            var clickRelay = objectRoot.AddComponent<DirectUiClickRelay>();
            clickRelay.Configure(this, button, () => ToggleTopPanel(panel));
            var labelText = AddTextTo(objectRoot.transform, label, 13, Color.white, TextAnchor.MiddleCenter);
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(2f, 1f);
            labelText.rectTransform.offsetMax = new Vector2(-2f, -1f);
            topNavigationButtons[panel] = button;
            topNavigationImages[panel] = image;
            topNavigationLabels[panel] = labelText;
            return button;
        }

        /// <summary>
        /// Reasserts the fixed navigation contract after a page/modal rebuild.
        /// The header itself is created once in BuildShell; only its state is
        /// refreshed, so page content cannot destroy listeners or create a
        /// duplicate set of top buttons.
        /// </summary>
        private void EnsureHeaderNavigationReady()
        {
            if (headerCanvas != null && canvas != null)
            {
                // Critical welcome and newborn naming dialogs intentionally
                // own input until acknowledged. All ordinary pages and
                // optional popups leave the header above page/modal content.
                headerCanvas.overrideSorting = true;
                headerCanvas.sortingOrder = (welcomeOpen || namingOpen)
                    ? canvas.sortingOrder - 1
                    : canvas.sortingOrder + 20;
                Transform headerParent = headerCanvas.transform.parent;
                if (headerParent != null && headerCanvas.transform.GetSiblingIndex() != headerParent.childCount - 1)
                    headerCanvas.transform.SetAsLastSibling();
                if (headerRaycaster == null)
                    headerRaycaster = headerCanvas.gameObject.GetComponent<GraphicRaycaster>();
                if (headerRaycaster == null)
                    headerRaycaster = headerCanvas.gameObject.AddComponent<GraphicRaycaster>();
                headerRaycaster.enabled = true;
                headerRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            }

            foreach (var entry in topNavigationButtons)
            {
                Button button = entry.Value;
                if (button == null) continue;
                if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
                button.interactable = true;
                Image image = button.GetComponent<Image>();
                if (image != null) image.raycastTarget = true;
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.raycastTarget = false;
            }
            foreach (var entry in simulationSpeedButtons)
            {
                Button button = entry.Value;
                if (button == null) continue;
                if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
                button.interactable = true;
                Image image = button.GetComponent<Image>();
                if (image != null) image.raycastTarget = true;
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.raycastTarget = false;
            }
        }

        private Button AddSimulationSpeedButton(Transform parent, float speed)
        {
            int speedInt = Mathf.RoundToInt(speed);
            var button = AddButtonTo(parent, speedInt + "×", true,
                () => game.SetSimulationSpeed(speed), new Color(0.14f, 0.29f, 0.29f), 46f);
            button.gameObject.name = "Simulation Speed " + speedInt + "x";
            simulationSpeedButtons[speedInt] = button;
            simulationSpeedImages[speedInt] = button.GetComponent<Image>();
            simulationSpeedLabels[speedInt] = button.GetComponentInChildren<Text>();
            return button;
        }

        private void ReturnToHabitatFromNavigation()
        {
            // Kept as a compatibility wrapper for older generated callers;
            // all navigation now uses the single tab transition below.
            ToggleTopPanel(MainPanel.Habitat);
        }

        private void ToggleTopPanel(MainPanel panel)
        {
            bool samePanel = activeMainPanel == panel;
            // Multi-select belongs only to the visible My Rats page. Clear it
            // before opening any other page, overlay, profile, or the second
            // tap that collapses My Rats.
            if (game != null && (panel != MainPanel.MyRats || samePanel))
                game.DeactivateMultipleSelection();

            if (panel == MainPanel.Settings)
            {
                if (activeMainPanel == MainPanel.MyRats)
                    ResetMyRatsSortState();
                if (settingsOpen)
                {
                    CloseSettings();
                }
                else
                {
                    OpenSettings();
                }
                return;
            }

            if (panel == MainPanel.DeveloperTools)
            {
                if (activeMainPanel == MainPanel.MyRats)
                    ResetMyRatsSortState();
                if (developerToolsOpen)
                {
                    CloseDeveloperTools();
                }
                else
                {
                    OpenDeveloperTools();
                }
                return;
            }

            // A tab switch owns the page area. Close transient overlays and
            // replace any previous page panel in the same refresh.
            // My Rats is intentionally a fresh roster view each time it is
            // opened. Keep the player's sex filter, but never carry a stale
            // zero-result Pregnancy sort into the next session.
            if (panel == MainPanel.MyRats || activeMainPanel == MainPanel.MyRats)
                ResetMyRatsSortState();
            if (panel != MainPanel.MyRats) expandedMyRatsId = null;
            if (panel != MainPanel.FamilyTree) familyTreeSubjectId = null;
            ResetProfileInformationExpansion();
            settingsOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            // Rename is an optional modal. A top-level navigation choice is
            // authoritative, so close it before rebuilding the requested
            // page instead of leaving its full-screen blocker over the new
            // tab.
            renameOpen = false;
            renameRatId = null;

            if (game != null && game.BreedingOpen && panel != MainPanel.Breeding)
            {
                // Leaving breeding through a top-level panel also clears its
                // temporary parent-selection state, so it cannot reappear
                // behind another panel on the next refresh.
                game.CloseBreeding();
            }

            // A profile is represented by the live selected-rat ID, not by a
            // separate panel flag. Clear that ID before rebuilding the new
            // page so the same refresh cannot select the old default profile
            // after the tab state has changed.
            if (game != null)
                game.ClearSelectionForNavigation();

            activeMainPanel = samePanel ? MainPanel.None : panel;
            // Apply the new input ownership before rebuilding the page. This
            // removes the previous page blocker and clears its pointer state
            // in the same transition that changes the authoritative tab, so
            // a rebuild can never leave an old My Rats/Family Tree shield
            // intercepting the next header tap for one rendered frame.
            SetOverlayVisibility();
            Refresh(true);
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void RefreshTopNavigationState()
        {
            EnsureHeaderNavigationReady();
            foreach (var entry in topNavigationButtons)
            {
                Button button = entry.Value;
                if (button == null) continue;
                Image image;
                if (!topNavigationImages.TryGetValue(entry.Key, out image))
                    image = button.GetComponent<Image>();
                if (image == null) continue;
                bool open = entry.Key == activeMainPanel ||
                    (entry.Key == MainPanel.Settings && settingsOpen) ||
                    (entry.Key == MainPanel.DeveloperTools && developerToolsOpen);
                Text label;
                if (!topNavigationLabels.TryGetValue(entry.Key, out label))
                    label = button.GetComponentInChildren<Text>();
                if (label != null && entry.Key == MainPanel.MyRats)
                {
                    string rosterLabel = "My Rats (" + CountColonyRats() + ")";
                    if (label.text != rosterLabel) label.text = rosterLabel;
                }
                Color targetColor = open
                    ? new Color(0.22f, 0.48f, 0.36f, 1f)
                    : new Color(0.11f, 0.25f, 0.25f, 1f);
                if (image.color != targetColor) image.color = targetColor;
            }
            foreach (var entry in simulationSpeedButtons)
            {
                if (entry.Value == null) continue;
                Image image;
                if (!simulationSpeedImages.TryGetValue(entry.Key, out image))
                    image = entry.Value.GetComponent<Image>();
                if (image != null)
                {
                    Color targetColor = Mathf.Abs(game.SimulationSpeed - entry.Key) < 0.01f
                        ? new Color(0.28f, 0.56f, 0.38f, 1f)
                        : new Color(0.14f, 0.29f, 0.29f, 1f);
                    if (image.color != targetColor) image.color = targetColor;
                }
                Text label;
                if (!simulationSpeedLabels.TryGetValue(entry.Key, out label))
                    label = entry.Value.GetComponentInChildren<Text>();
                string speedLabel = entry.Key + "×";
                if (label != null && label.text != speedLabel) label.text = speedLabel;
            }
        }

        private void SetOverlayVisibility()
        {
            ClearUiPointerState();
            InteractionManager.ResetPointerStateAfterUiTransition();
            if (game != null && namingOpen && !game.HasPendingLitterNaming)
                namingOpen = false;
            // The welcome dialog is the one modal that must be acknowledged
            // before any navigation or speed control is usable. Normally the
            // header deliberately renders above panels, but temporarily place
            // it below this overlay so its raycaster cannot bypass the modal
            // blocker while a genuinely new colony is paused.
            EnsureHeaderNavigationReady();
            if (welcomeOverlay != null) welcomeOverlay.gameObject.SetActive(welcomeOpen);
            if (settingsOverlay != null) settingsOverlay.gameObject.SetActive(settingsOpen && !welcomeOpen && !namingOpen && !renameOpen);
            if (developerToolsOverlay != null) developerToolsOverlay.gameObject.SetActive(developerToolsOpen && !welcomeOpen && !settingsOpen && !namingOpen && !renameOpen);
            if (ratAnimationShowcaseOverlay != null) ratAnimationShowcaseOverlay.gameObject.SetActive(ratAnimationShowcaseOpen && !welcomeOpen && !settingsOpen && !developerToolsOpen && !namingOpen && !renameOpen);
            if (eventLogOverlay != null) eventLogOverlay.gameObject.SetActive(eventLogOpen && !welcomeOpen && !settingsOpen && !developerToolsOpen && !ratAnimationShowcaseOpen && !namingOpen && !renameOpen);
            if (renameOverlay != null) renameOverlay.gameObject.SetActive(renameOpen && !welcomeOpen && !namingOpen);
            if (namingOverlay != null) namingOverlay.gameObject.SetActive(namingOpen && !welcomeOpen);
            if (myRatsInputBlocker != null)
            {
                bool blockWorldForPage = activeMainPanel != MainPanel.None &&
                    activeMainPanel != MainPanel.Habitat &&
                    !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                    !ratAnimationShowcaseOpen && !eventLogOpen && !namingOpen && !renameOpen;
                // This legacy-named shield is the page/world boundary for all
                // generated pages, not just the roster. It remains below the
                // page ScrollRect, so it cannot intercept page controls.
                myRatsInputBlocker.gameObject.SetActive(blockWorldForPage);
            }
            if (familyTreeInputBlocker != null)
            {
                bool blockWorldForFamilyTree = activeMainPanel == MainPanel.FamilyTree &&
                    !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                    !ratAnimationShowcaseOpen && !eventLogOpen && !namingOpen && !renameOpen;
                familyTreeInputBlocker.gameObject.SetActive(blockWorldForFamilyTree);
            }
            // Overlay activation can change sibling order. Reassert both the
            // page/blocker contract and the header contract after all
            // blockers have been toggled so a modal transition cannot leave
            // a full-page shield above the generated controls.
            ReassertPageInputLayerOrder();
            EnsureHeaderNavigationReady();
        }

        /// <summary>
        /// Reasserts the input ownership contract after a page/modal rebuild.
        /// The two transparent world shields must stay below the page
        /// ScrollRect, while a genuinely active modal must be above it. This
        /// is deliberately separate from visual refresh so an exception or a
        /// birth transition cannot leave a page-sized blocker on top of the
        /// generated controls.
        /// </summary>
        private void ReassertPageInputLayerOrder()
        {
            if (safeRoot == null) return;

            if (myRatsInputBlocker != null)
            {
                Image image = myRatsInputBlocker.GetComponent<Image>();
                if (image != null) image.raycastTarget = true;
                myRatsInputBlocker.SetSiblingIndex(0);
            }
            if (familyTreeInputBlocker != null)
            {
                Image image = familyTreeInputBlocker.GetComponent<Image>();
                if (image != null) image.raycastTarget = true;
                familyTreeInputBlocker.SetSiblingIndex(0);
            }

            // Put the normal page surface above both world shields. An active
            // modal is restored above the page immediately afterward.
            if (pageScroll != null && pageScroll.transform.parent == safeRoot)
                pageScroll.transform.SetAsLastSibling();

            RectTransform activeModal = null;
            if (welcomeOpen) activeModal = welcomeOverlay;
            else if (namingOpen) activeModal = namingOverlay;
            else if (renameOpen) activeModal = renameOverlay;
            else if (settingsOpen) activeModal = settingsOverlay;
            else if (developerToolsOpen) activeModal = developerToolsOverlay;
            else if (ratAnimationShowcaseOpen) activeModal = ratAnimationShowcaseOverlay;
            else if (eventLogOpen) activeModal = eventLogOverlay;
            if (activeModal != null && activeModal.gameObject.activeInHierarchy)
                activeModal.SetAsLastSibling();
        }

        /// <summary>
        /// Birth is a structural boundary: the page may have been rebuilt,
        /// the naming modal may have been opened, and the old pointer may
        /// still belong to a destroyed control. Restore all input ownership
        /// in one place without changing the authoritative modal state.
        /// </summary>
        public void RestoreInputStateAfterBirthTransition()
        {
            ClearUiPointerState();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            InteractionManager.ResetPointerStateAfterUiTransition();
            SetOverlayVisibility();
            ReassertPageInputLayerOrder();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogBirthTransitionState("input-state-restored");
#endif
        }

        private void CloseWelcome()
        {
            welcomeOpen = false;
            if (game != null)
            {
                // The acknowledgement button is a real user gesture, so it
                // is the first safe point to request the browser wake lock.
                game.DismissWelcomePopup();
            }
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            activeMainPanel = MainPanel.None;
            SetOverlayVisibility();
            Refresh(true);
        }

        private void OpenSettings()
        {
            if (game != null) game.PrepareForSettings();
            expandedMyRatsId = null;
            familyTreeSubjectId = null;
            ResetProfileInformationExpansion();
            welcomeOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            renameOpen = false;
            settingsOpen = true;
            activeMainPanel = MainPanel.Settings;
            RefreshCustomNameControls();
            SetOverlayVisibility();
            // Settings owns the navigation state immediately. Rebuild the
            // page now so a profile cannot remain underneath as stale page
            // content when the modal opens or when it is later closed.
            Refresh(true);
            RefreshTopNavigationState();
        }

        private void CloseSettings()
        {
            settingsOpen = false;
            activeMainPanel = MainPanel.None;
            SetOverlayVisibility();
            Refresh(true);
        }

        public void OpenWelcomeForNewGame()
        {
            welcomeOpen = true;
            GrowthSystem.SetSimulationPaused(true);
            settingsOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            renameOpen = false;
            namingOpen = false;
            activeMainPanel = MainPanel.None;
            SetOverlayVisibility();
            Refresh(true);
        }

        private void OpenDeveloperTools()
        {
            if (game != null) game.DeactivateMultipleSelection();
            welcomeOpen = false;
            settingsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            renameOpen = false;
            developerToolsOpen = true;
            activeMainPanel = MainPanel.DeveloperTools;
            RebuildDeveloperToolsContent();
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void CloseDeveloperTools()
        {
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            settingsOpen = true;
            eventLogOpen = false;
            renameOpen = false;
            activeMainPanel = MainPanel.Settings;
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        public void CloseTransientPanels()
        {
            if (game != null) game.DeactivateMultipleSelection();
            ResetMyRatsSortState();
            expandedMyRatsId = null;
            familyTreeSubjectId = null;
            profileNavigationFromMyRats = false;
            profileNavigationPreserveState = false;
            welcomeOpen = false;
            settingsOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            renameOpen = false;
            namingOpen = false;
            activeMainPanel = MainPanel.None;
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void OpenRatAnimationShowcase()
        {
            RatVisualDiagnostics.Reset();
            welcomeOpen = false;
            settingsOpen = false;
            developerToolsOpen = false;
            eventLogOpen = false;
            ratAnimationShowcaseOpen = true;
            activeMainPanel = MainPanel.DeveloperTools;
            if (ratAnimationShowcase != null)
            {
                ratAnimationShowcase.Configure(game.RatVisualFactory, FindAnimationShowcaseSample());
            }
            RefreshAnimationShowcasePanel();
            SetOverlayVisibility();
        }

        private void CloseRatAnimationShowcase()
        {
            ratAnimationShowcaseOpen = false;
            developerToolsOpen = true;
            activeMainPanel = MainPanel.DeveloperTools;
            RebuildDeveloperToolsContent();
            SetOverlayVisibility();
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long performanceSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.UiUpdate);
            try { UpdateCore(); }
            finally { RuntimePerformanceDiagnostics.End(PerformanceProbeArea.UiUpdate, performanceSample); }
#else
            UpdateCore();
#endif
        }

        private void UpdateCore()
        {
            bool physicalPointerDown = Input.GetMouseButton(0) || Input.touchCount > 0;
            if (deferredRefreshPending && Time.frameCount > deferredRefreshFrame && !physicalPointerDown)
            {
                // A browser/EventSystem pointer can outlive a generated relay
                // when the page is rebuilt. At this point Unity reports no
                // physical button/touch, so treat that sequence as released
                // and discard any stale relay bookkeeping.
                activeUiPointerIds.Clear();
                fallbackMouseRelay = null;
                fallbackTouchRelay = null;
                bool force = deferredRefreshForce;
                deferredRefreshPending = false;
                deferredRefreshForce = false;
                deferredRefreshFrame = -1;
                if (storePurchaseSucceeded)
                {
                    storePurchaseInProgress = false;
                    storePurchaseSucceeded = false;
                    storePurchaseListingId = null;
                }
                Refresh(force);
            }
            if (profileRefreshDeferred && !IsRatProfileScrollMoving())
                Refresh(true);
            if (profileActivityHistoryDeferred && !IsRatProfileScrollMoving())
            {
                profileActivityHistoryDeferred = false;
                RefreshLiveRatProfile();
            }
            if (ratAnimationShowcaseOpen) RefreshAnimationShowcasePanel();
            UpdateFamilyTreeZoomInput();
        }

        private void UpdateFamilyTreeZoomInput()
        {
            if (activeMainPanel != MainPanel.FamilyTree || familyTreeViewport == null ||
                familyTreeContent == null || !familyTreeViewport.gameObject.activeInHierarchy)
            {
                familyTreePinching = false;
                familyTreeMouseGestureActive = false;
                familyTreeTouchGestureFingerId = -1;
                return;
            }

            // The dedicated controller owns pointer drag, wheel, and pinch
            // input. Keep the legacy state fields synchronized for refresh
            // preservation and diagnostics, but do not run a second zoom
            // implementation in this UI owner.
            if (familyTreePanZoom != null)
            {
                familyTreeZoom = familyTreePanZoom.Zoom;
                familyTreeNormalizedPosition = familyTreePanZoom.NormalizedPosition;
                TrackFamilyTreeGestureDiagnostics();
                return;
            }

            TrackFamilyTreeGestureDiagnostics();

            if (Input.touchCount >= 2)
            {
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                Vector2 midpoint = (first.position + second.position) * 0.5f;
                if (RectTransformUtility.RectangleContainsScreenPoint(familyTreeViewport, midpoint, null))
                {
                    float distance = Vector2.Distance(first.position, second.position);
                    if (!familyTreePinching)
                    {
                        familyTreePinching = true;
                        familyTreeLastPinchDistance = distance;
                    }
                    else if (familyTreeLastPinchDistance > 0.01f && distance > 0.01f)
                    {
                        float scaleFactor = distance / familyTreeLastPinchDistance;
                        if (Mathf.Abs(scaleFactor - 1f) > 0.001f)
                        {
                            SetFamilyTreeZoom(familyTreeZoom * scaleFactor);
                            if (familyTreeScroll != null) familyTreeScroll.StopMovement();
                        }
                        familyTreeLastPinchDistance = distance;
                    }
                    return;
                }
            }

            familyTreePinching = false;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) <= 0.001f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(familyTreeViewport, Input.mousePosition, null)) return;
            SetFamilyTreeZoom(familyTreeZoom * (1f + wheel * 0.12f));
            if (familyTreeScroll != null) familyTreeScroll.StopMovement();
        }

        /// <summary>
        /// The dedicated FamilyTreePanZoomController owns the actual pan
        /// gesture. This development-only observer records the gesture
        /// boundary without becoming another drag handler.
        /// </summary>
        private void TrackFamilyTreeGestureDiagnostics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            familyTreeGestureDiagnosticTimer -= Time.unscaledDeltaTime;
            bool mouseInside = RectTransformUtility.RectangleContainsScreenPoint(
                familyTreeViewport, Input.mousePosition, null);

            if (Input.GetMouseButtonDown(0) && mouseInside)
            {
                familyTreeMouseGestureActive = true;
                familyTreeMouseGesturePosition = Input.mousePosition;
                familyTreeGestureDiagnosticTimer = 0f;
                LogFamilyTreeGestureDiagnostic("pointer-down", Input.mousePosition);
            }
            if (familyTreeMouseGestureActive && Input.GetMouseButton(0))
            {
                Vector2 current = Input.mousePosition;
                if (Vector2.Distance(familyTreeMouseGesturePosition, current) > 2f &&
                    familyTreeGestureDiagnosticTimer <= 0f)
                {
                    familyTreeMouseGesturePosition = current;
                    familyTreeGestureDiagnosticTimer = 0.25f;
                    LogFamilyTreeGestureDiagnostic("drag", current);
                }
            }
            if (familyTreeMouseGestureActive && Input.GetMouseButtonUp(0))
            {
                familyTreeMouseGestureActive = false;
                LogFamilyTreeGestureDiagnostic("pointer-up", Input.mousePosition);
            }

            for (int index = 0; index < Input.touchCount; index++)
            {
                Touch touch = Input.GetTouch(index);
                bool inside = RectTransformUtility.RectangleContainsScreenPoint(
                    familyTreeViewport, touch.position, null);
                if (touch.phase == TouchPhase.Began && inside)
                {
                    familyTreeTouchGestureFingerId = touch.fingerId;
                    familyTreeTouchGesturePosition = touch.position;
                    familyTreeGestureDiagnosticTimer = 0f;
                    LogFamilyTreeGestureDiagnostic("touch-down", touch.position, touch.fingerId);
                }
                else if (touch.fingerId == familyTreeTouchGestureFingerId &&
                    (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary))
                {
                    if (Vector2.Distance(familyTreeTouchGesturePosition, touch.position) > 2f &&
                        familyTreeGestureDiagnosticTimer <= 0f)
                    {
                        familyTreeTouchGesturePosition = touch.position;
                        familyTreeGestureDiagnosticTimer = 0.25f;
                        LogFamilyTreeGestureDiagnostic("drag", touch.position, touch.fingerId);
                    }
                }
                else if (touch.fingerId == familyTreeTouchGestureFingerId &&
                    (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled))
                {
                    LogFamilyTreeGestureDiagnostic("touch-up", touch.position, touch.fingerId);
                    familyTreeTouchGestureFingerId = -1;
                }
            }
#endif
        }

        private void LogFamilyTreeGestureDiagnostic(string phase, Vector2 screenPoint, int pointerId = -1)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string hitName = "none";
            EventSystem eventSystem = EventSystem.current;
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
                    hitName = results[0].gameObject.name;
            }

            Vector2 pan = familyTreeScroll == null
                ? familyTreeNormalizedPosition
                : new Vector2(familyTreeScroll.horizontalNormalizedPosition,
                    familyTreeScroll.verticalNormalizedPosition);
            Debug.Log("[Rat UI FamilyTree] phase=" + phase +
                " pointer=" + pointerId +
                " screen=" + screenPoint +
                " hit=" + hitName +
                " pan=" + pan +
                " zoom=" + familyTreeZoom.ToString("0.###") +
                " treeScroll=" + (familyTreeScroll != null && familyTreeScroll.isActiveAndEnabled) +
                " pageScroll=" + (pageScroll != null && pageScroll.isActiveAndEnabled) +
                " familyTreeBlocker=" + IsBlockerActive(familyTreeInputBlocker) +
                " worldInputBlocked=" + IsModalOverlayOpen +
                " pointerOverUi=" + (eventSystem != null &&
                    (pointerId < 0 ? eventSystem.IsPointerOverGameObject() : eventSystem.IsPointerOverGameObject(pointerId))));
#endif
        }

        private void SetFamilyTreeZoom(float value)
        {
            familyTreeZoom = Mathf.Clamp(value, 0.35f, 2.2f);
            if (familyTreePanZoom != null)
            {
                familyTreePanZoom.SetZoom(familyTreeZoom);
                familyTreeNormalizedPosition = familyTreePanZoom.NormalizedPosition;
                return;
            }
            if (familyTreeContent == null) return;
            familyTreeContent.localScale = Vector3.one * familyTreeZoom;
            Canvas.ForceUpdateCanvases();
        }

        private string BuildFamilyTreeGestureContext()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Image viewportImage = familyTreeViewport == null
                ? null : familyTreeViewport.GetComponent<Image>();
            GraphicRaycaster raycaster = familyTreeViewport == null
                ? null : familyTreeViewport.GetComponentInParent<GraphicRaycaster>();
            CanvasGroup viewportGroup = familyTreeViewport == null
                ? null : familyTreeViewport.GetComponentInParent<CanvasGroup>();
            return "viewport=" + (familyTreeViewport != null && familyTreeViewport.gameObject.activeInHierarchy) +
                " raycast=" + (viewportImage != null && viewportImage.raycastTarget) +
                " graphicRaycaster=" + (raycaster != null && raycaster.isActiveAndEnabled) +
                " mask=" + (familyTreeViewport != null && familyTreeViewport.GetComponent<RectMask2D>() != null) +
                " treeScroll=" + (familyTreeScroll != null && familyTreeScroll.isActiveAndEnabled) +
                ",enabled=" + (familyTreeScroll != null && familyTreeScroll.enabled) +
                " pageScroll=" + (pageScroll != null && pageScroll.isActiveAndEnabled) +
                ",enabled=" + (pageScroll != null && pageScroll.enabled) +
                " pageVertical=" + (pageScroll != null && pageScroll.vertical) +
                " familyBlocker=" + IsBlockerActive(familyTreeInputBlocker) +
                " myRatsBlocker=" + IsBlockerActive(myRatsInputBlocker) +
                " modals=welcome:" + welcomeOpen + ",naming:" + namingOpen +
                ",rename:" + renameOpen + ",settings:" + settingsOpen +
                ",dev:" + developerToolsOpen + ",events:" + eventLogOpen +
                " canvasGroup=" + (viewportGroup == null
                    ? "none" : ("blocks=" + viewportGroup.blocksRaycasts + ",interactable=" + viewportGroup.interactable));
#else
            return string.Empty;
#endif
        }

        private void RefreshAnimationShowcasePanel()
        {
            if (!ratAnimationShowcaseOpen || ratAnimationShowcase == null) return;
            if (animationShowcasePreviewImage != null) animationShowcasePreviewImage.texture = ratAnimationShowcase.PreviewTexture;
            if (animationShowcaseCurrentText != null)
            {
                animationShowcaseCurrentText.text = "Current: " + ratAnimationShowcase.CurrentClipLabel +
                    "\n" + ratAnimationShowcase.CurrentClipName;
            }
            if (animationShowcaseStatusText != null)
            {
                string setup = ratAnimationShowcase.SetupMessage;
                string status = ratAnimationShowcase.GetClipStatus(ratAnimationShowcase.CurrentIndex);
                animationShowcaseStatusText.text = string.IsNullOrEmpty(setup) ? status : setup + "\n" + status;
            }
            if (animationShowcaseSpeedText != null)
            {
                animationShowcaseSpeedText.text = ratAnimationShowcase.PlaybackSpeed.ToString("0.##") + "×";
            }
            if (animationShowcasePlayPauseButton != null)
            {
                SetButtonLabel(animationShowcasePlayPauseButton, ratAnimationShowcase.IsPlaying ? "Pause" : "Play");
            }

            for (int index = 0; index < animationShowcaseRowLabels.Count; index++)
            {
                if (animationShowcaseRowLabels[index] != null)
                {
                    animationShowcaseRowLabels[index].text = ratAnimationShowcase.GetClipRowText(index);
                }
                if (index < animationShowcaseRowButtons.Count && animationShowcaseRowButtons[index] != null)
                {
                    Image image = animationShowcaseRowButtons[index].GetComponent<Image>();
                    if (image != null)
                    {
                        bool active = index == ratAnimationShowcase.CurrentIndex;
                        image.color = active ? new Color(0.28f, 0.46f, 0.34f) :
                            (ratAnimationShowcase.IsClipUsable(index) ? new Color(0.14f, 0.28f, 0.27f) : new Color(0.14f, 0.16f, 0.17f));
                    }
                }
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            Text text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }

        private RatData FindAnimationShowcaseSample()
        {
            if (game != null && game.Save != null && game.Save.rats != null)
            {
                for (int i = 0; i < game.Save.rats.Count; i++)
                {
                    RatData rat = game.Save.rats[i];
                    if (rat != null && rat.stage == RatStage.Adult) return rat;
                }
                for (int i = 0; i < game.Save.rats.Count; i++)
                {
                    RatData rat = game.Save.rats[i];
                    if (rat != null && rat.stage == RatStage.YoungRat) return rat;
                }
            }

            // This object is never added to Save.rats. It is only a safe
            // fallback when a test colony contains Pinkies exclusively.
            return ColonyFactory.CreateRat(
                "animation_showcase_sample",
                "Animation Showcase Sample",
                RatSex.Male,
                GameConfig.StartGameTimeMs,
                0,
                GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s"),
                new TraitData(52f, 90f, 80f),
                RatStage.Adult);
        }

        private void RebuildContent()
        {
            ratRosterScroll = null;
            ratRosterContent = null;
            lastRosterSortSignature = null;
            ratProfileScroll = null;
            liveProfileRatId = null;
            liveProfileActivityText = null;
            liveProfileAgeText = null;
            liveProfileStatsText = null;
            liveProfileHabitatText = null;
            liveProfileReproductiveText = null;
            liveProfileActivityHistoryPanel = null;
            liveProfileActivityHistoryLayout = null;
            liveProfileActivityHistorySignature = null;
            liveActivityHistoryRatId = null;
            profileActivityHistoryDeferred = false;
            storePurchaseButtons.Clear();
            bool preserveFamilyTreeView = activeMainPanel == MainPanel.FamilyTree &&
                familyTreeScroll != null && !familyTreeViewResetRequested &&
                string.Equals(familyTreeRenderedSubjectId, familyTreeSubjectId, StringComparison.Ordinal);
            if (preserveFamilyTreeView)
            {
                if (familyTreePanZoom != null)
                {
                    familyTreeZoom = familyTreePanZoom.Zoom;
                    familyTreeNormalizedPosition = familyTreePanZoom.NormalizedPosition;
                }
                else
                {
                    familyTreeNormalizedPosition = new Vector2(
                        familyTreeScroll.horizontalNormalizedPosition,
                        familyTreeScroll.verticalNormalizedPosition);
                }
            }
            familyTreeScroll = null;
            familyTreeViewport = null;
            familyTreeContent = null;
            familyTreePanZoom = null;
            familyTreePinching = false;
            familyTreeMouseGestureActive = false;
            familyTreeTouchGestureFingerId = -1;
            liveTimedTextUpdates.Clear();
            liveUiRefreshTimer = 0f;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                // Destroy is deferred during Play Mode. Disable the old page
                // first so a clock/selection refresh cannot draw stale profile
                // content for a frame.
                GameObject oldChild = content.GetChild(i).gameObject;
                oldChild.SetActive(false);
                Destroy(oldChild);
            }

            RatData selectedRat = game.SelectedRat;
            HabitatObjectData selectedObject = game.SelectedObject;
            // A live profile owns vertical gestures while it is open. Disable
            // the outer page ScrollRect so a profile drag cannot move the
            // page underneath or compete with the nested information scroll.
            if (pageScroll != null)
            {
                // The family-tree ScrollRect is the sole owner of gestures
                // inside its viewport. Leaving the outer page ScrollRect
                // enabled creates a nested-drag race where the page consumes
                // the pointer before the tree can pan.
                bool familyTreeOwnsGestures = activeMainPanel == MainPanel.FamilyTree;
                pageScroll.enabled = !familyTreeOwnsGestures;
                pageScroll.horizontal = false;
                pageScroll.vertical = !familyTreeOwnsGestures &&
                    !(activeMainPanel == MainPanel.None &&
                        (selectedRat != null || selectedObject != null));
                if (familyTreeOwnsGestures) pageScroll.StopMovement();
            }
            RatData profileRat = string.IsNullOrEmpty(familyTreeSubjectId)
                ? selectedRat
                : BreedingSystem.FindHistoricalRat(game.Save, familyTreeSubjectId);
            if (game.BreedingOpen && activeMainPanel == MainPanel.None)
            {
                activeMainPanel = MainPanel.Breeding;
            }

            switch (activeMainPanel)
            {
                case MainPanel.Habitat:
                    AddEnclosureViewControls(content);
                    break;
                case MainPanel.MyRats:
                    AddRatRoster(content);
                    break;
                case MainPanel.Breeding:
                    if (game.BreedingOpen && (game.Mother != null || game.Father != null))
                    {
                        // The breeding flow replaces the normal profile so its
                        // compact parent comparison starts at the top of the
                        // page and both slots remain visible together.
                        AddBreedingPanel();
                    }
                    else
                    {
                        AddBreedingIntro(content);
                    }
                    break;
                case MainPanel.Store:
                    AddStorePanel(content);
                    break;
                case MainPanel.Upgrades:
                    AddUpgradesPanel(content);
                    break;
                case MainPanel.FamilyTree:
                    AddFamilyTreePanel(content);
                    break;
                default:
                    // With all top-level panels collapsed, only the current
                    // selection profile remains. If nothing is selected the
                    // complete habitat is unobstructed below the header.
                    if (profileRat != null) AddRatProfile(profileRat);
                    if (selectedObject != null) AddObjectProfile(selectedObject);
                    break;
            }
        }

        private void AddBreedingIntro(RectTransform parent)
        {
            var card = CreateCard("Breeding");
            AddText(card, "Select an adult rat in the habitat or My Rats list to begin breeding.", 14,
                new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            if (game != null && game.SelectedRat != null)
            {
                string reason;
                bool eligible = BreedingSystem.IsBreedEligible(game.Save, game.SelectedRat, game.GameTime, out reason);
                AddButton(card, eligible ? "Start breeding with " + ColonyFactory.DisplayName(game.SelectedRat) : "Breeding unavailable — " + reason,
                    eligible, game.OpenBreeding);
            }
        }

        private void AddStorePanel(RectTransform parent)
        {
            if (parent == null || game == null || game.Save == null) return;

            StoreSystem.EnsureStoreState(game.Save);
            var card = CreateCard("Rat Market");
            AddText(card, "Wallet: $" + game.Save.colonyCredits.ToString("N0"), 17,
                new Color(1f, 0.83f, 0.42f), TextAnchor.UpperLeft);
            var categoryRow = CreateRect("Store Categories", card);
            var categoryLayout = categoryRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            categoryLayout.spacing = 5f;
            categoryLayout.childControlWidth = true;
            categoryLayout.childControlHeight = true;
            categoryLayout.childForceExpandWidth = true;
            categoryLayout.childForceExpandHeight = false;
            AddButtonTo(categoryRow, "Buy", true, () => SetStoreCategory(StoreCategory.Buy),
                storeCategory == StoreCategory.Buy ? new Color(0.28f, 0.53f, 0.36f) : new Color(0.14f, 0.29f, 0.29f), 40f);
            AddButtonTo(categoryRow, "Sell", true, () => SetStoreCategory(StoreCategory.Sell),
                storeCategory == StoreCategory.Sell ? new Color(0.28f, 0.53f, 0.36f) : new Color(0.14f, 0.29f, 0.29f), 40f);
            AddButtonTo(categoryRow, "Euthanize", true, () => SetStoreCategory(StoreCategory.Euthanize),
                storeCategory == StoreCategory.Euthanize ? new Color(0.55f, 0.16f, 0.13f) : new Color(0.28f, 0.18f, 0.18f), 40f);

            if (storeCategory == StoreCategory.Sell)
            {
                AddStoreSellPanel(card);
                return;
            }
            if (storeCategory == StoreCategory.Euthanize)
            {
                AddStoreEuthanasiaPanel(card);
                return;
            }

            AddText(card, StoreSystem.GetRestockLabel(game.Save, game.GameTime), 14,
                new Color(0.68f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            AddText(card, "Low-level adult rats for your colony. Each listing keeps its coat, markings, stats, and price until purchased.",
                14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);

            if (game.Save.storeRatListings == null || game.Save.storeRatListings.Count == 0)
            {
                AddText(card, "The market is sold out. New supplies can be added here later.",
                    16, new Color(0.95f, 0.76f, 0.42f), TextAnchor.UpperLeft);
                return;
            }

            foreach (var listing in game.Save.storeRatListings)
            {
                AddStoreListingCard(card, listing);
            }
        }

        private void AddUpgradesPanel(RectTransform parent)
        {
            if (parent == null || game == null || game.Save == null) return;

            var card = CreateCard("Upgrades");
            AddText(card, "Spend dollars to expand the colony and improve future market listings.", 14,
                new Color(0.78f, 0.88f, 0.82f), TextAnchor.UpperLeft);

            int currentCapacity = game.ColonyCapacity;
            int nextCapacity = currentCapacity + GameConfig.ColonyCapacityUpgradeStep;
            int capacityCost = game.ColonyCapacityUpgradeCost;
            AddText(card, "Colony capacity: " + CountColonyRats() + " / " + currentCapacity, 16,
                Color.white, TextAnchor.UpperLeft);
            AddText(card, "Next upgrade: +" + GameConfig.ColonyCapacityUpgradeStep + " spaces (" + nextCapacity + " total)", 13,
                new Color(0.76f, 0.86f, 0.80f), TextAnchor.UpperLeft);
            bool canBuyCapacity = game.Save.colonyCredits >= capacityCost;
            AddButtonTo(card, canBuyCapacity
                    ? "Increase Colony Capacity\n$" + capacityCost
                    : "Increase Colony Capacity\nNeed $" + capacityCost,
                canBuyCapacity, game.PurchaseColonyCapacityUpgrade,
                new Color(0.16f, 0.40f, 0.34f), 58f);

            AddText(card, "Store quality cap: " + game.StoreQualityCap, 16,
                Color.white, TextAnchor.UpperLeft);
            AddText(card, "New listings only: next cap " + (game.StoreQualityCap + GameConfig.StoreQualityUpgradeStep) +
                " (existing listings and rats stay unchanged)", 13,
                new Color(0.76f, 0.86f, 0.80f), TextAnchor.UpperLeft);
            int qualityCost = game.StoreQualityUpgradeCost;
            bool canBuyQuality = game.Save.colonyCredits >= qualityCost;
            AddButtonTo(card, canBuyQuality
                    ? "Improve Store Quality\n$" + qualityCost
                    : "Improve Store Quality\nNeed $" + qualityCost,
                canBuyQuality, game.PurchaseStoreQualityUpgrade,
                new Color(0.24f, 0.38f, 0.50f), 58f);

            AddText(card, "Wallet: $" + game.Save.colonyCredits.ToString("N0"), 16,
                new Color(1f, 0.83f, 0.42f), TextAnchor.UpperLeft);
        }

        private void SetStoreCategory(StoreCategory category)
        {
            storeCategory = category;
            // Store categories are page-level controls. Always bring the
            // category bar back into view before rebuilding so a Sell list
            // cannot leave the Buy control above the current scroll position.
            if (pageScroll != null)
            {
                pageScroll.StopMovement();
                pageScroll.verticalNormalizedPosition = 1f;
            }
            lastSignature = string.Empty;
            Refresh(true);
        }

        private void OpenStoreForRatManagement(string ratId, bool euthanize)
        {
            storeCategory = euthanize ? StoreCategory.Euthanize : StoreCategory.Sell;
            activeMainPanel = MainPanel.Store;
            if (game != null) game.SelectRatFromRoster(ratId);
            if (game != null)
            {
                if (euthanize) game.RequestEuthanizeSelectedRat();
                else game.RequestSellSelectedRat();
            }
            SetOverlayVisibility();
            Refresh(true);
        }

        private void AddStoreSellPanel(RectTransform parent)
        {
            AddText(parent, "Sell rats for dollars. Historical parent, litter, and offspring records are kept.", 14,
                new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddStoreManagedRatRows(parent, false);
        }

        private void AddStoreEuthanasiaPanel(RectTransform parent)
        {
            AddText(parent, "Euthanasia is destructive and costs $" + GameConfig.EuthanasiaCostDollars + " per rat. This cannot be undone.", 14,
                new Color(1f, 0.52f, 0.38f), TextAnchor.UpperLeft);
            if (game.EuthanizeConfirmationPending && game.SelectedRat != null)
            {
                AddText(parent, "FINAL CONFIRMATION: euthanize " + ColonyFactory.DisplayName(game.SelectedRat) + " for $" + GameConfig.EuthanasiaCostDollars + "? " + game.SelectedRatRemovalWarning,
                    13, new Color(1f, 0.42f, 0.30f), TextAnchor.UpperLeft);
                AddButtonTo(parent, "CONFIRM EUTHANIZE RAT", true, game.ConfirmEuthanizeSelectedRat, new Color(0.70f, 0.12f, 0.10f), 50f);
                AddButtonTo(parent, "Cancel euthanasia", true, game.CancelEuthanizeSelectedRat, new Color(0.20f, 0.30f, 0.34f), 42f);
            }
            AddStoreManagedRatRows(parent, true);
        }

        private void AddStoreManagedRatRows(RectTransform parent, bool euthanize)
        {
            var listRoot = CreateRect("Store Rat List", parent);
            var listImage = listRoot.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(listImage, new Color(0.04f, 0.10f, 0.13f, 0.82f), false);
            // This list owns the touch/drag gesture. The old Store cards were
            // decorative and non-raycast, so scrolling over them fell through
            // to the habitat instead of reaching a ScrollRect.
            listImage.raycastTarget = true;
            var listElement = listRoot.gameObject.AddComponent<LayoutElement>();
            listElement.preferredHeight = 430f;
            listElement.minHeight = 260f;

            var list = listRoot.gameObject.AddComponent<ScrollRect>();
            storeRatListScroll = list;
            list.horizontal = false;
            list.vertical = true;
            list.inertia = true;
            list.movementType = ScrollRect.MovementType.Elastic;
            list.scrollSensitivity = 30f;

            var viewport = CreateRect("Store Rat List Viewport", listRoot);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0.02f, 0.05f, 0.06f, 0.20f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            list.viewport = viewport;

            var listContent = CreateRect("Store Rat List Content", viewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.sizeDelta = Vector2.zero;
            var contentLayout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = 6f;
            var contentFitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            list.content = listContent;

            if (game.Save.rats == null || game.Save.rats.Count == 0)
            {
                AddTextTo(listContent, "No active colony rats.", 14, new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                return;
            }
            foreach (var rat in game.Save.rats)
            {
                if (rat == null) continue;
                string ratId = rat.id;
                bool canSell = euthanize || game.CanSellRat(rat);
                string action = euthanize
                    ? "EUTHANIZE\n$" + GameConfig.EuthanasiaCostDollars
                    : canSell ? "SELL\n$" + game.SellValue(rat) : "SELL\nUnavailable";
                AddStoreRatCard(listContent, rat, action,
                    euthanize ? new Color(0.55f, 0.16f, 0.13f) :
                        canSell ? new Color(0.22f, 0.42f, 0.28f) : new Color(0.25f, 0.29f, 0.29f),
                    () =>
                    {
                        if (euthanize) OpenStoreForRatManagement(ratId, true);
                        else game.RequestSellRat(ratId);
                    },
                    !euthanize && canSell && game.IsSellConfirmationFor(ratId),
                    canSell);
            }
        }

        private void AddStoreRatCard(Transform parent, RatData rat, string actionLabel, Color actionColor,
            UnityEngine.Events.UnityAction action, bool inlineSaleConfirmation = false,
            bool actionInteractable = true)
        {
            if (parent == null || rat == null) return;

            // Sell cards can contain a wrapped coat/markings line plus the
            // pregnancy status and, when needed, a sale restriction/warning.
            // Leave enough vertical room for those rows instead of forcing
            // them to draw into each other on narrow screens.
            float cardHeight = inlineSaleConfirmation ? 218f : 158f;
            const float portraitSize = 88f;
            const float actionWidth = 94f;
            var card = CreateRect("Store Management Card " + rat.id, parent);
            var cardImage = card.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(cardImage, new Color(0.10f, 0.18f, 0.17f, 1f), true);
            cardImage.raycastTarget = false;
            var cardElement = card.gameObject.AddComponent<LayoutElement>();
            cardElement.preferredHeight = cardHeight;
            cardElement.minHeight = cardHeight;

            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 8, 8);
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var portraitRoot = CreateRect("Store Rat Portrait", card);
            var portraitLayout = portraitRoot.gameObject.AddComponent<LayoutElement>();
            portraitLayout.preferredWidth = portraitSize;
            portraitLayout.minWidth = portraitSize;
            portraitLayout.preferredHeight = portraitSize;
            portraitLayout.minHeight = portraitSize;
            var portrait = portraitRoot.gameObject.AddComponent<RawImage>();
            portrait.texture = portraitPreview == null ? null : portraitPreview.GetPortrait(rat);
            portrait.color = Color.white;
            portrait.raycastTarget = false;
            ValidatePortraitSlot(portraitRoot, "store management " + rat.id);

            var info = CreateRect("Store Rat Information", card);
            var infoLayout = info.gameObject.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 1f;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            var infoElement = info.gameObject.AddComponent<LayoutElement>();
            infoElement.flexibleWidth = 1f;
            infoElement.minHeight = portraitSize;
            infoElement.preferredHeight = portraitSize;

            string coat = rat.phenotype == null || !rat.phenotype.furRevealed ? "Unknown" : rat.phenotype.coatColorLabel;
            string markings = rat.phenotype == null || !rat.phenotype.furRevealed ? "Hidden" : rat.phenotype.markingsLabel;
            TraitData traits = rat.traits ?? new TraitData();
            AddText(info, ColonyFactory.DisplayName(rat) + "  •  " + SexLabel(rat.sex) + "  •  " + GrowthSystem.StageLabel(rat.stage), 14, Color.white, TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(info, "Coat: " + coat + "  •  " + markings, 12, new Color(1f, 0.84f, 0.52f), TextAnchor.UpperLeft);
            AddText(info, "Size " + traits.size.ToString("0") + "  •  Health " + traits.health.ToString("0") + "  •  Fertility " + traits.fertility.ToString("0"),
                12, Color.white, TextAnchor.UpperLeft);
            Text pregnancyStatus = AddText(info, string.Empty, 12,
                new Color(0.95f, 0.73f, 0.34f), TextAnchor.UpperLeft);
            BindLiveText(pregnancyStatus, () => SellPregnancyStatus(rat));
            if (!actionInteractable)
            {
                AddText(info, game.SaleRestrictionReason(rat), 11,
                    new Color(1f, 0.63f, 0.42f), TextAnchor.UpperLeft);
            }

            if (inlineSaleConfirmation)
            {
                AddSaleWarningsIfAny(info, rat, 11);
                AddInlineSaleControls(card, actionWidth);
            }
            else
            {
                var actionButton = AddButtonTo(card, actionLabel, actionInteractable, action, actionColor, 58f);
                var actionLayout = actionButton.GetComponent<LayoutElement>();
                actionLayout.minWidth = actionWidth;
                actionLayout.preferredWidth = actionWidth;
                actionLayout.flexibleWidth = 0f;
            }
        }

        private void AddInlineSaleControls(Transform parent, float width)
        {
            var controls = CreateRect("Inline Sale Confirmation Controls", parent);
            var element = controls.gameObject.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            var layout = controls.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            AddText(controls, "Are you\nsure?", 11, new Color(1f, 0.80f, 0.42f), TextAnchor.MiddleCenter);
            AddButtonTo(controls, "Confirm\nSale", true, game.ConfirmSellSelectedRat,
                new Color(0.20f, 0.46f, 0.29f), 42f);
            AddButtonTo(controls, "Cancel", true, game.CancelSellSelectedRat,
                new Color(0.20f, 0.30f, 0.34f), 38f);
        }

        private void AddStoreListingCard(Transform parent, StoreRatListingData listing)
        {
            if (parent == null || listing == null) return;

            RatData previewRat = StoreSystem.CreatePreviewRat(listing, game.GameTime);
            TraitData listingTraits = listing.traits ?? new TraitData();
            var card = CreateRect("Rat Market Listing " + listing.id, parent);
            var cardImage = card.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(cardImage, new Color(0.10f, 0.18f, 0.17f, 1f), true);
            // The card is a decorative container. Let its child Buy button
            // receive the pointer instead of making the whole listing an
            // opaque hit surface.
            cardImage.raycastTarget = false;
            var cardElement = card.gameObject.AddComponent<LayoutElement>();
            cardElement.preferredHeight = 128f;
            cardElement.minHeight = 128f;

            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 8, 8);
            row.spacing = 9f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            const float portraitSize = 104f;
            var portraitRoot = CreateRect("Market Rat Portrait", card);
            var portraitLayout = portraitRoot.gameObject.AddComponent<LayoutElement>();
            portraitLayout.preferredWidth = portraitSize;
            portraitLayout.minWidth = portraitSize;
            portraitLayout.preferredHeight = portraitSize;
            portraitLayout.minHeight = portraitSize;
            var portrait = portraitRoot.gameObject.AddComponent<RawImage>();
            portrait.texture = portraitPreview == null ? null : portraitPreview.GetPortrait(previewRat);
            portrait.color = Color.white;
            portrait.uvRect = new Rect(0f, 0f, 1f, 1f);
            portrait.raycastTarget = false;
            ValidatePortraitSlot(portraitRoot, "market listing " + listing.id);

            var info = CreateRect("Market Rat Information", card);
            var infoLayout = info.gameObject.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 1f;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            var infoElement = info.gameObject.AddComponent<LayoutElement>();
            infoElement.flexibleWidth = 1f;
            infoElement.minHeight = portraitSize;
            infoElement.preferredHeight = portraitSize;

            AddText(info, ColonyFactory.DisplayName(listing) + "  •  " + SexLabel(listing.sex) + "  •  Adult", 14, Color.white, TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            string fur = previewRat == null || previewRat.phenotype == null ? "Unknown" : previewRat.phenotype.coatColorLabel;
            string markings = previewRat == null || previewRat.phenotype == null ? "Unknown" : previewRat.phenotype.markingsLabel;
            AddText(info, "Coat: " + fur + "  •  " + markings, 13, new Color(1f, 0.84f, 0.52f), TextAnchor.UpperLeft);
            AddText(info, "Size " + listingTraits.size.ToString("0") + "  •  Health " + listingTraits.health.ToString("0") + "  •  Fertility " + listingTraits.fertility.ToString("0"),
                12, Color.white, TextAnchor.UpperLeft);

            bool canBuy = game.Save.colonyCredits >= listing.price;
            Button actionButton = null;
            string listingId = listing.id;
            actionButton = AddButtonTo(card, "BUY\n$" + listing.price, canBuy,
                () =>
                {
                    if (game == null || !BeginStorePurchaseAttempt(listingId)) return;
                    if (!game.BuyStoreRat(listingId))
                        CompleteStorePurchaseAttempt(listingId, false);
                }, new Color(0.16f, 0.40f, 0.34f), 58f);
            storePurchaseButtons[listingId] = actionButton;
            var actionLayout = actionButton.GetComponent<LayoutElement>();
            actionLayout.minWidth = 94f;
            actionLayout.preferredWidth = 94f;
            actionLayout.flexibleWidth = 0f;
        }

        private void AddEnclosureViewControls(RectTransform parent)
        {
            if (parent == null || game == null) return;
            var card = CreateCard(game.HabitatPageLabel);
            AddText(card, "Swipe left or right to move between full-size habitats.",
                13, new Color(0.78f, 0.9f, 0.82f), TextAnchor.UpperLeft);
            AddText(card, "Pairing Habitat: " + game.PairingHabitatCount + " / " + game.PairingHabitatCapacity + " spaces",
                13, new Color(1f, 0.84f, 0.52f), TextAnchor.UpperLeft);

            var pagerRow = CreateRect("Habitat Pager Controls", card);
            var pagerLayout = pagerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            pagerLayout.spacing = 8f;
            pagerLayout.childAlignment = TextAnchor.MiddleCenter;
            pagerLayout.childControlWidth = true;
            pagerLayout.childControlHeight = true;
            pagerLayout.childForceExpandWidth = true;
            pagerLayout.childForceExpandHeight = false;
            bool canGoPrevious = game.HabitatPageIndex > 0;
            bool canGoNext = game.HabitatPageIndex < game.HabitatPageCount - 1;
            AddButtonTo(pagerRow, "‹ Previous", canGoPrevious,
                () => RunHabitatPageAction(-1), new Color(0.14f, 0.30f, 0.31f), 42f);
            AddButtonTo(pagerRow, "Next ›", canGoNext,
                () => RunHabitatPageAction(1), new Color(0.14f, 0.30f, 0.31f), 42f);

            if (game.PairingMoveAllConfirmationPending)
            {
                AddText(card, "Move every rat out of Pairing Habitat? Mothers and pups will stay together in a normal habitat.",
                    13, new Color(1f, 0.55f, 0.36f), TextAnchor.UpperLeft);
                AddButtonTo(card, "CONFIRM MOVE ALL OUT", true, game.ConfirmMoveAllOutOfPairingHabitat,
                    new Color(0.65f, 0.22f, 0.16f), 46f);
                AddButtonTo(card, "Cancel Pairing evacuation", true, game.CancelMoveAllOutOfPairingHabitat,
                    new Color(0.20f, 0.30f, 0.34f), 42f);
            }
            else
            {
                AddButtonTo(card, "Move All Out of Pairing Habitat", true, game.RequestMoveAllOutOfPairingHabitat,
                    new Color(0.35f, 0.28f, 0.18f), 44f);
            }

        }

        private void RunHabitatPageAction(int direction)
        {
            if (game != null) game.TryNavigateHabitatSwipe(direction);
            if (activeMainPanel != MainPanel.Habitat) return;

            // Keep the existing compact-habitat workflow: once a destination
            // is chosen with the desktop buttons, the world is unobstructed.
            activeMainPanel = MainPanel.None;
            Refresh(true);
            RefreshTopNavigationState();
        }

        private void RunHabitatViewAction(Action viewAction)
        {
            if (viewAction != null) viewAction();
            if (activeMainPanel != MainPanel.Habitat) return;

            // The selected view is already applied by the action above. Hide
            // the Habitat controls afterward so the camera can be inspected
            // immediately, while preserving the chosen enclosure focus.
            activeMainPanel = MainPanel.None;
            Refresh(true);
            RefreshTopNavigationState();
        }

        private void AddRatProfile(RatData rat)
        {
            liveProfileRatId = rat == null ? null : rat.id;
            if (profileMoreInformationRatId != rat.id)
            {
                bool preserveNavigationState = profileNavigationPreserveState;
                profileMoreInformationRatId = rat.id;
                if (!preserveNavigationState)
                {
                    profileMoreInformationExpanded = false;
                    // Selecting a different rat normally opens a fresh
                    // profile at the top. Arrow navigation is the deliberate
                    // exception: it preserves the current profile layout.
                    ratProfileScrollNormalized = 1f;
                }
                ratProfileScrollRatId = rat.id;
            }

            var card = CreateCard(string.Empty);
            // This profile is a cutout over the ordinary Game view. Keep the
            // card itself transparent so the Main Camera can show the focused
            // live rat through the reserved portrait row; the details panel
            // below remains an opaque readable UI surface.
            var cardImage = card.GetComponent<Image>();
            if (cardImage != null)
            {
                cardImage.color = new Color(0f, 0f, 0f, 0f);
                cardImage.raycastTarget = false;
            }

            // CreateCard normally sizes itself from a vertical content stack.
            // A profile is different: it is a fixed phone-safe frame with a
            // lower-anchored information surface. Disable the generic stack
            // so the details panel can grow upward without pushing the live
            // habitat opening or changing the selected rat's camera view.
            var profileCardLayout = card.GetComponent<VerticalLayoutGroup>();
            if (profileCardLayout != null) profileCardLayout.enabled = false;
            var profileCardFitter = card.GetComponent<ContentSizeFitter>();
            if (profileCardFitter != null) profileCardFitter.enabled = false;
            var profileFrameElement = card.gameObject.AddComponent<LayoutElement>();
            profileFrameElement.minHeight = RatProfileFrameHeight;
            profileFrameElement.preferredHeight = RatProfileFrameHeight;
            profileFrameElement.flexibleHeight = 0f;

            AddRatPortrait(card, rat);

            // The information scroll is bottom-anchored with a bottom-safe
            // padding. Its height changes with More Information, so the top
            // edge moves upward while the lower edge stays stable.
            var detailsScrollRoot = CreateRect("Rat Profile Information Scroll", card);
            detailsScrollRoot.anchorMin = new Vector2(0f, 0f);
            detailsScrollRoot.anchorMax = new Vector2(1f, 0f);
            detailsScrollRoot.pivot = new Vector2(0.5f, 0f);
            detailsScrollRoot.offsetMin = new Vector2(12f, RatProfileBottomPadding);
            detailsScrollRoot.offsetMax = new Vector2(-12f,
                RatProfileBottomPadding + (profileMoreInformationExpanded
                    ? RatProfileExpandedInformationHeight
                    : RatProfileCollapsedInformationHeight));
            var detailsScrollImage = detailsScrollRoot.gameObject.AddComponent<Image>();
            detailsScrollImage.color = new Color(0.01f, 0.03f, 0.04f, 0.03f);
            detailsScrollImage.raycastTarget = true;
            var detailsScrollElement = detailsScrollRoot.gameObject.AddComponent<LayoutElement>();
            detailsScrollElement.ignoreLayout = true;
            var detailsScroll = detailsScrollRoot.gameObject.AddComponent<ScrollRect>();
            detailsScroll.horizontal = false;
            detailsScroll.vertical = true;
            detailsScroll.inertia = true;
            detailsScroll.movementType = ScrollRect.MovementType.Elastic;
            detailsScroll.scrollSensitivity = 30f;

            var detailsViewport = CreateRect("Rat Profile Information Viewport", detailsScrollRoot);
            detailsViewport.anchorMin = Vector2.zero;
            detailsViewport.anchorMax = Vector2.one;
            detailsViewport.offsetMin = Vector2.zero;
            detailsViewport.offsetMax = Vector2.zero;
            var detailsViewportImage = detailsViewport.gameObject.AddComponent<Image>();
            detailsViewportImage.color = new Color(0f, 0f, 0f, 0.02f);
            detailsViewportImage.raycastTarget = true;
            detailsViewport.gameObject.AddComponent<RectMask2D>();
            detailsScroll.viewport = detailsViewport;

            // Keep a compact, phone-safe scrollbar attached to the same
            // profile ScrollRect. It is generated once with the profile and
            // follows the content position instead of being rebuilt by timer
            // text updates.
            var scrollbarRoot = CreateRect("Rat Profile Scrollbar", detailsScrollRoot);
            scrollbarRoot.anchorMin = new Vector2(1f, 0f);
            scrollbarRoot.anchorMax = new Vector2(1f, 1f);
            scrollbarRoot.pivot = new Vector2(1f, 0.5f);
            scrollbarRoot.offsetMin = new Vector2(-10f, 4f);
            scrollbarRoot.offsetMax = new Vector2(-3f, -4f);
            var scrollbarTrack = scrollbarRoot.gameObject.AddComponent<Image>();
            scrollbarTrack.color = new Color(0.08f, 0.16f, 0.17f, 0.72f);
            var scrollbarHandle = CreateRect("Rat Profile Scrollbar Handle", scrollbarRoot);
            scrollbarHandle.anchorMin = new Vector2(0f, 0f);
            scrollbarHandle.anchorMax = new Vector2(1f, 0.35f);
            scrollbarHandle.offsetMin = Vector2.zero;
            scrollbarHandle.offsetMax = Vector2.zero;
            var scrollbarHandleImage = scrollbarHandle.gameObject.AddComponent<Image>();
            scrollbarHandleImage.color = new Color(0.42f, 0.68f, 0.48f, 0.95f);
            var scrollbar = scrollbarRoot.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = scrollbarHandleImage;
            scrollbar.handleRect = scrollbarHandle;
            scrollbarRoot.SetAsLastSibling();
            detailsScroll.verticalScrollbar = scrollbar;
            detailsScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            detailsScroll.verticalScrollbarSpacing = -2f;

            var detailsContent = CreateRect("Rat Profile Information Content", detailsViewport);
            detailsContent.anchorMin = new Vector2(0f, 1f);
            detailsContent.anchorMax = new Vector2(1f, 1f);
            detailsContent.pivot = new Vector2(0.5f, 1f);
            detailsContent.anchoredPosition = Vector2.zero;
            detailsContent.sizeDelta = Vector2.zero;
            var detailsContentLayout = detailsContent.gameObject.AddComponent<VerticalLayoutGroup>();
            detailsContentLayout.childControlWidth = true;
            detailsContentLayout.childControlHeight = true;
            detailsContentLayout.childForceExpandWidth = true;
            detailsContentLayout.childForceExpandHeight = false;
            detailsContentLayout.spacing = 3f;
            var detailsContentFitter = detailsContent.gameObject.AddComponent<ContentSizeFitter>();
            detailsContentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            detailsContentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            detailsScroll.content = detailsContent;
            ratProfileScroll = detailsScroll;
            ratProfileScrollRatId = rat.id;
            detailsScroll.verticalNormalizedPosition = ratProfileScrollNormalized;

            var details = CreateRect("Rat Profile Details", detailsContent);
            var detailsImage = details.gameObject.AddComponent<Image>();
            bool historical = IsHistoricalRat(rat);
            UiStyle.ApplyRounded(detailsImage, historical
                ? new Color(0.13f, 0.16f, 0.17f, 0.96f)
                : new Color(0.045f, 0.10f, 0.12f, 0.96f), false);
            detailsImage.raycastTarget = false;
            if (historical)
            {
                var historicalGroup = details.gameObject.AddComponent<CanvasGroup>();
                historicalGroup.alpha = 0.78f;
            }
            var detailsLayout = details.gameObject.AddComponent<VerticalLayoutGroup>();
            detailsLayout.childControlWidth = true;
            detailsLayout.childControlHeight = true;
            detailsLayout.childForceExpandWidth = true;
            detailsLayout.childForceExpandHeight = false;
            detailsLayout.spacing = 3f;
            detailsLayout.padding = new RectOffset(12, 12, 10, 10);

            AddRatProfileHeader(details, rat);
            // Active rats do not need an "Alive" label. Keep status text only
            // for historical records where it communicates something useful.
            string historicalStatus = historical &&
                (rat.removalDisposition == RatRemovalDisposition.NaturalDeath ||
                 rat.removalDisposition == RatRemovalDisposition.Euthanized ||
                 rat.removalDisposition == RatRemovalDisposition.Sold)
                ? HistoricalStatusLabel(rat)
                : string.Empty;
            if (!string.IsNullOrEmpty(historicalStatus))
            {
                AddText(details, historicalStatus, 13,
                    new Color(0.85f, 0.86f, 0.84f), TextAnchor.UpperLeft);
            }
            Text activityText = AddText(details, string.Empty, 14,
                new Color(1f, 0.82f, 0.38f), TextAnchor.UpperLeft);
            liveProfileActivityText = activityText;
            BindLiveText(activityText, () => "Current activity: " + game.CurrentRatActivityLabel(rat));

            AddRatProfileBasicInformation(details, rat, 14);
            AddButtonTo(details,
                profileMoreInformationExpanded ? "More Information  ▴" : "More Information  ▾",
                true, ToggleProfileMoreInformation,
                new Color(0.18f, 0.35f, 0.39f), 42f);

            if (profileMoreInformationExpanded)
            {
                AddExpandedRatProfileInformation(details, rat, historical);
            }

            if (rat.removalDisposition == RatRemovalDisposition.Sold)
            {
                AddSoldStamp(card);
            }

            liveProfileActivityHistorySignature = BuildRatActivityHistorySignature(rat);
        }

        private void AddRatProfileHeader(RectTransform parent, RatData rat)
        {
            if (parent == null || rat == null) return;
            List<RatData> navigationRats = GetProfileNavigationRats(rat);
            int currentIndex = -1;
            for (int index = 0; index < navigationRats.Count; index++)
            {
                if (navigationRats[index] != null && navigationRats[index].id == rat.id)
                {
                    currentIndex = index;
                    break;
                }
            }

            var header = CreateRect("Rat Profile Header", parent);
            var headerElement = header.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = 50f;
            headerElement.preferredHeight = 50f;
            var layout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            bool hasPrevious = currentIndex > 0;
            bool hasNext = currentIndex >= 0 && currentIndex < navigationRats.Count - 1;
            Button previous = AddButtonTo(header, "←", hasPrevious,
                hasPrevious ? (UnityEngine.Events.UnityAction)(() => NavigateProfile(-1)) : null,
                hasPrevious ? new Color(0.18f, 0.38f, 0.40f) : new Color(0.14f, 0.16f, 0.17f), 50f);
            Button next = null;
            var previousLayout = previous.GetComponent<LayoutElement>();
            if (previousLayout != null)
            {
                previousLayout.minWidth = 54f;
                previousLayout.preferredWidth = 54f;
                previousLayout.flexibleWidth = 0f;
            }

            var nameRoot = CreateRect("Rat Profile Header Name", header);
            var nameLayout = nameRoot.gameObject.AddComponent<LayoutElement>();
            nameLayout.minWidth = 80f;
            nameLayout.flexibleWidth = 1f;
            var nameText = AddTextTo(nameRoot, ColonyFactory.DisplayName(rat), 18,
                new Color(0.98f, 0.78f, 0.32f), TextAnchor.MiddleCenter);
            nameText.fontStyle = FontStyle.Bold;
            nameText.rectTransform.anchorMin = Vector2.zero;
            nameText.rectTransform.anchorMax = Vector2.one;
            nameText.rectTransform.offsetMin = new Vector2(2f, 0f);
            nameText.rectTransform.offsetMax = new Vector2(-2f, 0f);

            next = AddButtonTo(header, "→", hasNext,
                hasNext ? (UnityEngine.Events.UnityAction)(() => NavigateProfile(1)) : null,
                hasNext ? new Color(0.18f, 0.38f, 0.40f) : new Color(0.14f, 0.16f, 0.17f), 50f);
            var nextLayout = next.GetComponent<LayoutElement>();
            if (nextLayout != null)
            {
                nextLayout.minWidth = 54f;
                nextLayout.preferredWidth = 54f;
                nextLayout.flexibleWidth = 0f;
            }
        }

        private List<RatData> GetProfileNavigationRats(RatData current)
        {
            var result = new List<RatData>();
            if (game == null || game.Save == null || game.Save.rats == null || current == null)
                return result;

            foreach (RatData rat in game.Save.rats)
            {
                if (rat == null || rat.removalDisposition != RatRemovalDisposition.None) continue;
                if (profileNavigationFromMyRats)
                {
                    if (!IsRosterRatVisible(rat)) continue;
                }
                else if (rat.enclosure != current.enclosure)
                {
                    continue;
                }
                result.Add(rat);
            }

            if (profileNavigationFromMyRats)
                result.Sort(CompareRosterRats);
            else
            {
                result.Sort((left, right) =>
                {
                    int comparison = string.Compare(ColonyFactory.DisplayName(left),
                        ColonyFactory.DisplayName(right), StringComparison.OrdinalIgnoreCase);
                    if (comparison != 0) return comparison;
                    return string.Compare(left.id, right.id, StringComparison.OrdinalIgnoreCase);
                });
            }
            return result;
        }

        private void NavigateProfile(int direction)
        {
            if (game == null || game.SelectedRat == null || direction == 0) return;
            List<RatData> navigationRats = GetProfileNavigationRats(game.SelectedRat);
            int currentIndex = -1;
            for (int index = 0; index < navigationRats.Count; index++)
            {
                if (navigationRats[index] != null && navigationRats[index].id == game.SelectedRat.id)
                {
                    currentIndex = index;
                    break;
                }
            }
            int nextIndex = currentIndex + (direction < 0 ? -1 : 1);
            if (currentIndex < 0 || nextIndex < 0 || nextIndex >= navigationRats.Count) return;
            RatData target = navigationRats[nextIndex];
            if (target == null || string.IsNullOrEmpty(target.id)) return;

            // Arrow navigation intentionally keeps the profile's expanded
            // state and current nested-scroll position. The selected rat is
            // changed by stable ID only; no world action or highlight is
            // triggered by the navigation control.
            profileNavigationPreserveState = true;
            game.SelectRatForProfileNavigation(target.id);
        }

        private void AddRatProfileBasicInformation(RectTransform parent, RatData rat, int fontSize)
        {
            if (parent == null || rat == null) return;
            TraitData traits = rat.traits ?? new TraitData();
            liveProfileAgeText = AddText(parent, string.Empty, fontSize, Color.white, TextAnchor.UpperLeft);
            BindLiveText(liveProfileAgeText, () => "Age: " + GrowthSystem.FormatAge(rat.ageDays));
            liveProfileStatsText = AddText(parent, string.Empty, fontSize, Color.white, TextAnchor.UpperLeft);
            BindLiveText(liveProfileStatsText, () =>
            {
                TraitData currentTraits = rat.traits ?? traits;
                return "Size " + currentTraits.size.ToString("0") +
                    "  •  Health " + currentTraits.health.ToString("0") +
                    "  •  Fertility " + currentTraits.fertility.ToString("0");
            });
            liveProfileReproductiveText = AddText(parent, string.Empty, fontSize,
                new Color(0.72f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            BindLiveText(liveProfileReproductiveText, () => ReproductiveStateLabel(rat));
            PregnancyData pregnancy = FindPregnancyForFemale(rat);
            if (pregnancy != null)
                AddPregnancyProgress(parent, pregnancy, false);
        }

        private void AddBasicRatProfileInformation(RectTransform parent, RatData rat, int fontSize)
        {
            if (parent == null || rat == null) return;
            TraitData traits = rat.traits ?? new TraitData();
            AddText(parent, "Age: " + GrowthSystem.FormatAge(rat.ageDays), fontSize,
                Color.white, TextAnchor.UpperLeft);
            AddText(parent, "Size " + traits.size.ToString("0") +
                "  •  Health " + traits.health.ToString("0") +
                "  •  Fertility " + traits.fertility.ToString("0"), fontSize,
                Color.white, TextAnchor.UpperLeft);
        }

        private void AddExpandedRatProfileInformation(RectTransform parent, RatData rat, bool historical)
        {
            if (parent == null || rat == null) return;

            var more = CreateRect("Rat Profile More Information", parent);
            var moreImage = more.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(moreImage, new Color(0.02f, 0.07f, 0.08f, historical ? 0.74f : 0.84f), false);
            moreImage.raycastTarget = false;
            var moreLayout = more.gameObject.AddComponent<VerticalLayoutGroup>();
            moreLayout.spacing = 3f;
            moreLayout.padding = new RectOffset(8, 8, 7, 7);
            moreLayout.childControlWidth = true;
            moreLayout.childControlHeight = true;
            moreLayout.childForceExpandWidth = true;
            moreLayout.childForceExpandHeight = false;

            AddExpandedRatProfileDetails(more, rat, 14);
            AddRatActivityHistory(more, rat);

            AddButtonTo(more, "Rename Rat", true, () => OpenRenameModal(rat.id),
                new Color(0.20f, 0.34f, 0.38f), 42f);

            AddButtonTo(more, "Family Tree", true, () => OpenFamilyTree(rat.id),
                new Color(0.22f, 0.34f, 0.43f), 42f);

            bool liveRat = !historical && BreedingSystem.FindRat(game.Save, rat.id) != null;
            if (liveRat && game.CanSellRat(rat) && game.IsSellConfirmationFor(rat.id))
            {
                AddInlineProfileSaleConfirmation(more, rat);
            }
            else if (liveRat && game.CanSellRat(rat))
            {
                AddButtonTo(more, "Sell Rat\n$" + game.SellValue(rat), true,
                    () => game.RequestSellRat(rat.id), new Color(0.17f, 0.34f, 0.37f), 46f);
            }
            else if (liveRat)
            {
                AddText(more, game.SaleRestrictionReason(rat), 13,
                    new Color(1f, 0.63f, 0.42f), TextAnchor.UpperLeft);
            }

            if (liveRat && rat.enclosure == RatEnclosure.Pairing)
            {
                bool dependentLitter = rat.sex == RatSex.Female && EnclosureSystem.HasDependentPinkies(game.Save, rat.id);
                bool pendingPregnancy = EnclosureSystem.IsPregnant(game.Save, rat);
                bool canRemove = !((rat.stage == RatStage.Adult || rat.stage == RatStage.Mature) && rat.sex == RatSex.Female &&
                    (dependentLitter || pendingPregnancy));
                AddButton(more, canRemove ? "Remove from Pairing Habitat" : "Remain in Pairing Habitat while caring for litter",
                    canRemove, () => game.RemoveRatFromPairingHabitat(rat.id));
            }
            else if (liveRat)
            {
                // Pass the profile's ID explicitly. The profile may have been
                // opened from family-history navigation, so the global world
                // selection is not a safe source for this action.
                AddButton(more, "Move to Pairing Habitat", true,
                    () => game.MoveRatToPairingHabitat(rat.id));
            }

            PregnancyData pregnancy = liveRat ? FindPregnancyForFemale(rat) : null;
            if (pregnancy != null)
            {
                AddPregnancyProgress(more, pregnancy, false);
            }
            else if (liveRat)
            {
                string reason;
                bool canBreed = BreedingSystem.IsBreedEligible(game.Save, rat, game.GameTime, out reason);
                var breedButton = AddButton(more, canBreed ? "Breed" : "Breed unavailable — " + reason, canBreed, game.OpenBreeding);
                breedButton.gameObject.name = "Breed Button";
            }

            UnityEngine.Events.UnityAction returnAction = liveRat
                ? new UnityEngine.Events.UnityAction(game.ReturnToHabitat)
                : new UnityEngine.Events.UnityAction(() => OpenFamilyTree(rat.id));
            AddButton(more, liveRat ? "Return to Habitat" : "Back to Family Tree", true, returnAction);
        }

        private void AddExpandedRatProfileDetails(RectTransform parent, RatData rat, int fontSize)
        {
            if (parent == null || rat == null) return;
            string coat = rat.phenotype == null || !rat.phenotype.furRevealed
                ? "Hidden while Pinkie"
                : rat.phenotype.coatColorLabel;
            string markings = rat.phenotype == null || !rat.phenotype.furRevealed
                ? "Hidden while Pinkie"
                : rat.phenotype.markingsLabel;

            AddText(parent, "Stage: " + GrowthSystem.StageLabel(rat.stage) +
                "  •  Sex: " + SexLabel(rat.sex) +
                "  •  Generation: " + rat.generation, fontSize, Color.white, TextAnchor.UpperLeft);
            AddText(parent, "Coat: " + coat + "  •  Markings: " + markings, fontSize,
                new Color(0.95f, 0.83f, 0.55f), TextAnchor.UpperLeft);
            Text reproductiveStateText = AddText(parent, string.Empty, fontSize,
                new Color(0.72f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            BindLiveText(reproductiveStateText, () => "Reproductive state: " + ReproductiveStateLabel(rat));
            AddText(parent, "Known genes: " + KnownGeneSummary(rat), fontSize - 1,
                new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddText(parent, "Parents: " + ParentSummary(rat) + "  •  Litter: " + LitterNameForRat(rat), fontSize - 1,
                new Color(0.70f, 0.78f, 0.74f), TextAnchor.UpperLeft);
            liveProfileHabitatText = AddText(parent, string.Empty, fontSize,
                new Color(0.78f, 0.90f, 0.82f), TextAnchor.UpperLeft);
            BindLiveText(liveProfileHabitatText, () => "Current habitat: " + EnclosureSystem.Label(rat.enclosure));
        }

        private void ToggleProfileMoreInformation()
        {
            bool wasExpanded = profileMoreInformationExpanded;
            profileMoreInformationExpanded = !profileMoreInformationExpanded;
            if (!wasExpanded && profileMoreInformationExpanded)
            {
                profileScrollResetRequested = true;
                ratProfileScrollNormalized = 1f;
            }
            Refresh(true);
        }

        private void ResetProfileInformationExpansion()
        {
            profileMoreInformationRatId = null;
            profileMoreInformationExpanded = false;
            profileNavigationFromMyRats = false;
            profileNavigationPreserveState = false;
            ratProfileScrollRatId = null;
            ratProfileScrollNormalized = 1f;
            profileScrollResetRequested = false;
            profileRefreshDeferred = false;
            profileActivityHistoryDeferred = false;
            liveProfileRatId = null;
            liveProfileActivityText = null;
            liveProfileAgeText = null;
            liveProfileStatsText = null;
            liveProfileHabitatText = null;
            liveProfileReproductiveText = null;
            liveProfileActivityHistoryPanel = null;
            liveProfileActivityHistoryLayout = null;
            liveProfileActivityHistorySignature = null;
            liveActivityHistoryRatId = null;
            lastProfileStructureSignature = null;
        }

        private void AddInlineProfileSaleConfirmation(RectTransform parent, RatData rat)
        {
            var confirmation = CreateRect("Inline Profile Sale Confirmation", parent);
            var image = confirmation.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(image, new Color(0.22f, 0.25f, 0.18f, 0.96f), true);
            image.raycastTarget = false;
            var layout = confirmation.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.padding = new RectOffset(8, 8, 7, 7);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            AddText(confirmation, "Are you sure? Sell " + ColonyFactory.DisplayName(rat) + " for $" + game.SellValue(rat) + "?", 13,
                new Color(1f, 0.80f, 0.42f), TextAnchor.UpperLeft);
            AddSaleWarningsIfAny(confirmation, rat, 12);
            var actions = CreateRect("Profile Sale Confirmation Actions", confirmation);
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 5f;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = true;
            actionLayout.childForceExpandHeight = false;
            AddButtonTo(actions, "Confirm Sale", true, game.ConfirmSellSelectedRat,
                new Color(0.20f, 0.46f, 0.29f), 42f);
            AddButtonTo(actions, "Cancel", true, game.CancelSellSelectedRat,
                new Color(0.20f, 0.30f, 0.34f), 42f);
        }

        private void AddSaleWarningsIfAny(Transform parent, RatData rat, int fontSize)
        {
            if (parent == null || game == null || rat == null) return;
            string warnings = game.SaleWarningsFor(rat);
            if (string.IsNullOrEmpty(warnings) || warnings == "None") return;
            AddTextTo(parent, "Warnings: " + warnings + ".", fontSize,
                new Color(1f, 0.72f, 0.38f), TextAnchor.UpperLeft);
        }

        private string SellPregnancyStatus(RatData rat)
        {
            if (rat == null) return "Pregnancy: Unknown";
            if (rat.sex != RatSex.Female) return "Pregnancy: Not applicable";

            PregnancyData pregnancy = FindPregnancyForFemale(rat);
            if (pregnancy == null) return "Pregnancy: Not pregnant";

            int percentage = Mathf.Clamp(Mathf.RoundToInt(
                BreedingSystem.PregnancyProgress01(pregnancy, game.GameTime) * 100f), 0, 100);
            long remainingMs = Math.Max(0L, pregnancy.dueAt - game.GameTime);
            return "Pregnancy: " + percentage + "%  •  " + FormatRemainingTime(remainingMs);
        }

        private void AddObjectProfile(HabitatObjectData selected)
        {
            var card = CreateCard(selected.label + "  •  Habitat object");
            AddText(card, "Condition: " + selected.condition.ToString("0") + "%", 15, Color.white, TextAnchor.UpperLeft);
            AddText(card, "Tap the service button to keep this object ready for the colony.", 14, new Color(0.75f, 0.82f, 0.77f), TextAnchor.UpperLeft);
            AddButton(card, ServiceLabel(selected.type), true, game.ServiceSelectedObject);
        }

        private sealed class FamilyTreeEntry
        {
            public string path;
            public string parentPath;
            public RatData rat;
            public int level;
            public int index;
            public Vector2 center;
        }

        private void OpenFamilyTree(string ratId)
        {
            if (game == null || BreedingSystem.FindHistoricalRat(game.Save, ratId) == null) return;
            if (activeMainPanel != MainPanel.FamilyTree ||
                !string.Equals(familyTreeSubjectId, ratId, StringComparison.Ordinal))
            {
                familyTreeZoom = FamilyTreeInitialZoom;
                familyTreeNormalizedPosition = new Vector2(0.5f, 0.5f);
                familyTreeViewResetRequested = true;
            }
            ResetProfileInformationExpansion();
            familyTreeSubjectId = ratId;
            activeMainPanel = MainPanel.FamilyTree;
            Refresh(true);
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void ReturnToFamilyTreeProfile()
        {
            familyTreeViewResetRequested = true;
            ResetProfileInformationExpansion();
            activeMainPanel = MainPanel.None;
            Refresh(true);
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void SelectFamilyTreeSubject(string ratId)
        {
            RatData rat = game == null ? null : BreedingSystem.FindHistoricalRat(game.Save, ratId);
            if (rat == null) return;
            if (!string.Equals(familyTreeSubjectId, ratId, StringComparison.Ordinal))
            {
                familyTreeZoom = FamilyTreeInitialZoom;
                familyTreeNormalizedPosition = new Vector2(0.5f, 0.5f);
                familyTreeViewResetRequested = true;
            }
            familyTreeSubjectId = ratId;
            // Family-tree navigation is informational. Do not change the
            // habitat selection or camera while the player is exploring
            // ancestors and descendants; the selected tree card is the
            // active family-tree subject.
            Refresh(true);
        }

        private void AddFamilyTreePanel(RectTransform parent)
        {
            RatData subject = game == null || game.Save == null
                ? null
                : BreedingSystem.FindHistoricalRat(game.Save, familyTreeSubjectId);
            if (subject == null)
            {
                var missing = CreateCard("Family Tree");
                AddText(missing, "No family history is available for this rat.", 14,
                    new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                AddButtonTo(missing, "Back to Profile", true, ReturnToFamilyTreeProfile,
                    new Color(0.14f, 0.30f, 0.30f), 42f);
                return;
            }

            var card = CreateCard("Family Tree");
            AddText(card, "Selected rat: " + ColonyFactory.DisplayName(subject) + ". Tap any family member to select them.", 13,
                new Color(0.78f, 0.90f, 0.82f), TextAnchor.UpperLeft);
            AddText(card, "Drag to pan • Mouse wheel or pinch to zoom", 12,
                new Color(0.70f, 0.82f, 0.76f), TextAnchor.UpperLeft);
            AddButtonTo(card, "Back to " + ColonyFactory.DisplayName(subject) + " Profile", true, ReturnToFamilyTreeProfile,
                new Color(0.14f, 0.30f, 0.30f), 42f);

            var scrollRoot = CreateRect("Family Tree Scroll", card);
            var scrollImage = scrollRoot.gameObject.AddComponent<Image>();
            scrollImage.color = new Color(0.02f, 0.06f, 0.07f, 0.95f);
            scrollImage.raycastTarget = true;
            var scrollElement = scrollRoot.gameObject.AddComponent<LayoutElement>();
            scrollElement.preferredHeight = 520f;
            scrollElement.minHeight = 360f;
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            familyTreeScroll = scroll;
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.inertia = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;
            // Keep the component reference for diagnostics and migration,
            // but do not let the nested ScrollRect compete with the dedicated
            // FamilyTreePanZoomController below for the same pointer stream.
            scroll.enabled = false;

            var viewport = CreateRect("Family Tree Viewport", scrollRoot);
            familyTreeViewport = viewport;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.02f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            const float nodeWidth = 210f;
            const float nodeHeight = 154f;
            const float levelSpacing = 184f;
            const float treeMargin = 32f;
            var treeContent = CreateRect("Family Tree Content", viewport);
            familyTreeContent = treeContent;
            treeContent.anchorMin = new Vector2(0f, 1f);
            treeContent.anchorMax = new Vector2(0f, 1f);
            treeContent.pivot = new Vector2(0f, 1f);
            treeContent.anchoredPosition = Vector2.zero;
            bool resetTreeView = familyTreeViewResetRequested ||
                !string.Equals(familyTreeRenderedSubjectId, familyTreeSubjectId, StringComparison.Ordinal);
            if (resetTreeView)
            {
                familyTreeZoom = FamilyTreeInitialZoom;
                familyTreeNormalizedPosition = new Vector2(0.5f, 0.5f);
            }
            treeContent.localScale = Vector3.one * familyTreeZoom;
            scroll.content = treeContent;
            familyTreePanZoom = viewport.gameObject.AddComponent<FamilyTreePanZoomController>();

            var entries = new List<FamilyTreeEntry>();
            BuildFamilyTreeAncestors(subject, 0, "root", null, entries);
            BuildFamilyTreeDescendants(subject, 0, "root", entries, new HashSet<string> { subject.id });

            var entriesByLevel = new Dictionary<int, List<FamilyTreeEntry>>();
            int minimumLevel = 0;
            int maximumLevel = 0;
            int widestLevel = 1;
            foreach (var entry in entries)
            {
                List<FamilyTreeEntry> levelEntries;
                if (!entriesByLevel.TryGetValue(entry.level, out levelEntries))
                {
                    levelEntries = new List<FamilyTreeEntry>();
                    entriesByLevel.Add(entry.level, levelEntries);
                }
                levelEntries.Add(entry);
                minimumLevel = Mathf.Min(minimumLevel, entry.level);
                maximumLevel = Mathf.Max(maximumLevel, entry.level);
                widestLevel = Mathf.Max(widestLevel, levelEntries.Count);
            }

            // Keep the focused rat in the visual middle even when only one
            // side of the family exists. Empty rows provide breathing room
            // instead of pushing a root to the top or bottom edge.
            int displayMinimumLevel = Mathf.Min(minimumLevel, -maximumLevel);
            int displayMaximumLevel = Mathf.Max(maximumLevel, -minimumLevel);
            float treeWidth = Mathf.Max(1100f, widestLevel * (nodeWidth + 24f) + treeMargin * 2f);
            float treeHeight = (displayMaximumLevel - displayMinimumLevel + 1) * levelSpacing + treeMargin * 2f;
            treeContent.sizeDelta = new Vector2(treeWidth, treeHeight);

            foreach (var level in entriesByLevel)
            {
                level.Value.Sort((first, second) => first.index.CompareTo(second.index));
                float usableWidth = treeWidth - treeMargin * 2f;
                for (int index = 0; index < level.Value.Count; index++)
                {
                    FamilyTreeEntry entry = level.Value[index];
                    entry.index = index;
                    float x = treeMargin + (index + 0.5f) * usableWidth / level.Value.Count;
                    float y = treeMargin + (entry.level - displayMinimumLevel + 0.5f) * levelSpacing;
                    entry.center = new Vector2(x, y);
                }
            }

            var byPath = new Dictionary<string, FamilyTreeEntry>();
            foreach (var entry in entries) byPath[entry.path] = entry;

            // Draw relationship lines first so every node remains readable.
            foreach (var entry in entries)
            {
                if (entry.path == "root") continue;
                FamilyTreeEntry parentEntry;
                if (!byPath.TryGetValue(entry.parentPath, out parentEntry)) continue;
                Vector2 start = parentEntry.center;
                Vector2 end = entry.center;
                if (entry.level < parentEntry.level)
                {
                    start += new Vector2(0f, -nodeHeight * 0.5f);
                    end += new Vector2(0f, nodeHeight * 0.5f);
                }
                else
                {
                    start += new Vector2(0f, nodeHeight * 0.5f);
                    end += new Vector2(0f, -nodeHeight * 0.5f);
                }
                AddFamilyTreeLine(treeContent, start, end);
            }

            foreach (var entry in entries)
            {
                AddFamilyTreeNode(treeContent, entry, nodeWidth, nodeHeight);
            }
            Canvas.ForceUpdateCanvases();
            familyTreePanZoom.Configure(viewport, treeContent, familyTreeZoom,
                familyTreeNormalizedPosition, ResolveUiFont(), BuildFamilyTreeGestureContext);
            familyTreeRenderedSubjectId = familyTreeSubjectId;
            familyTreeViewResetRequested = false;
        }

        private void BuildFamilyTreeAncestors(RatData rat, int level, string path, string parentPath,
            List<FamilyTreeEntry> entries)
        {
            entries.Add(new FamilyTreeEntry
            {
                path = path,
                parentPath = parentPath,
                rat = rat,
                level = level,
                index = level == 0 ? 0 : entries.Count,
            });
            if (rat == null || level <= -4) return;

            RatData mother = rat == null ? null : BreedingSystem.FindHistoricalRat(game.Save, rat.motherId);
            RatData father = rat == null ? null : BreedingSystem.FindHistoricalRat(game.Save, rat.fatherId);
            BuildFamilyTreeAncestors(mother, level - 1, path + "M", path, entries);
            BuildFamilyTreeAncestors(father, level - 1, path + "F", path, entries);
        }

        private void BuildFamilyTreeDescendants(RatData rat, int level, string path,
            List<FamilyTreeEntry> entries, HashSet<string> visited)
        {
            if (rat == null || level >= 4) return;
            List<RatData> children = FindHistoricalChildren(rat.id);
            for (int index = 0; index < children.Count; index++)
            {
                RatData child = children[index];
                if (child == null || string.IsNullOrEmpty(child.id) || !visited.Add(child.id)) continue;
                string childPath = path + "/C" + index;
                entries.Add(new FamilyTreeEntry
                {
                    path = childPath,
                    parentPath = path,
                    rat = child,
                    level = level + 1,
                    index = index,
                });
                BuildFamilyTreeDescendants(child, level + 1, childPath, entries, visited);
            }
        }

        private List<RatData> FindHistoricalChildren(string parentId)
        {
            var result = new List<RatData>();
            if (game == null || game.Save == null || string.IsNullOrEmpty(parentId)) return result;
            var seen = new HashSet<string>();
            AddHistoricalChildrenFromList(game.Save.rats, parentId, seen, result);
            AddHistoricalChildrenFromList(game.Save.retiredRats, parentId, seen, result);
            result.Sort((first, second) =>
            {
                int timestamp = first.birthTimestamp.CompareTo(second.birthTimestamp);
                if (timestamp != 0) return timestamp;
                int name = string.Compare(first.name, second.name, StringComparison.OrdinalIgnoreCase);
                return name != 0 ? name : string.Compare(first.id, second.id, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static void AddHistoricalChildrenFromList(List<RatData> source, string parentId,
            HashSet<string> seen, List<RatData> result)
        {
            if (source == null) return;
            foreach (var candidate in source)
            {
                if (candidate == null || string.IsNullOrEmpty(candidate.id) ||
                    (candidate.motherId != parentId && candidate.fatherId != parentId) || !seen.Add(candidate.id)) continue;
                result.Add(candidate);
            }
        }

        private void AddFamilyTreeLine(RectTransform parent, Vector2 start, Vector2 end)
        {
            var line = CreateRect("Family Tree Connection", parent);
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(0f, 1f);
            line.pivot = new Vector2(0f, 0.5f);
            Vector2 delta = end - start;
            float distance = delta.magnitude;
            line.anchoredPosition = new Vector2(start.x, -start.y);
            line.sizeDelta = new Vector2(distance, 2f);
            line.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);
            var image = line.gameObject.AddComponent<Image>();
            image.color = new Color(0.42f, 0.62f, 0.55f, 0.82f);
            image.raycastTarget = false;
            line.SetAsFirstSibling();
        }

        private void AddFamilyTreeNode(RectTransform parent, FamilyTreeEntry entry, float width, float height)
        {
            var node = CreateRect(entry.rat == null ? "Unknown Family Node" : "Family Node", parent);
            node.anchorMin = new Vector2(0f, 1f);
            node.anchorMax = new Vector2(0f, 1f);
            node.pivot = new Vector2(0.5f, 0.5f);
            node.anchoredPosition = new Vector2(entry.center.x, -entry.center.y);
            node.sizeDelta = new Vector2(width, height);
            var image = node.gameObject.AddComponent<Image>();
            bool historical = IsHistoricalRat(entry.rat);
            bool selectedSubject = entry.path == "root";
            UiStyle.ApplyRounded(image, entry.rat == null
                ? new Color(0.10f, 0.13f, 0.14f, 0.94f)
                : (historical ? new Color(0.23f, 0.25f, 0.25f, 0.96f)
                    : (selectedSubject ? new Color(0.18f, 0.32f, 0.20f, 0.98f)
                        : new Color(0.08f, 0.18f, 0.17f, 0.96f))), true);
            image.raycastTarget = entry.rat != null;
            var layout = node.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.padding = new RectOffset(7, 7, 5, 5);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            if (entry.rat == null)
            {
                AddText(node, "Unknown", 13, new Color(0.70f, 0.75f, 0.72f), TextAnchor.MiddleCenter);
                AddText(node, "Ancestor not recorded", 10, new Color(0.58f, 0.64f, 0.61f), TextAnchor.MiddleCenter);
                return;
            }

            // Use the same cached rotating portrait renderer as My Rats,
            // Store, and breeding. This is presentation-only: it reuses the
            // actual RatData phenotype without adding a selectable world rat.
            var portraitRow = CreateRect("Family Tree Rat Portrait Row", node);
            var portraitRowLayout = portraitRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            portraitRowLayout.spacing = 6f;
            portraitRowLayout.childAlignment = TextAnchor.MiddleCenter;
            portraitRowLayout.childControlWidth = false;
            portraitRowLayout.childControlHeight = true;
            portraitRowLayout.childForceExpandWidth = false;
            portraitRowLayout.childForceExpandHeight = false;
            var portraitRowElement = portraitRow.gameObject.AddComponent<LayoutElement>();
            portraitRowElement.preferredHeight = 48f;
            portraitRowElement.minHeight = 48f;

            var portraitRoot = CreateRect("Family Tree Rat Portrait", portraitRow);
            var portraitLayout = portraitRoot.gameObject.AddComponent<LayoutElement>();
            portraitLayout.preferredWidth = 48f;
            portraitLayout.minWidth = 48f;
            portraitLayout.preferredHeight = 48f;
            portraitLayout.minHeight = 48f;
            ConfigureSquarePortraitSlot(portraitRoot, 48f);
            var portrait = portraitRoot.gameObject.AddComponent<RawImage>();
            portrait.texture = portraitPreview == null ? null : portraitPreview.GetPortrait(entry.rat);
            portrait.color = historical ? new Color(0.62f, 0.64f, 0.63f, 1f) : Color.white;
            portrait.uvRect = new Rect(0f, 0f, 1f, 1f);
            portrait.raycastTarget = false;
            ValidatePortraitSlot(portraitRoot, "family tree " + entry.rat.id);

            string ratId = entry.rat.id;
            var button = node.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var relay = node.gameObject.AddComponent<DirectUiClickRelay>();
            relay.Configure(this, button, () => SelectFamilyTreeSubject(ratId));

            string coat = entry.rat.phenotype == null || !entry.rat.phenotype.furRevealed
                ? "Hidden" : entry.rat.phenotype.coatColorLabel;
            string markings = entry.rat.phenotype == null || !entry.rat.phenotype.furRevealed
                ? "Hidden" : entry.rat.phenotype.markingsLabel;
            AddText(node, ColonyFactory.DisplayName(entry.rat), 12,
                historical ? new Color(0.92f, 0.94f, 0.92f) : Color.white,
                TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            AddText(node, SexLabel(entry.rat.sex) + " • " + GrowthSystem.StageLabel(entry.rat.stage), 10,
                new Color(0.86f, 0.91f, 0.87f), TextAnchor.MiddleCenter);
            AddText(node, coat + " • " + markings, 9, new Color(1f, 0.84f, 0.52f), TextAnchor.MiddleCenter);
            AddText(node, HistoricalStatusLabel(entry.rat), 9,
                historical ? new Color(0.82f, 0.84f, 0.82f) : new Color(0.65f, 0.86f, 0.72f), TextAnchor.MiddleCenter);
            if (entry.rat.removalDisposition == RatRemovalDisposition.Sold) AddSoldStamp(node);
        }

        private void AddSoldStamp(RectTransform parent)
        {
            if (parent == null) return;
            var stamp = CreateRect("SOLD Stamp", parent);
            stamp.anchorMin = new Vector2(0.5f, 0.5f);
            stamp.anchorMax = new Vector2(0.5f, 0.5f);
            stamp.pivot = new Vector2(0.5f, 0.5f);
            stamp.anchoredPosition = Vector2.zero;
            stamp.sizeDelta = new Vector2(150f, 34f);
            var stampLayout = stamp.gameObject.AddComponent<LayoutElement>();
            stampLayout.ignoreLayout = true;
            var image = stamp.gameObject.AddComponent<Image>();
            image.color = new Color(0.86f, 0.05f, 0.04f, 0.58f);
            image.raycastTarget = false;
            stamp.localRotation = Quaternion.Euler(0f, 0f, -12f);
            var label = AddTextTo(stamp, "SOLD", 17, new Color(1f, 0.92f, 0.90f), TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
        }

        private static bool IsHistoricalRat(RatData rat)
        {
            return rat != null && rat.removalDisposition != RatRemovalDisposition.None;
        }

        private static string HistoricalStatusLabel(RatData rat)
        {
            if (rat == null) return "Unknown";
            switch (rat.removalDisposition)
            {
                case RatRemovalDisposition.Sold: return "Sold";
                case RatRemovalDisposition.Euthanized: return "Euthanized";
                case RatRemovalDisposition.NaturalDeath: return "Died";
                case RatRemovalDisposition.Deleted: return "Removed";
                default: return "Alive • " + EnclosureSystem.Label(rat.enclosure);
            }
        }

        private void AddRatRoster(RectTransform parent)
        {
            if (parent == null || game == null || game.Save == null) return;

            int ratCount = CountVisibleColonyRats();
            var card = CreateCard("My Rats");
            string countSummary = rosterSortField == RosterSortField.Pregnancy
                ? ratCount + " pregnant rats shown"
                : ratCount + " shown of " + CountColonyRats() + " colony rats";
            AddText(card, countSummary + "  •  " + RosterSexFilterLabel() +
                "  •  Sort: " + RosterSortLabel(), 13,
                new Color(0.78f, 0.9f, 0.82f), TextAnchor.UpperLeft);
            AddButtonTo(card, "Close My Rats", true, () => ToggleTopPanel(MainPanel.MyRats),
                new Color(0.14f, 0.22f, 0.25f), 40f);
            AddRosterSexFilters(card);
            AddRosterSortControls(card);
            AddButtonTo(card, game.MultipleSelectionMode ? "Stop Select Multiple" : "Select Multiple", true,
                game.ToggleMultipleSelectionMode,
                game.MultipleSelectionMode ? new Color(0.28f, 0.50f, 0.34f) : new Color(0.18f, 0.34f, 0.36f), 44f);
            if (game.MultipleSelectionMode)
            {
                AddText(card, game.SelectedGroupCount + " rats selected. Tap rows or habitat rats to toggle them, then choose a destination.",
                    13, new Color(0.78f, 0.9f, 0.82f), TextAnchor.UpperLeft);
                var groupRow = CreateRect("Group Move Controls", card);
                var groupLayout = groupRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                groupLayout.spacing = 4f;
                groupLayout.childControlWidth = true;
                groupLayout.childControlHeight = true;
                groupLayout.childForceExpandWidth = true;
                groupLayout.childForceExpandHeight = false;
                AddButtonTo(groupRow, "Male", true, () => game.RequestMoveSelectedRats(RatEnclosure.MaleColony), new Color(0.15f, 0.33f, 0.29f), 40f);
                AddButtonTo(groupRow, "Female", true, () => game.RequestMoveSelectedRats(RatEnclosure.FemaleColony), new Color(0.15f, 0.33f, 0.29f), 40f);
                AddButtonTo(groupRow, "Pairing", true, () => game.RequestMoveSelectedRats(RatEnclosure.Pairing), new Color(0.25f, 0.38f, 0.24f), 40f);
                AddButtonTo(card, "Sell selected rats", true, game.RequestSellSelectedRats,
                    new Color(0.24f, 0.40f, 0.28f), 42f);
                if (game.GroupSellConfirmationPending)
                {
                    AddText(card, "Confirm sale of the selected rats? Young pups must be 42 days old and fully weaned; Elderly rats cannot be sold.",
                        12, new Color(1f, 0.72f, 0.38f), TextAnchor.UpperLeft);
                    AddButtonTo(card, "CONFIRM GROUP SALE", true, game.ConfirmSellSelectedRats,
                        new Color(0.20f, 0.46f, 0.29f), 44f);
                    AddButtonTo(card, "Cancel group sale", true, game.CancelSellSelectedRats,
                        new Color(0.20f, 0.30f, 0.34f), 40f);
                }
                if (game.GroupMoveConfirmationPending)
                {
                    AddText(card, "A mother or dependent litter is included. Confirm only if separating them is intentional.",
                        13, new Color(1f, 0.55f, 0.36f), TextAnchor.UpperLeft);
                    AddButtonTo(card, "CONFIRM GROUP MOVE", true, game.ConfirmMoveSelectedRats,
                        new Color(0.65f, 0.22f, 0.16f), 46f);
                    AddButtonTo(card, "Cancel group move", true, game.CancelMoveSelectedRats,
                        new Color(0.20f, 0.30f, 0.34f), 42f);
                }
            }

            var listRoot = CreateRect("My Rats List", card);
            var listImage = listRoot.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(listImage, new Color(0.04f, 0.10f, 0.13f, 0.82f), false);
            // The list background is decorative. The viewport below is the
            // only scroll hit surface, so an empty-state list can never
            // become a transparent blocker over the controls above it.
            listImage.raycastTarget = false;
            var listElement = listRoot.gameObject.AddComponent<LayoutElement>();
            listElement.preferredHeight = 318f;
            listElement.minHeight = 220f;
            var list = listRoot.gameObject.AddComponent<ScrollRect>();
            ratRosterScroll = list;
            list.horizontal = false;
            list.vertical = true;
            list.inertia = true;
            list.movementType = ScrollRect.MovementType.Elastic;
            list.scrollSensitivity = 30f;

            var viewport = CreateRect("My Rats Viewport", listRoot);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0.02f, 0.05f, 0.06f, 0.25f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            list.viewport = viewport;

            var listContent = CreateRect("My Rats Content", viewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.sizeDelta = Vector2.zero;
            var layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            var fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            list.content = listContent;
            ratRosterContent = listContent;

            var roster = new List<RatData>();
            foreach (var rat in game.Save.rats)
            {
                if (rat != null && IsRosterRatVisible(rat)) roster.Add(rat);
            }
            roster.Sort(CompareRosterRats);
            if (roster.Count == 0)
            {
                Text emptyState = AddTextTo(listContent,
                    rosterSortField == RosterSortField.Pregnancy ? "No pregnant rats." : "No rats are currently in the colony.",
                    14, new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                // Empty-state copy is informational only. It must never
                // participate in EventSystem or manual fallback hit testing.
                emptyState.raycastTarget = false;
                lastRosterSortSignature = BuildRosterSortSignature(roster);
                return;
            }

            foreach (var rat in roster) AddRatRosterRow(listContent, rat);
            lastRosterSortSignature = BuildRosterSortSignature(roster);
        }

        private int CountColonyRats()
        {
            if (game == null || game.Save == null || game.Save.rats == null) return 0;
            int count = 0;
            foreach (var rat in game.Save.rats)
            {
                if (rat != null) count++;
            }
            return count;
        }

        private int CountVisibleColonyRats()
        {
            if (game == null || game.Save == null || game.Save.rats == null) return 0;
            int count = 0;
            foreach (var rat in game.Save.rats)
                if (rat != null && IsRosterRatVisible(rat)) count++;
            return count;
        }

        private bool IsRosterRatVisible(RatData rat)
        {
            if (rat == null) return false;
            bool sexVisible;
            switch (rosterSexFilter)
            {
                case RosterSexFilter.Males: sexVisible = rat.sex == RatSex.Male; break;
                case RosterSexFilter.Females: sexVisible = rat.sex == RatSex.Female; break;
                default: sexVisible = true; break;
            }
            if (!sexVisible) return false;
            if (rosterSortField == RosterSortField.Pregnancy)
                return BreedingSystem.FindActivePregnancyForMother(game.Save, rat) != null;
            return true;
        }

        private void AddRosterSexFilters(RectTransform parent)
        {
            var row = CreateRect("Rat Sex Filters", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            AddRosterSexFilterButton(row, RosterSexFilter.All);
            AddRosterSexFilterButton(row, RosterSexFilter.Males);
            AddRosterSexFilterButton(row, RosterSexFilter.Females);
        }

        private void AddRosterSexFilterButton(RectTransform parent, RosterSexFilter filter)
        {
            bool active = rosterSexFilter == filter;
            AddButtonTo(parent, RosterSexFilterLabel(filter), true,
                () => SetRosterSexFilter(filter),
                active ? new Color(0.30f, 0.48f, 0.32f) : new Color(0.14f, 0.25f, 0.24f), 36f);
        }

        private void SetRosterSexFilter(RosterSexFilter filter)
        {
            if (rosterSexFilter == filter) return;
            rosterSexFilter = filter;
            PersistRosterPreferences();
            if (game != null) game.DeactivateMultipleSelection();
            if (!string.IsNullOrEmpty(expandedMyRatsId))
            {
                RatData expanded = BreedingSystem.FindRat(game.Save, expandedMyRatsId);
                if (!IsRosterRatVisible(expanded)) expandedMyRatsId = null;
            }
            Refresh(true);
        }

        private void LoadRosterPreferences()
        {
            rosterSortField = RosterSortField.Name;
            rosterSortAscending = true;
            rosterSexFilter = RosterSexFilter.All;
            if (game == null || game.Save == null) return;

            string savedField = game.Save.myRatsSortField ?? string.Empty;
            switch (savedField.Trim().ToLowerInvariant())
            {
                case "age": rosterSortField = RosterSortField.Age; break;
                case "size": rosterSortField = RosterSortField.Size; break;
                case "health": rosterSortField = RosterSortField.Health; break;
                case "fertility": rosterSortField = RosterSortField.Fertility; break;
                case "sex": rosterSortField = RosterSortField.Sex; break;
                case "pregnancy": rosterSortField = RosterSortField.Pregnancy; break;
                case "breeding":
                case "generation":
                case "fertilitynextopportunity":
                case "fertilityopportunity":
                case "fertility (next opportunity)":
                case "next opportunity":
                    rosterSortField = RosterSortField.Breeding;
                    rosterSortAscending = true;
                    break;
                default: rosterSortField = RosterSortField.Name; break;
            }

            if (rosterSortField != RosterSortField.Breeding && rosterSortField != RosterSortField.Pregnancy)
                rosterSortAscending = game.Save.myRatsSortAscending;

            switch ((game.Save.myRatsSexFilter ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "males": rosterSexFilter = RosterSexFilter.Males; break;
                case "females": rosterSexFilter = RosterSexFilter.Females; break;
                default: rosterSexFilter = RosterSexFilter.All; break;
            }
        }

        private void PersistRosterPreferences()
        {
            if (game == null || game.Save == null) return;
            game.Save.myRatsSortField = rosterSortField == RosterSortField.Generation
                ? "Breeding"
                : rosterSortField.ToString();
            game.Save.myRatsSortAscending = rosterSortAscending;
            game.Save.myRatsSexFilter = rosterSexFilter.ToString();
            SaveSystem.Save(game.Save);
        }

        private string RosterSexFilterLabel()
        {
            return "Sex: " + RosterSexFilterLabel(rosterSexFilter);
        }

        private static string RosterSexFilterLabel(RosterSexFilter filter)
        {
            switch (filter)
            {
                case RosterSexFilter.Males: return "Males";
                case RosterSexFilter.Females: return "Females";
                default: return "All";
            }
        }

        private void AddRosterSortControls(RectTransform parent)
        {
            var controls = CreateRect("Rat Roster Sort Controls", parent);
            var controlsLayout = controls.gameObject.AddComponent<VerticalLayoutGroup>();
            controlsLayout.spacing = 4f;
            controlsLayout.childControlWidth = true;
            controlsLayout.childControlHeight = true;
            controlsLayout.childForceExpandWidth = true;
            controlsLayout.childForceExpandHeight = false;
            var controlsFitter = controls.gameObject.AddComponent<ContentSizeFitter>();
            controlsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var fields = new[]
            {
                RosterSortField.Name,
                RosterSortField.Age,
                RosterSortField.Size,
                RosterSortField.Health,
                RosterSortField.Fertility,
                RosterSortField.Sex,
                RosterSortField.Pregnancy,
                RosterSortField.Breeding,
            };
            for (int index = 0; index < fields.Length; index += 4)
            {
                var row = CreateRect("Roster Sort Row", controls);
                var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 4f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = false;
                for (int offset = 0; offset < 4 && index + offset < fields.Length; offset++)
                {
                    RosterSortField field = fields[index + offset];
                    bool active = rosterSortField == field;
                    Color color = active ? new Color(0.3f, 0.48f, 0.32f) : new Color(0.14f, 0.25f, 0.24f);
                    bool fixedDirection = field == RosterSortField.Breeding || field == RosterSortField.Pregnancy;
                    string direction = active && !fixedDirection
                        ? (rosterSortAscending ? " ↑" : " ↓")
                        : string.Empty;
                    AddButtonTo(row, RosterFieldLabel(field) + direction, true,
                        () => SetRosterSort(field), color, 34f);
                }
            }
        }

        private void SetRosterSort(RosterSortField field)
        {
            if (field == RosterSortField.Breeding || field == RosterSortField.Pregnancy)
            {
                rosterSortField = field;
                rosterSortAscending = true;
            }
            else if (rosterSortField == field) rosterSortAscending = !rosterSortAscending;
            else
            {
                rosterSortField = field;
                rosterSortAscending = true;
            }
            PersistRosterPreferences();
            // Invalidate the previous empty/result signature before the
            // immediate rebuild. This makes a zero-result Pregnancy view
            // switch to any ordinary sort in the same UI update.
            lastRosterSortSignature = null;
            ClearUiPointerState();
            Refresh(true);
        }

        private void ResetMyRatsSortState()
        {
            rosterSortField = RosterSortField.Name;
            rosterSortAscending = true;
            lastRosterSortSignature = null;
            if (game == null || game.Save == null) return;
            bool changed = !string.Equals(game.Save.myRatsSortField, "Name", StringComparison.OrdinalIgnoreCase) ||
                !game.Save.myRatsSortAscending;
            game.Save.myRatsSortField = "Name";
            game.Save.myRatsSortAscending = true;
            if (changed) SaveSystem.Save(game.Save);
        }

        private string RosterSortLabel()
        {
            if (rosterSortField == RosterSortField.Pregnancy) return "Pregnancy (soonest first)";
            if (rosterSortField == RosterSortField.Breeding) return "Breeding (next opportunity)";
            return RosterFieldLabel(rosterSortField) + (rosterSortAscending ? " ascending" : " descending");
        }

        private static string RosterFieldLabel(RosterSortField field)
        {
            switch (field)
            {
                case RosterSortField.Age: return "Age";
                case RosterSortField.Size: return "Size";
                case RosterSortField.Health: return "Health";
                case RosterSortField.Fertility: return "Fertility";
                case RosterSortField.Sex: return "Sex";
                case RosterSortField.Pregnancy: return "Pregnancy";
                case RosterSortField.Breeding: return "Breeding";
                case RosterSortField.Generation: return "Generation";
                default: return "Name";
            }
        }

        private int CompareRosterRats(RatData first, RatData second)
        {
            int result;
            switch (rosterSortField)
            {
                case RosterSortField.Age: result = AgeValue(first).CompareTo(AgeValue(second)); break;
                case RosterSortField.Size: result = TraitValue(first, 0).CompareTo(TraitValue(second, 0)); break;
                case RosterSortField.Health: result = TraitValue(first, 1).CompareTo(TraitValue(second, 1)); break;
                case RosterSortField.Fertility:
                    result = BreedingSystem.CompareFertilityStatSort(first, second, rosterSortAscending);
                    break;
                case RosterSortField.Sex: result = first.sex.CompareTo(second.sex); break;
                case RosterSortField.Pregnancy:
                    result = BreedingSystem.ComparePregnancySort(
                        game == null ? null : game.Save,
                        first,
                        second,
                        game == null ? 0L : game.GameTime,
                         true);
                    break;
                case RosterSortField.Breeding:
                    result = BreedingSystem.CompareBreedingSort(
                        game == null ? null : game.Save,
                        first,
                        second,
                        game == null ? 0L : game.GameTime);
                    break;
                case RosterSortField.Generation: result = first.generation.CompareTo(second.generation); break;
                default: result = string.Compare(first.name, second.name, StringComparison.OrdinalIgnoreCase); break;
            }
            if (!rosterSortAscending && rosterSortField != RosterSortField.Pregnancy &&
                rosterSortField != RosterSortField.Breeding &&
                rosterSortField != RosterSortField.Fertility) result = -result;
            if (result != 0) return result;

            result = string.Compare(first.name, second.name, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
            return string.Compare(first.id, second.id, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildRosterSortSignature(List<RatData> sortedRoster)
        {
            if (sortedRoster == null) return string.Empty;
            var signature = new System.Text.StringBuilder();
            signature.Append(rosterSexFilter).Append('|').Append(rosterSortField).Append(';');
            foreach (RatData rat in sortedRoster)
            {
                if (rat == null) continue;
                signature.Append(rat.id ?? string.Empty).Append(':');
                if (rosterSortField == RosterSortField.Pregnancy)
                {
                    PregnancyData pregnancy = FindPregnancyForFemale(rat);
                    signature.Append(pregnancy == null ? 0L : pregnancy.dueAt);
                }
                else if (rosterSortField == RosterSortField.Breeding)
                {
                    BreedingSystem.BreedingOpportunitySortInfo opportunity =
                        BreedingSystem.GetBreedingOpportunitySortInfo(game.Save, rat, game.GameTime);
                    signature.Append(opportunity.availableNow ? '1' : '0')
                        .Append(opportunity.upcoming ? '1' : '0')
                        .Append(opportunity.nextAvailableAt)
                        .Append(':').Append(opportunity.state);
                }
                else if (rosterSortField == RosterSortField.Fertility)
                {
                    signature.Append(TraitValue(rat, 2).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
                signature.Append(';');
            }
            return signature.ToString();
        }

        private void RefreshRosterSortIfNeeded()
        {
            if (game == null || game.Save == null || activeMainPanel != MainPanel.MyRats ||
                ratRosterScroll == null || ratRosterContent == null ||
                (rosterSortField != RosterSortField.Pregnancy &&
                 rosterSortField != RosterSortField.Breeding &&
                 rosterSortField != RosterSortField.Fertility))
                return;

            var roster = new List<RatData>();
            foreach (RatData rat in game.Save.rats)
            {
                if (rat != null && IsRosterRatVisible(rat)) roster.Add(rat);
            }
            roster.Sort(CompareRosterRats);
            string signature = BuildRosterSortSignature(roster);
            if (signature == lastRosterSortSignature) return;

            float previousNormalized = ratRosterScroll.verticalNormalizedPosition;
            // Keep the existing My Rats ScrollRect and viewport. Only the row
            // children are refreshed when a pregnancy/opportunity ordering or
            // membership actually changes, so live labels and scroll state do
            // not reset on every simulation-clock update.
            liveTimedTextUpdates.Clear();
            for (int index = ratRosterContent.childCount - 1; index >= 0; index--)
            {
                GameObject oldRow = ratRosterContent.GetChild(index).gameObject;
                oldRow.SetActive(false);
                Destroy(oldRow);
            }

            if (roster.Count == 0)
            {
                Text emptyState = AddTextTo(ratRosterContent,
                    rosterSortField == RosterSortField.Pregnancy ? "No pregnant rats." : "No rats are currently in the colony.",
                    14, new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                emptyState.raycastTarget = false;
            }
            else
            {
                foreach (RatData rat in roster) AddRatRosterRow(ratRosterContent, rat);
            }

            lastRosterSortSignature = signature;
            Canvas.ForceUpdateCanvases();
            ratRosterScroll.verticalNormalizedPosition = previousNormalized;
        }

        private static float AgeValue(RatData rat)
        {
            return rat == null ? 0f : rat.ageDays;
        }

        private static float TraitValue(RatData rat, int trait)
        {
            if (rat == null || rat.traits == null) return 0f;
            switch (trait)
            {
                case 0: return rat.traits.size;
                case 1: return rat.traits.health;
                default: return rat.traits.fertility;
            }
        }

        private void AddRatRosterRow(Transform parent, RatData rat)
        {
            var rowRoot = new GameObject("My Rats Row");
            rowRoot.transform.SetParent(parent, false);
            var rowImage = rowRoot.AddComponent<Image>();
            bool expanded = !game.MultipleSelectionMode && expandedMyRatsId == rat.id;
            bool selected = game.MultipleSelectionMode
                ? game.IsRatSelectedForGroup(rat.id)
                : game.SelectedRat != null && game.SelectedRat.id == rat.id;
            UiStyle.ApplyRounded(rowImage, expanded || selected ? new Color(0.16f, 0.3f, 0.24f) : new Color(0.1f, 0.16f, 0.14f), true);
            rowImage.raycastTarget = true;

            // The entire card is the expand/toggle hit target. Keep this as a
            // single data-only UI action so the same tap cannot fall through
            // to the habitat selection raycast. Any intentional child action
            // buttons remain their own hit targets and do not toggle the card.
            var rowButton = rowRoot.AddComponent<Button>();
            rowButton.targetGraphic = rowImage;
            rowButton.navigation = new Navigation { mode = Navigation.Mode.None };
            string ratId = rat.id;
            var rowClickRelay = rowRoot.AddComponent<DirectUiClickRelay>();
            rowClickRelay.Configure(this, rowButton, () =>
            {
                if (game.MultipleSelectionMode) game.ToggleRatGroupSelection(ratId);
                else ToggleExpandedMyRat(ratId);
            });

            var rowVertical = rowRoot.AddComponent<VerticalLayoutGroup>();
            rowVertical.spacing = 5f;
            rowVertical.padding = new RectOffset(6, 6, 6, 6);
            rowVertical.childControlWidth = true;
            rowVertical.childControlHeight = true;
            rowVertical.childForceExpandWidth = true;
            rowVertical.childForceExpandHeight = false;
            var rowFitter = rowRoot.AddComponent<ContentSizeFitter>();
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = CreateRect("My Rats Row Header", rowRoot.transform);
            var headerImage = header.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(headerImage, expanded || selected ? new Color(0.18f, 0.34f, 0.28f) : new Color(0.08f, 0.14f, 0.13f), true);
            // The parent card owns input. This decorative header image must
            // not create a nested button that competes with the card toggle.
            headerImage.raycastTarget = false;

            var rowLayout = rowRoot.AddComponent<LayoutElement>();
            // The card height comes from its vertical layout and the content
            // fitters below. Keep a small collapsed minimum so four summary
            // lines never compete with the portrait or the next card.
            rowLayout.minHeight = expanded ? 0f : 124f;
            var horizontal = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.padding = new RectOffset(8, 8, 7, 7);
            horizontal.spacing = 8f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;

            const float rosterPortraitSize = 80f;
            const float headerHeight = 124f;
            var headerElement = header.gameObject.AddComponent<LayoutElement>();
            headerElement.preferredHeight = headerHeight;
            headerElement.minHeight = headerHeight;

            var portraitRoot = CreateRect("My Rats Portrait", header.transform);
            var portraitLayout = portraitRoot.gameObject.AddComponent<LayoutElement>();
            portraitLayout.preferredWidth = rosterPortraitSize;
            portraitLayout.minWidth = rosterPortraitSize;
            portraitLayout.preferredHeight = 80f;
            portraitLayout.minHeight = 80f;
            // Both axes are fixed by the layout element, matching the
            // eligible-mate thumbnail slot. The RenderTexture is square too,
            // so the RawImage cannot be stretched by the horizontal layout.
            var portrait = portraitRoot.gameObject.AddComponent<RawImage>();
            portrait.texture = portraitPreview == null ? null : portraitPreview.GetPortrait(rat);
            portrait.color = Color.white;
            portrait.uvRect = new Rect(0f, 0f, 1f, 1f);
            portrait.raycastTarget = false;
            ValidatePortraitSlot(portraitRoot, "rat roster " + rat.id);

            var infoRoot = CreateRect("My Rats Basic Information", header.transform);
            var infoLayout = infoRoot.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1f;
            infoLayout.minHeight = 0f;
            var infoVertical = infoRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            infoVertical.spacing = 0f;
            infoVertical.childControlWidth = true;
            infoVertical.childControlHeight = true;
            infoVertical.childForceExpandWidth = true;
            infoVertical.childForceExpandHeight = false;
            var infoFitter = infoRoot.gameObject.AddComponent<ContentSizeFitter>();
            infoFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text nameText = AddTextTo(infoRoot, BuildRatRosterName(rat, selected), 15, Color.white, TextAnchor.MiddleLeft);
            BindLiveText(nameText, () => BuildRatRosterName(rat, selected));
            Text ageText = AddTextTo(infoRoot, BuildRatRosterAge(rat), 13, Color.white, TextAnchor.MiddleLeft);
            BindLiveText(ageText, () => BuildRatRosterAge(rat));
            Text stageText = AddTextTo(infoRoot, BuildRatRosterStage(rat), 13, Color.white, TextAnchor.MiddleLeft);
            BindLiveText(stageText, () => BuildRatRosterStage(rat));
            PregnancyData pregnancy = FindPregnancyForFemale(rat);
            if (pregnancy != null)
            {
                AddPregnancyProgress(infoRoot, pregnancy);
            }
            else
            {
                Text availabilityText = AddTextTo(infoRoot, BuildRatRosterAvailability(rat), 13,
                    new Color(0.95f, 0.83f, 0.55f), TextAnchor.MiddleLeft);
                BindLiveText(availabilityText, () => BuildRatRosterAvailability(rat));
            }

            if (expanded)
            {
                var detailPanel = CreateRect("Expanded My Rat Details", rowRoot.transform);
                var detailImage = detailPanel.gameObject.AddComponent<Image>();
                UiStyle.ApplyRounded(detailImage, new Color(0.04f, 0.11f, 0.12f, 0.96f), true);
                detailImage.raycastTarget = false;
                var detailLayout = detailPanel.gameObject.AddComponent<VerticalLayoutGroup>();
                detailLayout.spacing = 2f;
                detailLayout.padding = new RectOffset(10, 10, 8, 8);
                detailLayout.childControlWidth = true;
                detailLayout.childControlHeight = true;
                detailLayout.childForceExpandWidth = true;
                detailLayout.childForceExpandHeight = false;
                var detailFitter = detailPanel.gameObject.AddComponent<ContentSizeFitter>();
                detailFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                AddDetailedRatInformation(detailPanel, rat, 12);
                AddRatActivityHistory(detailPanel, rat);
                AddButtonTo(detailPanel, "Profile", true,
                    () => OpenRatProfileFromMyRats(rat.id),
                    new Color(0.22f, 0.38f, 0.46f), 42f);
            }
        }

        private void BindLiveText(Text text, Func<string> valueProvider)
        {
            if (text == null || valueProvider == null) return;
            Action update = () =>
            {
                if (text != null) text.text = valueProvider();
            };
            liveTimedTextUpdates.Add(update);
            update();
        }

        private void BindLiveAction(Action update)
        {
            if (update == null) return;
            liveTimedTextUpdates.Add(update);
            update();
        }

        private string BuildRatRosterName(RatData rat, bool selected)
        {
            if (rat == null) return string.Empty;
            return (selected ? "✓ " : string.Empty) + ColonyFactory.DisplayName(rat);
        }

        private static string BuildRatRosterAge(RatData rat)
        {
            return rat == null ? string.Empty : "Age: " + GrowthSystem.FormatAge(rat.ageDays);
        }

        private static string BuildRatRosterStage(RatData rat)
        {
            return rat == null ? string.Empty : "Stage: " + GrowthSystem.StageLabel(rat.stage);
        }

        private string BuildRatRosterAvailability(RatData rat)
        {
            if (game == null || rat == null) return string.Empty;
            return BreedingSystem.BreedingAvailabilityLabel(game.Save, rat, game.GameTime);
        }

        private void AddDetailedRatInformation(RectTransform parent, RatData rat, int fontSize)
        {
            if (parent == null || rat == null) return;

            string coat = rat.phenotype == null || !rat.phenotype.furRevealed
                ? "Hidden while Pinkie"
                : rat.phenotype.coatColorLabel;
            string markings = rat.phenotype == null || !rat.phenotype.furRevealed
                ? "Hidden while Pinkie"
                : rat.phenotype.markingsLabel;
            TraitData traits = rat.traits ?? new TraitData();

            AddText(parent, "Age: " + GrowthSystem.FormatAge(rat.ageDays) + "  •  Stage: " + GrowthSystem.StageLabel(rat.stage) +
                "  •  Sex: " + SexLabel(rat.sex), fontSize, Color.white, TextAnchor.UpperLeft);
            AddText(parent, "Coat: " + coat + "  •  Markings: " + markings, fontSize,
                new Color(0.95f, 0.83f, 0.55f), TextAnchor.UpperLeft);
            AddText(parent, "Size " + traits.size.ToString("0") + "  •  Health " + traits.health.ToString("0") +
                "  •  Fertility " + traits.fertility.ToString("0") + "  •  Generation " + rat.generation,
                fontSize, Color.white, TextAnchor.UpperLeft);
            AddText(parent, "Known genes: " + KnownGeneSummary(rat), fontSize - 1,
                new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddText(parent, "Parents: " + ParentSummary(rat) + "  •  Litter: " + LitterNameForRat(rat), fontSize - 1,
                new Color(0.70f, 0.78f, 0.74f), TextAnchor.UpperLeft);
            AddText(parent, "Current habitat: " + EnclosureSystem.Label(rat.enclosure), fontSize,
                new Color(0.78f, 0.90f, 0.82f), TextAnchor.UpperLeft);
            Text reproductiveStateText = AddText(parent, string.Empty, fontSize,
                new Color(0.72f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            BindLiveText(reproductiveStateText, () => "Reproductive state: " + ReproductiveStateLabel(rat));
            string saleRestriction = game == null ? string.Empty : game.SaleRestrictionReason(rat);
            if (!string.IsNullOrEmpty(saleRestriction))
                AddText(parent, saleRestriction, fontSize - 1,
                    new Color(1f, 0.63f, 0.42f), TextAnchor.UpperLeft);
        }

        private void AddRatActivityHistory(RectTransform parent, RatData rat)
        {
            if (parent == null || rat == null) return;

            AddText(parent, "Recent activity", 13,
                new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft);
            var historyPanel = CreateRect("Rat Activity History", parent);
            var historyImage = historyPanel.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(historyImage, new Color(0.02f, 0.06f, 0.07f, 0.78f), false);
            historyImage.raycastTarget = false;
            var historyLayout = historyPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            historyLayout.spacing = 1f;
            historyLayout.padding = new RectOffset(8, 8, 6, 6);
            historyLayout.childControlWidth = true;
            historyLayout.childControlHeight = true;
            historyLayout.childForceExpandWidth = true;
            historyLayout.childForceExpandHeight = false;
            var historyElement = historyPanel.gameObject.AddComponent<LayoutElement>();
            liveProfileActivityHistoryPanel = historyPanel;
            liveProfileActivityHistoryLayout = historyElement;
            liveActivityHistoryRatId = rat.id;
            historyElement.minHeight = 28f;
            historyElement.preferredHeight = 28f + Mathf.Min(
                RatActivitySystem.MaximumHistoryEntries,
                rat.activity == null || rat.activity.history == null ? 0 : rat.activity.history.Count) * 19f;

            if (rat.activity == null || rat.activity.history == null || rat.activity.history.Count == 0)
            {
                AddText(historyPanel, "No recent activity recorded.", 11,
                    new Color(0.70f, 0.78f, 0.74f), TextAnchor.UpperLeft);
                liveProfileActivityHistorySignature = BuildRatActivityHistorySignature(rat);
                return;
            }

            int count = Mathf.Min(RatActivitySystem.MaximumHistoryEntries, rat.activity.history.Count);
            for (int index = 0; index < count; index++)
            {
                RatActivityEntryData entry = rat.activity.history[index];
                if (entry == null) continue;
                AddText(historyPanel, game.FormatRatActivityEntry(entry), 11,
                    new Color(0.80f, 0.87f, 0.83f), TextAnchor.UpperLeft);
            }
            liveProfileActivityHistorySignature = BuildRatActivityHistorySignature(rat);
        }

        private static string KnownGeneSummary(RatData rat)
        {
            if (rat == null || rat.genotype == null) return "Unavailable";
            return GeneticsSystem.FormatPair(rat.genotype, "B") + " " +
                GeneticsSystem.FormatPair(rat.genotype, "C") + " " +
                GeneticsSystem.FormatPair(rat.genotype, "D") + " " +
                GeneticsSystem.FormatPair(rat.genotype, "S");
        }

        private void ToggleExpandedMyRat(string ratId)
        {
            if (game == null || game.MultipleSelectionMode) return;
            familyTreeSubjectId = null;
            if (expandedMyRatsId == ratId)
            {
                expandedMyRatsId = null;
                Refresh(true);
                return;
            }

            expandedMyRatsId = ratId;
            // My Rats selection is data-only. Do not select the world object or
            // move the camera: the pointer/touch belongs to this UI row and
            // must never be reused by the habitat raycast path.
            Refresh(true);
        }

        private void OpenRatProfileFromMyRats(string ratId)
        {
            if (game == null || string.IsNullOrEmpty(ratId)) return;
            RatData rat = BreedingSystem.FindHistoricalRat(game.Save, ratId);
            if (rat == null) return;

            // The button belongs to the expanded card, so use its stable ID
            // and close the modal My Rats page before rebuilding the profile.
            // This also prevents the My Rats blocker/list from remaining over
            // the live profile view.
            game.DeactivateMultipleSelection();
            expandedMyRatsId = null;
            activeMainPanel = MainPanel.None;
            ResetProfileInformationExpansion();
            profileNavigationFromMyRats = true;

            RatData liveRat = BreedingSystem.FindRat(game.Save, ratId);
            if (liveRat != null)
            {
                familyTreeSubjectId = null;
                // This selects the actual live rat root; the profile then
                // follows that same object rather than creating a preview.
                game.SelectRatFromRoster(ratId);
            }
            else
            {
                // Retired rats have no live habitat object. Keep their stable
                // ID as the informational profile subject so historical
                // parentage/status data can still be displayed.
                familyTreeSubjectId = ratId;
                game.SelectEntity(null);
            }

            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private string ReproductiveStateLabel(RatData rat)
        {
            PregnancyData pregnancy = FindPregnancyForFemale(rat);
            if (pregnancy != null)
                return BreedingSystem.PregnancyProgressLabel(
                    game == null ? null : game.Save,
                    rat,
                    game == null ? 0L : game.GameTime);
            return BreedingSystem.ReproductiveStateLabel(game == null ? null : game.Save, rat, game == null ? 0L : game.GameTime);
        }

        private static string FormatRemainingTime(long remainingMs)
        {
            long totalMinutes = Math.Max(1L, (long)Math.Ceiling(remainingMs / 60000d));
            long days = totalMinutes / (24L * 60L);
            long hours = (totalMinutes % (24L * 60L)) / 60L;
            long minutes = totalMinutes % 60L;

            if (days > 0L) return days + "d " + hours + "h remaining";
            if (hours > 0L) return hours + "h " + minutes + "m remaining";
            return minutes + "m remaining";
        }

        private PregnancyData FindPregnancyForFemale(RatData rat)
        {
            if (game == null || game.Save == null || rat == null || rat.sex != RatSex.Female) return null;
            return BreedingSystem.FindActivePregnancyForMother(game.Save, rat);
        }

        private string LitterNameForRat(RatData rat)
        {
            if (rat == null || game == null || game.Save == null) return "—";
            if (!string.IsNullOrEmpty(rat.litterId))
            {
                string name = LitterNameSystem.GetName(game.Save, rat.litterId);
                return string.IsNullOrEmpty(name) ? "Unnamed litter" : name;
            }

            // Mothers do not store a single litterId on their RatData. Show
            // the most recent named litter when profile/roster text is being
            // viewed, while the internal litter IDs remain save-only data.
            if (rat.sex == RatSex.Female && game.Save.litters != null)
            {
                for (int index = game.Save.litters.Count - 1; index >= 0; index--)
                {
                    LitterData litter = game.Save.litters[index];
                    if (litter == null || litter.motherId != rat.id) continue;
                    string name = LitterNameSystem.GetName(game.Save, litter.id);
                    return string.IsNullOrEmpty(name) ? "Unnamed litter" : name;
                }
            }
            return "—";
        }

        private void AddPregnancyProgress(RectTransform parent, PregnancyData pregnancy, bool includeLabel = true)
        {
            if (parent == null || pregnancy == null || game == null) return;

            if (includeLabel)
            {
                Text pregnancyText = AddText(parent, string.Empty, 14,
                    new Color(1f, 0.73f, 0.34f), TextAnchor.UpperLeft);
                BindLiveText(pregnancyText, () => PregnancyProgressLabel(pregnancy));
            }
            var track = CreateRect("Pregnancy Progress Track", parent);
            var trackElement = track.gameObject.AddComponent<LayoutElement>();
            trackElement.preferredHeight = 14f;
            trackElement.minHeight = 14f;
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.03f, 0.06f, 0.07f, 0.92f);
            trackImage.raycastTarget = false;

            var fill = CreateRect("Pregnancy Progress Fill", track);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(BreedingSystem.PregnancyProgress01(pregnancy, game.GameTime), 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.95f, 0.55f, 0.28f, 1f);
            fillImage.raycastTarget = false;
            BindLiveAction(() =>
            {
                if (fill == null || game == null) return;
                fill.anchorMax = new Vector2(
                    BreedingSystem.PregnancyProgress01(pregnancy, game.GameTime), 1f);
            });
        }

        private string PregnancyProgressLabel(PregnancyData pregnancy)
        {
            if (pregnancy == null || game == null) return string.Empty;
            int percentage = Mathf.Clamp(Mathf.RoundToInt(
                BreedingSystem.PregnancyProgress01(pregnancy, game.GameTime) * 100f), 0, 100);
            long remainingMs = Math.Max(0L, pregnancy.dueAt - game.GameTime);
            return "Pregnancy  " + percentage + "%  •  " + FormatRemainingTime(remainingMs);
        }

        private void AddBreedingPanel()
        {
            var card = CreateCard("Breeding selection");
            DedicatedBreedingSessionData activeSession = game.ActiveDedicatedBreedingSession;
            if (activeSession != null)
            {
                long remaining = Math.Max(0L, activeSession.endsAt - game.GameTime);
                Text sessionText = AddText(card, string.Empty, 16,
                    new Color(1f, 0.82f, 0.38f), TextAnchor.UpperLeft);
                BindLiveText(sessionText, () =>
                {
                    DedicatedBreedingSessionData live = game.ActiveDedicatedBreedingSession;
                    return live == null ? "Dedicated breeding session complete." :
                        "Dedicated breeding session active — " + FormatRemainingTime(Math.Max(0L, live.endsAt - game.GameTime));
                });
                AddText(card, "The selected pair is occupied until the session ends. Success chance: " +
                    BreedingSystem.FormatChancePercent(activeSession.successChance) + ".", 13,
                    new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            }

            AddText(card, "Choose one fertile adult female", 17, Color.white, TextAnchor.UpperLeft);
            AddFertileBreedingList(card, BreedingSystem.GetFertileAdultFemales(game.Save, game.GameTime), RatSex.Female);
            AddText(card, "Choose one fertile adult male", 17, Color.white, TextAnchor.UpperLeft);
            AddFertileBreedingList(card, BreedingSystem.GetFertileAdultMales(game.Save, game.GameTime), RatSex.Male);

            if (game.Mother != null || game.Father != null)
            {
                AddText(card, "Selected pair", 17, Color.white, TextAnchor.UpperLeft);
                AddParentComparison(card, game.Mother, game.Father);
            }

            if (game.Mother != null && game.Father != null)
            {
                AddPossibleOffspring(card, game.Mother, game.Father);
                AddButton(card, activeSession == null ? "Start 2-hour breeding session" : "Breeding session in progress",
                    activeSession == null, game.ConfirmBreeding);
            }
            else
            {
                AddText(card, "Select both fertile adults to compare inherited traits and start a dedicated session.", 14,
                    new Color(1f, 0.82f, 0.4f), TextAnchor.UpperLeft);
            }
            if (game.BreedingOpen) AddButton(card, "Close breeding selection", true, game.CloseBreeding);
        }

        private void AddFertileBreedingList(RectTransform parent, List<RatData> candidates, RatSex sex)
        {
            var listRoot = CreateRect("Fertile " + sex + " List", parent);
            var listImage = listRoot.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(listImage, new Color(0.04f, 0.10f, 0.13f, 0.82f), false);
            listImage.raycastTarget = false;
            var element = listRoot.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = Mathf.Clamp(108f * Mathf.Max(1, candidates == null ? 0 : candidates.Count) + 12f, 98f, 360f);
            element.minHeight = 98f;
            var layout = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            if (candidates == null || candidates.Count == 0)
            {
                AddTextTo(listRoot, "No fertile adult " + (sex == RatSex.Female ? "females" : "males") + " available right now.", 14,
                    new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                return;
            }
            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                string label = (game.IsActiveBreedingSelection(candidate.id) ? "✓ " : string.Empty) + ColonyFactory.DisplayName(candidate) +
                    "  •  " + SexLabel(candidate.sex) + "  •  Age " + GrowthSystem.FormatAge(candidate.ageDays) + "\n" +
                    "Habitat: " + EnclosureSystem.Label(candidate.enclosure) + "\n" +
                    "State: " + ReproductiveStateLabel(candidate);
                AddMateRow(listRoot, candidate, label, game.IsActiveBreedingSelection(candidate.id)
                    ? new Color(0.16f, 0.3f, 0.24f) : new Color(0.1f, 0.16f, 0.14f));
            }
        }

        private void AddParentComparison(RectTransform parent, RatData mother, RatData father)
        {
            if (parent == null) return;

            // Keep the two slots in one fixed-height row. The outer page may
            // still scroll for the rest of the breeding information, but the
            // parent comparison itself never stacks or needs a second view.
            var comparison = CreateRect("Parent Comparison Area", parent);
            var comparisonElement = comparison.gameObject.AddComponent<LayoutElement>();
            comparisonElement.preferredHeight = 274f;
            comparisonElement.minHeight = 274f;
            var row = comparison.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(0, 0, 0, 0);
            row.spacing = 8f;
            row.childAlignment = TextAnchor.UpperCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;

            bool duplicate = mother != null && father != null && mother.id == father.id;
            AddCompactParentCard(comparison, "Mother", mother, new Color(0.44f, 0.28f, 0.12f, 0.9f));
            AddCompactParentCard(comparison, "Father", duplicate ? null : father, new Color(0.12f, 0.28f, 0.36f, 0.9f));
        }

        private void AddCompactParentCard(RectTransform parent, string slotLabel, RatData rat, Color color)
        {
            const float portraitSize = 88f;
            var card = CreateRect(slotLabel + " Card", parent);
            var image = card.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(image, color, true);
            image.raycastTarget = false;
            var element = card.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.preferredHeight = 268f;
            element.minHeight = 268f;

            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(7, 7, 6, 6);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var slot = AddTextTo(card, slotLabel, 13, Color.white, TextAnchor.MiddleCenter);
            slot.fontStyle = FontStyle.Bold;

            if (rat == null)
            {
                AddTextTo(card, slotLabel == "Mother" ? "Select an adult female" : "Select an adult male", 12, new Color(0.76f, 0.84f, 0.82f), TextAnchor.MiddleCenter);
                return;
            }

            AddTextTo(card, ColonyFactory.DisplayName(rat), 13, Color.white, TextAnchor.MiddleCenter);
            // The card's vertical layout intentionally controls the width of
            // direct children. Keep that stretched layout row separate from
            // the centered portrait slot so the RawImage cannot become a
            // wide, short rectangle.
            var portraitRow = CreateRect("Compact Parent Portrait Row", card);
            var portraitRowElement = portraitRow.gameObject.AddComponent<LayoutElement>();
            portraitRowElement.preferredHeight = portraitSize;
            portraitRowElement.minHeight = portraitSize;
            portraitRowElement.flexibleHeight = 0f;

            var portraitRoot = CreateRect("Compact Parent Portrait", portraitRow);
            ConfigureSquarePortraitSlot(portraitRoot, portraitSize);
            var portrait = portraitRoot.gameObject.AddComponent<RawImage>();
            portrait.texture = portraitPreview == null ? null : portraitPreview.GetPortrait(rat);
            portrait.color = Color.white;
            portrait.uvRect = new Rect(0f, 0f, 1f, 1f);
            portrait.raycastTarget = false;
            ValidatePortraitSlot(portraitRoot, slotLabel + " parent " + rat.id);

            string fur = rat.phenotype == null || !rat.phenotype.furRevealed ? "Unknown" : rat.phenotype.coatColorLabel;
            string markings = rat.phenotype == null || !rat.phenotype.furRevealed ? "Hidden" : rat.phenotype.markingsLabel;
            AddTextTo(card, "Sex: " + SexLabel(rat.sex) + "  •  " + GrowthSystem.StageLabel(rat.stage), 12, Color.white, TextAnchor.MiddleCenter);
            AddTextTo(card, "Coat: " + fur + "  •  " + markings, 12, new Color(1f, 0.87f, 0.62f), TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(rat.litterId))
            {
                AddTextTo(card, "Litter: " + LitterNameForRat(rat), 11, new Color(1f, 0.84f, 0.52f), TextAnchor.MiddleCenter);
            }
            AddTextTo(card, "Age: " + GrowthSystem.FormatAge(rat.ageDays), 12, new Color(0.84f, 0.9f, 0.86f), TextAnchor.MiddleCenter);
            AddStatBar(card, "Size", rat.traits.size, new Color(0.86f, 0.68f, 0.3f));
            AddStatBar(card, "Health", rat.traits.health, new Color(0.35f, 0.83f, 0.55f));
            AddStatBar(card, "Fertility", rat.traits.fertility, new Color(0.36f, 0.68f, 0.95f));
        }

        private void AddStatBar(Transform parent, string label, float value, Color fillColor)
        {
            const float rowHeight = 18f;
            var row = CreateRect(label + " Stat Bar", parent);
            var rowElement = row.gameObject.AddComponent<LayoutElement>();
            rowElement.preferredHeight = rowHeight;
            rowElement.minHeight = rowHeight;
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 4f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var labelText = AddTextTo(row, label, 11, new Color(0.84f, 0.9f, 0.86f), TextAnchor.MiddleLeft);
            var labelElement = labelText.gameObject.GetComponent<LayoutElement>();
            labelElement.minWidth = 54f;
            labelElement.preferredWidth = 54f;

            var track = CreateRect(label + " Bar Track", row);
            var trackElement = track.gameObject.AddComponent<LayoutElement>();
            trackElement.minWidth = 40f;
            trackElement.preferredHeight = 10f;
            trackElement.flexibleWidth = 1f;
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.03f, 0.06f, 0.07f, 0.92f);
            trackImage.raycastTarget = false;

            var fill = CreateRect(label + " Bar Fill", track);
            float normalized = Mathf.Clamp01(value / 100f);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(normalized, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;

            var valueText = AddTextTo(row, value.ToString("0") + "%", 11, Color.white, TextAnchor.MiddleRight);
            var valueElement = valueText.gameObject.GetComponent<LayoutElement>();
            valueElement.minWidth = 34f;
            valueElement.preferredWidth = 34f;
        }

        private void AddMateList(RectTransform parent)
        {
            var listRoot = new GameObject("Eligible Mate List");
            listRoot.transform.SetParent(parent, false);
            var listRect = listRoot.AddComponent<RectTransform>();
            var listImage = listRoot.AddComponent<Image>();
            UiStyle.ApplyRounded(listImage, new Color(0.04f, 0.10f, 0.13f, 0.82f), false);
            listImage.raycastTarget = false;
            var element = listRoot.AddComponent<LayoutElement>();
            element.preferredHeight = 214f;
            element.minHeight = 140f;
            var list = listRoot.AddComponent<ScrollRect>();
            mateListScroll = list;
            list.horizontal = false;
            list.vertical = true;
            list.inertia = true;
            list.movementType = ScrollRect.MovementType.Elastic;
            list.scrollSensitivity = 30f;

            var viewport = CreateRect("Mate Viewport", listRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0.02f, 0.05f, 0.06f, 0.25f);
            viewportImage.raycastTarget = false;
            viewport.gameObject.AddComponent<RectMask2D>();
            list.viewport = viewport;

            var listContent = CreateRect("Mate List Content", viewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.sizeDelta = Vector2.zero;
            var layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            var fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            list.content = listContent;

            List<RatData> mates = game.EligibleMates;
            if (mates.Count == 0)
            {
                AddTextTo(listContent, "No eligible adult mates are currently available.", 14, new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                return;
            }
            foreach (var mate in mates)
            {
                bool selected = game.IsActiveBreedingSelection(mate.id);
                string fur = mate.phenotype == null || !mate.phenotype.furRevealed ? "Unknown" : mate.phenotype.coatColorLabel;
                string label = (selected ? "✓ " : "") + ColonyFactory.DisplayName(mate) + "  •  " + SexLabel(mate.sex) + "  •  " + GrowthSystem.StageLabel(mate.stage) + "\n" +
                    "Fur " + fur + "  |  Size " + mate.traits.size.ToString("0") + "  Health " + mate.traits.health.ToString("0") + "  Fertility " + mate.traits.fertility.ToString("0");
                AddMateRow(listContent, mate, label, selected ? new Color(0.16f, 0.3f, 0.24f) : new Color(0.1f, 0.16f, 0.14f));
            }
        }

        private void AddMateRow(Transform parent, RatData mate, string label, Color color)
        {
            if (parent == null || mate == null) return;

            var rowRoot = new GameObject("Potential Mate Row");
            rowRoot.transform.SetParent(parent, false);
            var rowImage = rowRoot.AddComponent<Image>();
            UiStyle.ApplyRounded(rowImage, color, true);
            rowImage.raycastTarget = true;
            var rowButton = rowRoot.AddComponent<Button>();
            rowButton.targetGraphic = rowImage;
            rowButton.navigation = new Navigation { mode = Navigation.Mode.None };
            string mateId = mate.id;
            var rowClickRelay = rowRoot.AddComponent<DirectUiClickRelay>();
            rowClickRelay.Configure(this, rowButton, () => game.SelectBreedingParent(mateId));

            var rowLayout = rowRoot.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 96f;
            rowLayout.minHeight = 96f;

            var horizontal = rowRoot.AddComponent<HorizontalLayoutGroup>();
            horizontal.padding = new RectOffset(8, 8, 9, 9);
            horizontal.spacing = 8f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;

            var thumbnailRoot = CreateRect("Mate Portrait Thumbnail", rowRoot.transform);
            var thumbnailLayout = thumbnailRoot.gameObject.AddComponent<LayoutElement>();
            thumbnailLayout.preferredWidth = 64f;
            thumbnailLayout.minWidth = 64f;
            thumbnailLayout.preferredHeight = 64f;
            thumbnailLayout.minHeight = 64f;
            var thumbnail = thumbnailRoot.gameObject.AddComponent<RawImage>();
            thumbnail.texture = portraitPreview == null ? null : portraitPreview.GetPortrait(mate);
            thumbnail.color = Color.white;
            thumbnail.uvRect = new Rect(0f, 0f, 1f, 1f);
            thumbnail.raycastTarget = false;
            ValidatePortraitSlot(thumbnailRoot, "potential mate " + mate.id);

            var infoRoot = CreateRect("Mate Info", rowRoot.transform);
            var infoLayout = infoRoot.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1f;
            infoLayout.minHeight = 78f;
            infoLayout.preferredHeight = 78f;
            var infoText = AddTextTo(infoRoot, label, 14, Color.white, TextAnchor.MiddleLeft);
            infoText.rectTransform.anchorMin = Vector2.zero;
            infoText.rectTransform.anchorMax = Vector2.one;
            infoText.rectTransform.offsetMin = new Vector2(2f, 0f);
            infoText.rectTransform.offsetMax = new Vector2(-2f, 0f);
            BindLiveText(infoText, () =>
            {
                RatData live = BreedingSystem.FindRat(game.Save, mateId);
                if (live == null) return label;
                return (game.IsActiveBreedingSelection(live.id) ? "✓ " : string.Empty) + ColonyFactory.DisplayName(live) +
                    "  •  " + SexLabel(live.sex) + "  •  Age " + GrowthSystem.FormatAge(live.ageDays) + "\n" +
                    "Habitat: " + EnclosureSystem.Label(live.enclosure) + "\n" +
                    "State: " + ReproductiveStateLabel(live);
            });
        }

        private void AddRatPortrait(RectTransform parent, RatData rat)
        {
            if (parent == null || rat == null) return;
            // Keep a larger clear live-view window above the opaque details
            // panel so the selected habitat rat remains visible on phones.
            const float portraitContainerHeight = 300f;
            const float portraitSize = 270f;

            // The profile card is manually framed, so pin the live-view
            // opening to the top of that frame. This is still the actual
            // habitat camera showing through; no duplicate portrait object is
            // created.
            var portraitContainer = CreateRect("Rat Portrait Container", parent);
            portraitContainer.anchorMin = new Vector2(0.5f, 1f);
            portraitContainer.anchorMax = new Vector2(0.5f, 1f);
            portraitContainer.pivot = new Vector2(0.5f, 1f);
            portraitContainer.anchoredPosition = Vector2.zero;
            portraitContainer.sizeDelta = new Vector2(portraitSize, portraitContainerHeight);
            var portraitContainerLayout = portraitContainer.gameObject.AddComponent<LayoutElement>();
            portraitContainerLayout.preferredWidth = portraitSize;
            portraitContainerLayout.minWidth = portraitSize;
            portraitContainerLayout.preferredHeight = portraitContainerHeight;
            portraitContainerLayout.minHeight = portraitContainerHeight;
            // Keep the rendered square inside its reserved row. The image is
            // twenty pixels shorter than the row, so this clips only any
            // unexpected rect overflow and cannot crop the intended rat.
            portraitContainer.gameObject.AddComponent<RectMask2D>();
            var portraitLayout = portraitContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            portraitLayout.childAlignment = TextAnchor.MiddleCenter;
            portraitLayout.childControlWidth = false;
            portraitLayout.childControlHeight = false;
            portraitLayout.childForceExpandWidth = false;
            portraitLayout.childForceExpandHeight = false;
            portraitLayout.spacing = 0f;

            // This is a transparent opening over the ordinary Main Camera,
            // not a RenderTexture or a standalone rat portrait. The selected
            // live rat remains in the habitat and shows through this square.
            // Center anchors plus a zero anchored position avoid the previous
            // stretch/offset interaction between nested layout controllers.
            var portraitImageRoot = CreateRect("Rat Portrait Image", portraitContainer);
            portraitImageRoot.anchorMin = new Vector2(0.5f, 0.5f);
            portraitImageRoot.anchorMax = new Vector2(0.5f, 0.5f);
            portraitImageRoot.pivot = new Vector2(0.5f, 0.5f);
            portraitImageRoot.anchoredPosition = Vector2.zero;
            portraitImageRoot.sizeDelta = new Vector2(portraitSize, portraitSize);
            var portraitImageLayout = portraitImageRoot.gameObject.AddComponent<LayoutElement>();
            portraitImageLayout.preferredWidth = portraitSize;
            portraitImageLayout.minWidth = portraitSize;
            portraitImageLayout.preferredHeight = portraitSize;
            portraitImageLayout.minHeight = portraitSize;

            // Intentionally leave the opening without an Image or RawImage.
            // Any graphic here would fill the cutout and hide the habitat
            // behind it. The existing Main Camera is visible directly through
            // this RectTransform; the opaque details panel below remains the
            // only profile surface.
        }

        private void AddPossibleOffspring(RectTransform parent, RatData first, RatData second)
        {
            AddText(parent, "Offspring potential", 17, new Color(0.7f, 0.95f, 0.82f), TextAnchor.UpperLeft);
            var preview = GeneticsSystem.BuildPreview(first, second);
            if (preview == null)
            {
                AddText(parent, "No inheritance estimate is available yet.", 13, new Color(1f, 0.72f, 0.43f), TextAnchor.UpperLeft);
                return;
            }

            if (preview.traitRanges != null)
            {
                foreach (var range in preview.traitRanges)
                {
                    AddInheritanceRangeBar(parent, range);
                }
            }

            RatData mother = first.sex == RatSex.Female ? first : second;
            RatData father = first.sex == RatSex.Male ? first : second;
            int litterMinimum;
            int litterMaximum;
            BreedingSystem.GetLitterSizeRange(mother, father, out litterMinimum, out litterMaximum);
            AddText(parent,
                "Estimated conception chance: " + BreedingSystem.ConceptionChanceLabel(
                    mother, father, GameConfig.PairingPregnancyChance, 0f, 1f) +
                "  •  estimated litter range: " + litterMinimum + "–" + litterMaximum + " pinkies.",
                13, new Color(0.92f, 0.85f, 0.63f), TextAnchor.UpperLeft);

            bool hasBlack = false;
            bool hasBrown = false;
            bool hasAlbino = false;
            bool hasDiluted = false;
            bool hasSpotted = false;
            bool hasSolid = false;
            if (preview.furOutcomes != null)
            {
                foreach (var outcome in preview.furOutcomes)
                {
                    if (outcome == null) continue;
                    string color = (outcome.colorLabel ?? string.Empty).ToLowerInvariant();
                    string markings = (outcome.markingsLabel ?? string.Empty).ToLowerInvariant();
                    if (color.Contains("black")) hasBlack = true;
                    if (color.Contains("brown")) hasBrown = true;
                    if (color.Contains("albino")) hasAlbino = true;
                    if (color.Contains("diluted")) hasDiluted = true;
                    if (markings.Contains("spot")) hasSpotted = true;
                    if (markings.Contains("solid")) hasSolid = true;
                }
            }

            var colorSummary = new List<string>();
            if (hasBlack) colorSummary.Add("black");
            if (hasBrown) colorSummary.Add("brown");
            if (hasAlbino) colorSummary.Add("albino masking");
            if (hasDiluted) colorSummary.Add("dilution");
            string coatSummary = colorSummary.Count == 0 ? "calculated coat variation" : string.Join(", ", colorSummary.ToArray());
            string markingSummary = hasSpotted && hasSolid ? "solid or white spotting" :
                hasSpotted ? "white spotting" : hasSolid ? "solid coat" : "albino masking may remove spotting";
            AddText(parent, "Coat and markings: " + coatSummary + "; " + markingSummary + ".", 13, new Color(0.92f, 0.85f, 0.63f), TextAnchor.UpperLeft);
            AddText(parent, "Ranges show possible min/max values; the expected marker is an average, not a guarantee. Mutation uncertainty: " + (preview.mutationChance * 100f).ToString("0.###") + "% per inherited allele.", 12, new Color(1f, 0.72f, 0.43f), TextAnchor.UpperLeft);
        }

        private void AddInheritanceRangeBar(Transform parent, GeneticsSystem.TraitRangePreview range)
        {
            if (parent == null || range == null) return;

            var row = CreateRect(range.label + " Offspring Range", parent);
            var rowElement = row.gameObject.AddComponent<LayoutElement>();
            rowElement.preferredHeight = 28f;
            rowElement.minHeight = 28f;
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 5f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            string values = range.minimum.ToString("0") + "–" + range.maximum.ToString("0") + "  avg " + range.expected.ToString("0");
            var label = AddTextTo(row, range.label + "  " + values, 12, new Color(0.84f, 0.9f, 0.86f), TextAnchor.MiddleLeft);
            var labelElement = label.gameObject.GetComponent<LayoutElement>();
            labelElement.minWidth = 145f;
            labelElement.preferredWidth = 145f;

            var track = CreateRect(range.label + " Range Track", row);
            var trackElement = track.gameObject.AddComponent<LayoutElement>();
            trackElement.minWidth = 55f;
            trackElement.preferredHeight = 12f;
            trackElement.flexibleWidth = 1f;
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.03f, 0.06f, 0.07f, 0.92f);
            trackImage.raycastTarget = false;

            float minimum = Mathf.Clamp01(range.minimum / 100f);
            float maximum = Mathf.Clamp01(range.maximum / 100f);
            if (maximum < minimum)
            {
                float swap = minimum;
                minimum = maximum;
                maximum = swap;
            }
            var rangeFill = CreateRect(range.label + " Range", track);
            rangeFill.anchorMin = new Vector2(minimum, 0.15f);
            rangeFill.anchorMax = new Vector2(Mathf.Clamp01(Mathf.Max(minimum + 0.01f, maximum)), 0.85f);
            rangeFill.offsetMin = Vector2.zero;
            rangeFill.offsetMax = Vector2.zero;
            var rangeImage = rangeFill.gameObject.AddComponent<Image>();
            rangeImage.color = new Color(0.35f, 0.78f, 0.55f, 0.95f);
            rangeImage.raycastTarget = false;

            float expected = Mathf.Clamp01(range.expected / 100f);
            var marker = CreateRect(range.label + " Expected Marker", track);
            marker.anchorMin = new Vector2(expected, 0f);
            marker.anchorMax = new Vector2(expected, 1f);
            marker.sizeDelta = new Vector2(3f, 0f);
            marker.anchoredPosition = Vector2.zero;
            var markerImage = marker.gameObject.AddComponent<Image>();
            markerImage.color = Color.white;
            markerImage.raycastTarget = false;
        }

        private void RebuildDeveloperToolsContent()
        {
            if (developerToolsCard == null || game == null) return;
            for (int i = developerToolsCard.childCount - 1; i >= 0; i--)
            {
                Destroy(developerToolsCard.GetChild(i).gameObject);
            }

            AddText(developerToolsCard, "Developer Tools", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(developerToolsCard, "Developer-only controls. Test rats use real saved genotype and phenotype data; regular growth and inheritance rules are unchanged.", 13, new Color(0.7f, 0.78f, 0.74f), TextAnchor.UpperLeft);
            AddText(developerToolsCard, "Movement diagnostic: " + game.MovementDiagnostics, 12, new Color(0.58f, 0.86f, 0.72f), TextAnchor.UpperLeft);
            AddText(developerToolsCard, "Simulation diagnostic: " + game.SimulationPerformanceDiagnostics, 12, new Color(0.58f, 0.86f, 0.72f), TextAnchor.UpperLeft);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AddText(developerToolsCard, "Runtime Performance Investigation", 17, Color.white, TextAnchor.UpperLeft);
            AddText(developerToolsCard,
                "Capture uses Unity frame timings/GC counters plus measured subsystem profiler samples. A/B modes are temporary and do not edit the save.",
                12, new Color(0.7f, 0.78f, 0.74f), TextAnchor.UpperLeft);
            AddButton(developerToolsCard,
                RuntimePerformanceDiagnostics.HudVisible ? "Hide live Performance HUD" : "Show live Performance HUD",
                true, () =>
                {
                    game.SetPerformanceHudVisible(!RuntimePerformanceDiagnostics.HudVisible);
                    RebuildDeveloperToolsContent();
                });
            AddButton(developerToolsCard,
                RuntimePerformanceDiagnostics.CaptureEnabled ? "Stop profiler sample capture" : "Start profiler sample capture",
                true, () =>
                {
                    game.SetPerformanceCaptureEnabled(!RuntimePerformanceDiagnostics.CaptureEnabled);
                    RebuildDeveloperToolsContent();
                });
            AddText(developerToolsCard, "A/B one subsystem at a time • current: " + RuntimePerformanceDiagnostics.IsolationMode, 13, new Color(0.58f, 0.86f, 0.72f), TextAnchor.UpperLeft);
            AddButton(developerToolsCard, "A/B baseline • all systems enabled", true,
                () => SetPerformanceIsolationMode(PerformanceIsolationMode.Normal));
            AddButton(developerToolsCard, "A/B disable rat AI, movement and target choice", true,
                () => SetPerformanceIsolationMode(PerformanceIsolationMode.RatBehavior));
            AddButton(developerToolsCard, "A/B freeze rat and pinkie animations", true,
                () => SetPerformanceIsolationMode(PerformanceIsolationMode.RatAnimation));
            AddButton(developerToolsCard, "A/B hide rat renderers (simulation continues)", true,
                () => SetPerformanceIsolationMode(PerformanceIsolationMode.RatRendering));
            AddButton(developerToolsCard, "A/B disable rat shadows", true,
                () => SetPerformanceIsolationMode(PerformanceIsolationMode.RatShadows));
            AddButton(developerToolsCard, "A/B skip automatic page refreshes", true,
                () => SetPerformanceIsolationMode(PerformanceIsolationMode.AutomaticUiRefresh));
            AddButton(developerToolsCard, "A/B skip colony maintenance ticks (short test only)", true,
                () => SetPerformanceIsolationMode(PerformanceIsolationMode.ColonyMaintenance));
#endif
            AddText(developerToolsCard, "Visual seam isolation", 17, Color.white, TextAnchor.UpperLeft);
            AddText(developerToolsCard, "Current mode: " + RatVisualDiagnostics.ModeLabel + ". These modes affect only live visuals and never change saved phenotype data.", 13, new Color(0.7f, 0.78f, 0.74f), TextAnchor.UpperLeft);
            AddButton(developerToolsCard, "1  Plain coat  •  markings disabled", true, () => SetVisualDiagnosticMode(RatVisualDiagnosticMode.PlainCoat));
            AddButton(developerToolsCard, "2  Flat unlit  •  no lighting or shadows", true, () => SetVisualDiagnosticMode(RatVisualDiagnosticMode.FlatUnlit));
            AddButton(developerToolsCard, "3  Normal coat  •  markings enabled", true, () => SetVisualDiagnosticMode(RatVisualDiagnosticMode.Normal));
            AddButton(developerToolsCard, "4  Material / renderer ID colors", true, () => SetVisualDiagnosticMode(RatVisualDiagnosticMode.MaterialIds));
            AddButton(developerToolsCard, "5  World normal colors", true, () => SetVisualDiagnosticMode(RatVisualDiagnosticMode.Normals));
            AddButton(developerToolsCard, "6  World tangent colors", true, () => SetVisualDiagnosticMode(RatVisualDiagnosticMode.Tangents));
            AddText(developerToolsCard, "Spawn adult test rats", 17, Color.white, TextAnchor.UpperLeft);
            AddButton(developerToolsCard, "Solid Black  •  B/B C/C D/D s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SolidBlack));
            AddButton(developerToolsCard, "Solid Brown  •  b/b C/C D/D s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SolidBrown));
            AddButton(developerToolsCard, "Diluted Black  •  B/B C/C d/d s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.DilutedBlack));
            AddButton(developerToolsCard, "Diluted Brown  •  b/b C/C d/d s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.DilutedBrown));
            AddButton(developerToolsCard, "Albino  •  B/B c/c D/D s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.Albino));
            AddButton(developerToolsCard, "Force White Albino Preview  •  white fur, red eyes only", true,
                () => game.SpawnDeveloperRat(DeveloperRatPreset.ForceWhiteAlbinoPreview));
            AddButton(developerToolsCard, "Spotted Black  •  B/B C/C D/D S/S", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SpottedBlack));
            AddButton(developerToolsCard, "Spotted Brown  •  b/b C/C D/D S/S", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SpottedBrown));
            AddButton(developerToolsCard, "Spawn Marked Test Rat  •  blended face, body, and legs", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.MarkedTest));
            AddButton(developerToolsCard, "Spawn Pinkie Placement Test Litter  •  5 grounded pups", true, game.SpawnDeveloperPinkieLitter);
            AddButtonTo(developerToolsCard, "Rat Animation Showcase", true, OpenRatAnimationShowcase, new Color(0.18f, 0.34f, 0.42f), 48f);

            AddText(developerToolsCard, "Store testing", 17, Color.white, TextAnchor.UpperLeft);
            AddButton(developerToolsCard, "Restock Rat Market Now", true, game.RestockStoreNow);

            AddText(developerToolsCard, "Growth and pregnancy tests", 17, Color.white, TextAnchor.UpperLeft);
            if (game.SelectedRat != null)
            {
                bool adult = game.SelectedRat.stage == RatStage.Adult ||
                    game.SelectedRat.stage == RatStage.Mature ||
                    game.SelectedRat.stage == RatStage.Elderly;
                AddButton(developerToolsCard, adult ? "Already adult" : "Grow selected rat one stage", !adult, game.GrowSelectedRat);
            }
            else
            {
                AddButton(developerToolsCard, "Select a rat to grow it", false, null);
            }
            AddButton(developerToolsCard, "Grow all Pinkies to Young Rats", true, game.GrowAllPinkies);
            AddButton(developerToolsCard, "Grow all Young Rats to Adults", true, game.GrowAllYoungRats);
            bool hasPregnancy = game.PendingPregnancy != null;
            AddButton(developerToolsCard, hasPregnancy ? "Finish pregnancy" : "No pending pregnancy", hasPregnancy, game.FinishPregnancyTesting);
            AddButton(developerToolsCard, "Create test pregnancy due soon", true, game.CreateDeveloperPregnancyDueSoon);
            if (hasPregnancy)
            {
                PregnancyData pending = game.PendingPregnancy;
                RatData mother = BreedingSystem.FindRat(game.Save, pending.motherId);
                AddText(developerToolsCard,
                    "Birth diagnostic  •  " + pending.id +
                    "\nMother: " + (mother == null ? pending.motherId : ColonyFactory.DisplayName(mother)) +
                    "\nDue: " + game.FormatSimulationTimestamp(pending.dueAt) +
                    "\nApproach: " + pending.birthApproachStarted +
                    "  •  Attempts: " + pending.birthAttemptCount +
                    "\nCommit: " + pending.birthCommitState +
                    (string.IsNullOrEmpty(pending.birthFailureReason) ? string.Empty : "\nStatus: " + pending.birthFailureReason),
                    14, new Color(0.75f, 0.88f, 0.84f), TextAnchor.UpperLeft);
            }

            AddText(developerToolsCard, "Colony maintenance", 17, Color.white, TextAnchor.UpperLeft);
            if (game.SelectedRat == null)
            {
                AddButton(developerToolsCard, "Delete Selected Rat  •  select a rat first", false, null);
            }
            else if (game.DeleteConfirmationPending)
            {
                AddText(developerToolsCard, "This removes the selected rat, cancels any pending pregnancy safely, and preserves completed litter history.", 13, new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                AddButtonTo(developerToolsCard, "CONFIRM DELETE  •  " + ColonyFactory.DisplayName(game.SelectedRat), true, game.ConfirmDeleteSelectedRat, new Color(0.55f, 0.18f, 0.16f), 48f);
                AddButtonTo(developerToolsCard, "Cancel deletion", true, game.CancelDeleteSelectedRat, new Color(0.22f, 0.31f, 0.4f), 46f);
            }
            else
            {
                AddButtonTo(developerToolsCard, "Delete Selected Rat", true, game.RequestDeleteSelectedRat, new Color(0.55f, 0.18f, 0.16f), 48f);
            }

            if (game.ResetConfirmationPending)
            {
                AddText(developerToolsCard, "This permanently clears the local save and rebuilds the default colony, habitat, clock, founders, and UI state.", 13, new Color(1f, 0.52f, 0.36f), TextAnchor.UpperLeft);
                AddButtonTo(developerToolsCard, "CONFIRM FULL RESET", true, game.ConfirmFullReset, new Color(0.68f, 0.12f, 0.1f), 50f);
                AddButtonTo(developerToolsCard, "Cancel full reset", true, game.CancelFullReset, new Color(0.22f, 0.31f, 0.4f), 46f);
            }
            else
            {
                AddButtonTo(developerToolsCard, "Fully Reset Game", true, game.RequestFullReset, new Color(0.68f, 0.12f, 0.1f), 50f);
            }
            AddButtonTo(developerToolsCard, "Close Developer Tools", true, CloseDeveloperTools, new Color(0.14f, 0.22f, 0.25f), 46f);
        }

        private void SetVisualDiagnosticMode(RatVisualDiagnosticMode mode)
        {
            RatVisualDiagnostics.SetMode(mode);
            // Keep the diagnostic modal open while changing modes. The
            // existing Close Developer Tools -> Close Settings path hides the
            // panel without resetting the temporary material, and the Normal
            // button explicitly restores the saved materials.
            RebuildDeveloperToolsContent();
            SetOverlayVisibility();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void SetPerformanceIsolationMode(PerformanceIsolationMode mode)
        {
            game.SetPerformanceIsolation(mode);
            RebuildDeveloperToolsContent();
        }
#endif

        private RectTransform CreateCard(string title)
        {
            var card = CreateRect(title, content);
            var image = card.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(image, new Color(0.045f, 0.10f, 0.12f, 0.94f), true);
            // Cards are visual/layout containers. Only actual buttons and
            // selectable rows should receive pointer hits, so a card cannot
            // swallow a click before it reaches a control inside it.
            image.raycastTarget = false;
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 7f;
            layout.padding = new RectOffset(14, 14, 13, 14);
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (!string.IsNullOrEmpty(title))
            {
                AddText(card, title, 18, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            }
            return card;
        }

        private Text AddText(RectTransform parent, string value, int fontSize, Color color, TextAnchor anchor)
        {
            return AddTextTo(parent, value, fontSize, color, anchor);
        }

        private static int UiFontSize(int requestedSize)
        {
            // One shared scale keeps the runtime-generated UI coherent. The
            // small minimums keep helper/status copy and compact navigation
            // readable at the 540x960 reference resolution without affecting
            // the separate world-space enclosure signs.
            int scaled = Mathf.RoundToInt(requestedSize * 1.25f);
            if (requestedSize <= 11) return Mathf.Max(scaled, 14);
            if (requestedSize <= 13) return Mathf.Max(scaled, 16);
            if (requestedSize <= 15) return Mathf.Max(scaled, 18);
            if (requestedSize <= 17) return Mathf.Max(scaled, 20);
            if (requestedSize <= 19) return Mathf.Max(scaled, 23);
            return Mathf.Clamp(scaled, 24, 24);
        }

        private Text AddTextTo(Transform parent, string value, int fontSize, Color color, TextAnchor anchor)
        {
            var objectRoot = new GameObject("Text");
            objectRoot.transform.SetParent(parent, false);
            var text = objectRoot.AddComponent<Text>();
            // Use the bundled Unicode font first. LegacyRuntime.ttf does not
            // reliably contain the male/female symbols in WebGL, and browser
            // fallback fonts are not dependable for Unity UI Text. Keeping
            // one font on every generated label also makes the shared
            // ColonyFactory.DisplayName formatter render consistently across
            // profiles, lists, family history, and event messages.
            text.font = ResolveUiFont();
            text.text = value;
            text.fontSize = UiFontSize(fontSize);
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.resizeTextForBestFit = false;
            // Text is decorative content, not a second input surface. The
            // button Image/Selectable remains the hit target for controls.
            text.raycastTarget = false;
            var layout = objectRoot.AddComponent<LayoutElement>();
            layout.minHeight = text.fontSize + 7f;
            // A wrapped label must report its full preferred height to the
            // parent layout. Without this, long coat/marking names draw over
            // the stats and pregnancy rows beneath them in narrow sell cards.
            var fitter = objectRoot.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return text;
        }

        /// <summary>
        /// Resolve the shared runtime font once, with guarded fallbacks. A
        /// missing optional font must never throw while the shell is being
        /// built; otherwise Unity leaves the generated button backgrounds in
        /// place but none of their labels are rendered.
        /// </summary>
        private static Font ResolveUiFont()
        {
            if (builtInUiFont != null) return builtInUiFont;
            try
            {
                builtInUiFont = Resources.Load<Font>(BundledUiFontResourcePath);
                if (builtInUiFont == null)
                    builtInUiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (builtInUiFont == null)
                    builtInUiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch (Exception exception)
            {
                if (!uiFontWarningLogged)
                {
                    uiFontWarningLogged = true;
                    Debug.LogError("[Rat Habitat] UI font lookup failed; generated controls remain active. " + exception);
                }
            }
            if (builtInUiFont == null && !uiFontWarningLogged)
            {
                uiFontWarningLogged = true;
                Debug.LogError("[Rat Habitat] UI font could not be loaded from Resources/UI/NotoSansJP-Regular, LegacyRuntime.ttf, or Arial.ttf. Generated controls remain active but need an available Unity UI font.");
            }
            return builtInUiFont;
        }

        private Button AddButton(RectTransform parent, string label, bool enabled, UnityEngine.Events.UnityAction action)
        {
            return AddButtonTo(parent, label, enabled, action, new Color(0.16f, 0.38f, 0.33f), 48f);
        }

        private Button AddDiceButtonTo(Transform parent, UnityEngine.Events.UnityAction action, bool enabled, float height)
        {
            Button button = AddButtonTo(parent, string.Empty, enabled, action,
                new Color(0.14f, 0.29f, 0.29f), height, false);
            button.gameObject.name = "Randomize name";
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = height;
                layout.minWidth = height;
                layout.flexibleWidth = 0f;
            }

            RectTransform iconRoot = CreateRect("Dice Icon", button.transform);
            iconRoot.anchorMin = new Vector2(0.5f, 0.5f);
            iconRoot.anchorMax = new Vector2(0.5f, 0.5f);
            iconRoot.pivot = new Vector2(0.5f, 0.5f);
            iconRoot.anchoredPosition = Vector2.zero;
            float iconSize = Mathf.Clamp(height * 0.66f, 28f, 34f);
            iconRoot.sizeDelta = new Vector2(iconSize, iconSize);
            iconRoot.SetAsLastSibling();
            DiceIconGraphic icon = iconRoot.gameObject.AddComponent<DiceIconGraphic>();
            icon.color = enabled
                ? new Color(0.96f, 0.92f, 0.78f, 1f)
                : new Color(0.55f, 0.58f, 0.56f, 1f);
            icon.raycastTarget = false;
            // The icon is added to a live, layout-driven row. Rebuild once
            // after the final size and sibling order are assigned so the
            // first frame cannot retain an empty custom-Graphic mesh.
            icon.SetAllDirty();

            RatUiTooltip tooltip = button.gameObject.AddComponent<RatUiTooltip>();
            tooltip.Label = "Randomize name.";
            return button;
        }

        private static void ValidatePortraitSlot(Transform slot, string label)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (slot == null) return;
            RawImage[] images = slot.GetComponentsInChildren<RawImage>(true);
            int assignedRenderTextures = 0;
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].texture is RenderTexture) assignedRenderTextures++;
            }
            Debug.Assert(images.Length == 1 && assignedRenderTextures == 1,
                "[Rat Habitat] Portrait slot '" + label + "' expected one RawImage with one RenderTexture; found rawImages=" + images.Length + " assignedRenderTextures=" + assignedRenderTextures + ".");
#endif
        }

        private static void ConfigureSquarePortraitSlot(RectTransform slot, float size)
        {
            if (slot == null) return;
            slot.anchorMin = new Vector2(0.5f, 0.5f);
            slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = Vector2.zero;
            slot.sizeDelta = new Vector2(size, size);

            var aspect = slot.gameObject.GetComponent<AspectRatioFitter>();
            if (aspect == null) aspect = slot.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1f;
        }

        private Button AddButtonTo(Transform parent, string label, bool enabled, UnityEngine.Events.UnityAction action, Color color, float height, bool includeLabel = true)
        {
            var objectRoot = new GameObject("Button");
            objectRoot.transform.SetParent(parent, false);
            var image = objectRoot.AddComponent<Image>();
            UiStyle.ApplyRounded(image, enabled ? color : new Color(0.14f, 0.16f, 0.17f), true);
            var button = objectRoot.AddComponent<Button>();
            button.targetGraphic = image;
            button.interactable = enabled;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            // Use the normal Unity UI click path for generated controls. The
            // page viewport is an active UI hit surface, so the world input
            // path will not receive the same tap.
            var clickRelay = objectRoot.AddComponent<DirectUiClickRelay>();
            clickRelay.Configure(this, button, enabled ? action : null);
            var layout = objectRoot.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            if (includeLabel)
            {
                var labelText = AddTextTo(objectRoot.transform, label, 14, enabled ? Color.white : new Color(0.55f, 0.58f, 0.56f), TextAnchor.MiddleCenter);
                labelText.rectTransform.anchorMin = Vector2.zero;
                labelText.rectTransform.anchorMax = Vector2.one;
                labelText.rectTransform.offsetMin = new Vector2(10f, 4f);
                labelText.rectTransform.offsetMax = new Vector2(-10f, -4f);
            }
            return button;
        }

        private sealed class DirectUiClickRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
        {
            private VerticalSliceUI owner;
            private Button button;
            private UnityEngine.Events.UnityAction action;
            private bool pointerSequenceActive;
            private bool pointerSequenceActionInvoked;
            private int pointerSequenceId = int.MinValue;

            public void Configure(VerticalSliceUI uiOwner, Button ownerButton, UnityEngine.Events.UnityAction callback)
            {
                if (button != null) button.onClick.RemoveListener(HandleButtonClick);
                owner = uiOwner;
                button = ownerButton;
                action = callback;
                if (button != null) button.onClick.AddListener(HandleButtonClick);
            }

            private int lastInvokeFrame = -1;

            private void HandleButtonClick()
            {
                if (owner != null && owner.IsFamilyTreePanDragging) return;
                InvokeOnce(pointerSequenceId);
            }

            // Button.onClick can be skipped when a parent ScrollRect owns the
            // pointer sequence. Handle a completed, non-drag pointer-up on the
            // relay as well as the normal click callback so mouse and touch
            // taps reach the intended page control. InvokeOnce keeps these
            // paths from running the action twice.
            public void OnPointerDown(PointerEventData eventData)
            {
                pointerSequenceId = eventData == null ? int.MinValue : eventData.pointerId;
                pointerSequenceActionInvoked = false;
                pointerSequenceActive = eventData != null &&
                    eventData.button == PointerEventData.InputButton.Left &&
                    IsFallbackInteractable;
                if (pointerSequenceActive && owner != null)
                    owner.RegisterUiPointerDown(pointerSequenceId);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                bool validTap = pointerSequenceActive && eventData != null &&
                    eventData.button == PointerEventData.InputButton.Left &&
                    Vector2.Distance(eventData.pressPosition, eventData.position) <= 8f;
                pointerSequenceActive = false;
                if (owner != null && eventData != null)
                    owner.RegisterUiPointerUp(eventData.pointerId);
                if (validTap)
                    pointerSequenceActionInvoked = InvokeOnce(pointerSequenceId);
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                pointerSequenceActive = false;
                if (owner != null && owner.IsFamilyTreePanDragging) return;
                int pointerId = eventData == null ? pointerSequenceId : eventData.pointerId;
                if (pointerSequenceActionInvoked) return;
                if (eventData == null || eventData.button == PointerEventData.InputButton.Left)
                    pointerSequenceActionInvoked = InvokeOnce(pointerId);
            }

            public bool IsFallbackInteractable
            {
                get { return button != null && button.isActiveAndEnabled && button.IsInteractable() && action != null; }
            }

            public bool ContainsScreenPoint(Vector2 screenPoint)
            {
                var rect = transform as RectTransform;
                return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
            }

            public void InvokeFallback(int pointerId)
            {
                pointerSequenceId = pointerId;
                if (pointerSequenceActionInvoked) return;
                pointerSequenceActionInvoked = InvokeOnce(pointerId);
            }

            private bool InvokeOnce(int pointerId)
            {
                if (owner != null && owner.IsFamilyTreePanDragging) return false;
                if (!IsFallbackInteractable || lastInvokeFrame == Time.frameCount) return false;
                if (owner != null && !owner.CanInvokeUiAction(pointerId)) return false;
                lastInvokeFrame = Time.frameCount;
                if (owner != null) owner.RecordUiAction(pointerId);
                if (action != null) action.Invoke();
                return true;
            }
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root.AddComponent<RectTransform>();
        }

        private static string SexLabel(RatSex sex)
        {
            return sex == RatSex.Female ? "Female" : "Male";
        }

        private string ParentSummary(RatData rat)
        {
            if (rat == null) return "N/A";
            if (string.IsNullOrEmpty(rat.motherId) && string.IsNullOrEmpty(rat.fatherId)) return "N/A";

            RatData mother = game == null ? null : BreedingSystem.FindHistoricalRat(game.Save, rat.motherId);
            RatData father = game == null ? null : BreedingSystem.FindHistoricalRat(game.Save, rat.fatherId);
            if (mother != null && father != null && mother.generation == 0 && father.generation == 0) return "N/A";

            string motherName = mother == null ? rat.motherId : ColonyFactory.DisplayName(mother);
            string fatherName = father == null ? rat.fatherId : ColonyFactory.DisplayName(father);
            return (string.IsNullOrEmpty(motherName) ? "—" : motherName) + " / " + (string.IsNullOrEmpty(fatherName) ? "—" : fatherName);
        }

        private static string ServiceLabel(HabitatObjectType type)
        {
            switch (type)
            {
                case HabitatObjectType.Food: return "Refill food";
                case HabitatObjectType.Water: return "Refresh water";
                case HabitatObjectType.Nest: return "Tidy nest";
                case HabitatObjectType.ExerciseWheel: return "Check wheel";
                default: return "Check hide";
            }
        }
    }
}
