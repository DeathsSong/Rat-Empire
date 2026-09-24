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
        private Camera mainCamera;
        private HabitatBuilder habitat;
        private RatPresenter rats;
        private InteractionManager interaction;
        private VerticalSliceUI ui;
        private float saveTimer;
        private string selectedRatId;
        private string selectedObjectId;
        private string parentAId;
        private string parentBId;
        private bool breedingOpen;
        private BreedingParentSlot breedingSelectionSlot;
        private bool startupWarning;
        private bool uiUpdateErrorLogged;
        private string deleteConfirmationRatId;
        private string sellConfirmationRatId;
        private string euthanizeConfirmationRatId;
        private readonly HashSet<string> selectedRatIds = new HashSet<string>();
        private bool multipleSelectionMode;
        private bool groupMoveConfirmationPending;
        private RatEnclosure groupMoveConfirmationTarget;
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
        private HabitatCameraView cameraView = HabitatCameraView.Overview;
        private Coroutine pairingCheckRoutine;
        private PairingApproachRuntime pairingApproach;
        private const int MaximumEventLogEntries = 10;
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
        }

        private const float CameraFocusSmoothTime = 0.24f;
        private const float CameraZoomSmoothTime = 0.28f;
        private const float InspectionOrthographicMultiplier = 0.169344f;
        private const float PinkieInspectionOrthographicMultiplier = InspectionOrthographicMultiplier * 0.2f;
        // Lower the selected-rat focus target slightly so the live rat sits
        // higher in the transparent profile opening. This moves only the
        // existing habitat camera; it does not move the rat or the profile UI.
        private const float SelectedRatCameraVerticalOffset = -1.00f;
        // Pinkies get a slightly higher camera position in the live profile
        // window because their smaller body sits lower in the habitat view.
        private const float PinkieSelectedRatCameraVerticalOffset = 0.10f;
        private const float TopNavigationReservedPixels = 122f;
        private const int RatAnimationShowcasePreviewLayer = 30;
        private const float HabitatZoomButtonStep = 0.9f;
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
        public RatEnclosure GroupMoveConfirmationTarget { get { return groupMoveConfirmationTarget; } }
        public bool PairingMoveAllConfirmationPending { get { return pairingMoveAllConfirmationPending; } }
        public float SimulationSpeed { get { return Save == null || Save.clock == null ? 1f : GrowthSystem.NormalizeSpeed(Save.clock.speed); } }
        public string SimulationSpeedLabel { get { return ((int)SimulationSpeed) + "×"; } }
        public bool ResetConfirmationPending { get { return resetConfirmationPending; } }
        public HabitatCameraView CameraView { get { return cameraView; } }
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

        private bool RecordStatusEvent(string value)
        {
            if (Save == null || string.IsNullOrWhiteSpace(value)) return false;
            string compact = CompactEventMessage(value);
            string lower = compact.ToLowerInvariant();

            // StatusMessage is also used for transient guidance and internal
            // state. Only meaningful colony events belong in the persistent
            // player-facing history.
            if (lower.Contains("ready") || lower.Contains("simulation speed") ||
                lower.Contains("no conception") || lower.Contains("unsuccess") ||
                lower.Contains("cancelled") || lower.Contains("canceled") ||
                lower.StartsWith("select ") || lower.StartsWith("choose ") ||
                lower.StartsWith("choosing ") || lower.StartsWith("not enough") ||
                lower.StartsWith("the selected") || lower.StartsWith("that ") ||
                lower.StartsWith("this ") || lower.Contains("unavailable") ||
                lower.Contains("must remain") || lower.Contains("cannot") ||
                lower.Contains("could not")) return false;

            bool meaningful = lower.Contains("breeding") || lower.Contains("pregnan") ||
                lower.Contains("birth") || lower.Contains("moved") ||
                lower.Contains("joined") || lower.Contains("removed from") ||
                lower.Contains("sold") || lower.Contains("euthanized") ||
                lower.Contains("restocked") || lower.Contains("evacuat") ||
                lower.Contains("spawned") || lower.Contains("deleted") ||
                lower.Contains("reset") || lower.Contains("advanced") ||
                lower.Contains("died") || lower.Contains("warning") ||
                lower.Contains("error");
            if (!meaningful) return false;

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
                var parts = new List<string>
                {
                    selectedRatId ?? string.Empty,
                    selectedObjectId ?? string.Empty,
                    breedingOpen ? "breeding" : "habitat",
                    parentAId ?? string.Empty,
                    parentBId ?? string.Empty,
                    breedingSelectionSlot.ToString(),
                    Save == null || Save.clock == null ? "0" : (Save.clock.gameTimeMs / 1000L).ToString(),
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
        }

        private void Awake()
        {
            Debug.Log("[Rat Habitat] GameBootstrap.Awake started.");
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
                Save = ColonyFactory.CreateNew(GameConfig.NowMs());
            }
            StatusMessage = startupWarning
                ? "Startup warning — see the Unity Console."
                : "3D scene ready — " + Save.rats.Count + " rats loaded.";

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
                    ReportInputDiagnostic, AdjustHabitatZoom, FocusHabitatAtWorldPoint, IsWorldInputBlockedByModal);
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

            Vector3 midpoint = ChoosePairingMeetPoint(maleRoot.position, femaleRoot.position, male.id, female.id);
            Vector3 approachDirection = femaleRoot.position - maleRoot.position;
            approachDirection.y = 0f;
            if (approachDirection.sqrMagnitude <= 0.01f) approachDirection = Vector3.right;
            approachDirection.Normalize();

            Vector3 maleTarget = EnclosureSystem.ClampToEnclosure(
                RatEnclosure.Pairing,
                midpoint - approachDirection * (PairingMeetSeparation * 0.5f),
                0.78f);
            Vector3 femaleTarget = EnclosureSystem.ClampToEnclosure(
                RatEnclosure.Pairing,
                midpoint + approachDirection * (PairingMeetSeparation * 0.5f),
                0.78f);

            // A boundary clamp should never collapse the two meeting points
            // into one. Fall back to a horizontal pair in the central safe
            // area if a future enclosure size changes make that necessary.
            if (Vector3.Distance(maleTarget, femaleTarget) < 0.62f)
            {
                Vector3 safeCenter = EnclosureSystem.ClampToEnclosure(
                    RatEnclosure.Pairing,
                    new Vector3(17.35f, midpoint.y, -2.85f),
                    1.15f);
                maleTarget = EnclosureSystem.ClampToEnclosure(
                    RatEnclosure.Pairing,
                    safeCenter + Vector3.left * (PairingMeetSeparation * 0.5f),
                    0.78f);
                femaleTarget = EnclosureSystem.ClampToEnclosure(
                    RatEnclosure.Pairing,
                    safeCenter + Vector3.right * (PairingMeetSeparation * 0.5f),
                    0.78f);
            }

            float visualSpeedMultiplier = Mathf.Clamp(SimulationSpeed, 0.75f, 2.25f);
            if (!maleBehavior.BeginPairingApproach(maleTarget, femaleTarget, visualSpeedMultiplier)) return false;
            if (!femaleBehavior.BeginPairingApproach(femaleTarget, maleTarget, visualSpeedMultiplier))
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
            };
            // Do not announce the attempt yet. A Pairing Habitat check can
            // still be blocked or fail its conception roll; only a successful
            // breeding result should produce a player-facing notification.
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

            string reason = string.Empty;
            if (male.enclosure != RatEnclosure.Pairing || female.enclosure != RatEnclosure.Pairing ||
                !BreedingSystem.IsBreedEligible(Save, male, GameTime, out reason) ||
                !BreedingSystem.IsBreedEligible(Save, female, GameTime, out reason))
            {
                CancelPairingApproach(reason);
                return;
            }

            float deltaTime = Mathf.Min(0.1f, Mathf.Max(0f, Time.unscaledDeltaTime));
            pairingApproach.phaseTimeout -= deltaTime;
            if (pairingApproach.phase == PairingApproachPhase.Walking)
            {
                if (maleBehavior.PairingApproachAtTarget && femaleBehavior.PairingApproachAtTarget)
                {
                    pairingApproach.phase = PairingApproachPhase.Sniffing;
                    pairingApproach.phaseTimeout = PairingInteractionSeconds + 1.5f;
                    maleBehavior.BeginPairingInteraction(femaleRoot.position, PairingInteractionSeconds);
                    femaleBehavior.BeginPairingInteraction(maleRoot.position, PairingInteractionSeconds);
                    return;
                }

                float combinedDistance = Vector3.Distance(maleRoot.position, pairingApproach.maleTarget) +
                    Vector3.Distance(femaleRoot.position, pairingApproach.femaleTarget);
                if (combinedDistance >= pairingApproach.lastCombinedDistance - 0.003f)
                    pairingApproach.stalledSeconds += deltaTime;
                else
                    pairingApproach.stalledSeconds = 0f;
                pairingApproach.lastCombinedDistance = combinedDistance;
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
            bool resolved = PairingHabitatSystem.ResolvePair(
                Save,
                female,
                male,
                GameTime,
                GameConfig.PairingPregnancyChance,
                out conceptionSucceeded,
                out reason);

            maleBehavior.FinishPairingInteraction();
            femaleBehavior.FinishPairingInteraction();
            pairingApproach = null;
            Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;

            if (resolved && conceptionSucceeded)
            {
                StatusMessage = female.name + " is pregnant";
            }
            else
            {
                // Failed conception and cancelled attempts are intentionally
                // silent. The rats simply return to normal wandering.
                StatusMessage = string.Empty;
            }

            EnclosureSystem.RecalculateAssignments(Save);
            RefreshWorldAndUi(resolved && conceptionSucceeded);
            SaveSystem.Save(Save);
        }

        private void CancelPairingApproach(string reason)
        {
            if (pairingApproach == null) return;

            RatHabitatBehavior maleBehavior;
            RatHabitatBehavior femaleBehavior;
            if (rats != null)
            {
                if (rats.TryGetRatBehavior(pairingApproach.maleId, out maleBehavior)) maleBehavior.CancelPairingApproach();
                if (rats.TryGetRatBehavior(pairingApproach.femaleId, out femaleBehavior)) femaleBehavior.CancelPairingApproach();
            }

            pairingApproach = null;
            Save.pairingNextCheckGameTime = GameTime + GameConfig.PairingCheckIntervalMs;
            // A blocked or timed-out approach is not a player-facing event.
            // Keep the status strip clear; only successful breeding reports
            // the concise female/male message in ResolvePairingApproach.
            StatusMessage = string.Empty;
            RefreshWorldAndUi(false);
            SaveSystem.Save(Save);
        }

        private bool TryGetPairingParticipant(string ratId, out Transform root, out RatHabitatBehavior behavior)
        {
            root = null;
            behavior = null;
            if (rats == null || !rats.TryGetRatRoot(ratId, out root) || root == null ||
                !rats.TryGetRatBehavior(ratId, out behavior)) return false;
            return true;
        }

        private Vector3 ChoosePairingMeetPoint(Vector3 malePosition, Vector3 femalePosition, string maleId, string femaleId)
        {
            Vector3 midpoint = (malePosition + femalePosition) * 0.5f;
            Vector3[] candidates =
            {
                new Vector3(17.35f, midpoint.y, -2.85f),
                new Vector3(15.25f, midpoint.y, -1.15f),
                new Vector3(19.45f, midpoint.y, -1.15f),
                new Vector3(15.25f, midpoint.y, 2.15f),
                new Vector3(19.45f, midpoint.y, 2.15f),
                new Vector3(14.65f, midpoint.y, -5.20f),
                new Vector3(20.05f, midpoint.y, -5.20f),
            };

            float bestScore = float.MinValue;
            Vector3 best = EnclosureSystem.ClampToEnclosure(RatEnclosure.Pairing, candidates[0], 1.15f);
            for (int index = 0; index < candidates.Length; index++)
            {
                Vector3 candidate = EnclosureSystem.ClampToEnclosure(
                    RatEnclosure.Pairing, candidates[index], 1.15f);
                float score = -Mathf.Pow(Vector2.Distance(
                    new Vector2(candidate.x, candidate.z),
                    new Vector2(midpoint.x, midpoint.z)), 2f) * 0.025f;
                if (Vector2.Distance(
                    new Vector2(candidate.x, candidate.z),
                    new Vector2(EnclosureSystem.PairingNestPosition.x, EnclosureSystem.PairingNestPosition.z)) < 2.2f)
                {
                    score -= 3f;
                }

                if (Save != null)
                {
                    foreach (RatData rat in Save.rats)
                    {
                        if (rat == null || rat.id == maleId || rat.id == femaleId || rat.enclosure != RatEnclosure.Pairing) continue;
                        Transform otherRoot;
                        if (rats == null || !rats.TryGetRatRoot(rat.id, out otherRoot) || otherRoot == null) continue;
                        float distance = Vector2.Distance(
                            new Vector2(candidate.x, candidate.z),
                            new Vector2(otherRoot.position.x, otherRoot.position.z));
                        if (distance < 1.4f) score -= (1.4f - distance) * 4.5f;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        private void Update()
        {
            UpdateWorldViewport();
            UpdateCameraPresentation();
            if (Save == null) return;
            bool clockMoved = GrowthSystem.AdvanceClock(Save, GameConfig.NowMs());
            bool stageChanged = GrowthSystem.RefreshRatStages(Save);
            bool reproductiveStateChanged = BreedingSystem.RefreshReproductiveStates(Save, GameTime);
            bool storeChanged = StoreSystem.AdvanceRestock(Save, GameTime);
            UpdatePairingApproach();
            PruneGroupSelection();
            List<DedicatedBreedingSessionData> completedSessions;
            int completedSessionCount = BreedingSystem.ResolveDueDedicatedBreedingSessions(Save, GameTime, out completedSessions);
            if (completedSessionCount > 0)
            {
                EnclosureSystem.ClearBreedingPair();
                RestoreDedicatedBreedingPair();
                StatusMessage = BuildDedicatedSessionResultMessage(completedSessions);
                reproductiveStateChanged = true;
            }
            List<LitterData> newLitters;
            int births = BreedingSystem.FinishDuePregnancies(Save, GameTime, out newLitters);
            if (births > 0)
            {
                var litter = newLitters[0];
                var mother = BreedingSystem.FindRat(Save, litter.motherId);
                var father = BreedingSystem.FindRat(Save, litter.fatherId);
                StatusMessage = "Birth: " + (mother == null ? "Mother" : mother.name) + " and " + (father == null ? "Father" : father.name) + " welcomed " + litter.size + " pinkies in the " + litter.litterName + ".";
                stageChanged = true;
            }
            bool enclosureChanged = EnclosureSystem.RecalculateAssignments(Save);
            if (SelectedRat == null && !string.IsNullOrEmpty(selectedRatId))
            {
                selectedRatId = null;
                if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            }

            saveTimer += Time.unscaledDeltaTime;
            if (clockMoved && saveTimer >= 2f)
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
                    if (stageChanged || reproductiveStateChanged || enclosureChanged)
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

        public bool SelectEntity(SelectableEntity entity)
        {
            habitatZoomOffset = 0f;
            if (entity == null)
            {
                // A valid world tap that does not hit an interactable is an
                // empty-floor tap. Clear both the data selection and its
                // visual ring instead of leaving the previous profile open.
                selectedRatId = null;
                selectedObjectId = null;
                cameraView = HabitatCameraView.Overview;
                if (rats != null) rats.SetSelected(null);
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
            if (ui != null) ui.Refresh(true);
            return true;
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
            if (!multipleSelectionMode && selectedRatIds.Count == 0 && !groupMoveConfirmationPending) return;
            multipleSelectionMode = false;
            selectedRatIds.Clear();
            groupMoveConfirmationPending = false;
            if (rats != null) rats.SetSelected(selectedRatId);
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
            if (rats != null) rats.SetSelected(null);
            if (ui != null) ui.CloseTransientPanels();

            HabitatCameraView view = CameraViewForWorldPoint(worldPoint);
            cameraView = view;
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
                case RatEnclosure.Nursery: return HabitatCameraView.Nursery;
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
                case HabitatCameraView.Nursery: return "Nursery";
                case HabitatCameraView.Breeding: return "Breeding";
                case HabitatCameraView.Pairing: return "Pairing Habitat";
                default: return "Overview";
            }
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
                StatusMessage = rat.name + " is already in the Pairing Habitat.";
                if (ui != null) ui.Refresh(false);
                return;
            }

            if (BreedingSystem.FindActiveDedicatedSession(Save, rat.id) != null)
            {
                StatusMessage = rat.name + " is occupied by a dedicated breeding session.";
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

            rat.enclosure = RatEnclosure.Pairing;
            rat.pairingHabitatAssigned = true;
            EnclosureSystem.RecalculateAssignments(Save);
            // Keep this operation atomic even if a legacy/stale assignment
            // was encountered during reconciliation. The next frame and the
            // saved data must both see Pairing as the authoritative location.
            rat.enclosure = RatEnclosure.Pairing;
            rat.pairingHabitatAssigned = true;
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(rat);
            StatusMessage = rat.name + " moved to the Pairing Habitat.";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void RemoveSelectedRatFromPairingHabitat()
        {
            RatData rat = SelectedRat;
            if (rat == null || rat.enclosure != RatEnclosure.Pairing)
            {
                StatusMessage = "The selected rat is not in the Pairing Habitat.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            if (rat.stage == RatStage.Adult && rat.sex == RatSex.Female &&
                (EnclosureSystem.IsPregnant(Save, rat) || EnclosureSystem.HasDependentPinkies(Save, rat.id)))
            {
                StatusMessage = rat.name + " must remain in the Pairing Habitat until her pregnancy and dependent litter are complete.";
                if (ui != null) ui.Refresh(true);
                return;
            }

            rat.enclosure = EnclosureSystem.StandardEnclosure(Save, rat);
            rat.pairingHabitatAssigned = false;
            EnclosureSystem.RecalculateAssignments(Save);
            selectedRatId = rat.id;
            cameraView = CameraViewForRat(rat);
            StatusMessage = rat.name + " removed from the Pairing Habitat.";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
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
            StatusMessage = "Move every Pairing Habitat rat out? Pregnant and nursing families will go to the Nursery.";
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
                rat.enclosure = PairingEvacuationDestination(rat);
                rat.pairingHabitatAssigned = false;
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
            if (rat == null || rat.stage == RatStage.Pinkie) return RatEnclosure.Nursery;
            if (rat.sex == RatSex.Male) return RatEnclosure.MaleColony;
            return EnclosureSystem.IsPregnant(Save, rat) || rat.nursing ||
                EnclosureSystem.HasDependentPinkies(Save, rat.id)
                ? RatEnclosure.Nursery
                : RatEnclosure.FemaleColony;
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
                bool separatesFamily = target != RatEnclosure.Pairing && target != RatEnclosure.Nursery;
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
            MoveSelectedRatsToHabitat(groupMoveConfirmationTarget);
        }

        public void CancelMoveSelectedRats()
        {
            groupMoveConfirmationPending = false;
            StatusMessage = "Group move cancelled.";
            if (ui != null) ui.Refresh(true);
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
            int moved = 0;
            var ids = new List<string>(selectedRatIds);
            foreach (var id in ids)
            {
                RatData rat = BreedingSystem.FindRat(Save, id);
                if (rat == null) continue;
                if (target == RatEnclosure.Pairing)
                {
                    if (BreedingSystem.FindActiveDedicatedSession(Save, rat.id) != null) continue;
                    rat.enclosure = RatEnclosure.Pairing;
                    rat.pairingHabitatAssigned = true;
                }
                else if (rat.stage == RatStage.Pinkie)
                {
                    rat.enclosure = RatEnclosure.Nursery;
                    rat.pairingHabitatAssigned = false;
                }
                else if (target == RatEnclosure.Nursery)
                {
                    rat.enclosure = RatEnclosure.Nursery;
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
                moved++;
            }
            EnclosureSystem.RecalculateAssignments(Save);
            if (rats != null) rats.SetSelectedGroup(selectedRatIds);
            StatusMessage = "Moved " + moved + " selected rat" + (moved == 1 ? string.Empty : "s") + " to " + EnclosureSystem.Label(target) + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
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
            SetHabitatCameraView(HabitatCameraView.Nursery);
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
            SetHabitatCameraView(HabitatCameraView.Overview);
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
            StoreRatListingData listing = StoreSystem.FindListing(Save, listingId);
            if (listing == null)
            {
                StatusMessage = "That market rat is no longer available.";
                if (ui != null) ui.Refresh(true);
                return;
            }
            if (Save.colonyCredits < listing.price)
            {
                StatusMessage = "Not enough dollars — need $" + listing.price + " to buy " + listing.name + ".";
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
            StatusMessage = purchased.name + " joined the colony for $" + listing.price + ".";
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
            cameraView = view;
            habitatZoomOffset = 0f;
            selectedRatId = null;
            selectedObjectId = null;
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

            habitatZoomOffset = 0f;
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(rat);
            // The roster is a normal habitat view. Keep a defensive reset here
            // so a stale UI callback can never reopen breeding or leave a
            // duplicate parent pair behind.
            breedingOpen = false;
            parentAId = null;
            parentBId = null;
            breedingSelectionSlot = BreedingParentSlot.None;
            EnclosureSystem.ClearBreedingPair();
            if (rats != null) rats.SetSelected(rat.id);
            if (ui != null) ui.Refresh(true);
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

            habitatZoomOffset = 0f;
            selectedRatId = rat.id;
            selectedObjectId = null;
            cameraView = CameraViewForRat(rat);
            if (rats != null) rats.SetSelected(rat.id);
            if (ui != null) ui.Refresh(true);
        }

        private static HabitatCameraView CameraViewForRat(RatData rat)
        {
            if (rat == null) return HabitatCameraView.Overview;
            switch (rat.enclosure)
            {
                case RatEnclosure.MaleColony: return HabitatCameraView.MaleEnclosure;
                case RatEnclosure.Nursery: return HabitatCameraView.Nursery;
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
                StatusMessage = candidate.name + " is not an eligible " + (expectedSex == RatSex.Female ? "mother" : "father") + ". " + reason;
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
            StatusMessage = (breedingSelectionSlot == BreedingParentSlot.Mother ? "Mother" : "Father") + " set to " + candidate.name + "." +
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
            StatusMessage = "Dedicated breeding session started for " + mother.name + " and " + father.name + ". Ends in 2 in-game hours.";
            SaveSystem.Save(Save);
            // Pregnancy begins nursery care immediately. Reuse the stable
            // presenter roots so the mother relocates without creating a
            // second visual or changing selection identity.
            RefreshWorldAndUi(true);
        }

        private string BuildDedicatedSessionResultMessage(List<DedicatedBreedingSessionData> sessions)
        {
            if (sessions == null || sessions.Count == 0) return string.Empty;
            var session = sessions[0];
            var mother = BreedingSystem.FindRat(Save, session.motherId);
            return session.conceptionSucceeded
                ? (mother == null ? "The female is pregnant" : mother.name + " is pregnant")
                : "Dedicated breeding complete — no conception this session.";
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
            var mother = BreedingSystem.FindRat(Save, litter.motherId);
            var father = BreedingSystem.FindRat(Save, litter.fatherId);
            StatusMessage = "Birth: " + mother.name + " and " + father.name + " welcomed " + litter.size + " pinkies in the " + litter.litterName + ".";
            EnclosureSystem.RecalculateAssignments(Save);
            SaveSystem.Save(Save);
            rats.Render(Save, habitat.NestPosition);
            if (ui != null) ui.Refresh(true);
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
            StatusMessage = rat.name + " advanced to " + GrowthSystem.StageLabel(rat.stage) + ". Fur and markings now reveal at the young stage.";
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

            string label;
            GenotypeData genotype;
            switch (preset)
            {
                case DeveloperRatPreset.SolidBlack:
                    label = "Solid Black";
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s");
                    break;
                case DeveloperRatPreset.SolidBrown:
                    label = "Solid Brown";
                    genotype = GeneticsSystem.CreateFounder("b", "b", "C", "C", "D", "D", "s", "s");
                    break;
                case DeveloperRatPreset.DilutedBlack:
                    label = "Diluted Black";
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "d", "d", "s", "s");
                    break;
                case DeveloperRatPreset.DilutedBrown:
                    label = "Diluted Brown";
                    genotype = GeneticsSystem.CreateFounder("b", "b", "C", "C", "d", "d", "s", "s");
                    break;
                case DeveloperRatPreset.Albino:
                    label = "Albino";
                    genotype = GeneticsSystem.CreateFounder("B", "B", "c", "c", "D", "D", "s", "s");
                    break;
                case DeveloperRatPreset.SpottedBlack:
                    label = "Spotted Black";
                    genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "S", "S");
                    break;
                default:
                    label = "Spotted Brown";
                    genotype = GeneticsSystem.CreateFounder("b", "b", "C", "C", "D", "D", "S", "S");
                    break;
            }

            Save.EnsureLists();
            int ordinal = 1;
            string name;
            do
            {
                name = label + " Test " + ordinal;
                ordinal++;
            }
            while (FindRatByName(name) != null);

            RatSex sex = Save.rats.Count % 2 == 0 ? RatSex.Female : RatSex.Male;
            long birthTimestamp = GameTime - (long)((GameConfig.YoungStageDays + 30f) * GameConfig.GameDayMs);
            var rat = ColonyFactory.CreateRat(
                ColonyFactory.NewId("dev_rat"),
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
            StatusMessage = "Spawned " + rat.name + " with genotype " + DeveloperGeneSummary(rat.genotype) + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        public void SetSimulationSpeed(float speed)
        {
            if (Save == null || Save.clock == null) return;
            float normalized = GrowthSystem.NormalizeSpeed(speed);
            Save.clock.speed = normalized;
            SaveSystem.Save(Save);
            if (ui != null) ui.Refresh(true);
        }

        public int SellValue(RatData rat)
        {
            if (rat == null || rat.traits == null) return GameConfig.SellCreditBase;
            float quality = (rat.traits.health + rat.traits.fertility) * 0.5f;
            return Mathf.Max(1, Mathf.RoundToInt(GameConfig.SellCreditBase + quality * GameConfig.SellCreditTraitMultiplier));
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
            if (rat.enclosure == RatEnclosure.Nursery || rat.enclosure == RatEnclosure.Breeding || rat.enclosure == RatEnclosure.Pairing)
                warnings.Add("Special habitat: " + EnclosureSystem.Label(rat.enclosure));

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
            int dollars = SellValue(rat);
            string name = rat.name;
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
            StatusMessage = "FINAL CONFIRMATION: permanently euthanize " + rat.name + " for $" + GameConfig.EuthanasiaCostDollars + "? This cannot be undone. " + SelectedRatRemovalWarning;
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
            string name = rat.name;
            if (!RetireActiveRat(rat, RatRemovalDisposition.Euthanized, false)) return;
            Save.colonyCredits = Mathf.Max(0, Save.colonyCredits - GameConfig.EuthanasiaCostDollars);
            StatusMessage = name + " was euthanized for $" + GameConfig.EuthanasiaCostDollars + ".";
            SaveSystem.Save(Save);
            RefreshWorldAndUi(true);
        }

        private bool RetireActiveRat(RatData rat, RatRemovalDisposition disposition, bool awardCredits)
        {
            if (Save == null || rat == null || !Save.rats.Remove(rat)) return false;
            int saleDollars = awardCredits ? SellValue(rat) : 0;
            BreedingSystem.CancelPregnanciesForRat(Save, rat.id, GameTime);
            BreedingSystem.CancelDedicatedSessionsForRat(Save, rat.id, GameTime);
            rat.removalDisposition = disposition;
            rat.removedAt = GameTime;
            rat.reproductiveState = ReproductiveState.Infertile;
            rat.pregnancyId = null;
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
            StatusMessage = "Confirm deletion of " + selected.name + ". Pending pregnancies will be cancelled safely; completed litter history will remain.";
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

            string removedName = selected.name;
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
            if (habitat != null)
            {
                habitat.Rebuild(Save);
            }
            if (rats != null && habitat != null) rats.Render(Save, habitat.NestPosition);
            SaveSystem.Save(Save);
            StatusMessage = "Game fully reset. Default founders, habitat, clock, genetics, and UI state restored.";
            if (ui != null)
            {
                ui.CloseTransientPanels();
                ui.Refresh(true);
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
            // Lower three-quarter overview: preserve the complete four-cage
            // composition while showing more of the enclosure fronts instead
            // of looking down from a near top-down angle.
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

            // The top navigation is an overlay, so reserve its real pixel
            // height in the camera viewport. This keeps the complete habitat
            // below the header instead of rendering rats underneath it.
            float reserved = Mathf.Clamp01(TopNavigationReservedPixels / Screen.height);
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
            if (selected != null && TryGetSelectedRatFocus(selected, out ratRoot, out selectedFocusPoint))
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
            else if (cameraView != HabitatCameraView.Overview)
            {
                RatEnclosure enclosure;
                switch (cameraView)
                {
                    case HabitatCameraView.MaleEnclosure: enclosure = RatEnclosure.MaleColony; break;
                    case HabitatCameraView.Nursery: enclosure = RatEnclosure.Nursery; break;
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
            habitatZoomOffset = Mathf.Clamp(
                habitatZoomOffset - amount,
                -HabitatZoomOffsetLimit,
                HabitatZoomOffsetLimit);
            cameraZoomVelocity = 0f;
        }

        private float MinimumZoomForCurrentView(RatData selectedRat)
        {
            if (selectedRat != null)
            {
                return selectedRat.stage == RatStage.Pinkie
                    ? PinkieSelectedRatMinimumZoom
                    : SelectedRatMinimumZoom;
            }
            return cameraView == HabitatCameraView.Overview ? OverviewMinimumZoom : EnclosureMinimumZoom;
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
            orthographicSize = Mathf.Clamp(orthographicSize, 6.4f, normalCameraOrthographicSize);
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
