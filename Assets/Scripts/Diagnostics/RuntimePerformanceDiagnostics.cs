using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Unity.Profiling;

namespace RatHabitat
{
    [Flags]
    public enum PerformanceIsolationMode
    {
        Normal = 0,
        RatBehavior = 1 << 0,
        RatAnimation = 1 << 1,
        RatRendering = 1 << 2,
        RatShadows = 1 << 3,
        AutomaticUiRefresh = 1 << 4,
        ColonyMaintenance = 1 << 5,
        Simulation = 1 << 6,
        RatPresentation = 1 << 7,
        UiRendering = 1 << 8,
    }

    public enum PerformanceProbeArea
    {
        BootstrapUpdate,
        ClockAndAge,
        SimulationMaintenance,
        MaintenanceGrowth,
        MaintenanceReproductive,
        MaintenanceStore,
        MaintenanceSessions,
        MaintenanceBirths,
        MaintenanceWeanings,
        MaintenanceActivities,
        MaintenanceEnclosures,
        MaintenanceSaleEligibility,
        MaintenanceDeadlineResolution,
        MaintenancePairingChecks,
        MaintenanceHistoricalLitters,
        MaintenanceHistoricalPregnancies,
        MaintenanceHistoricalRatIndex,
        MaintenanceBirthRepairRecovery,
        PairingMovement,
        RatBehaviorUpdate,
        RatBehaviorLateUpdate,
        RatDestinationSelection,
        RatPresenterLateUpdate,
        GroundingAndBounds,
        PinkieUpdate,
        UiUpdate,
        UiRefresh,
        UiRebuildAndLayout,
        InteractionUpdate,
        RatPresentationBuild,
        RatMaterialSetup,
        NursingUpdate,
        SaveStorage,
        SaveExport,
        WorldMovementIntegration,
        RatSpacing,
        Count,
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public enum PerformanceLogSeverity
    {
        None,
        Lag,
        Critical,
    }

    /// <summary>Compact, allocation-free payload stored in the bounded diagnostic rings.</summary>
    public struct PerformanceLogSample
    {
        public long UtcTicks;
        public long GameTimeMs;
        public int Speed;
        public float Fps;
        public float AverageFrameMs;
        public float WorstFrameMs;
        public float CpuMainThreadMs;
        public float GpuMs;
        public float UnattributedFrameGapMs;
        public float BrowserAverageFrameGapMs;
        public float BrowserWorstFrameGapMs;
        public float BrowserGapWithinUnityCpuMs;
        public float BrowserGapOutsideUnityCpuMs;
        public int BrowserFrameCount;
        public int BrowserLongTaskCount;
        public float BrowserLongTaskTotalMs;
        public float BrowserLongTaskMaxMs;
        public bool BrowserTelemetryAvailable;
        public bool PageVisible;
        public bool PageFocused;
        public bool PageWasHiddenDuringSample;
        public bool PageWasUnfocusedDuringSample;
        public bool LongTaskApiAvailable;
        public long GcAllocatedBytes;
        public int Gc0Collections;
        public int Gc1Collections;
        public int Gc2Collections;
        public int ActiveRatCount;
        public int PinkieCount;
        public int AnimatorCount;
        public int RendererCount;
        public int UiGraphicCount;
        public int CanvasCount;
        public int SimulationSteps;
        public int SimulationProbeCalls;
        public int RatBehaviorUpdates;
        public int RepeatedRatBehaviorUpdates;
        public float SimulationMs;
        public float SimulationMaintenanceMs;
        public int MaintenancePasses;
        public float MaintenanceGrowthMs;
        public int MaintenanceGrowthPasses;
        public float MaintenanceReproductiveMs;
        public int MaintenanceReproductivePasses;
        public float MaintenanceStoreMs;
        public int MaintenanceStorePasses;
        public float MaintenanceSessionsMs;
        public int MaintenanceSessionsPasses;
        public float MaintenanceBirthsMs;
        public int MaintenanceBirthsPasses;
        public float MaintenanceWeaningsMs;
        public int MaintenanceWeaningsPasses;
        public float MaintenanceActivitiesMs;
        public int MaintenanceActivitiesPasses;
        public float MaintenanceEnclosuresMs;
        public int MaintenanceEnclosuresPasses;
        public float MaintenanceSaleEligibilityMs;
        public int MaintenanceSaleEligibilityPasses;
        public float MaintenanceDeadlineResolutionMs;
        public int MaintenanceDeadlineResolutionPasses;
        public float MaintenancePairingChecksMs;
        public int MaintenancePairingChecksPasses;
        public float MaintenanceHistoricalLittersMs;
        public int MaintenanceHistoricalLitterScans;
        public long MaintenanceHistoricalLitterRecords;
        public float MaintenanceHistoricalPregnanciesMs;
        public int MaintenanceHistoricalPregnancyScans;
        public long MaintenanceHistoricalPregnancyRecords;
        public float MaintenanceHistoricalRatIndexMs;
        public int MaintenanceHistoricalRatIndexScans;
        public long MaintenanceHistoricalRatIndexRecords;
        public float MaintenanceBirthRepairRecoveryMs;
        public int MaintenanceBirthRepairRecoveryPasses;
        public long MaintenanceBirthRepairRecordsScanned;
        public long MaintenanceBirthRepairTransactionsReopened;
        public float NursingMs;
        public int NursingPasses;
        public float UiRefreshMs;
        public float RatAiMovementMs;
        public float RatTargetSelectionMs;
        public float WorldMovementIntegrationMs;
        public float RatSpacingMs;
        public float AnimationMs;
        public float GroundingBoundsMs;
        public float RatPresentationMs;
        public int RatPresentationUpdates;
        public float SaveExportMs;
        public int SaveExportOperations;
        public float SaveStorageMs;
        public int SaveStorageOperations;
        public long SerializedDataBytes;
        public long LargestSerializedDataBytes;
        public int SaveQueueRequests;
        public int SaveQueueCoalescedRequests;
        public string SaveSource;
        public float InputInteractionsMs;
        public string CurrentPanel;
    }

    public struct PerformanceSpikeRecord
    {
        public PerformanceLogSeverity Severity;
        public bool IsUnattributedFrameGap;
        public PerformanceLogSample Context;
    }
#endif

    /// <summary>
    /// Development-build-only profiling switches and low-allocation sample
    /// aggregation. All test modes are transient and never touch colony data.
    /// </summary>
    public static class RuntimePerformanceDiagnostics
    {
        // Identifies the Git source baseline used for the local verification
        // build. The +worktree suffix distinguishes uncommitted source edits
        // from the checked-in WebGL bundle.
        public const string BuildRevision = "85e784b+worktree";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly string[] SampleNames =
        {
            "Rat Empire/Measured/Bootstrap Update",
            "Rat Empire/Measured/Clock and Age",
            "Rat Empire/Measured/Simulation Maintenance",
            "Rat Empire/Measured/Maintenance Growth",
            "Rat Empire/Measured/Maintenance Reproductive",
            "Rat Empire/Measured/Maintenance Store",
            "Rat Empire/Measured/Maintenance Sessions",
            "Rat Empire/Measured/Maintenance Births",
            "Rat Empire/Measured/Maintenance Weanings",
            "Rat Empire/Measured/Maintenance Activities",
            "Rat Empire/Measured/Maintenance Enclosures",
            "Rat Empire/Measured/Maintenance Sale Eligibility",
            "Rat Empire/Measured/Maintenance Deadline Resolution",
            "Rat Empire/Measured/Maintenance Pairing Checks",
            "Rat Empire/Measured/Maintenance Historical Litter Scan",
            "Rat Empire/Measured/Maintenance Historical Pregnancy Scan",
            "Rat Empire/Measured/Maintenance Historical Rat Index",
            "Rat Empire/Measured/Maintenance Birth Repair and Recovery",
            "Rat Empire/Measured/Pairing Movement",
            "Rat Empire/Measured/Rat Behavior Update",
            "Rat Empire/Measured/Rat Behavior LateUpdate",
            "Rat Empire/Measured/Rat Destination Selection",
            "Rat Empire/Measured/Rat Presenter LateUpdate",
            "Rat Empire/Measured/Grounding and Bounds",
            "Rat Empire/Measured/Pinkie Update",
            "Rat Empire/Measured/UI Update",
            "Rat Empire/Measured/UI Refresh",
            "Rat Empire/Measured/UI Rebuild and Layout",
            "Rat Empire/Measured/Interaction Update",
            "Rat Empire/Measured/Rat Presentation Build",
            "Rat Empire/Measured/Rat Material Setup",
            "Rat Empire/Measured/Nursing Update",
            "Rat Empire/Measured/Save Storage",
            "Rat Empire/Measured/Log Export",
            "Rat Empire/Measured/World Movement Integration",
            "Rat Empire/Measured/Rat Spacing",
        };
        private static readonly long[] WindowTicks = new long[(int)PerformanceProbeArea.Count];
        private static readonly long[] MaxTicks = new long[(int)PerformanceProbeArea.Count];
        private static readonly int[] Calls = new int[(int)PerformanceProbeArea.Count];
        private static readonly long[] WindowWorkCounts = new long[(int)PerformanceProbeArea.Count];
        private static readonly long[] WindowChangedCounts = new long[(int)PerformanceProbeArea.Count];
        private static readonly StringBuilder SummaryBuilder = new StringBuilder(512);
        private static readonly StringBuilder SaveSourceBuilder = new StringBuilder(256);
        private static readonly string[] SaveSourceNames = new string[16];
        private static readonly int[] SaveSourceCounts = new int[16];
        private static int saveSourceOverflowCount;
        private static readonly PerformanceLogSample[] SampleRing = new PerformanceLogSample[GameConfig.PerformanceLogSampleCapacity];
        private static readonly PerformanceSpikeRecord[] SpikeRing = new PerformanceSpikeRecord[GameConfig.PerformanceLogSpikeCapacity];
        private static readonly StringBuilder ExportBuilder = new StringBuilder(32768);
        private static int sampleWriteIndex = -1;
        private static int sampleCount;
        private static int spikeWriteIndex = -1;
        private static int spikeCount;
        private static bool spikeEpisodeActive;
        private static PerformanceLogSeverity spikeEpisodeSeverity;
        private static float sessionWorstFrameMs;
        private static PerformanceLogSample sessionWorstFrame;
        private static float sessionWorstSubsystemMs;
        private static string sessionWorstSubsystem = "n/a";
        private static bool captureEnabled = true;
        private static bool hudVisible;
        private static PerformanceIsolationMode isolationMode;
        private static long lastSampleUtcTicks;
        private static string lastExportActionStatus = "No copy or download attempted.";
        private static string manualExportText = string.Empty;
        private static int exportActionSequence;
        private static int pendingClipboardAction;
        private static bool browserTelemetryPending;
        private static float pendingBrowserAverageGapMs = -1f;
        private static float pendingBrowserWorstGapMs = -1f;
        private static int pendingBrowserFrameCount;
        private static int pendingBrowserLongTaskCount;
        private static float pendingBrowserLongTaskTotalMs = -1f;
        private static float pendingBrowserLongTaskMaxMs = -1f;
        private static bool pendingPageVisible;
        private static bool pendingPageFocused;
        private static bool pendingPageWasHidden;
        private static bool pendingPageWasUnfocused;
        private static bool pendingLongTaskApiAvailable;
        private static int saveQueueRequestsInWindow;
        private static int saveQueueCoalescedInWindow;
        private static long serializedBytesInWindow;
        private static long largestSerializedDataBytesInWindow;
        private static string lastSaveSourceInWindow = string.Empty;

        public static bool CaptureEnabled { get { return captureEnabled; } }
        public static bool HudVisible { get { return hudVisible; } }
        public static PerformanceIsolationMode IsolationMode { get { return isolationMode; } }
        public static int SampleCount { get { return sampleCount; } }
        public static int SpikeCount { get { return spikeCount; } }
        public static string LastExportActionStatus { get { return lastExportActionStatus; } }
        public static string ManualExportText { get { return manualExportText; } }
        public static float SessionWorstFrameMs { get { return sessionWorstFrameMs; } }
        public static float SessionWorstSubsystemMs { get { return sessionWorstSubsystemMs; } }
        public static string SessionWorstSubsystem { get { return sessionWorstSubsystem; } }

        /// <summary>
        /// Returns one bounded display slice of a raw performance export.
        /// Copy/download continue to use the complete raw string; this helper
        /// exists so the legacy Unity Text mesh never receives a huge log.
        /// </summary>
        public static string GetManualExportChunk(string raw, int page, int chunkSize)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            int safeChunkSize = Math.Max(1, chunkSize);
            int pageCount = Math.Max(1, (raw.Length + safeChunkSize - 1) / safeChunkSize);
            int safePage = Math.Max(0, Math.Min(page, pageCount - 1));
            int first = safePage * safeChunkSize;
            return raw.Substring(first, Math.Min(safeChunkSize, raw.Length - first));
        }

        public static void RecordSaveQueueRequest(string source, bool coalesced)
        {
            if (!captureEnabled) return;
            saveQueueRequestsInWindow++;
            if (coalesced) saveQueueCoalescedInWindow++;
            if (!string.IsNullOrEmpty(source)) lastSaveSourceInWindow = source;
        }

        public static void RecordSaveSerializedData(string source, int byteCount)
        {
            if (!captureEnabled) return;
            long safeByteCount = Math.Max(0, byteCount);
            serializedBytesInWindow += safeByteCount;
            largestSerializedDataBytesInWindow = Math.Max(largestSerializedDataBytesInWindow, safeByteCount);
            string safeSource = string.IsNullOrEmpty(source) ? "Unknown" : source;
            lastSaveSourceInWindow = safeSource;
            int emptySlot = -1;
            for (int index = 0; index < SaveSourceNames.Length; index++)
            {
                if (SaveSourceNames[index] == safeSource)
                {
                    SaveSourceCounts[index]++;
                    return;
                }
                if (SaveSourceNames[index] == null && emptySlot < 0) emptySlot = index;
            }
            if (emptySlot >= 0)
            {
                SaveSourceNames[emptySlot] = safeSource;
                SaveSourceCounts[emptySlot] = 1;
            }
            else
            {
                saveSourceOverflowCount++;
            }
        }

        private static string BuildSaveSourceSummary()
        {
            SaveSourceBuilder.Length = 0;
            for (int index = 0; index < SaveSourceNames.Length; index++)
            {
                if (SaveSourceNames[index] == null || SaveSourceCounts[index] <= 0) continue;
                if (SaveSourceBuilder.Length > 0) SaveSourceBuilder.Append("; ");
                SaveSourceBuilder.Append(SaveSourceNames[index]).Append('×').Append(SaveSourceCounts[index]);
            }
            if (saveSourceOverflowCount > 0)
            {
                if (SaveSourceBuilder.Length > 0) SaveSourceBuilder.Append("; ");
                SaveSourceBuilder.Append("other×").Append(saveSourceOverflowCount);
            }
            if (SaveSourceBuilder.Length == 0 && !string.IsNullOrEmpty(lastSaveSourceInWindow))
                SaveSourceBuilder.Append(lastSaveSourceInWindow).Append(" (queued)");
            return SaveSourceBuilder.ToString();
        }

        public static long Begin(PerformanceProbeArea area)
        {
            int index = (int)area;
            if (!captureEnabled || index < 0 || index >= (int)PerformanceProbeArea.Count ||
                SampleNames == null || index >= SampleNames.Length ||
                string.IsNullOrWhiteSpace(SampleNames[index]))
                return 0L;

            // A malformed probe must never turn diagnostics into a stream of
            // WebGL console exceptions. Validate first and treat any remaining
            // profiler argument rejection as a disabled sample.
            try
            {
                Profiler.BeginSample(SampleNames[index]);
            }
            catch (ArgumentException)
            {
                return 0L;
            }

            long start = Stopwatch.GetTimestamp();
            return start == 0L ? 1L : start;
        }

        public static void End(PerformanceProbeArea area, long startedAt)
        {
            if (startedAt == 0L) return;
            long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - startedAt);
            Profiler.EndSample();
            int index = (int)area;
            if (index < 0 || index >= (int)PerformanceProbeArea.Count ||
                index >= WindowTicks.Length || index >= MaxTicks.Length || index >= Calls.Length)
                return;
            WindowTicks[index] += elapsed;
            if (elapsed > MaxTicks[index]) MaxTicks[index] = elapsed;
            Calls[index]++;
        }

        public static float WindowMilliseconds(PerformanceProbeArea area)
        {
            return (float)TicksToMilliseconds(WindowTicks[(int)area]);
        }

        public static int WindowCallCount(PerformanceProbeArea area)
        {
            return Calls[(int)area];
        }

        public static long WindowWorkCount(PerformanceProbeArea area)
        {
            return WindowWorkCounts[(int)area];
        }

        public static long WindowChangedCount(PerformanceProbeArea area)
        {
            return WindowChangedCounts[(int)area];
        }

        public static void RecordMaintenanceWorkCount(
            PerformanceProbeArea area, long recordsScanned, long recordsChanged)
        {
            if (!captureEnabled) return;
            int index = (int)area;
            if (index < 0 || index >= WindowWorkCounts.Length) return;
            WindowWorkCounts[index] += Math.Max(0L, recordsScanned);
            WindowChangedCounts[index] += Math.Max(0L, recordsChanged);
        }

        public static void RecordBrowserTelemetry(float averageFrameGapMs, float worstFrameGapMs,
            int frameCount, int longTaskCount, float longTaskTotalMs, float longTaskMaxMs,
            bool pageVisible, bool pageFocused, bool pageWasHidden, bool pageWasUnfocused,
            bool longTaskApiAvailable)
        {
            if (!captureEnabled) return;
            browserTelemetryPending = true;
            pendingBrowserAverageGapMs = averageFrameGapMs;
            pendingBrowserWorstGapMs = worstFrameGapMs;
            pendingBrowserFrameCount = Math.Max(0, frameCount);
            pendingBrowserLongTaskCount = Math.Max(0, longTaskCount);
            pendingBrowserLongTaskTotalMs = longTaskTotalMs;
            pendingBrowserLongTaskMaxMs = longTaskMaxMs;
            pendingPageVisible = pageVisible;
            pendingPageFocused = pageFocused;
            pendingPageWasHidden = pageWasHidden;
            pendingPageWasUnfocused = pageWasUnfocused;
            pendingLongTaskApiAvailable = longTaskApiAvailable;
        }

        private static void AttachPendingBrowserTelemetry(ref PerformanceLogSample sample, bool consume)
        {
            if (!browserTelemetryPending) return;
            sample.BrowserTelemetryAvailable = true;
            sample.BrowserAverageFrameGapMs = pendingBrowserAverageGapMs;
            sample.BrowserWorstFrameGapMs = pendingBrowserWorstGapMs;
            // A zero CPU sample commonly means that this profiler counter is
            // unsupported in WebGL. Keep the split unavailable rather than
            // falsely attributing the entire browser gap to non-Unity work.
            sample.BrowserGapWithinUnityCpuMs = sample.CpuMainThreadMs <= 0f || pendingBrowserAverageGapMs < 0f
                ? -1f
                : Mathf.Min(pendingBrowserAverageGapMs, sample.CpuMainThreadMs);
            sample.BrowserGapOutsideUnityCpuMs = sample.CpuMainThreadMs <= 0f || pendingBrowserAverageGapMs < 0f
                ? -1f
                : Mathf.Max(0f, pendingBrowserAverageGapMs - sample.CpuMainThreadMs);
            sample.BrowserFrameCount = pendingBrowserFrameCount;
            sample.BrowserLongTaskCount = pendingBrowserLongTaskCount;
            sample.BrowserLongTaskTotalMs = pendingBrowserLongTaskTotalMs;
            sample.BrowserLongTaskMaxMs = pendingBrowserLongTaskMaxMs;
            sample.PageVisible = pendingPageVisible;
            sample.PageFocused = pendingPageFocused;
            sample.PageWasHiddenDuringSample = pendingPageWasHidden;
            sample.PageWasUnfocusedDuringSample = pendingPageWasUnfocused;
            sample.LongTaskApiAvailable = pendingLongTaskApiAvailable;
            if (consume) browserTelemetryPending = false;
        }

        public static void RecordSample(PerformanceLogSample sample)
        {
            if (!captureEnabled || SampleRing.Length == 0) return;
            sample.SaveStorageMs = WindowMilliseconds(PerformanceProbeArea.SaveStorage);
            sample.SaveStorageOperations = WindowCallCount(PerformanceProbeArea.SaveStorage);
            sample.SaveExportMs = WindowMilliseconds(PerformanceProbeArea.SaveExport);
            sample.SaveExportOperations = WindowCallCount(PerformanceProbeArea.SaveExport);
            sample.SerializedDataBytes = serializedBytesInWindow;
            sample.LargestSerializedDataBytes = largestSerializedDataBytesInWindow;
            sample.SaveQueueRequests = saveQueueRequestsInWindow;
            sample.SaveQueueCoalescedRequests = saveQueueCoalescedInWindow;
            sample.SaveSource = BuildSaveSourceSummary();
            if (sample.CpuMainThreadMs <= 0f) sample.CpuMainThreadMs = -1f;
            AttachPendingBrowserTelemetry(ref sample, true);
            sample.UnattributedFrameGapMs = sample.GpuMs < 0f && sample.CpuMainThreadMs > 0f
                ? Mathf.Max(0f, sample.AverageFrameMs - sample.CpuMainThreadMs)
                : -1f;
            sampleWriteIndex = (sampleWriteIndex + 1) % SampleRing.Length;
            SampleRing[sampleWriteIndex] = sample;
            if (sampleCount < SampleRing.Length) sampleCount++;
            lastSampleUtcTicks = sample.UtcTicks > 0L ? sample.UtcTicks : DateTime.UtcNow.Ticks;
            TrackWorstSubsystem("Simulation/maintenance", sample.SimulationMaintenanceMs);
            TrackWorstSubsystem("Maintenance/growth", sample.MaintenanceGrowthMs);
            TrackWorstSubsystem("Maintenance/reproductive", sample.MaintenanceReproductiveMs);
            TrackWorstSubsystem("Maintenance/store", sample.MaintenanceStoreMs);
            TrackWorstSubsystem("Maintenance/sessions", sample.MaintenanceSessionsMs);
            TrackWorstSubsystem("Maintenance/births", sample.MaintenanceBirthsMs);
            TrackWorstSubsystem("Maintenance/weanings", sample.MaintenanceWeaningsMs);
            TrackWorstSubsystem("Maintenance/activities", sample.MaintenanceActivitiesMs);
            TrackWorstSubsystem("Maintenance/enclosures", sample.MaintenanceEnclosuresMs);
            TrackWorstSubsystem("Maintenance/sale eligibility", sample.MaintenanceSaleEligibilityMs);
            TrackWorstSubsystem("Maintenance/deadline resolution", sample.MaintenanceDeadlineResolutionMs);
            TrackWorstSubsystem("Maintenance/pairing checks", sample.MaintenancePairingChecksMs);
            TrackWorstSubsystem("Maintenance/historical litters", sample.MaintenanceHistoricalLittersMs);
            TrackWorstSubsystem("Maintenance/historical pregnancies", sample.MaintenanceHistoricalPregnanciesMs);
            TrackWorstSubsystem("Maintenance/historical rat index", sample.MaintenanceHistoricalRatIndexMs);
            TrackWorstSubsystem("Maintenance/birth repair recovery", sample.MaintenanceBirthRepairRecoveryMs);
            TrackWorstSubsystem("Nursing", sample.NursingMs);
            TrackWorstSubsystem("Simulation", sample.SimulationMs);
            TrackWorstSubsystem("UI refresh", sample.UiRefreshMs);
            TrackWorstSubsystem("Rat AI/movement", sample.RatAiMovementMs);
            TrackWorstSubsystem("Rat target selection", sample.RatTargetSelectionMs);
            TrackWorstSubsystem("World movement integration", sample.WorldMovementIntegrationMs);
            TrackWorstSubsystem("Rat spacing", sample.RatSpacingMs);
            TrackWorstSubsystem("Animation", sample.AnimationMs);
            TrackWorstSubsystem("Grounding/bounds", sample.GroundingBoundsMs);
            TrackWorstSubsystem("Rat presentation/rendering", sample.RatPresentationMs);
            TrackWorstSubsystem("Browser frame gap (not GPU)", sample.BrowserWorstFrameGapMs);
            TrackWorstSubsystem("Save/storage", sample.SaveStorageMs);
            TrackWorstSubsystem("Log export", sample.SaveExportMs);
            TrackWorstSubsystem("Input/interactions", sample.InputInteractionsMs);
        }

        /// <summary>Called once per rendered frame; creates a record only for a new lag episode or severity escalation.</summary>
        public static void ObserveFrame(float frameMs, long gameTimeMs, int speed, float cpuMs, float gpuMs,
            long gcAllocatedBytes, int gc0, int gc1, int gc2, int activeRats, int pinkies,
            int animators, int renderers, int uiGraphics, int canvases, int simulationSteps,
            float maintenanceMs, float uiRefreshMs, float animationMs, string panelName)
        {
            if (!captureEnabled) return;
            if (cpuMs <= 0f) cpuMs = -1f;
            PerformanceLogSeverity severity = frameMs >= GameConfig.PerformanceCriticalSpikeThresholdMs
                ? PerformanceLogSeverity.Critical
                : frameMs >= GameConfig.PerformanceLagSpikeThresholdMs
                    ? PerformanceLogSeverity.Lag
                    : PerformanceLogSeverity.None;

            if (frameMs > sessionWorstFrameMs)
            {
                sessionWorstFrameMs = frameMs;
                sessionWorstFrame = BuildFrameContext(frameMs, gameTimeMs, speed, cpuMs, gpuMs,
                    gcAllocatedBytes, gc0, gc1, gc2, activeRats, pinkies, animators, renderers,
                    uiGraphics, canvases, simulationSteps, maintenanceMs, uiRefreshMs, animationMs, panelName);
            }

            if (severity == PerformanceLogSeverity.None)
            {
                spikeEpisodeActive = false;
                spikeEpisodeSeverity = PerformanceLogSeverity.None;
                return;
            }
            if (spikeEpisodeActive && severity <= spikeEpisodeSeverity) return;

            spikeEpisodeActive = true;
            spikeEpisodeSeverity = severity;
            if (SpikeRing.Length == 0) return;
            spikeWriteIndex = (spikeWriteIndex + 1) % SpikeRing.Length;
            SpikeRing[spikeWriteIndex] = new PerformanceSpikeRecord
            {
                Severity = severity,
                IsUnattributedFrameGap = gpuMs < 0f && cpuMs > 0f &&
                    frameMs - cpuMs >= GameConfig.PerformanceUnattributedFrameGapThresholdMs,
                Context = BuildFrameContext(frameMs, gameTimeMs, speed, cpuMs, gpuMs,
                    gcAllocatedBytes, gc0, gc1, gc2, activeRats, pinkies, animators, renderers,
                    uiGraphics, canvases, simulationSteps, maintenanceMs, uiRefreshMs, animationMs, panelName),
            };
            if (spikeCount < SpikeRing.Length) spikeCount++;
        }

        private static PerformanceLogSample BuildFrameContext(float frameMs, long gameTimeMs, int speed,
            float cpuMs, float gpuMs, long allocatedBytes, int gc0, int gc1, int gc2,
            int activeRats, int pinkies, int animators, int renderers, int uiGraphics, int canvases,
            int simulationSteps, float maintenanceMs, float uiRefreshMs, float animationMs, string panelName)
        {
            PerformanceLogSample context = new PerformanceLogSample
            {
                UtcTicks = DateTime.UtcNow.Ticks,
                GameTimeMs = gameTimeMs,
                Speed = speed,
                WorstFrameMs = frameMs,
                CpuMainThreadMs = cpuMs,
                GpuMs = gpuMs,
                UnattributedFrameGapMs = gpuMs < 0f && cpuMs > 0f
                    ? Mathf.Max(0f, frameMs - cpuMs)
                    : -1f,
                BrowserAverageFrameGapMs = -1f,
                BrowserWorstFrameGapMs = -1f,
                BrowserGapWithinUnityCpuMs = -1f,
                BrowserGapOutsideUnityCpuMs = -1f,
                BrowserLongTaskTotalMs = -1f,
                BrowserLongTaskMaxMs = -1f,
                GcAllocatedBytes = allocatedBytes,
                Gc0Collections = gc0,
                Gc1Collections = gc1,
                Gc2Collections = gc2,
                ActiveRatCount = activeRats,
                PinkieCount = pinkies,
                AnimatorCount = animators,
                RendererCount = renderers,
                UiGraphicCount = uiGraphics,
                CanvasCount = canvases,
                SimulationSteps = simulationSteps,
                SimulationProbeCalls = WindowCallCount(PerformanceProbeArea.ClockAndAge) +
                    WindowCallCount(PerformanceProbeArea.PairingMovement) +
                    WindowCallCount(PerformanceProbeArea.RatBehaviorUpdate) +
                    WindowCallCount(PerformanceProbeArea.SimulationMaintenance) +
                    WindowCallCount(PerformanceProbeArea.NursingUpdate),
                RatBehaviorUpdates = GrowthSystem.LastBehaviorUpdateCount,
                RepeatedRatBehaviorUpdates = GrowthSystem.LastRepeatedBehaviorUpdateCount,
                SimulationMs = WindowMilliseconds(PerformanceProbeArea.ClockAndAge) +
                    WindowMilliseconds(PerformanceProbeArea.PairingMovement) +
                    WindowMilliseconds(PerformanceProbeArea.RatBehaviorUpdate) +
                    WindowMilliseconds(PerformanceProbeArea.SimulationMaintenance) +
                    WindowMilliseconds(PerformanceProbeArea.NursingUpdate),
                SimulationMaintenanceMs = WindowMilliseconds(PerformanceProbeArea.SimulationMaintenance),
                MaintenancePasses = WindowCallCount(PerformanceProbeArea.SimulationMaintenance),
                MaintenanceGrowthMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceGrowth),
                MaintenanceGrowthPasses = WindowCallCount(PerformanceProbeArea.MaintenanceGrowth),
                MaintenanceReproductiveMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceReproductive),
                MaintenanceReproductivePasses = WindowCallCount(PerformanceProbeArea.MaintenanceReproductive),
                MaintenanceStoreMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceStore),
                MaintenanceStorePasses = WindowCallCount(PerformanceProbeArea.MaintenanceStore),
                MaintenanceSessionsMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceSessions),
                MaintenanceSessionsPasses = WindowCallCount(PerformanceProbeArea.MaintenanceSessions),
                MaintenanceBirthsMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceBirths),
                MaintenanceBirthsPasses = WindowCallCount(PerformanceProbeArea.MaintenanceBirths),
                MaintenanceWeaningsMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceWeanings),
                MaintenanceWeaningsPasses = WindowCallCount(PerformanceProbeArea.MaintenanceWeanings),
                MaintenanceActivitiesMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceActivities),
                MaintenanceActivitiesPasses = WindowCallCount(PerformanceProbeArea.MaintenanceActivities),
                MaintenanceEnclosuresMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceEnclosures),
                MaintenanceEnclosuresPasses = WindowCallCount(PerformanceProbeArea.MaintenanceEnclosures),
                MaintenanceSaleEligibilityMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceSaleEligibility),
                MaintenanceSaleEligibilityPasses = WindowCallCount(PerformanceProbeArea.MaintenanceSaleEligibility),
                MaintenanceDeadlineResolutionMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceDeadlineResolution),
                MaintenanceDeadlineResolutionPasses = WindowCallCount(PerformanceProbeArea.MaintenanceDeadlineResolution),
                MaintenancePairingChecksMs = WindowMilliseconds(PerformanceProbeArea.MaintenancePairingChecks),
                MaintenancePairingChecksPasses = WindowCallCount(PerformanceProbeArea.MaintenancePairingChecks),
                MaintenanceHistoricalLittersMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceHistoricalLitters),
                MaintenanceHistoricalLitterScans = WindowCallCount(PerformanceProbeArea.MaintenanceHistoricalLitters),
                MaintenanceHistoricalLitterRecords = WindowWorkCounts[(int)PerformanceProbeArea.MaintenanceHistoricalLitters],
                MaintenanceHistoricalPregnanciesMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceHistoricalPregnancies),
                MaintenanceHistoricalPregnancyScans = WindowCallCount(PerformanceProbeArea.MaintenanceHistoricalPregnancies),
                MaintenanceHistoricalPregnancyRecords = WindowWorkCounts[(int)PerformanceProbeArea.MaintenanceHistoricalPregnancies],
                MaintenanceHistoricalRatIndexMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceHistoricalRatIndex),
                MaintenanceHistoricalRatIndexScans = WindowCallCount(PerformanceProbeArea.MaintenanceHistoricalRatIndex),
                MaintenanceHistoricalRatIndexRecords = WindowWorkCounts[(int)PerformanceProbeArea.MaintenanceHistoricalRatIndex],
                MaintenanceBirthRepairRecoveryMs = WindowMilliseconds(PerformanceProbeArea.MaintenanceBirthRepairRecovery),
                MaintenanceBirthRepairRecoveryPasses = WindowCallCount(PerformanceProbeArea.MaintenanceBirthRepairRecovery),
                MaintenanceBirthRepairRecordsScanned = WindowWorkCounts[(int)PerformanceProbeArea.MaintenanceBirthRepairRecovery],
                MaintenanceBirthRepairTransactionsReopened = WindowChangedCounts[(int)PerformanceProbeArea.MaintenanceBirthRepairRecovery],
                NursingMs = WindowMilliseconds(PerformanceProbeArea.NursingUpdate),
                NursingPasses = WindowCallCount(PerformanceProbeArea.NursingUpdate),
                UiRefreshMs = WindowMilliseconds(PerformanceProbeArea.UiRefresh),
                RatAiMovementMs = WindowMilliseconds(PerformanceProbeArea.RatBehaviorUpdate) +
                    WindowMilliseconds(PerformanceProbeArea.RatBehaviorLateUpdate),
                RatTargetSelectionMs = WindowMilliseconds(PerformanceProbeArea.RatDestinationSelection),
                WorldMovementIntegrationMs = WindowMilliseconds(PerformanceProbeArea.WorldMovementIntegration),
                RatSpacingMs = WindowMilliseconds(PerformanceProbeArea.RatSpacing),
                AnimationMs = animationMs,
                GroundingBoundsMs = WindowMilliseconds(PerformanceProbeArea.GroundingAndBounds),
                RatPresentationMs = WindowMilliseconds(PerformanceProbeArea.RatPresenterLateUpdate) +
                    WindowMilliseconds(PerformanceProbeArea.RatPresentationBuild) +
                    WindowMilliseconds(PerformanceProbeArea.RatMaterialSetup) +
                    WindowMilliseconds(PerformanceProbeArea.PinkieUpdate),
                RatPresentationUpdates = WindowCallCount(PerformanceProbeArea.RatPresenterLateUpdate) +
                    WindowCallCount(PerformanceProbeArea.RatPresentationBuild) +
                    WindowCallCount(PerformanceProbeArea.RatMaterialSetup) +
                    WindowCallCount(PerformanceProbeArea.PinkieUpdate),
                SaveExportMs = WindowMilliseconds(PerformanceProbeArea.SaveExport),
                SaveExportOperations = WindowCallCount(PerformanceProbeArea.SaveExport),
                SaveStorageMs = WindowMilliseconds(PerformanceProbeArea.SaveStorage),
                SaveStorageOperations = WindowCallCount(PerformanceProbeArea.SaveStorage),
                SerializedDataBytes = serializedBytesInWindow,
                LargestSerializedDataBytes = largestSerializedDataBytesInWindow,
                SaveQueueRequests = saveQueueRequestsInWindow,
                SaveQueueCoalescedRequests = saveQueueCoalescedInWindow,
                SaveSource = BuildSaveSourceSummary(),
                InputInteractionsMs = WindowMilliseconds(PerformanceProbeArea.InteractionUpdate),
                CurrentPanel = panelName,
            };
            AttachPendingBrowserTelemetry(ref context, false);
            return context;
        }

        private static void TrackWorstSubsystem(string name, float value)
        {
            if (value <= sessionWorstSubsystemMs) return;
            sessionWorstSubsystemMs = value;
            sessionWorstSubsystem = name;
        }

        public static void ClearLog()
        {
            Array.Clear(SampleRing, 0, SampleRing.Length);
            Array.Clear(SpikeRing, 0, SpikeRing.Length);
            sampleWriteIndex = spikeWriteIndex = -1;
            sampleCount = spikeCount = 0;
            spikeEpisodeActive = false;
            spikeEpisodeSeverity = PerformanceLogSeverity.None;
            sessionWorstFrameMs = sessionWorstSubsystemMs = 0f;
            sessionWorstFrame = default(PerformanceLogSample);
            sessionWorstSubsystem = "n/a";
            lastSampleUtcTicks = 0L;
            manualExportText = string.Empty;
            browserTelemetryPending = false;
            pendingBrowserAverageGapMs = pendingBrowserWorstGapMs = -1f;
            pendingBrowserFrameCount = pendingBrowserLongTaskCount = 0;
            pendingBrowserLongTaskTotalMs = pendingBrowserLongTaskMaxMs = -1f;
            pendingPageWasHidden = pendingPageWasUnfocused = false;
            // Clearing is a log operation, not a capture toggle. Leave the
            // recorder running so the next one-second sample starts a fresh
            // diagnostics window immediately.
            captureEnabled = true;
            ClearWindow();
        }

        public static string BuildLiveSummary()
        {
            if (sampleCount == 0) return captureEnabled
                ? "Capture ON — waiting for the next sample."
                : "Capture OFF — no samples recorded.";
            PerformanceLogSample latest = SampleAt(sampleCount - 1);
            ExportBuilder.Length = 0;
            ExportBuilder.Append(captureEnabled ? "CAPTURE ON" : "CAPTURE OFF")
                .Append(" • source ").Append(BuildRevision)
                .Append(" • ").Append(latest.CurrentPanel ?? "None")
                .Append(" • ").Append(GameCalendar.FormatTimestamp(latest.GameTimeMs))
                .Append(" • ").Append(latest.Speed).Append('x')
                .Append(" • ").Append(latest.Fps.ToString("0.0", CultureInfo.InvariantCulture)).Append(" FPS / ")
                .Append(latest.AverageFrameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms avg / ")
                .Append(latest.WorstFrameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms worst")
                .Append("\nRats ").Append(latest.ActiveRatCount).Append(" (pinkies ").Append(latest.PinkieCount)
                .Append(") • animators ").Append(latest.AnimatorCount).Append(" • renderers ").Append(latest.RendererCount)
                .Append(" • UI ").Append(latest.UiGraphicCount).Append(" graphics/").Append(latest.CanvasCount).Append(" canvases")
                .Append("\nCPU ").Append(FormatMetric(latest.CpuMainThreadMs)).Append(" • GPU ").Append(FormatMetric(latest.GpuMs))
                .Append(" • unattributed frame remainder ").Append(FormatMetric(latest.UnattributedFrameGapMs))
                .Append(" • browser frame gap ").Append(FormatMetric(latest.BrowserWorstFrameGapMs))
                .Append(" • avg rAF gap: measured CPU / beyond CPU ").Append(FormatMetric(latest.BrowserGapWithinUnityCpuMs)).Append('/')
                .Append(FormatMetric(latest.BrowserGapOutsideUnityCpuMs))
                .Append(" • GC ").Append(latest.GcAllocatedBytes).Append(" B in sample; collections ")
                .Append(latest.Gc0Collections).Append('/').Append(latest.Gc1Collections).Append('/').Append(latest.Gc2Collections)
                .Append(" • sim steps/rat updates/repeats ").Append(latest.SimulationSteps).Append('/')
                .Append(latest.RatBehaviorUpdates).Append('/').Append(latest.RepeatedRatBehaviorUpdates)
                .Append(" • sim probe calls ").Append(latest.SimulationProbeCalls)
                .Append(" • sim ").Append(FormatMetric(latest.SimulationMs))
                .Append(" • maintenance passes ").Append(latest.MaintenancePasses)
                .Append(" • G/R/Store/Sess/Birth/Wean/Act/Encl/Sale ms ")
                .Append(latest.MaintenanceGrowthMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceReproductiveMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceStoreMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceSessionsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceBirthsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceWeaningsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceActivitiesMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceEnclosuresMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceSaleEligibilityMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(" ms; nursing ").Append(latest.NursingMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(" ms x").Append(latest.NursingPasses)
                .Append(" • deadline/pairing/histL/histP/repair ms ")
                .Append(latest.MaintenanceDeadlineResolutionMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenancePairingChecksMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceHistoricalLittersMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceHistoricalPregnanciesMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceHistoricalRatIndexMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(latest.MaintenanceBirthRepairRecoveryMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("; hist records L/P/R ").Append(latest.MaintenanceHistoricalLitterRecords).Append('/')
                .Append(latest.MaintenanceHistoricalPregnancyRecords).Append('/')
                .Append(latest.MaintenanceHistoricalRatIndexRecords)
                .Append("; birth repair scanned/reopened ").Append(latest.MaintenanceBirthRepairRecordsScanned).Append('/')
                .Append(latest.MaintenanceBirthRepairTransactionsReopened)
                .Append(" • save ").Append(FormatMetric(latest.SaveStorageMs)).Append(" x")
                .Append(latest.SaveStorageOperations).Append(" ops/JSON ").Append(latest.SerializedDataBytes)
                .Append(" B total, max ").Append(latest.LargestSerializedDataBytes).Append(" B")
                .Append(" • save source ").Append(string.IsNullOrEmpty(latest.SaveSource) ? "n/a" : latest.SaveSource)
                .Append(" • save requests/coalesced ").Append(latest.SaveQueueRequests).Append('/')
                .Append(latest.SaveQueueCoalescedRequests)
                .Append(" • export ").Append(FormatMetric(latest.SaveExportMs)).Append(" x")
                .Append(latest.SaveExportOperations)
                .Append(" • page ").Append(latest.BrowserTelemetryAvailable
                    ? (latest.PageVisible ? "visible" : "hidden") + "/" + (latest.PageFocused ? "focused" : "blurred")
                    : "browser telemetry n/a")
                .Append(latest.PageWasHiddenDuringSample ? " (hidden during window)" : string.Empty)
                .Append(latest.PageWasUnfocusedDuringSample ? " (unfocused during window)" : string.Empty)
                .Append(" • long tasks ").Append(latest.LongTaskApiAvailable
                    ? latest.BrowserLongTaskCount.ToString(CultureInfo.InvariantCulture) + "/" +
                        latest.BrowserLongTaskMaxMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms max"
                    : "n/a")
                .Append("\nWorst session frame ").Append(sessionWorstFrameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(" @ ").Append(new DateTime(sessionWorstFrame.UtcTicks, DateTimeKind.Utc).ToString("HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Append(" • ").Append(sessionWorstFrame.CurrentPanel ?? "None")
                .Append(" • rats/pinkies ").Append(sessionWorstFrame.ActiveRatCount).Append('/').Append(sessionWorstFrame.PinkieCount)
                .Append(" • worst subsystem ").Append(sessionWorstSubsystem).Append(" ")
                .Append(sessionWorstSubsystemMs.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ms/window");
            return ExportBuilder.ToString();
        }

        public static string BuildStatusText()
        {
            ExportBuilder.Length = 0;
            ExportBuilder.Append(captureEnabled ? "Capture ON" : "Capture OFF");
            if (sampleCount == 0)
            {
                ExportBuilder.Append(captureEnabled
                    ? " — waiting for the next sample."
                    : " — no samples recorded.");
                ExportBuilder.Append(" • 0 samples • 0 lag episodes • last sample waiting");
            }
            else
            {
                double elapsedSeconds = lastSampleUtcTicks <= 0L
                    ? double.PositiveInfinity
                    : Math.Max(0d, (DateTime.UtcNow.Ticks - lastSampleUtcTicks) / (double)TimeSpan.TicksPerSecond);
                ExportBuilder.Append(" • ").Append(sampleCount).Append(" samples • ")
                    .Append(spikeCount).Append(" lag episodes • last sample ");
                if (double.IsInfinity(elapsedSeconds)) ExportBuilder.Append("time unavailable");
                else ExportBuilder.Append(elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append("s ago");
            }
            ExportBuilder.Append("\nLast copy/download: ").Append(lastExportActionStatus);
            return ExportBuilder.ToString();
        }

        public static string BuildRecentSamplesText(int maximumCount)
        {
            ExportBuilder.Length = 0;
            int count = Math.Min(Math.Max(0, maximumCount), sampleCount);
            if (count == 0) return "No performance samples yet.";
            int first = sampleCount - count;
            for (int index = first; index < sampleCount; index++)
            {
                PerformanceLogSample sample = SampleAt(index);
                AppendCompactSample(ExportBuilder, sample);
                if (index < sampleCount - 1) ExportBuilder.Append('\n');
            }
            return ExportBuilder.ToString();
        }

        public static string BuildRecentSpikesText(int maximumCount)
        {
            ExportBuilder.Length = 0;
            int count = Math.Min(Math.Max(0, maximumCount), spikeCount);
            if (count == 0) return "No lag spikes recorded (threshold " + GameConfig.PerformanceLagSpikeThresholdMs + " ms).";
            int first = spikeCount - count;
            for (int index = first; index < spikeCount; index++)
            {
                PerformanceSpikeRecord spike = SpikeAt(index);
                if (spike.IsUnattributedFrameGap) ExportBuilder.Append("UNATTRIBUTED FRAME GAP • ");
                ExportBuilder.Append(spike.Severity == PerformanceLogSeverity.Critical ? "CRITICAL" : "LAG")
                    .Append(" • ");
                AppendCompactSample(ExportBuilder, spike.Context);
                if (index < spikeCount - 1) ExportBuilder.Append('\n');
            }
            return ExportBuilder.ToString();
        }

        public static string BuildExportText(bool csv)
        {
            string summary = csv ? null : BuildLiveSummary();
            ExportBuilder.Length = 0;
            if (!csv)
            {
                ExportBuilder.AppendLine("Rat Empire Performance Log")
                    .AppendLine(summary)
                    .AppendLine("\nSamples (oldest to newest)");
            }
            else
            {
                ExportBuilder.AppendLine("record,severity,source_revision,utc,game_day_time,speed,fps,avg_frame_ms,worst_frame_ms,cpu_main_ms,gpu_ms,unattributed_frame_gap_ms,browser_avg_frame_gap_ms,browser_worst_frame_gap_ms,browser_gap_within_unity_cpu_ms,browser_gap_outside_unity_cpu_ms,browser_frame_count,browser_telemetry_available,page_visible,page_focused,page_hidden_during_window,page_unfocused_during_window,long_task_api_available,long_task_count,long_task_total_ms,long_task_max_ms,gc_alloc_bytes,gc0,gc1,gc2,rats,pinkies,animators,renderers,ui_graphics,canvases,simulation_steps,simulation_probe_calls,rat_behavior_updates,repeated_rat_behavior_updates,simulation_ms,maintenance_ms,maintenance_passes,maintenance_growth_ms,maintenance_growth_passes,maintenance_reproductive_ms,maintenance_reproductive_passes,maintenance_store_ms,maintenance_store_passes,maintenance_sessions_ms,maintenance_sessions_passes,maintenance_births_ms,maintenance_births_passes,maintenance_weanings_ms,maintenance_weanings_passes,maintenance_activities_ms,maintenance_activities_passes,maintenance_enclosures_ms,maintenance_enclosures_passes,maintenance_sale_eligibility_ms,maintenance_sale_eligibility_passes,maintenance_deadline_resolution_ms,maintenance_deadline_resolution_passes,maintenance_pairing_checks_ms,maintenance_pairing_checks_passes,maintenance_historical_litters_ms,maintenance_historical_litter_scans,maintenance_historical_litter_records,maintenance_historical_pregnancies_ms,maintenance_historical_pregnancy_scans,maintenance_historical_pregnancy_records,maintenance_historical_rat_index_ms,maintenance_historical_rat_index_scans,maintenance_historical_rat_index_records,maintenance_birth_repair_recovery_ms,maintenance_birth_repair_recovery_passes,maintenance_birth_repair_records_scanned,maintenance_birth_repair_transactions_reopened,nursing_ms,nursing_passes,ui_refresh_ms,rat_ai_movement_ms,rat_target_selection_ms,world_movement_integration_ms,rat_spacing_ms,animation_ms,grounding_bounds_ms,presentation_ms,presentation_updates,save_storage_ms,save_storage_operations,serialized_data_bytes_total,serialized_data_bytes_max,save_queue_requests,save_queue_coalesced_requests,save_source,save_export_ms,save_export_operations,input_ms,panel");
            }

            for (int index = 0; index < sampleCount; index++)
            {
                PerformanceLogSample sample = SampleAt(index);
                if (csv) AppendCsvRecord(ExportBuilder, "sample", "", sample);
                else AppendCompactSample(ExportBuilder, sample).Append('\n');
            }
            if (!csv) ExportBuilder.AppendLine("\nLag spikes (oldest to newest)");
            for (int index = 0; index < spikeCount; index++)
            {
                PerformanceSpikeRecord spike = SpikeAt(index);
                if (csv) AppendCsvRecord(ExportBuilder, "spike",
                    spike.IsUnattributedFrameGap ? spike.Severity + "_FRAME_GAP" : spike.Severity.ToString(), spike.Context);
                else
                {
                    if (spike.IsUnattributedFrameGap) ExportBuilder.Append("UNATTRIBUTED FRAME GAP • ");
                    ExportBuilder.Append(spike.Severity == PerformanceLogSeverity.Critical ? "CRITICAL • " : "LAG • ");
                    AppendCompactSample(ExportBuilder, spike.Context).Append('\n');
                }
            }
            return ExportBuilder.ToString();
        }

        public static string BuildCompactDiagnostics()
        {
            string summary = BuildLiveSummary();
            string spikes = BuildRecentSpikesText(5);
            ExportBuilder.Length = 0;
            ExportBuilder.Append(summary).Append("\n\nRecent spikes\n").Append(spikes);
            return ExportBuilder.ToString();
        }

        public static bool TryCopyText(string text, string callbackReceiver = null)
        {
            if (string.IsNullOrEmpty(text)) return false;
            long exportSample = Begin(PerformanceProbeArea.SaveExport);
            try
            {
                manualExportText = text;
                int actionId = ++exportActionSequence;
#if UNITY_WEBGL && !UNITY_EDITOR
                try
                {
                    int result = RatPerformanceCopyText(text, callbackReceiver ?? string.Empty,
                        "OnPerformanceClipboardResult", actionId.ToString(CultureInfo.InvariantCulture));
                    if (result == 1)
                    {
                        pendingClipboardAction = 0;
                        lastExportActionStatus = "Copied to clipboard.";
                        return true;
                    }
                    if (result == 2)
                    {
                        pendingClipboardAction = actionId;
                        lastExportActionStatus = "Clipboard permission pending; manual copy text is available below.";
                        return true;
                    }
                    pendingClipboardAction = 0;
                    lastExportActionStatus = "Clipboard unavailable or blocked; use the manual copy text below.";
                    return false;
                }
                catch (Exception exception)
                {
                    pendingClipboardAction = 0;
                    lastExportActionStatus = "Clipboard failed: " + exception.Message + "; use the manual copy text below.";
                    return false;
                }
#elif UNITY_EDITOR
                GUIUtility.systemCopyBuffer = text;
                lastExportActionStatus = "Copied to clipboard (Editor).";
                return true;
#else
                lastExportActionStatus = "Clipboard unavailable; use the manual copy text below.";
                return false;
#endif
            }
            finally
            {
                End(PerformanceProbeArea.SaveExport, exportSample);
            }
        }

        public static void CompleteClipboardAction(int actionId, bool copied)
        {
            if (actionId <= 0 || actionId != pendingClipboardAction) return;
            pendingClipboardAction = 0;
            lastExportActionStatus = copied
                ? "Copied to clipboard."
                : "Clipboard blocked; select and copy the manual text below.";
        }

        public static bool TryDownloadLog(bool csv)
        {
            long exportSample = Begin(PerformanceProbeArea.SaveExport);
            try
            {
                string content = BuildExportText(csv);
                manualExportText = content;
#if UNITY_WEBGL && !UNITY_EDITOR
                try
                {
                    bool requested = RatPerformanceDownloadText(csv ? "rat-empire-performance.csv" : "rat-empire-performance.txt",
                        content, csv ? "text/csv;charset=utf-8" : "text/plain;charset=utf-8") != 0;
                    lastExportActionStatus = requested
                        ? "Browser download requested; if blocked, use the manual copy text below."
                        : "Browser download failed; use the manual copy text below.";
                    return requested;
                }
                catch (Exception exception)
                {
                    lastExportActionStatus = "Browser download failed: " + exception.Message + "; use the manual copy text below.";
                    return false;
                }
#else
                lastExportActionStatus = "Browser download unavailable in this player; use the manual copy text below.";
                return false;
#endif
            }
            finally
            {
                End(PerformanceProbeArea.SaveExport, exportSample);
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void RatPerformanceStartBrowserTelemetryNative(string target, string callbackMethod);
        [DllImport("__Internal")] private static extern void RatPerformanceStopBrowserTelemetryNative();
        [DllImport("__Internal")] private static extern int RatPerformanceCopyText(string text, string callbackReceiver, string callbackMethod, string actionId);
        [DllImport("__Internal")] private static extern int RatPerformanceDownloadText(string fileName, string content, string mimeType);
#endif

        public static void StartBrowserTelemetry(string target, string callbackMethod)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RatPerformanceStartBrowserTelemetryNative(target, callbackMethod);
#endif
        }

        public static void StopBrowserTelemetry()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RatPerformanceStopBrowserTelemetryNative();
#endif
        }

        private static PerformanceLogSample SampleAt(int chronologicalIndex)
        {
            int oldest = sampleCount < SampleRing.Length ? 0 : (sampleWriteIndex + 1) % SampleRing.Length;
            return SampleRing[(oldest + chronologicalIndex) % SampleRing.Length];
        }

        private static PerformanceSpikeRecord SpikeAt(int chronologicalIndex)
        {
            int oldest = spikeCount < SpikeRing.Length ? 0 : (spikeWriteIndex + 1) % SpikeRing.Length;
            return SpikeRing[(oldest + chronologicalIndex) % SpikeRing.Length];
        }

        private static string FormatMetric(float value)
        {
            return value < 0f ? "n/a" : value.ToString("0.00", CultureInfo.InvariantCulture) + " ms";
        }

        private static StringBuilder AppendCompactSample(StringBuilder builder, PerformanceLogSample sample)
        {
            builder.Append(new DateTime(sample.UtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
                .Append(" • src ").Append(BuildRevision)
                .Append(" • ").Append(GameCalendar.FormatTimestamp(sample.GameTimeMs)).Append(" • ").Append(sample.Speed).Append('x')
                .Append(" • FPS ").Append(sample.Fps.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" • frame ").Append(sample.AverageFrameMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("/")
                .Append(sample.WorstFrameMs.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(" • CPU/GPU ").Append(FormatMetric(sample.CpuMainThreadMs)).Append('/').Append(FormatMetric(sample.GpuMs))
                .Append(" • frame remainder ").Append(FormatMetric(sample.UnattributedFrameGapMs))
                .Append(" • browser gap ").Append(FormatMetric(sample.BrowserWorstFrameGapMs))
                .Append(" avg rAF gap measured CPU/beyond CPU ").Append(FormatMetric(sample.BrowserGapWithinUnityCpuMs)).Append('/')
                .Append(FormatMetric(sample.BrowserGapOutsideUnityCpuMs))
                .Append(" • page ").Append(sample.BrowserTelemetryAvailable
                    ? (sample.PageVisible ? "visible" : "hidden") + "/" + (sample.PageFocused ? "focused" : "blurred")
                    : "n/a")
                .Append(sample.PageWasHiddenDuringSample ? " (hidden during window)" : string.Empty)
                .Append(sample.PageWasUnfocusedDuringSample ? " (unfocused during window)" : string.Empty)
                .Append(" • long tasks ").Append(sample.LongTaskApiAvailable
                    ? sample.BrowserLongTaskCount.ToString(CultureInfo.InvariantCulture) + "/" +
                        sample.BrowserLongTaskMaxMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms max"
                    : "n/a")
                .Append(" • GC ").Append(sample.GcAllocatedBytes).Append(" B, ")
                .Append(sample.Gc0Collections).Append('/').Append(sample.Gc1Collections).Append('/').Append(sample.Gc2Collections)
                .Append(" • rats/pinkies ").Append(sample.ActiveRatCount).Append('/').Append(sample.PinkieCount)
                .Append(" • sim steps/rat updates/repeats ").Append(sample.SimulationSteps).Append('/')
                .Append(sample.RatBehaviorUpdates).Append('/').Append(sample.RepeatedRatBehaviorUpdates)
                .Append(" • sim ").Append(sample.SimulationMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • maint/UI ").Append(sample.SimulationMaintenanceMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.UiRefreshMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • maint passes ").Append(sample.MaintenancePasses)
                .Append(" • maint G/R/Store/Sess/Birth/Wean/Act/Encl/Sale ms ")
                .Append(sample.MaintenanceGrowthMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceReproductiveMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceStoreMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceSessionsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceBirthsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceWeaningsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceActivitiesMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceEnclosuresMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceSaleEligibilityMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("ms • nursing ").Append(sample.NursingMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("ms x").Append(sample.NursingPasses)
                .Append(" • deadline/pairing/histL/histP/repair ms ")
                .Append(sample.MaintenanceDeadlineResolutionMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenancePairingChecksMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceHistoricalLittersMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceHistoricalPregnanciesMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.MaintenanceBirthRepairRecoveryMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(" • history records L/P/R ").Append(sample.MaintenanceHistoricalLitterRecords).Append('/')
                .Append(sample.MaintenanceHistoricalPregnancyRecords).Append('/')
                .Append(sample.MaintenanceHistoricalRatIndexRecords)
                .Append(" • birth repair scan/reopen ").Append(sample.MaintenanceBirthRepairRecordsScanned).Append('/')
                .Append(sample.MaintenanceBirthRepairTransactionsReopened)
                .Append(" • AI/anim/ground ").Append(sample.RatAiMovementMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.AnimationMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.GroundingBoundsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • target/move/spacing ").Append(sample.RatTargetSelectionMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.WorldMovementIntegrationMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.RatSpacingMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • present/input ").Append(sample.RatPresentationMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.InputInteractionsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • present updates ").Append(sample.RatPresentationUpdates)
                .Append(" • save ").Append(sample.SaveStorageMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("ms (").Append(sample.SaveStorageOperations).Append(" ops, ")
                .Append(sample.SerializedDataBytes).Append(" B total, max ")
                .Append(sample.LargestSerializedDataBytes).Append(" B; source ")
                .Append(string.IsNullOrEmpty(sample.SaveSource) ? "n/a" : sample.SaveSource)
                .Append("; queued/coalesced ").Append(sample.SaveQueueRequests).Append('/')
                .Append(sample.SaveQueueCoalescedRequests).Append(')')
                .Append(" • export ").Append(sample.SaveExportMs.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("ms (").Append(sample.SaveExportOperations).Append(" ops)")
                .Append(" • ").Append(sample.CurrentPanel ?? "None");
            return builder;
        }

        private static void AppendCsvRecord(StringBuilder builder, string type, string severity, PerformanceLogSample sample)
        {
            builder.Append(type).Append(',').Append(severity).Append(',').Append(BuildRevision).Append(',')
                .Append(new DateTime(sample.UtcTicks, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture)).Append(',')
                .Append('"').Append(GameCalendar.FormatTimestamp(sample.GameTimeMs).Replace("\"", "\"\"")).Append('"')
                .Append(',').Append(sample.Speed).Append(',')
                .Append(sample.Fps.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.AverageFrameMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.WorstFrameMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.CpuMainThreadMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.GpuMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.UnattributedFrameGapMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.BrowserAverageFrameGapMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.BrowserWorstFrameGapMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.BrowserGapWithinUnityCpuMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.BrowserGapOutsideUnityCpuMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.BrowserFrameCount).Append(',')
                .Append(sample.BrowserTelemetryAvailable ? 1 : 0).Append(',')
                .Append(sample.PageVisible ? 1 : 0).Append(',')
                .Append(sample.PageFocused ? 1 : 0).Append(',')
                .Append(sample.PageWasHiddenDuringSample ? 1 : 0).Append(',')
                .Append(sample.PageWasUnfocusedDuringSample ? 1 : 0).Append(',')
                .Append(sample.LongTaskApiAvailable ? 1 : 0).Append(',')
                .Append(sample.BrowserLongTaskCount).Append(',')
                .Append(sample.BrowserLongTaskTotalMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.BrowserLongTaskMaxMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.GcAllocatedBytes).Append(',').Append(sample.Gc0Collections).Append(',')
                .Append(sample.Gc1Collections).Append(',').Append(sample.Gc2Collections).Append(',')
                .Append(sample.ActiveRatCount).Append(',').Append(sample.PinkieCount).Append(',')
                .Append(sample.AnimatorCount).Append(',').Append(sample.RendererCount).Append(',')
                .Append(sample.UiGraphicCount).Append(',').Append(sample.CanvasCount).Append(',')
                .Append(sample.SimulationSteps).Append(',')
                .Append(sample.SimulationProbeCalls).Append(',')
                .Append(sample.RatBehaviorUpdates).Append(',').Append(sample.RepeatedRatBehaviorUpdates).Append(',')
                .Append(sample.SimulationMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.SimulationMaintenanceMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenancePasses).Append(',')
                .Append(sample.MaintenanceGrowthMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceGrowthPasses).Append(',')
                .Append(sample.MaintenanceReproductiveMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceReproductivePasses).Append(',')
                .Append(sample.MaintenanceStoreMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceStorePasses).Append(',')
                .Append(sample.MaintenanceSessionsMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceSessionsPasses).Append(',')
                .Append(sample.MaintenanceBirthsMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceBirthsPasses).Append(',')
                .Append(sample.MaintenanceWeaningsMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceWeaningsPasses).Append(',')
                .Append(sample.MaintenanceActivitiesMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceActivitiesPasses).Append(',')
                .Append(sample.MaintenanceEnclosuresMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceEnclosuresPasses).Append(',')
                .Append(sample.MaintenanceSaleEligibilityMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceSaleEligibilityPasses).Append(',')
                .Append(sample.MaintenanceDeadlineResolutionMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceDeadlineResolutionPasses).Append(',')
                .Append(sample.MaintenancePairingChecksMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenancePairingChecksPasses).Append(',')
                .Append(sample.MaintenanceHistoricalLittersMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceHistoricalLitterScans).Append(',')
                .Append(sample.MaintenanceHistoricalLitterRecords).Append(',')
                .Append(sample.MaintenanceHistoricalPregnanciesMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceHistoricalPregnancyScans).Append(',')
                .Append(sample.MaintenanceHistoricalPregnancyRecords).Append(',')
                .Append(sample.MaintenanceHistoricalRatIndexMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceHistoricalRatIndexScans).Append(',')
                .Append(sample.MaintenanceHistoricalRatIndexRecords).Append(',')
                .Append(sample.MaintenanceBirthRepairRecoveryMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.MaintenanceBirthRepairRecoveryPasses).Append(',')
                .Append(sample.MaintenanceBirthRepairRecordsScanned).Append(',')
                .Append(sample.MaintenanceBirthRepairTransactionsReopened).Append(',')
                .Append(sample.NursingMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.NursingPasses).Append(',')
                .Append(sample.UiRefreshMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.RatAiMovementMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.RatTargetSelectionMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.WorldMovementIntegrationMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.RatSpacingMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.AnimationMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.GroundingBoundsMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.RatPresentationMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.RatPresentationUpdates).Append(',')
                .Append(sample.SaveStorageMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.SaveStorageOperations).Append(',')
                .Append(sample.SerializedDataBytes).Append(',')
                .Append(sample.LargestSerializedDataBytes).Append(',')
                .Append(sample.SaveQueueRequests).Append(',')
                .Append(sample.SaveQueueCoalescedRequests).Append(',')
                .Append('"').Append((sample.SaveSource ?? string.Empty).Replace("\"", "\"\"")).Append('"').Append(',')
                .Append(sample.SaveExportMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.SaveExportOperations).Append(',')
                .Append(sample.InputInteractionsMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append('"').Append((sample.CurrentPanel ?? "None").Replace("\"", "\"\"")).Append('"').AppendLine();
        }

        public static void SetCaptureEnabled(bool enabled)
        {
            captureEnabled = enabled;
            ClearWindow();
        }

        public static void SetHudVisible(bool visible)
        {
            hudVisible = visible;
        }

        public static void SetIsolationMode(PerformanceIsolationMode mode)
        {
            isolationMode = mode;
        }

        public static void ToggleIsolationMode(PerformanceIsolationMode mode)
        {
            if (mode == PerformanceIsolationMode.Normal)
            {
                isolationMode = PerformanceIsolationMode.Normal;
                return;
            }
            isolationMode ^= mode;
        }

        public static bool IsIsolationActive(PerformanceIsolationMode mode)
        {
            if (mode == PerformanceIsolationMode.Normal)
                return isolationMode == PerformanceIsolationMode.Normal;
            return (isolationMode & mode) == mode;
        }

        public static string ConsumeSampleSummary()
        {
            return ConsumeSampleSummary(true);
        }

        public static string ConsumeSampleSummary(bool formatForHud)
        {
            if (!formatForHud)
            {
                ClearWindow();
                return string.Empty;
            }
            SummaryBuilder.Length = 0;
            bool found = false;
            for (int i = 0; i < Calls.Length; i++)
            {
                if (Calls[i] == 0) continue;
                if (found) SummaryBuilder.Append("  |  ");
                found = true;
                SummaryBuilder.Append(SampleShortName((PerformanceProbeArea)i));
                SummaryBuilder.Append(' ');
                SummaryBuilder.Append(TicksToMilliseconds(WindowTicks[i]).ToString("0.0"));
                SummaryBuilder.Append("ms/s n=");
                SummaryBuilder.Append(Calls[i]);
                SummaryBuilder.Append(" max=");
                SummaryBuilder.Append((TicksToMilliseconds(MaxTicks[i]) * 1000d).ToString("0"));
                SummaryBuilder.Append("us");
            }
            ClearWindow();
            return found ? SummaryBuilder.ToString() : (captureEnabled ? "waiting for samples" : "capture off");
        }

        public static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }

        public static string SampleShortName(PerformanceProbeArea area)
        {
            switch (area)
            {
                case PerformanceProbeArea.BootstrapUpdate: return "Bootstrap";
                case PerformanceProbeArea.ClockAndAge: return "Clock/age";
                case PerformanceProbeArea.SimulationMaintenance: return "Maintenance";
                case PerformanceProbeArea.MaintenanceGrowth: return "Maint growth";
                case PerformanceProbeArea.MaintenanceReproductive: return "Maint reproductive";
                case PerformanceProbeArea.MaintenanceStore: return "Maint store";
                case PerformanceProbeArea.MaintenanceSessions: return "Maint sessions";
                case PerformanceProbeArea.MaintenanceBirths: return "Maint births";
                case PerformanceProbeArea.MaintenanceWeanings: return "Maint weanings";
                case PerformanceProbeArea.MaintenanceActivities: return "Maint activities";
                case PerformanceProbeArea.MaintenanceEnclosures: return "Maint enclosures";
                case PerformanceProbeArea.MaintenanceSaleEligibility: return "Maint sale eligibility";
                case PerformanceProbeArea.MaintenanceDeadlineResolution: return "Maint deadlines";
                case PerformanceProbeArea.MaintenancePairingChecks: return "Maint pairing checks";
                case PerformanceProbeArea.MaintenanceHistoricalLitters: return "History litters";
                case PerformanceProbeArea.MaintenanceHistoricalPregnancies: return "History pregnancies";
                case PerformanceProbeArea.MaintenanceHistoricalRatIndex: return "History rat index";
                case PerformanceProbeArea.MaintenanceBirthRepairRecovery: return "Birth repair recovery";
                case PerformanceProbeArea.PairingMovement: return "Pairing movement";
                case PerformanceProbeArea.RatBehaviorUpdate: return "Rat AI";
                case PerformanceProbeArea.RatBehaviorLateUpdate: return "Rat facing";
                case PerformanceProbeArea.RatDestinationSelection: return "Target choice";
                case PerformanceProbeArea.RatPresenterLateUpdate: return "Presenter";
                case PerformanceProbeArea.GroundingAndBounds: return "Ground/bounds";
                case PerformanceProbeArea.PinkieUpdate: return "Pinkie";
                case PerformanceProbeArea.UiUpdate: return "UI update";
                case PerformanceProbeArea.UiRefresh: return "UI refresh";
                case PerformanceProbeArea.UiRebuildAndLayout: return "UI build/layout";
                case PerformanceProbeArea.InteractionUpdate: return "Input";
                case PerformanceProbeArea.RatPresentationBuild: return "Rat render/rebuild";
                case PerformanceProbeArea.RatMaterialSetup: return "Coat/material";
                case PerformanceProbeArea.NursingUpdate: return "Nursing";
                case PerformanceProbeArea.SaveStorage: return "Save/storage";
                case PerformanceProbeArea.SaveExport: return "Log export";
                case PerformanceProbeArea.WorldMovementIntegration: return "World movement";
                case PerformanceProbeArea.RatSpacing: return "Rat spacing";
                default: return "Unknown";
            }
        }

        private static void ClearWindow()
        {
            Array.Clear(WindowTicks, 0, WindowTicks.Length);
            Array.Clear(MaxTicks, 0, MaxTicks.Length);
            Array.Clear(Calls, 0, Calls.Length);
            Array.Clear(WindowWorkCounts, 0, WindowWorkCounts.Length);
            Array.Clear(WindowChangedCounts, 0, WindowChangedCounts.Length);
            saveQueueRequestsInWindow = 0;
            saveQueueCoalescedInWindow = 0;
            serializedBytesInWindow = 0L;
            largestSerializedDataBytesInWindow = 0L;
            lastSaveSourceInWindow = string.Empty;
            Array.Clear(SaveSourceNames, 0, SaveSourceNames.Length);
            Array.Clear(SaveSourceCounts, 0, SaveSourceCounts.Length);
            saveSourceOverflowCount = 0;
            SaveSourceBuilder.Length = 0;
        }
#else
        public static bool CaptureEnabled { get { return false; } }
        public static bool HudVisible { get { return false; } }
        public static PerformanceIsolationMode IsolationMode { get { return PerformanceIsolationMode.Normal; } }
        public static int SampleCount { get { return 0; } }
        public static int SpikeCount { get { return 0; } }
        public static string LastExportActionStatus { get { return "Performance capture is available in Development builds."; } }
        public static string ManualExportText { get { return string.Empty; } }
        public static string GetManualExportChunk(string raw, int page, int chunkSize) { return string.Empty; }
        public static long Begin(PerformanceProbeArea area) { return 0L; }
        public static void End(PerformanceProbeArea area, long startedAt) { }
        public static int WindowCallCount(PerformanceProbeArea area) { return 0; }
        public static long WindowWorkCount(PerformanceProbeArea area) { return 0L; }
        public static long WindowChangedCount(PerformanceProbeArea area) { return 0L; }
        public static void RecordMaintenanceWorkCount(PerformanceProbeArea area, long recordsScanned, long recordsChanged) { }
        public static void RecordSaveQueueRequest(string source, bool coalesced) { }
        public static void RecordSaveSerializedData(string source, int byteCount) { }
        public static void RecordBrowserTelemetry(float averageFrameGapMs, float worstFrameGapMs,
            int frameCount, int longTaskCount, float longTaskTotalMs, float longTaskMaxMs,
            bool pageVisible, bool pageFocused, bool pageWasHidden, bool pageWasUnfocused,
            bool longTaskApiAvailable) { }
        public static void StartBrowserTelemetry(string target, string callbackMethod) { }
        public static void StopBrowserTelemetry() { }
        public static void SetCaptureEnabled(bool enabled) { }
        public static void ClearLog() { }
        public static string BuildLiveSummary() { return "Performance capture is available in Development builds."; }
        public static string BuildStatusText() { return "Performance capture is available in Development builds."; }
        public static string BuildRecentSamplesText(int maximumCount) { return string.Empty; }
        public static string BuildRecentSpikesText(int maximumCount) { return string.Empty; }
        public static string BuildExportText(bool csv) { return string.Empty; }
        public static string BuildCompactDiagnostics() { return string.Empty; }
        public static bool TryCopyText(string text, string callbackReceiver = null) { return false; }
        public static void CompleteClipboardAction(int actionId, bool copied) { }
        public static bool TryDownloadLog(bool csv) { return false; }
        public static void SetHudVisible(bool visible) { }
        public static void SetIsolationMode(PerformanceIsolationMode mode) { }
        public static void ToggleIsolationMode(PerformanceIsolationMode mode) { }
        public static bool IsIsolationActive(PerformanceIsolationMode mode) { return false; }
        public static string ConsumeSampleSummary() { return ""; }
        public static double TicksToMilliseconds(long ticks) { return 0d; }
        public static string SampleShortName(PerformanceProbeArea area) { return ""; }
#endif
    }

    /// <summary>
    /// Lightweight on-screen snapshot for a Development WebGL player. Unity's
    /// FrameTimingManager supplies main-thread/GPU timings where supported;
    /// unsupported browser/GPU counters are reported as unavailable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimePerformanceOverlay : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private GameBootstrap game;
        private ProfilerRecorder mainThreadRecorder;
        private ProfilerRecorder gcAllocatedRecorder;
        private ProfilerRecorder animatorRecorder;
        private readonly FrameTiming[] frameTimings = new FrameTiming[8];
        private string report = "Performance capture warming up…";
        private string sampleSummary = "waiting for samples";
        private float reportTimer;
        private float frameTimeSum;
        private float frameTimeMax;
        private int frameCount;
        private float fps;
        private float averageFrameMs;
        private float worstFrameMs;
        private float cpuMainThreadMs = -1f;
        private float profilerMainThreadMs = -1f;
        private float gpuFrameMs = -1f;
        private long gcAllocatedBytes = -1L;
        private long gcAllocatedPeakBytes = -1L;
        private long gcAllocatedWindowPeakBytes = -1L;
        private long gcAllocatedWindowBytes;
        private int gc0Delta;
        private int gc1Delta;
        private int gc2Delta;
        private int previousGc0;
        private int previousGc1;
        private int previousGc2;
        private long animatorUpdatesNs = -1L;
        private long animatorWindowPeakNs = -1L;
        private long animatorWindowTotalNs;
        private double mainThreadWindowTotalMs;
        private int mainThreadWindowSamples;
        private int activeRatCount;
        private int pinkieCount;
        private int animatorCount;
        private int rendererCount;
        private int uiGraphicCount;
        private int canvasCount;
        private long previousSimulationStepTotal;
        private long previousBehaviorUpdateTotal;
        private long previousRepeatedBehaviorUpdateTotal;
        private bool frameTimingWarningLogged;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private bool browserTelemetryRunning;
        private Canvas[] uiCanvasesForIsolation;
        private bool[] uiCanvasEnabledBeforeIsolation;
        private bool uiCanvasIsolationApplied;

        public void Configure(GameBootstrap bootstrap)
        {
            game = bootstrap;
        }

        private void Awake()
        {
            previousSimulationStepTotal = GrowthSystem.TotalSimulationSteps;
            previousBehaviorUpdateTotal = GrowthSystem.TotalBehaviorUpdateCount;
            previousRepeatedBehaviorUpdateTotal = GrowthSystem.TotalRepeatedBehaviorUpdateCount;
            previousGc0 = GC.CollectionCount(0);
            previousGc1 = GC.CollectionCount(1);
            previousGc2 = GC.CollectionCount(2);
            TryStartRecorder(ref mainThreadRecorder, ProfilerCategory.Internal, "Main Thread");
            TryStartRecorder(ref gcAllocatedRecorder, ProfilerCategory.Memory, "GC Allocated In Frame");
            TryStartRecorder(ref animatorRecorder, ProfilerCategory.Animation, "Animator.Update");
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                if (RuntimePerformanceDiagnostics.CaptureEnabled)
                {
                    RuntimePerformanceDiagnostics.StartBrowserTelemetry(gameObject.name, "OnBrowserPerformanceTelemetry");
                    browserTelemetryRunning = true;
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("[Performance Diagnostics] Browser frame telemetry unavailable: " + exception.Message);
            }
#endif
        }

        private void OnDestroy()
        {
            if (uiCanvasIsolationApplied)
            {
                RuntimePerformanceDiagnostics.SetIsolationMode(
                    RuntimePerformanceDiagnostics.IsolationMode & ~PerformanceIsolationMode.UiRendering);
                RestoreUiCanvasIsolation();
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserTelemetryRunning)
            {
                try { RuntimePerformanceDiagnostics.StopBrowserTelemetry(); }
                catch (Exception) { }
                browserTelemetryRunning = false;
            }
#endif
            if (mainThreadRecorder.Valid) mainThreadRecorder.Dispose();
            if (gcAllocatedRecorder.Valid) gcAllocatedRecorder.Dispose();
            if (animatorRecorder.Valid) animatorRecorder.Dispose();
        }

        public void OnBrowserPerformanceTelemetry(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            string[] values = payload.Split(',');
            if (values.Length < 11) return;
            float averageGap;
            float worstGap;
            int frames;
            int longTasks;
            float longTaskTotal;
            float longTaskMax;
            if (!float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out averageGap) ||
                !float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out worstGap) ||
                !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out frames) ||
                !int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out longTasks) ||
                !float.TryParse(values[4], NumberStyles.Float, CultureInfo.InvariantCulture, out longTaskTotal) ||
                !float.TryParse(values[5], NumberStyles.Float, CultureInfo.InvariantCulture, out longTaskMax)) return;
            RuntimePerformanceDiagnostics.RecordBrowserTelemetry(averageGap, worstGap, frames,
                longTasks, longTaskTotal, longTaskMax, values[6] == "1", values[7] == "1",
                values[9] == "1", values[10] == "1", values[8] == "1");
        }

        private static void TryStartRecorder(ref ProfilerRecorder recorder, ProfilerCategory category, string counterName)
        {
            try
            {
                recorder = ProfilerRecorder.StartNew(category, counterName, 32,
                    ProfilerRecorderOptions.SumAllSamplesInFrame);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("[Performance Diagnostics] Counter unavailable: " + counterName + " — " + exception.Message);
            }
        }

        private void Update()
        {
            ApplyUiCanvasIsolation();
#if UNITY_WEBGL && !UNITY_EDITOR
            if (RuntimePerformanceDiagnostics.CaptureEnabled && !browserTelemetryRunning)
            {
                try
                {
                    RuntimePerformanceDiagnostics.StartBrowserTelemetry(gameObject.name, "OnBrowserPerformanceTelemetry");
                    browserTelemetryRunning = true;
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogWarning("[Performance Diagnostics] Browser frame telemetry unavailable: " + exception.Message);
                }
            }
            else if (!RuntimePerformanceDiagnostics.CaptureEnabled && browserTelemetryRunning)
            {
                try { RuntimePerformanceDiagnostics.StopBrowserTelemetry(); }
                catch (Exception) { }
                browserTelemetryRunning = false;
            }
#endif
            try { FrameTimingManager.CaptureFrameTimings(); }
            catch (Exception exception)
            {
                if (!frameTimingWarningLogged)
                {
                    frameTimingWarningLogged = true;
                    UnityEngine.Debug.LogWarning("[Performance Diagnostics] Frame timing capture unavailable: " + exception.Message);
                }
            }
            float frameMs = Mathf.Max(0f, Time.unscaledDeltaTime * 1000f);
            frameCount++;
            frameTimeSum += frameMs;
            frameTimeMax = Mathf.Max(frameTimeMax, frameMs);
            ReadPerFrameProfilerCounters();
            if (gcAllocatedBytes > 0L) gcAllocatedWindowBytes += gcAllocatedBytes;
            RuntimePerformanceDiagnostics.ObserveFrame(frameMs,
                game == null ? 0L : game.GameTime,
                game == null ? 1 : Mathf.RoundToInt(game.SimulationSpeed),
                cpuMainThreadMs >= 0f ? cpuMainThreadMs : profilerMainThreadMs,
                gpuFrameMs,
                gcAllocatedBytes,
                gc0Delta, gc1Delta, gc2Delta,
                activeRatCount, pinkieCount, animatorCount, rendererCount,
                uiGraphicCount, canvasCount, GrowthSystem.LastSimulationStepCount,
                game == null ? 0f : game.LastMaintenanceDurationMs,
                game == null ? 0f : game.LastUiRefreshDurationMs,
                animatorUpdatesNs < 0L ? -1f : (float)(animatorUpdatesNs / 1000000d),
                game == null ? "None" : game.PerformancePanelName);
            reportTimer += Time.unscaledDeltaTime;
            if (reportTimer < GameConfig.PerformanceLogSampleIntervalSeconds) return;

            double sampleWindowSeconds = Math.Max(0.001d, reportTimer);
            fps = (float)(frameCount / sampleWindowSeconds);
            averageFrameMs = frameCount <= 0 ? 0f : frameTimeSum / frameCount;
            worstFrameMs = frameTimeMax;
            frameCount = 0;
            frameTimeSum = 0f;
            frameTimeMax = 0f;
            reportTimer = 0f;

            ReadFrameTimings();
            ReadProfilerCounters();
            if (RuntimePerformanceDiagnostics.CaptureEnabled || RuntimePerformanceDiagnostics.HudVisible)
                CountSceneObjects();
            if (RuntimePerformanceDiagnostics.CaptureEnabled)
            {
                long simulationStepTotal = GrowthSystem.TotalSimulationSteps;
                PerformanceLogSample sample = new PerformanceLogSample
                {
                    UtcTicks = DateTime.UtcNow.Ticks,
                    GameTimeMs = game == null ? 0L : game.GameTime,
                    Speed = game == null ? 1 : Mathf.RoundToInt(game.SimulationSpeed),
                    Fps = fps,
                    AverageFrameMs = averageFrameMs,
                    WorstFrameMs = worstFrameMs,
                    CpuMainThreadMs = cpuMainThreadMs < 0f && mainThreadWindowSamples > 0
                        ? (float)(mainThreadWindowTotalMs / mainThreadWindowSamples)
                        : (cpuMainThreadMs >= 0f ? cpuMainThreadMs : profilerMainThreadMs),
                    GpuMs = gpuFrameMs,
                    BrowserAverageFrameGapMs = -1f,
                    BrowserWorstFrameGapMs = -1f,
                    BrowserGapOutsideUnityCpuMs = -1f,
                    BrowserLongTaskTotalMs = -1f,
                    BrowserLongTaskMaxMs = -1f,
                    BrowserTelemetryAvailable = false,
                    GcAllocatedBytes = gcAllocatedRecorder.Valid ? gcAllocatedWindowBytes : -1L,
                    Gc0Collections = gc0Delta,
                    Gc1Collections = gc1Delta,
                    Gc2Collections = gc2Delta,
                    ActiveRatCount = activeRatCount,
                    PinkieCount = pinkieCount,
                    AnimatorCount = animatorCount,
                    RendererCount = rendererCount,
                    UiGraphicCount = uiGraphicCount,
                    CanvasCount = canvasCount,
                    SimulationSteps = (int)Math.Min(int.MaxValue, Math.Max(0L, simulationStepTotal - previousSimulationStepTotal)),
                    SimulationProbeCalls = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.ClockAndAge) +
                        RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.PairingMovement) +
                        RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.RatBehaviorUpdate) +
                        RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.SimulationMaintenance) +
                        RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.NursingUpdate),
                    RatBehaviorUpdates = (int)Math.Min(int.MaxValue, Math.Max(0L,
                        GrowthSystem.TotalBehaviorUpdateCount - previousBehaviorUpdateTotal)),
                    RepeatedRatBehaviorUpdates = (int)Math.Min(int.MaxValue, Math.Max(0L,
                        GrowthSystem.TotalRepeatedBehaviorUpdateCount - previousRepeatedBehaviorUpdateTotal)),
                    SimulationMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.ClockAndAge) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.PairingMovement) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatBehaviorUpdate) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.SimulationMaintenance) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.NursingUpdate),
                    SimulationMaintenanceMs = game == null ? 0f : game.ConsumePerformanceMaintenanceWindowMs(),
                    MaintenancePasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.SimulationMaintenance),
                    MaintenanceGrowthMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceGrowth),
                    MaintenanceGrowthPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceGrowth),
                    MaintenanceReproductiveMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceReproductive),
                    MaintenanceReproductivePasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceReproductive),
                    MaintenanceStoreMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceStore),
                    MaintenanceStorePasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceStore),
                    MaintenanceSessionsMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceSessions),
                    MaintenanceSessionsPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceSessions),
                    MaintenanceBirthsMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceBirths),
                    MaintenanceBirthsPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceBirths),
                    MaintenanceWeaningsMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceWeanings),
                    MaintenanceWeaningsPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceWeanings),
                    MaintenanceActivitiesMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceActivities),
                    MaintenanceActivitiesPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceActivities),
                    MaintenanceEnclosuresMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceEnclosures),
                    MaintenanceEnclosuresPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceEnclosures),
                    MaintenanceSaleEligibilityMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceSaleEligibility),
                    MaintenanceSaleEligibilityPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceSaleEligibility),
                    MaintenanceDeadlineResolutionMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceDeadlineResolution),
                    MaintenanceDeadlineResolutionPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceDeadlineResolution),
                    MaintenancePairingChecksMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenancePairingChecks),
                    MaintenancePairingChecksPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenancePairingChecks),
                    MaintenanceHistoricalLittersMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceHistoricalLitters),
                    MaintenanceHistoricalLitterScans = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceHistoricalLitters),
                    MaintenanceHistoricalLitterRecords = RuntimePerformanceDiagnostics.WindowWorkCount(PerformanceProbeArea.MaintenanceHistoricalLitters),
                    MaintenanceHistoricalPregnanciesMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceHistoricalPregnancies),
                    MaintenanceHistoricalPregnancyScans = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceHistoricalPregnancies),
                    MaintenanceHistoricalPregnancyRecords = RuntimePerformanceDiagnostics.WindowWorkCount(PerformanceProbeArea.MaintenanceHistoricalPregnancies),
                    MaintenanceHistoricalRatIndexMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceHistoricalRatIndex),
                    MaintenanceHistoricalRatIndexScans = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceHistoricalRatIndex),
                    MaintenanceHistoricalRatIndexRecords = RuntimePerformanceDiagnostics.WindowWorkCount(PerformanceProbeArea.MaintenanceHistoricalRatIndex),
                    MaintenanceBirthRepairRecoveryMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.MaintenanceBirthRepairRecovery),
                    MaintenanceBirthRepairRecoveryPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.MaintenanceBirthRepairRecovery),
                    MaintenanceBirthRepairRecordsScanned = RuntimePerformanceDiagnostics.WindowWorkCount(PerformanceProbeArea.MaintenanceBirthRepairRecovery),
                    MaintenanceBirthRepairTransactionsReopened = RuntimePerformanceDiagnostics.WindowChangedCount(PerformanceProbeArea.MaintenanceBirthRepairRecovery),
                    NursingMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.NursingUpdate),
                    NursingPasses = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.NursingUpdate),
                    UiRefreshMs = game == null ? 0f : game.ConsumePerformanceUiRefreshWindowMs(),
                RatAiMovementMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatBehaviorUpdate) +
                    RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatBehaviorLateUpdate),
                RatTargetSelectionMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatDestinationSelection),
                WorldMovementIntegrationMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.WorldMovementIntegration),
                RatSpacingMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatSpacing),
                    AnimationMs = !animatorRecorder.Valid ? -1f : (float)(animatorWindowTotalNs / 1000000d),
                    GroundingBoundsMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.GroundingAndBounds),
                    RatPresentationMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatPresenterLateUpdate) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatPresentationBuild) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatMaterialSetup) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.PinkieUpdate),
                    RatPresentationUpdates = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.RatPresenterLateUpdate) +
                        RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.RatPresentationBuild) +
                        RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.RatMaterialSetup) +
                        RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.PinkieUpdate),
                    SaveExportMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.SaveExport),
                    SaveExportOperations = RuntimePerformanceDiagnostics.WindowCallCount(PerformanceProbeArea.SaveExport),
                    InputInteractionsMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.InteractionUpdate),
                    CurrentPanel = game == null ? "None" : game.PerformancePanelName,
                };
                RuntimePerformanceDiagnostics.RecordSample(sample);
                previousSimulationStepTotal = simulationStepTotal;
                previousBehaviorUpdateTotal = GrowthSystem.TotalBehaviorUpdateCount;
                previousRepeatedBehaviorUpdateTotal = GrowthSystem.TotalRepeatedBehaviorUpdateCount;
            }
            else
            {
                previousSimulationStepTotal = GrowthSystem.TotalSimulationSteps;
                if (game != null)
                {
                    game.ConsumePerformanceMaintenanceWindowMs();
                    game.ConsumePerformanceUiRefreshWindowMs();
                }
            }
            sampleSummary = RuntimePerformanceDiagnostics.ConsumeSampleSummary(RuntimePerformanceDiagnostics.HudVisible);
            if (RuntimePerformanceDiagnostics.HudVisible) RebuildReport();
            else
            {
                mainThreadWindowTotalMs = 0d;
                mainThreadWindowSamples = 0;
                gcAllocatedWindowPeakBytes = -1L;
                animatorWindowPeakNs = -1L;
            }
            gcAllocatedWindowBytes = 0L;
            animatorWindowTotalNs = 0L;
        }

        private void ReadFrameTimings()
        {
            cpuMainThreadMs = -1f;
            gpuFrameMs = -1f;
            try
            {
                uint count = FrameTimingManager.GetLatestTimings((uint)frameTimings.Length, frameTimings);
                if (count == 0) return;
                double cpu = 0d;
                double gpu = 0d;
                int cpuCount = 0;
                int gpuCount = 0;
                for (int i = 0; i < count; i++)
                {
                    if (frameTimings[i].cpuMainThreadFrameTime > 0d)
                    {
                        cpu += frameTimings[i].cpuMainThreadFrameTime;
                        cpuCount++;
                    }
                    if (frameTimings[i].gpuFrameTime > 0d)
                    {
                        gpu += frameTimings[i].gpuFrameTime;
                        gpuCount++;
                    }
                }
                if (cpuCount > 0) cpuMainThreadMs = (float)(cpu / cpuCount);
                if (gpuCount > 0) gpuFrameMs = (float)(gpu / gpuCount);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("[Performance Diagnostics] Frame timing is unsupported: " + exception.Message);
            }
        }

        private void ReadProfilerCounters()
        {
            gc0Delta = ReadCollectionDelta(0, ref previousGc0);
            gc1Delta = ReadCollectionDelta(1, ref previousGc1);
            gc2Delta = ReadCollectionDelta(2, ref previousGc2);
        }

        private void ReadPerFrameProfilerCounters()
        {
            if (mainThreadRecorder.Valid)
            {
                profilerMainThreadMs = (float)(mainThreadRecorder.LastValue / 1000000d);
                if (profilerMainThreadMs > 0f)
                {
                    mainThreadWindowTotalMs += profilerMainThreadMs;
                    mainThreadWindowSamples++;
                }
            }
            gcAllocatedBytes = gcAllocatedRecorder.Valid ? gcAllocatedRecorder.LastValue : -1L;
            if (gcAllocatedBytes >= 0L)
            {
                gcAllocatedPeakBytes = Math.Max(gcAllocatedPeakBytes, gcAllocatedBytes);
                gcAllocatedWindowPeakBytes = Math.Max(gcAllocatedWindowPeakBytes, gcAllocatedBytes);
            }
            animatorUpdatesNs = animatorRecorder.Valid ? animatorRecorder.LastValue : -1L;
            if (animatorUpdatesNs >= 0L)
            {
                animatorWindowPeakNs = Math.Max(animatorWindowPeakNs, animatorUpdatesNs);
                animatorWindowTotalNs += animatorUpdatesNs;
            }
        }

        private static int ReadCollectionDelta(int generation, ref int previous)
        {
            int current = GC.CollectionCount(generation);
            int delta = Math.Max(0, current - previous);
            previous = current;
            return delta;
        }

        private void CountSceneObjects()
        {
            activeRatCount = 0;
            pinkieCount = 0;
            if (game != null && game.Save != null && game.Save.rats != null)
            {
                for (int i = 0; i < game.Save.rats.Count; i++)
                {
                    RatData rat = game.Save.rats[i];
                    if (rat == null || rat.removalDisposition != RatRemovalDisposition.None) continue;
                    activeRatCount++;
                    if (rat.stage == RatStage.Pinkie) pinkieCount++;
                }
            }
            // Diagnostics report objects participating in the active scene,
            // not inactive portrait/preview prefabs retained by UI builders.
            // This is sampled once per second and avoids inflating counts with
            // hidden stage visuals that cannot contribute to the current frame.
            animatorCount = UnityEngine.Object.FindObjectsOfType<Animator>().Length;
            rendererCount = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            uiGraphicCount = UnityEngine.Object.FindObjectsOfType<Graphic>().Length;
            canvasCount = UnityEngine.Object.FindObjectsOfType<Canvas>().Length;
        }

        private void RebuildReport()
        {
            if (cpuMainThreadMs <= 0f && mainThreadWindowSamples > 0)
                cpuMainThreadMs = (float)(mainThreadWindowTotalMs / mainThreadWindowSamples);
            string cpu = cpuMainThreadMs <= 0f ? "n/a" : cpuMainThreadMs.ToString("0.00") + "ms";
            string gpu = gpuFrameMs < 0f ? "n/a" : gpuFrameMs.ToString("0.00") + "ms";
            string allocated = gcAllocatedBytes < 0L ? "n/a" : gcAllocatedBytes.ToString("N0") + " B/frame";
            string allocatedPeak = gcAllocatedWindowPeakBytes < 0L ? "n/a" : gcAllocatedWindowPeakBytes.ToString("N0") + " B";
            string animator = animatorUpdatesNs < 0L ? "n/a" : (animatorUpdatesNs / 1000000d).ToString("0.00") + "ms (peak " +
                (Math.Max(animatorUpdatesNs, animatorWindowPeakNs) / 1000000d).ToString("0.00") + "ms)";
            report = "PERF • " + RuntimePerformanceDiagnostics.IsolationMode +
                " • " + (RuntimePerformanceDiagnostics.CaptureEnabled ? "CAPTURE ON" : "CAPTURE OFF") +
                "\nFPS " + fps.ToString("0.0") + " • frame avg/max " + averageFrameMs.ToString("0.00") + "/" + worstFrameMs.ToString("0.00") + " ms" +
                "\nCPU main " + cpu + " • GPU " + gpu + " • Animator " + animator +
                "\nGC alloc " + allocated + " (window peak " + allocatedPeak + "; lifetime peak " + (gcAllocatedPeakBytes < 0L ? "n/a" : gcAllocatedPeakBytes.ToString("N0") + " B") + ") • GC collections Δ " + gc0Delta + "/" + gc1Delta + "/" + gc2Delta +
                "\nRats " + activeRatCount + " (pinkies " + pinkieCount + ") • animators " + animatorCount + " • renderers " + rendererCount +
                "\nUI graphics " + uiGraphicCount + " • canvases " + canvasCount +
                "\nSim: " + (game == null ? "n/a" : game.PerformanceSimulationSummary) +
                "\nCPU samples (ms/s; n=count; max=single call): " + sampleSummary;
            mainThreadWindowTotalMs = 0d;
            mainThreadWindowSamples = 0;
            gcAllocatedWindowPeakBytes = -1L;
            animatorWindowPeakNs = -1L;
        }

        private void OnGUI()
        {
            bool uiRenderingIsolated = RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.UiRendering);
            if (!RuntimePerformanceDiagnostics.HudVisible && !uiRenderingIsolated) return;
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 8, 8) };
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    fontSize = Screen.width < 700 ? 10 : 12,
                    normal = { textColor = new Color(0.9f, 0.97f, 0.92f) }
                };
            }
            if (uiRenderingIsolated)
            {
                float restoreWidth = Mathf.Min(Screen.width - 24f, 340f);
                Rect restoreRect = new Rect((Screen.width - restoreWidth) * 0.5f,
                    Mathf.Max(8f, Screen.height * 0.08f), restoreWidth, 54f);
                GUI.depth = -100;
                GUI.Box(restoreRect, "UI rendering isolated for A/B test", boxStyle);
                if (GUI.Button(new Rect(restoreRect.x + 8f, restoreRect.y + 27f,
                    restoreRect.width - 16f, 22f), "Restore UI"))
                {
                    RuntimePerformanceDiagnostics.ToggleIsolationMode(PerformanceIsolationMode.UiRendering);
                    ApplyUiCanvasIsolation();
                }
                return;
            }
            if (!RuntimePerformanceDiagnostics.HudVisible) return;
            float width = Mathf.Min(Screen.width - 16f, Screen.width < 700 ? 520f : 600f);
            float height = Screen.width < 700 ? 184f : 166f;
            float x = Mathf.Max(8f, Screen.width - width - 8f);
            float y = Mathf.Max(8f, Screen.height - height - 8f);
            GUI.depth = -100;
            GUI.Box(new Rect(x, y, width, height), GUIContent.none, boxStyle);
            GUI.Label(new Rect(x + 8f, y + 6f, width - 16f, height - 12f), report, labelStyle);
        }

        private void ApplyUiCanvasIsolation()
        {
            bool shouldIsolate = RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.UiRendering);
            if (shouldIsolate == uiCanvasIsolationApplied) return;
            if (shouldIsolate)
            {
                uiCanvasesForIsolation = UnityEngine.Object.FindObjectsOfType<Canvas>();
                uiCanvasEnabledBeforeIsolation = new bool[uiCanvasesForIsolation.Length];
                for (int index = 0; index < uiCanvasesForIsolation.Length; index++)
                {
                    Canvas canvas = uiCanvasesForIsolation[index];
                    if (canvas == null) continue;
                    uiCanvasEnabledBeforeIsolation[index] = canvas.enabled;
                    canvas.enabled = false;
                }
                uiCanvasIsolationApplied = true;
                return;
            }

            RestoreUiCanvasIsolation();
        }

        private void RestoreUiCanvasIsolation()
        {
            if (uiCanvasesForIsolation != null)
            {
                for (int index = 0; index < uiCanvasesForIsolation.Length; index++)
                {
                    Canvas canvas = uiCanvasesForIsolation[index];
                    if (canvas != null && uiCanvasEnabledBeforeIsolation != null &&
                        index < uiCanvasEnabledBeforeIsolation.Length)
                        canvas.enabled = uiCanvasEnabledBeforeIsolation[index];
                }
            }
            uiCanvasesForIsolation = null;
            uiCanvasEnabledBeforeIsolation = null;
            uiCanvasIsolationApplied = false;
        }

#endif
    }
}
