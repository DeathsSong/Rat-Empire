using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RatHabitat
{
    public enum DeveloperRatPreset
    {
        SolidBlack,
        SolidBrown,
        DilutedBlack,
        DilutedBrown,
        Albino,
        SpottedBlack,
        SpottedBrown,
    }

    public enum BreedingParentSlot
    {
        None,
        Mother,
        Father,
    }

    public enum HabitatCameraView
    {
        Overview,
        MaleEnclosure,
        FemaleEnclosure,
        Nursery,
        Breeding,
        Pairing,
    }

    public class GameBootstrap : MonoBehaviour
    {
        private static readonly HabitatCameraView[] HabitatPages =
        {
            HabitatCameraView.MaleEnclosure,
            HabitatCameraView.FemaleEnclosure,
            HabitatCameraView.Breeding,
            HabitatCameraView.Pairing,
        };

        private Camera mainCamera;
        private HabitatBuilder habitat;
        private RatPresenter rats;
        private InteractionManager interaction;
        private VerticalSliceUI ui;
        private float habitatSwipeLockUntil;
        private const float HabitatSwipeDebounceSeconds = 0.24f;
        private bool habitatPageSettling;
        private int habitatSettledPageIndex = 3;
        private float habitatPageSettleUntil;
        private const float HabitatPageSettleSeconds = 0.48f;
        private float saveTimer;
        private string selectedRatId;
        private string selectedObjectId;
        private string parentAId;
        private string parentBId;
        private bool breedingOpen;
        private BreedingParentSlot breedingSelectionSlot;
        private bool startupWarning;
        private bool uiUpdateErrorLogged;
        private string saleEligibilitySignature;
        private string deleteConfirmationRatId;
        private string sellConfirmationRatId;
        private string euthanizeConfirmationRatId;
        private readonly HashSet<string> selectedRatIds = new HashSet<string>();
        private bool multipleSelectionMode;
        private bool groupMoveConfirmationPending;
        private RatEnclosure groupMoveConfirmationTarget;
        private bool groupSellConfirmationPending;
        private bool pairingMoveAllConfirmationPending;
        private bool resetConfirmationPending;
        private Vector3 normalCameraPosition;
        private Quaternion normalCameraRotation;
        private Vector3 normalCameraLookTarget;
        private float normalCameraDistance;
        private float normalCameraOrthographicSize;
        private Vector3 cameraMoveVelocity;
        private float cameraZoomVelocity;
        private float habitatZoomOffset;
        private bool cameraPresentationReady;
        // Habitat navigation starts on one full-size enclosure. Overview is
        // retained in the enum for save/backward compatibility, but is no
        // longer a player-facing combined four-cage presentation.
        // Pairing Habitat is the main landing page. The other full-size
        // enclosures remain available through the left/right carousel.
        private HabitatCameraView cameraView = HabitatCameraView.Pairing;
        private bool cameraFollowSelectedRat;
        private Coroutine pairingCheckRoutine;
        private PairingApproachRuntime pairingApproach;
        private const int MaximumEventLogEntries = 10;
        // Generated profile buttons can be rebuilt while the same pointer-up
        // event is still being processed. Keep the explicit move action from
        // being followed by the newly-created Remove button on that same
        // input sequence.
        private string lastPairingMoveRatId;
        private int lastPairingMoveFrame = -1;
        private const int MaximumEventMessageLength = 64;
        private string statusMessage;

        private enum PairingApproachPhase
        {
            Walking,
            Sniffing,
        }

        private sealed class PairingApproachRuntime
        {
            public string maleId;
            public string femaleId;
            public Vector3 maleTarget;
            public Vector3 femaleTarget;
            public PairingApproachPhase phase;
            public float phaseTimeout;
            public float lastCombinedDistance;
            public float stalledSeconds;
            public Vector3 lastMalePosition;
            public Vector3 lastFemalePosition;
            public float lastMaleDistance;
            public float lastFemaleDistance;
        }

        private sealed class PairingApproachPlan
        {
            public Vector3 maleTarget;
            public Vector3 femaleTarget;
            public List<Vector3> maleWaypoints = new List<Vector3>();
            public List<Vector3> femaleWaypoints = new List<Vector3>();
            public float score = float.MinValue;
        }

        private const float CameraFocusSmoothTime = 0.24f;
        // Zoom input is intentionally subtle. The camera still follows the
        // same target, but takes a little longer to settle so a wheel notch,
        // pinch update, or button press never produces a visible jump.
        private const float CameraZoomSmoothTime = 0.36f;
        private const float InspectionOrthographicMultiplier = 0.169344f;
        private const float PinkieInspectionOrthographicMultiplier = InspectionOrthographicMultiplier * 0.2f;
        // Lower the selected-rat focus target slightly so the live rat sits
        // higher in the transparent profile opening. This moves only the
        // existing habitat camera; it does not move the rat or the profile UI.
        private const float SelectedRatCameraVerticalOffset = -1.00f;
        // Pinkies get a slightly higher camera position in the live profile
        // window because their smaller body sits lower in the habitat view.
        private const float PinkieSelectedRatCameraVerticalOffset = 0.10f;
        private const float TopNavigationReservedReferenceHeight = 150f;
        private const int RatAnimationShowcasePreviewLayer = 30;
        private const float HabitatZoomInputStep = 0.42f;
        private const float HabitatZoomButtonStep = HabitatZoomInputStep;
        private const float HabitatZoomOffsetLimit = 8f;
        private const float OverviewMinimumZoom = 7.5f;
        private const float EnclosureMinimumZoom = 5.8f;
        private const float SelectedRatMinimumZoom = 1.8f;
        private const float PinkieSelectedRatMinimumZoom = 0.36f;
        // The selected-pinkie inspection view is tightly zoomed. The normal
        // selected-rat target sits above the pinkie, which places the pinkie
        // below the transparent portrait opening and behind the profile panel.
        // Move the selected pinkie modestly higher in the live profile opening
        // so the information panel does not hide the lower part of the nest.
        // This is applied only to a pinkie selected in Pairing Habitat; it
        // does not move the nest, pinkie, or any other habitat view.
        private const float PairingPinkieCameraLookTargetDownwardOffset = -0.45f;
        private const float PairingApproachTimeoutSeconds = 18f;
        private const float PairingInteractionSeconds = 2.25f;
        private const float PairingMeetSeparation = 0.92f;
        private const int MaximumPairingRouteRetries = 3;
        private const float PairingRoutePositionEpsilon = 0.006f;
        private string pairingRouteRetryPairKey;
        private int pairingRouteRetryCount;
        private Vector3 pairingFailedMaleTarget;
        private Vector3 pairingFailedFemaleTarget;
        // Live messages are transient, but their expiry is measured in the
        // simulation clock. The duration is sized to be roughly four real
        // seconds at the current speed, so 1x/2x/3x remain equally readable.
        private const int LiveEventDurationRealSeconds = 4;

        public ColonySaveData Save { get; private set; }
        private string liveEventMessage;
        private long liveEventExpiresAt;
        public string StatusMessage
        {
            get { return statusMessage; }
            private set
            {
                statusMessage = value;
                RecordStatusEvent(value);
            }
        }
        public string LiveEventMessage
        {
            get
            {
                if (string.IsNullOrEmpty(liveEventMessage)) return string.Empty;
                if (GameTime >= liveEventExpiresAt)
                {
                    liveEventMessage = null;
                    liveEventExpiresAt = 0L;
                    return string.Empty;
                }
                return liveEventMessage;
            }
        }
        public int RecentEventCount
        {
            get
            {
                if (Save == null) return 0;
                Save.EnsureLists();
                return Save.eventLog.Count;
            }
        }
        public List<ColonyEventData> RecentEvents
        {
            get
            {
                if (Save == null) return new List<ColonyEventData>();
                Save.EnsureLists();
                return Save.eventLog;
            }
        }
        public string EventLogSignature
        {
            get
            {
                if (Save == null || Save.eventLog == null || Save.eventLog.Count == 0) return string.Empty;
                ColonyEventData latest = Save.eventLog[0];
                return Save.eventLog.Count + ":" + latest.gameTimeMs + ":" + (latest.message ?? string.Empty);
            }
        }
        public bool BreedingOpen { get { return breedingOpen; } }
        public BreedingParentSlot BreedingSelectionSlot { get { return breedingSelectionSlot; } }
        public bool DeleteConfirmationPending { get { return !string.IsNullOrEmpty(deleteConfirmationRatId); } }
        public bool SellConfirmationPending { get { return !string.IsNullOrEmpty(sellConfirmationRatId); } }
        public bool IsSellConfirmationFor(string ratId)
        {
            return !string.IsNullOrEmpty(ratId) && ratId == sellConfirmationRatId;
        }
        public bool EuthanizeConfirmationPending { get { return !string.IsNullOrEmpty(euthanizeConfirmationRatId); } }
        public bool MultipleSelectionMode { get { return multipleSelectionMode; } }
        public int SelectedGroupCount { get { return selectedRatIds.Count; } }
        public bool GroupMoveConfirmationPending { get { return groupMoveConfirmationPending; } }
        public bool GroupSellConfirmationPending { get { return groupSellConfirmationPending; } }
        public RatEnclosure GroupMoveConfirmationTarget { get { return groupMoveConfirmationTarget; } }
        public bool PairingMoveAllConfirmationPending { get { return pairingMoveAllConfirmationPending; } }
        public int PairingHabitatCapacity { get { return GameConfig.BasePairingHabitatCapacity; } }
        public int PairingHabitatCount { get { return CountPairingHabitatRats(); } }
        public float SimulationSpeed { get { return Save == null || Save.clock == null ? 1f : GrowthSystem.NormalizeSpeed(Save.clock.speed); } }
        public string SimulationSpeedLabel { get { return ((int)SimulationSpeed) + "×"; } }
        public string MovementDiagnostics { get { return RatHabitatBehavior.GetMovementDiagnosticReadout(); } }
        public bool ResetConfirmationPending { get { return resetConfirmationPending; } }
        public bool WelcomePopupPending { get { return Save != null && Save.welcomePopupPending; } }
        public bool KeepScreenAwakeEnabled
        {
            get { return BrowserWakeLockSystem.IsEnabled(Save); }
        }
        public string KeepScreenAwakeStatusMessage
        {
            get { return BrowserWakeLockSystem.StatusMessage(Save); }
        }
        public HabitatCameraView CameraView { get { return cameraView; } }
        public int HabitatPageCount { get { return HabitatPages.Length; } }
        public int HabitatPageIndex
        {
            get
            {
                if (habitatPageSettling)
                    return Mathf.Clamp(habitatSettledPageIndex, 0, HabitatPages.Length - 1);
                int index = Array.IndexOf(HabitatPages, NormalizeHabitatView(cameraView));
                return index < 0 ? 0 : index;
            }
        }
        public string HabitatPageLabel
        {
            get
            {
                HabitatCameraView visibleView = HabitatPages[Mathf.Clamp(HabitatPageIndex, 0, HabitatPages.Length - 1)];
                return CameraViewLabel(visibleView) + "  •  " +
                    (HabitatPageIndex + 1) + "/" + HabitatPageCount;
            }
        }
        public long GameTime { get { return Save == null || Save.clock == null ? GameConfig.StartGameTimeMs : Save.clock.gameTimeMs; } }
        public RatData SelectedRat { get { return BreedingSystem.FindRat(Save, selectedRatId); } }
        public HabitatObjectData SelectedObject { get { return FindObject(selectedObjectId); } }
        public RatData ParentA { get { return BreedingSystem.FindRat(Save, parentAId); } }
        public RatData ParentB
        {
            get
            {
                // Treat stale duplicate state as no selected mate. This keeps
                // the two UI slots distinct and makes Confirm Breeding fail
                // safely until a real potential mate is selected.
                if (!string.IsNullOrEmpty(parentAId) && parentAId == parentBId) return null;
                return BreedingSystem.FindRat(Save, parentBId);
            }
        }
        public RatData Mother { get { return BreedingSystem.FindRat(Save, parentAId); } }
        public RatData Father
        {
            get
            {
                if (!string.IsNullOrEmpty(parentAId) && parentAId == parentBId) return null;
                return BreedingSystem.FindRat(Save, parentBId);
            }
        }
        public RatVisualFactory RatVisualFactory { get { return rats == null ? null : rats.GetVisualFactory(); } }

        public DedicatedBreedingSessionData ActiveDedicatedBreedingSession
        {
            get
            {
                if (Save == null || Save.breedingSessions == null) return null;
                foreach (var session in Save.breedingSessions)
                    if (session != null && session.status == "active") return session;
                return null;
            }
        }

        public bool TryGetLiveRatRoot(string ratId, out Transform root)
        {
            root = null;
            return rats != null && rats.TryGetRatRoot(ratId, out root);
        }

        public PregnancyData PendingPregnancy
        {
            get
            {
                if (Save == null) return null;
                foreach (var item in Save.pregnancies)
                {
                    if (item != null && item.status == "pending") return item;
                }
                return null;
            }
        }

        public List<RatData> EligibleMates
        {
            get { return GetEligibleBreedingRats(); }
        }

        public bool IsActiveBreedingSelection(string ratId)
        {
            if (string.IsNullOrEmpty(ratId)) return false;
            return ratId == parentAId || ratId == parentBId;
        }

        private List<RatData> GetEligibleBreedingRats()
        {
            var eligible = new List<RatData>();
            if (!breedingOpen || Save == null || breedingSelectionSlot == BreedingParentSlot.None) return eligible;

            string otherParentId = breedingSelectionSlot == BreedingParentSlot.Mother ? parentBId : parentAId;
            foreach (var candidate in Save.rats)
            {
                if (candidate == null || candidate.id == otherParentId) continue;
                if (breedingSelectionSlot == BreedingParentSlot.Mother && candidate.sex != RatSex.Female) continue;
                if (breedingSelectionSlot == BreedingParentSlot.Father && candidate.sex != RatSex.Male) continue;

                string reason;
                if (BreedingSystem.IsBreedEligible(Save, candidate, GameTime, out reason)) eligible.Add(candidate);
            }
            return eligible;
        }

        public string ClockLabel
        {
            get { return FormatSimulationTimestamp(GameTime); }
        }

        public string FormatEventLogEntry(ColonyEventData entry)
        {
            if (entry == null) return string.Empty;
            return FormatSimulationTimestamp(entry.gameTimeMs) + "  " + (entry.message ?? string.Empty);
        }

        public string FormatSimulationTimestamp(long gameTimeMs)
        {
            long safeTime = Math.Max(0L, gameTimeMs);
            long day = (safeTime / GameConfig.GameDayMs) + 1L;
            long dayTime = safeTime % GameConfig.GameDayMs;
            int hours = (int)(dayTime / (60L * 60L * 1000L));
            int minutes = (int)((dayTime / (60L * 1000L)) % 60L);
            return "Day " + day + "  •  " + hours.ToString("00") + ":" + minutes.ToString("00");
        }

        public string FormatRatActivityEntry(RatActivityEntryData entry)
        {
            if (entry == null) return string.Empty;
            string message = string.IsNullOrEmpty(entry.message) ? entry.activityLabel : entry.message;
            return FormatSimulationTimestamp(entry.gameTimeMs) + "  •  " + (message ?? string.Empty);
        }

        /// <summary>
        /// Returns the activity for this stable RatData record. Biological
        /// states take precedence over ambient movement, while the live
        /// behavior supplies readable activities such as Eating, Sleeping,
        /// and Exploring when no biological state is active.
        /// </summary>
        public string CurrentRatActivityLabel(RatData rat)
        {
            if (rat == null) return "Unknown";
            string key = RatActivitySystem.CurrentKey(Save, rat, GameTime);
            if (IsAuthoritativeActivityKey(key))
                return RatActivitySystem.CurrentLabel(Save, rat, GameTime);
            RatActivityData persistedActivity = RatActivitySystem.Ensure(rat, GameTime);
            if (key == "movement" && persistedActivity.currentActivityAt == GameTime)
                return persistedActivity.currentActivityLabel;
            RatHabitatBehavior behavior;
            if (rats != null && rats.TryGetRatBehavior(rat.id, out behavior) && behavior != null)
                return behavior.ActivityLabel;
            return RatActivitySystem.CurrentLabel(Save, rat, GameTime);
        }

        private static bool IsAuthoritativeActivityKey(string key)
        {
            return key == "sold" || key == "euthanized" || key == "deceased" ||
                key == "breeding" || key == "pregnant" || key == "nursing" || key == "recovery";
        }

        private bool RefreshRatActivities()
        {
            if (Save == null) return false;
            bool changed = RatActivitySystem.RefreshAuthoritativeActivities(Save, GameTime);
            foreach (var rat in Save.rats)
            {
                if (rat == null || rat.removalDisposition != RatRemovalDisposition.None) continue;
                string authoritativeKey = RatActivitySystem.CurrentKey(Save, rat, GameTime);
                if (IsAuthoritativeActivityKey(authoritativeKey)) continue;

                RatHabitatBehavior behavior;
                if (rats != null && rats.TryGetRatBehavior(rat.id, out behavior) && behavior != null)
                    changed |= RatActivitySystem.SetCurrent(Save, rat, behavior.ActivityKey, behavior.ActivityLabel, GameTime);
                else
                    changed |= RatActivitySystem.SetCurrent(Save, rat, "exploring", "Exploring", GameTime);
            }
            return changed;
        }

        private bool RecordStatusEvent(string value)
        {
            if (Save == null || string.IsNullOrWhiteSpace(value)) return false;
            string compact = CompactEventMessage(value);
            if (!EventLogPolicy.IsAllowed(compact)) return false;

            Save.EnsureLists();
            Save.eventLog.Insert(0, new ColonyEventData
            {
                gameTimeMs = GameTime,
                message = compact,
            });
            while (Save.eventLog.Count > MaximumEventLogEntries)
                Save.eventLog.RemoveAt(Save.eventLog.Count - 1);

            liveEventMessage = compact;
            liveEventExpiresAt = GameTime + LiveEventDurationGameMs();
            return true;
        }

        private long LiveEventDurationGameMs()
        {
            double simulatedMillisecondsPerRealSecond =
                GrowthSystem.SimulationMillisecondsPerRealSecond(SimulationSpeed);
            long duration = (long)Math.Round(simulatedMillisecondsPerRealSecond * LiveEventDurationRealSeconds);
            return Math.Max(60L * 1000L, duration);
        }

        private static string CompactEventMessage(string value)
        {
            string compact = value.Replace("\r", " ").Replace("\n", " ").Trim();
            while (compact.Contains("  ")) compact = compact.Replace("  ", " ");
            return compact.Length > MaximumEventMessageLength
                ? compact.Substring(0, MaximumEventMessageLength - 3) + "..."
                : compact;
        }

        public string UiSignature
        {
            get
            {
                return BuildUiSignature(true);
            }
        }

        // The profile uses this signature to distinguish structural changes
        // from ordinary simulation ticks. The full UiSignature continues to
        // include the clock for screens that need a normal refresh, while a
        // live profile can update its labels/activity rows in place.
        public string UiStructureSignature
        {
            get
            {
                return BuildUiSignature(false);
            }
        }

        private string BuildUiSignature(bool includeClock)
        {
                var parts = new List<string>
                {
                    selectedRatId ?? string.Empty,
                    selectedObjectId ?? string.Empty,
                    breedingOpen ? "breeding" : "habitat",
                    parentAId ?? string.Empty,
                    parentBId ?? string.Empty,
                    breedingSelectionSlot.ToString(),
                    Save == null ? "0" : Save.rats.Count.ToString(),
                    Save == null ? "0" : Save.litters.Count.ToString(),
                    Save == null ? "0" : Save.pregnancies.Count.ToString(),
                    SimulationSpeedLabel,
                    multipleSelectionMode ? "multi" : "single",
                    selectedRatIds.Count.ToString(),
                    sellConfirmationRatId ?? string.Empty,
                    EuthanizeConfirmationPending ? "euth" : string.Empty,
                    PairingMoveAllConfirmationPending ? "pairing-confirm" : string.Empty,
                    ActiveDedicatedBreedingSession == null ? string.Empty :
                        ActiveDedicatedBreedingSession.id + ":" + ActiveDedicatedBreedingSession.status + ":" + ActiveDedicatedBreedingSession.endsAt,
                };
                if (includeClock)
                {
                    parts.Insert(6, Save == null || Save.clock == null
                        ? "0"
                        : (Save.clock.gameTimeMs / 1000L).ToString());
                }
                if (Save != null)
                {
                    foreach (var rat in Save.rats)
                    {
                        if (rat != null) parts.Add(rat.id + ":" + rat.stage + ":" + rat.developerGrowthOverride + ":" +
                            (rat.pregnancyId ?? string.Empty) + ":" + rat.enclosure + ":" + rat.nursing + ":" + rat.reproductiveState);
                    }
                    foreach (var pregnancy in Save.pregnancies)
                    {
                        if (pregnancy != null) parts.Add(pregnancy.id + ":" + pregnancy.status + ":" + pregnancy.startedAt + ":" + pregnancy.dueAt + ":" + (pregnancy.litterId ?? string.Empty));
                    }
                    foreach (var item in Save.habitatObjects)
                    {
                        if (item != null) parts.Add(item.id + ":" + item.condition.ToString("0"));
                    }
                }
                return string.Join("|", parts.ToArray());
        }

        private void Awake()
        {
            Debug.Log("[Rat Habitat] GameBootstrap.Awake started.");
            GrowthSystem.SetSimulationPaused(false);
            Application.targetFrameRate = 60;
            try
            {
                Screen.orientation = Application.isMobilePlatform ? ScreenOrientation.Portrait : ScreenOrientation.AutoRotation;
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
            }

            try
            {
                EnsureEventSystem();
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
            }

            try
            {
                Save = SaveSystem.LoadOrCreate();
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
                Save = ColonyFactory.CreateNew(GameConfig.NowMs());
            }

            if (Save == null)
            {
                startupWarning = true;
                Save = ColonyFactory.CreateNew(GameConfig.NowMs());
            }
            try
            {
                GrowthSystem.AdvanceClock(Save, GameConfig.NowMs());
                GrowthSystem.RefreshRatStages(Save);
                BreedingSystem.RefreshReproductiveStates(Save, GameTime);
                    StoreSystem.AdvanceRestock(Save, GameTime);
                    EnclosureSystem.ClearBreedingPair();
                    RestoreDedicatedBreedingPair();
                    EnclosureSystem.RecalculateAssignments(Save);
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
                // Keep the save that was successfully loaded. A post-load
                // presentation/reconciliation issue must never replace a
                // valid browser colony with default starter rats.
            }
            StatusMessage = !string.IsNullOrEmpty(SaveSystem.LastLoadMessage)
                ? SaveSystem.LastLoadMessage
                : (startupWarning
                    ? "Startup warning — see the Unity Console."
                    : "3D scene ready — " + Save.rats.Count + " rats loaded.");
            BrowserWakeLockSystem.Initialize(Save);

            try
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    var cameraObject = new GameObject("Main Camera");
                    cameraObject.tag = "MainCamera";
                    mainCamera = cameraObject.AddComponent<Camera>();
                    cameraObject.AddComponent<SceneVisibilityGuard>();
                }
                ConfigureCamera();
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
                StatusMessage = "Camera startup warning — see the Unity Console.";
            }

            try
            {
                var worldRoot = GameObject.Find("Habitat Presentation");
                if (worldRoot == null) worldRoot = new GameObject("Habitat Presentation");
                habitat = worldRoot.GetComponent<HabitatBuilder>();
                if (habitat == null) habitat = worldRoot.AddComponent<HabitatBuilder>();
                rats = worldRoot.GetComponent<RatPresenter>();
                if (rats == null) rats = worldRoot.AddComponent<RatPresenter>();
                // Keep the model assignment on the scene-owned bootstrap
                // object, while the presenter and selectable rat roots remain
                // on the existing Habitat Presentation object.
                var sceneVisualFactory = GetComponent<RatVisualFactory>();
                if (sceneVisualFactory == null) sceneVisualFactory = worldRoot.GetComponent<RatVisualFactory>();
                if (sceneVisualFactory == null) sceneVisualFactory = worldRoot.AddComponent<RatVisualFactory>();
                rats.ConfigureVisualFactory(sceneVisualFactory);
                rats.ConfigureHabitat(habitat);

                try
                {
                    habitat.Build(Save);
                }
                catch (Exception exception)
                {
                    startupWarning = true;
                    Debug.LogException(exception);
                    StatusMessage = "Habitat startup warning — see the Unity Console.";
                }

                try
                {
                    rats.Render(Save, habitat.NestPosition);
                }
                catch (Exception exception)
                {
                    startupWarning = true;
                    Debug.LogException(exception);
                    StatusMessage = "Rat presentation startup warning — see the Unity Console.";
                }
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
                StatusMessage = "3D world startup warning — see the Unity Console.";
            }

            try
            {
                interaction = gameObject.GetComponent<InteractionManager>();
                if (interaction == null) interaction = gameObject.AddComponent<InteractionManager>();
                interaction.enabled = true;
                interaction.Configure(mainCamera, SelectEntity, IsSelectionPanelVisible, CloseBreeding,
                    ReportInputDiagnostic, AdjustHabitatZoom, FocusHabitatAtWorldPoint,
                    TryNavigateHabitatSwipe, IsWorldInputBlockedByModal);
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
                if (interaction != null) interaction.SetStartupError(exception.Message);
                StatusMessage = "Input startup warning — see the Unity Console.";
            }

            try
            {
                ui = gameObject.GetComponent<VerticalSliceUI>();
                if (ui == null) ui = gameObject.AddComponent<VerticalSliceUI>();
                ui.Initialize(this);
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
                StatusMessage = "UI startup warning — see the Unity Console.";
            }

            try
            {
                SaveSystem.Save(Save);
            }
            catch (Exception exception)
            {
                startupWarning = true;
                Debug.LogException(exception);
            }
            EnsurePairingCheckScheduled();
            pairingCheckRoutine = StartCoroutine(PairingCheckLoop());
            Debug.Log("[Rat Habitat] GameBootstrap.Awake completed. World objects and UI initialization were attempted independently.");
        }

        private void EnsurePairingCheckScheduled()
        {
            if (Save == null) return;
            Save.EnsureLists();
            if (Save.pairingNextCheckGameTime <= 0L)
            {
                Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;
            }
        }

        private void RestoreDedicatedBreedingPair()
        {
            if (Save == null || Save.breedingSessions == null) return;
            foreach (var session in Save.breedingSessions)
            {
                if (session == null || session.status != "active") continue;
                EnclosureSystem.SetBreedingPair(session.motherId, session.fatherId);
                return;
            }
        }

        private bool IsWorldInputBlockedByModal()
        {
            return ui != null && ui.IsModalOverlayOpen;
        }

        private IEnumerator PairingCheckLoop()
        {
            while (enabled)
            {
                if (Save == null)
                {
                    yield return new WaitForSecondsRealtime(1f);
                    continue;
                }

                if (ui != null && ui.IsWelcomeOpen)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                    continue;
                }

                // A visual approach owns the next pairing attempt until it
                // resolves or times out. This prevents a fast simulation mode
                // from starting a second pair while the first pair is still
                // walking toward one another.
                if (pairingApproach != null)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                    continue;
                }

                EnsurePairingCheckScheduled();
                long remainingGameMs = Save.pairingNextCheckGameTime - GameTime;
                if (remainingGameMs > 0L)
                {
                    double simulatedMillisecondsPerRealSecond =
                        GrowthSystem.SimulationMillisecondsPerRealSecond(SimulationSpeed);
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.05f,
                        (float)(remainingGameMs / simulatedMillisecondsPerRealSecond)));
                    continue;
                }

                // Advance the persisted deadline before evaluating. A stalled
                // or backgrounded app therefore performs one catch-up pass,
                // then resumes the normal 30-second cadence.
                Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;
                RatData male;
                RatData female;
                bool approachStarted = false;
                if (PairingHabitatSystem.TryChoosePair(Save, GameTime, out male, out female))
                {
                    approachStarted = BeginPairingApproach(male, female);
                }
                bool enclosureChanged = EnclosureSystem.RecalculateAssignments(Save);
                if (enclosureChanged && !approachStarted)
                {
                    RefreshWorldAndUi(true);
                }
                SaveSystem.Save(Save);
            }
        }

        private bool BeginPairingApproach(RatData male, RatData female)
        {
            if (pairingApproach != null || Save == null || male == null || female == null) return false;
            if (male.sex != RatSex.Male || female.sex != RatSex.Female ||
                male.enclosure != RatEnclosure.Pairing || female.enclosure != RatEnclosure.Pairing)
            {
                return false;
            }

            string reason;
            if (!BreedingSystem.IsBreedEligible(Save, male, GameTime, out reason) ||
                !BreedingSystem.IsBreedEligible(Save, female, GameTime, out reason)) return false;

            Transform maleRoot;
            Transform femaleRoot;
            RatHabitatBehavior maleBehavior;
            RatHabitatBehavior femaleBehavior;
            if (!TryGetPairingParticipant(male.id, out maleRoot, out maleBehavior) ||
                !TryGetPairingParticipant(female.id, out femaleRoot, out femaleBehavior)) return false;

            // Older saves or a previously failed direct approach can leave an
            // adult inside the nest footprint. Recover it to open floor before
            // planning a new courtship route; never ask the route solver to
            // walk from inside the obstacle.
            if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, maleRoot.position, 0.12f))
                maleBehavior.RecoverAtSafeOpenFloor();
            if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, femaleRoot.position, 0.12f))
                femaleBehavior.RecoverAtSafeOpenFloor();
            if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, maleRoot.position, 0.12f) ||
                EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, femaleRoot.position, 0.12f)) return false;

            string pairKey = male.id + "|" + female.id;
            Vector3 excludedMaleTarget = pairingRouteRetryPairKey == pairKey
                ? pairingFailedMaleTarget : Vector3.zero;
            Vector3 excludedFemaleTarget = pairingRouteRetryPairKey == pairKey
                ? pairingFailedFemaleTarget : Vector3.zero;
            PairingApproachPlan plan;
            if (!TryBuildPairingApproachPlan(
                maleRoot.position,
                femaleRoot.position,
                male.id,
                female.id,
                excludedMaleTarget,
                excludedFemaleTarget,
                out plan))
            {
                CancelPairingApproach("no reachable floor route around the nest");
                return false;
            }

            Vector3 maleTarget = plan.maleTarget;
            Vector3 femaleTarget = plan.femaleTarget;

            // RatHabitatBehavior now consumes the centralized behavior delta
            // from GrowthSystem. Do not multiply movement here as well or a
            // speed change would be applied twice to courtship movement.
            const float pairingRouteMultiplier = 1f;
            if (!maleBehavior.BeginPairingApproach(
                maleTarget, femaleTarget, pairingRouteMultiplier, plan.maleWaypoints)) return false;
            if (!femaleBehavior.BeginPairingApproach(
                femaleTarget, maleTarget, pairingRouteMultiplier, plan.femaleWaypoints))
            {
                maleBehavior.CancelPairingApproach();
                return false;
            }

            pairingApproach = new PairingApproachRuntime
            {
                maleId = male.id,
                femaleId = female.id,
                maleTarget = maleTarget,
                femaleTarget = femaleTarget,
                phase = PairingApproachPhase.Walking,
                phaseTimeout = PairingApproachTimeoutSeconds,
                lastCombinedDistance = Vector3.Distance(maleRoot.position, maleTarget) +
                    Vector3.Distance(femaleRoot.position, femaleTarget),
                stalledSeconds = 0f,
                lastMalePosition = maleRoot.position,
                lastFemalePosition = femaleRoot.position,
                lastMaleDistance = Vector3.Distance(maleRoot.position, maleTarget),
                lastFemaleDistance = Vector3.Distance(femaleRoot.position, femaleTarget),
            };
            pairingRouteRetryPairKey = null;
            pairingRouteRetryCount = 0;
            pairingFailedMaleTarget = Vector3.zero;
            pairingFailedFemaleTarget = Vector3.zero;
            // Do not mark the rats as actively breeding until they have
            // reached the face-to-face interaction point. The approach is a
            // route, not a breeding attempt, so activity timestamps and
            // cancellation history reflect the real interaction start.
            StatusMessage = string.Empty;
            if (ui != null) ui.Refresh(false);
            return true;
        }

        private void UpdatePairingApproach()
        {
            if (pairingApproach == null || Save == null) return;

            RatData male = BreedingSystem.FindRat(Save, pairingApproach.maleId);
            RatData female = BreedingSystem.FindRat(Save, pairingApproach.femaleId);
            Transform maleRoot;
            Transform femaleRoot;
            RatHabitatBehavior maleBehavior;
            RatHabitatBehavior femaleBehavior;
            if (male == null || female == null ||
                !TryGetPairingParticipant(male.id, out maleRoot, out maleBehavior) ||
                !TryGetPairingParticipant(female.id, out femaleRoot, out femaleBehavior))
            {
                CancelPairingApproach("one of the rats was no longer available");
                return;
            }

            if (male.enclosure != RatEnclosure.Pairing || female.enclosure != RatEnclosure.Pairing)
            {
                CancelPairingApproach("one of the rats left the Pairing Habitat");
                return;
            }

            // Courtship timeout/stall timers use the same centralized
            // behavior delta as the two rat movement controllers. This keeps
            // a 2x/3x approach from moving quickly while waiting on a 1x
            // timeout clock.
            float deltaTime = GrowthSystem.SimulationBehaviorDeltaSeconds(Time.unscaledDeltaTime);
            pairingApproach.phaseTimeout -= deltaTime;
            if (pairingApproach.phase == PairingApproachPhase.Walking)
            {
                string reason = string.Empty;
                if (!BreedingSystem.IsBreedEligible(Save, male, GameTime, out reason))
                {
                    CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                    return;
                }
                if (!BreedingSystem.IsBreedEligible(Save, female, GameTime, out reason))
                {
                    CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                    return;
                }

                if (maleBehavior.PairingApproachAtTarget && femaleBehavior.PairingApproachAtTarget)
                {
                    // Revalidate at the exact transition into the physical
                    // interaction. A fertile window may have ended while the
                    // rats were walking; that attempt must be cancelled
                    // before sniffing/breeding begins.
                    if (!BreedingSystem.IsBreedEligible(Save, female, GameTime, out reason))
                    {
                        CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                        return;
                    }
                    if (!BreedingSystem.IsBreedEligible(Save, male, GameTime, out reason))
                    {
                        CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                        return;
                    }

                    pairingApproach.phase = PairingApproachPhase.Sniffing;
                    pairingApproach.phaseTimeout = PairingInteractionSeconds + 1.5f;
                    maleBehavior.BeginPairingInteraction(femaleRoot.position, PairingInteractionSeconds);
                    femaleBehavior.BeginPairingInteraction(maleRoot.position, PairingInteractionSeconds);
                    RatActivitySystem.SetCurrent(Save, male, "breeding", "Breeding", GameTime,
                        "Breeding interaction started");
                    RatActivitySystem.SetCurrent(Save, female, "breeding", "Breeding", GameTime,
                        "Breeding interaction started");
                    // Pairing Habitat breeding is intentionally player-silent.
                    // Keep the state in each rat's activity history, but do
                    // not promote the ordinary interaction to the global
                    // top-right event log/live banner.
                    SaveSystem.Save(Save);
                    return;
                }

                float combinedDistance = Vector3.Distance(maleRoot.position, pairingApproach.maleTarget) +
                    Vector3.Distance(femaleRoot.position, pairingApproach.femaleTarget);
                float maleDistance = Vector3.Distance(maleRoot.position, pairingApproach.maleTarget);
                float femaleDistance = Vector3.Distance(femaleRoot.position, pairingApproach.femaleTarget);
                bool maleMoved = Vector3.Distance(maleRoot.position, pairingApproach.lastMalePosition) > PairingRoutePositionEpsilon;
                bool femaleMoved = Vector3.Distance(femaleRoot.position, pairingApproach.lastFemalePosition) > PairingRoutePositionEpsilon;
                bool distanceImproved = maleDistance < pairingApproach.lastMaleDistance - 0.003f ||
                    femaleDistance < pairingApproach.lastFemaleDistance - 0.003f;
                // A valid obstacle route can temporarily move farther from
                // the final meeting point while it rounds a nest corner. Only
                // treat it as stalled when neither root is moving, or when a
                // root is moving without any route progress at all.
                if ((!maleMoved && !femaleMoved) || (!distanceImproved && combinedDistance >= pairingApproach.lastCombinedDistance - 0.003f &&
                    !maleMoved && !femaleMoved))
                    pairingApproach.stalledSeconds += deltaTime;
                else
                    pairingApproach.stalledSeconds = 0f;
                pairingApproach.lastCombinedDistance = combinedDistance;
                pairingApproach.lastMalePosition = maleRoot.position;
                pairingApproach.lastFemalePosition = femaleRoot.position;
                pairingApproach.lastMaleDistance = maleDistance;
                pairingApproach.lastFemaleDistance = femaleDistance;
                if (pairingApproach.stalledSeconds >= 2.5f)
                {
                    CancelPairingApproach("the approach was blocked");
                    return;
                }

                if (pairingApproach.phaseTimeout <= 0f)
                {
                    CancelPairingApproach("the approach timed out");
                }
                return;
            }

            if (maleBehavior.PairingInteractionComplete && femaleBehavior.PairingInteractionComplete)
            {
                ResolvePairingApproach(male, female, maleBehavior, femaleBehavior);
            }
            else if (pairingApproach.phaseTimeout <= 0f)
            {
                CancelPairingApproach("the interaction timed out");
            }
        }

        private void ResolvePairingApproach(
            RatData male,
            RatData female,
            RatHabitatBehavior maleBehavior,
            RatHabitatBehavior femaleBehavior)
        {
            bool conceptionSucceeded;
            string reason;
            PregnancyData createdPregnancy;
            bool resolved = PairingHabitatSystem.ResolvePair(
                Save,
                female,
                male,
                GameTime,
                GameConfig.PairingPregnancyChance,
                true,
                out conceptionSucceeded,
                out reason,
                out createdPregnancy);

            maleBehavior.FinishPairingInteraction();
            femaleBehavior.FinishPairingInteraction();
            pairingApproach = null;
            Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;

            if (!resolved || !conceptionSucceeded)
            {
                // Failed conception and cancelled attempts are intentionally
                // silent. The rats simply return to normal wandering.
                RatActivitySystem.SetCurrent(Save, male, "exploring", "Exploring", GameTime);
                RatActivitySystem.SetCurrent(Save, female, "exploring", "Exploring", GameTime);
                StatusMessage = string.Empty;
            }
            else
            {
                // Pregnancy is a permitted colony-wide event; the ordinary
                // Pairing Habitat breeding approach/interaction remains
                // silent. Resolve the announcement from the saved pregnancy
                // record so the mother ID is always authoritative.
                AnnounceCreatedPregnancy(createdPregnancy);
            }

            EnclosureSystem.RecalculateAssignments(Save);
            RefreshWorldAndUi(resolved && conceptionSucceeded);
            SaveSystem.Save(Save);
        }

        private bool AnnounceCreatedPregnancy(PregnancyData pregnancy)
        {
            if (Save == null || pregnancy == null || pregnancy.status != "pending" ||
                pregnancy.pregnancyAnnouncementLogged) return false;

            // The pregnancy record is the source of truth. Never derive this
            // announcement from the selected rat, breeding slot, or father.
            RatData mother = BreedingSystem.FindRat(Save, pregnancy.motherId);
            if (mother == null || mother.sex != RatSex.Female) return false;

            // The state must be persisted before the player-facing event is
            // emitted. A second save persists the one-time announcement guard
            // together with the approved global event entry.
            if (!SaveSystem.Save(Save)) return false;
            pregnancy.pregnancyAnnouncementLogged = true;
            StatusMessage = ColonyFactory.DisplayName(mother) + " is pregnant!";
            SaveSystem.Save(Save);
            return true;
        }

        private void CancelPairingApproach(string reason)
        {
            if (pairingApproach == null) return;

            PairingApproachRuntime failedApproach = pairingApproach;
            string failureText = string.IsNullOrEmpty(reason) ? "route blocked" : reason;
            string failureLower = failureText.ToLowerInvariant();
            bool fertileWindowEnded = failureLower.Contains("fertile window") &&
                (failureLower.Contains("ended") || failureLower.Contains("outside"));
            bool routeFailure = failureLower.Contains("blocked") ||
                failureLower.Contains("timed out") || failureLower.Contains("route");

            RatData cancelledMale = BreedingSystem.FindRat(Save, failedApproach.maleId);
            RatData cancelledFemale = BreedingSystem.FindRat(Save, failedApproach.femaleId);
            if (fertileWindowEnded)
            {
                const string cancellationMessage = "Breeding cancelled — fertile window ended";
                RatActivitySystem.Record(Save, cancelledFemale, "breeding-cancelled", "Breeding cancelled",
                    GameTime, cancellationMessage);
                RatActivitySystem.Record(Save, cancelledMale, "breeding-cancelled", "Breeding cancelled",
                    GameTime, cancellationMessage);
            }
            if (cancelledFemale != null && RatActivitySystem.CurrentKey(Save, cancelledFemale, GameTime) == "breeding")
                RatActivitySystem.SetCurrent(Save, cancelledFemale, "exploring", "Exploring", GameTime);
            if (cancelledMale != null && RatActivitySystem.CurrentKey(Save, cancelledMale, GameTime) == "breeding")
                RatActivitySystem.SetCurrent(Save, cancelledMale, "exploring", "Exploring", GameTime);

            RatHabitatBehavior maleBehavior;
            RatHabitatBehavior femaleBehavior;
            if (rats != null)
            {
                if (rats.TryGetRatBehavior(pairingApproach.maleId, out maleBehavior)) maleBehavior.CancelPairingApproach();
                if (rats.TryGetRatBehavior(pairingApproach.femaleId, out femaleBehavior)) femaleBehavior.CancelPairingApproach();
            }

            pairingApproach = null;
            if (routeFailure)
            {
                string pairKey = failedApproach.maleId + "|" + failedApproach.femaleId;
                if (pairingRouteRetryPairKey != pairKey)
                {
                    pairingRouteRetryPairKey = pairKey;
                    pairingRouteRetryCount = 0;
                }
                pairingRouteRetryCount++;
                pairingFailedMaleTarget = failedApproach.maleTarget;
                pairingFailedFemaleTarget = failedApproach.femaleTarget;
                RatData failedMale = cancelledMale;
                RatData failedFemale = cancelledFemale;
                string diagnosticRat = failedFemale != null ? ColonyFactory.DisplayName(failedFemale) :
                    (failedMale != null ? ColonyFactory.DisplayName(failedMale) : failedApproach.femaleId);
                Debug.Log("[Rat Habitat] " + diagnosticRat + " route recovery: " + failureText);
                StatusMessage = diagnosticRat + " route recovery — nest blocked";
                if (pairingRouteRetryCount > MaximumPairingRouteRetries)
                {
                    if (rats != null && rats.TryGetRatBehavior(failedApproach.maleId, out maleBehavior))
                        maleBehavior.RecoverAtSafeOpenFloor();
                    if (rats != null && rats.TryGetRatBehavior(failedApproach.femaleId, out femaleBehavior))
                        femaleBehavior.RecoverAtSafeOpenFloor();
                    pairingRouteRetryPairKey = null;
                    pairingRouteRetryCount = 0;
                    pairingFailedMaleTarget = Vector3.zero;
                    pairingFailedFemaleTarget = Vector3.zero;
                }
            }
            Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;
            if (!routeFailure)
            {
                // Eligibility changes such as pregnancy are expected state
                // transitions, not route errors or player-facing events.
                StatusMessage = string.Empty;
            }
            RefreshWorldAndUi(false);
            SaveSystem.Save(Save);
        }

        private static string FormatPairingEligibilityCancellation(string reason)
        {
            if (!string.IsNullOrEmpty(reason) &&
                reason.ToLowerInvariant().Contains("outside the fertile window"))
                return "fertile window ended before interaction";
            return reason;
        }

        private bool TryGetPairingParticipant(string ratId, out Transform root, out RatHabitatBehavior behavior)
        {
            root = null;
            behavior = null;
            if (rats == null || !rats.TryGetRatRoot(ratId, out root) || root == null ||
                !rats.TryGetRatBehavior(ratId, out behavior)) return false;
            return true;
        }

        private bool TryBuildPairingApproachPlan(
            Vector3 malePosition,
            Vector3 femalePosition,
            string maleId,
            string femaleId,
            Vector3 excludedMaleTarget,
            Vector3 excludedFemaleTarget,
            out PairingApproachPlan bestPlan)
        {
            bestPlan = null;
            Vector3 midpoint = (malePosition + femalePosition) * 0.5f;
            Bounds nestBounds;
            EnclosureSystem.TryGetPairingNestAvoidanceBounds(0f, out nestBounds);
            Vector3 nestCenter = nestBounds.center;
            nestCenter.y = midpoint.y;
            float nestHalfX = nestBounds.extents.x;
            float nestHalfZ = nestBounds.extents.z;
            float approachClearance = 1.15f;
            var candidates = new List<Vector3>
            {
                new Vector3(midpoint.x, midpoint.y, midpoint.z),
                new Vector3(nestCenter.x, midpoint.y, nestCenter.z + nestHalfZ + approachClearance),
                new Vector3(nestCenter.x, midpoint.y, nestCenter.z - nestHalfZ - approachClearance),
                new Vector3(nestCenter.x + nestHalfX + approachClearance, midpoint.y, nestCenter.z),
                new Vector3(nestCenter.x - nestHalfX - approachClearance, midpoint.y, nestCenter.z),
                new Vector3(nestCenter.x + nestHalfX + approachClearance, midpoint.y, nestCenter.z + nestHalfZ + approachClearance),
                new Vector3(nestCenter.x - nestHalfX - approachClearance, midpoint.y, nestCenter.z + nestHalfZ + approachClearance),
                new Vector3(nestCenter.x + nestHalfX + approachClearance, midpoint.y, nestCenter.z - nestHalfZ - approachClearance),
                new Vector3(nestCenter.x - nestHalfX - approachClearance, midpoint.y, nestCenter.z - nestHalfZ - approachClearance),
            };

            for (int index = 0; index < candidates.Count; index++)
            {
                Vector3 candidate = EnclosureSystem.ClampToEnclosureBounds(
                    RatEnclosure.Pairing, candidates[index], 0.82f);
                if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, candidate, 0.32f)) continue;

                Vector3 separationAxis = PairingSeparationAxis(candidate, nestCenter, malePosition, femalePosition, nestHalfX, nestHalfZ);
                Vector3 maleTarget = EnclosureSystem.ClampToEnclosureBounds(
                    RatEnclosure.Pairing, candidate - separationAxis * (PairingMeetSeparation * 0.5f), 0.78f);
                Vector3 femaleTarget = EnclosureSystem.ClampToEnclosureBounds(
                    RatEnclosure.Pairing, candidate + separationAxis * (PairingMeetSeparation * 0.5f), 0.78f);
                if (!IsValidPairingTarget(maleTarget) || !IsValidPairingTarget(femaleTarget) ||
                    Vector3.Distance(maleTarget, femaleTarget) < 0.62f ||
                    TargetsMatch(maleTarget, femaleTarget, excludedMaleTarget, excludedFemaleTarget)) continue;

                List<Vector3> maleWaypoints;
                List<Vector3> femaleWaypoints;
                float maleRouteLength;
                float femaleRouteLength;
                if (!TryBuildNestSafeRoute(malePosition, maleTarget, out maleWaypoints, out maleRouteLength) ||
                    !TryBuildNestSafeRoute(femalePosition, femaleTarget, out femaleWaypoints, out femaleRouteLength)) continue;
                if (!ArePairingTargetsAvailable(maleTarget, femaleTarget, maleId, femaleId)) continue;

                float distanceToMidpoint = Vector2.Distance(
                    new Vector2(candidate.x, candidate.z), new Vector2(midpoint.x, midpoint.z));
                float score = -(maleRouteLength + femaleRouteLength) - distanceToMidpoint * 0.35f;
                if (bestPlan == null || score > bestPlan.score)
                {
                    bestPlan = new PairingApproachPlan
                    {
                        maleTarget = maleTarget,
                        femaleTarget = femaleTarget,
                        maleWaypoints = maleWaypoints,
                        femaleWaypoints = femaleWaypoints,
                        score = score,
                    };
                }
            }
            return bestPlan != null;
        }

        private static Vector3 PairingSeparationAxis(Vector3 candidate, Vector3 nestCenter,
            Vector3 malePosition, Vector3 femalePosition, float nestHalfX, float nestHalfZ)
        {
            Vector3 axis;
            if (Mathf.Abs(candidate.x - nestCenter.x) > nestHalfX + 0.4f)
            {
                axis = Vector3.forward;
            }
            else if (Mathf.Abs(candidate.z - nestCenter.z) > nestHalfZ + 0.4f)
            {
                axis = Vector3.right;
            }
            else
            {
                axis = femalePosition - malePosition;
                axis.y = 0f;
                if (axis.sqrMagnitude <= 0.01f) axis = Vector3.right;
                else axis.Normalize();
            }
            return axis;
        }

        private static bool IsValidPairingTarget(Vector3 point)
        {
            return EnclosureSystem.IsBehaviorPointAllowed(RatEnclosure.Pairing, point) &&
                !EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, point, 0.12f);
        }

        private static bool TargetsMatch(Vector3 maleTarget, Vector3 femaleTarget,
            Vector3 excludedMaleTarget, Vector3 excludedFemaleTarget)
        {
            if (excludedMaleTarget == Vector3.zero || excludedFemaleTarget == Vector3.zero) return false;
            return Vector3.Distance(maleTarget, excludedMaleTarget) < 0.28f &&
                Vector3.Distance(femaleTarget, excludedFemaleTarget) < 0.28f;
        }

        private bool ArePairingTargetsAvailable(Vector3 maleTarget, Vector3 femaleTarget,
            string maleId, string femaleId)
        {
            if (Save == null || rats == null) return true;
            foreach (RatData other in Save.rats)
            {
                if (other == null || other.id == maleId || other.id == femaleId ||
                    other.enclosure != RatEnclosure.Pairing) continue;
                Transform otherRoot;
                if (!rats.TryGetRatRoot(other.id, out otherRoot) || otherRoot == null) continue;
                Vector2 position = new Vector2(otherRoot.position.x, otherRoot.position.z);
                if (Vector2.Distance(position, new Vector2(maleTarget.x, maleTarget.z)) < 1.18f ||
                    Vector2.Distance(position, new Vector2(femaleTarget.x, femaleTarget.z)) < 1.18f) return false;
            }
            return true;
        }

        private static bool TryBuildNestSafeRoute(Vector3 start, Vector3 end,
            out List<Vector3> waypoints, out float routeLength)
        {
            waypoints = new List<Vector3>();
            routeLength = Vector3.Distance(start, end);
            if (!EnclosureSystem.IsInside(RatEnclosure.Pairing, start, 0f) ||
                !EnclosureSystem.IsInside(RatEnclosure.Pairing, end, 0f) ||
                EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, start, 0.12f) ||
                EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, end, 0.12f)) return false;
            if (EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing, start, end, 0.12f)) return true;

            Bounds nestBounds;
            EnclosureSystem.TryGetPairingNestAvoidanceBounds(0.34f, out nestBounds);
            float y = start.y;
            Vector3[] rawCorners =
            {
                new Vector3(nestBounds.min.x, y, nestBounds.min.z),
                new Vector3(nestBounds.min.x, y, nestBounds.max.z),
                new Vector3(nestBounds.max.x, y, nestBounds.min.z),
                new Vector3(nestBounds.max.x, y, nestBounds.max.z),
            };
            Vector3[] corners = new Vector3[rawCorners.Length];
            for (int index = 0; index < rawCorners.Length; index++)
            {
                // The enlarged nest may sit close to the lower cage wall.
                // Project only the route corner to the wall-safe cage bounds;
                // never project it through the nest itself.
                corners[index] = EnclosureSystem.ClampToEnclosureBounds(
                    RatEnclosure.Pairing, rawCorners[index], 0.78f);
            }

            var best = new List<Vector3>();
            float bestLength = float.MaxValue;
            for (int first = 0; first < corners.Length; first++)
            {
                if (!IsValidRoutePoint(corners[first])) continue;
                if (!EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing, start, corners[first], 0.02f) ||
                    !EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing, corners[first], end, 0.02f)) continue;
                float length = Vector3.Distance(start, corners[first]) + Vector3.Distance(corners[first], end);
                if (length < bestLength)
                {
                    bestLength = length;
                    best = new List<Vector3> { corners[first] };
                }

                for (int second = 0; second < corners.Length; second++)
                {
                    if (second == first || !IsValidRoutePoint(corners[second])) continue;
                    if (!EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing, corners[first], corners[second], 0.02f) ||
                        !EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing, corners[second], end, 0.02f)) continue;
                    length = Vector3.Distance(start, corners[first]) +
                        Vector3.Distance(corners[first], corners[second]) +
                        Vector3.Distance(corners[second], end);
                    if (length < bestLength)
                    {
                        bestLength = length;
                        best = new List<Vector3> { corners[first], corners[second] };
                    }
                }
            }

            if (best.Count == 0) return false;
            waypoints = best;
            routeLength = bestLength;
            return true;
        }

        private static bool IsValidRoutePoint(Vector3 point)
        {
            return EnclosureSystem.IsInside(RatEnclosure.Pairing, point, 0.78f) &&
                !EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, point, 0.02f);
        }

        private void Update()
        {
            UpdateWorldViewport();
            UpdateCameraPresentation();
            UpdateHabitatPageSettling();
            bool wakeLockStatusChanged = BrowserWakeLockSystem.PollStatus();
            if (wakeLockStatusChanged && ui != null) ui.RefreshWakeLockControls();
            if (Save == null) return;

            bool welcomePaused = ui != null && ui.IsWelcomeOpen;
            GrowthSystem.SetSimulationPaused(welcomePaused);
            if (welcomePaused)
            {
                // Keep the clock's real-time anchor at the current instant so
                // dismissing the modal never causes a catch-up jump.
                if (Save.clock != null) Save.clock.lastRealTimestamp = GameConfig.NowMs();
                return;
            }

            GrowthSystem.AdvanceClock(Save, GameConfig.NowMs());
            bool stageChanged = GrowthSystem.RefreshRatStages(Save);
            bool reproductiveStateChanged = BreedingSystem.RefreshReproductiveStates(Save, GameTime);
            bool storeChanged = StoreSystem.AdvanceRestock(Save, GameTime);
            bool saleEligibilityChanged = UpdateSaleEligibilitySignature();
            UpdatePairingApproach();
            PruneGroupSelection();
            List<DedicatedBreedingSessionData> completedSessions;
            int completedSessionCount = BreedingSystem.ResolveDueDedicatedBreedingSessions(Save, GameTime, out completedSessions);
            if (completedSessionCount > 0)
            {
                EnclosureSystem.ClearBreedingPair();
                RestoreDedicatedBreedingPair();
                // Persist the finished session and any pregnancy before
                // announcing a result. A success message is only valid after
                // the actual pregnancy state has been written successfully.
                bool resolvedStateSaved = SaveSystem.Save(Save);
                if (resolvedStateSaved)
                {
                    // Dedicated-session start/success/failure details stay
                    // out of the global log. A real pregnancy is still an
                    // allowed colony-wide event, so announce only successful
                    // sessions after the pregnancy state was saved.
                    foreach (var completedSession in completedSessions)
                    {
                        if (completedSession == null || !completedSession.conceptionSucceeded) continue;
                        // Use the pregnancy record created by the completed
                        // session. Its motherId remains authoritative after
                        // reloads and cannot be confused with the father or a
                        // stale profile selection.
                        PregnancyData pregnancy = BreedingSystem.FindPendingPregnancyForMother(
                            Save, completedSession.motherId);
                        AnnounceCreatedPregnancy(pregnancy);
                    }
                }
                else
                {
                    Debug.LogWarning("[Rat Habitat] Dedicated breeding resolved but the outcome could not be saved yet.");
                }
                reproductiveStateChanged = true;
            }
            List<LitterData> newLitters;
            int births = BreedingSystem.FinishDuePregnancies(Save, GameTime, out newLitters);
            if (births > 0)
            {
                foreach (var litter in newLitters)
                    AnnounceBirth(litter);
                stageChanged = true;
            }
            bool activityChanged = RefreshRatActivities();
            bool enclosureChanged = EnclosureSystem.RecalculateAssignments(Save);
            if (activityChanged) SaveSystem.Save(Save);
            if (SelectedRat == null && !string.IsNullOrEmpty(selectedRatId))
            {
                selectedRatId = null;
                if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            }

            saveTimer += Time.unscaledDeltaTime;
            // Autosave independently of whether the current frame advanced
            // the simulation. This captures UI/data changes and gives the
            // WebGL localStorage record a recent synchronous copy before a
            // browser tab is closed.
            if (saveTimer >= 2f)
            {
                SaveSystem.Save(Save);
                saveTimer = 0f;
            }
            if (stageChanged || reproductiveStateChanged || enclosureChanged)
            {
                try
                {
                    if (rats != null && habitat != null) rats.Render(Save, habitat.NestPosition);
                }
                catch (Exception exception)
                {
                    startupWarning = true;
                    Debug.LogException(exception);
                    StatusMessage = "Rat presentation update warning — see the Unity Console.";
                }
                if (births > 0 && rats != null)
                {
                    // Keep the mother beside, rather than on, the nest for
                    // the birth event. Newborn roots are placed inside the
                    // nest by BreedingSystem/RatPresenter and remain fixed.
                    foreach (var litter in newLitters)
                    {
                        if (litter == null) continue;
                        RatData mother = BreedingSystem.FindRat(Save, litter.motherId);
                        if (mother != null)
                            rats.PlaceRatBesideNest(mother.id, mother.enclosure);
                    }
                }
                SaveSystem.Save(Save);
            }
            if (completedSessionCount > 0)
            {
                EnclosureSystem.RecalculateAssignments(Save);
                SaveSystem.Save(Save);
                if (rats != null && habitat != null) rats.Render(Save, habitat.NestPosition);
            }
            // Start visible mother/pup care only after the presenter has a
            // stable root for every newborn. NursingSystem persists each
            // pup's turn and the mother's short interaction deadline.
            bool nursingChanged = NursingSystem.Tick(Save, GameTime, rats);
            activityChanged = activityChanged || nursingChanged;
            if (nursingChanged) SaveSystem.Save(Save);
            if (storeChanged)
            {
                StatusMessage = "Rat Market restocked.";
                SaveSystem.Save(Save);
                if (ui != null) ui.Refresh(true);
            }
            if (ui != null)
            {
                try
                {
                    // Keep the clock/status strip live, but do not rebuild
                    // ScrollRect content every frame. At the new accelerated
                    // simulation speeds that would destroy page buttons
                    // continuously and make them impossible to click.
                    if (stageChanged || reproductiveStateChanged || enclosureChanged || activityChanged || saleEligibilityChanged)
                        ui.Refresh(true);
                    else
                        ui.RefreshHeader();
                }
                catch (Exception exception)
                {
                    if (!uiUpdateErrorLogged)
                    {
                        uiUpdateErrorLogged = true;
                        Debug.LogException(exception);
                    }
                }
            }
        }

        private bool UpdateSaleEligibilitySignature()
        {
            if (Save == null || Save.rats == null) return false;
            var builder = new System.Text.StringBuilder();
            foreach (RatData rat in Save.rats)
            {
                if (rat == null) continue;
                builder.Append(rat.id ?? string.Empty).Append('=')
                    .Append(CanSellRat(rat) ? '1' : '0').Append(';');
            }
            string signature = builder.ToString();
            bool changed = saleEligibilitySignature != null && saleEligibilitySignature != signature;
            saleEligibilitySignature = signature;
            return changed;
        }

        public bool SelectEntity(SelectableEntity entity)
        {
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            habitatZoomOffset = 0f;
            ClearPendingSelectionActions();
            if (entity == null)
            {
                // A valid world tap that does not hit an interactable is an
                // empty-floor tap. Clear both the data selection and its
                // visual ring instead of leaving the previous profile open.
                selectedRatId = null;
                selectedObjectId = null;
                cameraFollowSelectedRat = false;
                cameraView = NormalizeHabitatView(cameraView);
                if (rats != null) rats.SetSelected(null);
                if (ui != null) ui.SuppressGeneratedUiActionsThisFrame();
                if (ui != null) ui.Refresh(true);
                return true;
            }
            if (entity.kind == SelectableKind.Rat)
            {
                if (multipleSelectionMode)
                {
                    ToggleRatGroupSelection(entity.entityId);
                    return true;
                }
                selectedRatId = entity.entityId;
                selectedObjectId = null;
                cameraView = CameraViewForRat(BreedingSystem.FindRat(Save, entity.entityId));
                cameraFollowSelectedRat = true;

                // A world rat click is an explicit request to inspect that
                // rat. Close any page/overlay first so the profile cannot be
                // hidden behind Habitat, My Rats, Settings, or breeding UI.
                if (breedingOpen)
                {
                    breedingOpen = false;
                    parentAId = null;
                    parentBId = null;
                    breedingSelectionSlot = BreedingParentSlot.None;
                    EnclosureSystem.ClearBreedingPair();
                    EnclosureSystem.RecalculateAssignments(Save);
                }
                if (ui != null) ui.CloseTransientPanels();
            }
            else
            {
                selectedObjectId = entity.entityId;
                selectedRatId = null;
            }
            if (rats != null) rats.SetSelected(entity.kind == SelectableKind.Rat ? entity.entityId : null);
            if (ui != null) ui.SuppressGeneratedUiActionsThisFrame();
            if (ui != null) ui.Refresh(true);
            return true;
        }

        private void ClearPendingSelectionActions()
        {
            // Selection is inspection only. Any confirmation that was armed
            // for a previous rat must be cancelled before a new profile is
            // rebuilt, otherwise a refreshed control could inherit the old
            // rat's pending action.
            sellConfirmationRatId = null;
            euthanizeConfirmationRatId = null;
            deleteConfirmationRatId = null;
        }

        public void ToggleMultipleSelectionMode()
        {
            multipleSelectionMode = !multipleSelectionMode;
            if (!multipleSelectionMode)
            {
                selectedRatIds.Clear();
                if (rats != null) rats.SetSelected(selectedRatId);
            }
            else
            {
                selectedRatId = null;
                selectedObjectId = null;
                if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            }
            if (ui != null) ui.Refresh(true);
        }

        /// <summary>
        /// Leaves My Rats multi-select as soon as that page is no longer the
        /// visible interaction context. This prevents a stale roster mode
        /// from turning a later habitat/profile tap into another group toggle.
        /// </summary>
        public void DeactivateMultipleSelection()
        {
            if (!multipleSelectionMode && selectedRatIds.Count == 0 && !groupMoveConfirmationPending && !groupSellConfirmationPending) return;
            multipleSelectionMode = false;
            selectedRatIds.Clear();
            groupMoveConfirmationPending = false;
            groupSellConfirmationPending = false;
            if (rats != null) rats.SetSelected(selectedRatId);
        }

        /// <summary>
        /// Clears the live habitat selection without refreshing the UI. A
        /// top-level tab switch uses this as the first half of one atomic
        /// navigation transition, then the UI rebuilds the requested panel in
        /// the same frame. Keeping the refresh out of this helper prevents an
        /// old profile from being rebuilt between clearing the selection and
        /// selecting the new tab.
        /// </summary>
        public void ClearSelectionForNavigation()
        {
            ClearPendingSelectionActions();
            selectedRatId = null;
            selectedObjectId = null;
            cameraFollowSelectedRat = false;
            habitatZoomOffset = 0f;
            if (rats != null) rats.SetSelected(null);
        }

        public bool IsRatSelectedForGroup(string ratId)
        {
            return !string.IsNullOrEmpty(ratId) && selectedRatIds.Contains(ratId);
        }

        public void ToggleRatGroupSelection(string ratId)
        {
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            if (rat == null || !multipleSelectionMode) return;
            if (!selectedRatIds.Add(ratId)) selectedRatIds.Remove(ratId);
            selectedRatId = null;
            selectedObjectId = null;
            if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            if (ui != null) ui.SuppressGeneratedUiActionsThisFrame();
            if (ui != null) ui.Refresh(true);
        }

        public void ClearMultipleSelection()
        {
            selectedRatIds.Clear();
            if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            if (ui != null) ui.Refresh(true);
        }

        private void PruneGroupSelection()
        {
            if (Save == null || selectedRatIds.Count == 0) return;
            var stale = new List<string>();
            foreach (var id in selectedRatIds)
            {
                if (BreedingSystem.FindRat(Save, id) == null) stale.Add(id);
            }
            if (stale.Count == 0) return;
            foreach (var id in stale) selectedRatIds.Remove(id);
            if (rats != null) rats.SetSelectedGroup(selectedRatIds);
        }

        public void ReturnToHabitat()
        {
            SelectEntity(null);
        }

        /// <summary>
        /// Handles a tap on habitat space that did not hit a rat or object.
        /// World taps are intentionally separate from SelectEntity(null): the
        /// latter is the explicit Return-to-Habitat action and means Overview,
        /// while an empty-space tap should focus the enclosure under the tap.
        /// </summary>
        public void FocusHabitatAtWorldPoint(Vector3 worldPoint)
        {
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            if (breedingOpen)
            {
                breedingOpen = false;
                parentAId = null;
                parentBId = null;
                breedingSelectionSlot = BreedingParentSlot.None;
                EnclosureSystem.ClearBreedingPair();
                EnclosureSystem.RecalculateAssignments(Save);
            }

            selectedRatId = null;
            selectedObjectId = null;
            cameraFollowSelectedRat = false;
            if (rats != null) rats.SetSelected(null);
            if (ui != null) ui.CloseTransientPanels();

            HabitatCameraView view = CameraViewForWorldPoint(worldPoint);
            cameraView = NormalizeHabitatView(view);
            habitatZoomOffset = 0f;
            cameraMoveVelocity = Vector3.zero;
            cameraZoomVelocity = 0f;
            if (ui != null) ui.Refresh(true);
        }

        private static HabitatCameraView CameraViewForWorldPoint(Vector3 worldPoint)
        {
            RatEnclosure closest = RatEnclosure.FemaleColony;
            float closestDistance = float.MaxValue;

            foreach (RatEnclosure enclosure in EnclosureSystem.AllEnclosures)
            {
                EnclosureSystem.Definition definition = EnclosureSystem.GetDefinition(enclosure);
                float dx = worldPoint.x < definition.minX ? definition.minX - worldPoint.x :
                    (worldPoint.x > definition.maxX ? worldPoint.x - definition.maxX : 0f);
                float dz = worldPoint.z < definition.minZ ? definition.minZ - worldPoint.z :
                    (worldPoint.z > definition.maxZ ? worldPoint.z - definition.maxZ : 0f);
                float distance = dx * dx + dz * dz;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = enclosure;
                }
            }

            switch (closest)
            {
                case RatEnclosure.MaleColony: return HabitatCameraView.MaleEnclosure;
                case RatEnclosure.Breeding: return HabitatCameraView.Breeding;
                case RatEnclosure.Pairing: return HabitatCameraView.Pairing;
                default: return HabitatCameraView.FemaleEnclosure;
            }
        }

        private static string CameraViewLabel(HabitatCameraView view)
        {
            switch (view)
            {
                case HabitatCameraView.MaleEnclosure: return "Male Cage";
                case HabitatCameraView.FemaleEnclosure: return "Female Cage";
                case HabitatCameraView.Breeding: return "Breeding";
                case HabitatCameraView.Pairing: return "Pairing Habitat";
                default: return "Overview";
            }
        }

        private static HabitatCameraView NormalizeHabitatView(HabitatCameraView view)
        {
            return view == HabitatCameraView.Overview || view == HabitatCameraView.Nursery
                ? HabitatCameraView.Pairing
                : view;
        }

        public void MoveSelectedRatToPairingHabitat()
        {
            MoveRatToPairingHabitat(selectedRatId);
        }

        /// <summary>
        /// Moves the explicitly requested live rat into Pairing Habitat.
        /// Profile actions pass their row/profile ID rather than relying on a
        /// possibly stale global selection. Pairing placement is a manual
        /// assignment and must not be overwritten by an old breeding-selection
        /// marker during the same refresh.
        /// </summary>
        public void MoveRatToPairingHabitat(string ratId)
        {
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            if (rat == null)
            {
                StatusMessage = "Select a rat first.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            if (rat.enclosure == RatEnclosure.Pairing)
            {
                // Repair older saves that carried the enum but not the
                // explicit manual-placement flag. The Pairing Habitat is
                // intentionally independent of age, sex, fertility, and
                // pregnancy eligibility.
                rat.pairingHabitatAssigned = true;
                SaveSystem.Save(Save);
                StatusMessage = ColonyFactory.DisplayName(rat) + " is already in the Pairing Habitat.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            int pairingCount = CountPairingHabitatRats();
            if (pairingCount >= GameConfig.BasePairingHabitatCapacity)
            {
                StatusMessage = "Pairing Habitat is full (" + pairingCount + "/" +
                    GameConfig.BasePairingHabitatCapacity + "). Move a rat out first.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            if (BreedingSystem.FindActiveDedicatedSession(Save, rat.id) != null)
            {
                StatusMessage = ColonyFactory.DisplayName(rat) + " is occupied by a dedicated breeding session.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            // A profile can remain open after the breeding chooser was closed.
            // If this rat was still marked as one of that old pair, the next
            // assignment reconciliation would immediately put it back in the
            // breeding enclosure. A deliberate manual move supersedes that
            // stale chooser state, but never interrupts a live dedicated
            // session (which is rejected above).
            if (EnclosureSystem.IsActiveBreedingParticipant(rat))
            {
                breedingOpen = false;
                parentAId = null;
                parentBId = null;
                breedingSelectionSlot = BreedingParentSlot.None;
                EnclosureSystem.ClearBreedingPair();
            }

            RatEnclosure previousEnclosure = rat.enclosure;
            rat.enclosure = RatEnclosure.Pairing;
            rat.pairingHabitatAssigned = true;
            if (previousEnclosure != RatEnclosure.Pairing)
                RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving habitats", GameTime,
                    "Moved to Pairing Habitat");
            EnclosureSystem.RecalculateAssignments(Save);
            // Keep this operation atomic even if a legacy/stale assignment
            // was encountered during reconciliation. The next frame and the
            // saved data must both see Pairing as the authoritative location.
            rat.enclosure = RatEnclosure.Pairing;
            rat.pairingHabitatAssigned = true;
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraFollowSelectedRat = true;
            lastPairingMoveRatId = rat.id;
            lastPairingMoveFrame = Time.frameCount;
            if (rats != null) rats.SetSelected(rat.id);
            cameraView = CameraViewForRat(rat);
            StatusMessage = "[Rat Empire] " + ColonyFactory.DisplayName(rat) + " moved to Pairing Habitat.";
            Debug.Log(StatusMessage);
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void RemoveSelectedRatFromPairingHabitat()
        {
            RemoveRatFromPairingHabitat(selectedRatId);
        }

        /// <summary>
        /// Removes only the explicitly requested rat. Profile actions must
        /// pass their stable data ID so a stale selection cannot remove a
        /// different rat after the profile has refreshed.
        /// </summary>
        public void RemoveRatFromPairingHabitat(string ratId)
        {
            if (!string.IsNullOrEmpty(lastPairingMoveRatId) &&
                lastPairingMoveRatId == ratId &&
                Time.frameCount <= lastPairingMoveFrame + 1)
            {
                // A rebuilt profile can expose the new Remove button beneath
                // the pointer that just confirmed Move. Ignore that stale
                // same-input activation; an intentional second tap is still
                // accepted on the following frame.
                return;
            }

            RatData rat = BreedingSystem.FindRat(Save, ratId);
            if (rat == null || rat.enclosure != RatEnclosure.Pairing)
            {
                StatusMessage = "The selected rat is not in the Pairing Habitat.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            if ((rat.stage == RatStage.Adult || rat.stage == RatStage.Mature) && rat.sex == RatSex.Female &&
                (EnclosureSystem.IsPregnant(Save, rat) || EnclosureSystem.HasDependentPinkies(Save, rat.id)))
            {
                StatusMessage = ColonyFactory.DisplayName(rat) + " must remain in the Pairing Habitat until her pregnancy and dependent litter are complete.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            RatEnclosure previousEnclosure = rat.enclosure;
            rat.enclosure = EnclosureSystem.StandardEnclosure(Save, rat);
            rat.pairingHabitatAssigned = false;
            if (previousEnclosure == RatEnclosure.Pairing)
                RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving habitats", GameTime,
                    "Moved from Pairing Habitat");
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = rat.id;
            cameraFollowSelectedRat = true;
            if (rats != null) rats.SetSelected(rat.id);
            cameraView = CameraViewForRat(rat);
            StatusMessage = "[Rat Empire] " + ColonyFactory.DisplayName(rat) + " removed from Pairing Habitat.";
            Debug.Log(StatusMessage);
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        /// <summary>
        /// Clears transient selection/chooser state before Settings becomes
        /// the only active panel. It does not alter habitat assignments or
        /// interrupt an active dedicated breeding session.
        /// </summary>
        public void PrepareForSettings()
        {
            DeactivateMultipleSelection();
            // Do not leave a Store/profile confirmation or a group/pairing
            // evacuation prompt armed behind the modal Settings panel.
            deleteConfirmationRatId = null;
            sellConfirmationRatId = null;
            euthanizeConfirmationRatId = null;
            groupMoveConfirmationPending = false;
            groupSellConfirmationPending = false;
            pairingMoveAllConfirmationPending = false;
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            RestoreDedicatedBreedingPair();
            selectedRatId = null;
            selectedObjectId = null;
            if (rats != null) rats.SetSelected(null);
        }

        public void RequestMoveAllOutOfPairingHabitat()
        {
            if (Save == null) return;
            bool hasPairingRat = false;
            foreach (var rat in Save.rats)
            {
                if (rat != null && rat.enclosure == RatEnclosure.Pairing) { hasPairingRat = true; break; }
            }
            if (!hasPairingRat)
            {
                StatusMessage = "The Pairing Habitat is already empty.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            pairingMoveAllConfirmationPending = true;
            StatusMessage = "Move every Pairing Habitat rat out? Mothers and pups will stay together in a normal habitat.";
            if (ui != null) ui.Refresh(true);
        }

        public void CancelMoveAllOutOfPairingHabitat()
        {
            pairingMoveAllConfirmationPending = false;
            StatusMessage = "Pairing Habitat evacuation cancelled.";
            if (ui != null) ui.Refresh(true);
        }

        public void ConfirmMoveAllOutOfPairingHabitat()
        {
            if (!pairingMoveAllConfirmationPending || Save == null) return;
            int moved = 0;
            var pairingRats = new List<RatData>();
            foreach (var rat in Save.rats)
            {
                if (rat != null && rat.enclosure == RatEnclosure.Pairing) pairingRats.Add(rat);
            }
            foreach (var rat in pairingRats)
            {
                RatEnclosure destination = PairingEvacuationDestination(rat);
                rat.enclosure = destination;
                rat.pairingHabitatAssigned = false;
                RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving habitats", GameTime,
                    "Moved to " + EnclosureSystem.Label(destination));
                moved++;
            }
            pairingMoveAllConfirmationPending = false;
            selectedRatId = null;
            selectedObjectId = null;
            selectedRatIds.Clear();
            if (rats != null) rats.SetSelected(null);
            EnclosureSystem.RecalculateAssignments(Save);
            cameraView = HabitatCameraView.Pairing;
            StatusMessage = "Moved " + moved + " rat" + (moved == 1 ? string.Empty : "s") + " out of the Pairing Habitat.";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        private RatEnclosure PairingEvacuationDestination(RatData rat)
        {
            if (rat == null) return RatEnclosure.FemaleColony;
            if (rat.stage == RatStage.Pinkie && !string.IsNullOrEmpty(rat.motherId))
            {
                RatData mother = BreedingSystem.FindRat(Save, rat.motherId);
                if (mother != null && mother.enclosure != RatEnclosure.Pairing &&
                    mother.enclosure != RatEnclosure.Nursery)
                    return mother.enclosure;
                // When the whole family is being evacuated, the mother will
                // resolve to Female Cage below; keep every pup with her.
                return RatEnclosure.FemaleColony;
            }
            return rat.sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony;
        }

        public void RequestMoveSelectedRats(RatEnclosure target)
        {
            if (!multipleSelectionMode || selectedRatIds.Count == 0)
            {
                StatusMessage = "Select at least one rat first.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            groupMoveConfirmationTarget = target;
            bool needsConfirmation = false;
            foreach (var id in selectedRatIds)
            {
                RatData rat = BreedingSystem.FindRat(Save, id);
                if (rat == null) continue;
                bool hasFamily = rat.nursing || EnclosureSystem.HasDependentPinkies(Save, rat.id) || HasSelectedMotherRelationship(rat.id);
                bool separatesFamily = target != RatEnclosure.Pairing;
                if ((separatesFamily && hasFamily) || HasUnselectedMotherRelationship(rat.id))
                {
                    needsConfirmation = true;
                    break;
                }
            }
            if (needsConfirmation)
            {
                groupMoveConfirmationPending = true;
                StatusMessage = "This group includes a mother or dependent litter. Confirm if separation is intentional.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            MoveSelectedRatsToHabitat(target);
        }

        public void ConfirmMoveSelectedRats()
        {
            if (!groupMoveConfirmationPending) return;
            groupMoveConfirmationPending = false;
            groupSellConfirmationPending = false;
            MoveSelectedRatsToHabitat(groupMoveConfirmationTarget);
        }

        public void CancelMoveSelectedRats()
        {
            groupMoveConfirmationPending = false;
            StatusMessage = "Group move cancelled.";
            if (ui != null) ui.Refresh(true);
        }

        public void RequestSellSelectedRats()
        {
            if (!multipleSelectionMode || selectedRatIds.Count == 0)
            {
                StatusMessage = "Select at least one rat first.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            foreach (string id in selectedRatIds)
            {
                RatData rat = BreedingSystem.FindRat(Save, id);
                if (rat == null) continue;
                if (!CanSellRat(rat))
                {
                    StatusMessage = ColonyFactory.DisplayName(rat) + ": " + SaleRestrictionReason(rat);
                    if (ui != null) ui.Refresh(true);
                    return;
                }
            }

            groupSellConfirmationPending = true;
            StatusMessage = "Sell " + selectedRatIds.Count + " selected rat" +
                (selectedRatIds.Count == 1 ? string.Empty : "s") + "? This cannot be undone.";
            if (ui != null) ui.Refresh(true);
        }

        public void CancelSellSelectedRats()
        {
            groupSellConfirmationPending = false;
            StatusMessage = "Group sale cancelled.";
            if (ui != null) ui.Refresh(true);
        }

        public void ConfirmSellSelectedRats()
        {
            if (!groupSellConfirmationPending || Save == null) return;
            var ids = new List<string>(selectedRatIds);
            foreach (string id in ids)
            {
                RatData rat = BreedingSystem.FindRat(Save, id);
                if (rat != null && !CanSellRat(rat))
                {
                    groupSellConfirmationPending = false;
                    StatusMessage = ColonyFactory.DisplayName(rat) + ": " + SaleRestrictionReason(rat);
                    if (ui != null) ui.Refresh(true);
                    return;
                }
            }

            int sold = 0;
            int dollars = 0;
            foreach (string id in ids)
            {
                RatData rat = BreedingSystem.FindRat(Save, id);
                if (rat == null) continue;
                int value = SellValue(rat);
                if (RetireActiveRat(rat, RatRemovalDisposition.Sold, true))
                {
                    sold++;
                    dollars += value;
                }
            }
            groupSellConfirmationPending = false;
            multipleSelectionMode = false;
            selectedRatIds.Clear();
            StatusMessage = "Sold " + sold + " rat" + (sold == 1 ? string.Empty : "s") +
                " for $" + dollars + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        private bool HasSelectedMotherRelationship(string ratId)
        {
            if (Save == null || string.IsNullOrEmpty(ratId)) return false;
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            if (rat != null && rat.sex == RatSex.Female && EnclosureSystem.HasDependentPinkies(Save, ratId)) return true;
            foreach (var candidate in Save.rats)
            {
                if (candidate != null && candidate.motherId == ratId && candidate.stage == RatStage.Pinkie) return true;
            }
            return false;
        }

        private bool HasUnselectedMotherRelationship(string ratId)
        {
            if (Save == null || string.IsNullOrEmpty(ratId)) return false;
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            if (rat != null && rat.sex == RatSex.Female && EnclosureSystem.HasDependentPinkies(Save, ratId))
            {
                foreach (var pup in Save.rats)
                    if (pup != null && pup.stage == RatStage.Pinkie && pup.motherId == ratId && !selectedRatIds.Contains(pup.id)) return true;
            }
            foreach (var candidate in Save.rats)
            {
                if (candidate == null || candidate.stage != RatStage.Pinkie || candidate.motherId != ratId) continue;
                if (!selectedRatIds.Contains(candidate.id)) return true;
            }
            if (rat != null && rat.stage == RatStage.Pinkie && !string.IsNullOrEmpty(rat.motherId) &&
                !selectedRatIds.Contains(rat.motherId)) return true;
            return false;
        }

        private void MoveSelectedRatsToHabitat(RatEnclosure target)
        {
            if (target == RatEnclosure.Pairing)
            {
                int currentPairingCount = CountPairingHabitatRats();
                int incomingCount = 0;
                foreach (var id in selectedRatIds)
                {
                    RatData candidate = BreedingSystem.FindRat(Save, id);
                    if (candidate == null || candidate.enclosure == RatEnclosure.Pairing) continue;
                    if (BreedingSystem.FindActiveDedicatedSession(Save, candidate.id) != null) continue;
                    incomingCount++;
                }

                int requestedCount = currentPairingCount + incomingCount;
                if (requestedCount > GameConfig.BasePairingHabitatCapacity)
                {
                    StatusMessage = "Pairing Habitat has " +
                        (GameConfig.BasePairingHabitatCapacity - currentPairingCount) +
                        " space" + (GameConfig.BasePairingHabitatCapacity - currentPairingCount == 1 ? string.Empty : "s") +
                        " remaining (" + currentPairingCount + "/" +
                        GameConfig.BasePairingHabitatCapacity + ").";
                    if (ui != null) ui.Refresh(true);
                    return;
                }
            }

            int moved = 0;
            var ids = new List<string>(selectedRatIds);
            foreach (var id in ids)
            {
                RatData rat = BreedingSystem.FindRat(Save, id);
                if (rat == null) continue;
                RatEnclosure previousEnclosure = rat.enclosure;
                if (target == RatEnclosure.Pairing)
                {
                    if (BreedingSystem.FindActiveDedicatedSession(Save, rat.id) != null) continue;
                    rat.enclosure = RatEnclosure.Pairing;
                    rat.pairingHabitatAssigned = true;
                }
                else if (rat.stage == RatStage.Pinkie)
                {
                    rat.enclosure = string.IsNullOrEmpty(rat.motherId)
                        ? (rat.sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony)
                        : PairingEvacuationDestination(rat);
                    rat.pairingHabitatAssigned = false;
                }
                else if (target == RatEnclosure.Nursery)
                {
                    // Compatibility callers may still pass the removed enum.
                    // Treat it as an ordinary sex-based destination rather
                    // than creating a new Nursery assignment.
                    rat.enclosure = rat.sex == RatSex.Male
                        ? RatEnclosure.MaleColony
                        : RatEnclosure.FemaleColony;
                    rat.pairingHabitatAssigned = false;
                }
                else if (target == RatEnclosure.MaleColony && rat.sex == RatSex.Male)
                {
                    rat.enclosure = RatEnclosure.MaleColony;
                    rat.pairingHabitatAssigned = false;
                }
                else if (target == RatEnclosure.FemaleColony && rat.sex == RatSex.Female)
                {
                    rat.enclosure = RatEnclosure.FemaleColony;
                    rat.pairingHabitatAssigned = false;
                }
                else
                {
                    continue;
                }
                if (previousEnclosure != rat.enclosure)
                    RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving habitats", GameTime,
                        "Moved to " + EnclosureSystem.Label(rat.enclosure));
                moved++;
            }
            EnclosureSystem.RecalculateAssignments(Save);
            if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            StatusMessage = "Moved " + moved + " selected rat" + (moved == 1 ? string.Empty : "s") + " to " + EnclosureSystem.Label(target) + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        private int CountPairingHabitatRats()
        {
            if (Save == null || Save.rats == null) return 0;
            int count = 0;
            foreach (var rat in Save.rats)
            {
                if (rat != null && rat.enclosure == RatEnclosure.Pairing) count++;
            }
            return count;
        }

        public void ViewMaleEnclosure()
        {
            SetHabitatCameraView(HabitatCameraView.MaleEnclosure);
        }

        public void ViewFemaleEnclosure()
        {
            SetHabitatCameraView(HabitatCameraView.FemaleEnclosure);
        }

        public void ViewNursery()
        {
            // Compatibility entry point for old generated callbacks. There
            // is no Nursery page anymore; the main habitat is Pairing.
            SetHabitatCameraView(HabitatCameraView.Pairing);
        }

        public void ViewBreedingEnclosure()
        {
            SetHabitatCameraView(HabitatCameraView.Breeding);
        }

        public void ViewPairingHabitat()
        {
            SetHabitatCameraView(HabitatCameraView.Pairing);
        }

        public void ViewHabitatOverview()
        {
            // Kept as a compatibility entry point for older generated UI and
            // saved callbacks. The main habitat is now Pairing, and there is
            // no longer a combined overview page.
            SetHabitatCameraView(HabitatCameraView.Pairing);
        }

        public bool TryNavigateHabitatSwipe(int direction)
        {
            if (direction == 0 || ui == null || ui.IsModalOverlayOpen) return false;
            // A single touch/mouse gesture can generate several end/move
            // callbacks on mobile. Lock only after a page actually changes,
            // so one gesture can never skip a habitat.
            if (habitatPageSettling || Time.unscaledTime < habitatSwipeLockUntil) return false;
            // A profile is an intentional inspection surface. Keep it stable
            // until the player returns to the habitat rather than changing
            // the live camera underneath a profile during a swipe.
            if (IsSelectionPanelVisible()) return false;

            int current = HabitatPageIndex;
            int next = Mathf.Clamp(current + (direction < 0 ? -1 : 1), 0, HabitatPages.Length - 1);
            if (next == current) return false;

            cameraView = HabitatPages[next];
            cameraFollowSelectedRat = false;
            habitatZoomOffset = 0f;
            cameraMoveVelocity = Vector3.zero;
            cameraZoomVelocity = 0f;
            habitatPageSettling = true;
            habitatSettledPageIndex = current;
            habitatPageSettleUntil = Time.unscaledTime + HabitatPageSettleSeconds;
            habitatSwipeLockUntil = Time.unscaledTime +
                Mathf.Max(HabitatSwipeDebounceSeconds, HabitatPageSettleSeconds);
            if (ui != null) ui.RefreshHeader();
            return true;
        }

        private void UpdateHabitatPageSettling()
        {
            if (!habitatPageSettling || Time.unscaledTime < habitatPageSettleUntil) return;
            int settled = Array.IndexOf(HabitatPages, NormalizeHabitatView(cameraView));
            habitatSettledPageIndex = settled < 0 ? habitatSettledPageIndex : settled;
            habitatPageSettling = false;
            if (ui != null) ui.RefreshHeader();
        }

        public void BuyStoreRat(string listingId)
        {
            if (Save == null)
            {
                StatusMessage = "Store data is not ready.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            StoreSystem.EnsureStoreState(Save);
            int colonyCapacity = UpgradeSystem.ColonyCapacity(Save);
            if (Save.rats != null && Save.rats.Count >= colonyCapacity)
            {
                StatusMessage = "Colony capacity reached (" + colonyCapacity + "). Buy a capacity upgrade first.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            StoreRatListingData listing = StoreSystem.FindListing(Save, listingId);
            if (listing == null)
            {
                StatusMessage = "That market rat is no longer available.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            if (Save.colonyCredits < listing.price)
            {
                StatusMessage = "Not enough dollars — need $" + listing.price + " to buy " + ColonyFactory.DisplayName(listing) + ".";
                if (ui != null) ui.Refresh(false);
                return;
            }

            RatData purchased = StoreSystem.CreatePurchasedRat(listing, GameTime);
            if (purchased == null)
            {
                StatusMessage = "The market rat could not be created.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            Save.rats.Add(purchased);
            Save.ratIds.Add(purchased.id);
            Save.colonyCredits -= listing.price;
            Save.storeRatListings.Remove(listing);
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = purchased.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(purchased);
            cameraFollowSelectedRat = true;
            StatusMessage = ColonyFactory.DisplayName(purchased) + " joined the colony for $" + listing.price + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public int ColonyCapacity
        {
            get { return UpgradeSystem.ColonyCapacity(Save); }
        }

        public int StoreQualityCap
        {
            get { return UpgradeSystem.StoreQualityCap(Save); }
        }

        public int StoreQualityUpgradeCost
        {
            get { return UpgradeSystem.StoreQualityUpgradeCost(Save); }
        }

        public int ColonyCapacityUpgradeCost
        {
            get { return UpgradeSystem.ColonyCapacityUpgradeCost(Save); }
        }

        public void PurchaseStoreQualityUpgrade()
        {
            if (Save == null) return;
            UpgradeSystem.EnsureState(Save);
            int cost = UpgradeSystem.StoreQualityUpgradeCost(Save);
            if (Save.colonyCredits < cost)
            {
                StatusMessage = "Not enough dollars. Store quality upgrade costs $" + cost + ".";
                if (ui != null) ui.Refresh(false);
                return;
            }

            int newCap;
            if (!UpgradeSystem.PurchaseStoreQualityUpgrade(Save, out newCap)) return;
            StatusMessage = "Store quality upgraded. New listings can reach " + newCap + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void PurchaseColonyCapacityUpgrade()
        {
            if (Save == null) return;
            UpgradeSystem.EnsureState(Save);
            int cost = UpgradeSystem.ColonyCapacityUpgradeCost(Save);
            if (Save.colonyCredits < cost)
            {
                StatusMessage = "Not enough dollars. Capacity upgrade costs $" + cost + ".";
                if (ui != null) ui.Refresh(false);
                return;
            }

            int newCapacity;
            if (!UpgradeSystem.PurchaseColonyCapacityUpgrade(Save, out newCapacity)) return;
            StatusMessage = "Colony capacity upgraded to " + newCapacity + " rats.";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void RestockStoreNow()
        {
            if (Save == null) return;
            StoreSystem.RestockNow(Save, GameTime);
            StatusMessage = "Rat Market restocked.";
            SaveSystem.Save(Save);
            if (ui != null) ui.Refresh(true);
        }

        private void SetHabitatCameraView(HabitatCameraView view)
        {
            cameraView = NormalizeHabitatView(view);
            int settled = Array.IndexOf(HabitatPages, cameraView);
            habitatSettledPageIndex = settled < 0 ? habitatSettledPageIndex : settled;
            habitatPageSettling = false;
            habitatSwipeLockUntil = 0f;
            habitatZoomOffset = 0f;
            selectedRatId = null;
            selectedObjectId = null;
            cameraFollowSelectedRat = false;
            if (rats != null) rats.SetSelected(null);
            cameraMoveVelocity = Vector3.zero;
            cameraZoomVelocity = 0f;
            if (ui != null) ui.Refresh(true);
        }

        public bool IsSelectionPanelVisible()
        {
            return ui != null && (SelectedRat != null || SelectedObject != null);
        }

        public void SelectRatFromRoster(string id)
        {
            var rat = BreedingSystem.FindRat(Save, id);
            if (rat == null) return;

            if (multipleSelectionMode)
            {
                ToggleRatGroupSelection(id);
                return;
            }

            ClearPendingSelectionActions();
            habitatZoomOffset = 0f;
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(rat);
            cameraFollowSelectedRat = true;
            // The roster is a normal habitat view. Keep a defensive reset here
            // so a stale UI callback can never reopen breeding or leave a
            // duplicate parent pair behind.
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            if (rats != null) rats.SetSelected(rat.id);
            if (ui != null) ui.SuppressGeneratedUiActionsThisFrame();
            if (ui != null) ui.Refresh(true);
        }

        /// <summary>
        /// Changes only the inspected rat for the profile header arrows. This
        /// deliberately does not close panels, toggle breeding, clear the
        /// profile, or invoke any move/sale action; the UI owns the current
        /// profile context and preserves its expanded/scroll state.
        /// </summary>
        public bool SelectRatForProfileNavigation(string id)
        {
            RatData rat = BreedingSystem.FindRat(Save, id);
            if (rat == null) return false;
            ClearPendingSelectionActions();
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(rat);
            cameraFollowSelectedRat = true;
            if (rats != null) rats.SetSelected(rat.id);
            if (ui != null) ui.SuppressGeneratedUiActionsThisFrame();
            if (ui != null) ui.Refresh(true);
            return true;
        }

        /// <summary>
        /// Focuses an active rat for a profile or family-tree view without
        /// clearing an in-progress breeding selection/session. Family-history
        /// navigation is informational and must not mutate breeding state.
        /// </summary>
        public void FocusRatForFamilyTree(string id)
        {
            var rat = BreedingSystem.FindRat(Save, id);
            if (rat == null) return;

            ClearPendingSelectionActions();
            habitatZoomOffset = 0f;
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(rat);
            cameraFollowSelectedRat = true;
            if (rats != null) rats.SetSelected(rat.id);
            if (ui != null) ui.SuppressGeneratedUiActionsThisFrame();
            if (ui != null) ui.Refresh(true);
        }

        private static HabitatCameraView CameraViewForRat(RatData rat)
        {
            if (rat == null) return HabitatCameraView.Overview;
            switch (rat.enclosure)
            {
                case RatEnclosure.MaleColony: return HabitatCameraView.MaleEnclosure;
                case RatEnclosure.Breeding: return HabitatCameraView.Breeding;
                case RatEnclosure.Pairing: return HabitatCameraView.Pairing;
                default: return HabitatCameraView.FemaleEnclosure;
            }
        }

        public void OpenBreeding()
        {
            if (breedingOpen) return;
            var selected = SelectedRat;
            string reason;
            if (!BreedingSystem.IsBreedEligible(Save, selected, GameTime, out reason))
            {
                StatusMessage = reason;
                if (ui != null) ui.Refresh(false);
                return;
            }
            breedingOpen = true;
            parentAId = selected.sex == RatSex.Female ? selected.id : null;
            parentBId = selected.sex == RatSex.Male ? selected.id : null;
            EnclosureSystem.SetBreedingPair(parentAId, parentBId);
            EnclosureSystem.RecalculateAssignments(Save);
            RefreshWorldAndUi(true);
            breedingSelectionSlot = selected.sex == RatSex.Female ? BreedingParentSlot.Father : BreedingParentSlot.Mother;
            StatusMessage = "Choose a " + (breedingSelectionSlot == BreedingParentSlot.Mother ? "mother" : "father") + ".";
            if (ui != null) ui.Refresh(true);
        }

        public void ChangeMother()
        {
            if (!breedingOpen) return;
            breedingSelectionSlot = BreedingParentSlot.Mother;
            StatusMessage = "Choosing the mother. Select an eligible adult female.";
            if (ui != null) ui.Refresh(true);
        }

        public void ChangeFather()
        {
            if (!breedingOpen) return;
            breedingSelectionSlot = BreedingParentSlot.Father;
            StatusMessage = "Choosing the father. Select an eligible adult male.";
            if (ui != null) ui.Refresh(true);
        }

        public void CloseBreeding()
        {
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            RestoreDedicatedBreedingPair();
            EnclosureSystem.RecalculateAssignments(Save);
            RefreshWorldAndUi(true);
            if (ui != null) ui.Refresh(true);
        }

        public void SelectMate(string id)
        {
            if (!breedingOpen) return;
            var candidate = BreedingSystem.FindRat(Save, id);
            if (candidate == null || breedingSelectionSlot == BreedingParentSlot.None) return;

            RatData otherParent = breedingSelectionSlot == BreedingParentSlot.Mother ? Father : Mother;
            if (otherParent != null && candidate.id == otherParent.id)
            {
                StatusMessage = "The same rat cannot occupy both parent slots.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            RatSex expectedSex = breedingSelectionSlot == BreedingParentSlot.Mother ? RatSex.Female : RatSex.Male;
            string reason = string.Empty;
            if (candidate.sex != expectedSex || !BreedingSystem.IsBreedEligible(Save, candidate, GameTime, out reason))
            {
                StatusMessage = ColonyFactory.DisplayName(candidate) + " is not an eligible " + (expectedSex == RatSex.Female ? "mother" : "father") + ". " + reason;
                if (ui != null) ui.Refresh(false);
                return;
            }

            if (breedingSelectionSlot == BreedingParentSlot.Mother) parentAId = candidate.id;
            else parentBId = candidate.id;
            EnclosureSystem.SetBreedingPair(parentAId, parentBId);
            EnclosureSystem.RecalculateAssignments(Save);
            RefreshWorldAndUi(true);
            RatData mother = Mother;
            RatData father = Father;
            StatusMessage = (breedingSelectionSlot == BreedingParentSlot.Mother ? "Mother" : "Father") + " set to " + ColonyFactory.DisplayName(candidate) + "." +
                (mother != null && father != null ? " Both parents are ready to compare." : " Choose the other parent when ready.");
            if (ui != null) ui.Refresh(true);
        }

        public void SelectBreedingParent(string id)
        {
            RatData candidate = BreedingSystem.FindRat(Save, id);
            if (candidate == null)
            {
                StatusMessage = "That breeding candidate is no longer available.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            if (!breedingOpen)
            {
                breedingOpen = true;
                parentAId = null;
                parentBId = null;
            }
            breedingSelectionSlot = candidate.sex == RatSex.Female
                ? BreedingParentSlot.Mother
                : BreedingParentSlot.Father;
            SelectMate(id);
            if (Mother != null && Father != null)
                breedingSelectionSlot = BreedingParentSlot.None;
        }

        public void ConfirmBreeding()
        {
            RatData mother = Mother;
            RatData father = Father;
            DedicatedBreedingSessionData session;
            string reason;
            if (!BreedingSystem.StartDedicatedBreedingSession(Save, mother, father, GameTime, out session, out reason))
            {
                StatusMessage = reason;
                if (ui != null) ui.Refresh(false);
                return;
            }
            EnclosureSystem.SetBreedingPair(mother.id, father.id);
            EnclosureSystem.RecalculateAssignments(Save);
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            SaveSystem.Save(Save);
            // Pregnancy begins care in the mother's current habitat. Reuse
            // the stable presenter roots without creating a second visual or
            // changing selection identity.
            RefreshWorldAndUi(true);
        }

        public void FinishPregnancyTesting()
        {
            var pending = PendingPregnancy;
            if (pending == null)
            {
                StatusMessage = "No pending pregnancy.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            LitterData litter;
            string reason;
            if (!BreedingSystem.FinishPregnancy(Save, pending.id, GameTime, out litter, out reason))
            {
                StatusMessage = reason;
                if (ui != null) ui.Refresh(false);
                return;
            }
            AnnounceBirth(litter);
            EnclosureSystem.RecalculateAssignments(Save);
            SaveSystem.Save(Save);
            rats.Render(Save, habitat.NestPosition);
            if (ui != null) ui.Refresh(true);
        }

        private bool AnnounceBirth(LitterData litter)
        {
            if (Save == null || litter == null || litter.birthAnnouncementLogged) return false;

            // The completed pregnancy record is authoritative for the mother.
            // Do not use the selected rat, the father, or a stale profile.
            PregnancyData pregnancy = BreedingSystem.FindPregnancyForLitter(Save, litter.id);
            if (pregnancy == null || string.IsNullOrEmpty(pregnancy.motherId)) return false;
            RatData mother = BreedingSystem.FindHistoricalRat(Save, pregnancy.motherId);
            if (mother == null) return false;

            int createdPups = 0;
            if (litter.pupIds != null)
            {
                foreach (var pupId in litter.pupIds)
                {
                    if (BreedingSystem.FindHistoricalRat(Save, pupId) != null) createdPups++;
                }
            }
            if (createdPups <= 0) return false;

            // Persist the completed pregnancy, litter, and all created pinkies
            // before exposing the player-facing event.
            if (!SaveSystem.Save(Save)) return false;

            string pupWord = createdPups == 1 ? "pup" : "pups";
            litter.birthAnnouncementLogged = true;
            StatusMessage = ColonyFactory.DisplayName(mother) + " has given birth to " +
                createdPups + " " + pupWord + "!";
            // Persist both the one-time guard and the approved event entry.
            SaveSystem.Save(Save);
            return true;
        }

        public void GrowSelectedRat()
        {
            var rat = SelectedRat;
            if (rat == null) return;
            if (!GrowthSystem.AdvanceRatToNextStage(rat, GameTime))
            {
                StatusMessage = "Already adult.";
                if (ui != null) ui.Refresh(false);
                return;
            }
                StatusMessage = ColonyFactory.DisplayName(rat) + " advanced to " + GrowthSystem.StageLabel(rat.stage) + ". Fur and markings now reveal at the young stage.";
            EnclosureSystem.RecalculateAssignments(Save);
            SaveSystem.Save(Save);
            rats.Render(Save, habitat.NestPosition);
            if (ui != null) ui.Refresh(true);
        }

        public void GrowAllPinkies()
        {
            int count = GrowthSystem.AdvanceAllPinkiesToYoung(Save);
            StatusMessage = count + " Pinkie" + (count == 1 ? " advanced" : "s advanced") + " to Young Rat.";
            EnclosureSystem.RecalculateAssignments(Save);
            SaveSystem.Save(Save);
            rats.Render(Save, habitat.NestPosition);
            if (ui != null) ui.Refresh(true);
        }

        public void GrowAllYoungRats()
        {
            int count = GrowthSystem.AdvanceAllYoungToAdults(Save);
            StatusMessage = count + " Young Rat" + (count == 1 ? " advanced" : "s advanced") + " to Adult.";
            EnclosureSystem.RecalculateAssignments(Save);
            SaveSystem.Save(Save);
            rats.Render(Save, habitat.NestPosition);
            if (ui != null) ui.Refresh(true);
        }

        public void SpawnDeveloperRat(DeveloperRatPreset preset)
        {
            if (Save == null)
            {
                StatusMessage = "Colony data is not ready.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            GenotypeData genotype;
            switch (preset)
            {
                case DeveloperRatPreset.SolidBlack:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s");
                    break;
                case DeveloperRatPreset.SolidBrown:
                    genotype = GeneticsSystem.CreateFounder("b", "b", "C", "C", "D", "D", "s", "s");
                    break;
                case DeveloperRatPreset.DilutedBlack:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "d", "d", "s", "s");
                    break;
                case DeveloperRatPreset.DilutedBrown:
                    genotype = GeneticsSystem.CreateFounder("b", "b", "C", "C", "d", "d", "s", "s");
                    break;
                case DeveloperRatPreset.Albino:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "c", "c", "D", "D", "s", "s");
                    break;
                case DeveloperRatPreset.SpottedBlack:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "S", "S");
                    break;
                default:
                    genotype = GeneticsSystem.CreateFounder("b", "b", "C", "C", "D", "D", "S", "S");
                    break;
            }

            Save.EnsureLists();
            RatSex sex = Save.rats.Count % 2 == 0 ? RatSex.Female : RatSex.Male;
            float developerAdultAge = (sex == RatSex.Female
                ? GameConfig.FemaleSexualMaturityDays
                : GameConfig.MaleSexualMaturityDays) + 30f;
            long birthTimestamp = GameTime - (long)(developerAdultAge * GameConfig.GameDayMs);
            string developerId = ColonyFactory.NewId("dev_rat");
            // Developer presets deliberately reuse the normal sex-specific
            // friendly-name pools. IDs remain unique, so duplicate display
            // names never compromise selection or save data.
            string name = ColonyFactory.GeneratedName(developerId, sex);
            var rat = ColonyFactory.CreateRat(
                developerId,
                name,
                sex,
                birthTimestamp,
                0,
                genotype,
                new TraitData(60f, 100f, 75f),
                RatStage.Adult);
            // CreateRat already derives the phenotype from the real genotype;
            // derive once more explicitly here so this developer action cannot
            // accidentally become a visual-only tint path.
            GeneticsSystem.EnsureCoatAppearance(rat);
            rat.phenotype = GeneticsSystem.DerivePhenotype(RatStage.Adult, rat.genotype,
                rat.coatColorVariant, rat.coatTone);
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            Debug.Log("[Rat Habitat] Developer spawn audit: rat=" + rat.name +
                " genotype=" + DeveloperGeneSummary(rat.genotype) +
                " coatColorId=" + rat.phenotype.coatColorId +
                " coatColorHex=" + rat.phenotype.coatColorHex +
                " accentHex=" + rat.phenotype.accentHex +
                " spotted=" + rat.phenotype.spotted +
                " materialAudit=deferred-to-RatVisualFactory");
            Save.rats.Add(rat);
            Save.ratIds.Add(rat.id);
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = rat.id;
            selectedObjectId = null;
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            deleteConfirmationRatId = null;
                StatusMessage = "Spawned " + ColonyFactory.DisplayName(rat) + " with genotype " + DeveloperGeneSummary(rat.genotype) + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void SetSimulationSpeed(float speed)
        {
            if (Save == null || Save.clock == null) return;
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            float normalized = GrowthSystem.NormalizeSpeed(speed);
            Save.clock.speed = normalized;
            // Apply the presentation multiplier in the same action as the
            // persisted clock change so movement and animations respond on
            // the very next frame instead of waiting for a second clock tick.
            GrowthSystem.SetRuntimeSpeed(normalized);
            SaveSystem.Save(Save);
            Debug.Log("[Rat Movement] " + MovementDiagnostics);
            if (ui != null) ui.Refresh(true);
        }

        public int SellValue(RatData rat)
        {
            return StoreSystem.CalculateSaleValue(Save, rat, GameTime);
        }

        public bool CanSellRat(RatData rat)
        {
            return StoreSystem.CanSellRat(Save, rat, GameTime);
        }

        public string SaleRestrictionReason(RatData rat)
        {
            return StoreSystem.SaleRestrictionReason(Save, rat, GameTime);
        }

        public string SaleWarningsFor(RatData rat)
        {
            if (rat == null || Save == null) return "None";
            var warnings = new List<string>();
            bool hasActiveLitter = HasActiveLitter(rat.id);
            bool hasAnyLitter = HasAnyLitterHistory(rat.id);

            if (EnclosureSystem.IsPregnant(Save, rat)) warnings.Add("Pregnant");
            if (rat.nursing || EnclosureSystem.HasDependentPinkies(Save, rat.id)) warnings.Add("Nursing");
            if (rat.traits != null && rat.traits.health <= 40f) warnings.Add("Sick or injured");
            if (hasActiveLitter) warnings.Add("Has offspring");
            if (hasAnyLitter) warnings.Add("Parent of a litter");
            if (rat.enclosure == RatEnclosure.Breeding || rat.enclosure == RatEnclosure.Pairing)
                warnings.Add("Special habitat: " + EnclosureSystem.Label(rat.enclosure));

            string restriction = SaleRestrictionReason(rat);
            if (!string.IsNullOrEmpty(restriction) && !warnings.Contains(restriction)) warnings.Add(restriction);

            return warnings.Count == 0 ? "None" : string.Join(" • ", warnings.ToArray());
        }

        public string SelectedRatRemovalWarning
        {
            get
            {
                RatData rat = SelectedRat;
                if (rat == null || Save == null) return string.Empty;
                string warnings = SaleWarningsFor(rat);
                return warnings == "None" ? string.Empty : "Warnings: " + warnings + ".";
            }
        }

        private bool HasAnyLitterHistory(string ratId)
        {
            if (Save == null || string.IsNullOrEmpty(ratId) || Save.litters == null) return false;
            foreach (var litter in Save.litters)
            {
                if (litter != null && (litter.motherId == ratId || litter.fatherId == ratId)) return true;
            }
            return false;
        }

        private bool HasActiveLitter(string ratId)
        {
            if (Save == null || string.IsNullOrEmpty(ratId) || Save.litters == null) return false;
            foreach (var litter in Save.litters)
            {
                if (litter == null || (litter.motherId != ratId && litter.fatherId != ratId) || litter.pupIds == null) continue;
                foreach (var pupId in litter.pupIds)
                {
                    RatData pup = BreedingSystem.FindRat(Save, pupId);
                    if (pup != null && (pup.stage == RatStage.Pinkie || pup.stage == RatStage.YoungRat)) return true;
                }
            }
            return false;
        }

        public void RequestSellSelectedRat()
        {
            RatData rat = SelectedRat;
            if (rat == null)
            {
                StatusMessage = "Select a rat before selling it.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            RequestSellRat(rat.id);
        }

        public void RequestSellRat(string ratId)
        {
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            if (rat == null)
            {
                StatusMessage = "That rat is no longer available for sale.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            if (!CanSellRat(rat))
            {
                StatusMessage = SaleRestrictionReason(rat);
                if (ui != null) ui.Refresh(true);
                return;
            }
            sellConfirmationRatId = rat.id;
            euthanizeConfirmationRatId = null;
            StatusMessage = string.Empty;
            if (ui != null) ui.Refresh(true);
        }

        public void CancelSellSelectedRat()
        {
            sellConfirmationRatId = null;
            StatusMessage = "Sale cancelled.";
            if (ui != null) ui.Refresh(true);
        }

        public void ConfirmSellSelectedRat()
        {
            RatData rat = BreedingSystem.FindRat(Save, sellConfirmationRatId);
            if (rat == null || rat.id != sellConfirmationRatId)
            {
                sellConfirmationRatId = null;
                StatusMessage = "The selected rat changed; sale cancelled.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            if (!CanSellRat(rat))
            {
                sellConfirmationRatId = null;
                StatusMessage = SaleRestrictionReason(rat);
                if (ui != null) ui.Refresh(true);
                return;
            }
            int dollars = SellValue(rat);
            string name = ColonyFactory.DisplayName(rat);
            if (!RetireActiveRat(rat, RatRemovalDisposition.Sold, true)) return;
            StatusMessage = name + " was sold for $" + dollars + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void RequestEuthanizeSelectedRat()
        {
            RatData rat = SelectedRat;
            if (rat == null)
            {
                StatusMessage = "Select a rat before euthanasia.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            if (Save.colonyCredits < GameConfig.EuthanasiaCostDollars)
            {
                StatusMessage = "Not enough dollars. Euthanasia costs $" + GameConfig.EuthanasiaCostDollars + ".";
                if (ui != null) ui.Refresh(false);
                return;
            }
            euthanizeConfirmationRatId = rat.id;
            sellConfirmationRatId = null;
            StatusMessage = "FINAL CONFIRMATION: permanently euthanize " + ColonyFactory.DisplayName(rat) + " for $" + GameConfig.EuthanasiaCostDollars + "? This cannot be undone. " + SelectedRatRemovalWarning;
            if (ui != null) ui.Refresh(true);
        }

        public void CancelEuthanizeSelectedRat()
        {
            euthanizeConfirmationRatId = null;
            StatusMessage = "Euthanasia cancelled.";
            if (ui != null) ui.Refresh(true);
        }

        public void ConfirmEuthanizeSelectedRat()
        {
            RatData rat = SelectedRat;
            if (rat == null || rat.id != euthanizeConfirmationRatId)
            {
                euthanizeConfirmationRatId = null;
                StatusMessage = "The selected rat changed; euthanasia cancelled.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            string name = ColonyFactory.DisplayName(rat);
            if (!RetireActiveRat(rat, RatRemovalDisposition.Euthanized, false)) return;
            Save.colonyCredits = Mathf.Max(0, Save.colonyCredits - GameConfig.EuthanasiaCostDollars);
            StatusMessage = name + " was euthanized for $" + GameConfig.EuthanasiaCostDollars + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        private bool RetireActiveRat(RatData rat, RatRemovalDisposition disposition, bool awardCredits)
        {
            if (disposition == RatRemovalDisposition.Sold && !CanSellRat(rat))
            {
                StatusMessage = SaleRestrictionReason(rat);
                sellConfirmationRatId = null;
                if (ui != null) ui.Refresh(true);
                return false;
            }
            if (Save == null || rat == null || !Save.rats.Remove(rat)) return false;
            int saleDollars = awardCredits ? SellValue(rat) : 0;
            BreedingSystem.CancelPregnanciesForRat(Save, rat.id, GameTime);
            BreedingSystem.CancelDedicatedSessionsForRat(Save, rat.id, GameTime);
            rat.removalDisposition = disposition;
            rat.removedAt = GameTime;
            rat.reproductiveState = ReproductiveState.Infertile;
            rat.pregnancyId = null;
            string removalKey = disposition == RatRemovalDisposition.Sold ? "sold" :
                disposition == RatRemovalDisposition.Euthanized ? "euthanized" : "deceased";
            string removalLabel = disposition == RatRemovalDisposition.Sold ? "Sold" :
                disposition == RatRemovalDisposition.Euthanized ? "Euthanized" : "Deceased";
            string removalMessage = disposition == RatRemovalDisposition.Sold ? "Sold" :
                disposition == RatRemovalDisposition.Euthanized ? "Euthanized" : "Removed";
            RatActivitySystem.SetCurrent(Save, rat, removalKey, removalLabel, GameTime, removalMessage);
            Save.retiredRats.Add(rat);
            if (awardCredits)
            {
                Save.colonyCredits += saleDollars;
                Save.lifetimeSaleCredits += saleDollars;
            }
            selectedRatIds.Remove(rat.id);
            if (selectedRatId == rat.id) selectedRatId = null;
            selectedObjectId = null;
            sellConfirmationRatId = null;
            euthanizeConfirmationRatId = null;
            deleteConfirmationRatId = null;
            if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            EnclosureSystem.ClearBreedingPair();
            RestoreDedicatedBreedingPair();
            EnclosureSystem.RecalculateAssignments(Save);
            return true;
        }

        public void RequestDeleteSelectedRat()
        {
            var selected = SelectedRat;
            if (selected == null)
            {
                StatusMessage = "Select a rat before deleting it.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            deleteConfirmationRatId = selected.id;
            StatusMessage = "Confirm deletion of " + ColonyFactory.DisplayName(selected) + ". Pending pregnancies will be cancelled safely; completed litter history will remain.";
            if (ui != null) ui.Refresh(true);
        }

        public void CancelDeleteSelectedRat()
        {
            deleteConfirmationRatId = null;
            StatusMessage = "Rat deletion cancelled.";
            if (ui != null) ui.Refresh(true);
        }

        public void ConfirmDeleteSelectedRat()
        {
            var selected = SelectedRat;
            if (selected == null || string.IsNullOrEmpty(deleteConfirmationRatId) || selected.id != deleteConfirmationRatId)
            {
                deleteConfirmationRatId = null;
                StatusMessage = "The selected rat changed; deletion was cancelled.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            string removedName = ColonyFactory.DisplayName(selected);
            int cancelledPregnancies = CountPendingPregnanciesFor(selected.id);
            if (!RetireActiveRat(selected, RatRemovalDisposition.Deleted, false))
            {
                StatusMessage = "The selected rat could not be removed.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            parentAId = null;
            parentBId = null;
            breedingOpen = false;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            StatusMessage = "Deleted " + removedName + "." + (cancelledPregnancies == 0 ? string.Empty : " Cancelled " + cancelledPregnancies + " pending pregnancy" + (cancelledPregnancies == 1 ? "." : "ies."));
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void RequestFullReset()
        {
            resetConfirmationPending = true;
            deleteConfirmationRatId = null;
            sellConfirmationRatId = null;
            euthanizeConfirmationRatId = null;
            StatusMessage = "Full reset requested. This clears the local save and recreates the default colony.";
            if (ui != null) ui.Refresh(true);
        }

        public void CancelFullReset()
        {
            resetConfirmationPending = false;
            StatusMessage = "Full reset cancelled.";
            if (ui != null) ui.Refresh(true);
        }

        public void ConfirmFullReset()
        {
            if (!resetConfirmationPending)
            {
                StatusMessage = "Request the full reset first.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            resetConfirmationPending = false;
            deleteConfirmationRatId = null;
            sellConfirmationRatId = null;
            euthanizeConfirmationRatId = null;
            groupMoveConfirmationPending = false;
            pairingMoveAllConfirmationPending = false;
            selectedRatIds.Clear();
            multipleSelectionMode = false;
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            selectedRatId = null;
            selectedObjectId = null;
            saveTimer = 0f;
            SaveSystem.DeleteLocalSave();

            // Clear live presentation roots before replacing the in-memory
            // object so no stale rats or selection rings survive the reset.
            if (rats != null) rats.Render(null, habitat == null ? Vector3.zero : habitat.NestPosition);
            Save = ColonyFactory.CreateNew(GameConfig.NowMs());
            BrowserWakeLockSystem.Initialize(Save);
            if (habitat != null)
            {
                habitat.Rebuild(Save);
            }
            if (rats != null && habitat != null) rats.Render(Save, habitat.NestPosition);
            SaveSystem.Save(Save);
            StatusMessage = "Game fully reset. A new randomized starter pair, habitat, clock, genetics, and UI state were restored.";
            if (ui != null)
            {
                ui.CloseTransientPanels();
                ui.OpenWelcomeForNewGame();
            }
        }

        public void DismissWelcomePopup()
        {
            if (Save == null) return;
            Save.welcomePopupPending = false;
            if (Save.clock != null) Save.clock.lastRealTimestamp = GameConfig.NowMs();
            GrowthSystem.SetSimulationPaused(false);
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            SaveSystem.Save(Save);
        }

        public void ToggleKeepScreenAwake()
        {
            if (Save == null) return;
            bool enabled = !KeepScreenAwakeEnabled;
            BrowserWakeLockSystem.SetPreference(Save, enabled, enabled);
            StatusMessage = enabled
                ? "Keep Screen Awake enabled."
                : "Keep Screen Awake disabled.";
            SaveSystem.Save(Save);
            if (ui != null) ui.RefreshWakeLockControls();
        }

        public void RequestScreenWakeLockFromUserGesture()
        {
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            if (ui != null) ui.RefreshWakeLockControls();
        }

        public void ServiceSelectedObject()
        {
            var selected = SelectedObject;
            if (selected == null) return;
            selected.condition = 100f;
            selected.lastServicedAt = GameTime;
            habitat.UpdateObjectCondition(selected.id, selected.condition);
            StatusMessage = selected.label + " is ready.";
            SaveSystem.Save(Save);
            if (ui != null) ui.Refresh(true);
        }

        public void SaveNow()
        {
            SaveSystem.Save(Save);
            StatusMessage = "Colony saved locally.";
            if (ui != null) ui.Refresh(false);
        }

        private HabitatObjectData FindObject(string id)
        {
            if (Save == null || string.IsNullOrEmpty(id)) return null;
            foreach (var item in Save.habitatObjects)
            {
                if (item != null && item.id == id) return item;
            }
            return null;
        }

        private RatData FindRatByName(string name)
        {
            if (Save == null || string.IsNullOrEmpty(name)) return null;
            foreach (var rat in Save.rats)
            {
                if (rat != null && rat.name == name) return rat;
            }
            return null;
        }

        private static string DeveloperGeneSummary(GenotypeData genotype)
        {
            if (genotype == null) return "B/B C/C D/D s/s";
            return GeneticsSystem.FormatPair(genotype, "B") + " " +
                GeneticsSystem.FormatPair(genotype, "C") + " " +
                GeneticsSystem.FormatPair(genotype, "D") + " " +
                GeneticsSystem.FormatPair(genotype, "S");
        }

        private int CountPendingPregnanciesFor(string ratId)
        {
            if (Save == null || string.IsNullOrEmpty(ratId)) return 0;
            int count = 0;
            foreach (var pregnancy in Save.pregnancies)
            {
                if (pregnancy != null && pregnancy.status == "pending" &&
                    (pregnancy.motherId == ratId || pregnancy.fatherId == ratId)) count++;
            }
            return count;
        }

        private void RefreshWorldAndUi(bool forceUi)
        {
            if (rats != null && habitat != null) rats.Render(Save, habitat.NestPosition);
            if (ui != null) ui.Refresh(forceUi);
        }

        private void ConfigureCamera()
        {
            SceneVisibilityGuard.DisableBuiltInPhysicsRaycasters(mainCamera);
            mainCamera.enabled = true;
            mainCamera.gameObject.tag = "MainCamera";
            mainCamera.orthographic = true;
            mainCamera.orthographicSize = GameConfig.CameraDefaultOrthographicSize;
            mainCamera.nearClipPlane = 0.1f;
            mainCamera.farClipPlane = 100f;
            // The animation showcase preview layer stays out of the live
            // camera so its track motion cannot appear in the habitat. The
            // selected-rat profile is a transparent UI cutout over this same
            // Main Camera, so the live rat remains visible without a second
            // camera, layer swap, or duplicate visual.
            mainCamera.cullingMask = ~(1 << RatAnimationShowcasePreviewLayer);
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.025f, 0.075f, 0.11f);
            UpdateWorldViewport();
            mainCamera.targetTexture = null;
            mainCamera.useOcclusionCulling = false;
            // Lower three-quarter page framing. Every page uses the same
            // full-size enclosure footprint; navigation shifts the camera
            // horizontally between the independent cages.
            normalCameraPosition = new Vector3(0f, 10.25f, -19.0f);
            normalCameraLookTarget = new Vector3(0f, 0.25f, -2.85f);
            normalCameraOrthographicSize = GameConfig.CameraDefaultOrthographicSize;
            mainCamera.transform.position = normalCameraPosition;
            mainCamera.transform.LookAt(normalCameraLookTarget);
            normalCameraRotation = mainCamera.transform.rotation;
            normalCameraDistance = Vector3.Distance(normalCameraPosition, normalCameraLookTarget);
            cameraPresentationReady = true;
        }

        private void UpdateWorldViewport()
        {
            if (mainCamera == null || Screen.width <= 0 || Screen.height <= 0) return;

            // The UI Canvas is height-scaled against the 540x960 mobile
            // reference. Derive the world reservation from that same scale so
            // the camera continues to line up when the browser is resized or
            // moved between portrait and landscape displays.
            float uiScale = Mathf.Max(0.5f, Screen.height / 960f);
            float reservedPixels = TopNavigationReservedReferenceHeight * uiScale;
            float reserved = Mathf.Clamp01(reservedPixels / Screen.height);
            Rect desired = new Rect(0f, 0f, 1f, 1f - reserved);
            Rect current = mainCamera.rect;
            if (Mathf.Abs(current.yMax - desired.yMax) > 0.0001f ||
                Mathf.Abs(current.width - desired.width) > 0.0001f)
            {
                mainCamera.rect = desired;
                cameraZoomVelocity = 0f;
            }
        }

        private void UpdateCameraPresentation()
        {
            if (mainCamera == null || !cameraPresentationReady) return;

            Vector3 desiredPosition = normalCameraPosition;
            Quaternion desiredRotation = normalCameraRotation;
            float desiredSize = normalCameraOrthographicSize;
            var selected = SelectedRat;
            Transform ratRoot;
            Vector3 selectedFocusPoint;
            if (cameraFollowSelectedRat && selected != null &&
                TryGetSelectedRatFocus(selected, out ratRoot, out selectedFocusPoint))
            {
                bool selectedPinkie = selected.stage == RatStage.Pinkie;
                float verticalOffset = selectedPinkie
                    ? PinkieSelectedRatCameraVerticalOffset
                    : SelectedRatCameraVerticalOffset;
                Vector3 focusPoint = selectedFocusPoint + Vector3.up * verticalOffset;
                if (cameraView == HabitatCameraView.Pairing &&
                    selected.enclosure == RatEnclosure.Pairing &&
                    selected.stage == RatStage.Pinkie)
                {
                    focusPoint += Vector3.up * PairingPinkieCameraLookTargetDownwardOffset;
                }
                Vector3 cameraForward = normalCameraRotation * Vector3.forward;
                desiredPosition = focusPoint - cameraForward * normalCameraDistance;
                desiredSize = normalCameraOrthographicSize * (selectedPinkie
                    ? PinkieInspectionOrthographicMultiplier
                    : InspectionOrthographicMultiplier);
            }
            else
            {
                RatEnclosure enclosure;
                switch (cameraView)
                {
                    case HabitatCameraView.MaleEnclosure: enclosure = RatEnclosure.MaleColony; break;
                    case HabitatCameraView.Breeding: enclosure = RatEnclosure.Breeding; break;
                    case HabitatCameraView.Pairing: enclosure = RatEnclosure.Pairing; break;
                    default: enclosure = RatEnclosure.FemaleColony; break;
                }
                Vector3 frameTarget;
                float frameSize;
                CalculateEnclosureFrame(enclosure, out frameTarget, out frameSize);
                Vector3 cameraForward = normalCameraRotation * Vector3.forward;
                desiredPosition = frameTarget - cameraForward * normalCameraDistance;
                desiredSize = frameSize;
            }

            desiredSize = Mathf.Clamp(
                desiredSize + habitatZoomOffset,
                MinimumZoomForCurrentView(selected),
                GameConfig.CameraMaximumOrthographicSize);

            float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            mainCamera.transform.position = Vector3.SmoothDamp(
                mainCamera.transform.position,
                desiredPosition,
                ref cameraMoveVelocity,
                CameraFocusSmoothTime,
                Mathf.Infinity,
                deltaTime);
            float rotationBlend = 1f - Mathf.Exp(-deltaTime / CameraFocusSmoothTime);
            mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, desiredRotation, rotationBlend);
            mainCamera.orthographicSize = Mathf.SmoothDamp(
                mainCamera.orthographicSize,
                desiredSize,
                ref cameraZoomVelocity,
                CameraZoomSmoothTime,
                Mathf.Infinity,
                deltaTime);
        }

        private bool TryGetSelectedRatFocus(RatData selected, out Transform ratRoot, out Vector3 focusPoint)
        {
            ratRoot = null;
            focusPoint = Vector3.zero;
            if (selected == null || string.IsNullOrEmpty(selected.id)) return false;

            if (rats != null) rats.TryGetRatRoot(selected.id, out ratRoot);
            if (ratRoot == null)
            {
                // Keep camera focus working even if a UI refresh or stage
                // reconciliation temporarily replaced the presenter's lookup
                // entry. The stable SelectableEntity remains on the same live
                // rat root and is not a duplicate visual.
                var entities = UnityEngine.Object.FindObjectsOfType<SelectableEntity>(true);
                for (int i = 0; i < entities.Length; i++)
                {
                    SelectableEntity entity = entities[i];
                    if (entity != null && entity.kind == SelectableKind.Rat && entity.entityId == selected.id)
                    {
                        ratRoot = entity.transform;
                        break;
                    }
                }
            }

            if (ratRoot == null) return false;

            var controller = ratRoot.GetComponent<RatVisualController>();
            Bounds localBounds;
            if (controller != null && controller.TryGetSelectionBounds(out localBounds))
            {
                focusPoint = ratRoot.TransformPoint(localBounds.center);
                return true;
            }

            float fallbackHeight = selected.stage == RatStage.Pinkie ? 0.32f : 0.72f;
            focusPoint = ratRoot.position + Vector3.up * fallbackHeight;
            return true;
        }

        public void ZoomIn()
        {
            AdjustHabitatZoom(HabitatZoomButtonStep);
        }

        public void ZoomOut()
        {
            AdjustHabitatZoom(-HabitatZoomButtonStep);
        }

        public void AdjustHabitatZoom(float amount)
        {
            if (!cameraPresentationReady || Mathf.Abs(amount) < 0.0001f) return;
            // Positive input means zoom in for the mouse wheel, pinch, and the
            // visible + button. Store an additive size offset because the
            // camera presentation recomputes its view frame every frame.
            // Limit one input update to one small shared step so wheel,
            // trackpad, pinch, and buttons all have the same gentle response.
            float appliedAmount = Mathf.Clamp(amount, -HabitatZoomInputStep, HabitatZoomInputStep);
            habitatZoomOffset = Mathf.Clamp(
                habitatZoomOffset - appliedAmount,
                -HabitatZoomOffsetLimit,
                HabitatZoomOffsetLimit);
            // Keep the SmoothDamp velocity so consecutive small inputs blend
            // into the existing camera motion instead of restarting the
            // interpolation on every wheel or pinch sample.
        }

        private float MinimumZoomForCurrentView(RatData selectedRat)
        {
            if (selectedRat != null)
            {
                return selectedRat.stage == RatStage.Pinkie
                    ? PinkieSelectedRatMinimumZoom
                    : SelectedRatMinimumZoom;
            }
            return EnclosureMinimumZoom;
        }

        private void CalculateEnclosureFrame(RatEnclosure enclosure, out Vector3 target, out float orthographicSize)
        {
            EnclosureSystem.Definition definition = EnclosureSystem.GetDefinition(enclosure);
            target = new Vector3(definition.Center.x, 0.72f, definition.Center.z);

            Vector3 right = normalCameraRotation * Vector3.right;
            Vector3 up = normalCameraRotation * Vector3.up;
            float halfWidth = 0f;
            float halfHeight = 0f;
            // Frame the complete cage including the wall tops. This keeps the
            // selected-cage view stable and prevents panning/zooming from
            // drifting outside the active enclosure presentation.
            float[] heights = { 0f, 2.35f };
            for (int xIndex = 0; xIndex < 2; xIndex++)
            {
                float x = xIndex == 0 ? definition.minX - 0.14f : definition.maxX + 0.14f;
                for (int zIndex = 0; zIndex < 2; zIndex++)
                {
                    float z = zIndex == 0 ? definition.minZ - 0.14f : definition.maxZ + 0.14f;
                    for (int yIndex = 0; yIndex < heights.Length; yIndex++)
                    {
                        Vector3 delta = new Vector3(x, heights[yIndex], z) - target;
                        halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(delta, right)));
                        halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(delta, up)));
                    }
                }
            }

            float aspect = mainCamera == null ? 0.5625f : Mathf.Max(0.1f, mainCamera.aspect);
            const float padding = 0.55f;
            orthographicSize = Mathf.Max(halfHeight + padding, (halfWidth + padding) / aspect);
            // Avoid a very tight frame on unusual editor aspect ratios while
            // still making a single cage substantially larger than Overview.
            // The lower bound prevents a loose frame on very wide screens.
            // Do not cap the upper bound at the phone reference size: a narrow
            // browser or tall tablet needs a little more vertical view to keep
            // the entire enclosure width inside the camera without cropping.
            orthographicSize = Mathf.Clamp(orthographicSize, 6.4f,
                Mathf.Max(normalCameraOrthographicSize, GameConfig.CameraMaximumOrthographicSize));
        }

        private void ReportInputDiagnostic(string message)
        {
            // Interaction diagnostics stay in the Unity Console through
            // InteractionManager. Keep the normal header focused on concise
            // player-facing state such as Ready or Selected <rat>.
        }

        private static void EnsureEventSystem()
        {
            var eventSystems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
            var eventSystem = EventSystem.current;
            if (eventSystem == null || !eventSystem.gameObject.activeInHierarchy)
            {
                eventSystem = null;
                foreach (var candidate in eventSystems)
                {
                    if (candidate != null && candidate.gameObject.activeInHierarchy)
                    {
                        eventSystem = candidate;
                        break;
                    }
                }
                if (eventSystem == null && eventSystems.Length > 0) eventSystem = eventSystems[0];
            }
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            // Exactly one EventSystem should dispatch UI pointer events. A
            // duplicate can produce duplicate button callbacks and confusing
            // pointer ownership even when the world raycast is correct.
            foreach (var candidate in eventSystems)
            {
                if (candidate == null || candidate == eventSystem) continue;
                candidate.enabled = false;
                UnityEngine.Object.Destroy(candidate.gameObject);
            }
            eventSystem.gameObject.SetActive(true);
            eventSystem.enabled = true;

            // Keep the project's existing legacy input setting. Only install a
            // module when the scene has no UI input module at all, preventing
            // duplicate-module console errors while making runtime UI buttons
            // work in a clean scene.
            var modules = eventSystem.GetComponents<BaseInputModule>();
            BaseInputModule selectedModule = null;
            foreach (var module in modules)
            {
                if (module != null && module.isActiveAndEnabled)
                {
                    selectedModule = module;
                    break;
                }
            }
            if (selectedModule == null && modules.Length > 0) selectedModule = modules[0];
            if (selectedModule == null)
            {
                selectedModule = eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
            selectedModule.enabled = true;
            foreach (var module in modules)
            {
                if (module == null || module == selectedModule) continue;
                module.enabled = false;
                UnityEngine.Object.Destroy(module);
            }
            Debug.Log("[Rat Habitat] EventSystem ready: exactly one EventSystem and one compatible UI input module.");
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveSystem.Save(Save);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) SaveSystem.Save(Save);
        }

        private void OnDestroy()
        {
            if (pairingCheckRoutine != null) StopCoroutine(pairingCheckRoutine);
        }

        private void OnApplicationQuit()
        {
            SaveSystem.Save(Save);
        }
    }
}
