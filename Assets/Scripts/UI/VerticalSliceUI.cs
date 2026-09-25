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
        private const float MaximumUiColumnWidth = 540f;
        private const float MinimumSideMargin = 12f;
        // The header has a title/status strip, navigation strip, and exactly
        // three persistent simulation-speed controls. The world camera uses
        // the same reservation so the habitat never renders under the header.
        private const float HeaderHeight = 122f;
        private const float PageTopInset = 126f;
        // The profile is a fixed phone-safe frame: the live habitat remains
        // visible above it, while the opaque information surface is anchored
        // to the lower edge and grows upward when expanded.
        private const float RatProfileFrameHeight = 730f;
        private const float RatProfileCollapsedInformationHeight = 205f;
        private const float RatProfileExpandedInformationHeight = 406f;
        private const float RatProfileBottomPadding = 16f;
        private const float ModalMaximumWidth = 500f;
        private const int PageBottomSafePadding = 104;

        private GameBootstrap game;
        private Canvas canvas;
        private RectTransform safeRoot;
        private RectTransform headerContent;
        private RectTransform welcomeOverlay;
        private RectTransform welcomeCard;
        private RectTransform settingsOverlay;
        private RectTransform settingsCard;
        private RectTransform developerToolsOverlay;
        private RectTransform developerToolsViewport;
        private RectTransform developerToolsCard;
        private RectTransform ratAnimationShowcaseOverlay;
        private RectTransform ratAnimationShowcaseCard;
        private RectTransform eventLogOverlay;
        private RectTransform eventLogCard;
        private RectTransform eventLogContent;
        private Text clockText;
        private Text walletText;
        private Text liveEventText;
        private Button eventLogToggleButton;
        private ScrollRect eventLogScroll;
        private ScrollRect pageScroll;
        private ScrollRect mateListScroll;
        private ScrollRect ratRosterScroll;
        private ScrollRect storeRatListScroll;
        private ScrollRect familyTreeScroll;
        private RectTransform familyTreeViewport;
        private RectTransform familyTreeContent;
        private RectTransform familyTreeInputBlocker;
        private float familyTreeZoom = 1f;
        private float familyTreeLastPinchDistance;
        private bool familyTreePinching;
        private RectTransform myRatsInputBlocker;
        private RectTransform content;
        private VerticalLayoutGroup contentLayout;
        private string lastSignature;
        private bool ready;
        private static Font builtInUiFont;
        private const string BundledUiFontResourcePath = "UI/NotoSansJP-Regular";
        private int layoutScreenWidth = -1;
        private int layoutScreenHeight = -1;
        // World interaction must be available as soon as the scene renders.
        // The welcome copy remains available through Settings > Show Welcome Again.
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
        private readonly Dictionary<MainPanel, Button> topNavigationButtons = new Dictionary<MainPanel, Button>();
        private readonly Dictionary<int, Button> simulationSpeedButtons = new Dictionary<int, Button>();
        // A generated page control can be reached by both Unity's normal
        // Button/EventSystem path and InteractionManager's manual fallback
        // path in the same frame. Refreshing the page destroys the old relay,
        // so a per-relay guard alone is not enough: the replacement relay
        // could invoke the same action a second time. Keep one UI action per
        // frame across regenerated controls.
        private static int lastUiActionFrame = -1;
        private MainPanel activeMainPanel = MainPanel.None;
        private RosterSortField rosterSortField = RosterSortField.Name;
        private bool rosterSortAscending = true;
        private string expandedMyRatsId;
        private string familyTreeSubjectId;
        private string profileMoreInformationRatId;
        private bool profileMoreInformationExpanded;

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
            CoatColor,
            Generation,
        }

        public void Initialize(GameBootstrap owner)
        {
            game = owner;
            portraitPreview = GetComponent<RatPortraitPreview>();
            if (portraitPreview == null) portraitPreview = gameObject.AddComponent<RatPortraitPreview>();
            portraitPreview.Configure(game.RatVisualFactory);
            ratAnimationShowcase = GetComponent<RatAnimationShowcase>();
            if (ratAnimationShowcase == null) ratAnimationShowcase = gameObject.AddComponent<RatAnimationShowcase>();
            ratAnimationShowcase.Configure(game.RatVisualFactory, FindAnimationShowcaseSample());
            BuildShell();
            ready = true;
            Refresh(true);
        }

        /// <summary>
        /// Page controls live inside a nested ScrollRect and are generated at
        /// runtime. On some Unity 2022 Android/editor input paths the
        /// ScrollRect receives the pointer but does not complete the child's
        /// Button click. Keep the standard EventSystem path for the fixed
        /// header, while using the final screen-position hit for generated
        /// page controls so touch and mouse activation remain deterministic.
        /// </summary>
        private void ProcessFallbackUiInput()
        {
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
                    }
                    else if (touch.phase == TouchPhase.Ended && touch.fingerId == fallbackTouchFingerId)
                    {
                        DirectUiClickRelay relay = fallbackTouchRelay;
                        bool moved = Vector2.Distance(fallbackTouchDownPosition, touch.position) > 24f;
                        fallbackTouchRelay = null;
                        fallbackTouchFingerId = -1;
                        if (!moved && relay != null && relay.ContainsScreenPoint(touch.position))
                            relay.InvokeFallback();
                    }
                    else if (touch.phase == TouchPhase.Canceled && touch.fingerId == fallbackTouchFingerId)
                    {
                        fallbackTouchRelay = null;
                        fallbackTouchFingerId = -1;
                    }
                }
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                fallbackMouseDownPosition = Input.mousePosition;
                fallbackMouseRelay = FindFallbackRelay(fallbackMouseDownPosition);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                DirectUiClickRelay relay = fallbackMouseRelay;
                bool moved = Vector2.Distance(fallbackMouseDownPosition, Input.mousePosition) > 8f;
                fallbackMouseRelay = null;
                if (!moved && relay != null && relay.ContainsScreenPoint(Input.mousePosition))
                    relay.InvokeFallback();
            }
        }

        private DirectUiClickRelay FindFallbackRelay(Vector2 screenPoint)
        {
            DirectUiClickRelay[] relays = GetComponentsInChildren<DirectUiClickRelay>(false);
            DirectUiClickRelay best = null;
            int bestDepth = int.MinValue;
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
            if (relay == null) return IsModalOverlayOpen;
            relay.InvokeFallback();
            return true;
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
                return welcomeOpen || settingsOpen || developerToolsOpen || ratAnimationShowcaseOpen || eventLogOpen ||
                    (activeMainPanel == MainPanel.MyRats && !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                     !ratAnimationShowcaseOpen && !eventLogOpen) ||
                    (activeMainPanel == MainPanel.FamilyTree && !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                     !ratAnimationShowcaseOpen && !eventLogOpen);
            }
        }

        private bool IsRelayInsideActiveModal(DirectUiClickRelay relay)
        {
            if (relay == null) return false;
            RectTransform activeOverlay = null;
            if (welcomeOpen) activeOverlay = welcomeOverlay;
            else if (settingsOpen) activeOverlay = settingsOverlay;
            else if (developerToolsOpen) activeOverlay = developerToolsOverlay;
            else if (ratAnimationShowcaseOpen) activeOverlay = ratAnimationShowcaseOverlay;
            else if (eventLogOpen) activeOverlay = eventLogOverlay;
            if (activeOverlay == null && activeMainPanel == MainPanel.MyRats &&
                !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                !ratAnimationShowcaseOpen && !eventLogOpen)
            {
                // My Rats is a modal page from the world's point of view, but
                // its own generated controls still need the normal UI fallback
                // path when EventSystem dispatch is unavailable on Android.
                return (pageScroll != null && relay.transform.IsChildOf(pageScroll.transform)) ||
                    (headerContent != null && relay.transform.IsChildOf(headerContent));
            }
            if (activeOverlay == null && activeMainPanel == MainPanel.FamilyTree &&
                !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                !ratAnimationShowcaseOpen && !eventLogOpen)
            {
                return (pageScroll != null && relay.transform.IsChildOf(pageScroll.transform)) ||
                    (headerContent != null && relay.transform.IsChildOf(headerContent));
            }
            return activeOverlay != null && relay.transform.IsChildOf(activeOverlay);
        }

        public void Refresh(bool force)
        {
            if (!ready || game == null) return;
            RefreshHeader();
            if (game.BreedingOpen && activeMainPanel == MainPanel.None)
            {
                activeMainPanel = MainPanel.Breeding;
            }
            RefreshTopNavigationState();
            string signature = game.UiSignature;
            if (!force && signature == lastSignature) return;

            float previousNormalized = string.IsNullOrEmpty(lastSignature) ? 1f : pageScroll.verticalNormalizedPosition;
            float previousMateNormalized = mateListScroll == null ? 1f : mateListScroll.verticalNormalizedPosition;
            float previousRosterNormalized = ratRosterScroll == null ? 1f : ratRosterScroll.verticalNormalizedPosition;
            float previousStoreNormalized = storeRatListScroll == null ? 1f : storeRatListScroll.verticalNormalizedPosition;
            RebuildContent();
            if (developerToolsOpen) RebuildDeveloperToolsContent();
            if (ratAnimationShowcaseOpen) RefreshAnimationShowcasePanel();
            lastSignature = signature;
            Canvas.ForceUpdateCanvases();
            // Rebuilds preserve the user's current page position. No focus or
            // viewport jump is requested by a selection or clock refresh.
            pageScroll.verticalNormalizedPosition = previousNormalized;
            if (mateListScroll != null) mateListScroll.verticalNormalizedPosition = previousMateNormalized;
            if (ratRosterScroll != null) ratRosterScroll.verticalNormalizedPosition = previousRosterNormalized;
            if (storeRatListScroll != null) storeRatListScroll.verticalNormalizedPosition = previousStoreNormalized;
            RefreshTopNavigationState();
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
            if (clockText != null) clockText.text = game.ClockLabel;
            if (walletText != null)
            {
                int balance = game.Save == null ? 0 : game.Save.colonyCredits;
                walletText.text = "Wallet  $" + balance.ToString("N0");
            }
            if (liveEventText != null)
            {
                string liveMessage = game.LiveEventMessage;
                // The habitat itself supplies the large in-world sign. Keep a
                // compact page indicator in the header whenever no transient
                // event is being announced, so a swipe is still discoverable
                // after the Habitat controls collapse.
                liveEventText.text = string.IsNullOrEmpty(liveMessage)
                    ? game.HabitatPageLabel
                    : liveMessage;
            }
            if (eventLogToggleButton != null)
            {
                Text eventLabel = eventLogToggleButton.GetComponentInChildren<Text>();
                if (eventLabel != null)
                {
                    eventLabel.text = game.RecentEventCount > 0
                        ? "Events (" + game.RecentEventCount + ")"
                        : "Events";
                }
            }
            string eventLogSignature = game.EventLogSignature;
            if (eventLogOpen && eventLogSignature != lastEventLogSignature)
                RefreshEventLogPanel();
            for (int index = 0; index < liveTimedTextUpdates.Count; index++)
            {
                liveTimedTextUpdates[index]?.Invoke();
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

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

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

            var title = AddText(headerContent, "RAT EMPIRE", 16, Color.white, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 0.68f);
            title.rectTransform.anchorMax = new Vector2(0.34f, 1f);
            title.rectTransform.offsetMin = new Vector2(12f, 1f);
            title.rectTransform.offsetMax = new Vector2(0f, -1f);
            title.fontStyle = FontStyle.Bold;

            walletText = AddText(headerContent, "$0", 12, new Color(1f, 0.82f, 0.38f), TextAnchor.MiddleLeft);
            walletText.rectTransform.anchorMin = new Vector2(0f, 0.54f);
            walletText.rectTransform.anchorMax = new Vector2(0.34f, 0.69f);
            walletText.rectTransform.offsetMin = new Vector2(12f, 0f);
            walletText.rectTransform.offsetMax = new Vector2(0f, -1f);

            clockText = AddText(headerContent, "Day 1", 14, new Color(0.73f, 0.9f, 0.78f), TextAnchor.UpperLeft);
            clockText.rectTransform.anchorMin = new Vector2(0.34f, 0.54f);
            clockText.rectTransform.anchorMax = new Vector2(0.55f, 1f);
            clockText.rectTransform.offsetMin = new Vector2(2f, 1f);
            clockText.rectTransform.offsetMax = new Vector2(0f, -1f);

            eventLogToggleButton = AddButtonTo(headerContent, "Events", true, ToggleEventLog,
                new Color(0.11f, 0.25f, 0.25f, 1f), 30f);
            RectTransform eventLogButtonRect = eventLogToggleButton.GetComponent<RectTransform>();
            eventLogButtonRect.anchorMin = new Vector2(0.55f, 0.54f);
            eventLogButtonRect.anchorMax = new Vector2(1f, 0.72f);
            eventLogButtonRect.offsetMin = new Vector2(0f, 1f);
            eventLogButtonRect.offsetMax = new Vector2(-12f, -1f);

            liveEventText = AddText(headerContent, string.Empty, 10,
                new Color(1f, 0.82f, 0.38f), TextAnchor.MiddleRight);
            liveEventText.rectTransform.anchorMin = new Vector2(0.55f, 0.72f);
            liveEventText.rectTransform.anchorMax = new Vector2(1f, 1f);
            liveEventText.rectTransform.offsetMin = new Vector2(0f, 1f);
            liveEventText.rectTransform.offsetMax = new Vector2(-12f, -1f);
            liveEventText.horizontalOverflow = HorizontalWrapMode.Wrap;
            liveEventText.verticalOverflow = VerticalWrapMode.Truncate;

            var navigation = CreateRect("Top Navigation", headerContent);
            navigation.anchorMin = new Vector2(0f, 0.25f);
            navigation.anchorMax = new Vector2(1f, 0.54f);
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
            AddTopNavigationButton(navigation, MainPanel.Habitat, "Habitat");
            AddTopNavigationButton(navigation, MainPanel.MyRats, "My Rats");
            AddTopNavigationButton(navigation, MainPanel.Store, "Store");
            AddTopNavigationButton(navigation, MainPanel.Upgrades, "Upgrades");
            AddTopNavigationButton(navigation, MainPanel.Settings, "Settings");

            var speedRow = CreateRect("Simulation Speed Controls", headerContent);
            speedRow.anchorMin = new Vector2(0f, 0f);
            speedRow.anchorMax = new Vector2(1f, 0.23f);
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
            myRatsInputBlocker.SetSiblingIndex(scrollObject.transform.GetSiblingIndex());

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
            familyTreeInputBlocker.SetSiblingIndex(scrollObject.transform.GetSiblingIndex());

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
                settingsCard.sizeDelta = new Vector2(modalWidth, 0f);
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
            if (developerToolsViewport != null)
            {
                float viewportHeight = Mathf.Min(720f, Mathf.Max(360f, safeRoot.rect.height - 40f));
                developerToolsViewport.sizeDelta = new Vector2(modalWidth, viewportHeight);
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
            AddText(welcomeCard, "Welcome to the habitat", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(welcomeCard, "Meet your randomized starter pair in their cozy 3D habitat.", 16, Color.white, TextAnchor.UpperLeft);
            AddText(welcomeCard, "Tap a rat or habitat object to interact. Breed, care for the colony, and watch each generation grow.", 14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddButtonTo(welcomeCard, "Continue", true, CloseWelcome, new Color(0.16f, 0.38f, 0.33f), 46f);
        }

        private void BuildSettingsPopup()
        {
            settingsOverlay = CreateModalOverlay("Settings Popup", new Color(0.01f, 0.03f, 0.04f, 0.74f), out settingsCard);
            AddText(settingsCard, "Settings", 22, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
            AddText(settingsCard, "Local save", 17, Color.white, TextAnchor.UpperLeft);
            AddText(settingsCard, "Save the colony, pregnancy state, litters, growth, genes, traits, and timestamps locally on this device.", 14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddText(settingsCard, "Pairing Habitat: " + (GameConfig.PairingPregnancyChance * 100f).ToString("0") +
                "% pregnancy chance per " + (GameConfig.PairingCheckIntervalMs / 1000L).ToString() + " real-time seconds.",
                14, new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddButtonTo(settingsCard, "Save now", true, game.SaveNow, new Color(0.16f, 0.38f, 0.33f), 46f);
            AddButtonTo(settingsCard, "Show Welcome Again", true, ShowWelcomeAgain, new Color(0.22f, 0.31f, 0.4f), 46f);
            AddButtonTo(settingsCard, "Developer Tools", true, OpenDeveloperTools, new Color(0.12f, 0.27f, 0.29f), 46f);
            AddButtonTo(settingsCard, "Close Settings", true, CloseSettings, new Color(0.14f, 0.22f, 0.25f), 46f);
            SetOverlayVisibility();
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
            // can leak through a modal. The overlay is inserted first, which
            // leaves the always-visible header above it as the close path.
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
            overlay.SetAsFirstSibling();

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
            if (panel == MainPanel.Habitat)
            {
                // Habitat is the same escape hatch as the profile's
                // "Return to Habitat" action: clear the selected entity and
                // leave the player looking at the unobstructed habitat.
                button.onClick.AddListener(ReturnToHabitatFromNavigation);
            }
            else
            {
                button.onClick.AddListener(() => ToggleTopPanel(panel));
            }
            var labelText = AddTextTo(objectRoot.transform, label, 13, Color.white, TextAnchor.MiddleCenter);
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(2f, 1f);
            labelText.rectTransform.offsetMax = new Vector2(-2f, -1f);
            topNavigationButtons[panel] = button;
            return button;
        }

        private Button AddSimulationSpeedButton(Transform parent, float speed)
        {
            int speedInt = Mathf.RoundToInt(speed);
            var button = AddButtonTo(parent, speedInt + "×", true,
                () => game.SetSimulationSpeed(speed), new Color(0.14f, 0.29f, 0.29f), 28f);
            button.gameObject.name = "Simulation Speed " + speedInt + "x";
            simulationSpeedButtons[speedInt] = button;
            return button;
        }

        private void ReturnToHabitatFromNavigation()
        {
            bool collapse = activeMainPanel == MainPanel.Habitat;
            if (game != null) game.DeactivateMultipleSelection();
            expandedMyRatsId = null;
            familyTreeSubjectId = null;
            ResetProfileInformationExpansion();
            welcomeOpen = false;
            settingsOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            activeMainPanel = collapse ? MainPanel.None : MainPanel.Habitat;

            if (game != null)
            {
                if (game.BreedingOpen) game.CloseBreeding();
                // Collapsing the Habitat panel must not reset the camera to
                // Overview. In particular, keep a focused Pairing Habitat
                // view while its controls are hidden. Opening Habitat from a
                // different page still behaves like Return to Habitat and
                // clears the selection as before.
                if (!collapse) game.ReturnToHabitat();
            }
            else
            {
                Refresh(true);
            }

            SetOverlayVisibility();
            RefreshTopNavigationState();
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
            if (panel != MainPanel.MyRats) expandedMyRatsId = null;
            if (panel != MainPanel.FamilyTree) familyTreeSubjectId = null;
            ResetProfileInformationExpansion();
            settingsOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;

            if (game != null && game.BreedingOpen && panel != MainPanel.Breeding)
            {
                // Leaving breeding through a top-level panel also clears its
                // temporary parent-selection state, so it cannot reappear
                // behind another panel on the next refresh.
                game.CloseBreeding();
            }

            activeMainPanel = samePanel ? MainPanel.None : panel;
            Refresh(true);
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void RefreshTopNavigationState()
        {
            foreach (var entry in topNavigationButtons)
            {
                Button button = entry.Value;
                if (button == null) continue;
                Image image = button.GetComponent<Image>();
                if (image == null) continue;
                bool open = entry.Key == activeMainPanel ||
                    (entry.Key == MainPanel.Settings && settingsOpen) ||
                    (entry.Key == MainPanel.DeveloperTools && developerToolsOpen);
                Text label = button.GetComponentInChildren<Text>();
                if (label != null && entry.Key == MainPanel.MyRats)
                {
                    label.text = "My Rats (" + CountColonyRats() + ")";
                }
                image.color = open
                    ? new Color(0.22f, 0.48f, 0.36f, 1f)
                    : new Color(0.11f, 0.25f, 0.25f, 1f);
            }
            foreach (var entry in simulationSpeedButtons)
            {
                if (entry.Value == null) continue;
                Image image = entry.Value.GetComponent<Image>();
                if (image != null)
                {
                    image.color = Mathf.Abs(game.SimulationSpeed - entry.Key) < 0.01f
                        ? new Color(0.28f, 0.56f, 0.38f, 1f)
                        : new Color(0.14f, 0.29f, 0.29f, 1f);
                }
                Text label = entry.Value.GetComponentInChildren<Text>();
                if (label != null) label.text = entry.Key + "×";
            }
        }

        private void SetOverlayVisibility()
        {
            if (welcomeOverlay != null) welcomeOverlay.gameObject.SetActive(welcomeOpen);
            if (settingsOverlay != null) settingsOverlay.gameObject.SetActive(settingsOpen && !welcomeOpen);
            if (developerToolsOverlay != null) developerToolsOverlay.gameObject.SetActive(developerToolsOpen && !welcomeOpen && !settingsOpen);
            if (ratAnimationShowcaseOverlay != null) ratAnimationShowcaseOverlay.gameObject.SetActive(ratAnimationShowcaseOpen && !welcomeOpen && !settingsOpen && !developerToolsOpen);
            if (eventLogOverlay != null) eventLogOverlay.gameObject.SetActive(eventLogOpen && !welcomeOpen && !settingsOpen && !developerToolsOpen && !ratAnimationShowcaseOpen);
            if (myRatsInputBlocker != null)
            {
                bool blockWorldForMyRats = activeMainPanel == MainPanel.MyRats &&
                    !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                    !ratAnimationShowcaseOpen && !eventLogOpen;
                myRatsInputBlocker.gameObject.SetActive(blockWorldForMyRats);
            }
            if (familyTreeInputBlocker != null)
            {
                bool blockWorldForFamilyTree = activeMainPanel == MainPanel.FamilyTree &&
                    !welcomeOpen && !settingsOpen && !developerToolsOpen &&
                    !ratAnimationShowcaseOpen && !eventLogOpen;
                familyTreeInputBlocker.gameObject.SetActive(blockWorldForFamilyTree);
            }
        }

        private void CloseWelcome()
        {
            welcomeOpen = false;
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
            settingsOpen = true;
            activeMainPanel = MainPanel.Settings;
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void CloseSettings()
        {
            settingsOpen = false;
            activeMainPanel = MainPanel.None;
            SetOverlayVisibility();
            Refresh(true);
        }

        private void ShowWelcomeAgain()
        {
            settingsOpen = false;
            welcomeOpen = true;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            activeMainPanel = MainPanel.None;
            SetOverlayVisibility();
        }

        private void OpenDeveloperTools()
        {
            if (game != null) game.DeactivateMultipleSelection();
            welcomeOpen = false;
            settingsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
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
            activeMainPanel = MainPanel.Settings;
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        public void CloseTransientPanels()
        {
            if (game != null) game.DeactivateMultipleSelection();
            expandedMyRatsId = null;
            familyTreeSubjectId = null;
            welcomeOpen = false;
            settingsOpen = false;
            developerToolsOpen = false;
            ratAnimationShowcaseOpen = false;
            eventLogOpen = false;
            activeMainPanel = MainPanel.None;
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void OpenRatAnimationShowcase()
        {
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
            if (ratAnimationShowcaseOpen) RefreshAnimationShowcasePanel();
            UpdateFamilyTreeZoomInput();
        }

        private void UpdateFamilyTreeZoomInput()
        {
            if (activeMainPanel != MainPanel.FamilyTree || familyTreeViewport == null ||
                familyTreeContent == null || !familyTreeViewport.gameObject.activeInHierarchy)
            {
                familyTreePinching = false;
                return;
            }

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

        private void SetFamilyTreeZoom(float value)
        {
            familyTreeZoom = Mathf.Clamp(value, 0.35f, 2.2f);
            if (familyTreeContent == null) return;
            familyTreeContent.localScale = Vector3.one * familyTreeZoom;
            Canvas.ForceUpdateCanvases();
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
            familyTreeScroll = null;
            familyTreeViewport = null;
            familyTreeContent = null;
            familyTreePinching = false;
            liveTimedTextUpdates.Clear();
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
                string action = euthanize
                    ? "EUTHANIZE\n$" + GameConfig.EuthanasiaCostDollars
                    : "SELL\n$" + game.SellValue(rat);
                AddStoreRatCard(listContent, rat, action,
                    euthanize ? new Color(0.55f, 0.16f, 0.13f) : new Color(0.22f, 0.42f, 0.28f),
                    () =>
                    {
                        if (euthanize) OpenStoreForRatManagement(ratId, true);
                        else game.RequestSellRat(ratId);
                    },
                    !euthanize && game.IsSellConfirmationFor(ratId));
            }
        }

        private void AddStoreRatCard(Transform parent, RatData rat, string actionLabel, Color actionColor,
            UnityEngine.Events.UnityAction action, bool inlineSaleConfirmation = false)
        {
            if (parent == null || rat == null) return;

            float cardHeight = inlineSaleConfirmation ? 188f : 136f;
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

            if (inlineSaleConfirmation)
            {
                AddText(info, "Warnings: " + game.SaleWarningsFor(rat), 11,
                    new Color(1f, 0.72f, 0.38f), TextAnchor.UpperLeft);
                AddInlineSaleControls(card, actionWidth);
            }
            else
            {
                var actionButton = AddButtonTo(card, actionLabel, true, action, actionColor, 58f);
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
            var actionButton = AddButtonTo(card, "BUY\n$" + listing.price, canBuy,
                () => game.BuyStoreRat(listing.id), new Color(0.16f, 0.40f, 0.34f), 58f);
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
                AddText(card, "Move every rat out of Pairing Habitat? Pregnant and nursing families will be routed to Nursery.",
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
            if (profileMoreInformationRatId != rat.id)
            {
                profileMoreInformationRatId = rat.id;
                profileMoreInformationExpanded = false;
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
            detailsScroll.verticalNormalizedPosition = 1f;

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

            Text nameText = AddText(details, ColonyFactory.DisplayName(rat), 18, new Color(0.98f, 0.78f, 0.32f), TextAnchor.UpperLeft);
            nameText.fontStyle = FontStyle.Bold;
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
            BindLiveText(activityText, () => "Current activity: " + game.CurrentRatActivityLabel(rat));

            AddBasicRatProfileInformation(details, rat, 14);
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

            AddButtonTo(more, "Family Tree", true, () => OpenFamilyTree(rat.id),
                new Color(0.22f, 0.34f, 0.43f), 42f);

            bool liveRat = !historical && BreedingSystem.FindRat(game.Save, rat.id) != null;
            if (liveRat && game.IsSellConfirmationFor(rat.id))
            {
                AddInlineProfileSaleConfirmation(more, rat);
            }
            else if (liveRat)
            {
                AddButtonTo(more, "Sell Rat\n$" + game.SellValue(rat), true,
                    () => game.RequestSellRat(rat.id), new Color(0.17f, 0.34f, 0.37f), 46f);
            }

            if (liveRat && rat.enclosure == RatEnclosure.Pairing)
            {
                bool dependentLitter = rat.sex == RatSex.Female && EnclosureSystem.HasDependentPinkies(game.Save, rat.id);
                bool pendingPregnancy = EnclosureSystem.IsPregnant(game.Save, rat);
                bool canRemove = !(rat.stage == RatStage.Adult && rat.sex == RatSex.Female &&
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
                AddPregnancyProgress(more, pregnancy);
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
            AddText(parent, "Known genes: " + KnownGeneSummary(rat), fontSize - 1,
                new Color(0.78f, 0.86f, 0.82f), TextAnchor.UpperLeft);
            AddText(parent, "Parents: " + ParentSummary(rat) + "  •  Litter: " + LitterNameForRat(rat), fontSize - 1,
                new Color(0.70f, 0.78f, 0.74f), TextAnchor.UpperLeft);
            AddText(parent, "Current habitat: " + EnclosureSystem.Label(rat.enclosure), fontSize,
                new Color(0.78f, 0.90f, 0.82f), TextAnchor.UpperLeft);
            Text reproductiveStateText = AddText(parent, string.Empty, fontSize,
                new Color(0.72f, 0.84f, 0.78f), TextAnchor.UpperLeft);
            BindLiveText(reproductiveStateText, () => "Reproductive state: " + ReproductiveStateLabel(rat));
        }

        private void ToggleProfileMoreInformation()
        {
            profileMoreInformationExpanded = !profileMoreInformationExpanded;
            Refresh(true);
        }

        private void ResetProfileInformationExpansion()
        {
            profileMoreInformationRatId = null;
            profileMoreInformationExpanded = false;
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
            AddText(confirmation, "Warnings: " + game.SaleWarningsFor(rat), 12,
                new Color(1f, 0.72f, 0.38f), TextAnchor.UpperLeft);
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
            ResetProfileInformationExpansion();
            familyTreeSubjectId = ratId;
            activeMainPanel = MainPanel.FamilyTree;
            Refresh(true);
            SetOverlayVisibility();
            RefreshTopNavigationState();
        }

        private void ReturnToFamilyTreeProfile()
        {
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
            treeContent.localScale = Vector3.one * familyTreeZoom;
            scroll.content = treeContent;

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
            scroll.horizontalNormalizedPosition = 0.5f;
            scroll.verticalNormalizedPosition = 0.5f;
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
            relay.Configure(button, () => SelectFamilyTreeSubject(ratId));

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

            int ratCount = CountColonyRats();
            var card = CreateCard("My Rats");
            AddText(card, ratCount + " colony rats  •  Sort: " + RosterSortLabel(), 13,
                new Color(0.78f, 0.9f, 0.82f), TextAnchor.UpperLeft);
            AddButtonTo(card, "Close My Rats", true, () => ToggleTopPanel(MainPanel.MyRats),
                new Color(0.14f, 0.22f, 0.25f), 40f);
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
                AddButtonTo(groupRow, "Nursery", true, () => game.RequestMoveSelectedRats(RatEnclosure.Nursery), new Color(0.25f, 0.25f, 0.44f), 40f);
                AddButtonTo(groupRow, "Pairing", true, () => game.RequestMoveSelectedRats(RatEnclosure.Pairing), new Color(0.25f, 0.38f, 0.24f), 40f);
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
            listImage.raycastTarget = true;
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

            var roster = new List<RatData>();
            foreach (var rat in game.Save.rats)
            {
                if (rat != null) roster.Add(rat);
            }
            roster.Sort(CompareRosterRats);
            if (roster.Count == 0)
            {
                AddTextTo(listContent, "No rats are currently in the colony.", 14, new Color(1f, 0.72f, 0.42f), TextAnchor.UpperLeft);
                return;
            }

            foreach (var rat in roster) AddRatRosterRow(listContent, rat);
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
                RosterSortField.CoatColor,
                RosterSortField.Generation,
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
                    AddButtonTo(row, RosterFieldLabel(field) + (active ? (rosterSortAscending ? " ↑" : " ↓") : string.Empty), true,
                        () => SetRosterSort(field), color, 34f);
                }
            }
        }

        private void SetRosterSort(RosterSortField field)
        {
            if (rosterSortField == field) rosterSortAscending = !rosterSortAscending;
            else
            {
                rosterSortField = field;
                rosterSortAscending = true;
            }
            Refresh(true);
        }

        private string RosterSortLabel()
        {
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
                case RosterSortField.CoatColor: return "Coat";
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
                case RosterSortField.Fertility: result = TraitValue(first, 2).CompareTo(TraitValue(second, 2)); break;
                case RosterSortField.Sex: result = first.sex.CompareTo(second.sex); break;
                case RosterSortField.CoatColor: result = string.Compare(CoatSortKey(first), CoatSortKey(second), StringComparison.OrdinalIgnoreCase); break;
                case RosterSortField.Generation: result = first.generation.CompareTo(second.generation); break;
                default: result = string.Compare(first.name, second.name, StringComparison.OrdinalIgnoreCase); break;
            }
            if (!rosterSortAscending) result = -result;
            if (result != 0) return result;

            result = string.Compare(first.name, second.name, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
            return string.Compare(first.id, second.id, StringComparison.OrdinalIgnoreCase);
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

        private static string CoatSortKey(RatData rat)
        {
            if (rat == null || rat.phenotype == null || !rat.phenotype.furRevealed) return "unknown";
            return rat.phenotype.coatColorLabel ?? "unknown";
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
            rowClickRelay.Configure(rowButton, () =>
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

            var header = CreateRect("My Rats Row Header", rowRoot.transform);
            var headerImage = header.gameObject.AddComponent<Image>();
            UiStyle.ApplyRounded(headerImage, expanded || selected ? new Color(0.18f, 0.34f, 0.28f) : new Color(0.08f, 0.14f, 0.13f), true);
            // The parent card owns input. This decorative header image must
            // not create a nested button that competes with the card toggle.
            headerImage.raycastTarget = false;

            var rowLayout = rowRoot.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = expanded ? 336f : 116f;
            rowLayout.minHeight = expanded ? 336f : 116f;
            var horizontal = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.padding = new RectOffset(8, 8, 7, 7);
            horizontal.spacing = 8f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;

            const float rosterPortraitSize = 80f;
            const float headerHeight = 96f;
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
            infoLayout.minHeight = 80f;
            infoLayout.preferredHeight = 80f;
            var infoText = AddTextTo(infoRoot, BuildRatRosterLabel(rat, selected), 14, Color.white, TextAnchor.MiddleLeft);
            infoText.rectTransform.anchorMin = Vector2.zero;
            infoText.rectTransform.anchorMax = Vector2.one;
            infoText.rectTransform.offsetMin = new Vector2(2f, 0f);
            infoText.rectTransform.offsetMax = new Vector2(-2f, 0f);
            BindLiveText(infoText, () => BuildRatRosterLabel(rat, selected));

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
                var detailElement = detailPanel.gameObject.AddComponent<LayoutElement>();
                detailElement.preferredHeight = 222f;
                detailElement.minHeight = 222f;
                AddDetailedRatInformation(detailPanel, rat, 12);
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

        private string BuildRatRosterLabel(RatData rat, bool selected)
        {
            if (rat == null) return string.Empty;
            string markings = rat.phenotype == null || !rat.phenotype.furRevealed ? "Hidden" : rat.phenotype.markingsLabel;
            return (selected ? "✓ " : string.Empty) + ColonyFactory.DisplayName(rat) + "  •  " + SexLabel(rat.sex) + "  •  " + GrowthSystem.StageLabel(rat.stage) + "\n" +
                "Markings " + markings + "  •  Age " + GrowthSystem.FormatAge(rat.ageDays) + "  •  Gen " + rat.generation +
                RosterPregnancySuffix(rat) + "\nHabitat: " + EnclosureSystem.Label(rat.enclosure) +
                "\nState: " + ReproductiveStateLabel(rat);
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
            historyElement.minHeight = 28f;
            historyElement.preferredHeight = 28f + Mathf.Min(
                RatActivitySystem.MaximumHistoryEntries,
                rat.activity == null || rat.activity.history == null ? 0 : rat.activity.history.Count) * 19f;

            if (rat.activity == null || rat.activity.history == null || rat.activity.history.Count == 0)
            {
                AddText(historyPanel, "No recent activity recorded.", 11,
                    new Color(0.70f, 0.78f, 0.74f), TextAnchor.UpperLeft);
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

        private string ReproductiveStateLabel(RatData rat)
        {
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
            if (game == null || game.Save == null || rat == null || rat.sex != RatSex.Female || rat.stage != RatStage.Adult) return null;
            PregnancyData pregnancy = BreedingSystem.FindPendingPregnancy(game.Save, rat.id);
            if (pregnancy == null || pregnancy.status != "pending" || pregnancy.motherId != rat.id) return null;
            return pregnancy;
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

        private void AddPregnancyProgress(RectTransform parent, PregnancyData pregnancy)
        {
            if (parent == null || pregnancy == null || game == null) return;

            long durationMs = pregnancy.dueAt - pregnancy.startedAt;
            if (durationMs <= 0L) return;
            long elapsedMs = game.GameTime - pregnancy.startedAt;
            float rawProgress = Mathf.Clamp01(elapsedMs / (float)durationMs);
            // A pending pregnancy is represented as 1% at its starting instant so
            // the bar visibly runs from 1% through 100% rather than appearing empty.
            float progress = Mathf.Clamp(rawProgress, 0.01f, 1f);

            Text pregnancyText = AddText(parent, string.Empty, 14,
                new Color(1f, 0.73f, 0.34f), TextAnchor.UpperLeft);
            BindLiveText(pregnancyText, () => PregnancyProgressLabel(pregnancy));
            var track = CreateRect("Pregnancy Progress Track", parent);
            var trackElement = track.gameObject.AddComponent<LayoutElement>();
            trackElement.preferredHeight = 14f;
            trackElement.minHeight = 14f;
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.03f, 0.06f, 0.07f, 0.92f);
            trackImage.raycastTarget = false;

            var fill = CreateRect("Pregnancy Progress Fill", track);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(progress, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.95f, 0.55f, 0.28f, 1f);
            fillImage.raycastTarget = false;
        }

        private string RosterPregnancySuffix(RatData rat)
        {
            PregnancyData pregnancy = FindPregnancyForFemale(rat);
            if (pregnancy == null || game == null) return string.Empty;
            long durationMs = pregnancy.dueAt - pregnancy.startedAt;
            if (durationMs <= 0L) return string.Empty;
            float progress = Mathf.Clamp01((game.GameTime - pregnancy.startedAt) / (float)durationMs);
            int percentage = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 1, 100);
            return "\nPregnant " + percentage + "%";
        }

        private string PregnancyProgressLabel(PregnancyData pregnancy)
        {
            if (pregnancy == null || game == null) return string.Empty;
            long durationMs = pregnancy.dueAt - pregnancy.startedAt;
            if (durationMs <= 0L) return "Pregnancy";
            float progress = Mathf.Clamp01((game.GameTime - pregnancy.startedAt) / (float)durationMs);
            int percentage = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 1, 100);
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
            rowClickRelay.Configure(rowButton, () => game.SelectBreedingParent(mateId));

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
            AddText(developerToolsCard, "Spawn adult test rats", 17, Color.white, TextAnchor.UpperLeft);
            AddButton(developerToolsCard, "Solid Black  •  B/B C/C D/D s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SolidBlack));
            AddButton(developerToolsCard, "Solid Brown  •  b/b C/C D/D s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SolidBrown));
            AddButton(developerToolsCard, "Diluted Black  •  B/B C/C d/d s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.DilutedBlack));
            AddButton(developerToolsCard, "Diluted Brown  •  b/b C/C d/d s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.DilutedBrown));
            AddButton(developerToolsCard, "Albino  •  B/B c/c D/D s/s", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.Albino));
            AddButton(developerToolsCard, "Spotted Black  •  B/B C/C D/D S/S", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SpottedBlack));
            AddButton(developerToolsCard, "Spotted Brown  •  b/b C/C D/D S/S", true, () => game.SpawnDeveloperRat(DeveloperRatPreset.SpottedBrown));
            AddButtonTo(developerToolsCard, "Rat Animation Showcase", true, OpenRatAnimationShowcase, new Color(0.18f, 0.34f, 0.42f), 48f);

            AddText(developerToolsCard, "Store testing", 17, Color.white, TextAnchor.UpperLeft);
            AddButton(developerToolsCard, "Restock Rat Market Now", true, game.RestockStoreNow);

            AddText(developerToolsCard, "Growth and pregnancy tests", 17, Color.white, TextAnchor.UpperLeft);
            if (game.SelectedRat != null)
            {
                bool adult = game.SelectedRat.stage == RatStage.Adult;
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
            if (builtInUiFont == null)
            {
                builtInUiFont = Resources.Load<Font>(BundledUiFontResourcePath);
                if (builtInUiFont == null)
                    builtInUiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            text.font = builtInUiFont;
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
            return text;
        }

        private Button AddButton(RectTransform parent, string label, bool enabled, UnityEngine.Events.UnityAction action)
        {
            return AddButtonTo(parent, label, enabled, action, new Color(0.16f, 0.38f, 0.33f), 48f);
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

        private Button AddButtonTo(Transform parent, string label, bool enabled, UnityEngine.Events.UnityAction action, Color color, float height)
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
            clickRelay.Configure(button, enabled ? action : null);
            var layout = objectRoot.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            var labelText = AddTextTo(objectRoot.transform, label, 14, enabled ? Color.white : new Color(0.55f, 0.58f, 0.56f), TextAnchor.MiddleCenter);
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(10f, 4f);
            labelText.rectTransform.offsetMax = new Vector2(-10f, -4f);
            return button;
        }

        private sealed class DirectUiClickRelay : MonoBehaviour
        {
            private Button button;
            private UnityEngine.Events.UnityAction action;

            public void Configure(Button owner, UnityEngine.Events.UnityAction callback)
            {
                if (button != null) button.onClick.RemoveListener(HandleButtonClick);
                button = owner;
                action = callback;
                if (button != null) button.onClick.AddListener(HandleButtonClick);
            }

            private int lastInvokeFrame = -1;

            private void HandleButtonClick()
            {
                InvokeOnce();
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

            public void InvokeFallback()
            {
                InvokeOnce();
            }

            private void InvokeOnce()
            {
                if (!IsFallbackInteractable || lastInvokeFrame == Time.frameCount) return;
                if (lastUiActionFrame == Time.frameCount) return;
                lastInvokeFrame = Time.frameCount;
                lastUiActionFrame = Time.frameCount;
                if (action != null) action.Invoke();
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
