using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Unity.Profiling;

namespace RatHabitat
{
    public enum PerformanceIsolationMode
    {
        Normal,
        RatBehavior,
        RatAnimation,
        RatRendering,
        RatShadows,
        AutomaticUiRefresh,
        ColonyMaintenance,
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
        private static bool captureEnabled = true;
        private static bool hudVisible;
        private static PerformanceIsolationMode isolationMode;

        public static bool CaptureEnabled { get { return captureEnabled; } }
        public static bool HudVisible { get { return hudVisible; } }
        public static PerformanceIsolationMode IsolationMode { get { return isolationMode; } }

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

        public static bool IsIsolationActive(PerformanceIsolationMode mode)
        {
            return isolationMode == mode;
        }

        public static string ConsumeSampleSummary()
        {
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
        private const float ReportIntervalSeconds = 1f;
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
        private int gc0Delta;
        private int gc1Delta;
        private int gc2Delta;
        private int previousGc0;
        private int previousGc1;
        private int previousGc2;
        private long animatorUpdatesNs = -1L;
        private long animatorWindowPeakNs = -1L;
        private double mainThreadWindowTotalMs;
        private int mainThreadWindowSamples;
        private int activeRatCount;
        private int pinkieCount;
        private int animatorCount;
        private int rendererCount;
        private int uiGraphicCount;
        private int canvasCount;
        private bool frameTimingWarningLogged;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;

        public void Configure(GameBootstrap bootstrap)
        {
            game = bootstrap;
        }

        private void Awake()
        {
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
            reportTimer += Time.unscaledDeltaTime;
            if (reportTimer < ReportIntervalSeconds) return;

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
            CountSceneObjects();
            sampleSummary = RuntimePerformanceDiagnostics.ConsumeSampleSummary();
            RebuildReport();
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
            if (animatorUpdatesNs >= 0L) animatorWindowPeakNs = Math.Max(animatorWindowPeakNs, animatorUpdatesNs);
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
            animatorCount = UnityEngine.Object.FindObjectsOfType<Animator>(true).Length;
            rendererCount = UnityEngine.Object.FindObjectsOfType<Renderer>(true).Length;
            uiGraphicCount = UnityEngine.Object.FindObjectsOfType<Graphic>(true).Length;
            canvasCount = UnityEngine.Object.FindObjectsOfType<Canvas>(true).Length;
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
