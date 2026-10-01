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
    }

    public enum PerformanceProbeArea
    {
        BootstrapUpdate,
        ClockAndAge,
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
        public float SimulationMaintenanceMs;
        public float UiRefreshMs;
        public float RatAiMovementMs;
        public float AnimationMs;
        public float GroundingBoundsMs;
        public float RatPresentationMs;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly string[] SampleNames =
        {
            "Rat Empire/Measured/Bootstrap Update",
            "Rat Empire/Measured/Clock and Age",
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
        };
        private static readonly long[] WindowTicks = new long[(int)PerformanceProbeArea.Count];
        private static readonly long[] MaxTicks = new long[(int)PerformanceProbeArea.Count];
        private static readonly int[] Calls = new int[(int)PerformanceProbeArea.Count];
        private static readonly StringBuilder SummaryBuilder = new StringBuilder(512);
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

        public static bool CaptureEnabled { get { return captureEnabled; } }
        public static bool HudVisible { get { return hudVisible; } }
        public static PerformanceIsolationMode IsolationMode { get { return isolationMode; } }
        public static int SampleCount { get { return sampleCount; } }
        public static int SpikeCount { get { return spikeCount; } }
        public static float SessionWorstFrameMs { get { return sessionWorstFrameMs; } }
        public static float SessionWorstSubsystemMs { get { return sessionWorstSubsystemMs; } }
        public static string SessionWorstSubsystem { get { return sessionWorstSubsystem; } }

        public static long Begin(PerformanceProbeArea area)
        {
            if (!captureEnabled) return 0L;
            Profiler.BeginSample(SampleNames[(int)area]);
            long start = Stopwatch.GetTimestamp();
            return start == 0L ? 1L : start;
        }

        public static void End(PerformanceProbeArea area, long startedAt)
        {
            if (startedAt == 0L) return;
            long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - startedAt);
            Profiler.EndSample();
            int index = (int)area;
            WindowTicks[index] += elapsed;
            if (elapsed > MaxTicks[index]) MaxTicks[index] = elapsed;
            Calls[index]++;
        }

        public static float WindowMilliseconds(PerformanceProbeArea area)
        {
            return (float)TicksToMilliseconds(WindowTicks[(int)area]);
        }

        public static void RecordSample(PerformanceLogSample sample)
        {
            if (!captureEnabled || SampleRing.Length == 0) return;
            sample.UnattributedFrameGapMs = sample.GpuMs < 0f && sample.CpuMainThreadMs >= 0f
                ? Mathf.Max(0f, sample.AverageFrameMs - sample.CpuMainThreadMs)
                : -1f;
            sampleWriteIndex = (sampleWriteIndex + 1) % SampleRing.Length;
            SampleRing[sampleWriteIndex] = sample;
            if (sampleCount < SampleRing.Length) sampleCount++;
            TrackWorstSubsystem("Simulation/maintenance", sample.SimulationMaintenanceMs);
            TrackWorstSubsystem("UI refresh", sample.UiRefreshMs);
            TrackWorstSubsystem("Rat AI/movement", sample.RatAiMovementMs);
            TrackWorstSubsystem("Animation", sample.AnimationMs);
            TrackWorstSubsystem("Grounding/bounds", sample.GroundingBoundsMs);
            TrackWorstSubsystem("Rat presentation/rendering", sample.RatPresentationMs);
            TrackWorstSubsystem("Input/interactions", sample.InputInteractionsMs);
        }

        /// <summary>Called once per rendered frame; creates a record only for a new lag episode or severity escalation.</summary>
        public static void ObserveFrame(float frameMs, long gameTimeMs, int speed, float cpuMs, float gpuMs,
            long gcAllocatedBytes, int gc0, int gc1, int gc2, int activeRats, int pinkies,
            int animators, int renderers, int uiGraphics, int canvases, int simulationSteps,
            float maintenanceMs, float uiRefreshMs, float animationMs, string panelName)
        {
            if (!captureEnabled) return;
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
                IsUnattributedFrameGap = gpuMs < 0f && cpuMs >= 0f &&
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
            return new PerformanceLogSample
            {
                UtcTicks = DateTime.UtcNow.Ticks,
                GameTimeMs = gameTimeMs,
                Speed = speed,
                WorstFrameMs = frameMs,
                CpuMainThreadMs = cpuMs,
                GpuMs = gpuMs,
                UnattributedFrameGapMs = gpuMs < 0f && cpuMs >= 0f
                    ? Mathf.Max(0f, frameMs - cpuMs)
                    : -1f,
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
                SimulationMaintenanceMs = maintenanceMs,
                UiRefreshMs = uiRefreshMs,
                RatAiMovementMs = WindowMilliseconds(PerformanceProbeArea.RatBehaviorUpdate) +
                    WindowMilliseconds(PerformanceProbeArea.RatBehaviorLateUpdate) +
                    WindowMilliseconds(PerformanceProbeArea.RatDestinationSelection),
                AnimationMs = animationMs,
                GroundingBoundsMs = WindowMilliseconds(PerformanceProbeArea.GroundingAndBounds),
                RatPresentationMs = WindowMilliseconds(PerformanceProbeArea.RatPresenterLateUpdate) +
                    WindowMilliseconds(PerformanceProbeArea.RatPresentationBuild) +
                    WindowMilliseconds(PerformanceProbeArea.RatMaterialSetup),
                InputInteractionsMs = WindowMilliseconds(PerformanceProbeArea.InteractionUpdate),
                CurrentPanel = panelName,
            };
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
            ClearWindow();
        }

        public static string BuildLiveSummary()
        {
            if (sampleCount == 0) return captureEnabled ? "Capture running • waiting for the first 1-second sample." : "Capture stopped.";
            PerformanceLogSample latest = SampleAt(sampleCount - 1);
            ExportBuilder.Length = 0;
            ExportBuilder.Append(captureEnabled ? "CAPTURE ON" : "CAPTURE OFF")
                .Append(" • ").Append(latest.CurrentPanel ?? "None")
                .Append(" • Day ").Append(GameDayLabel(latest.GameTimeMs))
                .Append(" • ").Append(latest.Speed).Append('x')
                .Append(" • ").Append(latest.Fps.ToString("0.0", CultureInfo.InvariantCulture)).Append(" FPS / ")
                .Append(latest.AverageFrameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms avg / ")
                .Append(latest.WorstFrameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms worst")
                .Append("\nRats ").Append(latest.ActiveRatCount).Append(" (pinkies ").Append(latest.PinkieCount)
                .Append(") • animators ").Append(latest.AnimatorCount).Append(" • renderers ").Append(latest.RendererCount)
                .Append(" • UI ").Append(latest.UiGraphicCount).Append(" graphics/").Append(latest.CanvasCount).Append(" canvases")
                .Append("\nCPU ").Append(FormatMetric(latest.CpuMainThreadMs)).Append(" • GPU ").Append(FormatMetric(latest.GpuMs))
                .Append(" • unattributed frame remainder ").Append(FormatMetric(latest.UnattributedFrameGapMs))
                .Append(" • GC ").Append(latest.GcAllocatedBytes).Append(" B in sample; collections ")
                .Append(latest.Gc0Collections).Append('/').Append(latest.Gc1Collections).Append('/').Append(latest.Gc2Collections)
                .Append(" • steps ").Append(latest.SimulationSteps)
                .Append("\nWorst session frame ").Append(sessionWorstFrameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(" @ ").Append(new DateTime(sessionWorstFrame.UtcTicks, DateTimeKind.Utc).ToString("HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Append(" • ").Append(sessionWorstFrame.CurrentPanel ?? "None")
                .Append(" • rats/pinkies ").Append(sessionWorstFrame.ActiveRatCount).Append('/').Append(sessionWorstFrame.PinkieCount)
                .Append(" • worst subsystem ").Append(sessionWorstSubsystem).Append(" ")
                .Append(sessionWorstSubsystemMs.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ms/window");
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
                ExportBuilder.AppendLine("record,severity,utc,game_day_time,speed,fps,avg_frame_ms,worst_frame_ms,cpu_main_ms,gpu_ms,unattributed_frame_gap_ms,gc_alloc_bytes,gc0,gc1,gc2,rats,pinkies,animators,renderers,ui_graphics,canvases,simulation_steps,maintenance_ms,ui_refresh_ms,rat_ai_movement_ms,animation_ms,grounding_bounds_ms,presentation_ms,input_ms,panel");
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

        public static bool TryCopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return RatPerformanceCopyText(text) != 0; }
            catch (Exception) { return false; }
#elif UNITY_EDITOR
            GUIUtility.systemCopyBuffer = text;
            return true;
#else
            return false;
#endif
        }

        public static bool TryDownloadLog(bool csv)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                string content = BuildExportText(csv);
                RatPerformanceDownloadText(csv ? "rat-empire-performance.csv" : "rat-empire-performance.txt",
                    content, csv ? "text/csv;charset=utf-8" : "text/plain;charset=utf-8");
                return true;
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int RatPerformanceCopyText(string text);
        [DllImport("__Internal")] private static extern void RatPerformanceDownloadText(string fileName, string content, string mimeType);
#endif

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

        private static string GameDayLabel(long gameTimeMs)
        {
            long day = Math.Max(1L, gameTimeMs / GameConfig.GameDayMs + 1L);
            long minuteOfDay = (gameTimeMs % GameConfig.GameDayMs) / 60000L;
            return day.ToString(CultureInfo.InvariantCulture) + " " +
                (minuteOfDay / 60L).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (minuteOfDay % 60L).ToString("00", CultureInfo.InvariantCulture);
        }

        private static StringBuilder AppendCompactSample(StringBuilder builder, PerformanceLogSample sample)
        {
            builder.Append(new DateTime(sample.UtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
                .Append(" • Day ").Append(GameDayLabel(sample.GameTimeMs)).Append(" • ").Append(sample.Speed).Append('x')
                .Append(" • FPS ").Append(sample.Fps.ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" • frame ").Append(sample.AverageFrameMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("/")
                .Append(sample.WorstFrameMs.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(" • CPU/GPU ").Append(FormatMetric(sample.CpuMainThreadMs)).Append('/').Append(FormatMetric(sample.GpuMs))
                .Append(" • frame remainder ").Append(FormatMetric(sample.UnattributedFrameGapMs))
                .Append(" • GC ").Append(sample.GcAllocatedBytes).Append(" B, ")
                .Append(sample.Gc0Collections).Append('/').Append(sample.Gc1Collections).Append('/').Append(sample.Gc2Collections)
                .Append(" • rats/pinkies ").Append(sample.ActiveRatCount).Append('/').Append(sample.PinkieCount)
                .Append(" • sim steps ").Append(sample.SimulationSteps)
                .Append(" • maint/UI ").Append(sample.SimulationMaintenanceMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.UiRefreshMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • AI/anim/ground ").Append(sample.RatAiMovementMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.AnimationMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.GroundingBoundsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • present/input ").Append(sample.RatPresentationMs.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(sample.InputInteractionsMs.ToString("0.00", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" • ").Append(sample.CurrentPanel ?? "None");
            return builder;
        }

        private static void AppendCsvRecord(StringBuilder builder, string type, string severity, PerformanceLogSample sample)
        {
            builder.Append(type).Append(',').Append(severity).Append(',')
                .Append(new DateTime(sample.UtcTicks, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture)).Append(',')
                .Append(GameDayLabel(sample.GameTimeMs)).Append(',').Append(sample.Speed).Append(',')
                .Append(sample.Fps.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.AverageFrameMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.WorstFrameMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.CpuMainThreadMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.GpuMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.UnattributedFrameGapMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.GcAllocatedBytes).Append(',').Append(sample.Gc0Collections).Append(',')
                .Append(sample.Gc1Collections).Append(',').Append(sample.Gc2Collections).Append(',')
                .Append(sample.ActiveRatCount).Append(',').Append(sample.PinkieCount).Append(',')
                .Append(sample.AnimatorCount).Append(',').Append(sample.RendererCount).Append(',')
                .Append(sample.UiGraphicCount).Append(',').Append(sample.CanvasCount).Append(',')
                .Append(sample.SimulationSteps).Append(',')
                .Append(sample.SimulationMaintenanceMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.UiRefreshMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.RatAiMovementMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.AnimationMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.GroundingBoundsMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.RatPresentationMs.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
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
                default: return "Unknown";
            }
        }

        private static void ClearWindow()
        {
            Array.Clear(WindowTicks, 0, WindowTicks.Length);
            Array.Clear(MaxTicks, 0, MaxTicks.Length);
            Array.Clear(Calls, 0, Calls.Length);
        }
#else
        public static bool CaptureEnabled { get { return false; } }
        public static bool HudVisible { get { return false; } }
        public static PerformanceIsolationMode IsolationMode { get { return PerformanceIsolationMode.Normal; } }
        public static long Begin(PerformanceProbeArea area) { return 0L; }
        public static void End(PerformanceProbeArea area, long startedAt) { }
        public static void SetCaptureEnabled(bool enabled) { }
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
        private bool frameTimingWarningLogged;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;

        public void Configure(GameBootstrap bootstrap)
        {
            game = bootstrap;
        }

        private void Awake()
        {
            previousSimulationStepTotal = GrowthSystem.TotalSimulationSteps;
            previousGc0 = GC.CollectionCount(0);
            previousGc1 = GC.CollectionCount(1);
            previousGc2 = GC.CollectionCount(2);
            TryStartRecorder(ref mainThreadRecorder, ProfilerCategory.Internal, "Main Thread");
            TryStartRecorder(ref gcAllocatedRecorder, ProfilerCategory.Memory, "GC Allocated In Frame");
            TryStartRecorder(ref animatorRecorder, ProfilerCategory.Animation, "Animator.Update");
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
                    SimulationMaintenanceMs = game == null ? 0f : game.ConsumePerformanceMaintenanceWindowMs(),
                    UiRefreshMs = game == null ? 0f : game.ConsumePerformanceUiRefreshWindowMs(),
                    RatAiMovementMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatBehaviorUpdate) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatBehaviorLateUpdate) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatDestinationSelection),
                    AnimationMs = !animatorRecorder.Valid ? -1f : (float)(animatorWindowTotalNs / 1000000d),
                    GroundingBoundsMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.GroundingAndBounds),
                    RatPresentationMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatPresenterLateUpdate) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatPresentationBuild) +
                        RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.RatMaterialSetup),
                    InputInteractionsMs = RuntimePerformanceDiagnostics.WindowMilliseconds(PerformanceProbeArea.InteractionUpdate),
                    CurrentPanel = game == null ? "None" : game.PerformancePanelName,
                };
                RuntimePerformanceDiagnostics.RecordSample(sample);
                previousSimulationStepTotal = simulationStepTotal;
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
                if (profilerMainThreadMs >= 0f)
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
            if (cpuMainThreadMs < 0f && mainThreadWindowSamples > 0)
                cpuMainThreadMs = (float)(mainThreadWindowTotalMs / mainThreadWindowSamples);
            string cpu = cpuMainThreadMs < 0f ? "n/a" : cpuMainThreadMs.ToString("0.00") + "ms";
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
            if (!RuntimePerformanceDiagnostics.HudVisible) return;
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
            float width = Mathf.Min(Screen.width - 16f, Screen.width < 700 ? 520f : 600f);
            float height = Screen.width < 700 ? 184f : 166f;
            float x = Mathf.Max(8f, Screen.width - width - 8f);
            float y = Mathf.Max(8f, Screen.height - height - 8f);
            GUI.depth = -100;
            GUI.Box(new Rect(x, y, width, height), GUIContent.none, boxStyle);
            GUI.Label(new Rect(x + 8f, y + 6f, width - 16f, height - 12f), report, labelStyle);
        }

        private void OnDestroy()
        {
            if (mainThreadRecorder.Valid) mainThreadRecorder.Dispose();
            if (gcAllocatedRecorder.Valid) gcAllocatedRecorder.Dispose();
            if (animatorRecorder.Valid) animatorRecorder.Dispose();
        }
#endif
    }
}
