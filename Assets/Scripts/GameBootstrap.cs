using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RatHabitat
{
    /// <summary>
    /// Runtime-only ownership of each tank's shared delivery approach point.
    /// Pregnancy records remain authoritative; this prevents concurrent live
    /// approaches from stacking on that point. The reservation is released as
    /// soon as a birth completes and never excludes nursing mothers or litters
    /// from the shared nest.
    /// </summary>
    public sealed class BirthQueueReservations
    {
        private readonly Dictionary<RatEnclosure, string> occupants =
            new Dictionary<RatEnclosure, string>();

        public int Count { get { return occupants.Count; } }

        public bool TryGetOccupant(RatEnclosure enclosure, out string motherId)
        {
            return occupants.TryGetValue(enclosure, out motherId);
        }

        public bool IsReservedBy(RatEnclosure enclosure, string motherId)
        {
            return !string.IsNullOrEmpty(motherId) && occupants.TryGetValue(enclosure, out string occupant) &&
                string.Equals(occupant, motherId, StringComparison.Ordinal);
        }

        public bool TryReserve(RatEnclosure enclosure, string motherId)
        {
            if (string.IsNullOrEmpty(motherId)) return false;
            if (occupants.TryGetValue(enclosure, out string occupant))
                return string.Equals(occupant, motherId, StringComparison.Ordinal);
            occupants.Add(enclosure, motherId);
            return true;
        }

        public bool Release(RatEnclosure enclosure, string motherId)
        {
            if (!IsReservedBy(enclosure, motherId)) return false;
            occupants.Remove(enclosure);
            return true;
        }

        public void Clear()
        {
            occupants.Clear();
        }
    }

    public enum DeveloperRatPreset
    {
        SolidBlack,
        SolidBrown,
        DilutedBlack,
        DilutedBrown,
        Albino,
        ForceWhiteAlbinoPreview,
        SpottedBlack,
        SpottedBrown,
        MarkedTest,
        MarkingMutationTest,
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
        // A failed nest route/presentation is retried on a short realtime
        // cooldown. A mother already walking to the nest is polled cheaply for
        // arrival, but must not trigger a full colony maintenance scan each frame.
        private readonly Dictionary<string, float> birthRetryAfterRealtime = new Dictionary<string, float>();
        private const float BirthRetryCooldownSeconds = 0.5f;
        private const float BirthApproachStallTimeoutRealtimeSeconds = 12f;
        private const float BirthApproachMaximumRealtimeSeconds = 90f;
        private const float BirthApproachProgressDistance = 0.035f;
        private readonly BirthQueueReservations birthQueueReservations = new BirthQueueReservations();
        private readonly Dictionary<string, BirthApproachWatchdog> birthApproachWatchdogs =
            new Dictionary<string, BirthApproachWatchdog>(StringComparer.Ordinal);
        private readonly List<string> staleBirthApproachIds = new List<string>();
        private bool birthQueueMaintenancePending;
        // Reconcile a visible mother with stale presenter indexes on retry.
        private bool birthPresentationRepairRequested;
        private int birthQueueRouteFailures;
        private int birthQueueRouteTimeouts;
        private int birthQueueSuccessfulBirths;
        private int birthQueueBlockedBirths;
        private int birthQueueBlockedCapacity;
        private int birthQueueBlockedNest;
        private int birthQueueBlockedPresentation;
        private int birthQueueBlockedRoute;
        private int birthQueueBlockedOther;
        // Full-colony reconciliation is daily or deadline-driven. All age,
        // pregnancy, recovery, restock, and nursing state is evaluated against
        // the current authoritative game timestamp; skipped intervals are
        // coalesced rather than replayed.
        private const long ColonyMaintenanceIntervalGameMs = GameConfig.GameDayMs;
        private const float AmbientActivityPollIntervalSeconds = 0.25f;
        private const float AutosaveIntervalSeconds = 5f;
        private long nextColonyMaintenanceGameTime;
        private long nextAgeTransitionGameTime = long.MaxValue;
        private long nextNursingTickGameTime = long.MaxValue;
        private long nextWeaningAnnouncementGameTime = long.MaxValue;
        private bool colonyMaintenanceInitialized;
        private bool ageTransitionScheduleInitialized;
        private bool nursingScheduleInitialized;
        private bool weaningScheduleInitialized;
        private struct TimedSimulationWorkDue
        {
            public bool StoreRestock;
            public bool BreedingSession;
            public bool Pregnancy;
            public bool Recovery;
            public bool Weaning;

            public bool Any
            {
                get { return StoreRestock || BreedingSession || Pregnancy || Recovery || Weaning; }
            }
        }
        private bool performanceSimulationPauseActive;
        private long performanceSimulationPauseStartedAt;
        private float ambientActivityPollTimer;
        private float wakeLockPollTimer;
        private int maintenancePassCount;
        private float lastMaintenanceDurationMs;
        private float lastUiRefreshDurationMs;
        private float lastViewportFrameDurationMs;
        private float lastClockFrameDurationMs;
        private float lastMovementFrameDurationMs;
        private float lastBootstrapUpdateDurationMs;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float performanceMaintenanceWindowMs;
        private float performanceUiRefreshWindowMs;
#endif
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
        private readonly Dictionary<string, int> ratNameRandomizeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
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
        private Vector3 habitatZoomFocusOffset;
        private readonly TankCameraPanState tankCameraPanState = new TankCameraPanState();
        private bool cameraPresentationReady;
        private bool cameraFocusNest;
        private RatEnclosure cameraFocusNestEnclosure = RatEnclosure.Pairing;
        // Habitat navigation starts on one full-size enclosure. Overview is
        // retained in the enum for save/backward compatibility, but is no
        // longer a player-facing combined four-cage presentation.
        // Pairing Habitat is the main landing page. The other full-size
        // enclosures remain available through the left/right carousel.
        private HabitatCameraView cameraView = HabitatCameraView.Pairing;
        private bool cameraFollowSelectedRat;
        private Coroutine pairingCheckRoutine;
        private long lastPairingEligibilityScanGameTime;
        private bool pairingEligibilityScanInitialized;
        private PairingApproachRuntime pairingApproach;
        private bool pairingHeaderRefreshPending;
        private float pairingHeaderRefreshDueAt;
        private float pairingRetryNotBeforeRealtime;
        private const float PairingHeaderRefreshDelaySeconds = 0.25f;
        private const float PairingRouteRetryBaseDelaySeconds = 0.25f;
        private const float PairingRouteRetryMaxDelaySeconds = 2f;
        private const int MaximumEventLogEntries = 10;
        // Generated profile buttons can be rebuilt while the same pointer-up
        // event is still being processed. Keep the explicit move action from
        // being followed by the newly-created Remove button on that same
        // input sequence.
        private string lastPairingMoveRatId;
        private int lastPairingMoveFrame = -1;
        private const int MaximumEventMessageLength = 64;
        private string statusMessage;
        private readonly List<string> staleGroupSelectionIds = new List<string>();

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
            public float stalledSeconds;
            public Vector3 lastMalePosition;
            public Vector3 lastFemalePosition;
            public bool femaleFertileWindowCommitted;
            public bool femaleWindowRecoveryRecorded;
            public float pairingSpeed;
        }

        private sealed class BirthApproachWatchdog
        {
            public PregnancyData pregnancy;
            public RatData mother;
            public string motherId;
            public RatEnclosure enclosure;
            public float elapsedRealtime;
            public float stalledRealtime;
            public Vector3 lastPosition;
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
        private const float HabitatZoomInOffsetLimit = 14f;
        private const float HabitatZoomOutOffsetLimit = 8f;
        private const float OverviewMinimumZoom = 7.5f;
        private const float EnclosureMinimumZoom = 2.4f;
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
        public string BirthQueueDiagnostics { get { return BuildBirthQueueDiagnostics(); } }
        private string liveEventMessage;
        private string liveEventCategory;
        private long liveEventExpiresAt;
        private string cachedEventLogSignature;
        private int cachedEventLogCount = -1;
        private long cachedEventLogTime = long.MinValue;
        private string cachedEventLogMessage;
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
                if (!EventLogPolicy.IsCategoryEnabled(Save, liveEventCategory ??
                    EventLogPolicy.CategoryForMessage(liveEventMessage)))
                {
                    liveEventMessage = null;
                    liveEventCategory = null;
                    liveEventExpiresAt = 0L;
                    return string.Empty;
                }
                if (GameTime >= liveEventExpiresAt)
                {
                    liveEventMessage = null;
                    liveEventCategory = null;
                    liveEventExpiresAt = 0L;
                    return string.Empty;
                }
                return StripSexSymbolsFromEventMessage(liveEventMessage);
            }
        }
        public string LatestEnabledEventMessage
        {
            get
            {
                if (Save == null || Save.eventLog == null) return string.Empty;
                for (int index = 0; index < Save.eventLog.Count; index++)
                {
                    ColonyEventData entry = Save.eventLog[index];
                    if (EventLogPolicy.IsAlertEnabled(Save, entry))
                        return StripSexSymbolsFromEventMessage(entry.message);
                }
                return string.Empty;
            }
        }
        public bool AllAlertCategoriesDisabled
        {
            get { return EventLogPolicy.AllCategoriesDisabled(Save); }
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
                int count = Save.eventLog.Count;
                long gameTime = latest == null ? 0L : latest.gameTimeMs;
                string message = latest == null ? string.Empty :
                    StripSexSymbolsFromEventMessage(latest.message);
                if (count == cachedEventLogCount && gameTime == cachedEventLogTime &&
                    string.Equals(message, cachedEventLogMessage, StringComparison.Ordinal))
                    return cachedEventLogSignature ?? string.Empty;

                cachedEventLogCount = count;
                cachedEventLogTime = gameTime;
                cachedEventLogMessage = message;
                cachedEventLogSignature = count + ":" + gameTime + ":" + message;
                return cachedEventLogSignature;
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
        public int PairingHabitatCapacity { get { return UpgradeSystem.PairingHabitatCapacity(Save); } }
        public int PairingHabitatCount { get { return CountPairingHabitatRats(); } }
        public int PairingHabitatCapacityUpgradeCost
        {
            get { return UpgradeSystem.PairingHabitatCapacityUpgradeCost(Save); }
        }
        public int ForSaleHabitatCount { get { return EnclosureSystem.CountForSaleRats(Save); } }
        public int ForSaleHabitatCapacity { get { return UpgradeSystem.ColonyCapacity(Save); } }
        public float SimulationSpeed { get { return Save == null || Save.clock == null ? 1f : GrowthSystem.NormalizeSpeed(Save.clock.speed); } }
        public string SimulationSpeedLabel { get { return ((int)SimulationSpeed) + "×"; } }
        public string MovementDiagnostics { get { return RatHabitatBehavior.GetMovementDiagnosticReadout(); } }
        public string SimulationPerformanceDiagnostics
        {
            get
            {
                return "Speed: " + SimulationSpeed.ToString("0.#") + "x | clock: " +
                    GrowthSystem.GameSecondsPerRealSecond(SimulationSpeed).ToString("0.###") +
                    " game s/real s (" + GrowthSystem.RealSecondsPerGameHour(SimulationSpeed).ToString("0.###") +
                    " real s/game h) | rat movement multiplier: " +
                    GrowthSystem.RuntimeSimulationMultiplier.ToString("0.#") + "x | steps: " +
                    GrowthSystem.LastSimulationStepCount + " | compressed visual actions: " +
                    GrowthSystem.LastCompressedVisualActionCount + " | " +
                    "Maintenance passes: " + maintenancePassCount +
                    "  last " + lastMaintenanceDurationMs.ToString("0.00") + " ms" +
                    "  UI " + lastUiRefreshDurationMs.ToString("0.00") + " ms" +
                    "  frame viewport " + lastViewportFrameDurationMs.ToString("0.00") +
                    " ms clock " + lastClockFrameDurationMs.ToString("0.00") +
                    " ms movement " + lastMovementFrameDurationMs.ToString("0.00") + " ms" +
                    "  tick: " + (ColonyMaintenanceIntervalGameMs / (60L * 1000L)) + " game minutes";
            }
        }
        public string PerformanceSimulationSummary
        {
            get
            {
                return SimulationSpeedLabel + " clock=" +
                    GrowthSystem.GameSecondsPerRealSecond(SimulationSpeed).ToString("0.##") +
                    " game-s/real-s; steps=" + GrowthSystem.LastSimulationStepCount +
                    "; maintenance=" + lastMaintenanceDurationMs.ToString("0.00") + "ms" +
                    "; UI=" + lastUiRefreshDurationMs.ToString("0.00") + "ms" +
                    "; frame work=" + lastBootstrapUpdateDurationMs.ToString("0.00") + "ms";
            }
        }
        public float LastMaintenanceDurationMs { get { return lastMaintenanceDurationMs; } }
        public float LastUiRefreshDurationMs { get { return lastUiRefreshDurationMs; } }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string PerformancePanelName { get { return ui == null ? "None" : ui.PerformancePanelName; } }
        public float ConsumePerformanceMaintenanceWindowMs()
        {
            float value = performanceMaintenanceWindowMs;
            performanceMaintenanceWindowMs = 0f;
            return value;
        }
        public float ConsumePerformanceUiRefreshWindowMs()
        {
            float value = performanceUiRefreshWindowMs;
            performanceUiRefreshWindowMs = 0f;
            return value;
        }
#endif
        public bool ResetConfirmationPending { get { return resetConfirmationPending; } }
        public bool WelcomePopupPending { get { return Save != null && Save.welcomePopupPending; } }
        public bool HasPendingLitterNaming
        {
            get { return PendingNamingLitter != null; }
        }
        public LitterData PendingNamingLitter
        {
            get
            {
                if (Save == null || Save.pendingNamingLitterIds == null) return null;
                for (int i = 0; i < Save.pendingNamingLitterIds.Count; i++)
                {
                    string litterId = Save.pendingNamingLitterIds[i];
                    if (string.IsNullOrEmpty(litterId)) continue;
                    foreach (LitterData litter in Save.litters)
                        if (litter != null && litter.id == litterId) return litter;
                }
                return null;
            }
        }
        public List<RatData> PendingNamingPups
        {
            get
            {
                var pups = new List<RatData>();
                LitterData litter = PendingNamingLitter;
                if (litter == null || litter.pupIds == null) return pups;
                foreach (string pupId in litter.pupIds)
                {
                    RatData pup = BreedingSystem.FindRat(Save, pupId);
                    if (pup != null) pups.Add(pup);
                }
                return pups;
            }
        }
        public bool CustomNamesClearConfirmationPending { get; private set; }
        public bool KeepScreenAwakeEnabled
        {
            get { return BrowserWakeLockSystem.IsEnabled(Save); }
        }
        public bool AutoNamePinkiesEnabled
        {
            get { return Save != null && Save.autoNamePinkies; }
        }
        public string KeepScreenAwakeStatusMessage
        {
            get { return BrowserWakeLockSystem.StatusMessage(Save); }
        }

        public bool IsAlertCategoryEnabled(string category)
        {
            return EventLogPolicy.IsCategoryEnabled(Save, category);
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
            return FormatSimulationTimestamp(entry.gameTimeMs) + "  " +
                StripSexSymbolsFromEventMessage(entry.message);
        }

        public string FormatSimulationTimestamp(long gameTimeMs)
        {
            return GameCalendar.FormatTimestamp(gameTimeMs);
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
            if (IsAuthoritativeActivityKey(key, rat))
                return RatActivitySystem.CurrentLabel(Save, rat, GameTime);
            RatActivityData persistedActivity = RatActivitySystem.Ensure(rat, GameTime);
            if (key == "movement" && persistedActivity.currentActivityAt == GameTime)
                return persistedActivity.currentActivityLabel;
            RatHabitatBehavior behavior;
            if (rats != null && rats.TryGetRatBehavior(rat.id, out behavior) && behavior != null)
                return behavior.ActivityLabel;
            return RatActivitySystem.CurrentLabel(Save, rat, GameTime);
        }

        private static bool IsAuthoritativeActivityKey(string key, RatData rat)
        {
            if (key == "growing")
                return rat != null && rat.removalDisposition == RatRemovalDisposition.None && rat.stage == RatStage.Pinkie;
            return key == "sold" || key == "euthanized" || key == "deceased" ||
                key == "breeding" || key == "pregnant" || key == "birth-approach" || key == "birth-waiting" ||
                key == "nursing" || key == "recovery";
        }

        private bool RefreshRatActivities()
        {
            if (Save == null) return false;
            bool changed = RatActivitySystem.RefreshAuthoritativeActivities(Save, GameTime);
            foreach (var rat in Save.rats)
            {
                if (rat == null || rat.removalDisposition != RatRemovalDisposition.None) continue;
                string authoritativeKey = RatActivitySystem.CurrentKey(Save, rat, GameTime);
                if (IsAuthoritativeActivityKey(authoritativeKey, rat)) continue;

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
            // Sex symbols remain part of normal rat identification everywhere
            // else. The global top-right event stream is intentionally more
            // conversational, so normalize them only at this event boundary.
            string compact = CompactEventMessage(StripSexSymbolsFromEventMessage(value));
            string category = EventLogPolicy.CategoryForMessage(compact);
            if (string.IsNullOrEmpty(category)) return false;

            Save.EnsureLists();
            Save.eventLog.Insert(0, new ColonyEventData
            {
                gameTimeMs = GameTime,
                message = compact,
                category = category,
            });
            while (Save.eventLog.Count > MaximumEventLogEntries)
                Save.eventLog.RemoveAt(Save.eventLog.Count - 1);

            // History is always retained for an approved category. The
            // preference only controls the transient top-screen alert.
            if (EventLogPolicy.IsCategoryEnabled(Save, category))
            {
                liveEventMessage = compact;
                liveEventCategory = category;
                liveEventExpiresAt = GameTime + LiveEventDurationGameMs();
            }
            return true;
        }

        private long LiveEventDurationGameMs()
        {
            double simulatedMillisecondsPerRealSecond =
                GrowthSystem.SimulationMillisecondsPerRealSecond(SimulationSpeed);
            long duration = (long)Math.Round(simulatedMillisecondsPerRealSecond * LiveEventDurationRealSeconds);
            return Math.Max(60L * 1000L, duration);
        }

        private void ShowTransientRestockSaleAlert(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            liveEventMessage = message;
            liveEventCategory = EventLogPolicy.Sale;
            liveEventExpiresAt = GameTime + LiveEventDurationGameMs();
        }

        private static string CompactEventMessage(string value)
        {
            string compact = value.Replace("\r", " ").Replace("\n", " ").Trim();
            while (compact.Contains("  ")) compact = compact.Replace("  ", " ");
            return compact.Length > MaximumEventMessageLength
                ? compact.Substring(0, MaximumEventMessageLength - 3) + "..."
                : compact;
        }

        private static string StripSexSymbolsFromEventMessage(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string message = value.Replace("♂", string.Empty).Replace("♀", string.Empty);
            while (message.Contains("  ")) message = message.Replace("  ", " ");
            return message.Trim();
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
                    breedingOpen ? "breeding" : "tank",
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RuntimePerformanceOverlay performanceOverlay = GetComponent<RuntimePerformanceOverlay>();
            if (performanceOverlay == null) performanceOverlay = gameObject.AddComponent<RuntimePerformanceOverlay>();
            performanceOverlay.Configure(this);
#endif
            GrowthSystem.SetSimulationPaused(false);
            Application.targetFrameRate = 60;
            try
            {
#if !UNITY_WEBGL
                Screen.orientation = Application.isMobilePlatform ? ScreenOrientation.Portrait : ScreenOrientation.AutoRotation;
#endif
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
                bool startupReproductiveChanged = BreedingSystem.RefreshReproductiveStates(Save, GameTime);
                StoreSystem.AdvanceRestock(Save, GameTime);
                ScheduleNextWeaningAnnouncement();
                EnclosureSystem.ClearBreedingPair();
                RestoreDedicatedBreedingPair();
                EnclosureSystem.RecalculateAssignments(Save);
                if (startupReproductiveChanged) SaveSystem.Save(Save);
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
                    StatusMessage = "Tank startup warning — see the Unity Console.";
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
                    ReportInputDiagnostic, AdjustHabitatZoomAtScreenPoint, FocusHabitatAtWorldPoint,
                    TryNavigateHabitatSwipe, IsWorldInputBlockedByModal,
                    PanHabitatCameraByScreenDelta);
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
            long realNow = GameConfig.NowMs();
            long latestReasonableDeadline = GameConfig.NextPairingCheckRealTimestamp(realNow);
            if (Save.pairingNextCheckRealTimestamp <= 0L ||
                Save.pairingNextCheckRealTimestamp > latestReasonableDeadline)
                Save.pairingNextCheckRealTimestamp = latestReasonableDeadline;
            if (Save.pairingNextCheckGameTime <= 0L)
                Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;
            if (!pairingEligibilityScanInitialized)
            {
                lastPairingEligibilityScanGameTime = GameTime;
                pairingEligibilityScanInitialized = true;
            }
        }

        private void ScheduleNextPairingCheck(bool processingScheduledCheck = false)
        {
            if (Save == null) return;
            long realNow = GameConfig.NowMs();
            long previousDeadline = Save.pairingNextCheckRealTimestamp;
            if (previousDeadline > 0L && previousDeadline <= realNow)
            {
                long lateByMs = realNow - previousDeadline;
                long skippedChecks = lateByMs / GameConfig.PairingCheckIntervalRealMs +
                    (processingScheduledCheck ? 0L : 1L);
                PairingHabitatSystem.RecordSkippedChecks(SimulationSpeed, skippedChecks);
            }
            Save.pairingNextCheckRealTimestamp = GameConfig.NextPairingCheckRealTimestamp(realNow);
            // Keep the older field coherent for backward compatibility with
            // saves/tools that still inspect the simulation-time deadline.
            Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;
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
                    yield return new WaitForSecondsRealtime(0.5f);
                    continue;
                }

                if (ui != null && ui.IsWelcomeOpen)
                {
                    yield return new WaitForSecondsRealtime(0.25f);
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

                // After a failed physical route this can
                // retry once per frame, repeating route planning, logging,
                // saving, and UI work. Keep the game timestamp due, but back
                // off route failures in real time instead of hot-looping.
                float retryWait = pairingRetryNotBeforeRealtime - Time.realtimeSinceStartup;
                if (retryWait > 0f)
                {
                    yield return new WaitForSecondsRealtime(Mathf.Min(retryWait, 0.25f));
                    continue;
                }

                EnsurePairingCheckScheduled();
                long remainingRealMs = Save.pairingNextCheckRealTimestamp - GameConfig.NowMs();
                if (remainingRealMs > 0L)
                {
                    float waitSeconds = (float)(remainingRealMs / 1000d);
                    if (waitSeconds > 0.001f)
                        yield return new WaitForSecondsRealtime(waitSeconds);
                    else
                        yield return null;
                    continue;
                }

                // Advance from the current wall-clock time before evaluating.
                // If a courtship or frame stall crossed fertile-window
                // boundaries, inspect the recent interval once; do not replay
                // all missed wall-clock ticks or create a catch-up burst.
                long currentGameTime = GameTime;
                long opportunityWindowStart = GetPairingOpportunityWindowStart(currentGameTime);
                float pairingSpeed = SimulationSpeed;
                ScheduleNextPairingCheck(true);
                RatData male;
                RatData female;
                bool approachStarted = false;
                long pairingCheckSample = RuntimePerformanceDiagnostics.Begin(
                    PerformanceProbeArea.MaintenancePairingChecks);
                bool pairAvailable = PairingHabitatSystem.TryChoosePair(
                    Save, opportunityWindowStart, currentGameTime, pairingSpeed, out male, out female);
                lastPairingEligibilityScanGameTime = currentGameTime;
                RuntimePerformanceDiagnostics.End(
                    PerformanceProbeArea.MaintenancePairingChecks, pairingCheckSample);
                if (pairAvailable)
                {
                    approachStarted = BeginPairingApproach(male, female, opportunityWindowStart);
                }
                // A pairing check does not change enclosure assignments. Do
                // not run the full relationship reconciliation or serialize
                // browser storage for a no-op attempt.
                if (approachStarted)
                {
                    QueuePairingHeaderRefresh();
                }
            }
        }

        private long GetPairingOpportunityWindowStart(long currentGameTime)
        {
            if (!pairingEligibilityScanInitialized || lastPairingEligibilityScanGameTime > currentGameTime)
                return currentGameTime;

            // Pairing checks remain wall-clock rate limited and missed checks
            // are coalesced into one decision. Do not truncate this interval:
            // at 3x even a short real-time courtship or browser stall can span
            // one or more fertile windows in game time. The opportunity scan
            // remembers that a window was crossed, while current age,
            // pregnancy, recovery, nursing, and cooldown rules remain live.
            return lastPairingEligibilityScanGameTime;
        }

        private bool BeginPairingApproach(
            RatData male, RatData female, long opportunityWindowStartGameTime)
        {
            if (pairingApproach != null || Save == null || male == null || female == null) return false;
            float selectedSpeed = SimulationSpeed;
            if (male.sex != RatSex.Male || female.sex != RatSex.Female ||
                male.enclosure != RatEnclosure.Pairing || female.enclosure != RatEnclosure.Pairing)
            {
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed,
                    "pair participants were not a male/female pair in the Pairing Tank");
                return false;
            }

            string reason;
            if (!BreedingSystem.IsBreedEligibleAtOpportunity(
                    Save, male, GameTime, opportunityWindowStartGameTime, out reason))
            {
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed, "male eligibility: " + reason);
                return false;
            }
            bool femaleWindowRecoveredAtSelection;
            if (!BreedingSystem.IsBreedEligibleAtOpportunity(
                    Save, female, GameTime, opportunityWindowStartGameTime, out reason,
                    out femaleWindowRecoveredAtSelection))
            {
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed, "female eligibility: " + reason);
                return false;
            }

            Transform maleRoot;
            Transform femaleRoot;
            RatHabitatBehavior maleBehavior;
            RatHabitatBehavior femaleBehavior;
            if (!TryGetPairingParticipant(male.id, out maleRoot, out maleBehavior) ||
                !TryGetPairingParticipant(female.id, out femaleRoot, out femaleBehavior))
            {
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed,
                    "a selected rat had no live presentation behavior");
                return false;
            }

            // Older saves or a previously failed direct approach can leave an
            // adult inside the nest footprint. Recover it to open floor before
            // planning a new courtship route; never ask the route solver to
            // walk from inside the obstacle.
            if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, maleRoot.position, 0.12f))
                maleBehavior.RecoverAtSafeOpenFloor();
            if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, femaleRoot.position, 0.12f))
                femaleBehavior.RecoverAtSafeOpenFloor();
            if (EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, maleRoot.position, 0.12f) ||
                EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing, femaleRoot.position, 0.12f))
            {
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed,
                    "a participant could not be recovered to open floor");
                return false;
            }

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
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed,
                    "no reachable floor route around the nest");
                PairingHabitatSystem.RecordRouteFailure(selectedSpeed);
                return false;
            }

            Vector3 maleTarget = plan.maleTarget;
            Vector3 femaleTarget = plan.femaleTarget;

            // RatHabitatBehavior now consumes the centralized behavior delta
            // from GrowthSystem. Do not multiply movement here as well or a
            // speed change would be applied twice to courtship movement.
            const float pairingRouteMultiplier = 1f;
            if (!maleBehavior.BeginPairingApproach(
                maleTarget, femaleTarget, pairingRouteMultiplier, plan.maleWaypoints))
            {
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed,
                    "male movement behavior rejected the approach");
                return false;
            }
            if (!femaleBehavior.BeginPairingApproach(
                femaleTarget, maleTarget, pairingRouteMultiplier, plan.femaleWaypoints))
            {
                maleBehavior.CancelPairingApproach();
                PairingHabitatSystem.RecordApproachCancelled(selectedSpeed,
                    "female movement behavior rejected the approach");
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
                stalledSeconds = 0f,
                lastMalePosition = maleRoot.position,
                lastFemalePosition = femaleRoot.position,
                femaleFertileWindowCommitted = true,
                femaleWindowRecoveryRecorded = femaleWindowRecoveredAtSelection,
                pairingSpeed = selectedSpeed,
            };
            PairingHabitatSystem.RecordApproachStarted(selectedSpeed);
            pairingRouteRetryPairKey = null;
            pairingRouteRetryCount = 0;
            pairingFailedMaleTarget = Vector3.zero;
            pairingFailedFemaleTarget = Vector3.zero;
            // Do not mark the rats as actively breeding until they have
            // reached the face-to-face interaction point. The approach is a
            // route, not a breeding attempt, so activity timestamps and
            // cancellation history reflect the real interaction start.
            StatusMessage = string.Empty;
            return true;
        }

        private bool IsPairingApproachParticipantEligible(
            RatData rat, bool isFemale, out string reason)
        {
            if (!isFemale || pairingApproach == null)
                return BreedingSystem.IsBreedEligible(Save, rat, GameTime, out reason);

            bool usedCommittedWindow;
            bool eligible = BreedingSystem.IsBreedEligibleForCommittedPairingOpportunity(
                Save, rat, GameTime, pairingApproach.femaleFertileWindowCommitted,
                out reason, out usedCommittedWindow);
            if (eligible && usedCommittedWindow && !pairingApproach.femaleWindowRecoveryRecorded)
            {
                PairingHabitatSystem.RecordRecoveredFertileWindow(pairingApproach.pairingSpeed);
                pairingApproach.femaleWindowRecoveryRecorded = true;
            }
            return eligible;
        }

        private static void AdvancePairingApproachWatchdogs(
            ref float phaseTimeout,
            ref float stalledSeconds,
            float unscaledDeltaSeconds,
            bool walking,
            bool eitherRatMoved)
        {
            float realSeconds = Mathf.Max(0f, unscaledDeltaSeconds);
            phaseTimeout -= realSeconds;
            if (walking)
                stalledSeconds = eitherRatMoved ? 0f : stalledSeconds + realSeconds;
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
                CancelPairingApproach("one of the rats left the Pairing Tank");
                return;
            }

            // Courtship routing watchdogs are real-time presentation timers,
            // not biology. Rat movement and every eligibility check continue
            // to use accelerated simulation time, but these timers must not
            // be consumed 60x/1,440x faster during fast-forward.
            bool walking = pairingApproach.phase == PairingApproachPhase.Walking;
            bool maleMoved = walking && Vector3.Distance(
                maleRoot.position, pairingApproach.lastMalePosition) > PairingRoutePositionEpsilon;
            bool femaleMoved = walking && Vector3.Distance(
                femaleRoot.position, pairingApproach.lastFemalePosition) > PairingRoutePositionEpsilon;
            AdvancePairingApproachWatchdogs(
                ref pairingApproach.phaseTimeout,
                ref pairingApproach.stalledSeconds,
                Time.unscaledDeltaTime,
                walking,
                maleMoved || femaleMoved);
            if (pairingApproach.phase == PairingApproachPhase.Walking)
            {
                string reason = string.Empty;
                if (!IsPairingApproachParticipantEligible(male, false, out reason))
                {
                    CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                    return;
                }
                if (!IsPairingApproachParticipantEligible(female, true, out reason))
                {
                    CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                    return;
                }

                if (maleBehavior.PairingApproachAtTarget && femaleBehavior.PairingApproachAtTarget)
                {
                    // Revalidate at the exact transition into the physical
                    // interaction. The selected fertile-window opportunity is
                    // committed through this approach, while every other
                    // biological eligibility rule is checked live.
                    if (!IsPairingApproachParticipantEligible(female, true, out reason))
                    {
                        CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                        return;
                    }
                    if (!IsPairingApproachParticipantEligible(male, false, out reason))
                    {
                        CancelPairingApproach(FormatPairingEligibilityCancellation(reason));
                        return;
                    }

                    pairingApproach.phase = PairingApproachPhase.Sniffing;
                    pairingApproach.phaseTimeout = PairingInteractionSeconds + 1.5f;
                    maleBehavior.BeginPairingInteraction(femaleRoot.position, PairingInteractionSeconds);
                    femaleBehavior.BeginPairingInteraction(maleRoot.position, PairingInteractionSeconds);
                    PairingHabitatSystem.RecordInteractionStarted(pairingApproach.pairingSpeed);
                    RatActivitySystem.SetCurrent(Save, male, "breeding", "Breeding", GameTime,
                        "Breeding interaction started");
                    RatActivitySystem.SetCurrent(Save, female, "breeding", "Breeding", GameTime,
                        "Breeding interaction started");
                    // Pairing Habitat breeding is intentionally player-silent.
                    // Keep the state in each rat's activity history, but do
                    // not promote the ordinary interaction to the global
                    // top-right event log/live banner.
                    SaveSystem.QueueSave(Save, "GameBootstrap.UpdatePairingApproach/interaction-started");
                    return;
                }

                // A valid obstacle route can temporarily move farther from
                // its meeting point while rounding the nest. Movement of
                // either root resets the real-time stall timer; only a route
                // with no world-space progress is considered stalled.
                pairingApproach.lastMalePosition = maleRoot.position;
                pairingApproach.lastFemalePosition = femaleRoot.position;
                if (pairingApproach.stalledSeconds >= 2.5f)
                {
                    CancelPairingApproach("the approach was blocked");
                    return;
                }

                if (pairingApproach.phaseTimeout <= 0f)
                {
                    // At 1,440x one rendered frame can represent more than
                    // the authored watchdog duration. If both roots are still
                    // making route progress, extend the watchdog rather than
                    // cancelling a valid approach solely because the visual
                    // movement cap spreads it across a few frames. The
                    // simulation-time stall detector above still cancels a
                    // genuinely blocked route.
                    if (maleMoved || femaleMoved)
                        pairingApproach.phaseTimeout = PairingApproachTimeoutSeconds;
                    else
                        CancelPairingApproach("the approach timed out");
                }
                return;
            }

            if (maleBehavior.PairingInteractionComplete && femaleBehavior.PairingInteractionComplete)
            {
                PairingHabitatSystem.RecordInteractionCompleted(pairingApproach.pairingSpeed);
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
            float diagnosticSpeed = pairingApproach == null ? SimulationSpeed : pairingApproach.pairingSpeed;
            bool resolved = PairingHabitatSystem.ResolvePair(
                Save,
                female,
                male,
                GameTime,
                GameConfig.PairingPregnancyChance,
                pairingApproach != null && pairingApproach.femaleFertileWindowCommitted,
                diagnosticSpeed,
                out conceptionSucceeded,
                out reason,
                out createdPregnancy);

            maleBehavior.FinishPairingInteraction();
            femaleBehavior.FinishPairingInteraction();
            pairingApproach = null;
            ScheduleNextPairingCheck();
            bool pregnancyStateQueued = false;

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
                // Reconcile placement before queuing the pregnancy/announcement
                // so one snapshot contains the complete authoritative result.
                EnclosureSystem.RecalculateAssignments(Save);
                pregnancyStateQueued = AnnounceCreatedPregnancy(createdPregnancy);
            }

            if (resolved && conceptionSucceeded)
            {
                // A conception changes reproductive presentation and colony
                // structure, so refresh the world and roster once. The
                // pregnancy announcement above queued the full state; avoid a
                // second full serialization.
                RefreshWorldAndUi(true);
            }
            else if (ui != null)
            {
                // Failed/cancelled attempts only update cooldown/activity
                // timestamps. Rat visuals own their transitions, so rebuilding
                // the entire roster here is unnecessary fast-forward work.
                ui.RefreshHeader();
            }

            if (!pregnancyStateQueued)
                SaveSystem.QueueSave(Save, "GameBootstrap.ResolvePairingApproach");
        }

        private bool AnnounceCreatedPregnancy(PregnancyData pregnancy)
        {
            if (Save == null || pregnancy == null || pregnancy.status != "pending" ||
                pregnancy.pregnancyAnnouncementLogged) return false;

            // The pregnancy record is the source of truth. Never derive this
            // announcement from the selected rat, breeding slot, or father.
            RatData mother = BreedingSystem.FindRat(Save, pregnancy.motherId);
            if (mother == null || mother.sex != RatSex.Female) return false;

            // Queue the pregnancy and its one-time announcement guard as one
            // atomic snapshot. SaveSystem coalesces this with any other
            // maintenance changes before serializing the full colony.
            bool previousAnnouncementState = pregnancy.pregnancyAnnouncementLogged;
            string previousStatus = StatusMessage;
            ColonyEventData previousNewestEvent = Save.eventLog != null && Save.eventLog.Count > 0
                ? Save.eventLog[0]
                : null;
            string previousLiveMessage = liveEventMessage;
            string previousLiveCategory = liveEventCategory;
            long previousLiveExpiry = liveEventExpiresAt;
            pregnancy.pregnancyAnnouncementLogged = true;
            StatusMessage = ColonyFactory.DisplayName(mother) + " is pregnant!";
            if (SaveSystem.QueueSave(Save, "GameBootstrap.AnnounceCreatedPregnancy")) return true;
            pregnancy.pregnancyAnnouncementLogged = previousAnnouncementState;
            if (Save.eventLog != null && Save.eventLog.Count > 0 &&
                !ReferenceEquals(Save.eventLog[0], previousNewestEvent))
                Save.eventLog.RemoveAt(0);
            statusMessage = previousStatus;
            liveEventMessage = previousLiveMessage;
            liveEventCategory = previousLiveCategory;
            liveEventExpiresAt = previousLiveExpiry;
            return false;
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
                failureLower.Contains("route") ||
                (failureLower.Contains("approach") && failureLower.Contains("timed out"));
            PairingHabitatSystem.RecordApproachCancelled(failedApproach.pairingSpeed, failureText);

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
                PairingHabitatSystem.RecordRouteFailure(failedApproach.pairingSpeed);
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
                bool exhaustedRouteRetries = pairingRouteRetryCount > MaximumPairingRouteRetries;
                if (pairingRouteRetryCount == 1 || exhaustedRouteRetries)
                    Debug.Log("[Rat Habitat] " + diagnosticRat + " route recovery: " + failureText);
                StatusMessage = diagnosticRat + " route recovery — nest blocked";
                int retryExponent = Mathf.Clamp(pairingRouteRetryCount - 1, 0, 3);
                float retryDelay = Mathf.Min(PairingRouteRetryMaxDelaySeconds,
                    PairingRouteRetryBaseDelaySeconds * Mathf.Pow(2f, retryExponent));
                if (exhaustedRouteRetries)
                {
                    if (rats != null && rats.TryGetRatBehavior(failedApproach.maleId, out maleBehavior))
                        maleBehavior.RecoverAtSafeOpenFloor();
                    if (rats != null && rats.TryGetRatBehavior(failedApproach.femaleId, out femaleBehavior))
                        femaleBehavior.RecoverAtSafeOpenFloor();
                    pairingRouteRetryPairKey = null;
                    pairingRouteRetryCount = 0;
                    pairingFailedMaleTarget = Vector3.zero;
                    pairingFailedFemaleTarget = Vector3.zero;
                    retryDelay = PairingRouteRetryMaxDelaySeconds;
                }
                pairingRetryNotBeforeRealtime = Time.realtimeSinceStartup + retryDelay;
            }
            ScheduleNextPairingCheck();
            if (!routeFailure)
            {
                // Eligibility changes such as pregnancy are expected state
                // transitions, not route errors or player-facing events.
                StatusMessage = string.Empty;
            }
            QueuePairingHeaderRefresh();
            SaveSystem.QueueSave(Save, "GameBootstrap.CancelPairingApproach");
        }

        private void QueuePairingHeaderRefresh()
        {
            // Pairing changes do not alter the colony roster; the two
            // behaviors own their visuals directly. Refresh only the small
            // header/status area, never re-render all rats and rebuild the
            // complete UI for a route retry.
            if (pairingHeaderRefreshPending) return;
            pairingHeaderRefreshPending = true;
            pairingHeaderRefreshDueAt = Time.realtimeSinceStartup +
                PairingHeaderRefreshDelaySeconds;
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

        private static void BeginPerformanceSample(string name)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.BeginSample(name);
#endif
        }

        private static void EndPerformanceSample()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.EndSample();
#endif
        }

        private bool IsTimedSimulationWorkDue(out TimedSimulationWorkDue due)
        {
            due = default(TimedSimulationWorkDue);
            if (Save == null) return false;
            due.Pregnancy = birthQueueMaintenancePending;
            long gameTime = GameTime;
            due.StoreRestock = Save.storeNextRestockGameTime > 0L &&
                Save.storeNextRestockGameTime <= gameTime;
            due.Weaning = !weaningScheduleInitialized || nextWeaningAnnouncementGameTime <= gameTime;

            due.BreedingSession = BreedingSystem.NextActiveSessionDueGameTime(Save) <= gameTime;

            // The cached list contains pending records only, rather than
            // rescanning the full historical pregnancy ledger on every
            // rendered frame. Before the earliest due timestamp, this loop
            // is skipped entirely.
            if (BreedingSystem.NextPendingPregnancyDueGameTime(Save) <= gameTime)
            {
                List<PregnancyData> pendingPregnancies = BreedingSystem.GetIndexedPendingPregnancies(Save);
                foreach (PregnancyData pregnancy in pendingPregnancies)
                {
                    if (pregnancy == null || pregnancy.status != "pending" || pregnancy.dueAt > gameTime ||
                        !BirthRetryReady(pregnancy)) continue;
                    RatData mother = BreedingSystem.FindRat(Save, pregnancy.motherId);
                    if (mother == null || mother.enclosure == RatEnclosure.ForSale ||
                        !EnclosureSystem.HasNest(mother.enclosure))
                    {
                        due.Pregnancy = true;
                        break;
                    }

                    string occupantId;
                    bool tankOccupied = birthQueueReservations.TryGetOccupant(mother.enclosure, out occupantId);
                    if (tankOccupied && !string.Equals(occupantId, mother.id, StringComparison.Ordinal))
                    {
                        // Wake once to publish the waiting state. Thereafter
                        // the active route's arrival/watchdog wakes resolution.
                        if (!pregnancy.birthWaitingForNest)
                        {
                            due.Pregnancy = true;
                            break;
                        }
                        continue;
                    }

                    // An unreserved due mother, a stale persisted route, or a
                    // mother already at the nest needs a resolution pass. A
                    // walking route is polled through the cheap watchdog only.
                    if (!tankOccupied || !pregnancy.birthApproachStarted ||
                        string.IsNullOrEmpty(mother.id) || !birthApproachWatchdogs.ContainsKey(mother.id) || rats == null ||
                        !rats.TryGetRatBehavior(mother.id, out RatHabitatBehavior birthBehavior) ||
                        birthBehavior == null || !birthBehavior.BirthApproachActive ||
                        birthBehavior.BirthApproachAtNest || TryGetMotherRootAtNest(mother, out _))
                    {
                        due.Pregnancy = true;
                        break;
                    }
                }
            }
            due.Recovery = BreedingSystem.NextRecoveryTransitionGameTime(Save) <= gameTime;
            return due.Any;
        }

        private bool BirthRetryReady(PregnancyData pregnancy)
        {
            float retryAt;
            return pregnancy == null || string.IsNullOrEmpty(pregnancy.id) ||
                !birthRetryAfterRealtime.TryGetValue(pregnancy.id, out retryAt) ||
                Time.unscaledTime >= retryAt;
        }

        private bool MarkBirthBlockedAndScheduleRetry(PregnancyData pregnancy, string reason)
        {
            if (pregnancy != null && !string.IsNullOrEmpty(pregnancy.id))
                birthRetryAfterRealtime[pregnancy.id] = Time.unscaledTime + BirthRetryCooldownSeconds;
            RecordBirthBlockedReason(reason);
            bool changed = BreedingSystem.MarkBirthBlocked(pregnancy, GameTime, reason);
            if (changed)
            {
                string playerMessage = reason.Trim();
                if (!playerMessage.StartsWith("Birth delayed", StringComparison.OrdinalIgnoreCase))
                    playerMessage = "Birth delayed — " + playerMessage;
                // RecordStatusEvent puts this in the persistent Events history
                // and the configured birth alert. Do this only when the
                // diagnostic reason changes, not on every retry window.
                StatusMessage = playerMessage;
            }
            return changed;
        }

        private bool UpdateBirthApproachWatchdogs()
        {
            if (Save == null || birthApproachWatchdogs.Count == 0) return false;
            float delta = Mathf.Max(0f, Time.unscaledDeltaTime);
            staleBirthApproachIds.Clear();
            bool changed = false;
            foreach (KeyValuePair<string, BirthApproachWatchdog> entry in birthApproachWatchdogs)
            {
                BirthApproachWatchdog watchdog = entry.Value;
                PregnancyData pregnancy = watchdog == null ? null : watchdog.pregnancy;
                RatData mother = watchdog == null ? null : watchdog.mother;
                if (watchdog == null || pregnancy == null || pregnancy.status != "pending" ||
                    mother == null || mother.removalDisposition != RatRemovalDisposition.None ||
                    mother.enclosure != watchdog.enclosure)
                {
                    if (watchdog != null)
                    {
                        if (mother != null && rats != null &&
                            rats.TryGetRatBehavior(mother.id, out RatHabitatBehavior staleBehavior) &&
                            staleBehavior != null && staleBehavior.BirthApproachActive)
                            staleBehavior.CancelBirthApproach();
                        birthQueueReservations.Release(watchdog.enclosure, watchdog.motherId);
                        if (pregnancy != null && pregnancy.status == "pending")
                        {
                            ClearBirthApproachState(pregnancy);
                            pregnancy.birthWaitingForNest = true;
                            if (mother != null)
                                RatActivitySystem.SetCurrent(Save, mother,
                                    "birth-waiting", "Waiting to give birth", GameTime);
                            birthQueueRouteFailures++;
                            MarkBirthBlockedAndScheduleRetry(pregnancy,
                                "Birth approach became stale; pregnancy retained for retry.");
                            birthQueueMaintenancePending = true;
                            changed = true;
                        }
                    }
                    staleBirthApproachIds.Add(entry.Key);
                    continue;
                }

                RatHabitatBehavior behavior = null;
                if (rats == null || !rats.TryGetRatBehavior(mother.id, mother, out behavior) || behavior == null ||
                    !behavior.BirthApproachActive)
                {
                    if (TryGetMotherRootAtNest(mother, out Vector3 parkedPosition))
                    {
                        // A missing behavior component is not permission to
                        // release a mother who has already reached the nest.
                        // Keep the safe reservation and let the due-pregnancy
                        // pass resolve from this confirmed world position.
                        if (behavior != null) behavior.ConfirmBirthApproachArrivalAtNest();
                        watchdog.lastPosition = parkedPosition;
                        watchdog.elapsedRealtime = 0f;
                        watchdog.stalledRealtime = 0f;
                        birthQueueMaintenancePending = true;
                        continue;
                    }
                    FailBirthApproach(watchdog, null,
                        "Mother presentation route became unavailable; pregnancy retained for retry.", false);
                    staleBirthApproachIds.Add(entry.Key);
                    changed = true;
                    continue;
                }

                // A final movement step can enter the nest zone without
                // crossing the behavior's internal distance threshold. Treat
                // that world-space position as arrival and stop the route.
                if (!behavior.BirthApproachAtNest &&
                    TryGetMotherRootAtNest(mother, out Vector3 arrivedPosition))
                {
                    behavior.ConfirmBirthApproachArrivalAtNest();
                    watchdog.lastPosition = arrivedPosition;
                    watchdog.stalledRealtime = 0f;
                    birthQueueMaintenancePending = true;
                }

                // Arrival holds the one nest reservation until the authoritative
                // birth transaction completes. Storage retries at the nest are
                // not route stalls and must not send another mother into it.
                if (behavior.BirthApproachAtNest)
                {
                    watchdog.stalledRealtime = 0f;
                    watchdog.lastPosition = behavior.transform.position;
                    continue;
                }

                watchdog.elapsedRealtime += delta;
                Vector3 position = behavior.transform.position;
                if (Vector3.Distance(position, watchdog.lastPosition) >= BirthApproachProgressDistance)
                {
                    watchdog.lastPosition = position;
                    watchdog.stalledRealtime = 0f;
                }
                else
                {
                    watchdog.stalledRealtime += delta;
                }

                bool stalled = watchdog.stalledRealtime >= BirthApproachStallTimeoutRealtimeSeconds;
                bool timedOut = watchdog.elapsedRealtime >= BirthApproachMaximumRealtimeSeconds;
                if (!stalled && !timedOut) continue;

                if (timedOut) birthQueueRouteTimeouts++;
                FailBirthApproach(watchdog, behavior,
                    timedOut
                        ? "Birth route timed out; pregnancy remains due and will retry."
                        : "Birth route stalled; pregnancy remains due and will retry.",
                    true);
                staleBirthApproachIds.Add(entry.Key);
                changed = true;
            }

            for (int index = 0; index < staleBirthApproachIds.Count; index++)
                birthApproachWatchdogs.Remove(staleBirthApproachIds[index]);
            staleBirthApproachIds.Clear();
            return changed;
        }

        private void FailBirthApproach(BirthApproachWatchdog watchdog,
            RatHabitatBehavior behavior, string reason, bool routeWasActive)
        {
            if (watchdog == null) return;
            if (behavior != null) behavior.CancelBirthApproach();
            if (watchdog.pregnancy != null)
            {
                watchdog.pregnancy.birthApproachStarted = false;
                watchdog.pregnancy.birthApproachStartedAt = 0L;
                watchdog.pregnancy.birthWaitingForNest = true;
            }
            birthQueueReservations.Release(watchdog.enclosure, watchdog.motherId);
            if (routeWasActive) birthQueueRouteFailures++;
            birthQueueMaintenancePending = true;
            if (watchdog.mother != null)
                RatActivitySystem.SetCurrent(Save, watchdog.mother,
                    "birth-waiting", "Waiting to give birth", GameTime);
            MarkBirthBlockedAndScheduleRetry(watchdog.pregnancy, reason);
        }

        private void RecordBirthBlockedReason(string reason)
        {
            birthQueueBlockedBirths++;
            if (string.IsNullOrEmpty(reason))
            {
                birthQueueBlockedOther++;
                return;
            }
            if (reason.IndexOf("capacity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("room", StringComparison.OrdinalIgnoreCase) >= 0)
                birthQueueBlockedCapacity++;
            else if (reason.IndexOf("nest", StringComparison.OrdinalIgnoreCase) >= 0)
                birthQueueBlockedNest++;
            else if (reason.IndexOf("presentation", StringComparison.OrdinalIgnoreCase) >= 0)
                birthQueueBlockedPresentation++;
            else if (reason.IndexOf("route", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("stalled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                birthQueueBlockedRoute++;
            else
                birthQueueBlockedOther++;
        }

        private string BuildBirthQueueDiagnostics()
        {
            if (Save == null) return "Birth queue: colony data unavailable.";
            System.Text.StringBuilder summary = new System.Text.StringBuilder(320);
            summary.Append("Birth queue • ");
            AppendBirthTankDiagnostics(summary, RatEnclosure.Pairing, "Pairing Tank");
            summary.Append("; ");
            AppendBirthTankDiagnostics(summary, RatEnclosure.FemaleColony, "Female Tank");
            summary.Append("; ");
            AppendBirthTankDiagnostics(summary, RatEnclosure.Nursery, "Nursery Tank");
            summary.Append("; ");
            AppendBirthTankDiagnostics(summary, RatEnclosure.MaleColony, "Male Tank");
            summary.Append("; ");
            AppendBirthTankDiagnostics(summary, RatEnclosure.ForSale, "For Sale Tank");
            summary.Append("; route failures/timeouts ").Append(birthQueueRouteFailures).Append('/')
                .Append(birthQueueRouteTimeouts).Append("; births ").Append(birthQueueSuccessfulBirths)
                .Append("; blocked ").Append(birthQueueBlockedBirths).Append(" [capacity ")
                .Append(birthQueueBlockedCapacity).Append(", nest ").Append(birthQueueBlockedNest)
                .Append(", presentation ").Append(birthQueueBlockedPresentation).Append(", route ")
                .Append(birthQueueBlockedRoute).Append(", other ").Append(birthQueueBlockedOther).Append(']');
            return summary.ToString();
        }

        private void AppendBirthTankDiagnostics(System.Text.StringBuilder summary,
            RatEnclosure enclosure, string label)
        {
            int dueCount = 0;
            int queuedCount = 0;
            int waitingCount = 0;
            if (Save != null)
            {
                List<PregnancyData> pending = BreedingSystem.GetIndexedPendingPregnancies(Save);
                for (int index = 0; index < pending.Count; index++)
                {
                    PregnancyData pregnancy = pending[index];
                    if (pregnancy == null || pregnancy.status != "pending" || pregnancy.dueAt > GameTime) continue;
                    RatData mother = BreedingSystem.FindRat(Save, pregnancy.motherId);
                    if (mother == null || mother.enclosure != enclosure) continue;
                    dueCount++;
                    if (pregnancy.birthWaitingForNest) waitingCount++;
                    if (pregnancy.birthWaitingForNest || birthQueueReservations.IsReservedBy(enclosure, mother.id))
                        queuedCount++;
                }
            }
            string activeMother;
            bool active = birthQueueReservations.TryGetOccupant(enclosure, out activeMother);
            summary.Append(label).Append(" due/queued/active/waiting ")
                .Append(dueCount).Append('/').Append(queuedCount).Append('/')
                .Append(active ? 1 : 0).Append('/').Append(waitingCount);
        }

        private void ScheduleNextNursingTick()
        {
            nextNursingTickGameTime = NursingSystem.NextOpportunityGameTime(Save, GameTime);
            nursingScheduleInitialized = true;
        }

        private void ScheduleNextWeaningAnnouncement()
        {
            nextWeaningAnnouncementGameTime = long.MaxValue;
            if (Save != null && Save.litters != null)
            {
                foreach (LitterData litter in Save.litters)
                {
                    if (litter == null || litter.weaningAnnouncementLogged || litter.weaningTimestamp <= 0L)
                        continue;
                    if (litter.weaningTimestamp < nextWeaningAnnouncementGameTime)
                        nextWeaningAnnouncementGameTime = litter.weaningTimestamp;
                }
            }
            weaningScheduleInitialized = true;
        }

        // Birth-transition fixes keep modal pause and presentation ordering
        // authoritative across WebGL frames.
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long performanceSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.BootstrapUpdate);
            float startedAt = Time.realtimeSinceStartup;
            try
            {
                UpdateCore();
            }
            finally
            {
                lastBootstrapUpdateDurationMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
                RuntimePerformanceDiagnostics.End(PerformanceProbeArea.BootstrapUpdate, performanceSample);
            }
#else
            UpdateCore();
#endif
        }

        private void UpdateCore()
        {
            float frameSampleStartedAt = Time.realtimeSinceStartup;
            BeginPerformanceSample("Rat Empire/Frame/Viewport and Camera");
            UpdateWorldViewport();
            UpdateCameraPresentation();
            UpdateHabitatPageSettling();
            EndPerformanceSample();
            lastViewportFrameDurationMs = (Time.realtimeSinceStartup - frameSampleStartedAt) * 1000f;

            wakeLockPollTimer -= Time.unscaledDeltaTime;
            if (wakeLockPollTimer <= 0f)
            {
                bool wakeLockStatusChanged = BrowserWakeLockSystem.PollStatus();
                if (wakeLockStatusChanged && ui != null) ui.RefreshWakeLockControls();
                wakeLockPollTimer = 0.5f;
            }
            if (Save == null) return;

            if (pairingHeaderRefreshPending &&
                Time.realtimeSinceStartup >= pairingHeaderRefreshDueAt)
            {
                pairingHeaderRefreshPending = false;
                if (ui != null) ui.RefreshHeader();
            }

            // Service one coalesced colony save at a bounded cadence. The
            // queued save stores the latest live Save reference, so multiple
            // maintenance/activity changes collapse into one serialization.
            SaveSystem.FlushPendingSaveIfDue();

            // The birth naming queue is part of the same authoritative modal
            // pause as the welcome dialog. Do not let the next rendered frame
            // unpause the clock while a newborn naming blocker is still open
            // (or while the UI is rebuilding the popup).
            bool welcomePaused = ui != null && ui.IsWelcomeOpen;
            bool newbornNamingPaused = HasPendingLitterNaming ||
                (ui != null && ui.IsPendingLitterNamingOpen);
            bool modalPaused = welcomePaused || newbornNamingPaused;
            bool performanceSimulationPaused =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.Simulation);
#else
                false;
#endif
            GrowthSystem.SetSimulationPaused(modalPaused || performanceSimulationPaused);
            if (performanceSimulationPaused)
            {
                // Keep this A/B experiment entirely outside the colony save.
                // Capture one transient real-time boundary, then discard any
                // hidden-tab elapsed signal without touching Save.clock.
                if (!performanceSimulationPauseActive)
                {
                    performanceSimulationPauseStartedAt = GameConfig.NowMs();
                    performanceSimulationPauseActive = true;
                }
                SaveSystem.DiscardBrowserLifecycleElapsed();
                return;
            }
            long simulationClockNow = GameConfig.NowMs();
            bool resumedFromPerformancePause = performanceSimulationPauseActive;
            if (resumedFromPerformancePause)
            {
                performanceSimulationPauseActive = false;
                // Advance through the final active instant before the switch
                // was enabled, but not through diagnostic pause time.
                simulationClockNow = performanceSimulationPauseStartedAt;
                performanceSimulationPauseStartedAt = 0L;
                SaveSystem.DiscardBrowserLifecycleElapsed();
            }
            // Browser visibility changes can suspend Unity's rendered loop.
            // Consume the guarded lifecycle signal before the normal frame
            // clock update so the same persisted timestamp advances the
            // colony once on resume, without replaying visual frames.
            if (!resumedFromPerformancePause)
                SaveSystem.ResumeFromBrowserLifecycle(Save, modalPaused);
            if (modalPaused)
            {
                // Keep the clock's real-time anchor at the current instant so
                // dismissing the modal never causes a catch-up jump.
                if (Save.clock != null) Save.clock.lastRealTimestamp = GameConfig.NowMs();
                return;
            }

            frameSampleStartedAt = Time.realtimeSinceStartup;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long clockAgeSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.ClockAndAge);
#endif
            BeginPerformanceSample("Rat Empire/Frame/Clock and Age");
            GrowthSystem.AdvanceClock(Save, simulationClockNow);
            GrowthSystem.RefreshRatAges(Save, GameTime);
            EndPerformanceSample();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RuntimePerformanceDiagnostics.End(PerformanceProbeArea.ClockAndAge, clockAgeSample);
#endif
            lastClockFrameDurationMs = (Time.realtimeSinceStartup - frameSampleStartedAt) * 1000f;

            // Movement and the active pairing interaction stay per-frame. All
            // biological deadlines and relationship reconciliation below are
            // gated by simulation time or a known deadline instead of running
            // as a full-colony scan on every rendered frame.
            frameSampleStartedAt = Time.realtimeSinceStartup;
            BeginPerformanceSample("Rat Empire/Frame/Movement and Pairing");
            long pairingMovementSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.PairingMovement);
            UpdatePairingApproach();
            PruneGroupSelection();
            RuntimePerformanceDiagnostics.End(PerformanceProbeArea.PairingMovement, pairingMovementSample);
            EndPerformanceSample();
            lastMovementFrameDurationMs = (Time.realtimeSinceStartup - frameSampleStartedAt) * 1000f;
            bool birthWatchdogChanged = UpdateBirthApproachWatchdogs();

            bool stageChanged = false;
            bool reproductiveStateChanged = false;
            bool storeChanged = false;
            StoreRestockResult storeRestockResult = default(StoreRestockResult);
            bool saleEligibilityChanged = false;
            bool activityChanged = false;
            bool enclosureChanged = false;
            bool nursingChanged = false;
            bool nursingPassDue = !nursingScheduleInitialized || nextNursingTickGameTime <= GameTime;
            bool alertAnnouncementStateChanged = false;
            bool birthSequenceChanged = birthWatchdogChanged;
            bool birthBatchQueued = false;
            int completedSessionCount = 0;
            int births = 0;
            List<DedicatedBreedingSessionData> completedSessions = null;
            List<LitterData> newLitters = null;

            bool maintenanceAllowed =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                !RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.ColonyMaintenance);
#else
                true;
#endif
            TimedSimulationWorkDue timedWorkDue = default(TimedSimulationWorkDue);
            long deadlineResolutionSample = RuntimePerformanceDiagnostics.Begin(
                PerformanceProbeArea.MaintenanceDeadlineResolution);
            bool hasTimedWorkDue = maintenanceAllowed && IsTimedSimulationWorkDue(out timedWorkDue);
            RuntimePerformanceDiagnostics.End(
                PerformanceProbeArea.MaintenanceDeadlineResolution, deadlineResolutionSample);
            bool dailyMaintenanceDue = !colonyMaintenanceInitialized || GameTime >= nextColonyMaintenanceGameTime;
            bool ageTransitionDue = !ageTransitionScheduleInitialized || GameTime >= nextAgeTransitionGameTime;
            bool maintenanceDue = maintenanceAllowed &&
                (dailyMaintenanceDue || ageTransitionDue || hasTimedWorkDue);
            if (maintenanceDue)
            {
                float maintenanceStartedAt = Time.realtimeSinceStartup;
                long simulationMaintenanceSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.SimulationMaintenance);
                BeginPerformanceSample("Rat Empire/Simulation Maintenance");
                // Store sale values depend on current age-declined traits, so
                // a restock catch-up refreshes biological values once at the
                // current timestamp before calculating any payout.
                bool growthPassDue = dailyMaintenanceDue || ageTransitionDue || timedWorkDue.StoreRestock;
                bool reproductivePassDue = growthPassDue || timedWorkDue.BreedingSession ||
                    timedWorkDue.Pregnancy || timedWorkDue.Recovery;

                if (growthPassDue)
                {
                    long growthSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceGrowth);
                    BeginPerformanceSample("Rat Empire/Simulation/Growth and Biology");
                    stageChanged = GrowthSystem.RefreshRatStages(Save);
                    if (stageChanged) alertAnnouncementStateChanged |= AnnounceNewNaturalDeaths();
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceGrowth, growthSample);
                    EndPerformanceSample();
                }

                if (reproductivePassDue)
                {
                    long reproductiveSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceReproductive);
                    BeginPerformanceSample("Rat Empire/Simulation/Reproductive State");
                    reproductiveStateChanged = BreedingSystem.RefreshReproductiveStates(
                        Save, GameTime, false);
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceReproductive, reproductiveSample);
                    EndPerformanceSample();
                }

                if (timedWorkDue.StoreRestock)
                {
                    long storeSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceStore);
                    BeginPerformanceSample("Rat Empire/Simulation/Store and Sale Eligibility");
                    storeChanged = StoreSystem.AdvanceRestock(Save, GameTime, out storeRestockResult);
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceStore, storeSample);
                    EndPerformanceSample();
                }

                if (timedWorkDue.BreedingSession)
                {
                    long sessionSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceSessions);
                    BeginPerformanceSample("Rat Empire/Simulation/Breeding Deadlines");
                    completedSessionCount = BreedingSystem.ResolveDueDedicatedBreedingSessions(
                        Save, GameTime, out completedSessions);
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceSessions, sessionSample);
                    EndPerformanceSample();
                }
                if (completedSessionCount > 0)
                {
                    EnclosureSystem.ClearBreedingPair();
                    RestoreDedicatedBreedingPair();
                    // Add the session result and any conception announcement
                    // to the same authoritative maintenance batch. The final
                    // state-change queue below persists them together instead
                    // of synchronously serializing the entire colony here.
                    if (completedSessions != null)
                    {
                        foreach (DedicatedBreedingSessionData completedSession in completedSessions)
                        {
                            if (completedSession == null || !completedSession.conceptionSucceeded) continue;
                            PregnancyData pregnancy = BreedingSystem.FindPendingPregnancyForMother(
                                Save, completedSession.motherId);
                            AnnounceCreatedPregnancy(pregnancy);
                        }
                    }
                    reproductiveStateChanged = true;
                }

                if (timedWorkDue.Pregnancy)
                {
                    long birthSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceBirths);
                    BeginPerformanceSample("Rat Empire/Simulation/Pregnancy and Birth Deadlines");
                    bool birthPassChanged;
                    births = ProcessDuePregnanciesAtNest(out newLitters, out birthPassChanged);
                    birthSequenceChanged |= birthPassChanged;
                    birthQueueMaintenancePending = false;
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceBirths, birthSample);
                    EndPerformanceSample();
                }
                if (births > 0)
                {
                    foreach (LitterData litter in newLitters)
                        AnnounceBirth(litter);
                    PreparePendingLitterNaming(newLitters);
                    if (ui != null) ui.LogBirthTransitionState("birth-processed");
                    stageChanged = true;
                    nextNursingTickGameTime = GameTime;
                    nursingPassDue = true;
                    ScheduleNextWeaningAnnouncement();
                }
                else if (stageChanged)
                {
                    // Stage eligibility can change which mothers have a
                    // nursing opportunity. Recompute the independent nursing
                    // deadline without promoting it to a colony-wide pass.
                    ScheduleNextNursingTick();
                    nursingPassDue |= nextNursingTickGameTime <= GameTime;
                }
                if (timedWorkDue.Weaning)
                {
                    long weaningSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceWeanings);
                    alertAnnouncementStateChanged |= AnnounceCompletedWeanings();
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceWeanings, weaningSample);
                }

                bool refreshActivities = dailyMaintenanceDue || stageChanged || reproductiveStateChanged ||
                    storeChanged || births > 0 || completedSessionCount > 0 || birthSequenceChanged;
                if (refreshActivities)
                {
                    long activitySample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceActivities);
                    BeginPerformanceSample("Rat Empire/Simulation/Activity Refresh");
                    activityChanged = RefreshRatActivities();
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceActivities, activitySample);
                    EndPerformanceSample();
                }
                if (stageChanged || reproductiveStateChanged || births > 0 || completedSessionCount > 0)
                {
                    long enclosureSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceEnclosures);
                    BeginPerformanceSample("Rat Empire/Simulation/Enclosure Recalculation");
                    enclosureChanged = EnclosureSystem.RecalculateAssignments(Save);
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceEnclosures, enclosureSample);
                    EndPerformanceSample();
                }

                if (growthPassDue || reproductiveStateChanged || storeChanged || births > 0 || completedSessionCount > 0)
                {
                    long saleEligibilityBefore = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.MaintenanceSaleEligibility);
                    saleEligibilityChanged = UpdateSaleEligibilitySignature();
                    RuntimePerformanceDiagnostics.End(PerformanceProbeArea.MaintenanceSaleEligibility, saleEligibilityBefore);
                }

                long maintenanceDeadlineBase = colonyMaintenanceInitialized
                    ? nextColonyMaintenanceGameTime
                    : GameTime;
                nextColonyMaintenanceGameTime = GrowthSystem.AdvanceDeadlinePastNow(
                    maintenanceDeadlineBase, ColonyMaintenanceIntervalGameMs, GameTime);
                if (growthPassDue || births > 0)
                {
                    nextAgeTransitionGameTime = GrowthSystem.NextAgeBoundaryGameTime(Save, GameTime);
                    ageTransitionScheduleInitialized = true;
                }
                colonyMaintenanceInitialized = true;
                maintenancePassCount++;
                lastMaintenanceDurationMs = (Time.realtimeSinceStartup - maintenanceStartedAt) * 1000f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                performanceMaintenanceWindowMs += lastMaintenanceDurationMs;
#endif
                RuntimePerformanceDiagnostics.End(PerformanceProbeArea.SimulationMaintenance, simulationMaintenanceSample);
                EndPerformanceSample();
            }

            // Nursing interaction opportunities are independent deadlines,
            // not a reason to wake the full-colony maintenance bundle.
            // Births force one immediate pass after newborn presentation roots
            // are rebuilt; otherwise missed cooldowns are coalesced at now.
            // Ambient behavior transitions are meaningful activity history but
            // do not need a biological full pass. Poll them on a short real
            // time interval so idle/walk transitions remain visible without
            // rebuilding or serializing the colony on every frame.
            ambientActivityPollTimer -= Time.unscaledDeltaTime;
            if (ambientActivityPollTimer <= 0f)
            {
                activityChanged |= RefreshRatActivities();
                ambientActivityPollTimer = AmbientActivityPollIntervalSeconds;
            }

            if (SelectedRat == null && !string.IsNullOrEmpty(selectedRatId))
            {
                selectedRatId = null;
                if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            }

            if (storeChanged)
            {
                StatusMessage = "Rat Market restocked.";
                ShowTransientRestockSaleAlert(storeRestockResult.autoSaleMessage);
            }

            bool structuralPresentationChange = stageChanged || reproductiveStateChanged || enclosureChanged ||
                births > 0 || birthPresentationRepairRequested;
            if (structuralPresentationChange)
            {
                BeginPerformanceSample("Rat Empire/Presentation Render");
                try
                {
                    if (rats != null && habitat != null)
                    {
                        rats.Render(Save, habitat.NestPosition);
                        birthPresentationRepairRequested = false;
                    }
                }
                catch (Exception exception)
                {
                    startupWarning = true;
                    Debug.LogException(exception);
                    StatusMessage = "Rat presentation update warning — see the Unity Console.";
                }
                EndPerformanceSample();
            }

            // Newborn presenter roots must exist before the nursing system can
            // start its mother/pup interaction. This ordering is important on
            // a birth frame and preserves the previous stable-root contract.
            if (nursingPassDue)
            {
                BeginPerformanceSample("Rat Empire/Simulation/Nursing");
                long nursingSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.NursingUpdate);
                nursingChanged = NursingSystem.Tick(Save, GameTime, rats);
                RuntimePerformanceDiagnostics.End(PerformanceProbeArea.NursingUpdate, nursingSample);
                ScheduleNextNursingTick();
                EndPerformanceSample();
            }

            // Birth creation, pregnancy finalization, the one-time event, and
            // the pinkie naming queue are one authoritative state change. Queue
            // them together once after nursing has observed the newborns; the
            // bounded save service persists the latest snapshot, while lifecycle
            // callbacks still force an immediate write before the page leaves.
            if (births > 0)
            {
                birthBatchQueued = SaveSystem.QueueSave(Save, "GameBootstrap.UpdateCore/birth-batch");
                if (birthBatchQueued)
                    saveTimer = 0f;
                else
                    StatusMessage = "Birth is safe in the colony; saving will retry automatically.";
            }

            bool stateNeedsSave = activityChanged || nursingChanged || stageChanged ||
                reproductiveStateChanged || enclosureChanged || storeChanged ||
                births > 0 || birthSequenceChanged || completedSessionCount > 0 || alertAnnouncementStateChanged;
            if (stateNeedsSave && !birthBatchQueued)
            {
                SaveSystem.QueueSave(Save, "GameBootstrap.UpdateCore/state-change");
                saveTimer = 0f;
            }

            saveTimer += Time.unscaledDeltaTime;
            // Autosave remains periodic for reliable browser persistence, but
            // it is deliberately not a frame operation or a pairing-loop
            // operation. Important mutations above still save immediately.
            if (!stateNeedsSave && saveTimer >= AutosaveIntervalSeconds)
            {
                SaveSystem.QueueSave(Save, "GameBootstrap.UpdateCore/autosave");
                saveTimer = 0f;
            }

            if (ui != null)
            {
                try
                {
                    BeginPerformanceSample("Rat Empire/UI Update");
                    float uiStartedAt = Time.realtimeSinceStartup;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (!RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.AutomaticUiRefresh))
                    {
#endif
                        if (structuralPresentationChange || saleEligibilityChanged || storeChanged || births > 0 || completedSessionCount > 0)
                            ui.Refresh(true);
                        else
                            ui.RefreshHeader();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    }
#endif
                    if (births > 0) ui.LogBirthTransitionState("birth-ui-refresh-complete");
                    lastUiRefreshDurationMs = (Time.realtimeSinceStartup - uiStartedAt) * 1000f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    performanceUiRefreshWindowMs += lastUiRefreshDurationMs;
#endif
                    EndPerformanceSample();
                }
                catch (Exception exception)
                {
                    EndPerformanceSample();
                    if (!uiUpdateErrorLogged)
                    {
                        uiUpdateErrorLogged = true;
                        Debug.LogException(exception);
                    }
                }
                finally
                {
                    // Even a page-specific exception must not leave the
                    // birth/naming blocker or the old pointer capture in
                    // control of the next page interaction.
                    if (births > 0) ui.RestoreInputStateAfterBirthTransition();
                }
            }
        }

        public void SetPerformanceIsolation(PerformanceIsolationMode mode)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RuntimePerformanceDiagnostics.SetIsolationMode(mode);
            if (rats != null) rats.ApplyPerformanceIsolationMode();
            Debug.Log("[Performance Diagnostics] Isolation mode: " + mode + ". This mode is transient and does not write or reset the save.");
#endif
        }

        public void TogglePerformanceIsolation(PerformanceIsolationMode mode)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RuntimePerformanceDiagnostics.ToggleIsolationMode(mode);
            if (rats != null) rats.ApplyPerformanceIsolationMode();
            Debug.Log("[Performance Diagnostics] Isolation modes: " + RuntimePerformanceDiagnostics.IsolationMode +
                ". This mode is transient and does not write or reset the save.");
#endif
        }

        public void SetPerformanceHudVisible(bool visible)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RuntimePerformanceDiagnostics.SetHudVisible(visible);
#endif
        }

        public void SetPerformanceCaptureEnabled(bool enabled)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RuntimePerformanceDiagnostics.SetCaptureEnabled(enabled);
#endif
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
            habitatZoomFocusOffset = Vector3.zero;
            ClearPendingSelectionActions();
            if (entity == null)
            {
                // A valid world tap that does not hit an interactable is an
                // empty-floor tap. Clear both the data selection and its
                // visual ring instead of leaving the previous profile open.
                selectedRatId = null;
                selectedObjectId = null;
                cameraFocusNest = false;
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
                tankCameraPanState.Recenter();
                selectedRatId = entity.entityId;
                selectedObjectId = null;
                cameraFocusNest = false;
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
                if (IsNestEntity(entity))
                {
                    selectedRatId = null;
                    selectedObjectId = null;
                    cameraFollowSelectedRat = false;
                    cameraFocusNest = true;
                    cameraFocusNestEnclosure = entity.entityId == "pairing_nest"
                        ? RatEnclosure.Pairing : RatEnclosure.FemaleColony;
                    cameraView = cameraFocusNestEnclosure == RatEnclosure.Pairing
                        ? HabitatCameraView.Pairing : HabitatCameraView.FemaleEnclosure;
                    habitatZoomOffset = 0f;
                    habitatZoomFocusOffset = Vector3.zero;
                    cameraMoveVelocity = Vector3.zero;
                    cameraZoomVelocity = 0f;
                    if (rats != null) rats.SetSelected(null);
                    if (ui != null) ui.CloseTransientPanels();
                    if (ui != null) ui.SuppressGeneratedUiActionsThisFrame();
                    if (ui != null) ui.RefreshHeader();
                    return true;
                }
                selectedObjectId = entity.entityId;
                selectedRatId = null;
                cameraFocusNest = false;
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
            cameraFocusNest = false;
            cameraFollowSelectedRat = false;
            habitatZoomOffset = 0f;
            habitatZoomFocusOffset = Vector3.zero;
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
            staleGroupSelectionIds.Clear();
            foreach (var id in selectedRatIds)
            {
                if (BreedingSystem.FindRat(Save, id) == null) staleGroupSelectionIds.Add(id);
            }
            if (staleGroupSelectionIds.Count == 0) return;
            foreach (var id in staleGroupSelectionIds) selectedRatIds.Remove(id);
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
            cameraFocusNest = false;
            cameraFollowSelectedRat = false;
            if (rats != null) rats.SetSelected(null);
            if (ui != null) ui.CloseTransientPanels();

            HabitatCameraView view = CameraViewForWorldPoint(worldPoint);
            cameraView = NormalizeHabitatView(view);
            cameraFocusNest = false;
            habitatZoomOffset = 0f;
            habitatZoomFocusOffset = Vector3.zero;
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
                case HabitatCameraView.Breeding: return "For Sale Tank";
                case HabitatCameraView.Pairing: return "Pairing Tank";
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
        /// Moves the explicitly requested live rat into Pairing Tank.
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
                // explicit manual-placement flag. The Pairing Tank is
                // intentionally independent of age, sex, fertility, and
                // pregnancy eligibility.
                rat.pairingHabitatAssigned = true;
                SaveSystem.Save(Save);
                StatusMessage = ColonyFactory.DisplayName(rat) + " is already in the Pairing Tank.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            int pairingCount = CountPairingHabitatRats();
            if (pairingCount >= PairingHabitatCapacity)
            {
                StatusMessage = "Pairing Tank is full (" + pairingCount + "/" +
                    PairingHabitatCapacity + "). Upgrade Pairing Tank Capacity or move a rat out first.";
                PairingHabitatSystem.RecordCapacityBlocked(StatusMessage, SimulationSpeed);
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
                RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving tanks", GameTime,
                    "Moved to Pairing Tank");
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
            StatusMessage = "[Rat Empire] " + ColonyFactory.DisplayName(rat) + " moved to Pairing Tank.";
            Debug.Log(StatusMessage);
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void MoveRatToForSaleTank(string ratId)
        {
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            string reason;
            if (!EnclosureSystem.TryAssignToForSale(Save, rat, GameTime,
                ForSaleHabitatCapacity, out reason))
            {
                StatusMessage = reason;
                if (ui != null) ui.Refresh(true);
                return;
            }

            RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving tanks", GameTime,
                "Moved to For Sale Tank");
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraFollowSelectedRat = true;
            if (rats != null) rats.SetSelected(rat.id);
            cameraView = HabitatCameraView.Breeding;
            StatusMessage = ColonyFactory.DisplayName(rat) + " moved to the For Sale Tank.";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void MoveAllEligibleRatsToForSaleTank()
        {
            if (Save == null) return;
            int moved;
            string reason;
            List<RatData> movedRats;
            if (!EnclosureSystem.TryAssignAllSellableToForSale(Save, GameTime,
                ForSaleHabitatCapacity, out moved, out reason, out movedRats))
            {
                StatusMessage = reason;
                if (ui != null) ui.Refresh(true);
                return;
            }

            StatusMessage = reason;
            if (moved == 0)
            {
                if (ui != null) ui.Refresh(true);
                return;
            }

            foreach (RatData rat in movedRats)
            {
                if (rat == null) continue;
                RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving tanks", GameTime,
                    "Moved to For Sale Tank");
            }
            EnclosureSystem.RecalculateAssignments(Save);
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public bool CanReturnRatFromForSaleTank(RatData rat)
        {
            return EnclosureSystem.CanReturnFromForSale(rat);
        }

        public void RemoveRatFromForSaleTank(string ratId)
        {
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            RatEnclosure destination;
            string reason;
            if (!EnclosureSystem.TryReturnFromForSale(Save, rat,
                PairingHabitatCapacity, out destination, out reason))
            {
                StatusMessage = reason;
                if (ui != null) ui.Refresh(true);
                return;
            }

            if (EnclosureSystem.IsActiveBreedingParticipant(rat))
            {
                breedingOpen = false;
                parentAId = null;
                parentBId = null;
                breedingSelectionSlot = BreedingParentSlot.None;
                EnclosureSystem.ClearBreedingPair();
            }

            RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving tanks", GameTime,
                "Returned to " + EnclosureSystem.Label(destination));
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraFollowSelectedRat = true;
            if (rats != null) rats.SetSelected(rat.id);
            cameraView = CameraViewForRat(rat);
            StatusMessage = ColonyFactory.DisplayName(rat) + " returned to " +
                EnclosureSystem.Label(destination) + ".";
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
                StatusMessage = "The selected rat is not in the Pairing Tank.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            if ((rat.stage == RatStage.Adult || rat.stage == RatStage.Mature) && rat.sex == RatSex.Female &&
                (EnclosureSystem.IsPregnant(Save, rat) || EnclosureSystem.HasDependentPinkies(Save, rat.id)))
            {
                StatusMessage = ColonyFactory.DisplayName(rat) + " must remain in the Pairing Tank until her pregnancy and dependent litter are complete.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            RatEnclosure previousEnclosure = rat.enclosure;
            rat.enclosure = EnclosureSystem.StandardEnclosure(Save, rat);
            rat.pairingHabitatAssigned = false;
            if (previousEnclosure == RatEnclosure.Pairing)
                RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving tanks", GameTime,
                    "Moved from Pairing Tank");
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = rat.id;
            cameraFollowSelectedRat = true;
            if (rats != null) rats.SetSelected(rat.id);
            cameraView = CameraViewForRat(rat);
            StatusMessage = "[Rat Empire] " + ColonyFactory.DisplayName(rat) + " removed from Pairing Tank.";
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
                StatusMessage = "The Pairing Tank is already empty.";
                if (ui != null) ui.Refresh(false);
                return;
            }
            pairingMoveAllConfirmationPending = true;
            StatusMessage = "Move every Pairing Tank rat out? Mothers and pups will stay together in a normal tank.";
            if (ui != null) ui.Refresh(true);
        }

        public void CancelMoveAllOutOfPairingHabitat()
        {
            pairingMoveAllConfirmationPending = false;
            StatusMessage = "Pairing Tank evacuation cancelled.";
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
                RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving tanks", GameTime,
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
            StatusMessage = "Moved " + moved + " rat" + (moved == 1 ? string.Empty : "s") + " out of the Pairing Tank.";
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
                if (requestedCount > PairingHabitatCapacity)
                {
                    StatusMessage = "Pairing Tank has " +
                        Math.Max(0, PairingHabitatCapacity - currentPairingCount) +
                        " spaces remaining (" + currentPairingCount + "/" +
                        PairingHabitatCapacity + "). Upgrade Pairing Tank Capacity or move rats out.";
                    PairingHabitatSystem.RecordCapacityBlocked(StatusMessage, SimulationSpeed);
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
                    RatActivitySystem.SetCurrent(Save, rat, "movement", "Moving tanks", GameTime,
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

            tankCameraPanState.Recenter();
            cameraView = HabitatPages[next];
            cameraFocusNest = false;
            cameraFollowSelectedRat = false;
            habitatZoomOffset = 0f;
            habitatZoomFocusOffset = Vector3.zero;
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

        public bool BuyStoreRat(string listingId)
        {
            if (Save == null)
            {
                StatusMessage = "Store data is not ready.";
                if (ui != null) ui.Refresh(false);
                return false;
            }

            StoreSystem.EnsureStoreState(Save);
            int colonyCapacity = UpgradeSystem.ColonyCapacity(Save);
            if (Save.rats != null && Save.rats.Count >= colonyCapacity)
            {
                StatusMessage = "Colony capacity reached (" + colonyCapacity + "). Buy a capacity upgrade first.";
                if (ui != null) ui.Refresh(false);
                return false;
            }
            StoreRatListingData listing = StoreSystem.FindListing(Save, listingId);
            if (listing == null)
            {
                StatusMessage = "That market rat is no longer available.";
                if (ui != null) ui.Refresh(true);
                return false;
            }
            if (Save.colonyCredits < listing.price)
            {
                StatusMessage = "Not enough dollars — need $" + listing.price + " to buy " + ColonyFactory.DisplayName(listing) + ".";
                if (ui != null) ui.Refresh(false);
                return false;
            }

            RatData purchased = StoreSystem.CreatePurchasedRat(listing, GameTime);
            if (purchased == null)
            {
                StatusMessage = "The market rat could not be created.";
                if (ui != null) ui.Refresh(false);
                return false;
            }

            Save.rats.Add(purchased);
            Save.ratIds.Add(purchased.id);
            RatNameSystem.EnsureUniqueName(Save, purchased, GameTime);
            Save.colonyCredits -= listing.price;
            Save.storeRatListings.Remove(listing);
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = purchased.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(purchased);
            cameraFollowSelectedRat = true;
            StatusMessage = ColonyFactory.DisplayName(purchased) + " joined the colony for $" + listing.price + ".";
            SaveSystem.Save(Save);
            if (ui != null) ui.CompleteStorePurchaseAttempt(listingId, true);
            RefreshWorldAndUi(true, true);
            return true;
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

        public int StoreListingCapacity
        {
            get { return UpgradeSystem.StoreListingCount(Save); }
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
            StatusMessage = "Store quality upgraded. New listings can reach " + newCap +
                "; the next restock will offer " + StoreListingCapacity + " rats.";
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

        public void PurchasePairingHabitatCapacityUpgrade()
        {
            if (Save == null) return;
            UpgradeSystem.EnsureState(Save);
            int cost = UpgradeSystem.PairingHabitatCapacityUpgradeCost(Save);
            if (Save.colonyCredits < cost)
            {
                StatusMessage = "Not enough dollars. Pairing Tank Capacity upgrade costs $" + cost + ".";
                if (ui != null) ui.Refresh(false);
                return;
            }

            int newCapacity;
            if (!UpgradeSystem.PurchasePairingHabitatCapacityUpgrade(Save, out newCapacity)) return;
            StatusMessage = "Pairing Tank capacity upgraded to " + newCapacity +
                " occupants. Existing residents were kept.";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void RestockStoreNow()
        {
            if (Save == null) return;
            StoreRestockResult result = StoreSystem.RestockNowWithResult(Save, GameTime);
            StatusMessage = "Rat Market restocked.";
            ShowTransientRestockSaleAlert(result.autoSaleMessage);
            SaveSystem.Save(Save);
            if (ui != null) ui.Refresh(true);
        }

        private void SetHabitatCameraView(HabitatCameraView view)
        {
            tankCameraPanState.Recenter();
            cameraView = NormalizeHabitatView(view);
            cameraFocusNest = false;
            int settled = Array.IndexOf(HabitatPages, cameraView);
            habitatSettledPageIndex = settled < 0 ? habitatSettledPageIndex : settled;
            habitatPageSettling = false;
            habitatSwipeLockUntil = 0f;
            habitatZoomOffset = 0f;
            habitatZoomFocusOffset = Vector3.zero;
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

            tankCameraPanState.Recenter();

            ClearPendingSelectionActions();
            habitatZoomOffset = 0f;
            habitatZoomFocusOffset = Vector3.zero;
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
        /// Changes a living colony rat's player-owned favorite flag and saves
        /// it immediately. The UI refresh is intentionally left to the caller
        /// so this action cannot also select the rat or move the habitat camera.
        /// </summary>
        public bool SetRatFavorite(string ratId, bool isFavorite)
        {
            RatData rat = BreedingSystem.FindRat(Save, ratId);
            if (rat == null) return false;
            bool previous = rat.isFavorite;
            string reason;
            if (!RatFavoriteSystem.SetFavorite(Save, ratId, isFavorite, out reason))
            {
                StatusMessage = reason;
                if (ui != null) ui.Refresh(true);
                return false;
            }
            if (previous == isFavorite) return true;
            if (SaveSystem.Save(Save)) return true;
            rat.isFavorite = previous;
            return false;
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
            habitatZoomFocusOffset = Vector3.zero;
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
            // Developer birth testing still uses the production movement gate:
            // make the pregnancy due now, then let the mother walk to the nest
            // before the litter is created.
            pending.dueAt = GameTime;
            List<LitterData> litters;
            bool sequenceChanged;
            int births = ProcessDuePregnanciesAtNest(out litters, out sequenceChanged);
            if (births <= 0)
            {
                StatusMessage = "The mother is going to the nest before giving birth.";
                SaveSystem.Save(Save);
                if (ui != null) ui.Refresh(true);
                return;
            }
            foreach (LitterData litter in litters) AnnounceBirth(litter);
            PreparePendingLitterNaming(litters);
            EnclosureSystem.RecalculateAssignments(Save);
            SaveSystem.Save(Save);
            rats.Render(Save, habitat.NestPosition);
            if (ui != null) ui.Refresh(true);
        }

        public void CreateDeveloperPregnancyDueSoon()
        {
            if (Save == null)
            {
                StatusMessage = "Colony data is not ready.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            RatData female = null;
            RatData male = null;
            foreach (RatData candidate in Save.rats)
            {
                if (candidate == null) continue;
                string reason;
                if (candidate.sex == RatSex.Female && female == null &&
                    BreedingSystem.IsBreedEligible(Save, candidate, GameTime, out reason)) female = candidate;
                if (candidate.sex == RatSex.Male && male == null &&
                    BreedingSystem.IsBreedEligible(Save, candidate, GameTime, out reason)) male = candidate;
            }

            if (female == null || male == null)
            {
                StatusMessage = "No eligible adult female and male are available for the pregnancy test.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            PregnancyData pregnancy;
            string startReason;
            if (!BreedingSystem.StartBreeding(Save, female, male, GameTime, out pregnancy, out startReason) || pregnancy == null)
            {
                StatusMessage = string.IsNullOrEmpty(startReason) ? "The test pregnancy could not start." : startReason;
                if (ui != null) ui.Refresh(true);
                return;
            }

            // This deliberately shortens only this developer-created record;
            // the production game-clock and normal gestation configuration are
            // untouched. The standard due-pregnancy route still has to move
            // the mother to the nest before it can create the litter.
            long testDuration = Math.Max(1L, GameConfig.GameDayMs / 24L);
            pregnancy.startedAt = GameTime;
            pregnancy.dueAt = GameTime + testDuration;
            pregnancy.gestationDurationMs = testDuration;
            pregnancy.birthApproachStarted = false;
            pregnancy.birthApproachStartedAt = 0L;
            pregnancy.birthFailureReason = string.Empty;
            pregnancy.birthCommitState = 0;
            StatusMessage = "Test pregnancy " + pregnancy.id + " created for " +
                ColonyFactory.DisplayName(female) + ". Due at " + FormatSimulationTimestamp(pregnancy.dueAt) + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        private bool AnnounceNewNaturalDeaths()
        {
            if (Save == null || Save.retiredRats == null) return false;
            bool changed = false;
            foreach (RatData rat in Save.retiredRats)
            {
                if (rat == null || rat.removalDisposition != RatRemovalDisposition.NaturalDeath ||
                    rat.naturalDeathAnnouncementLogged) continue;

                // Mark the retired record before exposing the event. The
                // guard is serialized in the same save pass as the history
                // entry, so a refresh cannot replay a death announcement.
                rat.naturalDeathAnnouncementLogged = true;
                StatusMessage = ColonyFactory.DisplayName(rat) + " died.";
                changed = true;
            }
            return changed;
        }

        private bool AnnounceCompletedWeanings()
        {
            if (Save == null || Save.litters == null)
            {
                nextWeaningAnnouncementGameTime = long.MaxValue;
                weaningScheduleInitialized = true;
                return false;
            }

            bool changed = false;
            int recordsScanned = 0;
            int weaningsAnnounced = 0;
            nextWeaningAnnouncementGameTime = long.MaxValue;
            long historicalLitterScan = RuntimePerformanceDiagnostics.Begin(
                PerformanceProbeArea.MaintenanceHistoricalLitters);
            foreach (LitterData litter in Save.litters)
            {
                recordsScanned++;
                if (litter == null || litter.weaningAnnouncementLogged ||
                    litter.weaningTimestamp <= 0L) continue;

                if (GameTime < litter.weaningTimestamp)
                {
                    if (litter.weaningTimestamp < nextWeaningAnnouncementGameTime)
                        nextWeaningAnnouncementGameTime = litter.weaningTimestamp;
                    continue;
                }

                litter.weaningAnnouncementLogged = true;
                RatData mother = BreedingSystem.FindHistoricalRat(Save, litter.motherId);
                StatusMessage = mother == null
                    ? "A litter is fully weaned."
                    : ColonyFactory.DisplayName(mother) + "'s litter is fully weaned.";
                changed = true;
                weaningsAnnounced++;
            }
            RuntimePerformanceDiagnostics.End(
                PerformanceProbeArea.MaintenanceHistoricalLitters, historicalLitterScan);
            RuntimePerformanceDiagnostics.RecordMaintenanceWorkCount(
                PerformanceProbeArea.MaintenanceHistoricalLitters, recordsScanned, weaningsAnnounced);
            weaningScheduleInitialized = true;
            return changed;
        }

        /// <summary>
        /// Starts/resumes due pregnancies at the nest and resolves them only
        /// after the live mother behavior reports arrival. The pregnancy
        /// record owns the persisted approach flag, so a reload resumes the
        /// route rather than creating duplicate litters or teleporting the
        /// mother for a birth frame.
        /// </summary>
        private int ProcessDuePregnanciesAtNest(out List<LitterData> newLitters,
            out bool sequenceChanged)
        {
            newLitters = new List<LitterData>();
            sequenceChanged = false;
            if (Save == null || rats == null) return 0;

            List<PregnancyData> duePregnancies = BreedingSystem.GetDuePendingPregnancies(Save, GameTime);
            foreach (PregnancyData pregnancy in duePregnancies)
            {
                if (pregnancy == null || string.IsNullOrEmpty(pregnancy.motherId)) continue;
                if (!BirthRetryReady(pregnancy)) continue;
                RatData mother = BreedingSystem.FindRat(Save, pregnancy.motherId);
                if (mother == null)
                {
                    sequenceChanged |= MarkBirthBlockedAndScheduleRetry(pregnancy,
                        "Mother record is unavailable; pregnancy retained for retry.");
                    continue;
                }
                if (mother.enclosure == RatEnclosure.ForSale)
                {
                    string transferReason;
                    if (!MoveSaleBirthFamilyToPairing(mother, pregnancy, out transferReason))
                    {
                        sequenceChanged |= ReleaseBirthApproachForMother(mother, pregnancy);
                        sequenceChanged |= MarkBirthBlockedAndScheduleRetry(pregnancy, transferReason);
                        continue;
                    }
                    sequenceChanged = true;
                }
                if (!EnclosureSystem.HasNest(mother.enclosure))
                {
                    sequenceChanged |= ReleaseBirthApproachForMother(mother, pregnancy);
                    sequenceChanged |= MarkBirthBlockedAndScheduleRetry(pregnancy,
                        "No valid nest is available in the mother's tank; pregnancy retained.");
                    continue;
                }

                string currentOccupant;
                if (birthQueueReservations.TryGetOccupant(mother.enclosure, out currentOccupant) &&
                    !string.Equals(currentOccupant, mother.id, StringComparison.Ordinal))
                {
                    // Only the tank's current reservation may approach its
                    // caregiver position. All other due mothers remain
                    // pregnant, visibly queued, and stationary.
                    if (SetBirthWaitingForNest(pregnancy, mother)) sequenceChanged = true;
                    continue;
                }

                if (!birthQueueReservations.TryReserve(mother.enclosure, mother.id))
                {
                    if (SetBirthWaitingForNest(pregnancy, mother)) sequenceChanged = true;
                    continue;
                }

                RatHabitatBehavior behavior;
                bool hasBehavior = rats.TryGetRatBehavior(mother.id, mother, out behavior) && behavior != null;
                bool rootAtNest = TryGetMotherRootAtNest(mother, out _);
                if (hasBehavior && rootAtNest)
                    behavior.ConfirmBirthApproachArrivalAtNest();
                bool arrivedAtNest = rootAtNest || (hasBehavior && behavior.BirthApproachAtNest);
                if (!hasBehavior && !rootAtNest)
                {
                    string presentationFailureReason = "Mother presentation is not ready; pregnancy retained for retry.";
                    string tankLabel = EnclosureSystem.GetDefinition(mother.enclosure).label;
                    birthQueueReservations.Release(mother.enclosure, mother.id);
                    birthApproachWatchdogs.Remove(mother.id);
                    SetBirthWaitingForNest(pregnancy, mother);
                    birthQueueRouteFailures++;
                    Debug.LogWarning("[Rat Habitat] Birth presentation unavailable | pregnancyId=" +
                        pregnancy.id + " | motherId=" + mother.id + " | tank=" + tankLabel +
                        " | reason=" + presentationFailureReason + " | parkedAtNest=" + rootAtNest);
                    birthPresentationRepairRequested = true;
                    sequenceChanged = true;
                    sequenceChanged |= MarkBirthBlockedAndScheduleRetry(pregnancy, presentationFailureReason);
                    continue;
                }

                bool approachStartedNow = false;
                if (hasBehavior && !behavior.BirthApproachActive && !rootAtNest)
                {
                    Vector3 caregiverTarget = EnclosureSystem.GetNestCaregiverPosition(mother.enclosure);
                    if (!behavior.BeginBirthApproach(caregiverTarget))
                    {
                        birthQueueReservations.Release(mother.enclosure, mother.id);
                        birthApproachWatchdogs.Remove(mother.id);
                        SetBirthWaitingForNest(pregnancy, mother);
                        birthQueueRouteFailures++;
                        sequenceChanged = true;
                        sequenceChanged |= MarkBirthBlockedAndScheduleRetry(pregnancy,
                            "Mother could not start a safe route to the nest; retrying.");
                        continue;
                    }
                    approachStartedNow = true;
                }

                // A mother whose transaction failed is still at the nest and
                // owns this reservation. Keep the persisted waiting label on
                // retries; only a newly started/resumed route clears it.
                if (approachStartedNow || !pregnancy.birthApproachStarted)
                {
                    if (!arrivedAtNest) birthRetryAfterRealtime.Remove(pregnancy.id);
                    pregnancy.birthApproachStarted = true;
                    pregnancy.birthApproachStartedAt = GameTime;
                    pregnancy.birthWaitingForNest = arrivedAtNest;
                    RatActivitySystem.SetCurrent(Save, mother,
                        arrivedAtNest ? "birth-waiting" : "birth-approach",
                        arrivedAtNest ? "Waiting to give birth" : "Going to give birth", GameTime);
                    sequenceChanged = true;
                }
                else if (arrivedAtNest && !pregnancy.birthWaitingForNest)
                {
                    // A stable visible root at the nest is an arrival even if
                    // its behavior component could not be recovered or its
                    // route's internal arrival flag was missed. Preserve the
                    // real-time retry cooldown after transaction failures.
                    pregnancy.birthWaitingForNest = true;
                    RatActivitySystem.SetCurrent(Save, mother,
                        "birth-waiting", "Waiting to give birth", GameTime);
                    sequenceChanged = true;
                }

                if (hasBehavior && !arrivedAtNest && !birthApproachWatchdogs.ContainsKey(mother.id))
                {
                    birthApproachWatchdogs[mother.id] = new BirthApproachWatchdog
                    {
                        pregnancy = pregnancy,
                        mother = mother,
                        motherId = mother.id,
                        enclosure = mother.enclosure,
                        lastPosition = behavior.transform.position,
                    };
                }

                if (approachStartedNow)
                {
                    RatActivitySystem.SetCurrent(Save, mother, "birth-approach", "Going to give birth", GameTime);
                }
                if (!arrivedAtNest)
                {
                    // Do not replan or mark this as blocked on every unrelated
                    // maintenance pass. The live route continues and arrival
                    // will wake the next birth-resolution pass.
                    continue;
                }

                LitterData litter;
                string reason;
                bool birthCommitted = BreedingSystem.FinishPregnancyForBatch(
                    Save, pregnancy.id, GameTime, out litter, out reason);
                if (!birthCommitted || litter == null)
                {
                    string failureReason = string.IsNullOrWhiteSpace(reason)
                        ? (birthCommitted
                            ? "Birth transaction reported success without returning a litter."
                            : "Birth transaction returned false without a reason.")
                        : reason.Trim();
                    string tankLabel = EnclosureSystem.GetDefinition(mother.enclosure).label;
                    Debug.LogWarning("[Rat Habitat] FinishPregnancyForBatch failed | pregnancyId=" +
                        pregnancy.id + " | motherId=" + mother.id + " | tank=" + tankLabel +
                        " | reason=" + failureReason);

                    // A transaction failure is not a route failure. Keep the
                    // mother physically at the nest, retain her reservation
                    // and persisted approach timestamp, and retry this same
                    // pending pregnancy after the real-time retry cooldown.
                    // The next queued mother cannot enter until this one has
                    // committed successfully.
                    pregnancy.birthWaitingForNest = true;
                    sequenceChanged = true; // attempt count/time and retry state are authoritative
                    sequenceChanged |= RatActivitySystem.SetCurrent(Save, mother,
                        "birth-waiting", "Waiting to give birth", GameTime);
                    sequenceChanged |= MarkBirthBlockedAndScheduleRetry(pregnancy, failureReason);
                    continue;
                }
                birthRetryAfterRealtime.Remove(pregnancy.id);
                birthQueueReservations.Release(mother.enclosure, mother.id);
                birthApproachWatchdogs.Remove(mother.id);
                pregnancy.birthWaitingForNest = false;
                pregnancy.birthApproachStarted = false;
                pregnancy.birthApproachStartedAt = 0L;
                birthQueueSuccessfulBirths++;

                // FinishPregnancy has now written the litter and recovery
                // deadline. Transition the existing root into caregiving at
                // its actual arrival position before the render pass creates
                // the new pinkie roots.
                if (hasBehavior) behavior.FinishBirthApproach();
                newLitters.Add(litter);
                sequenceChanged = true;
            }

            return newLitters.Count;
        }

        private bool SetBirthWaitingForNest(PregnancyData pregnancy, RatData mother)
        {
            if (pregnancy == null || mother == null) return false;
            bool changed = !pregnancy.birthWaitingForNest || pregnancy.birthApproachStarted;
            if (pregnancy.birthApproachStarted && rats != null &&
                rats.TryGetRatBehavior(mother.id, out RatHabitatBehavior behavior) &&
                behavior != null && behavior.BirthApproachActive)
                behavior.CancelBirthApproach();
            birthQueueReservations.Release(mother.enclosure, mother.id);
            birthApproachWatchdogs.Remove(mother.id);
            pregnancy.birthWaitingForNest = true;
            pregnancy.birthApproachStarted = false;
            pregnancy.birthApproachStartedAt = 0L;
            changed |= RatActivitySystem.SetCurrent(Save, mother,
                "birth-waiting", "Waiting to give birth", GameTime);
            return changed;
        }

        private bool TryGetMotherRootAtNest(RatData mother, out Vector3 position)
        {
            position = Vector3.zero;
            if (mother == null || rats == null || !EnclosureSystem.HasNest(mother.enclosure) ||
                !rats.TryGetRatRoot(mother.id, out Transform root) || root == null) return false;
            position = root.position;
            return EnclosureSystem.IsInsideNestCaregiverZone(mother.enclosure, position);
        }

        private static void ClearBirthApproachState(PregnancyData pregnancy)
        {
            if (pregnancy == null) return;
            pregnancy.birthApproachStarted = false;
            pregnancy.birthApproachStartedAt = 0L;
            pregnancy.birthWaitingForNest = false;
        }

        private bool ReleaseBirthApproachForMother(RatData mother, PregnancyData pregnancy)
        {
            if (mother == null) return false;
            bool changed = pregnancy != null &&
                (pregnancy.birthApproachStarted || pregnancy.birthWaitingForNest || pregnancy.birthApproachStartedAt != 0L);
            if (rats != null && rats.TryGetRatBehavior(mother.id, out RatHabitatBehavior behavior) &&
                behavior != null && behavior.BirthApproachActive)
                behavior.CancelBirthApproach();
            birthQueueReservations.Release(mother.enclosure, mother.id);
            birthApproachWatchdogs.Remove(mother.id);
            ClearBirthApproachState(pregnancy);
            changed |= RatActivitySystem.SetCurrent(Save, mother, "pregnant", "Pregnant", GameTime);
            return changed;
        }

        private bool MoveSaleBirthFamilyToPairing(RatData mother, PregnancyData pregnancy,
            out string reason)
        {
            List<RatData> movedFamily;
            if (!EnclosureSystem.TryPrepareForSaleBirth(Save, mother, pregnancy,
                PairingHabitatCapacity, out movedFamily, out reason)) return false;

            foreach (RatData member in movedFamily)
                RatActivitySystem.SetCurrent(Save, member, "movement", "Moving tanks", GameTime,
                    "Moved with family to Pairing Tank for birth");
            EnclosureSystem.RecalculateAssignments(Save);
            cameraView = HabitatCameraView.Pairing;
            cameraFocusNest = false;
            cameraFollowSelectedRat = false;
            habitatZoomFocusOffset = Vector3.zero;
            // This transfer is part of the same due-pregnancy maintenance
            // batch. The caller queues its complete resulting state once,
            // including the resumed birth approach flag and any retry reason.
            RefreshWorldAndUi(false);
            return true;
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

            string pupWord = createdPups == 1 ? "pup" : "pups";
            string announcement = ColonyFactory.DisplayName(mother) + " has given birth to " +
                createdPups + " " + pupWord + "!";
            StatusMessage = announcement;
            litter.birthAnnouncementLogged = true;
            // The live birth batch persists this together with the pregnancy,
            // pups, and pending naming queue in one full-colony write.
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
                case DeveloperRatPreset.ForceWhiteAlbinoPreview:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "c", "c", "D", "D", "s", "s");
                    break;
                case DeveloperRatPreset.SpottedBlack:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "S", "S");
                    break;
                case DeveloperRatPreset.MarkedTest:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "S", "S");
                    break;
                case DeveloperRatPreset.MarkingMutationTest:
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s");
                    GeneticsSystem.ForceMarkingMutation(genotype, "developer-test", GameTime);
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
            string name = RatNameSystem.GenerateAvailableName(Save, developerId, sex, GameTime);
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
            if (preset == DeveloperRatPreset.MarkedTest)
            {
                // This is a deterministic presentation fixture only. It uses
                // the real saved genotype/phenotype pipeline and is never
                // selected by normal colony or store generation.
                rat.coatColorVariant = "black";
                rat.coatTone = 1f;
                rat.markingFamily = "Variegated";
            }
            rat.phenotype = GeneticsSystem.DerivePhenotype(RatStage.Adult, rat.genotype,
                rat.coatColorVariant, rat.coatTone);
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            if (preset == DeveloperRatPreset.ForceWhiteAlbinoPreview)
            {
                // This is a visual fixture only. Keep the real c/c genotype,
                // but make every stored phenotype color unambiguously neutral
                // so Store, habitat, My Rats, and profile use the same white
                // albino test rat through the normal presentation pipeline.
                rat.coatColorVariant = "albino";
                rat.coatTone = 1f;
                rat.phenotype.coatColorId = "albino";
                rat.phenotype.coatColorLabel = "Albino";
                rat.phenotype.coatColorHex = "#F8F7F1";
                rat.phenotype.accentHex = "#D9DCE2";
                rat.phenotype.spotted = false;
                rat.phenotype.markingFamily = "Albino masking";
                rat.phenotype.markingsLabel = "Albino masking";
                rat.phenotype.furRevealed = true;
            }
            Debug.Log("[Rat Habitat] Developer spawn audit: rat=" + rat.name +
                " genotype=" + DeveloperGeneSummary(rat.genotype) +
                " coatColorId=" + rat.phenotype.coatColorId +
                " coatColorHex=" + rat.phenotype.coatColorHex +
                " accentHex=" + rat.phenotype.accentHex +
                " spotted=" + rat.phenotype.spotted +
                " materialAudit=deferred-to-RatVisualFactory");
            Save.rats.Add(rat);
            Save.ratIds.Add(rat.id);
            RatNameSystem.EnsureUniqueName(Save, rat, GameTime);
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

        /// <summary>
        /// Creates a deterministic, already-born litter for validating the
        /// pinkie placement path. This is intentionally a developer action:
        /// it adds real RatData/LitterData records so save/load, rendering,
        /// nursing, and the normal relationship-based assignment code are
        /// exercised without introducing a second test-only visual path.
        /// </summary>
        public void SpawnDeveloperPinkieLitter()
        {
            if (Save == null)
            {
                StatusMessage = "Colony data is not ready.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            Save.EnsureLists();
            long now = GameTime;
            string motherId = ColonyFactory.NewId("dev_pinkie_mother");
            string litterId = ColonyFactory.NewId("dev_pinkie_litter");
            GenotypeData founder = GeneticsSystem.CreateFounder(
                "B", "B", "C", "C", "D", "D", "s", "s");
            float motherAgeDays = GameConfig.FemaleSexualMaturityDays + 30f;
            RatData mother = ColonyFactory.CreateRat(
                motherId,
                RatNameSystem.GenerateAvailableName(Save, motherId, RatSex.Female, now),
                RatSex.Female,
                now - (long)(motherAgeDays * GameConfig.GameDayMs),
                0,
                founder.Clone(),
                new TraitData(55f, 80f, 80f),
                RatStage.Adult);
            mother.enclosure = RatEnclosure.Pairing;
            mother.pairingHabitatAssigned = true;
            mother.nursing = true;
            mother.nursingUntil = now + (long)(GameConfig.WeaningDays * GameConfig.GameDayMs);
            mother.recoveryUntil = now + (long)(GameConfig.RecoveryDays * GameConfig.GameDayMs);
            mother.reproductiveState = ReproductiveState.Recovery;
            mother.nursingPupId = null;
            mother.nursingInteractionUntil = 0L;
            mother.nursingRetryAt = now;
            RatActivitySystem.SetCurrent(Save, mother, "nursing", "Nursing", now);
            Save.rats.Add(mother);
            Save.ratIds.Add(mother.id);
            RatNameSystem.EnsureUniqueName(Save, mother, now);

            var litter = new LitterData
            {
                id = litterId,
                motherId = mother.id,
                fatherId = null,
                litterName = "Developer Pinkie Placement Test",
                size = 5,
                generation = mother.generation + 1,
                birthTimestamp = now,
                weaningTimestamp = now + (long)(GameConfig.WeaningDays * GameConfig.GameDayMs),
            };

            for (int index = 0; index < litter.size; index++)
            {
                RatSex sex = index % 2 == 0 ? RatSex.Female : RatSex.Male;
                string pupId = ColonyFactory.NewId("dev_pinkie");
                RatData pup = ColonyFactory.CreateRat(
                    pupId,
                    RatNameSystem.GenerateAvailableName(Save, pupId, sex, now),
                    sex,
                    now,
                    litter.generation,
                    // Use a cloned genotype rather than UnityEngine.Random so
                    // repeated test litters keep the same phenotype pipeline.
                    founder.Clone(),
                    new TraitData(55f, 80f, 80f),
                    RatStage.Pinkie);
                pup.motherId = mother.id;
                pup.fatherId = null;
                pup.litterId = litter.id;
                pup.birthTimestamp = now;
                pup.growthTimestamp = now;
                pup.ageDays = 0f;
                pup.enclosure = RatEnclosure.Pairing;
                pup.pairingHabitatAssigned = true;
                pup.nursing = false;
                pup.lastNursedAt = now - (long)index * GameConfig.GameDayMs / 24L;
                RatActivitySystem.SetCurrent(Save, pup, "growing", "Growing", now);
                Save.rats.Add(pup);
                Save.ratIds.Add(pup.id);
                RatNameSystem.EnsureUniqueName(Save, pup, now);
                litter.pupIds.Add(pup.id);
            }

            Save.litters.Add(litter);
            ScheduleNextWeaningAnnouncement();
            GrowthSystem.RefreshRatAges(Save, now);
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = mother.id;
            selectedObjectId = null;
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            deleteConfirmationRatId = null;
            sellConfirmationRatId = null;
            euthanizeConfirmationRatId = null;
            StatusMessage = "Spawned a five-pinkie nest placement test litter.";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void SetSimulationSpeed(float speed)
        {
            if (Save == null || Save.clock == null) return;
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            float normalized = GrowthSystem.NormalizeSpeed(speed);
            // Settle elapsed clock time at the previous rate, then re-anchor
            // the timestamp before the selected rate takes effect. This keeps
            // a speed-button press from retroactively scaling the previous
            // interval while presentation responds immediately.
            GrowthSystem.ChangeSpeedAtTimestamp(Save, normalized, GameConfig.NowMs());
            // Speed-button taps commonly happen in quick succession. Queue
            // the newest clock anchor/rate so SaveSystem can coalesce them
            // instead of synchronously serializing the colony on every tap.
            SaveSystem.QueueSave(Save, "GameBootstrap.SetSimulationSpeed");
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

        public bool CanMoveRatToForSaleTank(RatData rat)
        {
            return rat != null && rat.enclosure != RatEnclosure.ForSale &&
                EnclosureSystem.CanEnterForSaleTank(Save, rat, GameTime) &&
                !EnclosureSystem.HasDependentPinkies(Save, rat.id) &&
                ForSaleHabitatCount < ForSaleHabitatCapacity;
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
            // Do not infer illness or injury from a low Health stat. This
            // project currently has no authoritative active sickness/injury
            // record, so showing that warning here would falsely block or
            // alarm otherwise healthy rats.
            if (hasActiveLitter) warnings.Add("Has offspring");
            if (hasAnyLitter) warnings.Add("Parent of a litter");
            if (rat.enclosure == RatEnclosure.Breeding || rat.enclosure == RatEnclosure.Pairing)
                warnings.Add("Special tank: " + EnclosureSystem.Label(rat.enclosure));

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
            if (ui != null) ui.RefreshImmediate();
        }

        public void CancelSellSelectedRat()
        {
            sellConfirmationRatId = null;
            StatusMessage = "Sale cancelled.";
            if (ui != null) ui.RefreshImmediate();
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
            EnclosureSystem.ClearSaleReturnTank(rat);
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
            RatNameSystem.RecordUsage(Save, rat.name, rat.sex, rat.id, GameTime);
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
            StatusMessage = "Game fully reset. A new randomized starter pair, tanks, clock, genetics, and UI state were restored.";
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
            GrowthSystem.SetSimulationPaused(HasPendingLitterNaming);
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            SaveSystem.Save(Save);
        }

        public bool TryRenameRat(string ratId, string rawName, out string error)
        {
            error = string.Empty;
            RatData rat = BreedingSystem.FindHistoricalRat(Save, ratId);
            string name;
            if (rat == null) { error = "That rat is no longer available."; return false; }
            if (!RatNameSystem.TrySanitizePlayerName(rawName, out name, out error)) return false;
            if (!RatNameSystem.IsNameAvailable(Save, name, rat.id))
            {
                error = "That name is already in use by another rat or store listing.";
                return false;
            }
            rat.name = name;
            rat.nameWasPlayerAssigned = true;
            RatNameSystem.RecordUsage(Save, rat.name, rat.sex, rat.id, GameTime);
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
            return true;
        }

        public string RandomizeRatName(string ratId)
        {
            RatData rat = BreedingSystem.FindHistoricalRat(Save, ratId);
            if (rat == null) return string.Empty;
            return RatNameSystem.GenerateAvailableName(Save,
                rat.id + "|rename|" + NextNameRandomizeCount(rat.id), rat.sex, GameTime);
        }

        public bool TryRenamePendingPup(string pupId, string rawName, out string error)
        {
            error = string.Empty;
            RatData pup = BreedingSystem.FindRat(Save, pupId);
            string name;
            if (pup == null) { error = "That newborn is no longer available."; return false; }
            if (!RatNameSystem.TrySanitizePlayerName(rawName, out name, out error)) return false;
            if (!RatNameSystem.IsNameAvailable(Save, name, pup.id))
            {
                error = "That name is already in use.";
                return false;
            }
            foreach (RatData other in PendingNamingPups)
                if (other != null && other.id != pup.id && RatNameSystem.NormalizeForComparison(other.name) == RatNameSystem.NormalizeForComparison(name))
                {
                    error = "Each pup in the litter needs a different name.";
                    return false;
                }
            pup.name = name;
            pup.nameWasPlayerAssigned = true;
            RatNameSystem.RecordUsage(Save, name, pup.sex, pup.id, GameTime);
            return true;
        }

        public string RandomizePendingPupName(string pupId)
        {
            RatData pup = BreedingSystem.FindRat(Save, pupId);
            if (pup == null) return string.Empty;
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (RatData other in PendingNamingPups)
                if (other != null && other.id != pup.id) occupied.Add(RatNameSystem.NormalizeForComparison(other.name));
            string name = RatNameSystem.GenerateAvailableName(Save,
                pup.id + "|newborn-randomize|" + NextNameRandomizeCount(pup.id), pup.sex, GameTime);
            int suffix = 2;
            string baseName = name;
            while (occupied.Contains(RatNameSystem.NormalizeForComparison(name))) name = baseName + " " + suffix++;
            pup.name = name;
            pup.nameWasPlayerAssigned = true;
            RatNameSystem.RecordUsage(Save, name, pup.sex, pup.id, GameTime);
            return name;
        }

        private int NextNameRandomizeCount(string ratId)
        {
            int count;
            ratNameRandomizeCounts.TryGetValue(ratId ?? string.Empty, out count);
            count++;
            ratNameRandomizeCounts[ratId ?? string.Empty] = count;
            return count;
        }

        public void RandomizeAllPendingPupNames()
        {
            foreach (RatData pup in PendingNamingPups) if (pup != null) RandomizePendingPupName(pup.id);
            SaveSystem.Save(Save);
            if (ui != null) ui.RefreshPendingNamingPopup();
        }

        public bool ApprovePendingLitterNames()
        {
            if (Save == null || PendingNamingLitter == null) return false;
            Save.pendingNamingLitterIds.Remove(PendingNamingLitter.id);
            Save.clock.lastRealTimestamp = GameConfig.NowMs();
            // Persist every validated player-entered name and the cleared
            // naming queue before releasing the modal pause. If storage fails,
            // leave the colony paused so the player can retry safely.
            if (!SaveSystem.Save(Save)) return false;
            GrowthSystem.SetSimulationPaused(HasPendingLitterNaming || WelcomePopupPending);
            if (ui != null)
            {
                if (HasPendingLitterNaming) ui.OpenPendingLitterNaming();
                else ui.ClosePendingLitterNaming();
            }
            return true;
        }

        public string CustomNamesText(RatSex sex)
        {
            return RatNameSystem.CustomNamesText(Save, sex);
        }

        public List<string> CustomNames(RatSex sex)
        {
            if (Save == null) return new List<string>();
            Save.EnsureLists();
            List<string> source = sex == RatSex.Female ? Save.customFemaleRatNames : Save.customMaleRatNames;
            return source == null ? new List<string>() : new List<string>(source);
        }

        public bool TryAddCustomName(RatSex sex, string rawName, out string error)
        {
            error = string.Empty;
            if (Save == null)
            {
                error = "The colony save is not ready.";
                return false;
            }
            string name;
            if (!RatNameSystem.TrySanitizePlayerName(rawName, out name, out error)) return false;
            Save.EnsureLists();
            List<string> target = sex == RatSex.Female ? Save.customFemaleRatNames : Save.customMaleRatNames;
            string normalized = RatNameSystem.NormalizeForComparison(name);
            for (int index = 0; index < target.Count; index++)
            {
                if (RatNameSystem.NormalizeForComparison(target[index]) == normalized)
                {
                    error = "That custom name is already in the list.";
                    return false;
                }
            }
            target.Add(name);
            CustomNamesClearConfirmationPending = false;
            StatusMessage = "Custom rat names saved.";
            SaveSystem.Save(Save);
            if (ui != null) ui.RefreshCustomNameControls();
            return true;
        }

        public bool RemoveCustomName(RatSex sex, string name)
        {
            if (Save == null || string.IsNullOrEmpty(name)) return false;
            Save.EnsureLists();
            List<string> target = sex == RatSex.Female ? Save.customFemaleRatNames : Save.customMaleRatNames;
            string normalized = RatNameSystem.NormalizeForComparison(name);
            for (int index = 0; index < target.Count; index++)
            {
                if (RatNameSystem.NormalizeForComparison(target[index]) != normalized) continue;
                target.RemoveAt(index);
                CustomNamesClearConfirmationPending = false;
                StatusMessage = "Custom rat names saved.";
                SaveSystem.Save(Save);
                if (ui != null) ui.RefreshCustomNameControls();
                return true;
            }
            return false;
        }

        public void ApplyCustomNameLists(string maleText, string femaleText)
        {
            if (Save == null) return;
            RatNameSystem.SetCustomNames(Save, RatSex.Male, maleText);
            RatNameSystem.SetCustomNames(Save, RatSex.Female, femaleText);
            StatusMessage = "Custom rat names saved.";
            SaveSystem.Save(Save);
            if (ui != null) ui.RefreshCustomNameControls();
        }

        public void RequestClearCustomNameLists()
        {
            CustomNamesClearConfirmationPending = true;
            if (ui != null) ui.RefreshCustomNameControls();
        }

        public void ConfirmClearCustomNameLists()
        {
            if (Save == null) return;
            Save.customMaleRatNames.Clear();
            Save.customFemaleRatNames.Clear();
            CustomNamesClearConfirmationPending = false;
            StatusMessage = "Custom rat names cleared.";
            SaveSystem.Save(Save);
            if (ui != null) ui.RefreshCustomNameControls();
        }

        public void CancelClearCustomNameLists()
        {
            CustomNamesClearConfirmationPending = false;
            if (ui != null) ui.RefreshCustomNameControls();
        }

        private void QueuePendingLitterNaming(LitterData litter)
        {
            if (Save == null || litter == null) return;
            Save.EnsureLists();
            if (!Save.pendingNamingLitterIds.Contains(litter.id)) Save.pendingNamingLitterIds.Add(litter.id);
        }

        private void PreparePendingLitterNaming(List<LitterData> litters)
        {
            if (Save == null || litters == null || litters.Count == 0) return;
            // Birth already assigns and persists a generated name for each
            // pup. In automatic mode those names are final unless the player
            // later chooses to rename a rat, so no modal or simulation pause
            // is needed.
            if (Save.autoNamePinkies) return;
            foreach (LitterData litter in litters) QueuePendingLitterNaming(litter);
            Save.clock.lastRealTimestamp = GameConfig.NowMs();
            GrowthSystem.SetSimulationPaused(true);
            // The caller commits this queue with the complete birth batch.
            if (ui != null) ui.OpenPendingLitterNaming();
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

        public void ToggleAutoNamePinkies()
        {
            if (Save == null) return;
            Save.autoNamePinkies = !Save.autoNamePinkies;
            StatusMessage = Save.autoNamePinkies
                ? "New pinkies will keep their automatically generated names."
                : "You will be asked to name each new litter.";
            SaveSystem.Save(Save);
            if (ui != null) ui.RefreshAutoNamePinkiesControls();
        }

        public void RequestScreenWakeLockFromUserGesture()
        {
            BrowserWakeLockSystem.RequestFromUserGesture(Save);
            if (ui != null) ui.RefreshWakeLockControls();
        }

        public void ToggleAlertCategory(string category)
        {
            if (Save == null || string.IsNullOrEmpty(category)) return;
            bool enabled = !EventLogPolicy.IsCategoryEnabled(Save, category);
            EventLogPolicy.SetCategoryEnabled(Save, category, enabled);
            if (!enabled && string.Equals(liveEventCategory, category, StringComparison.Ordinal))
            {
                liveEventMessage = null;
                liveEventCategory = null;
                liveEventExpiresAt = 0L;
            }
            SaveSystem.Save(Save);
            if (ui != null)
            {
                ui.RefreshAlertPreferenceControls();
                ui.RefreshHeader();
            }
        }

        public void ResetAlertPreferences()
        {
            if (Save == null) return;
            EventLogPolicy.ResetPreferences(Save);
            SaveSystem.Save(Save);
            if (ui != null)
            {
                ui.RefreshAlertPreferenceControls();
                ui.RefreshHeader();
            }
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

        private bool IsNestEntity(SelectableEntity entity)
        {
            if (entity == null || entity.kind != SelectableKind.HabitatObject) return false;
            if (entity.entityId == "pairing_nest") return true;
            HabitatObjectData data = FindObject(entity.entityId);
            return data != null && data.type == HabitatObjectType.Nest;
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
            RefreshWorldAndUi(forceUi, false);
        }

        private void RefreshWorldAndUi(bool forceUi, bool waitForPointerRelease)
        {
            if (rats != null && habitat != null) rats.Render(Save, habitat.NestPosition);
            if (ui != null)
            {
                if (waitForPointerRelease) ui.RequestRefreshAfterPointerRelease(forceUi);
                else ui.Refresh(forceUi);
            }
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
            EnsureTankCameraPanFocus(selected);
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
                desiredPosition = ClampCameraTargetToEnclosure(selected.enclosure,
                    focusPoint + tankCameraPanState.Offset) - cameraForward * normalCameraDistance;
                desiredSize = normalCameraOrthographicSize * (selectedPinkie
                    ? PinkieInspectionOrthographicMultiplier
                    : InspectionOrthographicMultiplier);
            }
            else if (cameraFocusNest && EnclosureSystem.HasNest(cameraFocusNestEnclosure))
            {
                Vector3 focusPoint = EnclosureSystem.GetNestPosition(cameraFocusNestEnclosure) +
                    Vector3.up * 0.42f;
                Vector3 cameraForward = normalCameraRotation * Vector3.forward;
                desiredPosition = ClampCameraTargetToEnclosure(cameraFocusNestEnclosure,
                    focusPoint + tankCameraPanState.Offset) - cameraForward * normalCameraDistance;
                desiredSize = normalCameraOrthographicSize * 0.58f;
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
                frameTarget = ClampCameraTargetToEnclosure(
                    enclosure, frameTarget + habitatZoomFocusOffset + tankCameraPanState.Offset);
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
            Vector2 screenCenter = mainCamera == null ? Vector2.zero : mainCamera.pixelRect.center;
            AdjustHabitatZoomAtScreenPoint(amount, screenCenter);
        }

        private void AdjustHabitatZoomAtScreenPoint(float amount, Vector2 screenPosition)
        {
            if (!cameraPresentationReady || Mathf.Abs(amount) < 0.0001f) return;
            // Positive input means zoom in for the mouse wheel, pinch, and the
            // visible + button. Store an additive size offset because the
            // camera presentation recomputes its view frame every frame.
            // Limit one input update to one small shared step so wheel,
            // trackpad, pinch, and buttons all have the same gentle response.
            float appliedAmount = Mathf.Clamp(amount, -HabitatZoomInputStep, HabitatZoomInputStep);
            float previousOffset = habitatZoomOffset;
            habitatZoomOffset = Mathf.Clamp(
                habitatZoomOffset - appliedAmount,
                -HabitatZoomInOffsetLimit,
                HabitatZoomOutOffsetLimit);

            RatData selected = SelectedRat;
            if (selected == null && !cameraFocusNest && mainCamera != null)
            {
                RatEnclosure enclosure = EnclosureForCameraView(cameraView);
                Vector3 unusedTarget;
                float frameSize;
                CalculateEnclosureFrame(enclosure, out unusedTarget, out frameSize);
                float oldSize = Mathf.Clamp(frameSize + previousOffset,
                    EnclosureMinimumZoom, GameConfig.CameraMaximumOrthographicSize);
                float newSize = Mathf.Clamp(frameSize + habitatZoomOffset,
                    EnclosureMinimumZoom, GameConfig.CameraMaximumOrthographicSize);
                float sizeDelta = newSize - oldSize;
                Rect cameraRect = mainCamera.pixelRect;
                if (sizeDelta != 0f && cameraRect.width > 1f && cameraRect.height > 1f)
                {
                    float normalizedX = Mathf.Clamp01((screenPosition.x - cameraRect.xMin) / cameraRect.width);
                    float normalizedY = Mathf.Clamp01((screenPosition.y - cameraRect.yMin) / cameraRect.height);
                    float offsetX = normalizedX * 2f - 1f;
                    float offsetY = normalizedY * 2f - 1f;
                    Vector3 right = normalCameraRotation * Vector3.right;
                    Vector3 up = normalCameraRotation * Vector3.up;
                    Vector3 anchorShift = -sizeDelta *
                        (offsetX * mainCamera.aspect * right + offsetY * up);
                    habitatZoomFocusOffset = ClampCameraTargetOffset(
                        enclosure, habitatZoomFocusOffset + anchorShift);
                }
            }
            // Keep the SmoothDamp velocity so consecutive small inputs blend
            // into the existing camera motion instead of restarting the
            // interpolation on every wheel or pinch sample.
        }

        private void EnsureTankCameraPanFocus(RatData selected)
        {
            if (cameraFollowSelectedRat && selected != null)
            {
                tankCameraPanState.SetFocus(selected.enclosure, selected.id, false);
                return;
            }

            if (cameraFocusNest && EnclosureSystem.HasNest(cameraFocusNestEnclosure))
            {
                tankCameraPanState.SetFocus(cameraFocusNestEnclosure, null, true);
                return;
            }

            tankCameraPanState.SetFocus(EnclosureForCameraView(cameraView), null, false);
        }

        private Vector3 GetTankCameraPanBaseTarget(RatData selected, out RatEnclosure enclosure)
        {
            Transform ratRoot;
            Vector3 focusPoint;
            if (cameraFollowSelectedRat && selected != null &&
                TryGetSelectedRatFocus(selected, out ratRoot, out focusPoint))
            {
                float verticalOffset = selected.stage == RatStage.Pinkie
                    ? PinkieSelectedRatCameraVerticalOffset
                    : SelectedRatCameraVerticalOffset;
                focusPoint += Vector3.up * verticalOffset;
                if (cameraView == HabitatCameraView.Pairing &&
                    selected.enclosure == RatEnclosure.Pairing && selected.stage == RatStage.Pinkie)
                    focusPoint += Vector3.up * PairingPinkieCameraLookTargetDownwardOffset;
                enclosure = selected.enclosure;
                return focusPoint;
            }

            if (cameraFocusNest && EnclosureSystem.HasNest(cameraFocusNestEnclosure))
            {
                enclosure = cameraFocusNestEnclosure;
                return EnclosureSystem.GetNestPosition(enclosure) + Vector3.up * 0.42f;
            }

            enclosure = EnclosureForCameraView(cameraView);
            Vector3 frameTarget;
            float unusedSize;
            CalculateEnclosureFrame(enclosure, out frameTarget, out unusedSize);
            return ClampCameraTargetToEnclosure(enclosure, frameTarget + habitatZoomFocusOffset);
        }

        private void PanHabitatCameraByScreenDelta(Vector2 screenDelta)
        {
            if (!cameraPresentationReady || mainCamera == null ||
                (ui != null && ui.IsModalOverlayOpen)) return;

            RatData selected = SelectedRat;
            EnsureTankCameraPanFocus(selected);
            RatEnclosure enclosure;
            Vector3 baseTarget = GetTankCameraPanBaseTarget(selected, out enclosure);
            Vector3 currentTarget = ClampCameraTargetToEnclosure(
                enclosure, baseTarget + tankCameraPanState.Offset);
            Vector3 worldDrag = TankCameraPanMath.ScreenDeltaToWorldPlane(mainCamera, screenDelta);
            if (worldDrag.sqrMagnitude <= 0.000001f) return;

            // Moving the view with the pointer requires moving the camera's
            // look target in the opposite direction to the projected drag.
            Vector3 nextTarget = ClampCameraTargetToEnclosure(enclosure, currentTarget - worldDrag);
            tankCameraPanState.SetOffset(nextTarget - baseTarget);
            cameraMoveVelocity = Vector3.zero;
        }

        private static RatEnclosure EnclosureForCameraView(HabitatCameraView view)
        {
            switch (view)
            {
                case HabitatCameraView.MaleEnclosure: return RatEnclosure.MaleColony;
                case HabitatCameraView.Breeding: return RatEnclosure.Breeding;
                case HabitatCameraView.Pairing: return RatEnclosure.Pairing;
                default: return RatEnclosure.FemaleColony;
            }
        }

        private static Vector3 ClampCameraTargetToEnclosure(RatEnclosure enclosure, Vector3 target)
        {
            return TankCameraPanMath.ClampTargetToEnclosure(enclosure, target);
        }

        private static Vector3 ClampCameraTargetOffset(RatEnclosure enclosure, Vector3 offset)
        {
            Vector3 center = EnclosureSystem.GetDefinition(enclosure).Center;
            center.y = 0.72f;
            return ClampCameraTargetToEnclosure(enclosure, center + offset) - center;
        }

        private float MinimumZoomForCurrentView(RatData selectedRat)
        {
            if (cameraFocusNest) return 3.2f;
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

        private void SaveBeforeApplicationInactive()
        {
            if (Save == null) return;

            // Keep the saved anchor current at the lifecycle edge. The next
            // active frame then catches up only the real elapsed interval.
            // Welcome-modal time remains intentionally paused.
            if (Save.clock != null)
            {
                if (ui != null && ui.IsWelcomeOpen)
                    Save.clock.lastRealTimestamp = GameConfig.NowMs();
                else
                    GrowthSystem.AdvanceClock(Save, GameConfig.NowMs());
            }
            SaveSystem.Save(Save);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveBeforeApplicationInactive();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Focus loss must not pause simulation. It is only a persistence
            // boundary; ProjectSettings.runInBackground keeps the visible
            // WebGL game running while another window has focus.
            if (!hasFocus) SaveBeforeApplicationInactive();
        }

        private void OnDestroy()
        {
            if (pairingCheckRoutine != null) StopCoroutine(pairingCheckRoutine);
        }

        private void OnApplicationQuit()
        {
            SaveBeforeApplicationInactive();
        }
    }
}
