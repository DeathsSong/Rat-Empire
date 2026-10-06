#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RatHabitat.Tests
{
    public class VerticalSliceSystemTests
    {
        [Test]
        public void RuntimePerformanceLogStaysBoundedAndRecordsLagSeverityEscalation()
        {
            bool wasCapturing = RuntimePerformanceDiagnostics.CaptureEnabled;
            try
            {
                RuntimePerformanceDiagnostics.SetCaptureEnabled(true);
                RuntimePerformanceDiagnostics.ClearLog();
                int writes = GameConfig.PerformanceLogSampleCapacity + 7;
                for (int index = 0; index < writes; index++)
                {
                    RuntimePerformanceDiagnostics.RecordSample(new PerformanceLogSample
                    {
                        UtcTicks = DateTime.UtcNow.Ticks,
                        GameTimeMs = GameConfig.StartGameTimeMs + index * 1000L,
                        Speed = 2,
                        Fps = index,
                        AverageFrameMs = 1000f / Mathf.Max(1, index),
                        WorstFrameMs = 50f,
                        CpuMainThreadMs = 4f,
                        GpuMs = 6f,
                        GcAllocatedBytes = 2048,
                        ActiveRatCount = 18,
                        PinkieCount = 4,
                        AnimatorCount = 18,
                        RendererCount = 60,
                        UiGraphicCount = 200,
                        CanvasCount = 2,
                        SimulationSteps = 18,
                        SimulationMaintenanceMs = 1.5f,
                        UiRefreshMs = 0.8f,
                        RatAiMovementMs = 2.2f,
                        AnimationMs = 3.2f,
                        GroundingBoundsMs = 0.4f,
                        RatPresentationMs = 1.1f,
                        InputInteractionsMs = 0.1f,
                        CurrentPanel = "Habitat",
                    });
                }

                Assert.AreEqual(GameConfig.PerformanceLogSampleCapacity, RuntimePerformanceDiagnostics.SampleCount);
                StringAssert.Contains((writes - 1) + ".0", RuntimePerformanceDiagnostics.BuildRecentSamplesText(1));
                Assert.AreEqual(0, RuntimePerformanceDiagnostics.SpikeCount);

                RuntimePerformanceDiagnostics.ObserveFrame(110f, GameConfig.StartGameTimeMs, 1, 8f, 5f,
                    512, 1, 0, 0, 18, 4, 18, 60, 200, 2, 2, 1.5f, 0.8f, 3f, "My Rats");
                RuntimePerformanceDiagnostics.ObserveFrame(270f, GameConfig.StartGameTimeMs, 1, 8f, 5f,
                    1024, 1, 0, 0, 18, 4, 18, 60, 200, 2, 2, 1.5f, 1.3f, 4f, "My Rats");
                Assert.AreEqual(2, RuntimePerformanceDiagnostics.SpikeCount,
                    "A sustained slowdown creates one lag event and one critical-severity escalation rather than flooding the ring per frame.");
                StringAssert.Contains("CRITICAL", RuntimePerformanceDiagnostics.BuildRecentSpikesText(2));
                StringAssert.Contains("rats/pinkies 18/4", RuntimePerformanceDiagnostics.BuildRecentSpikesText(2));
                StringAssert.Contains("record,severity,utc", RuntimePerformanceDiagnostics.BuildExportText(true));
                StringAssert.Contains("spike,", RuntimePerformanceDiagnostics.BuildExportText(true));

                RuntimePerformanceDiagnostics.ObserveFrame(20f, GameConfig.StartGameTimeMs, 1, 8f, 5f,
                    0, 0, 0, 0, 18, 4, 18, 60, 200, 2, 2, 0f, 0f, 1f, "My Rats");
                RuntimePerformanceDiagnostics.ObserveFrame(110f, GameConfig.StartGameTimeMs, 3, 8f, 5f,
                    0, 0, 0, 0, 18, 4, 18, 60, 200, 2, 3, 0f, 0f, 1f, "Store");
                Assert.AreEqual(3, RuntimePerformanceDiagnostics.SpikeCount,
                    "A later lag episode is recorded again after frame time recovers below threshold.");
            }
            finally
            {
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(wasCapturing);
            }
        }

        [Test]
        public void RoutineSaveRequestsCoalesceWithoutWritingUntilTheQueueIsFlushed()
        {
            bool hadSaveKey = PlayerPrefs.HasKey("rat-habitat-save-v1");
            string previousSaveKey = PlayerPrefs.GetString("rat-habitat-save-v1", string.Empty);
            bool hadSaveFile = File.Exists(SaveSystem.SavePath);
            string previousSaveFile = hadSaveFile ? File.ReadAllText(SaveSystem.SavePath) : string.Empty;
            bool previousCapture = RuntimePerformanceDiagnostics.CaptureEnabled;
            ColonySaveData colony = ColonyFactory.CreateNew(GameConfig.NowMs());
            for (int index = 0; index < 18; index++)
            {
                RatData testRat = new RatData
                {
                    id = "save-queue-test-rat-" + index,
                    name = "Queue Test " + index,
                    sex = index % 2 == 0 ? RatSex.Female : RatSex.Male,
                    stage = index < 4 ? RatStage.Pinkie : RatStage.Adult,
                };
                colony.rats.Add(testRat);
                colony.ratIds.Add(testRat.id);
            }
            Assert.AreEqual(20, colony.rats.Count, "Exercise the routine-save queue with a colony-sized save.");
            Assert.AreEqual(4, colony.rats.FindAll(rat => rat.stage == RatStage.Pinkie).Count);

            try
            {
                SaveSystem.ClearPendingSaveQueueForTests();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(true);
                RuntimePerformanceDiagnostics.ClearLog();
                PlayerPrefs.SetString("rat-habitat-save-v1", "save-queue-test-sentinel");

                for (int index = 0; index < 11; index++)
                {
                    colony.colonyCredits += 1;
                    Assert.IsTrue(SaveSystem.QueueSave(colony, "routine-save-test"));
                }

                Assert.IsTrue(SaveSystem.HasPendingSave);
                Assert.AreEqual(11, SaveSystem.PendingSaveRequestCount);
                Assert.AreEqual(10, SaveSystem.PendingSaveCoalescedRequestCount);
                Assert.AreEqual("save-queue-test-sentinel", PlayerPrefs.GetString("rat-habitat-save-v1"),
                    "QueueSave must not synchronously touch the browser save key.");
                Assert.AreEqual(hadSaveFile, File.Exists(SaveSystem.SavePath));
                if (hadSaveFile)
                    Assert.AreEqual(previousSaveFile, File.ReadAllText(SaveSystem.SavePath),
                        "QueueSave must not synchronously write the desktop save file.");

                RuntimePerformanceDiagnostics.RecordSample(new PerformanceLogSample
                {
                    UtcTicks = DateTime.UtcNow.Ticks,
                    GameTimeMs = GameConfig.StartGameTimeMs,
                    Speed = 1,
                    Fps = 60,
                    AverageFrameMs = 16.7f,
                    WorstFrameMs = 20f,
                    CurrentPanel = "My Rats",
                });
                StringAssert.Contains("11/10", RuntimePerformanceDiagnostics.BuildRecentSamplesText(1),
                    "The log should report all routine requests and how many were coalesced.");
            }
            finally
            {
                SaveSystem.ClearPendingSaveQueueForTests();
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(previousCapture);
                if (hadSaveKey) PlayerPrefs.SetString("rat-habitat-save-v1", previousSaveKey);
                else PlayerPrefs.DeleteKey("rat-habitat-save-v1");
            }
        }

        [Test]
        public void ManualPerformanceLogChunksStayBelowLegacyTextMeshLimitAndCoverRawLog()
        {
            const int chunkSize = 850;
            string raw = new string('x', chunkSize * 13 + 271);
            var reconstructed = new System.Text.StringBuilder(raw.Length);
            int pageCount = (raw.Length + chunkSize - 1) / chunkSize;

            for (int page = 0; page < pageCount; page++)
            {
                string chunk = RuntimePerformanceDiagnostics.GetManualExportChunk(raw, page, chunkSize);
                Assert.LessOrEqual(chunk.Length, chunkSize,
                    "A selectable legacy Unity Text mesh must never receive the full export.");
                reconstructed.Append(chunk);
            }

            Assert.AreEqual(raw, reconstructed.ToString(),
                "Chunk navigation should expose every character from the full raw export.");
            Assert.AreEqual(RuntimePerformanceDiagnostics.GetManualExportChunk(raw, -10, chunkSize),
                RuntimePerformanceDiagnostics.GetManualExportChunk(raw, 0, chunkSize));
            Assert.AreEqual(RuntimePerformanceDiagnostics.GetManualExportChunk(raw, pageCount + 10, chunkSize),
                RuntimePerformanceDiagnostics.GetManualExportChunk(raw, pageCount - 1, chunkSize));
        }

        [Test]
        public void CsvExportHeaderAndSampleRowsHaveMatchingColumnCounts()
        {
            bool previousCapture = RuntimePerformanceDiagnostics.CaptureEnabled;
            try
            {
                RuntimePerformanceDiagnostics.SetCaptureEnabled(true);
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.RecordSample(new PerformanceLogSample
                {
                    UtcTicks = DateTime.UtcNow.Ticks,
                    GameTimeMs = GameConfig.StartGameTimeMs,
                    Speed = 1,
                    Fps = 60,
                    AverageFrameMs = 16.7f,
                    WorstFrameMs = 20f,
                    SaveSource = "source, with comma",
                    CurrentPanel = "panel, with comma",
                });

                string[] lines = RuntimePerformanceDiagnostics.BuildExportText(true)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Assert.GreaterOrEqual(lines.Length, 2);
                Assert.AreEqual(CountCsvColumns(lines[0]), CountCsvColumns(lines[1]),
                    "Save source and panel text must remain correctly quoted in CSV.");
            }
            finally
            {
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(previousCapture);
            }
        }

        private static int CountCsvColumns(string line)
        {
            int columns = 1;
            bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char value = line[index];
                if (value == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"') index++;
                    else quoted = !quoted;
                }
                else if (value == ',' && !quoted) columns++;
            }
            return columns;
        }

        [Test]
        public void PerformanceControlsOnlyChangeDiagnosticsAndNeverTouchColonyOrLocalSave()
        {
            const string browserSaveKey = "rat-habitat-save-v1";
            bool hadSaveKey = PlayerPrefs.HasKey(browserSaveKey);
            string previousSaveKeyValue = PlayerPrefs.GetString(browserSaveKey, string.Empty);
            string previousClipboard = GUIUtility.systemCopyBuffer;
            bool hadSaveFile = File.Exists(SaveSystem.SavePath);
            string previousSaveFile = hadSaveFile ? File.ReadAllText(SaveSystem.SavePath) : string.Empty;
            ColonySaveData colony = ColonyFactory.CreateNew(GameConfig.NowMs());
            string colonyJsonBefore = JsonUtility.ToJson(colony, true);
            bool previousCapture = RuntimePerformanceDiagnostics.CaptureEnabled;
            bool previousHud = RuntimePerformanceDiagnostics.HudVisible;
            PerformanceIsolationMode previousIsolation = RuntimePerformanceDiagnostics.IsolationMode;

            try
            {
                // Sentinel the stable browser-save key in the Editor prefs
                // namespace so any accidental reset/write is observable.
                PlayerPrefs.SetString(browserSaveKey, "performance-controls-must-not-touch-this");
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(false); // Stop
                Assert.IsFalse(RuntimePerformanceDiagnostics.CaptureEnabled);
                RuntimePerformanceDiagnostics.SetCaptureEnabled(true); // Start
                Assert.IsTrue(RuntimePerformanceDiagnostics.CaptureEnabled);

                RuntimePerformanceDiagnostics.RecordSample(new PerformanceLogSample
                {
                    UtcTicks = DateTime.UtcNow.Ticks,
                    GameTimeMs = GameConfig.StartGameTimeMs,
                    Speed = 1,
                    Fps = 60f,
                    AverageFrameMs = 16.7f,
                    WorstFrameMs = 23f,
                    CpuMainThreadMs = 5f,
                    GpuMs = -1f,
                    ActiveRatCount = 2,
                    CurrentPanel = "Developer Tools",
                });
                Assert.AreEqual(1, RuntimePerformanceDiagnostics.SampleCount);

                RuntimePerformanceDiagnostics.SetHudVisible(!previousHud);
                RuntimePerformanceDiagnostics.SetIsolationMode(PerformanceIsolationMode.RatRendering);
                SaveSystem.DiscardBrowserLifecycleElapsed();
                string copiedText = RuntimePerformanceDiagnostics.BuildExportText(false);
                bool copyReported = RuntimePerformanceDiagnostics.TryCopyText(copiedText);
                Assert.IsTrue(copyReported);
                Assert.AreEqual(copiedText, RuntimePerformanceDiagnostics.ManualExportText);
                StringAssert.Contains("Copied to clipboard", RuntimePerformanceDiagnostics.LastExportActionStatus);
                RuntimePerformanceDiagnostics.TryDownloadLog(true);
                Assert.IsNotEmpty(RuntimePerformanceDiagnostics.ManualExportText);
                StringAssert.Contains("manual copy text", RuntimePerformanceDiagnostics.LastExportActionStatus);

                // Clear is explicitly allowed to clear only diagnostics and
                // must re-arm capture for the next sample window.
                RuntimePerformanceDiagnostics.SetCaptureEnabled(false);
                RuntimePerformanceDiagnostics.ClearLog();
                Assert.IsTrue(RuntimePerformanceDiagnostics.CaptureEnabled);
                Assert.AreEqual(0, RuntimePerformanceDiagnostics.SampleCount);
                Assert.AreEqual(0, RuntimePerformanceDiagnostics.SpikeCount);
                StringAssert.Contains("Capture ON — waiting for the next sample.", RuntimePerformanceDiagnostics.BuildLiveSummary());
                StringAssert.Contains("0 samples • 0 lag episodes", RuntimePerformanceDiagnostics.BuildStatusText());

                Assert.AreEqual(colonyJsonBefore, JsonUtility.ToJson(colony, true),
                    "Performance controls must not modify any colony record or simulation state.");
                Assert.AreEqual("performance-controls-must-not-touch-this", PlayerPrefs.GetString(browserSaveKey),
                    "Performance controls must never write or delete the local save key.");
                Assert.AreEqual(hadSaveFile, File.Exists(SaveSystem.SavePath));
                if (hadSaveFile)
                    Assert.AreEqual(previousSaveFile, File.ReadAllText(SaveSystem.SavePath),
                        "Performance controls must leave the existing persistent save file byte-for-byte unchanged.");
            }
            finally
            {
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(previousCapture);
                RuntimePerformanceDiagnostics.SetHudVisible(previousHud);
                RuntimePerformanceDiagnostics.SetIsolationMode(previousIsolation);
                GUIUtility.systemCopyBuffer = previousClipboard;
                if (hadSaveKey) PlayerPrefs.SetString(browserSaveKey, previousSaveKeyValue);
                else PlayerPrefs.DeleteKey(browserSaveKey);
            }
        }

        [Test]
        public void RatBehaviorCatchUpUsesOneColonyWideBudgetAndKeepsOneUpdatePerRat()
        {
            try
            {
                GrowthSystem.ResetBehaviorDiagnosticsForTests();
                GrowthSystem.SetBehaviorParticipantCount(20);
                GrowthSystem.BeginBehaviorUpdate(0.01f); // establish this frame's diagnostic window
                int beforeTwenty = GrowthSystem.LastSimulationStepCount;
                for (int rat = 0; rat < 20; rat++)
                    Assert.AreEqual(GrowthSystem.MaximumTotalBehaviorStepsPerFrame / 20,
                        GrowthSystem.BeginBehaviorUpdate(120f, "twenty-rat-" + rat));
                Assert.LessOrEqual(GrowthSystem.LastSimulationStepCount - beforeTwenty,
                    GrowthSystem.MaximumTotalBehaviorStepsPerFrame,
                    "Twenty rats share the total catch-up budget instead of each replaying 120 steps.");
                Assert.AreEqual(0, GrowthSystem.LastRepeatedBehaviorUpdateCount,
                    "A one-second sample with one behavior update per rat is not duplicated simulation work.");
                GrowthSystem.BeginBehaviorUpdate(120f, "twenty-rat-0");
                Assert.AreEqual(1, GrowthSystem.LastRepeatedBehaviorUpdateCount,
                    "The diagnostic must expose a repeated same-rat update in one rendered frame.");

                GrowthSystem.SetBehaviorParticipantCount(40);
                int beforeForty = GrowthSystem.LastSimulationStepCount;
                for (int rat = 0; rat < 40; rat++)
                    Assert.AreEqual(Mathf.Max(1, GrowthSystem.MaximumTotalBehaviorStepsPerFrame / 40),
                        GrowthSystem.BeginBehaviorUpdate(120f, "forty-rat-" + rat));
                Assert.LessOrEqual(GrowthSystem.LastSimulationStepCount - beforeForty,
                    GrowthSystem.MaximumTotalBehaviorStepsPerFrame);

                GrowthSystem.SetBehaviorParticipantCount(20);
                Assert.AreEqual(1, GrowthSystem.BeginBehaviorUpdate(0.5f),
                    "Normal-speed rat updates remain one behavior step per rendered frame.");
            }
            finally
            {
                GrowthSystem.SetBehaviorParticipantCount(1);
            }
        }

        [Test]
        public void SimulationStepCounterCountsSubstepsWhileBehaviorCounterCountsRatUpdates()
        {
            try
            {
                GrowthSystem.ResetBehaviorDiagnosticsForTests();
                GrowthSystem.SetBehaviorParticipantCount(2);
                GrowthSystem.BeginBehaviorUpdate(0.01f); // begin a diagnostic frame
                int stepsBefore = GrowthSystem.LastSimulationStepCount;
                int updatesBefore = GrowthSystem.LastBehaviorUpdateCount;
                int repeatsBefore = GrowthSystem.LastRepeatedBehaviorUpdateCount;

                int returnedSteps = GrowthSystem.BeginBehaviorUpdate(2.1f, "counter-semantics-rat");

                Assert.AreEqual(3, returnedSteps);
                Assert.AreEqual(3, GrowthSystem.LastSimulationStepCount - stepsBefore,
                    "Simulation steps are bounded substeps, not an alternate rat-call counter.");
                Assert.AreEqual(1, GrowthSystem.LastBehaviorUpdateCount - updatesBefore,
                    "Rat behavior updates count invocations.");
                Assert.AreEqual(0, GrowthSystem.LastRepeatedBehaviorUpdateCount - repeatsBefore,
                    "One participant update does not mean duplicate AI.");
            }
            finally
            {
                GrowthSystem.SetBehaviorParticipantCount(1);
            }
        }

        [Test]
        public void ExpiredRecoveryDeadlineDoesNotKeepSchedulingMaintenanceAfterRecoveryEnds()
        {
            var rat = new RatData
            {
                id = "recovery-scheduler-test",
                sex = RatSex.Female,
                stage = RatStage.Adult,
                reproductiveState = ReproductiveState.Recovery,
                recoveryUntil = 1000L,
            };

            Assert.IsTrue(BreedingSystem.IsRecoveryTransitionDue(rat, 1000L));
            // RefreshReproductiveStates changes the state but intentionally
            // preserves its historical deadline. That old timestamp must no
            // longer wake full-colony maintenance on every frame.
            rat.reproductiveState = ReproductiveState.Fertile;
            Assert.IsFalse(BreedingSystem.IsRecoveryTransitionDue(rat, 1001L));
        }

        [Test]
        public void AgeMaintenanceSchedulerReturnsTheNextStageBoundary()
        {
            long day = GameConfig.GameDayMs;
            long now = 100L * day;
            var save = new ColonySaveData();
            save.EnsureLists();
            var rat = new RatData
            {
                id = "age-boundary-test",
                sex = RatSex.Female,
                stage = RatStage.Adult,
                birthTimestamp = 0L,
                sexualMaturityDays = 60f,
                breedingEndAgeDays = 500f,
                expectedLifespanDays = 500f,
                ageDays = 100f,
            };
            save.rats.Add(rat);

            Assert.AreEqual(now + (long)((GameConfig.MatureStartDays - 100f) * day),
                GrowthSystem.NextAgeBoundaryGameTime(save, now));

            rat.ageDays = 70f;
            rat.birthTimestamp = now - 70L * day;
            rat.breedingEndAgeDays = 75f;
            Assert.AreEqual(now + 5L * day,
                GrowthSystem.NextAgeBoundaryGameTime(save, now),
                "The scheduler must choose the earliest future boundary, including breeding-end age.");
        }

        [Test]
        public void StaticDeveloperGrowthOverrideDoesNotScheduleAStaleDueBoundary()
        {
            var save = new ColonySaveData();
            save.EnsureLists();
            save.rats.Add(new RatData
            {
                id = "static-dev-age-test",
                sex = RatSex.Female,
                stage = RatStage.Adult,
                developerGrowthOverride = true,
                growthTimestamp = 0L,
                growthAnchorAgeDays = 10f,
                ageDays = 10f,
                expectedLifespanDays = 500f,
                sexualMaturityDays = 60f,
                breedingEndAgeDays = 300f,
            });

            long now = 100L * GameConfig.GameDayMs;
            Assert.AreEqual(long.MaxValue, GrowthSystem.NextAgeBoundaryGameTime(save, now),
                "A non-advancing override must not produce a current/past due time that retriggers maintenance every frame.");
        }

        [Test]
        public void PerformanceIsolationSwitchesCanBeCombinedAndResetTogether()
        {
            PerformanceIsolationMode previous = RuntimePerformanceDiagnostics.IsolationMode;
            try
            {
                RuntimePerformanceDiagnostics.SetIsolationMode(PerformanceIsolationMode.Normal);
                RuntimePerformanceDiagnostics.ToggleIsolationMode(PerformanceIsolationMode.Simulation);
                RuntimePerformanceDiagnostics.ToggleIsolationMode(PerformanceIsolationMode.RatPresentation);
                RuntimePerformanceDiagnostics.ToggleIsolationMode(PerformanceIsolationMode.RatAnimation);
                RuntimePerformanceDiagnostics.ToggleIsolationMode(PerformanceIsolationMode.UiRendering);
                RuntimePerformanceDiagnostics.ToggleIsolationMode(PerformanceIsolationMode.AutomaticUiRefresh);
                Assert.IsTrue(RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.Simulation));
                Assert.IsTrue(RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.RatPresentation));
                Assert.IsTrue(RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.RatAnimation));
                Assert.IsTrue(RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.UiRendering));
                Assert.IsTrue(RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.AutomaticUiRefresh));
                Assert.IsFalse(RuntimePerformanceDiagnostics.IsIsolationActive(PerformanceIsolationMode.RatRendering));
                RuntimePerformanceDiagnostics.ToggleIsolationMode(PerformanceIsolationMode.Normal);
                Assert.AreEqual(PerformanceIsolationMode.Normal, RuntimePerformanceDiagnostics.IsolationMode);
            }
            finally
            {
                RuntimePerformanceDiagnostics.SetIsolationMode(previous);
            }
        }

        [Test]
        public void PerformanceLogSeparatesUnattributedFrameGapsFromGpuTiming()
        {
            bool wasCapturing = RuntimePerformanceDiagnostics.CaptureEnabled;
            try
            {
                RuntimePerformanceDiagnostics.SetCaptureEnabled(true);
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.ObserveFrame(180f, GameConfig.StartGameTimeMs, 1,
                    8f, -1f, 0, 0, 0, 0, 20, 2, 20, 60, 200, 2, 240, 4f, 1f, 2f, "Habitat");
                StringAssert.Contains("UNATTRIBUTED FRAME GAP", RuntimePerformanceDiagnostics.BuildRecentSpikesText(1));
                StringAssert.Contains("unattributed_frame_gap_ms", RuntimePerformanceDiagnostics.BuildExportText(true));
                StringAssert.Contains("CPU/GPU 8.00 ms/n/a", RuntimePerformanceDiagnostics.BuildRecentSpikesText(1));
            }
            finally
            {
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(wasCapturing);
            }
        }

        [Test]
        public void BrowserGapDoesNotClaimCpuAttributionWhenWebGlCpuCounterIsZero()
        {
            bool wasCapturing = RuntimePerformanceDiagnostics.CaptureEnabled;
            try
            {
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(true);
                RuntimePerformanceDiagnostics.RecordBrowserTelemetry(16.7f, 3055.6f, 61,
                    1, 3055.6f, 3055.6f, true, true, true, true, true);
                RuntimePerformanceDiagnostics.ObserveFrame(3055.6f, GameConfig.StartGameTimeMs, 1,
                    0f, -1f, 0, 0, 0, 0, 14, 0, 14, 42, 160, 2, 854, 0f, 0f, 0f, "Habitat");
                RuntimePerformanceDiagnostics.RecordSample(new PerformanceLogSample
                {
                    UtcTicks = DateTime.UtcNow.Ticks,
                    GameTimeMs = GameConfig.StartGameTimeMs,
                    Speed = 1,
                    Fps = 0f,
                    AverageFrameMs = 3055.6f,
                    WorstFrameMs = 3055.6f,
                    CpuMainThreadMs = 0f,
                    GpuMs = -1f,
                    ActiveRatCount = 14,
                    CurrentPanel = "Habitat",
                });

                string export = RuntimePerformanceDiagnostics.BuildExportText(true);
                StringAssert.Contains("browser_avg_frame_gap_ms", export);
                StringAssert.Contains("page_hidden_during_window", export);
                StringAssert.Contains("simulation_probe_calls", export);
                StringAssert.Contains("presentation_updates", export);
                string[] csvLines = export.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(csvLines[0].TrimEnd('\r').Split(',').Length,
                    csvLines[1].TrimEnd('\r').Split(',').Length,
                    "CSV data rows must stay aligned with the expanded telemetry header.");
                StringAssert.Contains("n/a", RuntimePerformanceDiagnostics.BuildRecentSamplesText(1));
                StringAssert.DoesNotContain("UNATTRIBUTED FRAME GAP", RuntimePerformanceDiagnostics.BuildRecentSpikesText(1),
                    "A zero CPU reading is unavailable, not proof the full browser gap happened outside Unity CPU work.");
                StringAssert.Contains("hidden during window", RuntimePerformanceDiagnostics.BuildRecentSamplesText(1));
                StringAssert.Contains("browser gap 3055.60 ms", RuntimePerformanceDiagnostics.BuildRecentSpikesText(1));
            }
            finally
            {
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(wasCapturing);
            }
        }

        [Test]
        public void FavoriteStatusTogglesAndPersistsWithTheRatRecord()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            RatData rat = save.rats[0];
            Assert.IsFalse(rat.isFavorite, "New colony rats start without favorites.");

            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, rat.id, true));
            Assert.IsTrue(rat.isFavorite);
            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, rat.id, false));
            Assert.IsFalse(rat.isFavorite);
            Assert.IsFalse(RatFavoriteSystem.SetFavorite(save, "missing-rat", true));

            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, rat.id, true));
            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData loadedRat = BreedingSystem.FindRat(loaded, rat.id);
            Assert.IsNotNull(loadedRat);
            Assert.IsTrue(loadedRat.isFavorite);

            RatData legacyRat = JsonUtility.FromJson<RatData>("{\"id\":\"legacy\",\"name\":\"Legacy\"}");
            Assert.IsNotNull(legacyRat);
            Assert.IsFalse(legacyRat.isFavorite,
                "Legacy rat JSON without the new field safely defaults to not favorited.");
        }

        [Test]
        public void NewlyCreatedPinkiesAndPurchasedRatsStartWithoutFavorites()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            RatData pinkie = ColonyFactory.CreateRat(
                "favorite-pinkie-test", "Pip", RatSex.Female, 1000000L, 1,
                save.rats[0].genotype.Clone(), new TraitData(3f, 5f, 4f), RatStage.Pinkie);
            RatData purchased = StoreSystem.CreatePurchasedRat(save.storeRatListings[0], 1000000L);

            Assert.IsFalse(pinkie.isFavorite);
            Assert.IsFalse(purchased.isFavorite);
        }

        [Test]
        public void FavoritesFilterSupportsSortingAndAnEmptyResultWithoutChangingOtherRows()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            RatData first = save.rats[0];
            RatData second = save.rats[1];
            first.traits.fertility = 3f;
            second.traits.fertility = 8f;
            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, first.id, true));
            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, second.id, true));

            var visibleFavorites = new List<RatData>();
            foreach (RatData rat in save.rats)
                if (RatFavoriteSystem.IsVisibleInFavorites(rat, true)) visibleFavorites.Add(rat);
            visibleFavorites.Sort((left, right) => left.traits.fertility.CompareTo(right.traits.fertility));
            Assert.AreEqual(2, visibleFavorites.Count);
            Assert.AreSame(first, visibleFavorites[0]);
            Assert.AreSame(second, visibleFavorites[1]);

            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, first.id, false));
            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, second.id, false));
            visibleFavorites.Clear();
            foreach (RatData rat in save.rats)
                if (RatFavoriteSystem.IsVisibleInFavorites(rat, true)) visibleFavorites.Add(rat);
            Assert.AreEqual(0, visibleFavorites.Count);
            Assert.IsTrue(RatFavoriteSystem.IsVisibleInFavorites(first, false),
                "Leaving Favorites must return ordinary roster rows even when there are no favorites.");
        }

        [Test]
        public void StoreSellFiltersRespectEligibilitySexAndFavoriteStatus()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            Assert.GreaterOrEqual(save.rats.Count, 2);
            RatData female = save.rats[0];
            RatData male = save.rats[1];
            female.sex = RatSex.Female;
            male.sex = RatSex.Male;
            MakeEligibleAdultForSale(female);
            MakeEligibleAdultForSale(male);
            female.isFavorite = true;

            RatData ineligiblePinkie = ColonyFactory.CreateRat(
                "sell-filter-pinkie", "Pip", RatSex.Female, 1000000L, 1,
                female.genotype.Clone(), new TraitData(3f, 5f, 4f), RatStage.Pinkie);
            ineligiblePinkie.isFavorite = true;
            ineligiblePinkie.ageDays = 0f;
            save.rats.Add(ineligiblePinkie);

            Assert.AreEqual(2, StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.All).Count);
            List<RatData> males = StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.Males);
            Assert.AreEqual(1, males.Count);
            Assert.AreSame(male, males[0]);

            List<RatData> females = StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.Females);
            Assert.AreEqual(1, females.Count);
            Assert.AreSame(female, females[0]);

            List<RatData> favorites = StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.Favorites);
            Assert.AreEqual(1, favorites.Count,
                "Favorites filtering must still exclude a favorite pinkie that sale rules reject.");
            Assert.AreSame(female, favorites[0]);
            Assert.IsFalse(StoreSystem.CanSellRat(save, ineligiblePinkie, 1000000L));
        }

        [Test]
        public void StoreSellFiltersReturnEmptyResultsAndResetToAllOnOpen()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            for (int index = 0; index < save.rats.Count; index++)
            {
                RatData rat = save.rats[index];
                rat.stage = RatStage.Pinkie;
                rat.ageDays = 0f;
                rat.isFavorite = false;
            }

            Assert.AreEqual(0, StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.All).Count);
            Assert.AreEqual(0, StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.Males).Count);
            Assert.AreEqual(0, StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.Females).Count);
            Assert.AreEqual(0, StoreSystem.GetSellableRats(
                save, 1000000L, StoreSellFilter.Favorites).Count);

            var state = new StoreSellFilterState();
            Assert.AreEqual(StoreSellFilter.All, state.Selected);
            state.Select(StoreSellFilter.Favorites);
            Assert.AreEqual(StoreSellFilter.Favorites, state.Selected);
            state.ResetForSellPanelOpen();
            Assert.AreEqual(StoreSellFilter.All, state.Selected,
                "Opening Sell again must return to All regardless of the previous selection.");
            state.Select((StoreSellFilter)999);
            Assert.AreEqual(StoreSellFilter.All, state.Selected,
                "An invalid filter value safely falls back to All.");
        }

        private static void MakeEligibleAdultForSale(RatData rat)
        {
            rat.stage = RatStage.Adult;
            rat.ageDays = Mathf.Max(GameConfig.PupSaleMinimumAgeDays + 1f, 60f);
        }

        [Test]
        public void MyRatsMixedGrowthStagesShareOneScrollableRosterContent()
        {
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            var gameObject = new GameObject("My Rats Scroll Test Game");
            var canvasObject = new GameObject("My Rats Scroll Test Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var eventSystemObject = new GameObject("My Rats Scroll Test EventSystem", typeof(EventSystem));
            var presenterObject = new GameObject("My Rats Scroll Test Presenter");
            var pinkieRoot = new GameObject("My Rats Scroll Test Pinkie", typeof(BoxCollider));
            try
            {
                // RatPortraitPreview is intentionally omitted in this focused
                // UI-layout test; suppress only its development assertion
                // about an absent preview texture.
                LogAssert.ignoreFailingMessages = true;

                gameObject.SetActive(false);
                GameBootstrap game = gameObject.AddComponent<GameBootstrap>();
                var save = new ColonySaveData
                {
                    clock = new ClockData { gameTimeMs = GameConfig.StartGameTimeMs, speed = 1f }
                };
                save.EnsureLists();
                for (int index = 0; index < 12; index++)
                {
                    RatStage stage = index % 3 == 0 ? RatStage.Pinkie :
                        index % 3 == 1 ? RatStage.YoungRat : RatStage.Adult;
                    RatData rat = ColonyFactory.CreateRat(
                        "roster-scroll-" + index,
                        "Scroll Rat " + index,
                        index % 2 == 0 ? RatSex.Female : RatSex.Male,
                        GameConfig.StartGameTimeMs,
                        0,
                        GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s"),
                        new TraitData(10f + index, 20f + index, 30f + index),
                        stage);
                    save.rats.Add(rat);
                }
                typeof(GameBootstrap).GetProperty("Save").GetSetMethod(true).Invoke(game, new object[] { save });

                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                eventSystemObject.GetComponent<EventSystem>().enabled = true;
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(540f, 960f);
                var pageObject = new GameObject("My Rats Test Page", typeof(RectTransform));
                pageObject.transform.SetParent(canvasObject.transform, false);
                RectTransform pageContent = pageObject.GetComponent<RectTransform>();
                pageContent.anchorMin = new Vector2(0f, 1f);
                pageContent.anchorMax = new Vector2(1f, 1f);
                pageContent.pivot = new Vector2(0.5f, 1f);
                pageContent.sizeDelta = new Vector2(0f, 960f);
                var pageLayout = pageContent.gameObject.AddComponent<VerticalLayoutGroup>();
                pageLayout.childControlWidth = true;
                pageLayout.childControlHeight = true;
                pageLayout.childForceExpandWidth = true;
                pageLayout.childForceExpandHeight = false;
                var pageFitter = pageContent.gameObject.AddComponent<ContentSizeFitter>();
                pageFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                VerticalSliceUI ui = canvasObject.AddComponent<VerticalSliceUI>();
                SetPrivateField(ui, "game", game);
                SetPrivateField(ui, "content", pageContent);
                var addRoster = typeof(VerticalSliceUI).GetMethod(
                    "AddRatRoster",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(addRoster);
                addRoster.Invoke(ui, new object[] { pageContent });

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
                Canvas.ForceUpdateCanvases();

                ScrollRect rosterScroll = pageContent.GetComponentInChildren<ScrollRect>(true);
                Assert.IsNotNull(rosterScroll, "The roster must have its own ScrollRect.");
                Assert.IsTrue(rosterScroll.vertical);
                Assert.IsFalse(rosterScroll.horizontal);
                Assert.IsNotNull(rosterScroll.content);
                Assert.AreEqual(save.rats.Count, rosterScroll.content.childCount,
                    "Pinkies, young rats, and adults must all create rows in the same content transform.");
                Assert.IsNotNull(rosterScroll.viewport.GetComponent<RectMask2D>());
                Assert.IsTrue(rosterScroll.viewport.GetComponent<Image>().raycastTarget,
                    "The roster viewport must be a valid EventSystem hit surface.");
                MyRatsScrollDragRelay dragRelay = rosterScroll.viewport.GetComponent<MyRatsScrollDragRelay>();
                Assert.IsNotNull(dragRelay,
                    "The viewport must own drag dispatch explicitly instead of relying on parent-handler discovery.");
                int pinkieCount = 0;
                int nonPinkieCount = 0;
                foreach (RatData rat in save.rats)
                {
                    if (rat.stage == RatStage.Pinkie) pinkieCount++;
                    else nonPinkieCount++;
                }
                Assert.Greater(pinkieCount, 0, "This regression case must contain pinkies.");
                Assert.Greater(nonPinkieCount, 0, "Compare pinkie rows with young/adult rows in the same roster.");
                for (int rowIndex = 0; rowIndex < rosterScroll.content.childCount; rowIndex++)
                {
                    GameObject mixedStageRow = rosterScroll.content.GetChild(rowIndex).gameObject;
                    Assert.AreSame(dragRelay.gameObject,
                        ExecuteEvents.GetEventHandler<IDragHandler>(mixedStageRow),
                        "Every row, regardless of growth stage, must resolve the same My Rats viewport drag owner.");
                }

                // Simulate an imported pinkie that accidentally contains UI
                // and physics raycasters. The presenter quarantine must leave
                // the ordinary roster GraphicRaycaster as the only hit path.
                var pinkieUi = new GameObject("Pinkie Accidental UI", typeof(RectTransform), typeof(Canvas),
                    typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(Image), typeof(EventTrigger));
                pinkieUi.transform.SetParent(pinkieRoot.transform, false);
                Canvas pinkieCanvas = pinkieUi.GetComponent<Canvas>();
                pinkieCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                pinkieUi.GetComponent<Image>().raycastTarget = true;
                pinkieUi.GetComponent<CanvasGroup>().blocksRaycasts = true;
                var pinkieCamera = new GameObject("Pinkie Accidental Physics Raycaster", typeof(Camera), typeof(PhysicsRaycaster));
                pinkieCamera.transform.SetParent(pinkieRoot.transform, false);
                RatPresenter presenter = presenterObject.AddComponent<RatPresenter>();
                RatData pinkieData = ColonyFactory.CreateRat(
                    "scroll-test-pinkie", "Scroll Test Pinkie", RatSex.Female,
                    GameConfig.StartGameTimeMs, 0,
                    GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s"),
                    new TraitData(10f, 10f, 10f), RatStage.Pinkie);
                var isolatePinkie = typeof(RatPresenter).GetMethod(
                    "EnsurePinkieInputIsolation",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(isolatePinkie);
                isolatePinkie.Invoke(presenter, new object[] { pinkieRoot, pinkieData });
                Assert.IsFalse(pinkieRoot.GetComponent<BoxCollider>().enabled,
                    "An imported pinkie collider must not participate in UI or world hit testing.");
                Assert.IsFalse(pinkieUi.GetComponent<Image>().raycastTarget);
                Assert.IsFalse(pinkieUi.GetComponent<GraphicRaycaster>().isActiveAndEnabled);
                Assert.IsFalse(pinkieUi.GetComponent<Canvas>().isActiveAndEnabled);
                Assert.IsFalse(pinkieUi.GetComponent<CanvasGroup>().blocksRaycasts);
                Assert.IsFalse(pinkieUi.GetComponent<EventTrigger>().enabled);
                Assert.IsFalse(pinkieCamera.GetComponent<PhysicsRaycaster>().isActiveAndEnabled);

                var finalizeLayout = typeof(VerticalSliceUI).GetMethod(
                    "RebuildRatRosterLayout",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(finalizeLayout);
                finalizeLayout.Invoke(ui, new object[] { 1f });
                Assert.Greater(rosterScroll.content.rect.height, rosterScroll.viewport.rect.height,
                    "Mixed-stage row layout must produce a scrollable content extent.");

                // Follow the actual EventSystem route at a point over the
                // mixed-stage list. The top hit may be a row Graphic, but its
                // drag owner must be the viewport relay (not the page ScrollRect
                // or the pinkie). Forward a touch-like swipe past the viewport
                // edge and verify the same roster ScrollRect continues moving.
                Vector3[] viewportCorners = new Vector3[4];
                rosterScroll.viewport.GetWorldCorners(viewportCorners);
                Vector2 downPosition = RectTransformUtility.WorldToScreenPoint(
                    null, (viewportCorners[0] + viewportCorners[2]) * 0.5f);
                var pointer = new PointerEventData(eventSystemObject.GetComponent<EventSystem>())
                {
                    pointerId = 7,
                    position = downPosition,
                    pressPosition = downPosition,
                    button = PointerEventData.InputButton.Left,
                };
                // EditMode -batchmode has no rendered display surface, so
                // exercise EventSystem's actual handler-resolution step using
                // the same row Graphic a live GraphicRaycaster reports.
                GameObject rowHit = rosterScroll.content.GetChild(0).gameObject;
                pointer.pointerCurrentRaycast = new RaycastResult
                {
                    gameObject = rowHit,
                    module = canvasObject.GetComponent<GraphicRaycaster>(),
                };
                Assert.IsFalse(rowHit.transform.IsChildOf(pinkieRoot.transform),
                    "A pinkie presentation object must never win the page UI raycast.");
                GameObject dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(rowHit);
                Assert.AreSame(dragRelay.gameObject, dragTarget,
                    "The My Rats viewport must own the drag before the parent page ScrollRect.");
                pointer.pointerDrag = dragTarget;
                pointer.pointerPress = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rowHit);
                pointer.rawPointerPress = rowHit;
                Assert.IsTrue(ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.initializePotentialDrag));
                Assert.IsTrue(ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.beginDragHandler));
                Assert.IsTrue(dragRelay.IsDragging);
                Assert.IsTrue(ui.IsPointerOverRatRosterScroll(downPosition));
                Vector2 beforeDrag = rosterScroll.content.anchoredPosition;
                pointer.position = downPosition + new Vector2(0f, -220f);
                pointer.delta = new Vector2(0f, -220f);
                Assert.IsTrue(ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.dragHandler));
                Assert.IsFalse(ui.IsPointerOverRatRosterScroll(pointer.position),
                    "This test intentionally carries the active drag outside the viewport.");
                Assert.IsTrue(dragRelay.IsDragging,
                    "Drag ownership must persist after the pointer exits the viewport bounds.");
                Assert.Greater(Mathf.Abs(rosterScroll.content.anchoredPosition.y - beforeDrag.y), 1f,
                    "The nested ScrollRect must receive and apply the vertical drag.");
                rosterScroll.velocity = Vector2.zero;
                var isScrollMoving = typeof(VerticalSliceUI).GetMethod(
                    "IsRatRosterScrollMoving",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(isScrollMoving);
                Assert.IsTrue((bool)isScrollMoving.Invoke(ui, null),
                    "A live pointer sequence must protect the ScrollRect from rebuilds even after the pointer leaves its bounds.");
                Assert.IsTrue(ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.endDragHandler));
                Assert.IsFalse(dragRelay.IsDragging);

                var visibleMethod = typeof(VerticalSliceUI).GetMethod(
                    "IsRosterRatVisible",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(visibleMethod);
                foreach (RatData rat in save.rats)
                    Assert.IsTrue((bool)visibleMethod.Invoke(ui, new object[] { rat }),
                        rat.stage + " rats must not be excluded from the normal roster.");

                rosterScroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                Assert.Less(rosterScroll.verticalNormalizedPosition, 0.01f,
                    "The populated mixed-stage list must reach its lower end when scrolled.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(eventSystemObject);
                Object.DestroyImmediate(presenterObject);
                Object.DestroyImmediate(pinkieRoot);
            }
        }

        [Test]
        public void StoreMarketAndSellListingsUseIndependentScrollableViewports()
        {
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            var gameObject = new GameObject("Store Scroll Test Game");
            var canvasObject = new GameObject("Store Scroll Test Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var eventSystemObject = new GameObject("Store Scroll Test EventSystem", typeof(EventSystem));
            try
            {
                LogAssert.ignoreFailingMessages = true;

                gameObject.SetActive(false);
                GameBootstrap game = gameObject.AddComponent<GameBootstrap>();
                ColonySaveData save = ColonyFactory.CreateNew(1000000L);
                save.colonyCredits = 100000;
                StoreRatListingData listingTemplate = save.storeRatListings[0];
                for (int index = save.storeRatListings.Count; index < 10; index++)
                {
                    StoreRatListingData listing = JsonUtility.FromJson<StoreRatListingData>(
                        JsonUtility.ToJson(listingTemplate));
                    listing.id = "store-scroll-listing-" + index;
                    listing.name = "Market Scroll Rat " + index;
                    listing.price = 100;
                    save.storeRatListings.Add(listing);
                }

                for (int index = 0; index < 10; index++)
                {
                    string ratId = "store-scroll-sell-rat-" + index;
                    RatData rat = ColonyFactory.CreateRat(ratId, "Sell Scroll Rat " + index,
                        index % 2 == 0 ? RatSex.Female : RatSex.Male, 1000000L, 1,
                        GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s"),
                        new TraitData(20f, 30f, 30f), RatStage.Adult);
                    MakeEligibleAdultForSale(rat);
                    save.rats.Add(rat);
                    save.ratIds.Add(ratId);
                }
                typeof(GameBootstrap).GetProperty("Save").GetSetMethod(true)
                    .Invoke(game, new object[] { save });

                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(540f, 960f);
                EventSystem eventSystem = eventSystemObject.GetComponent<EventSystem>();
                eventSystem.enabled = true;

                var pageObject = new GameObject("Store Scroll Test Page", typeof(RectTransform), typeof(ScrollRect));
                pageObject.transform.SetParent(canvasObject.transform, false);
                RectTransform pageRect = pageObject.GetComponent<RectTransform>();
                pageRect.anchorMin = Vector2.zero;
                pageRect.anchorMax = Vector2.one;
                pageRect.offsetMin = Vector2.zero;
                pageRect.offsetMax = new Vector2(0f, -154f);
                ScrollRect pageScroll = pageObject.GetComponent<ScrollRect>();
                var pageViewportObject = new GameObject("Store Scroll Test Page Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
                pageViewportObject.transform.SetParent(pageObject.transform, false);
                RectTransform pageViewport = pageViewportObject.GetComponent<RectTransform>();
                pageViewport.anchorMin = Vector2.zero;
                pageViewport.anchorMax = Vector2.one;
                pageViewport.offsetMin = Vector2.zero;
                pageViewport.offsetMax = Vector2.zero;
                var pageContentObject = new GameObject("Store Scroll Test Page Content", typeof(RectTransform),
                    typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                pageContentObject.transform.SetParent(pageViewport, false);
                RectTransform pageContent = pageContentObject.GetComponent<RectTransform>();
                pageContent.anchorMin = new Vector2(0f, 1f);
                pageContent.anchorMax = new Vector2(1f, 1f);
                pageContent.pivot = new Vector2(0.5f, 1f);
                pageContent.sizeDelta = Vector2.zero;
                VerticalLayoutGroup pageLayout = pageContentObject.GetComponent<VerticalLayoutGroup>();
                pageLayout.childControlWidth = true;
                pageLayout.childControlHeight = true;
                pageLayout.childForceExpandWidth = true;
                pageLayout.childForceExpandHeight = false;
                pageContentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                pageScroll.viewport = pageViewport;
                pageScroll.content = pageContent;
                pageScroll.vertical = true;

                VerticalSliceUI ui = canvasObject.AddComponent<VerticalSliceUI>();
                SetPrivateField(ui, "game", game);
                SetPrivateField(ui, "content", pageContent);
                Type mainPanelType = typeof(VerticalSliceUI).GetNestedType(
                    "MainPanel", System.Reflection.BindingFlags.NonPublic);
                Type storeCategoryType = typeof(VerticalSliceUI).GetNestedType(
                    "StoreCategory", System.Reflection.BindingFlags.NonPublic);
                SetPrivateField(ui, "pageScroll", pageScroll);
                Assert.IsNotNull(mainPanelType);
                Assert.IsNotNull(storeCategoryType);
                SetPrivateField(ui, "activeMainPanel", System.Enum.Parse(mainPanelType, "Store"));
                var rebuild = typeof(VerticalSliceUI).GetMethod(
                    "RebuildContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(rebuild);

                rebuild.Invoke(ui, null);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
                Canvas.ForceUpdateCanvases();

                Assert.IsFalse(pageScroll.enabled,
                    "Store controls remain fixed while the dedicated market list owns vertical gestures.");
                ScrollRect marketScroll = GetPrivateField<ScrollRect>(ui, "storeRatListScroll");
                AssertStoreListCanScrollAndKeepButtons(marketScroll, eventSystem, "BUY", 10);
                Assert.Greater(marketScroll.content.rect.height, marketScroll.viewport.rect.height,
                    "The full market listing content must exceed the viewport for an upgraded market.");

                marketScroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                Assert.Less(marketScroll.verticalNormalizedPosition, 0.01f,
                    "The last Buy listing must be reachable at the bottom of the list.");
                var resetList = typeof(VerticalSliceUI).GetMethod(
                    "ResetStoreRatListToTop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(resetList);
                resetList.Invoke(ui, null);
                Assert.Greater(marketScroll.verticalNormalizedPosition, 0.99f,
                    "The explicit list reset must run after its content layout and reach the true top.");

                // A normal game-clock refresh must update live Store labels
                // without replacing the nested list while the player is
                // reading or dragging it. This was the browser failure mode:
                // the global UI signature changes each simulation second.
                SetPrivateField(ui, "ready", true);
                marketScroll.verticalNormalizedPosition = 0.45f;
                Canvas.ForceUpdateCanvases();
                float preservedStorePosition = marketScroll.verticalNormalizedPosition;
                string previousUiSignature = game.UiSignature;
                SetPrivateField(ui, "lastSignature", previousUiSignature);
                save.clock.gameTimeMs += 5000L;
                Assert.AreNotEqual(previousUiSignature, game.UiSignature,
                    "The fixture must represent a clock-only UI signature change.");
                ui.Refresh(false);
                Assert.AreSame(marketScroll, GetPrivateField<ScrollRect>(ui, "storeRatListScroll"),
                    "A clock-only refresh must preserve the Store ScrollRect and its active drag target.");
                Assert.AreEqual(preservedStorePosition, marketScroll.verticalNormalizedPosition, 0.01f,
                    "Clock-only refreshes must not reset or jump the listing position.");

                SetPrivateField(ui, "storeCategory", System.Enum.Parse(storeCategoryType, "Sell"));
                rebuild.Invoke(ui, null);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
                Canvas.ForceUpdateCanvases();
                ScrollRect sellScroll = GetPrivateField<ScrollRect>(ui, "storeRatListScroll");
                AssertStoreListCanScrollAndKeepButtons(sellScroll, eventSystem, "SELL", 12);
                Assert.Greater(sellScroll.content.rect.height, sellScroll.viewport.rect.height,
                    "The Sell list uses the same scrollable viewport with a long eligible-rat roster.");

                Transform sellFilters = pageContent.Find("Rat Market/Sell Rat Filters");
                Assert.IsNotNull(sellFilters);
                foreach (Button filterButton in sellFilters.GetComponentsInChildren<Button>(true))
                {
                    Assert.IsTrue(filterButton.interactable);
                    Assert.AreNotSame(sellScroll.gameObject,
                        ExecuteEvents.GetEventHandler<IDragHandler>(filterButton.gameObject),
                        "Filter controls stay outside the rat-list drag surface.");
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        private static void AssertStoreListCanScrollAndKeepButtons(ScrollRect scroll,
            EventSystem eventSystem, string buttonLabel, int expectedRows)
        {
            Assert.IsNotNull(scroll, "The market category must have its own ScrollRect.");
            Assert.IsTrue(scroll.vertical);
            Assert.IsFalse(scroll.horizontal);
            Assert.IsNotNull(scroll.content);
            Assert.AreEqual(expectedRows, scroll.content.childCount,
                "Every market/sell rat must be represented inside the same scrolling content.");
            Assert.IsNotNull(scroll.viewport.GetComponent<RectMask2D>());
            Assert.IsTrue(scroll.viewport.GetComponent<Image>().raycastTarget,
                "The list viewport must receive mouse-wheel and touch/mouse drag gestures.");

            foreach (Transform row in scroll.content)
            {
                Button action = row.GetComponentInChildren<Button>(true);
                Assert.IsNotNull(action, buttonLabel + " action button must exist on every row.");
                Assert.IsTrue(action.interactable, buttonLabel + " action button must remain enabled.");
                Assert.AreSame(action.gameObject,
                    ExecuteEvents.GetEventHandler<IPointerClickHandler>(action.gameObject),
                    buttonLabel + " taps must resolve to the actual row button.");
                Assert.AreSame(scroll.gameObject,
                    ExecuteEvents.GetEventHandler<IDragHandler>(action.gameObject),
                    buttonLabel + " mouse/touch drags over a row must resolve to the list ScrollRect.");
                Assert.AreSame(scroll.gameObject,
                    ExecuteEvents.GetEventHandler<IScrollHandler>(action.gameObject),
                    buttonLabel + " wheel scrolling over a row must resolve to the list ScrollRect.");
            }

            // Exercise the ordinary ScrollRect pointer path with both a mouse
            // pointer and a touch-style pointer ID, without invoking the
            // destructive Buy/Sell button callbacks in this layout test.
            Vector3[] corners = new Vector3[4];
            scroll.viewport.GetWorldCorners(corners);
            Vector2 start = RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) * 0.5f);
            int[] pointerIds = { -1, 7 };
            foreach (int pointerId in pointerIds)
            {
                scroll.verticalNormalizedPosition = 1f;
                Canvas.ForceUpdateCanvases();
                Vector2 before = scroll.content.anchoredPosition;
                var pointer = new PointerEventData(eventSystem)
                {
                    pointerId = pointerId,
                    position = start,
                    pressPosition = start,
                    button = PointerEventData.InputButton.Left,
                };
                GameObject dragOwner = ExecuteEvents.GetEventHandler<IDragHandler>(scroll.content.GetChild(0).gameObject);
                Assert.AreSame(scroll.gameObject, dragOwner);
                pointer.pointerDrag = dragOwner;
                Assert.IsTrue(ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.initializePotentialDrag));
                Assert.IsTrue(ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.beginDragHandler));
                pointer.position = start + new Vector2(0f, -180f);
                pointer.delta = new Vector2(0f, -180f);
                Assert.IsTrue(ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.dragHandler));
                Assert.Greater(Mathf.Abs(scroll.content.anchoredPosition.y - before.y), 1f,
                    buttonLabel + " list must respond to vertical mouse/touch dragging.");
                ExecuteEvents.Execute(dragOwner, pointer, ExecuteEvents.endDragHandler);
            }
        }

        [Test]
        public void FavoriteStatusDoesNotChangeSaleEligibilityOrPrice()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            RatData rat = save.rats[0];
            rat.stage = RatStage.Adult;
            rat.ageDays = GameConfig.PupSaleMinimumAgeDays + 10f;
            long gameTime = GameConfig.StartGameTimeMs + 10L * GameConfig.GameDayMs;
            bool canSellBefore = StoreSystem.CanSellRat(save, rat, gameTime);
            int saleValueBefore = StoreSystem.CalculateSaleValue(save, rat, gameTime);

            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, rat.id, true));

            Assert.AreEqual(canSellBefore, StoreSystem.CanSellRat(save, rat, gameTime));
            Assert.AreEqual(saleValueBefore, StoreSystem.CalculateSaleValue(save, rat, gameTime));
        }

        [Test]
        public void FavoriteStarPointerClickDoesNotInvokeItsRatCardParent()
        {
            GameObject eventSystemObject = new GameObject("Favorite Test EventSystem", typeof(EventSystem));
            GameObject card = new GameObject("Rat Card", typeof(RectTransform), typeof(Image), typeof(Button));
            try
            {
                int cardActions = 0;
                int favoriteActions = 0;
                card.GetComponent<Button>().onClick.AddListener(() => cardActions++);

                var star = new GameObject("Favorite Star", typeof(RectTransform), typeof(Image), typeof(Button));
                star.transform.SetParent(card.transform, false);
                star.GetComponent<Button>().onClick.AddListener(() => favoriteActions++);

                var pointer = new PointerEventData(eventSystemObject.GetComponent<EventSystem>())
                {
                    button = PointerEventData.InputButton.Left,
                    eligibleForClick = true,
                };
                bool handled = ExecuteEvents.ExecuteHierarchy(
                    star, pointer, ExecuteEvents.pointerClickHandler);

                Assert.IsTrue(handled);
                Assert.AreEqual(1, favoriteActions);
                Assert.AreEqual(0, cardActions,
                    "The nested favorite control owns the pointer click; the underlying card must not expand/select.");
            }
            finally
            {
                Object.DestroyImmediate(card);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        public void FoundersAreAdultAndHaveExactlyTwoAllelesPerLocus()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            Assert.AreEqual(2, save.rats.Count);
            Assert.AreEqual(RatStage.Adult, save.rats[0].stage);
            Assert.AreEqual(RatStage.Adult, save.rats[1].stage);
            foreach (var rat in save.rats)
            {
                Assert.AreEqual(4, rat.genotype.loci.Count);
                foreach (var locus in rat.genotype.loci)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(locus.firstAllele));
                    Assert.IsFalse(string.IsNullOrEmpty(locus.secondAllele));
                }
            }
        }

        [Test]
        public void LegacyMarketPricesMigrateFromSavedTraitsAndMarkingsOnlyOnce()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            save.storeInventoryInitialized = true;
            save.storeRatListings = new List<StoreRatListingData>
            {
                LegacyListing("legacy-self", "Legacy Self", 0f, 4f, "Self", false),
                LegacyListing("legacy-spotted", "Legacy Spotted", 12f, 13f, "Dominant white spotted", false),
                LegacyListing("legacy-mask", "Legacy Mask", 15f, 15f, "Mask", false),
                LegacyListing("legacy-albino", "Legacy Albino", 7f, 9f, "Albino masking", true),
                LegacyListing("legacy-solid", "Legacy Solid", 0f, 0f, "Solid", false),
            };

            // Serialize as an old save: each listing still carries the legacy
            // fixed price and the newly introduced version field is zero.
            string oldSaveJson = SaveSystem.ToJson(save);
            ColonySaveData migrated = SaveSystem.FromJson(oldSaveJson);

            Assert.IsNotNull(migrated);
            Assert.AreEqual(102, migrated.storeRatListings[0].price);
            Assert.AreEqual(133, migrated.storeRatListings[1].price);
            Assert.AreEqual(123, migrated.storeRatListings[2].price);
            Assert.AreEqual(108, migrated.storeRatListings[3].price);
            Assert.AreEqual(100, migrated.storeRatListings[4].price);
            foreach (StoreRatListingData listing in migrated.storeRatListings)
                Assert.AreEqual(GameConfig.StorePurchasePricingVersion, listing.pricingVersion);

            StoreRatListingData migratedListing = migrated.storeRatListings[1];
            Assert.AreEqual("legacy-spotted", migratedListing.id);
            Assert.AreEqual("Legacy Spotted", migratedListing.name);
            Assert.AreEqual(12f, migratedListing.traits.health);
            Assert.AreEqual(13f, migratedListing.traits.fertility);
            Assert.AreEqual("Dominant white spotted", migratedListing.markingFamily);

            int savedPrice = migratedListing.price;
            migratedListing.traits.health = 100f;
            StoreSystem.EnsureStoreState(migrated);
            Assert.AreEqual(savedPrice, migratedListing.price,
                "Current-version listings must not be repriced during refresh/repair.");
        }

        [Test]
        public void NewAndRestockedMarketListingsPersistTheirPricingVersion()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            Assert.IsTrue(save.storeInventoryInitialized);
            foreach (StoreRatListingData listing in save.storeRatListings)
            {
                Assert.AreEqual(GameConfig.StorePurchasePricingVersion, listing.pricingVersion);
                Assert.AreEqual(StoreSystem.CalculatePurchasePrice(
                    listing.traits, listing.markingFamily, listing.genotype), listing.price);
            }

            StoreSystem.RestockNow(save, save.clock.gameTimeMs + GameConfig.GameDayMs);
            foreach (StoreRatListingData listing in save.storeRatListings)
                Assert.AreEqual(GameConfig.StorePurchasePricingVersion, listing.pricingVersion);
        }

        private static StoreRatListingData LegacyListing(
            string id, string name, float health, float fertility, string markingFamily, bool albino)
        {
            GenotypeData genotype = GeneticsSystem.CreateFounder(
                "B", "B", albino ? "c" : "C", albino ? "c" : "C", "D", "D",
                markingFamily == "Self" || markingFamily == "Solid" ? "s" : "S", "s");
            return new StoreRatListingData
            {
                id = id,
                name = name,
                sex = RatSex.Female,
                price = GameConfig.StarterAdultRatPrice,
                pricingVersion = 0,
                markingFamily = markingFamily,
                coatColorVariant = albino ? "albino" : "black",
                coatTone = 1f,
                genotype = genotype,
                traits = new TraitData(10f, health, fertility),
            };
        }

        [Test]
        public void WelcomePopupIsPendingOnlyForNewOrResetColoniesAndPersistsAcknowledgement()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            Assert.IsTrue(save.welcomePopupPending);

            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            Assert.IsTrue(loaded.welcomePopupPending);

            loaded.welcomePopupPending = false;
            ColonySaveData continued = SaveSystem.FromJson(SaveSystem.ToJson(loaded));
            Assert.IsFalse(continued.welcomePopupPending);
        }

        [Test]
        public void BuiltInNamePoolsAreLargeAndDoNotGenerateAutomaticJrNames()
        {
            Assert.GreaterOrEqual(new HashSet<string>(GameConfig.MaleRatNames).Count, 200);
            Assert.GreaterOrEqual(new HashSet<string>(GameConfig.FemaleRatNames).Count, 200);
            foreach (string name in GameConfig.MaleRatNames)
                Assert.IsFalse(name.EndsWith(" Jr", StringComparison.OrdinalIgnoreCase));
            foreach (string name in GameConfig.FemaleRatNames)
                Assert.IsFalse(name.EndsWith(" Jr", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void GeneratedNamesAreMostlySingleAndStableForTheSameRatId()
        {
            Assert.That(GameConfig.RatDoubleNameChance, Is.InRange(0.10f, 0.15f));
            var save = new ColonySaveData();
            save.EnsureLists();
            int doubleNames = 0;
            const int sampleCount = 1000;
            for (int i = 0; i < sampleCount; i++)
            {
                string id = "name-shape-rat-" + i.ToString("D4");
                string generated = RatNameSystem.GenerateAvailableName(
                    save, id, RatSex.Female, GameConfig.StartGameTimeMs);
                if (generated.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 1)
                    doubleNames++;

                var reloadedSave = new ColonySaveData();
                reloadedSave.EnsureLists();
                Assert.AreEqual(generated, RatNameSystem.GenerateAvailableName(
                    reloadedSave, id, RatSex.Female, GameConfig.StartGameTimeMs),
                    "A rat's saved ID must produce the same name after reload.");
            }

            Assert.That(doubleNames, Is.InRange(100, 150),
                "New names should remain mostly single, with about 10-15% two-part names.");
        }

        [Test]
        public void MarketNamesUseSexSpecificPoolsStayUniqueAndPersistThroughPurchase()
        {
            ColonySaveData save = ColonyFactory.CreateNew(4451231L);
            Assert.AreEqual(2, save.storeRatListings.Count);
            var livingNames = new HashSet<string>();
            foreach (RatData rat in save.rats)
                livingNames.Add(RatNameSystem.NormalizeForComparison(rat.name));

            var listingNames = new HashSet<string>();
            foreach (StoreRatListingData listing in save.storeRatListings)
            {
                string key = RatNameSystem.NormalizeForComparison(listing.name);
                Assert.IsTrue(listingNames.Add(key), "Listings in one restock must not share a normalized name.");
                Assert.IsFalse(livingNames.Contains(key), "Market names should avoid names already used by colony rats.");
                string[] sexPool = listing.sex == RatSex.Female ? GameConfig.FemaleRatNames : GameConfig.MaleRatNames;
                Assert.IsTrue(ContainsNormalizedName(sexPool, listing.name),
                    listing.sex + " listing must draw from the matching built-in name pool.");
            }

            string maleName = StoreSystem.FindListing(save, "store_adult_male_0").name;
            string femaleName = StoreSystem.FindListing(save, "store_adult_female_0").name;
            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            Assert.AreEqual(maleName, StoreSystem.FindListing(loaded, "store_adult_male_0").name,
                "Reloading must not regenerate a listing's persisted name.");
            Assert.AreEqual(femaleName, StoreSystem.FindListing(loaded, "store_adult_female_0").name);
            StoreSystem.EnsureStoreState(loaded);
            Assert.AreEqual(maleName, StoreSystem.FindListing(loaded, "store_adult_male_0").name,
                "Store refresh/repair must leave current listing names unchanged.");

            StoreRatListingData purchasedListing = StoreSystem.FindListing(loaded, "store_adult_male_0");
            RatData purchased = StoreSystem.CreatePurchasedRat(purchasedListing, loaded.clock.gameTimeMs);
            Assert.AreEqual(maleName, purchased.name, "A purchased rat keeps its market listing name.");

            var maleNamesAcrossColonies = new HashSet<string>();
            var femaleNamesAcrossColonies = new HashSet<string>();
            for (int index = 0; index < 16; index++)
            {
                ColonySaveData generated = ColonyFactory.CreateNew(7000000L + index * 7919L);
                maleNamesAcrossColonies.Add(generated.storeRatListings[0].name);
                femaleNamesAcrossColonies.Add(generated.storeRatListings[1].name);
            }
            Assert.Greater(maleNamesAcrossColonies.Count, 1,
                "Seeded restocks should vary between colonies rather than repeating a fixed male name.");
            Assert.Greater(femaleNamesAcrossColonies.Count, 1,
                "Seeded restocks should vary between colonies rather than repeating a fixed female name.");
        }

        [Test]
        public void MarketNameGenerationIncludesSexSpecificCustomNameLists()
        {
            string[] originalMaleNames = (string[])GameConfig.MaleRatNames.Clone();
            string[] originalFemaleNames = (string[])GameConfig.FemaleRatNames.Clone();
            try
            {
                Array.Clear(GameConfig.MaleRatNames, 0, GameConfig.MaleRatNames.Length);
                Array.Clear(GameConfig.FemaleRatNames, 0, GameConfig.FemaleRatNames.Length);
                var save = new ColonySaveData
                {
                    createdAt = 88990011L,
                    storeInventoryInitialized = true,
                    ratNameMigrationVersion = RatNameSystem.CurrentMigrationVersion,
                    clock = new ClockData { gameTimeMs = GameConfig.StartGameTimeMs },
                };
                save.EnsureLists();
                save.customMaleRatNames.Add("Custom Market Buck");
                save.customFemaleRatNames.Add("Custom Market Doe");

                StoreSystem.RestockNow(save, save.clock.gameTimeMs);

                Assert.AreEqual("Custom Market Buck", StoreSystem.FindListing(save, "store_adult_male_1").name);
                Assert.AreEqual("Custom Market Doe", StoreSystem.FindListing(save, "store_adult_female_1").name);
            }
            finally
            {
                Array.Copy(originalMaleNames, GameConfig.MaleRatNames, originalMaleNames.Length);
                Array.Copy(originalFemaleNames, GameConfig.FemaleRatNames, originalFemaleNames.Length);
            }
        }

        private static bool ContainsNormalizedName(string[] pool, string name)
        {
            if (pool == null) return false;
            string key = RatNameSystem.NormalizeForComparison(name);
            foreach (string candidate in pool)
                if (RatNameSystem.NormalizeForComparison(candidate) == key) return true;
            return false;
        }

        [Test]
        public void CustomCompoundNamesStayCompleteAndRecentSecondNamesAreAvoided()
        {
            string[] originalFemaleNames = (string[])GameConfig.FemaleRatNames.Clone();
            string[] originalMaleNames = (string[])GameConfig.MaleRatNames.Clone();
            try
            {
                Array.Clear(GameConfig.FemaleRatNames, 0, GameConfig.FemaleRatNames.Length);
                GameConfig.FemaleRatNames[0] = "Simple Builtin";
                var customSave = new ColonySaveData();
                customSave.EnsureLists();
                customSave.customFemaleRatNames.Add("Player Chosen Full Name");

                string selectedCustom = string.Empty;
                for (int i = 0; i < 100; i++)
                {
                    selectedCustom = RatNameSystem.GenerateAvailableName(
                        customSave, "custom-complete-name-" + i, RatSex.Female, GameConfig.StartGameTimeMs);
                    if (selectedCustom == "Player Chosen Full Name") break;
                }
                Assert.AreEqual("Player Chosen Full Name", selectedCustom,
                    "A custom entry is a complete candidate; the allocator must not append another name.");

                Array.Clear(GameConfig.MaleRatNames, 0, GameConfig.MaleRatNames.Length);
                GameConfig.MaleRatNames[0] = "First Bear";
                GameConfig.MaleRatNames[1] = "Second Paws";
                var historySave = new ColonySaveData();
                historySave.EnsureLists();
                long now = 100L * GameConfig.GameDayMs;
                RatNameSystem.RecordUsage(historySave, "Previously Used Bear", RatSex.Male,
                    "old-rat", now - GameConfig.GameDayMs);
                string afterBear = RatNameSystem.GenerateAvailableName(
                    historySave, "avoid-bear-repeat", RatSex.Male, now);
                Assert.AreEqual("Second Paws", afterBear,
                    "Recent second-name usage should steer a compound name away from Bear.");
            }
            finally
            {
                Array.Copy(originalFemaleNames, GameConfig.FemaleRatNames, originalFemaleNames.Length);
                Array.Copy(originalMaleNames, GameConfig.MaleRatNames, originalMaleNames.Length);
            }
        }

        [Test]
        public void NameShapeUpdateDoesNotRenameExistingRats()
        {
            var save = new ColonySaveData();
            save.EnsureLists();
            save.ratNameMigrationVersion = RatNameSystem.CurrentMigrationVersion;
            save.rats.Add(new RatData { id = "existing-1", name = "Shelby Mae", sex = RatSex.Female });
            save.rats.Add(new RatData { id = "existing-2", name = "Milo Bear", sex = RatSex.Male });

            RatNameSystem.EnsureUniqueNames(save, 1000000L);

            Assert.AreEqual("Shelby Mae", save.rats[0].name);
            Assert.AreEqual("Milo Bear", save.rats[1].name);
        }

        [Test]
        public void DirectParentNameInheritanceUsesOnlyTheRequestedLineageSuffixes()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            save.rats.Clear();
            RatData parent = ColonyFactory.CreateRat("parent-harry", "Harry", RatSex.Male, 0L, 0,
                GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s"),
                new TraitData(10f, 10f, 10f), RatStage.Adult);
            save.rats.Add(parent);
            RatData pup = ColonyFactory.CreateRat("pup-harry", "Harry", RatSex.Female, 0L, 0,
                parent.genotype, new TraitData(10f, 10f, 10f), RatStage.Pinkie);
            pup.motherId = parent.id;
            save.rats.Add(pup);
            RatNameSystem.EnsureBirthName(save, pup, 1000L);
            Assert.AreEqual("Harry Jr", pup.name);

            parent.name = "Harry Jr";
            RatData grandPup = ColonyFactory.CreateRat("grand-harry", "Harry Jr", RatSex.Female, 0L, 0,
                parent.genotype, new TraitData(10f, 10f, 10f), RatStage.Pinkie);
            grandPup.motherId = parent.id;
            save.rats.Add(grandPup);
            RatNameSystem.EnsureBirthName(save, grandPup, 1000L);
            Assert.AreEqual("Harry III", grandPup.name);

            RatData greatGrandPup = ColonyFactory.CreateRat("great-grand-harry", "Harry III", RatSex.Male,
                0L, 3, parent.genotype, new TraitData(10f, 10f, 10f), RatStage.Pinkie);
            greatGrandPup.motherId = grandPup.id;
            save.rats.Add(greatGrandPup);
            RatNameSystem.EnsureBirthName(save, greatGrandPup, 1000L);
            Assert.AreEqual("Harry IV", greatGrandPup.name);
        }

        [Test]
        public void PlayerNamesRemainOwnedAndCustomListsAreSanitized()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            RatData first = save.rats[0];
            first.name = "Harry";
            first.nameWasPlayerAssigned = true;
            RatNameSystem.SetCustomNames(save, RatSex.Male, "  Custom One  \n\ncustom-one\nCustom Two");
            Assert.AreEqual(2, save.customMaleRatNames.Count);
            Assert.AreEqual("Custom One\nCustom Two", RatNameSystem.CustomNamesText(save, RatSex.Male));
            RatData second = save.rats[1];
            second.name = "Harry";
            RatNameSystem.EnsureUniqueNames(save, save.clock.gameTimeMs);
            Assert.AreEqual("Harry", first.name);
            Assert.AreNotEqual("Harry", second.name);
        }

        [Test]
        public void PendingNewbornNamingQueueSurvivesSaveLoad()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            save.pendingNamingLitterIds.Add("pending-litter");
            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            Assert.IsNotNull(loaded.pendingNamingLitterIds);
            Assert.Contains("pending-litter", loaded.pendingNamingLitterIds);
        }

        [Test]
        public void NewGameFoundersUseAbsoluteBeginnerStats()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            Assert.AreEqual(2, save.rats.Count);
            Assert.AreNotEqual(save.rats[0].traits.size, save.rats[1].traits.size,
                "The coordinated starter pair should not be identical clones.");
            foreach (var rat in save.rats)
            {
                Assert.LessOrEqual(rat.traits.size, 15f);
                Assert.LessOrEqual(rat.traits.health, 15f);
                Assert.LessOrEqual(rat.traits.fertility, 15f);
                Assert.LessOrEqual(rat.baseHealth, 15f);
                Assert.LessOrEqual(rat.baseFertility, 15f);
                Assert.GreaterOrEqual(rat.traits.size, 0f);
                Assert.GreaterOrEqual(rat.traits.health, 0f);
                Assert.GreaterOrEqual(rat.traits.fertility, 0f);
            }
        }

        [Test]
        public void NewGameFoundersUseRandomizedStoreStylePair()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            Assert.AreEqual(2, save.rats.Count);

            RatData female = save.rats.Find(rat => rat.sex == RatSex.Female);
            RatData male = save.rats.Find(rat => rat.sex == RatSex.Male);
            Assert.IsNotNull(female);
            Assert.IsNotNull(male);
            Assert.IsTrue(female.isStarterRat);
            Assert.IsTrue(male.isStarterRat);
            Assert.AreNotEqual(female.id, male.id);
            Assert.IsTrue(System.Array.IndexOf(GameConfig.FemaleRatNames, female.name) >= 0);
            Assert.IsTrue(System.Array.IndexOf(GameConfig.MaleRatNames, male.name) >= 0);
            Assert.IsFalse(string.IsNullOrEmpty(female.coatColorVariant));
            Assert.IsFalse(string.IsNullOrEmpty(male.coatColorVariant));
            Assert.IsFalse(string.IsNullOrEmpty(female.markingFamily));
            Assert.IsFalse(string.IsNullOrEmpty(male.markingFamily));
            Assert.AreEqual("Solid", female.markingFamily);
            Assert.AreEqual("Solid", male.markingFamily);
            Assert.IsFalse(female.phenotype.spotted);
            Assert.IsFalse(male.phenotype.spotted);
            Assert.AreEqual(GameConfig.StarterSolidCoatColorVariant, female.coatColorVariant);
            Assert.AreEqual(GameConfig.StarterSolidCoatColorVariant, male.coatColorVariant);
            Assert.AreEqual(female.phenotype.coatColorLabel, male.phenotype.coatColorLabel,
                "The two founders should start with the same solid coat color.");
            Assert.IsFalse(GeneticsSystem.IsAlbinoGenotype(female.genotype));
            Assert.IsFalse(GeneticsSystem.IsAlbinoGenotype(male.genotype));
            Assert.GreaterOrEqual(female.ageDays, GameConfig.StarterFemaleMinimumAgeDays);
            Assert.LessOrEqual(female.ageDays, GameConfig.StarterFemaleMaximumAgeDays);
            Assert.GreaterOrEqual(male.ageDays, GameConfig.StarterMaleMinimumAgeDays);
            Assert.LessOrEqual(male.ageDays, GameConfig.StarterMaleMaximumAgeDays);
            Assert.AreEqual(RatStage.Adult, female.stage);
            Assert.AreEqual(RatStage.Adult, male.stage);
            Assert.AreEqual(RatEnclosure.Pairing, female.enclosure);
            Assert.AreEqual(RatEnclosure.Pairing, male.enclosure);
            Assert.IsTrue(female.pairingHabitatAssigned);
            Assert.IsTrue(male.pairingHabitatAssigned);
        }

        [Test]
        public void MarketMarkingRollIsRareByDefault()
        {
            Assert.AreEqual(0.01f, GameConfig.StoreFounderMarkingChance,
                "Only one percent of market founder rolls should introduce visible markings.");
        }

        [Test]
        public void MatureAndElderlyStagesFollowAgeDeclineAndIndividualCutoff()
        {
            RatData rat = CreateAgeBoundaryRat(364f, 700f, 60f);

            Assert.AreEqual(RatStage.Adult, GrowthSystem.StageForAge(rat));
            Assert.IsTrue(StoreSystem.CanSellRat(rat));
            int normalValue = StoreSystem.CalculateSaleValue(rat);

            rat.ageDays = 365f;
            Assert.AreEqual(RatStage.Mature, GrowthSystem.StageForAge(rat));
            Assert.IsTrue(StoreSystem.CanSellRat(rat));
            Assert.AreEqual(normalValue, StoreSystem.CalculateSaleValue(rat),
                "The price is unchanged at the exact decline boundary and begins decreasing after it.");

            rat.ageDays = 532f;
            Assert.AreEqual(RatStage.Mature, GrowthSystem.StageForAge(rat));
            Assert.Less(BreedingSystem.AgeBreedingEffectiveness(rat), 1f);
            Assert.Less(StoreSystem.CalculateSaleValue(rat), normalValue);

            rat.ageDays = 699f;
            Assert.AreEqual(RatStage.Mature, GrowthSystem.StageForAge(rat));
            Assert.IsTrue(StoreSystem.CanSellRat(rat));
            Assert.Greater(BreedingSystem.AgeBreedingEffectiveness(rat), 0f);

            rat.ageDays = 700f;
            Assert.AreEqual(RatStage.Elderly, GrowthSystem.StageForAge(rat));
            Assert.IsFalse(StoreSystem.CanSellRat(rat));
            Assert.AreEqual(0, StoreSystem.CalculateSaleValue(rat));

            rat.ageDays = 701f;
            Assert.AreEqual(RatStage.Elderly, GrowthSystem.StageForAge(rat));
            Assert.IsFalse(StoreSystem.CanSellRat(rat));
        }

        [Test]
        public void NaturallyLowFertilityAdultIsNotElderlyOrSaleBlocked()
        {
            RatData rat = CreateAgeBoundaryRat(120f, 700f, 0f);
            rat.traits.fertility = 0f;
            rat.baseFertility = 0f;

            Assert.AreEqual(RatStage.Adult, GrowthSystem.StageForAge(rat));
            Assert.IsTrue(StoreSystem.CanSellRat(rat));
            Assert.Greater(StoreSystem.CalculateSaleValue(rat), 0);
        }

        [Test]
        public void MatureElderlyStageAndSaleRestrictionSurviveJsonReload()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            RatData rat = save.rats[0];
            rat.birthTimestamp = save.clock.gameTimeMs - (long)(700f * GameConfig.GameDayMs);
            rat.ageDays = 700f;
            rat.breedingEndAgeDays = 700f;
            // This case is about the Elderly-stage sale restriction, not
            // natural death. Ensure this rat's saved lifespan extends past
            // the 700-day stage boundary before reloading the colony.
            rat.expectedLifespanDays = GameConfig.MaximumLifespanDays;
            rat.stage = RatStage.Mature;

            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData loadedRat = loaded.rats.Find(candidate => candidate.id == rat.id);
            Assert.IsNotNull(loadedRat);
            Assert.AreEqual(RatStage.Elderly, loadedRat.stage);
            Assert.IsFalse(StoreSystem.CanSellRat(loadedRat));
            Assert.AreEqual(0, StoreSystem.CalculateSaleValue(loadedRat));
        }

        [Test]
        public void PupSaleRequiresSixWeeksAndCompletedWeaning()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            long now = save.clock.gameTimeMs;
            var pup = new RatData
            {
                id = "pup-sale-test",
                name = "Pup",
                sex = RatSex.Female,
                stage = RatStage.YoungRat,
                ageDays = 42f,
                birthTimestamp = now - (long)(42f * GameConfig.GameDayMs),
                breedingEndAgeDays = 700f,
                sexualMaturityDays = GameConfig.FemaleSexualMaturityDays,
                expectedLifespanDays = GameConfig.MaximumLifespanDays,
                motherId = save.rats[0].id,
                litterId = "litter-sale-test",
                traits = new TraitData(8f, 8f, 8f),
            };
            save.rats.Add(pup);
            save.ratIds.Add(pup.id);
            save.litters.Add(new LitterData
            {
                id = pup.litterId,
                motherId = pup.motherId,
                pupIds = new List<string> { pup.id },
                birthTimestamp = now - (long)(21f * GameConfig.GameDayMs),
                weaningTimestamp = now + GameConfig.GameDayMs,
            });

            Assert.IsFalse(StoreSystem.CanSellRat(save, pup, now));
            Assert.That(StoreSystem.SaleRestrictionReason(save, pup, now), Does.Contain("Not fully weaned"));
            Assert.IsTrue(StoreSystem.CanSellRat(save, pup, now + GameConfig.GameDayMs));

            pup.ageDays = 41f;
            Assert.IsFalse(StoreSystem.CanSellRat(save, pup, now + GameConfig.GameDayMs));
            Assert.That(StoreSystem.SaleRestrictionReason(save, pup, now + GameConfig.GameDayMs), Does.Contain("Too young"));
        }

        [Test]
        public void OffspringTraitsUseParentMidpointsForLowValuesInsteadOfFiftyFallbacks()
        {
            var inherited = GeneticsSystem.InheritTraits(
                new TraitData(8f, 8f, 8f),
                new TraitData(2f, 2f, 2f));

            // TraitVariation is intentionally preserved, so the expected
            // midpoint is 5 with the existing +/- variation around it.
            Assert.That(inherited.size, Is.InRange(0f, 9f));
            Assert.That(inherited.health, Is.InRange(0f, 9f));
            Assert.That(inherited.fertility, Is.InRange(0f, 9f));
            Assert.Less(inherited.health, 50f);

            var child = ColonyFactory.CreateRat(
                "low-inherited-child", "Child", RatSex.Female, 1000000L, 1,
                GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s"),
                inherited, RatStage.Pinkie);
            GrowthSystem.EnsureBiologyDefaults(child);
            Assert.AreEqual(child.traits.health, child.baseHealth);
            Assert.AreEqual(child.traits.fertility, child.baseFertility);
            Assert.IsTrue(child.baseHealthInitialized);
            Assert.IsTrue(child.baseFertilityInitialized);
        }

        [Test]
        public void ZeroInheritedTraitsRemainZeroThroughBiologyInitialization()
        {
            var inherited = GeneticsSystem.InheritTraits(
                new TraitData(0f, 0f, 0f),
                new TraitData(0f, 0f, 0f));
            Assert.AreEqual(0f, inherited.size);
            Assert.AreEqual(0f, inherited.health);
            Assert.AreEqual(0f, inherited.fertility);

            var child = ColonyFactory.CreateRat(
                "zero-inherited-child", "Child", RatSex.Male, 1000000L, 1,
                GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s"),
                inherited, RatStage.Pinkie);
            GrowthSystem.EnsureBiologyDefaults(child);
            Assert.AreEqual(0f, child.traits.health);
            Assert.AreEqual(0f, child.baseHealth);
            Assert.AreEqual(0f, child.traits.fertility);
            Assert.AreEqual(0f, child.baseFertility);
        }

        [Test]
        public void InheritedTraitRangesRemainValidForFifteenAndHighParents()
        {
            var beginner = GeneticsSystem.InheritTraits(
                new TraitData(15f, 15f, 15f),
                new TraitData(15f, 15f, 15f));
            Assert.That(beginner.size, Is.InRange(11f, 19f));
            Assert.That(beginner.health, Is.InRange(11f, 19f));
            Assert.That(beginner.fertility, Is.InRange(11f, 19f));

            var high = GeneticsSystem.InheritTraits(
                new TraitData(96f, 96f, 96f),
                new TraitData(88f, 88f, 88f));
            Assert.That(high.size, Is.InRange(84f, 100f));
            Assert.That(high.health, Is.InRange(84f, 100f));
            Assert.That(high.fertility, Is.InRange(84f, 100f));
        }

        [Test]
        public void AdultVisualScaleUsesStoredSizeAndAgeStillControlsGrowth()
        {
            GenotypeData genotype = GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s");
            RatData low = ColonyFactory.CreateRat("size-low", "Low", RatSex.Male, 0L, 0,
                genotype.Clone(), new TraitData(0f, 40f, 40f), RatStage.Adult);
            RatData middle = ColonyFactory.CreateRat("size-middle", "Middle", RatSex.Male, 0L, 0,
                genotype.Clone(), new TraitData(50f, 40f, 40f), RatStage.Adult);
            RatData high = ColonyFactory.CreateRat("size-high", "High", RatSex.Male, 0L, 0,
                genotype.Clone(), new TraitData(100f, 40f, 40f), RatStage.Adult);

            low.ageDays = low.sexualMaturityDays;
            middle.ageDays = middle.sexualMaturityDays;
            high.ageDays = high.sexualMaturityDays;

            float lowAdultScale = GrowthSystem.VisualScaleForAge(low);
            float middleAdultScale = GrowthSystem.VisualScaleForAge(middle);
            float highAdultScale = GrowthSystem.VisualScaleForAge(high);

            Assert.AreEqual(GameConfig.AdultSizeVisualScaleMinimum, lowAdultScale, 0.0001f);
            Assert.AreEqual(GameConfig.AdultVisualScale, middleAdultScale, 0.0001f);
            Assert.AreEqual(GameConfig.AdultSizeVisualScaleMaximum, highAdultScale, 0.0001f);
            Assert.Less(lowAdultScale, middleAdultScale);
            Assert.Less(middleAdultScale, highAdultScale);
            Assert.AreEqual(0f, low.traits.size, 0.0001f,
                "Visual scaling must not rewrite the stored Size rating.");
            Assert.AreEqual(100f, high.traits.size, 0.0001f,
                "Visual scaling must not rewrite the stored Size rating.");

            low.ageDays = GameConfig.PinkieStageDays;
            float lowYoungScale = GrowthSystem.VisualScaleForAge(low);
            Assert.Greater(lowAdultScale, lowYoungScale,
                "A young rat should grow toward its own size-dependent adult endpoint.");
            Assert.Less(lowYoungScale, lowAdultScale);
        }

        [Test]
        public void InheritedTraitBaselinesPersistThroughSaveReloadAndBirth()
        {
            const long gameTime = 1000000L;
            var save = ColonyFactory.CreateNew(gameTime);
            save.rats.Clear();
            save.ratIds.Clear();
            var genotype = GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s");
            var mother = ColonyFactory.CreateRat(
                "inheritance-mother", "Taffy", RatSex.Female,
                gameTime - (100L * GameConfig.GameDayMs), 0, genotype.Clone(),
                new TraitData(8f, 8f, 8f), RatStage.Adult);
            var father = ColonyFactory.CreateRat(
                "inheritance-father", "Otto", RatSex.Male,
                gameTime - (100L * GameConfig.GameDayMs), 0, genotype.Clone(),
                new TraitData(2f, 2f, 2f), RatStage.Adult);
            mother.enclosure = RatEnclosure.FemaleColony;
            father.enclosure = RatEnclosure.MaleColony;
            save.rats.Add(mother);
            save.rats.Add(father);
            save.ratIds.Add(mother.id);
            save.ratIds.Add(father.id);
            var pregnancy = new PregnancyData
            {
                id = "inheritance-pregnancy",
                motherId = mother.id,
                fatherId = father.id,
                startedAt = gameTime,
                dueAt = gameTime,
                status = "pending",
                expectedLitterSize = 1,
            };
            mother.pregnancyId = pregnancy.id;
            mother.reproductiveState = ReproductiveState.Pregnant;
            save.pregnancies.Add(pregnancy);

            LitterData litter;
            string reason;
            Assert.IsTrue(BreedingSystem.FinishPregnancy(save, pregnancy.id, gameTime, out litter, out reason), reason);
            RatData pup = BreedingSystem.FindRat(save, litter.pupIds[0]);
            Assert.IsNotNull(pup);
            Assert.That(pup.traits.size, Is.InRange(0f, 9f));
            Assert.That(pup.traits.health, Is.InRange(0f, 9f));
            Assert.That(pup.traits.fertility, Is.InRange(0f, 9f));
            Assert.Less(pup.traits.health, 50f);
            Assert.IsTrue(pup.baseHealthInitialized);
            Assert.IsTrue(pup.baseFertilityInitialized);

            string json = SaveSystem.ToJson(save);
            ColonySaveData restored = SaveSystem.FromJson(json);
            RatData restoredPup = BreedingSystem.FindRat(restored, pup.id);
            Assert.IsNotNull(restoredPup);
            Assert.AreEqual(pup.traits.size, restoredPup.traits.size, 0.0001f);
            Assert.AreEqual(pup.traits.health, restoredPup.traits.health, 0.0001f);
            Assert.AreEqual(pup.traits.fertility, restoredPup.traits.fertility, 0.0001f);
            Assert.AreEqual(pup.baseHealth, restoredPup.baseHealth, 0.0001f);
            Assert.AreEqual(pup.baseFertility, restoredPup.baseFertility, 0.0001f);
        }

        [Test]
        public void BehaviorDeltaFollowsSelectedSimulationSpeed()
        {
            // This also exercises a slower WebGL frame. No elapsed real time
            // may be discarded before the simulation multiplier is applied.
            const float realFrameSeconds = 0.06f;

            GrowthSystem.SetRuntimeSpeed(1f);
            float oneX = GrowthSystem.SimulationBehaviorDeltaSeconds(realFrameSeconds);
            GrowthSystem.SetRuntimeSpeed(2f);
            float twoX = GrowthSystem.SimulationBehaviorDeltaSeconds(realFrameSeconds);
            GrowthSystem.SetRuntimeSpeed(3f);
            float threeX = GrowthSystem.SimulationBehaviorDeltaSeconds(realFrameSeconds);
            GrowthSystem.SetRuntimeSpeed(1f);

            Assert.AreEqual(1f, GrowthSystem.SimulationMultiplierForSpeed(1f), 0.00001f,
                "1x is the baseline game-clock movement rate.");
            Assert.AreEqual(60f, GrowthSystem.SimulationMultiplierForSpeed(2f), 0.0001f,
                "2x movement must use the same rate ratio as the clock: one game hour per real second.");
            Assert.AreEqual(1440f, GrowthSystem.SimulationMultiplierForSpeed(3f), 0.001f,
                "3x movement must use the same rate ratio as the clock: one game day per real second.");
            Assert.AreEqual(oneX * 60f, twoX, 0.0001f,
                "2x advances one game hour per real second, 60 times the 1x rate.");
            Assert.AreEqual(oneX * 1440f, threeX, 0.001f,
                "3x advances one game day per real second, 1,440 times the 1x rate.");
        }

        [Test]
        public void WorldMovementStepProducesProportionalTravelTimes()
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 0.037f;
            const int frameCount = 180;
            Vector3 target = new Vector3(100000f, 0f, 0f);
            GameObject[] roots = new GameObject[3];

            float[] travelled = new float[3];
            try
            {
                for (int speedIndex = 0; speedIndex < travelled.Length; speedIndex++)
                {
                    roots[speedIndex] = new GameObject("world movement " + (speedIndex + 1) + "x");
                    float speed = speedIndex + 1f;
                    GrowthSystem.SetRuntimeSpeed(speed);
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        float frameSimulationDelta = GrowthSystem.SimulationMovementDeltaSeconds(realFrameSeconds);
                        float movementBudget = GrowthSystem.SimulationMovementTimeBudget(frameSimulationDelta);
                        Vector3 previous = roots[speedIndex].transform.position;
                        roots[speedIndex].transform.position = GrowthSystem.SimulationMovementTargetPosition(
                            previous, target, baseWorldSpeed, frameSimulationDelta, ref movementBudget);
                        travelled[speedIndex] += Vector3.Distance(
                            previous, roots[speedIndex].transform.position);
                    }
                }

                Assert.AreEqual(travelled[0] * 60f, travelled[1], 0.02f,
                    "2x Transform travel must match the clock's 60x game-time rate in equal real time.");
                Assert.AreEqual(travelled[0] * 1440f, travelled[2], 0.5f,
                    "3x Transform travel must match the clock's 1,440x game-time rate in equal real time.");

                Assert.That(roots[0].transform.position.x, Is.EqualTo(travelled[0]).Within(0.0001f));
                Assert.That(roots[1].transform.position.x, Is.EqualTo(travelled[1]).Within(0.0001f));
                Assert.That(roots[2].transform.position.x, Is.EqualTo(travelled[2]).Within(0.0001f));
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                for (int index = 0; index < roots.Length; index++)
                    if (roots[index] != null) UnityEngine.Object.DestroyImmediate(roots[index]);
            }
        }

        [TestCase(1f, 2f)]
        [TestCase(2f, 3f)]
        [TestCase(3f, 1f)]
        public void RatWorldTransformTakesSpeedChangesImmediately(float startingSpeed, float nextSpeed)
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 1f / 60f;
            const int framesPerSegment = 12;
            const long startTimestamp = 5000000L;
            Vector3 target = new Vector3(10000f, 0f, 0f);
            GameObject root = new GameObject("speed transition rat root");
            ColonySaveData save = ColonyFactory.CreateNew(startTimestamp);
            save.clock.gameTimeMs = GameConfig.StartGameTimeMs;
            save.clock.lastRealTimestamp = startTimestamp;
            save.clock.speed = startingSpeed;

            try
            {
                GrowthSystem.SetSimulationPaused(false);
                GrowthSystem.SetRuntimeSpeed(startingSpeed);
                float startingX = root.transform.position.x;
                for (int frame = 0; frame < framesPerSegment; frame++)
                    ApplyWorldMovementFrame(root.transform, target, baseWorldSpeed, realFrameSeconds);
                float firstSegmentDistance = root.transform.position.x - startingX;
                float startingMultiplier = GrowthSystem.SimulationMultiplierForSpeed(startingSpeed);

                long switchTimestamp = startTimestamp + (long)Math.Round(
                    framesPerSegment * realFrameSeconds * 1000f);
                Assert.IsTrue(GrowthSystem.ChangeSpeedAtTimestamp(
                    save, nextSpeed, switchTimestamp));
                Vector3 positionAtSwitch = root.transform.position;
                Assert.AreEqual(startingX + firstSegmentDistance, positionAtSwitch.x, 0.0001f,
                    "Changing speed must not move or reset the rat at the switch boundary.");
                for (int frame = 0; frame < framesPerSegment; frame++)
                {
                    Vector3 previous = root.transform.position;
                    ApplyWorldMovementFrame(root.transform, target, baseWorldSpeed, realFrameSeconds);
                    Assert.LessOrEqual(
                        Vector3.Distance(previous, root.transform.position),
                        baseWorldSpeed * GrowthSystem.SimulationMultiplierForSpeed(nextSpeed) * realFrameSeconds + 0.0001f,
                        "A speed change must not teleport the rat or apply a multiplier more than once.");
                }

                float nextSegmentDistance = root.transform.position.x - positionAtSwitch.x;
                Assert.AreEqual(baseWorldSpeed * startingMultiplier * framesPerSegment * realFrameSeconds,
                    firstSegmentDistance, 0.0001f, "The first segment must use only its selected speed.");
                Assert.AreEqual(baseWorldSpeed * GrowthSystem.SimulationMultiplierForSpeed(nextSpeed) * framesPerSegment * realFrameSeconds,
                    nextSegmentDistance, 0.0001f, "The next segment must use the new speed immediately.");
                Assert.Less(root.transform.position.x, target.x,
                    "The rat must continue toward the same distant world-space target after switching speeds.");
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(1f)]
        [TestCase(2f)]
        [TestCase(3f)]
        public void SlowRenderedFramesRetainFullMovementTime(float speed)
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 0.25f;
            GameObject root = new GameObject("slow-frame rat root");
            Vector3 target = new Vector3(10000f, 0f, 0f);

            try
            {
                GrowthSystem.SetSimulationPaused(false);
                GrowthSystem.SetRuntimeSpeed(speed);
                ApplyWorldMovementFrame(root.transform, target, baseWorldSpeed, realFrameSeconds);

                Assert.AreEqual(baseWorldSpeed * GrowthSystem.SimulationMultiplierForSpeed(speed) * realFrameSeconds,
                    root.transform.position.x, 0.0001f,
                    "A slow WebGL frame must not discard movement time or weaken the selected speed multiplier.");
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WorldMovementRetainsSpeedWhenBehaviorWorkIsCapped()
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 1.2f;
            Vector3 target = new Vector3(10000f, 0f, 0f);
            GameObject[] roots = new GameObject[3];
            float[] distances = new float[3];

            try
            {
                GrowthSystem.SetSimulationPaused(false);
                GrowthSystem.SetBehaviorParticipantCount(20);
                for (int speedIndex = 0; speedIndex < roots.Length; speedIndex++)
                {
                    roots[speedIndex] = new GameObject("capped behavior movement " + (speedIndex + 1) + "x");
                    GrowthSystem.SetRuntimeSpeed(speedIndex + 1f);
                    float simulationDelta = GrowthSystem.SimulationMovementDeltaSeconds(realFrameSeconds);
                    int behaviorSteps = GrowthSystem.BeginBehaviorUpdate(simulationDelta);
                    float movementStepDelta = simulationDelta / behaviorSteps;
                    float movementBudget = GrowthSystem.SimulationMovementTimeBudget(simulationDelta);

                    Assert.LessOrEqual(behaviorSteps, 3,
                        "Twenty rats must remain within the shared 64-step behavior-work cap.");
                    for (int step = 0; step < behaviorSteps; step++)
                    {
                        Vector3 previous = roots[speedIndex].transform.position;
                        roots[speedIndex].transform.position = GrowthSystem.SimulationMovementTargetPosition(
                            previous, target, baseWorldSpeed, movementStepDelta, ref movementBudget);
                    }
                    distances[speedIndex] = roots[speedIndex].transform.position.x;
                    Assert.AreEqual(baseWorldSpeed * simulationDelta, distances[speedIndex], 0.0001f,
                        "The bounded decision-step count must not discard any current-frame world movement time.");
                }

                Assert.AreEqual(distances[0] * 60f, distances[1], 0.02f,
                    "Work-capped 2x movement must retain the 60x clock rate.");
                Assert.AreEqual(distances[0] * 1440f, distances[2], 0.5f,
                    "Work-capped 3x movement must retain the 1,440x clock rate.");
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
                GrowthSystem.SetBehaviorParticipantCount(1);
                for (int index = 0; index < roots.Length; index++)
                    if (roots[index] != null) UnityEngine.Object.DestroyImmediate(roots[index]);
            }
        }

        private static void ApplyWorldMovementFrame(
            Transform root,
            Vector3 target,
            float baseWorldSpeed,
            float realDeltaSeconds)
        {
            float simulationDelta = GrowthSystem.SimulationMovementDeltaSeconds(realDeltaSeconds);
            float movementBudget = GrowthSystem.SimulationMovementTimeBudget(simulationDelta);
            root.position = GrowthSystem.SimulationMovementTargetPosition(
                root.position, target, baseWorldSpeed, simulationDelta, ref movementBudget);
        }

        [Test]
        public void MovementSpeedTransitionsRemainLinearAndDoNotCompound()
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 1f / 60f;
            const int framesPerSegment = 60;
            const long startingTimestamp = 2000000L;
            float[] segmentSpeeds = { 1f, 2f, 3f, 1f };
            float totalDistance = 0f;
            ColonySaveData save = ColonyFactory.CreateNew(startingTimestamp);
            save.clock.gameTimeMs = GameConfig.StartGameTimeMs;
            save.clock.lastRealTimestamp = startingTimestamp;
            save.clock.speed = 1f;

            try
            {
                GrowthSystem.SetSimulationPaused(false);
                for (int segment = 0; segment < segmentSpeeds.Length; segment++)
                {
                    Assert.AreEqual(
                        segment > 0,
                        GrowthSystem.ChangeSpeedAtTimestamp(
                            save, segmentSpeeds[segment], startingTimestamp + segment * 1000L),
                        "The active simulation rate must change only at the timestamp boundary.");
                    float segmentDistance = 0f;
                    for (int frame = 0; frame < framesPerSegment; frame++)
                    {
                        segmentDistance += GrowthSystem.SimulationMovementStep(
                            baseWorldSpeed, realFrameSeconds);
                    }

                    float multiplier = GrowthSystem.SimulationMultiplierForSpeed(segmentSpeeds[segment]);
                    Assert.AreEqual(
                        baseWorldSpeed * multiplier, segmentDistance, 0.0001f,
                        "A speed transition must affect only the current interval and must not compound movement speed.");
                    totalDistance += segmentDistance;
                }

                Assert.AreEqual(baseWorldSpeed * 1502f, totalDistance, 0.001f,
                    "One real second at 1x, 2x, 3x, then 1x must total 1,502 baseline-seconds of travel.");
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
            }
        }

        [Test]
        public void CatchUpBehaviorCannotSpendOldSimulationTimeAsMovement()
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 1f / 60f;
            float[] measuredFrameDistances = new float[3];

            try
            {
                GrowthSystem.SetSimulationPaused(false);
                for (int speedIndex = 0; speedIndex < measuredFrameDistances.Length; speedIndex++)
                {
                    GrowthSystem.SetRuntimeSpeed(speedIndex + 1f);
                    float currentFrameSimulationDelta =
                        GrowthSystem.SimulationBehaviorDeltaSeconds(realFrameSeconds);
                    float movementBudget = GrowthSystem.SimulationMovementTimeBudget(
                        currentFrameSimulationDelta);

                    // Simulate stale catch-up steps from earlier frames. They
                    // must share only this frame's budget, not each add more
                    // movement time to it.
                    for (int catchUpStep = 0; catchUpStep < 12; catchUpStep++)
                    {
                        measuredFrameDistances[speedIndex] += GrowthSystem.SimulationMovementStep(
                            baseWorldSpeed, currentFrameSimulationDelta, ref movementBudget);
                    }

                    Assert.AreEqual(
                        baseWorldSpeed * currentFrameSimulationDelta,
                        measuredFrameDistances[speedIndex], 0.0001f,
                        "Backlog steps must not produce movement beyond the current frame's scaled delta.");
                }

                Assert.AreEqual(measuredFrameDistances[0] * 60f, measuredFrameDistances[1], 0.0001f);
                Assert.AreEqual(measuredFrameDistances[0] * 1440f, measuredFrameDistances[2], 0.001f);

                GrowthSystem.SetRuntimeSpeed(3f);
                float longFrameSimulationDelta = GrowthSystem.SimulationBehaviorDeltaSeconds(1f);
                float longFrameBudget = GrowthSystem.SimulationMovementTimeBudget(longFrameSimulationDelta);
                float longFrameDistance = 0f;
                for (int catchUpStep = 0; catchUpStep < 12; catchUpStep++)
                {
                    longFrameDistance += GrowthSystem.SimulationMovementStep(
                        baseWorldSpeed, longFrameSimulationDelta, ref longFrameBudget);
                }
                Assert.AreEqual(
                    baseWorldSpeed * longFrameSimulationDelta,
                    longFrameDistance, 0.0001f,
                    "A long frame uses its full selected-speed movement interval exactly once.");
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
            }
        }

        [Test]
        public void ChangingSimulationSpeedSettlesClockAtPreviousRate()
        {
            const long startingTimestamp = 1000000L;
            ColonySaveData save = ColonyFactory.CreateNew(startingTimestamp);
            save.clock.gameTimeMs = GameConfig.StartGameTimeMs;
            save.clock.lastRealTimestamp = startingTimestamp;
            save.clock.speed = 1f;

            try
            {
                GrowthSystem.SetSimulationPaused(false);
                GrowthSystem.SetRuntimeSpeed(1f);
                Assert.IsTrue(GrowthSystem.ChangeSpeedAtTimestamp(save, 2f, startingTimestamp + 1000L));
                Assert.AreEqual(GameConfig.StartGameTimeMs + 60000L, save.clock.gameTimeMs,
                    "The first interval must be accrued using the old 1x clock rate.");

                Assert.IsTrue(GrowthSystem.ChangeSpeedAtTimestamp(save, 3f, startingTimestamp + 2000L));
                Assert.AreEqual(GameConfig.StartGameTimeMs + 3660000L, save.clock.gameTimeMs,
                    "The next interval must be accrued using the old 2x clock rate.");

                Assert.IsTrue(GrowthSystem.ChangeSpeedAtTimestamp(save, 1f, startingTimestamp + 3000L));
                Assert.AreEqual(GameConfig.StartGameTimeMs + 90060000L, save.clock.gameTimeMs,
                    "The next interval must be accrued using the old 3x clock rate.");

                GrowthSystem.AdvanceClock(save, startingTimestamp + 4000L);
                Assert.AreEqual(GameConfig.StartGameTimeMs + 90120000L, save.clock.gameTimeMs,
                    "After returning to 1x, the next interval must use 1x and no prior segment is rescaled.");
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
            }
        }

        [Test]
        public void ConceptionChanceKeepsLowFertilityViableWithoutChangingHabitatMaximums()
        {
            const long gameTime = 700000000L;
            float[] fertilityValues = { 0f, 1f, 5f, 15f, 50f, 100f };
            float previousPairingChance = -1f;
            float previousDedicatedChance = -1f;

            foreach (float fertility in fertilityValues)
            {
                var save = CreatePairingTestSave(gameTime, fertility);
                RatData female = save.rats[0];
                RatData male = save.rats[1];
                float pairingChance = BreedingSystem.CalculateConceptionChance(
                    female, male, GameConfig.PairingPregnancyChance, 0f, 0.20f);
                float dedicatedChance = BreedingSystem.CalculateConceptionChance(
                    female, male, GameConfig.PairingPregnancyChance,
                    GameConfig.DedicatedBreedingSuccessBonus,
                    GameConfig.DedicatedBreedingSuccessCap);

                Assert.GreaterOrEqual(pairingChance, previousPairingChance);
                Assert.GreaterOrEqual(dedicatedChance, previousDedicatedChance);
                previousPairingChance = pairingChance;
                previousDedicatedChance = dedicatedChance;

                if (fertility <= 0f)
                {
                    Assert.AreEqual(0f, pairingChance, 0.000001f);
                    Assert.AreEqual(0f, dedicatedChance, 0.000001f);
                }
                if (Mathf.Approximately(fertility, 1f))
                {
                    Assert.That(pairingChance * 100f, Is.InRange(0.9f, 1.3f));
                    Assert.That(dedicatedChance * 100f, Is.InRange(2.0f, 2.9f));
                    StringAssert.AreEqualIgnoringCase(
                        "1.1%", BreedingSystem.ConceptionChanceLabel(
                            female, male, GameConfig.PairingPregnancyChance, 0f, 0.20f));
                }
                if (Mathf.Approximately(fertility, 100f))
                {
                    Assert.AreEqual(0.20f, pairingChance, 0.000001f);
                    Assert.AreEqual(0.45f, dedicatedChance, 0.000001f);
                }
            }
        }

        [Test]
        public void BreedingAgeDeclineStartsAtOneYearAndReachesZeroAtIndividualCutoff()
        {
            const long gameTime = 700000000L;
            var save = CreatePairingTestSave(gameTime, 100f);
            RatData female = save.rats[0];
            RatData male = save.rats[1];
            female.breedingEndAgeDays = 730f;
            male.breedingEndAgeDays = 730f;

            female.ageDays = 364f;
            male.ageDays = 364f;
            float beforeDecline = BreedingSystem.CalculateConceptionChance(
                female, male, GameConfig.PairingPregnancyChance, 0f, 0.20f);
            Assert.AreEqual(0.20f, beforeDecline, 0.000001f);

            female.ageDays = 365f;
            male.ageDays = 365f;
            float atDeclineStart = BreedingSystem.CalculateConceptionChance(
                female, male, GameConfig.PairingPregnancyChance, 0f, 0.20f);
            Assert.AreEqual(beforeDecline, atDeclineStart, 0.000001f);
            Assert.AreEqual(1f, BreedingSystem.AgeBreedingEffectiveness(female), 0.000001f);
            female.stage = RatStage.Senior;
            female.reproductiveState = ReproductiveState.Fertile;
            female.estrousCycleAnchorGameTime = gameTime;
            StringAssert.DoesNotContain("age-related decline",
                BreedingSystem.ReproductiveStateLabel(save, female, gameTime));

            female.ageDays = 547.5f;
            male.ageDays = 547.5f;
            float midpoint = BreedingSystem.CalculateConceptionChance(
                female, male, GameConfig.PairingPregnancyChance, 0f, 0.20f);
            Assert.Less(midpoint, atDeclineStart);
            Assert.Greater(midpoint, 0f);
            Assert.That(BreedingSystem.AgeBreedingEffectiveness(female), Is.InRange(0.49f, 0.51f));
            StringAssert.Contains("age-related decline",
                BreedingSystem.ReproductiveStateLabel(save, female, gameTime));

            female.ageDays = 365f;
            male.ageDays = 365f;
            int fullMinimum;
            int fullMaximum;
            BreedingSystem.GetLitterSizeRange(female, male, out fullMinimum, out fullMaximum);
            female.ageDays = 547.5f;
            male.ageDays = 547.5f;
            int midpointMinimum;
            int midpointMaximum;
            BreedingSystem.GetLitterSizeRange(female, male, out midpointMinimum, out midpointMaximum);
            female.ageDays = 729f;
            male.ageDays = 729f;
            int lateMinimum;
            int lateMaximum;
            BreedingSystem.GetLitterSizeRange(female, male, out lateMinimum, out lateMaximum);
            Assert.GreaterOrEqual(fullMaximum, midpointMaximum);
            Assert.GreaterOrEqual(midpointMaximum, lateMaximum);
            Assert.GreaterOrEqual(lateMinimum, 1);

            female.ageDays = 729f;
            male.ageDays = 729f;
            Assert.Greater(BreedingSystem.AgeBreedingEffectiveness(female), 0f);
            female.ageDays = 730f;
            male.ageDays = 730f;
            Assert.AreEqual(0f, BreedingSystem.AgeBreedingEffectiveness(female), 0.000001f);
            Assert.AreEqual(0f, BreedingSystem.CalculateConceptionChance(
                female, male, GameConfig.PairingPregnancyChance, 0f, 0.20f), 0.000001f);

            string reason;
            // Eligibility uses birthTimestamp as the authoritative age, so
            // synchronize the fixture timestamp with the 730-day cutoff
            // instead of relying on the display/cache ageDays field alone.
            female.birthTimestamp = gameTime - (long)(female.ageDays * GameConfig.GameDayMs);
            male.birthTimestamp = gameTime - (long)(male.ageDays * GameConfig.GameDayMs);
            Assert.IsFalse(BreedingSystem.IsBreedEligible(save, female, gameTime, out reason));
            Assert.AreEqual("Past breeding age", BreedingSystem.ReproductiveStateLabel(save, female, gameTime));
        }

        [Test]
        public void LegacyBreedingEndAgesAreExtendedOnceDuringSaveMigration()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            save.schemaVersion = GameConfig.SaveVersion - 1;
            save.rats[0].breedingEndAgeDays = 270f;
            save.rats[1].breedingEndAgeDays = 365f;

            Assert.IsTrue(ColonyFactory.MigrateLegacyStarterStats(save));
            Assert.AreEqual(635f, save.rats[0].breedingEndAgeDays, 0.0001f);
            Assert.AreEqual(730f, save.rats[1].breedingEndAgeDays, 0.0001f);
            Assert.AreEqual(GameConfig.SaveVersion, save.schemaVersion);
            save.rats[1].ageDays = 400f;
            save.rats[1].stage = RatStage.Senior;
            save.rats[1].reproductiveState = ReproductiveState.Infertile;
            save.rats[1].breedingCooldownUntil = 0L;
            string reason;
            Assert.IsTrue(BreedingSystem.IsBreedEligible(save, save.rats[1], save.clock.gameTimeMs, out reason), reason);
            Assert.AreEqual("Fertile — age-related decline",
                BreedingSystem.ReproductiveStateLabel(save, save.rats[1], save.clock.gameTimeMs));
            Assert.IsFalse(ColonyFactory.MigrateLegacyStarterStats(save));
            Assert.AreEqual(635f, save.rats[0].breedingEndAgeDays, 0.0001f);
            Assert.AreEqual(730f, save.rats[1].breedingEndAgeDays, 0.0001f);
        }

        [Test]
        public void ReproductiveLabelsAndEligibilityShareSexualMaturityRules()
        {
            const long birthTime = 0L;
            long maturityTime = (long)(GameConfig.FemaleSexualMaturityDays * GameConfig.GameDayMs);
            var save = ColonyFactory.CreateNew(maturityTime);
            var female = ColonyFactory.CreateRat(
                "maturity-test-female", "Maturity Test", RatSex.Female,
                birthTime, 0, save.rats[0].genotype.Clone(),
                new TraitData(50f, 50f, 50f), RatStage.Adult);
            female.ageDays = GameConfig.FemaleSexualMaturityDays - 1f;
            female.sexualMaturityDays = GameConfig.FemaleSexualMaturityDays;
            female.breedingEndAgeDays = 365f;
            female.estrousCycleAnchorGameTime = maturityTime;
            female.reproductiveState = ReproductiveState.Fertile;
            save.rats.Add(female);
            save.ratIds.Add(female.id);

            string reason;
            Assert.IsFalse(BreedingSystem.IsBreedEligible(save, female, maturityTime - GameConfig.GameDayMs, out reason));
            StringAssert.StartsWith("Immature — breeding available in 1 days, 0 hours",
                BreedingSystem.ReproductiveStateLabel(save, female, maturityTime - GameConfig.GameDayMs));
            StringAssert.StartsWith("Immature — breeding available", reason);

            female.ageDays = GameConfig.FemaleSexualMaturityDays;
            Assert.IsTrue(BreedingSystem.IsBreedEligible(save, female, maturityTime, out reason));
            StringAssert.StartsWith("Fertile", BreedingSystem.ReproductiveStateLabel(save, female, maturityTime));
            Assert.IsTrue(string.IsNullOrEmpty(reason));

            long outsideWindowTime = maturityTime + (2L * GameConfig.GameDayMs);
            female.ageDays = GameConfig.FemaleSexualMaturityDays + 2f;
            Assert.IsFalse(BreedingSystem.IsBreedEligible(save, female, outsideWindowTime, out reason));
            StringAssert.StartsWith("Next fertile window in",
                BreedingSystem.ReproductiveStateLabel(save, female, outsideWindowTime));
            StringAssert.Contains("Outside the fertile window", reason);

            save.pregnancies.Add(new PregnancyData
            {
                id = "maturity-test-pregnancy",
                motherId = female.id,
                fatherId = save.rats[1].id,
                startedAt = outsideWindowTime,
                dueAt = outsideWindowTime + GameConfig.PregnancyMs,
                status = "pending",
            });
            Assert.IsFalse(BreedingSystem.IsBreedEligible(save, female, outsideWindowTime, out reason));
            StringAssert.StartsWith("Pregnant", BreedingSystem.ReproductiveStateLabel(save, female, outsideWindowTime));
            Assert.AreEqual("Currently pregnant.", reason);

            save.pregnancies.Clear();
            female.ageDays = 400f;
            female.birthTimestamp = outsideWindowTime - (long)(female.ageDays * GameConfig.GameDayMs);
            female.stage = RatStage.Senior;
            female.reproductiveState = ReproductiveState.Infertile;
            Assert.IsFalse(BreedingSystem.IsBreedEligible(save, female, outsideWindowTime, out reason));
            Assert.AreEqual("Past breeding age", BreedingSystem.ReproductiveStateLabel(save, female, outsideWindowTime));
            Assert.AreEqual("Past breeding age.", reason);
        }

        [Test]
        public void PairingResolutionUsesLiveWindowAndCommittedInteractionCanFinish()
        {
            const long gameTime = 700000000L;
            var outsideSave = CreatePairingTestSave(gameTime, 100f);
            RatData outsideFemale = outsideSave.rats[0];
            RatData outsideMale = outsideSave.rats[1];
            long outsideWindow = gameTime +
                (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs) + 1L;

            bool conceived;
            string reason;
            Assert.IsFalse(PairingHabitatSystem.ResolvePair(
                outsideSave, outsideFemale, outsideMale, outsideWindow, 1f,
                out conceived, out reason));
            Assert.IsFalse(conceived);
            Assert.IsNull(BreedingSystem.FindPendingPregnancyForMother(outsideSave, outsideFemale.id));
            StringAssert.Contains("Outside the fertile window", reason);

            var committedSave = CreatePairingTestSave(gameTime, 100f);
            RatData committedFemale = committedSave.rats[0];
            RatData committedMale = committedSave.rats[1];
            Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                committedSave, committedFemale, committedMale, outsideWindow, 1f, true,
                out conceived, out reason), reason);
            Assert.IsTrue(conceived);
            Assert.IsNotNull(BreedingSystem.FindPendingPregnancyForMother(
                committedSave, committedFemale.id));
        }

        [Test]
        public void DedicatedSessionCompletesAfterWindowCloses()
        {
            const long gameTime = 710000000L;
            var save = CreatePairingTestSave(gameTime, 100f);
            RatData female = save.rats[0];
            RatData male = save.rats[1];
            DedicatedBreedingSessionData session;
            string reason;

            Assert.IsTrue(BreedingSystem.StartDedicatedBreedingSession(
                save, female, male, gameTime, out session, out reason), reason);
            long completionTime = session.endsAt;
            long fertileWindowMs = (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs);
            long sessionDurationMs = completionTime - gameTime;
            // Start near the end of the live window so this committed
            // two-hour session crosses it before resolution.
            female.estrousCycleAnchorGameTime = gameTime -
                (fertileWindowMs - (sessionDurationMs / 2L));
            Assert.IsTrue(BreedingSystem.IsInFertileWindow(female, gameTime));
            Assert.IsFalse(BreedingSystem.IsInFertileWindow(female, completionTime));

            List<DedicatedBreedingSessionData> resolved;
            Assert.AreEqual(1, BreedingSystem.ResolveDueDedicatedBreedingSessions(
                save, completionTime, out resolved));
            Assert.AreEqual("finished", session.status);
            Assert.AreEqual(1, resolved.Count);
            // Conception remains stochastic; the important regression guard is
            // that the committed session resolves after the window closes
            // instead of being rejected as a new outside-window attempt.
        }

        [Test]
        public void StoreQualityUpgradesUsePersistedFivePointStepsForFutureStock()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            Assert.AreEqual(15, UpgradeSystem.StoreQualityCap(save));
            Assert.AreEqual(2, UpgradeSystem.StoreListingCount(save));
            Assert.AreEqual(2, save.storeRatListings.Count);
            var originalListings = new List<StoreRatListingData>();
            foreach (var listing in save.storeRatListings)
            {
                originalListings.Add(new StoreRatListingData
                {
                    id = listing.id,
                    traits = new TraitData(listing.traits.size, listing.traits.health, listing.traits.fertility),
                });
                Assert.LessOrEqual(listing.traits.size, 15f);
                Assert.LessOrEqual(listing.traits.health, 15f);
                Assert.LessOrEqual(listing.traits.fertility, 15f);
            }

            save.colonyCredits = 10000;
            int nextCap;
            Assert.IsTrue(UpgradeSystem.PurchaseStoreQualityUpgrade(save, out nextCap));
            Assert.AreEqual(20, nextCap);
            Assert.AreEqual(3, UpgradeSystem.StoreListingCount(save));
            Assert.AreEqual(2, save.storeRatListings.Count,
                "Buying an upgrade must not delete, reroll, or add listings before restock.");
            for (int index = 0; index < originalListings.Count; index++)
            {
                StoreRatListingData current = save.storeRatListings[index];
                StoreRatListingData original = originalListings[index];
                Assert.AreEqual(original.traits.size, current.traits.size);
                Assert.AreEqual(original.traits.health, current.traits.health);
                Assert.AreEqual(original.traits.fertility, current.traits.fertility);
            }

            StoreSystem.RestockNow(save, save.clock.gameTimeMs);
            Assert.AreEqual(3, save.storeRatListings.Count,
                "The first store upgrade adds exactly one listing on restock.");
            foreach (var listing in save.storeRatListings)
            {
                Assert.LessOrEqual(listing.traits.size, 20f);
                Assert.LessOrEqual(listing.traits.health, 20f);
                Assert.LessOrEqual(listing.traits.fertility, 20f);
            }

            save.colonyCredits = 10000;
            Assert.IsTrue(UpgradeSystem.PurchaseStoreQualityUpgrade(save, out nextCap));
            Assert.AreEqual(4, UpgradeSystem.StoreListingCount(save));
            Assert.AreEqual(3, save.storeRatListings.Count,
                "The second upgrade leaves current stock untouched.");
            string json = SaveSystem.ToJson(save);
            ColonySaveData loaded = SaveSystem.FromJson(json);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(2, loaded.storeQualityUpgradeLevel);
            Assert.AreEqual(4, UpgradeSystem.StoreListingCount(loaded));
            Assert.AreEqual(3, loaded.storeRatListings.Count,
                "Save/load must preserve existing listings until the next restock.");
            StoreSystem.RestockNow(loaded, loaded.clock.gameTimeMs);
            Assert.AreEqual(4, loaded.storeRatListings.Count,
                "The second persisted upgrade adds exactly one more listing.");

            ColonySaveData legacy = SaveSystem.FromJson("{\"storeInventoryInitialized\":true}");
            Assert.IsNotNull(legacy);
            Assert.AreEqual(0, legacy.storeQualityUpgradeLevel,
                "A legacy save without an upgrade field migrates to the base level.");
            Assert.AreEqual(2, UpgradeSystem.StoreListingCount(legacy));
        }

        [Test]
        public void RestockAutomaticallySellsOnlyEligibleForSaleRatsOnceAndPreservesLineage()
        {
            const long now = 900000000L;
            ColonySaveData save = ColonyFactory.CreateNew(now);
            RatData eligible = CreateAutoSaleTestRat(save, "auto-sale-eligible", "Sale Rat", RatSex.Male, 120f);
            eligible.motherId = "historical-mother";
            eligible.fatherId = "historical-father";
            eligible.litterId = "historical-litter";
            save.litters.Add(new LitterData
            {
                id = "historical-litter",
                motherId = eligible.motherId,
                fatherId = eligible.fatherId,
                size = 1,
                pupIds = new List<string> { eligible.id },
            });

            RatData pregnant = CreateAutoSaleTestRat(save, "auto-sale-pregnant", "Pregnant Rat", RatSex.Female, 120f);
            pregnant.reproductiveState = ReproductiveState.Pregnant;
            pregnant.pregnancyId = "auto-sale-pregnancy";
            save.pregnancies.Add(new PregnancyData
            {
                id = pregnant.pregnancyId,
                motherId = pregnant.id,
                fatherId = save.rats[1].id,
                startedAt = now,
                dueAt = now + GameConfig.PregnancyMs,
                gestationDurationMs = GameConfig.PregnancyMs,
                status = "pending",
                expectedLitterSize = 3,
            });

            RatData tooYoung = CreateAutoSaleTestRat(save, "auto-sale-too-young", "Young Rat", RatSex.Male, 12f);
            RatData nursing = CreateAutoSaleTestRat(save, "auto-sale-nursing", "Nursing Rat", RatSex.Female, 120f);
            nursing.nursing = true;
            RatData dependentPinkie = ColonyFactory.CreateRat(
                "auto-sale-dependent-pinkie", "Dependent Pinkie", RatSex.Female,
                save.clock.gameTimeMs, 2, nursing.genotype.Clone(), new TraitData(2f, 2f, 2f), RatStage.Pinkie);
            dependentPinkie.motherId = nursing.id;
            dependentPinkie.enclosure = RatEnclosure.ForSale;
            save.rats.Add(dependentPinkie);
            save.ratIds.Add(dependentPinkie.id);
            RatData pairingAssigned = CreateAutoSaleTestRat(
                save, "auto-sale-pairing-assigned", "Pairing Family Rat", RatSex.Female, 120f);
            pairingAssigned.pairingHabitatAssigned = true;
            int walletBefore = save.colonyCredits;
            int expectedSale = StoreSystem.CalculateSaleValue(save, eligible, now);
            Assert.Greater(expectedSale, 0);

            StoreRestockResult result = StoreSystem.RestockNowWithResult(save, now);
            Assert.AreEqual(1, result.soldRatCount);
            Assert.AreEqual(expectedSale, result.creditedDollars);
            Assert.AreEqual(walletBefore + expectedSale, save.colonyCredits);
            Assert.AreEqual(expectedSale, save.lifetimeSaleCredits);
            Assert.IsFalse(save.rats.Contains(eligible));
            Assert.AreSame(eligible, save.retiredRats.Find(rat => rat.id == eligible.id));
            Assert.AreEqual(RatRemovalDisposition.Sold, eligible.removalDisposition);
            Assert.AreEqual("historical-mother", eligible.motherId);
            Assert.AreEqual("historical-father", eligible.fatherId);
            Assert.AreEqual("historical-litter", eligible.litterId);
            Assert.IsTrue(save.litters[0].pupIds.Contains(eligible.id));
            Assert.IsTrue(save.rats.Contains(pregnant), "A pending pregnancy must not be auto-sold.");
            Assert.IsTrue(save.rats.Contains(tooYoung), "An age-ineligible rat must remain in the tank.");
            Assert.IsTrue(save.rats.Contains(nursing), "A mother with dependent pinkies must remain in the tank.");
            Assert.IsTrue(save.rats.Contains(dependentPinkie), "An ineligible pinkie must remain in the tank.");
            Assert.IsTrue(save.rats.Contains(pairingAssigned), "A Pairing-assigned family member must never be auto-sold.");
            Assert.AreEqual(5, result.skippedRatCount);
            StringAssert.Contains("automatically sold for $" + expectedSale, result.autoSaleMessage);
            StringAssert.Contains("Pregnant Rat", result.autoSaleMessage);
            StringAssert.Contains("(pregnant)", result.autoSaleMessage);
            StringAssert.Contains("Young Rat", result.autoSaleMessage);
            StringAssert.Contains("Too young to sell", result.autoSaleMessage);
            StringAssert.Contains("Nursing Rat", result.autoSaleMessage);
            StringAssert.Contains("nursing with dependent pinkies", result.autoSaleMessage);
            StringAssert.Contains("Pairing Family Rat", result.autoSaleMessage);
            StringAssert.Contains("assigned to Pairing Habitat", result.autoSaleMessage);
            Assert.AreEqual(EventLogPolicy.Sale, save.eventLog[0].category);

            int walletAfterSale = save.colonyCredits;
            StoreRestockResult repeated = StoreSystem.RestockNowWithResult(save, now + GameConfig.GameDayMs);
            Assert.AreEqual(0, repeated.soldRatCount);
            Assert.AreEqual(walletAfterSale, save.colonyCredits,
                "Repeated restock processing must not pay for an already-retired rat again.");
            Assert.AreEqual(1, save.retiredRats.FindAll(rat => rat.id == eligible.id).Count);

            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            Assert.IsNotNull(loaded);
            RatData loadedHistory = loaded.retiredRats.Find(rat => rat.id == eligible.id);
            Assert.IsNotNull(loadedHistory);
            Assert.AreEqual("historical-mother", loadedHistory.motherId);
            Assert.AreEqual("historical-father", loadedHistory.fatherId);
            Assert.AreEqual("historical-litter", loadedHistory.litterId);
            Assert.IsTrue(loaded.litters[0].pupIds.Contains(eligible.id));
            int loadedWallet = loaded.colonyCredits;
            StoreSystem.RestockNow(loaded, loaded.clock.gameTimeMs + GameConfig.GameDayMs);
            Assert.AreEqual(loadedWallet, loaded.colonyCredits,
                "Save/load and a subsequent restock must not replay the payout.");
        }

        private static RatData CreateAutoSaleTestRat(
            ColonySaveData save, string id, string name, RatSex sex, float ageDays)
        {
            long gameTime = save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs;
            GenotypeData genotype = save.rats[0].genotype.Clone();
            RatData rat = ColonyFactory.CreateRat(id, name, sex,
                gameTime - (long)(ageDays * GameConfig.GameDayMs), 1,
                genotype, new TraitData(10f, 10f, 10f),
                ageDays < GameConfig.PupSaleMinimumAgeDays ? RatStage.YoungRat : RatStage.Adult);
            rat.ageDays = ageDays;
            rat.enclosure = RatEnclosure.ForSale;
            save.rats.Add(rat);
            save.ratIds.Add(id);
            return rat;
        }

        [Test]
        public void ColonyCapacityUpgradeUsesPersistedFiveRatSteps()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            Assert.AreEqual(GameConfig.BaseColonyCapacity, UpgradeSystem.ColonyCapacity(save));
            save.colonyCredits = 10000;
            int nextCapacity;
            Assert.IsTrue(UpgradeSystem.PurchaseColonyCapacityUpgrade(save, out nextCapacity));
            Assert.AreEqual(GameConfig.BaseColonyCapacity + GameConfig.ColonyCapacityUpgradeStep, nextCapacity);
            Assert.AreEqual(25, UpgradeSystem.ColonyCapacity(save));
        }

        [Test]
        public void PairingHabitatCapacityDefaultsToTenAndUpgradeAddsFiveWithEscalatingCost()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            Assert.AreEqual(10, UpgradeSystem.PairingHabitatCapacity(save));
            Assert.AreEqual(150, UpgradeSystem.PairingHabitatCapacityUpgradeCost(save));

            int originalRatCount = save.rats.Count;
            save.colonyCredits = 10000;
            int capacity;
            Assert.IsTrue(UpgradeSystem.PurchasePairingHabitatCapacityUpgrade(save, out capacity));
            Assert.AreEqual(15, capacity);
            Assert.AreEqual(250, UpgradeSystem.PairingHabitatCapacityUpgradeCost(save));
            Assert.IsTrue(UpgradeSystem.PurchasePairingHabitatCapacityUpgrade(save, out capacity));
            Assert.AreEqual(20, capacity);
            Assert.AreEqual(350, UpgradeSystem.PairingHabitatCapacityUpgradeCost(save));
            Assert.AreEqual(originalRatCount, save.rats.Count,
                "Increasing capacity must not remove or recreate current colony members.");
        }

        [Test]
        public void PairingHabitatCapacityUpgradeAndResidentsPersistThroughSaveLoad()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            save.colonyCredits = 10000;
            save.rats[0].enclosure = RatEnclosure.Pairing;
            save.rats[0].pairingHabitatAssigned = true;
            RatData pinkie = CreateSaleTestRat(save, "capacity-persist-pinkie", RatSex.Female,
                1f, RatStage.Pinkie);
            pinkie.enclosure = RatEnclosure.Pairing;
            pinkie.pairingHabitatAssigned = true;

            int capacity;
            Assert.IsTrue(UpgradeSystem.PurchasePairingHabitatCapacityUpgrade(save, out capacity));
            Assert.AreEqual(15, capacity);
            string json = SaveSystem.ToJson(save);
            ColonySaveData loaded = SaveSystem.FromJson(json);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.pairingHabitatCapacityUpgradeLevel);
            Assert.AreEqual(15, UpgradeSystem.PairingHabitatCapacity(loaded));
            Assert.AreEqual(save.rats.Count, loaded.rats.Count);
            Assert.IsNotNull(loaded.rats.Find(rat => rat.id == pinkie.id));
            Assert.AreEqual(RatEnclosure.Pairing, loaded.rats.Find(rat => rat.id == pinkie.id).enclosure);
            Assert.AreEqual(2, loaded.rats.FindAll(rat => rat.enclosure == RatEnclosure.Pairing).Count,
                "Both an adult and a pinkie occupy a persisted Pairing Habitat slot.");
        }

        [Test]
        public void LegacySaveWithoutPairingCapacityUpgradeMigratesWithoutLosingRats()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            RatData pinkie = CreateSaleTestRat(save, "legacy-capacity-pinkie", RatSex.Male,
                1f, RatStage.Pinkie);
            pinkie.enclosure = RatEnclosure.Pairing;
            pinkie.pairingHabitatAssigned = true;
            int originalRatCount = save.rats.Count;
            var originalIds = new HashSet<string>();
            foreach (RatData rat in save.rats) originalIds.Add(rat.id);

            string json = SaveSystem.ToJson(save);
            const string field = "\"pairingHabitatCapacityUpgradeLevel\"";
            int fieldIndex = json.IndexOf(field, StringComparison.Ordinal);
            Assert.GreaterOrEqual(fieldIndex, 0, "The current save must serialize its upgrade level.");
            int lineStart = json.LastIndexOf('\n', fieldIndex);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int lineEnd = json.IndexOf('\n', fieldIndex);
            if (lineEnd < 0) lineEnd = json.Length;
            string legacyJson = json.Remove(lineStart, lineEnd - lineStart);

            ColonySaveData migrated = SaveSystem.FromJson(legacyJson);
            Assert.IsNotNull(migrated);
            Assert.AreEqual(0, migrated.pairingHabitatCapacityUpgradeLevel);
            Assert.AreEqual(10, UpgradeSystem.PairingHabitatCapacity(migrated));
            Assert.AreEqual(originalRatCount, migrated.rats.Count);
            Assert.AreEqual(originalRatCount, migrated.ratIds.Count);
            Assert.IsNotNull(migrated.rats.Find(rat => rat.id == pinkie.id));
            foreach (RatData rat in migrated.rats)
                Assert.IsTrue(originalIds.Contains(rat.id), "Legacy migration must preserve every rat ID.");
        }

        [Test]
        public void StarterMigrationDoesNotReduceBredOffspringStats()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            var offspring = ColonyFactory.CreateRat(
                "offspring", "Offspring", RatSex.Female, 1000000L, 1,
                save.rats[0].genotype.Clone(), new TraitData(88f, 77f, 66f), RatStage.Adult);
            offspring.motherId = save.rats[0].id;
            offspring.fatherId = save.rats[1].id;
            save.rats.Add(offspring);
            save.schemaVersion = 2;

            Assert.IsTrue(ColonyFactory.MigrateLegacyStarterStats(save));
            Assert.AreEqual(88f, offspring.traits.size);
            Assert.AreEqual(77f, offspring.traits.health);
            Assert.AreEqual(66f, offspring.traits.fertility);
            Assert.AreEqual(GameConfig.SaveVersion, save.schemaVersion);
        }

        [Test]
        public void PreviewIsDeterministicAndDoesNotCreateRats()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            var first = save.rats[0];
            var second = save.rats[1];
            int before = save.rats.Count;
            var one = GeneticsSystem.BuildPreview(first, second);
            var two = GeneticsSystem.BuildPreview(first, second);
            Assert.AreEqual(before, save.rats.Count);
            Assert.AreEqual(one.loci.Count, two.loci.Count);
            Assert.AreEqual(one.furOutcomes.Count, two.furOutcomes.Count);
            for (int i = 0; i < one.loci.Count; i++)
            {
                Assert.AreEqual(one.loci[i].parentA, two.loci[i].parentA);
                Assert.AreEqual(one.loci[i].parentB, two.loci[i].parentB);
                Assert.AreEqual(one.loci[i].outcomes.Count, two.loci[i].outcomes.Count);
            }
        }

        [Test]
        public void MarkingMutationRateIsSeparateDeterministicAndShownInBreedingPreview()
        {
            Assert.AreEqual(0.05f, GameConfig.MarkingMutationRate, 0.000001f);
            Assert.AreEqual(0.0025f, GameConfig.MutationRate, 0.000001f);
            Assert.AreEqual(GameConfig.MarkingMutationRate, GeneticsSystem.MutationRateForLocus("S"));
            Assert.AreEqual(GameConfig.MutationRate, GeneticsSystem.MutationRateForLocus("B"));
            Assert.AreEqual(GameConfig.MutationRate, GeneticsSystem.MutationRateForLocus("C"));
            Assert.AreEqual(GameConfig.MutationRate, GeneticsSystem.MutationRateForLocus("D"));

            GenotypeData solidGenotype = GeneticsSystem.CreateFounder(
                "B", "B", "C", "C", "D", "D", "s", "s");
            RatData parentA = ColonyFactory.CreateRat(
                "mutation-preview-a", "Parent A", RatSex.Female, 1000000L, 1,
                solidGenotype.Clone(), new TraitData(10f, 10f, 10f), RatStage.Adult);
            RatData parentB = ColonyFactory.CreateRat(
                "mutation-preview-b", "Parent B", RatSex.Male, 1000000L, 1,
                solidGenotype.Clone(), new TraitData(10f, 10f, 10f), RatStage.Adult);
            GeneticsSystem.BreedingPreviewData preview = GeneticsSystem.BuildPreview(parentA, parentB);
            Assert.AreEqual(GameConfig.MarkingMutationRate, preview.sLocusMutationChance);
            Assert.AreEqual(GameConfig.MutationRate, preview.bcdMutationChance);

            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            try
            {
                int[] firstRun = CountSeededMutationRecords(solidGenotype, 12000, 481516);
                int[] repeatedRun = CountSeededMutationRecords(solidGenotype, 12000, 481516);
                CollectionAssert.AreEqual(firstRun, repeatedRun,
                    "The same random seed must reproduce the same inherited mutations.");

                // Each B/C/D locus has two inherited alleles at 0.25%; S has
                // two at 5%. These deterministic bounds distinguish the rates
                // without relying on an exact count from a random distribution.
                Assert.That(firstRun[0], Is.InRange(35, 85), "B-locus mutation count");
                Assert.That(firstRun[1], Is.InRange(35, 85), "C-locus mutation count");
                Assert.That(firstRun[2], Is.InRange(35, 85), "D-locus mutation count");
                Assert.That(firstRun[3], Is.InRange(1100, 1300), "S-locus mutation count");

                RatData solidMother = new RatData { id = "solid-mother", genotype = solidGenotype.Clone(), markingFamily = "Solid" };
                RatData solidFather = new RatData { id = "solid-father", genotype = solidGenotype.Clone(), markingFamily = "Self" };
                int markedPups = CountSeededSpontaneouslyMarkedOffspring(
                    solidMother, solidFather, 12000, 481516);
                Assert.That(markedPups, Is.InRange(1050, 1300),
                    "Solid s/s parents should produce visibly marked offspring at the configured non-guaranteed rate.");

                GenotypeData inheritedMarked = GeneticsSystem.CreateFounder(
                    "B", "B", "C", "C", "D", "D", "S", "S");
                RatData markedMother = new RatData { id = "marked-mother", genotype = inheritedMarked, markingFamily = "Blaze" };
                RatData markedFather = new RatData { id = "marked-father", genotype = inheritedMarked.Clone(), markingFamily = "Blaze" };
                Assert.AreEqual("Blaze", GeneticsSystem.ResolveOffspringMarkingFamily(
                    markedMother, markedFather, inheritedMarked.Clone(), "inherited-marking-test"),
                    "Existing marking inheritance must continue to preserve the parental marking family.");
            }
            finally
            {
                UnityEngine.Random.state = previousRandomState;
            }
        }

        private static int[] CountSeededMutationRecords(GenotypeData parents, int offspringCount, int seed)
        {
            var counts = new int[4];
            UnityEngine.Random.InitState(seed);
            for (int index = 0; index < offspringCount; index++)
            {
                GenotypeData offspring = GeneticsSystem.InheritGenotype(parents, parents, index + 1L);
                foreach (MutationRecordData mutation in offspring.mutations)
                {
                    if (mutation == null) continue;
                    int locusIndex = Array.IndexOf(GameConfig.Loci, mutation.locus);
                    if (locusIndex >= 0) counts[locusIndex]++;
                }
            }
            return counts;
        }

        private static int CountSeededSpontaneouslyMarkedOffspring(
            RatData mother, RatData father, int offspringCount, int seed)
        {
            int marked = 0;
            UnityEngine.Random.InitState(seed);
            for (int index = 0; index < offspringCount; index++)
            {
                GenotypeData child = GeneticsSystem.InheritGenotype(
                    mother.genotype, father.genotype, index + 1L);
                string family = GeneticsSystem.ResolveOffspringMarkingFamily(
                    mother, father, child, "solid-parent-pup-" + index);
                if (!string.Equals(family, "Solid", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(family, "Self", StringComparison.OrdinalIgnoreCase)) marked++;
            }
            return marked;
        }

        [Test]
        public void BreedingCreatesPinkiesWithHiddenFurAndLineage()
        {
            // The new-game founders are intentionally randomized and may be
            // outside the live fertile window. Use the explicit Pairing test
            // fixture here so this test exercises litter creation rather than
            // depending on a particular starter cycle phase.
            var save = CreatePairingTestSave(2000000L, 10f);
            PregnancyData pregnancy;
            string reason;
            Assert.IsTrue(BreedingSystem.StartBreeding(save, save.rats[0], save.rats[1], 2000000L, out pregnancy, out reason), reason);
            LitterData litter;
            Assert.IsTrue(BreedingSystem.FinishPregnancy(save, pregnancy.id, 2001000L, out litter, out reason), reason);
            Assert.GreaterOrEqual(litter.size, GameConfig.MinimumLitterSize);
            Assert.LessOrEqual(litter.size, GameConfig.MaximumLitterSize);
            foreach (var pupId in litter.pupIds)
            {
                RatData pup = BreedingSystem.FindRat(save, pupId);
                Assert.IsNotNull(pup);
                Assert.AreEqual(RatStage.Pinkie, pup.stage);
                Assert.AreEqual(litter.id, pup.litterId);
                Assert.AreEqual(litter.motherId, pup.motherId);
                Assert.AreEqual(litter.fatherId, pup.fatherId);
                Assert.IsFalse(pup.phenotype.furRevealed);
                Assert.AreEqual("Unknown", pup.phenotype.coatColorLabel);
                Assert.AreEqual(4, pup.genotype.loci.Count);
            }
        }

        [Test]
        public void PairingFailureAppliesPersistedCooldownBeforeRetry()
        {
            const long gameTime = 500000000L;
            var save = CreatePairingTestSave(gameTime, 80f);
            RatData female = save.rats[0];
            RatData male = save.rats[1];

            bool conceptionSucceeded;
            string reason;
            Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                save, female, male, gameTime, 0f, out conceptionSucceeded, out reason), reason);
            Assert.IsFalse(conceptionSucceeded);
            Assert.IsNull(BreedingSystem.FindPendingPregnancyForMother(save, female.id));
            Assert.AreEqual(gameTime + GameConfig.PairingAttemptCooldownMs, female.breedingCooldownUntil);
            Assert.AreEqual(gameTime + GameConfig.PairingAttemptCooldownMs, male.breedingCooldownUntil);

            RatData chosenMale;
            RatData chosenFemale;
            Assert.IsFalse(PairingHabitatSystem.TryChoosePair(save, gameTime + 1000L, out chosenMale, out chosenFemale));
            Assert.IsTrue(PairingHabitatSystem.TryChoosePair(
                save, gameTime + GameConfig.PairingAttemptCooldownMs + 1L, out chosenMale, out chosenFemale));
        }

        [Test]
        public void PairingSuccessCreatesOnePregnancyAndBlocksRepeatResolution()
        {
            const long gameTime = 600000000L;
            var save = CreatePairingTestSave(gameTime, 100f);
            RatData female = save.rats[0];
            RatData male = save.rats[1];

            bool conceptionSucceeded;
            string reason;
            Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                save, female, male, gameTime, 1f, out conceptionSucceeded, out reason), reason);
            Assert.IsTrue(conceptionSucceeded);
            PregnancyData pregnancy = BreedingSystem.FindPendingPregnancyForMother(save, female.id);
            Assert.IsNotNull(pregnancy);
            Assert.AreEqual(ReproductiveState.Pregnant, female.reproductiveState);

            bool secondSuccess;
            Assert.IsFalse(PairingHabitatSystem.ResolvePair(
                save, female, male, gameTime + GameConfig.PairingAttemptCooldownMs + 1L,
                1f, out secondSuccess, out reason));
            Assert.IsFalse(secondSuccess);
            Assert.AreEqual(pregnancy.id, BreedingSystem.FindPendingPregnancyForMother(save, female.id).id);
        }

        [Test]
        public void PregnancySortUsesAuthoritativeRecordsAndDueDateOrder()
        {
            const long gameTime = 700000000L;
            var save = ColonyFactory.CreateNew(gameTime);
            save.rats.Clear();
            save.ratIds.Clear();
            save.pregnancies.Clear();

            RatData dueSoon = CreatePregnancySortRat(save, "pregnant-soon", ReproductiveState.Pregnant, gameTime);
            RatData dueLater = CreatePregnancySortRat(save, "pregnant-later", ReproductiveState.Pregnant, gameTime);
            RatData fertile = CreatePregnancySortRat(save, "fertile", ReproductiveState.Fertile, gameTime);
            RatData recovering = CreatePregnancySortRat(save, "recovering", ReproductiveState.Recovery, gameTime);
            RatData nursing = CreatePregnancySortRat(save, "nursing", ReproductiveState.Nursing, gameTime);
            RatData immature = CreatePregnancySortRat(save, "immature", ReproductiveState.Immature, gameTime);
            RatData infertile = CreatePregnancySortRat(save, "infertile", ReproductiveState.Infertile, gameTime);

            fertile.reproductiveState = ReproductiveState.Pregnant;
            fertile.pregnancyId = "stale-pregnancy-id";
            fertile.ageDays = fertile.sexualMaturityDays;
            // The stale state/ID must not make a rat sort as pregnant without
            // a matching pending pregnancy record.

            dueSoon.pregnancyId = "pregnancy-soon";
            dueLater.pregnancyId = "pregnancy-later";
            save.pregnancies.Add(new PregnancyData
            {
                id = dueSoon.pregnancyId,
                motherId = dueSoon.id,
                fatherId = "unused-father-soon",
                startedAt = gameTime,
                dueAt = gameTime + 2L * GameConfig.GameDayMs,
                status = "pending",
            });
            save.pregnancies.Add(new PregnancyData
            {
                id = dueLater.pregnancyId,
                motherId = dueLater.id,
                fatherId = "unused-father-later",
                startedAt = gameTime,
                dueAt = gameTime + 5L * GameConfig.GameDayMs,
                status = "pending",
            });
            RatData nursingPup = ColonyFactory.CreateRat(
                "nursing-pup", "Nursing Pup", RatSex.Male,
                gameTime, 0,
                GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s"),
                new TraitData(2f, 2f, 2f), RatStage.Pinkie);
            nursingPup.motherId = nursing.id;
            save.rats.Add(nursingPup);
            save.ratIds.Add(nursingPup.id);

            Assert.AreEqual(ReproductiveState.Pregnant,
                BreedingSystem.GetReproductiveStatus(save, dueSoon, gameTime).state);
            Assert.AreEqual(ReproductiveState.Fertile,
                BreedingSystem.GetReproductiveStatus(save, fertile, gameTime).state,
                "A stale saved label/ID without a matching record is not pregnancy data.");

            var ascending = new List<RatData>
            {
                fertile, infertile, nursing, dueLater, immature, recovering, dueSoon,
            };
            ascending.Sort((first, second) => BreedingSystem.ComparePregnancySort(
                save, first, second, gameTime, true));
            Assert.AreEqual(dueSoon.id, ascending[0].id);
            Assert.AreEqual(dueLater.id, ascending[1].id);
            Assert.Less(
                Array.IndexOf(ascending.ToArray(), nursing),
                Array.IndexOf(ascending.ToArray(), recovering));
            Assert.Less(
                Array.IndexOf(ascending.ToArray(), recovering),
                Array.IndexOf(ascending.ToArray(), fertile));
            Assert.Less(
                Array.IndexOf(ascending.ToArray(), fertile),
                Array.IndexOf(ascending.ToArray(), immature));
            Assert.Less(
                Array.IndexOf(ascending.ToArray(), immature),
                Array.IndexOf(ascending.ToArray(), infertile));

            var descending = new List<RatData>
            {
                fertile, infertile, nursing, dueLater, immature, recovering, dueSoon,
            };
            descending.Sort((first, second) => BreedingSystem.ComparePregnancySort(
                save, first, second, gameTime, false));
            Assert.AreEqual(infertile.id, descending[0].id);
            Assert.AreEqual(dueLater.id, descending[5].id);
            Assert.AreEqual(dueSoon.id, descending[6].id);
            Assert.Less(
                Array.IndexOf(descending.ToArray(), dueLater),
                Array.IndexOf(descending.ToArray(), dueSoon));

            ColonySaveData restored = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData restoredSoon = BreedingSystem.FindRat(restored, dueSoon.id);
            RatData restoredLater = BreedingSystem.FindRat(restored, dueLater.id);
            Assert.AreEqual(dueSoon.pregnancyId, restoredSoon.pregnancyId);
            Assert.AreEqual(ReproductiveState.Pregnant,
                BreedingSystem.GetReproductiveStatus(restored, restoredSoon, gameTime).state);
            Assert.Less(
                BreedingSystem.ComparePregnancySort(restored, restoredSoon, restoredLater, gameTime, true),
                0,
                "Sooner due dates must remain ahead after save/load.");
        }

        [Test]
        public void GeneratedRatNamesUseFriendlyPoolsAndMigrateNumericSuffixes()
        {
            string maleName = ColonyFactory.GeneratedName("same-rat-id", RatSex.Male);
            string femaleName = ColonyFactory.GeneratedName("same-rat-id", RatSex.Female);
            Assert.IsFalse(char.IsDigit(maleName[maleName.Length - 1]));
            Assert.IsFalse(char.IsDigit(femaleName[femaleName.Length - 1]));
            Assert.AreEqual("Mabel", ColonyFactory.NormalizeDisplayName("Mabel 4"));
            Assert.AreEqual("Randy", ColonyFactory.NormalizeDisplayName("Randy 12"));

            var male = new RatData { name = "Otto", sex = RatSex.Male };
            var female = new RatData { name = "Mabel", sex = RatSex.Female };
            Assert.AreEqual("Otto ♂", ColonyFactory.DisplayName(male));
            Assert.AreEqual("Mabel ♀", ColonyFactory.DisplayName(female));
            Assert.AreEqual("Mabel ♀", ColonyFactory.DisplayName("Mabel ♀", RatSex.Female));

            var assignedName = new RatData
            {
                name = "Mabel 4",
                sex = RatSex.Female,
                nameWasPlayerAssigned = true,
            };
            Assert.AreEqual("Mabel 4 ♀", ColonyFactory.DisplayName(assignedName),
                "Player-entered names are complete names and are not migrated or recombined.");
            var assignedSave = ColonyFactory.CreateNew(1000000L);
            assignedSave.rats[0].name = "Mabel 4";
            assignedSave.rats[0].nameWasPlayerAssigned = true;
            ColonyFactory.NormalizeDisplayNames(assignedSave);
            Assert.AreEqual("Mabel 4", assignedSave.rats[0].name,
                "Save migration must preserve an explicitly assigned full name.");
        }

        [Test]
        public void RatActivityHistoryIsStableByIdAndPersistsThroughJson()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            RatData rat = save.rats[0];
            string ratId = rat.id;

            Assert.IsTrue(RatActivitySystem.SetCurrent(save, rat, "eating", "Eating", 2000000L));
            Assert.IsFalse(RatActivitySystem.SetCurrent(save, rat, "eating", "Eating", 3000000L),
                "Repeated activity labels should not create repeated history entries.");
            Assert.IsTrue(RatActivitySystem.SetCurrent(save, rat, "movement", "Moving habitats", 4000000L,
                "Moved to Pairing Habitat"));

            string json = SaveSystem.ToJson(save);
            ColonySaveData restored = SaveSystem.FromJson(json);
            RatData restoredRat = BreedingSystem.FindRat(restored, ratId);

            Assert.IsNotNull(restoredRat);
            Assert.IsNotNull(restoredRat.activity);
            Assert.AreEqual("Moving habitats", restoredRat.activity.currentActivityLabel,
                "The activity data belongs to the rat ID, not its display name.");
            Assert.AreEqual(2, restoredRat.activity.history.Count);
            Assert.AreEqual("Moved to Pairing Habitat", restoredRat.activity.history[0].message);
            Assert.AreEqual(4000000L, restoredRat.activity.history[0].gameTimeMs);
        }

        [Test]
        public void GameCalendarFormatsEpochYearsMonthsAndOrdinalExceptions()
        {
            Assert.AreEqual("Year 0 - January 1st", GameCalendar.FormatDate(0L));
            Assert.AreEqual("Year 0 - January 1st  •  08:00",
                GameCalendar.FormatTimestamp(GameConfig.StartGameTimeMs));
            long augustFourthYearThree = (3L * 365L + 215L) * GameConfig.GameDayMs;
            Assert.AreEqual("Year 3 - August 4th", GameCalendar.FormatDate(augustFourthYearThree));
            Assert.AreEqual("th", GameCalendar.OrdinalSuffix(11));
            Assert.AreEqual("th", GameCalendar.OrdinalSuffix(12));
            Assert.AreEqual("th", GameCalendar.OrdinalSuffix(13));
            Assert.AreEqual("st", GameCalendar.OrdinalSuffix(21));
            Assert.AreEqual("nd", GameCalendar.OrdinalSuffix(22));
            Assert.AreEqual("rd", GameCalendar.OrdinalSuffix(23));
            Assert.AreEqual("st", GameCalendar.OrdinalSuffix(31));
            Assert.AreEqual("Year 0 - January 11th", GameCalendar.FormatDate(10L * GameConfig.GameDayMs));
            Assert.AreEqual("Year 0 - January 12th", GameCalendar.FormatDate(11L * GameConfig.GameDayMs));
            Assert.AreEqual("Year 0 - January 13th", GameCalendar.FormatDate(12L * GameConfig.GameDayMs));
        }

        [Test]
        public void SaleTankMovesOnlyEligibleIndependentRatsAndPreservesNursingFamilies()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            RatData male = CreateSaleTestRat(save, "sale-male", RatSex.Male, 100f, RatStage.Adult);
            RatData mother = CreateSaleTestRat(save, "sale-mother", RatSex.Female, 100f, RatStage.Adult);
            mother.enclosure = RatEnclosure.FemaleColony;
            RatData pinkie = CreateSaleTestRat(save, "sale-pinkie", RatSex.Female, 1f, RatStage.Pinkie);
            pinkie.motherId = mother.id;
            pinkie.enclosure = RatEnclosure.FemaleColony;

            string reason;
            Assert.IsTrue(EnclosureSystem.TryAssignToForSale(save, male, now, 2, out reason), reason);
            Assert.IsFalse(EnclosureSystem.TryAssignToForSale(save, mother, now, 2, out reason));
            StringAssert.Contains("dependent pinkies", reason);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony, pinkie.enclosure);

            int moved;
            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 2, out moved, out reason));
            Assert.AreEqual(0, moved, "The already-moved male is not moved or logged twice.");
            StringAssert.Contains("mother with dependent pinkies stayed", reason);
            EnclosureSystem.RecalculateAssignments(save);
            Assert.AreEqual(RatEnclosure.ForSale, male.enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony, pinkie.enclosure);
            Assert.AreEqual(1, EnclosureSystem.CountForSaleRats(save),
                "A pinkie cannot be carried into For Sale through its mother's assignment.");
            Assert.AreEqual(3, save.rats.Count, "Habitat transfer must not duplicate or remove rat records.");
        }

        [Test]
        public void SaleTankBulkMoveIsAtomicWhenCapacityIsInsufficient()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            RatData first = CreateSaleTestRat(save, "sale-capacity-1", RatSex.Male, 100f, RatStage.Adult);
            RatData second = CreateSaleTestRat(save, "sale-capacity-2", RatSex.Female, 100f, RatStage.Adult);
            int moved;
            string reason;

            Assert.IsFalse(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 1, out moved, out reason));
            Assert.AreEqual(0, moved);
            Assert.AreEqual(RatEnclosure.MaleColony, first.enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony, second.enclosure);
            StringAssert.Contains("No rats were moved", reason);
        }

        [Test]
        public void SaleTankBulkMoveTransfersEligibleRatsAndKeepsNursingFamiliesTogether()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            RatData male = CreateSaleTestRat(save, "sale-bulk-male", RatSex.Male, 100f, RatStage.Adult);
            RatData female = CreateSaleTestRat(save, "sale-bulk-female", RatSex.Female, 100f, RatStage.Adult);
            RatData nursingMother = CreateSaleTestRat(save, "sale-bulk-nursing-mother",
                RatSex.Female, 100f, RatStage.Adult);
            RatData pinkie = CreateSaleTestRat(save, "sale-bulk-pinkie", RatSex.Female, 1f, RatStage.Pinkie);
            pinkie.motherId = nursingMother.id;

            int moved;
            string reason;
            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 3,
                out moved, out reason), reason);
            Assert.AreEqual(2, moved);
            Assert.AreEqual(RatEnclosure.ForSale, male.enclosure);
            Assert.AreEqual(RatEnclosure.ForSale, female.enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony, nursingMother.enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony, pinkie.enclosure);
            StringAssert.Contains("mother with dependent pinkies stayed", reason);
            Assert.AreEqual(4, save.rats.Count, "Bulk transfer changes assignments only, not colony records.");
        }

        [Test]
        public void LegacyBreedingAssignmentsMigrateWithoutChangingSerializedEnumOrIncludingPinkiesForSale()
        {
            EnclosureSystem.ClearBreedingPair();
            Assert.AreEqual((int)RatEnclosure.Breeding, (int)RatEnclosure.ForSale,
                "For Sale uses the original serialized enum value for save compatibility.");
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            RatData eligible = CreateSaleTestRat(save, "legacy-sale-eligible", RatSex.Male, 100f, RatStage.Adult);
            eligible.enclosure = (RatEnclosure)3;
            RatData mother = CreateSaleTestRat(save, "legacy-sale-mother", RatSex.Female, 100f, RatStage.Adult);
            mother.enclosure = (RatEnclosure)3;
            RatData pinkie = CreateSaleTestRat(save, "legacy-sale-pinkie", RatSex.Female, 1f, RatStage.Pinkie);
            pinkie.motherId = mother.id;
            pinkie.enclosure = (RatEnclosure)3;

            ColonySaveData restored = SaveSystem.FromJson(SaveSystem.ToJson(save));
            EnclosureSystem.RecalculateAssignments(restored);

            Assert.AreEqual(RatEnclosure.ForSale,
                BreedingSystem.FindRat(restored, eligible.id).enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony,
                BreedingSystem.FindRat(restored, mother.id).enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony,
                BreedingSystem.FindRat(restored, pinkie.id).enclosure);
            foreach (RatData rat in restored.rats)
                if (rat != null && rat.enclosure == RatEnclosure.ForSale)
                    Assert.IsTrue(StoreSystem.CanSellRat(restored, rat, now),
                        rat.id + " must pass the same eligibility check as the Sell screen.");
        }

        [Test]
        public void LegacyForSaleMigrationRespectsTheUpgradedColonyCapacity()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            int capacity = UpgradeSystem.ColonyCapacity(save);
            for (int index = 0; index < capacity + 2; index++)
            {
                RatData rat = CreateSaleTestRat(save, "legacy-sale-overflow-" + index,
                    index % 2 == 0 ? RatSex.Male : RatSex.Female, 100f, RatStage.Adult);
                rat.enclosure = RatEnclosure.Breeding;
            }

            EnclosureSystem.RecalculateAssignments(save);

            Assert.AreEqual(capacity, EnclosureSystem.CountForSaleRats(save));
            foreach (RatData rat in save.rats)
            {
                if (rat.enclosure == RatEnclosure.ForSale)
                    Assert.IsTrue(StoreSystem.CanSellRat(save, rat, now));
            }
            Assert.AreEqual(2, save.rats.FindAll(rat => rat.enclosure != RatEnclosure.ForSale).Count,
                "Overflow from the previous full-size Breeding zone returns to ordinary habitats without data loss.");
            Assert.AreEqual(capacity + 2, save.rats.Count);
        }

        [Test]
        public void ForSaleBirthTransferMovesExistingFamilyAndRetainsPregnancyWhenCapacityIsFull()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            RatData mother = CreateSaleTestRat(save, "sale-birth-mother", RatSex.Female, 100f, RatStage.Adult);
            mother.enclosure = RatEnclosure.ForSale;
            RatData existingPinkie = CreateSaleTestRat(save, "sale-birth-existing-pinkie", RatSex.Female, 1f, RatStage.Pinkie);
            existingPinkie.motherId = mother.id;
            existingPinkie.enclosure = RatEnclosure.ForSale;
            var pregnancy = new PregnancyData
            {
                id = "sale-birth-pregnancy",
                motherId = mother.id,
                fatherId = "father-id",
                status = "pending",
                expectedLitterSize = 3,
            };
            save.pregnancies.Add(pregnancy);

            List<RatData> movedFamily;
            string reason;
            Assert.IsTrue(EnclosureSystem.TryPrepareForSaleBirth(save, mother, pregnancy, 10,
                out movedFamily, out reason), reason);
            Assert.AreEqual(2, movedFamily.Count);
            Assert.AreEqual(RatEnclosure.Pairing, mother.enclosure);
            Assert.AreEqual(RatEnclosure.Pairing, existingPinkie.enclosure);
            Assert.IsTrue(mother.pairingHabitatAssigned);
            Assert.AreEqual("pending", pregnancy.status);
            Assert.AreEqual(2, save.rats.Count, "Preparing a birth moves existing records but creates no extra rats.");

            EnclosureSystem.RecalculateAssignments(save);
            Assert.AreEqual(RatEnclosure.Pairing, mother.enclosure,
                "An active pregnancy must not override the temporary Pairing assignment for a For Sale birth.");
            Assert.AreEqual(RatEnclosure.Pairing, existingPinkie.enclosure,
                "Reassignment must keep the existing litter with its mother during the birth transfer.");

            var fullSave = CreateEmptySaleTestSave(now);
            RatData fullMother = CreateSaleTestRat(fullSave, "sale-birth-full-mother", RatSex.Female, 100f, RatStage.Adult);
            fullMother.enclosure = RatEnclosure.ForSale;
            for (int index = 0; index < 8; index++)
            {
                RatData occupant = CreateSaleTestRat(fullSave, "pairing-occupant-" + index,
                    index % 2 == 0 ? RatSex.Male : RatSex.Female, 100f, RatStage.Adult);
                occupant.enclosure = RatEnclosure.Pairing;
                occupant.pairingHabitatAssigned = true;
            }
            var blockedPregnancy = new PregnancyData
            {
                id = "sale-birth-blocked-pregnancy",
                motherId = fullMother.id,
                status = "pending",
                expectedLitterSize = 3,
            };
            fullSave.pregnancies.Add(blockedPregnancy);
            int originalRatCount = fullSave.rats.Count;
            Assert.IsFalse(EnclosureSystem.TryPrepareForSaleBirth(fullSave, fullMother, blockedPregnancy,
                UpgradeSystem.PairingHabitatCapacity(fullSave),
                out movedFamily, out reason));
            StringAssert.Contains("12/10 Pairing Habitat spaces", reason);
            StringAssert.Contains("Pairing Habitat Capacity", reason);
            StringAssert.Contains("Move All Out of Pairing Habitat", reason);
            StringAssert.Contains("retries automatically", reason);
            Assert.AreEqual(EventLogPolicy.Birth,
                EventLogPolicy.CategoryForMessage("Birth delayed — " + reason),
                "A capacity-blocked birth must become a player-visible Birth event.");
            Assert.AreEqual("pending", blockedPregnancy.status);
            Assert.AreEqual(RatEnclosure.ForSale, fullMother.enclosure);
            Assert.AreEqual(8, fullSave.rats.FindAll(rat => rat.enclosure == RatEnclosure.Pairing).Count,
                "A blocked transfer must not partially move the mother or litter into a full habitat.");
            Assert.AreEqual(originalRatCount, fullSave.rats.Count);
        }

        [Test]
        public void PairingCapacityUpgradeAllowsAtomicMotherAndEntireLitterTransfer()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            for (int index = 0; index < 8; index++)
            {
                RatData occupant = CreateSaleTestRat(save, "capacity-transfer-occupant-" + index,
                    index % 2 == 0 ? RatSex.Male : RatSex.Female, 100f, RatStage.Adult);
                occupant.enclosure = RatEnclosure.Pairing;
                occupant.pairingHabitatAssigned = true;
            }

            RatData mother = CreateSaleTestRat(save, "capacity-transfer-mother", RatSex.Female,
                100f, RatStage.Adult);
            mother.enclosure = RatEnclosure.ForSale;
            const string litterId = "capacity-transfer-litter";
            var litter = new LitterData
            {
                id = litterId,
                motherId = mother.id,
                size = 3,
                pupIds = new List<string>(),
            };
            for (int index = 0; index < 3; index++)
            {
                RatData pup = CreateSaleTestRat(save, "capacity-transfer-pup-" + index,
                    index % 2 == 0 ? RatSex.Male : RatSex.Female, 1f, RatStage.Pinkie);
                pup.motherId = mother.id;
                pup.litterId = litterId;
                pup.enclosure = RatEnclosure.ForSale;
                litter.pupIds.Add(pup.id);
            }
            save.litters.Add(litter);
            var pregnancy = new PregnancyData
            {
                id = "capacity-transfer-pregnancy",
                motherId = mother.id,
                litterId = litterId,
                status = "pending",
                birthCommitState = 1,
                expectedLitterSize = 3,
            };
            save.pregnancies.Add(pregnancy);
            int originalRatCount = save.rats.Count;

            List<RatData> movedFamily;
            string reason;
            Assert.IsFalse(EnclosureSystem.TryPrepareForSaleBirth(save, mother, pregnancy,
                UpgradeSystem.PairingHabitatCapacity(save), out movedFamily, out reason));
            Assert.AreEqual(0, movedFamily.Count);
            Assert.AreEqual(RatEnclosure.ForSale, mother.enclosure);
            Assert.AreEqual(3, save.rats.FindAll(rat => rat.stage == RatStage.Pinkie &&
                rat.motherId == mother.id && rat.enclosure == RatEnclosure.ForSale).Count);

            save.colonyCredits = 1000;
            int upgradedCapacity;
            Assert.IsTrue(UpgradeSystem.PurchasePairingHabitatCapacityUpgrade(save, out upgradedCapacity));
            Assert.AreEqual(15, upgradedCapacity);
            Assert.IsTrue(EnclosureSystem.TryPrepareForSaleBirth(save, mother, pregnancy,
                upgradedCapacity, out movedFamily, out reason), reason);

            Assert.AreEqual(4, movedFamily.Count, "The mother and all three pinkies move as one family.");
            Assert.AreEqual(RatEnclosure.Pairing, mother.enclosure);
            Assert.AreEqual(3, save.rats.FindAll(rat => rat.stage == RatStage.Pinkie &&
                rat.motherId == mother.id && rat.enclosure == RatEnclosure.Pairing).Count);
            Assert.AreEqual(12, save.rats.FindAll(rat => rat.enclosure == RatEnclosure.Pairing).Count);
            Assert.AreEqual(0, save.rats.FindAll(rat => rat.enclosure == RatEnclosure.ForSale).Count);
            Assert.AreEqual(originalRatCount, save.rats.Count,
                "Transfer and upgrade must neither duplicate nor delete rats.");
            Assert.AreEqual("pending", pregnancy.status,
                "The transfer preserves the pregnancy record for the normal retry/finish path.");

            EnclosureSystem.RecalculateAssignments(save);
            Assert.AreEqual(RatEnclosure.Pairing, mother.enclosure);
            Assert.AreEqual(3, save.rats.FindAll(rat => rat.stage == RatStage.Pinkie &&
                rat.motherId == mother.id && rat.enclosure == RatEnclosure.Pairing).Count);
        }

        private static ColonySaveData CreateEmptySaleTestSave(long gameTime)
        {
            var save = new ColonySaveData();
            save.EnsureLists();
            save.clock.gameTimeMs = gameTime;
            return save;
        }

        private static RatData CreateSaleTestRat(ColonySaveData save, string id, RatSex sex,
            float ageDays, RatStage stage)
        {
            var genotype = GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s");
            RatData rat = ColonyFactory.CreateRat(id, id, sex,
                save.clock.gameTimeMs - (long)(ageDays * GameConfig.GameDayMs), 0,
                genotype, new TraitData(50f, 70f, 70f), stage);
            rat.ageDays = ageDays;
            rat.sexualMaturityDays = sex == RatSex.Female ? 70f : 56f;
            rat.breedingEndAgeDays = 700f;
            rat.enclosure = sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony;
            save.rats.Add(rat);
            save.ratIds.Add(rat.id);
            return rat;
        }

        private static ColonySaveData CreatePairingTestSave(long gameTime, float fertility)
        {
            var save = ColonyFactory.CreateNew(gameTime);
            // CreateNew intentionally includes the randomized starter pair.
            // This fixture replaces those founders so index 0/1 are the
            // explicitly configured Pairing Habitat participants used by the
            // tests below.
            save.rats.Clear();
            save.ratIds.Clear();
            var genotype = GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "S", "s");
            var female = ColonyFactory.CreateRat(
                "pairing-female", "Olive", RatSex.Female,
                gameTime - (100L * GameConfig.GameDayMs), 0,
                genotype.Clone(), new TraitData(80f, 80f, fertility), RatStage.Adult);
            var male = ColonyFactory.CreateRat(
                "pairing-male", "Branch", RatSex.Male,
                gameTime - (100L * GameConfig.GameDayMs), 0,
                genotype.Clone(), new TraitData(80f, 80f, fertility), RatStage.Adult);
            foreach (var rat in new[] { female, male })
            {
                rat.enclosure = RatEnclosure.Pairing;
                rat.pairingHabitatAssigned = true;
                rat.ageDays = 100f;
                rat.reproductiveState = ReproductiveState.Fertile;
                rat.estrousCycleAnchorGameTime = gameTime;
                rat.sexualMaturityDays = rat.sex == RatSex.Female ? 70f : 56f;
                rat.breedingEndAgeDays = 365f;
                rat.baseHealth = rat.traits.health;
                rat.baseFertility = rat.traits.fertility;
                save.rats.Add(rat);
                save.ratIds.Add(rat.id);
            }
            return save;
        }

        private static RatData CreatePregnancySortRat(
            ColonySaveData save,
            string id,
            ReproductiveState state,
            long gameTime)
        {
            var genotype = GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s");
            RatData rat = ColonyFactory.CreateRat(
                id,
                id,
                RatSex.Female,
                gameTime - (100L * GameConfig.GameDayMs),
                0,
                genotype,
                new TraitData(10f, 10f, 10f),
                RatStage.Adult);
            rat.enclosure = RatEnclosure.FemaleColony;
            rat.ageDays = 100f;
            rat.sexualMaturityDays = 70f;
            rat.breedingEndAgeDays = 700f;
            rat.estrousCycleAnchorGameTime = gameTime;
            rat.reproductiveState = state;
            rat.nursing = state == ReproductiveState.Nursing;
            rat.nursingUntil = gameTime + GameConfig.GameDayMs;
            rat.recoveryUntil = gameTime + GameConfig.GameDayMs;
            if (state == ReproductiveState.Immature) rat.ageDays = 10f;
            if (state == ReproductiveState.Infertile) rat.ageDays = rat.breedingEndAgeDays;
            // The systems under test derive biological age from the saved
            // birth timestamp; keep it aligned with each synthetic state.
            rat.birthTimestamp = gameTime - (long)(rat.ageDays * GameConfig.GameDayMs);
            save.rats.Add(rat);
            save.ratIds.Add(rat.id);
            return rat;
        }

        [Test]
        public void EnclosuresFollowPregnancyBirthAndDependentPinkieGrowth()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            RatData mother = save.rats[0];
            RatData father = save.rats[1];
            PrepareStarterPairForBreeding(save, 2000000L, false);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            Assert.AreEqual(RatEnclosure.MaleColony, father.enclosure);

            PregnancyData pregnancy;
            string reason;
            Assert.IsTrue(BreedingSystem.StartBreeding(save, mother, father, 2000000L, out pregnancy, out reason), reason);
            EnclosureSystem.RecalculateAssignments(save);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            Assert.IsFalse(mother.nursing);
            Assert.AreEqual(RatEnclosure.MaleColony, father.enclosure);

            LitterData litter;
            Assert.IsTrue(BreedingSystem.FinishPregnancy(save, pregnancy.id, 2001000L, out litter, out reason), reason);
            EnclosureSystem.RecalculateAssignments(save);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            Assert.IsTrue(mother.nursing);
            foreach (var pupId in litter.pupIds)
            {
                Assert.AreEqual(RatEnclosure.FemaleColony, BreedingSystem.FindRat(save, pupId).enclosure);
            }

            foreach (var pupId in litter.pupIds)
            {
                RatData pup = BreedingSystem.FindRat(save, pupId);
                Assert.IsTrue(GrowthSystem.AdvanceRatToNextStage(pup, 3000000L));
            }
            EnclosureSystem.RecalculateAssignments(save);
            Assert.IsFalse(mother.nursing);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            foreach (var pupId in litter.pupIds)
            {
                RatData pup = BreedingSystem.FindRat(save, pupId);
                Assert.AreEqual(RatEnclosure.FemaleColony, pup.enclosure);
            }
        }

        [Test]
        public void RemovingTheLastDependentPinkieReleasesItsMother()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            RatData mother = save.rats[0];
            RatData father = save.rats[1];
            PrepareStarterPairForBreeding(save, 2000000L, false);
            PregnancyData pregnancy;
            string reason;
            Assert.IsTrue(BreedingSystem.StartBreeding(save, mother, father, 2000000L, out pregnancy, out reason), reason);
            LitterData litter;
            Assert.IsTrue(BreedingSystem.FinishPregnancy(save, pregnancy.id, 2001000L, out litter, out reason), reason);
            EnclosureSystem.RecalculateAssignments(save);
            Assert.IsTrue(mother.nursing);

            foreach (var pupId in litter.pupIds)
            {
                save.rats.RemoveAll(rat => rat != null && rat.id == pupId);
                EnclosureSystem.RecalculateAssignments(save);
                if (EnclosureSystem.HasDependentPinkies(save, mother.id)) Assert.IsTrue(mother.nursing);
            }
            Assert.IsFalse(mother.nursing);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
        }

        private static void PrepareStarterPairForBreeding(ColonySaveData save, long gameTime, bool keepPairingAssignment)
        {
            if (save == null || save.rats == null || save.rats.Count < 2) return;
            for (int i = 0; i < 2; i++)
            {
                var rat = save.rats[i];
                if (rat == null) continue;
                rat.stage = RatStage.Adult;
                rat.ageDays = rat.sex == RatSex.Female ? 100f : 86f;
                rat.sexualMaturityDays = rat.sex == RatSex.Female
                    ? GameConfig.FemaleSexualMaturityDays
                    : GameConfig.MaleSexualMaturityDays;
                rat.breedingEndAgeDays = GameConfig.MaximumBreedingEndDays;
                rat.estrousCycleAnchorGameTime = gameTime;
                rat.breedingCooldownUntil = 0L;
                rat.pregnancyId = null;
                rat.nursing = false;
                rat.reproductiveState = ReproductiveState.Fertile;
                rat.pairingHabitatAssigned = keepPairingAssignment;
                rat.enclosure = keepPairingAssignment
                    ? RatEnclosure.Pairing
                    : (rat.sex == RatSex.Female ? RatEnclosure.FemaleColony : RatEnclosure.MaleColony);
            }
        }

        [Test]
        public void PhysicalEnclosuresAreEqualFullSizeAndArrangedLeftToRight()
        {
            RatEnclosure[] pages =
            {
                RatEnclosure.MaleColony,
                RatEnclosure.FemaleColony,
                RatEnclosure.Breeding,
                RatEnclosure.Pairing,
            };
            EnclosureSystem.Definition previous = null;
            for (int index = 0; index < pages.Length; index++)
            {
                EnclosureSystem.Definition current = EnclosureSystem.GetDefinition(pages[index]);
                Assert.AreEqual(10.70f, current.Width, 0.001f);
                Assert.AreEqual(22.00f, current.Depth, 0.001f);
                if (previous != null)
                    Assert.Less(previous.maxX, current.minX, "Habitat pages must have a visible horizontal gap.");
                previous = current;
            }

            Vector3 femalePoint = EnclosureSystem.PointInEnclosure(RatEnclosure.FemaleColony, -1.5f, 4f);
            Assert.IsTrue(EnclosureSystem.IsBehaviorPointAllowed(RatEnclosure.FemaleColony, femalePoint));
            Assert.IsFalse(EnclosureSystem.IsBehaviorPointAllowed(RatEnclosure.Pairing, EnclosureSystem.PairingNestPosition));
        }

        [Test]
        public void RemovedNurseryAssignmentsFollowTheRecordedMother()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            RatData mother = save.rats[0];
            mother.enclosure = RatEnclosure.Pairing;
            mother.pairingHabitatAssigned = true;

            RatData pup = ColonyFactory.CreateRat(
                "legacy-pup", "Legacy Pup", RatSex.Male, 1000000L, 1,
                mother.genotype.Clone(), new TraitData(4f, 5f, 6f), RatStage.Pinkie);
            pup.motherId = mother.id;
            pup.enclosure = RatEnclosure.Nursery;
            save.rats.Add(pup);
            save.ratIds.Add(pup.id);

            RatData orphan = ColonyFactory.CreateRat(
                "legacy-orphan", "Legacy Orphan", RatSex.Male, 1000000L, 1,
                mother.genotype.Clone(), new TraitData(4f, 5f, 6f), RatStage.Pinkie);
            orphan.enclosure = RatEnclosure.Nursery;
            save.rats.Add(orphan);
            save.ratIds.Add(orphan.id);

            EnclosureSystem.RecalculateAssignments(save);

            Assert.AreEqual(RatEnclosure.Pairing, pup.enclosure);
            Assert.IsTrue(pup.pairingHabitatAssigned);
            Assert.AreEqual(RatEnclosure.MaleColony, orphan.enclosure);
        }

        [Test]
        public void ScreenAwakePreferenceDefaultsOnAndPreservesAnIntentionalOptOut()
        {
            ColonySaveData newSave = ColonyFactory.CreateNew(1000000L);
            Assert.IsTrue(newSave.keepScreenAwake);
            Assert.IsTrue(newSave.keepScreenAwakePreferenceInitialized);

            // A legacy JSON record has no initialized bit. EnsureLists should
            // migrate it to the gameplay-friendly default rather than treating
            // a missing field as an intentional opt-out.
            var legacy = new ColonySaveData { keepScreenAwake = false };
            legacy.keepScreenAwakePreferenceInitialized = false;
            legacy.EnsureLists();
            Assert.IsTrue(legacy.keepScreenAwake);
            Assert.IsTrue(legacy.keepScreenAwakePreferenceInitialized);

            legacy.keepScreenAwake = false;
            legacy.keepScreenAwakePreferenceInitialized = true;
            legacy.EnsureLists();
            Assert.IsFalse(legacy.keepScreenAwake);
        }

        [Test]
        public void DeveloperGrowthRevealsFurAndPersistsTheGrowthAnchor()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            var rat = ColonyFactory.CreateRat("pup", "Pup", RatSex.Female, 1000000L, 1, save.rats[0].genotype.Clone(), new TraitData(50f, 50f, 50f), RatStage.Pinkie);
            save.rats.Add(rat);
            save.clock.gameTimeMs = 3000000L;
            Assert.IsTrue(GrowthSystem.AdvanceRatToNextStage(rat, save.clock.gameTimeMs));
            Assert.AreEqual(RatStage.YoungRat, rat.stage);
            Assert.IsTrue(rat.phenotype.furRevealed);
            Assert.IsTrue(rat.developerGrowthOverride);
            float age = rat.ageDays;
            GrowthSystem.RefreshRatStages(save);
            Assert.GreaterOrEqual(rat.ageDays, age);
            Assert.AreEqual(RatStage.YoungRat, rat.stage);
        }

        [Test]
        public void DeveloperPhenotypeCoatsUseDistinctImportedMaterialPaths()
        {
            var factoryHost = new GameObject("Developer Phenotype Material Test Factory");
            var visualParent = new GameObject("Developer Phenotype Material Test Parent").transform;
            var factory = factoryHost.AddComponent<RatVisualFactory>();
            factory.handPaintedRatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandPaintedRat/HandPaintedRat.prefab");
            Assert.IsNotNull(factory.handPaintedRatPrefab, "The imported Hand Painted Rat prefab is not available at the expected asset path.");

            try
            {
                AssertDeveloperCoat(factory, visualParent, "Albino", "B", "B", "c", "c", "D", "D", "s", "s", "albino", "rat_bege_psd", true, false);
                AssertDeveloperCoat(factory, visualParent, "Diluted Brown", "b", "b", "C", "C", "d", "d", "s", "s", "diluted-brown", "rat_khaki", false, false);
                AssertDeveloperCoat(factory, visualParent, "Solid Brown", "b", "b", "C", "C", "D", "D", "s", "s", "brown", "rat_khaki", false, false);
                AssertDeveloperCoat(factory, visualParent, "Solid Black", "B", "B", "C", "C", "D", "D", "s", "s", "black", "rat_grey", false, false);
                AssertDeveloperCoat(factory, visualParent, "Diluted Black", "B", "B", "C", "C", "d", "d", "s", "s", "diluted-black", "rat_grey", false, false);
                AssertDeveloperCoat(factory, visualParent, "Spotted Brown", "b", "b", "C", "C", "D", "D", "S", "S", "brown", "rat_khaki", false, true);
            }
            finally
            {
                Object.DestroyImmediate(visualParent.gameObject);
                Object.DestroyImmediate(factoryHost);
            }
        }

        [Test]
        public void DeveloperPhenotypeVisualSetCoversEveryCoatVariantAndMarkingFamily()
        {
            var factoryHost = new GameObject("Developer Phenotype Coverage Factory");
            var visualParent = new GameObject("Developer Phenotype Coverage Parent").transform;
            var factory = factoryHost.AddComponent<RatVisualFactory>();
            factory.handPaintedRatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandPaintedRat/HandPaintedRat.prefab");
            Assert.IsNotNull(factory.handPaintedRatPrefab);

            string[] coatVariants =
            {
                "black", "agouti", "mink", "russian blue", "blue agouti", "beige",
                "champagne", "fawn", "silver fawn", "cinnamon", "russian cinnamon",
                "american blue", "black marten", "tonkinese", "burmese", "roan",
                "agouti marten", "aussie mink", "coffee", "chocolate", "blue marten",
                "pink-eye platinum", "russian dove", "pink-eye white", "albino"
            };
            string[] markingFamilies = GeneticsSystem.MarkingFamilies;
            GenotypeData ordinaryGenotype = GeneticsSystem.CreateFounder(
                "B", "B", "C", "C", "D", "D", "S", "S");
            var renderedCoatColors = new HashSet<string>();

            try
            {
                for (int index = 0; index < coatVariants.Length; index++)
                {
                    string variant = coatVariants[index];
                    GenotypeData genotype = variant == "albino"
                        ? GeneticsSystem.CreateFounder("B", "B", "c", "c", "D", "D", "S", "S")
                        : ordinaryGenotype.Clone();
                    RatData rat = ColonyFactory.CreateRat(
                        "visual-coat-" + index, "Visual Coat " + index, RatSex.Female, 0L, 0,
                        genotype, new TraitData(50f, 50f, 50f), RatStage.Adult);
                    rat.coatColorVariant = variant;
                    rat.coatTone = 1f;
                    rat.phenotype = GeneticsSystem.DerivePhenotype(
                        RatStage.Adult, rat.genotype, rat.coatColorVariant, rat.coatTone);
                    rat.markingFamily = "Self";
                    GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);

                    GameObject visual = factory.CreateStageVisual(visualParent, rat);
                    try
                    {
                        var renderer = visual == null ? null : visual.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        Assert.IsNotNull(renderer, variant + " imported renderer");
                        Material material = renderer.sharedMaterials[0];
                        Assert.AreEqual("Rat Habitat/Hand Painted Rat Coat", material.shader.name,
                            variant + " must use the shared phenotype shader");
                        Assert.IsTrue(material.HasProperty("_AccentColor"), variant + " accent color property");
                        Assert.IsTrue(material.HasProperty("_MarkingFamily"), variant + " marking-family property");
                        renderedCoatColors.Add(ColorUtility.ToHtmlStringRGB(material.GetColor("_Color")));
                        Assert.IsFalse(string.IsNullOrEmpty(rat.phenotype.coatColorLabel), variant + " label");
                        Assert.AreNotEqual("Unknown", rat.phenotype.coatColorLabel, variant + " resolved label");
                        Assert.AreEqual(0f, material.GetFloat("_SpotStrength"), 0.001f,
                            variant + " self coat should not receive a white marking overlay");
                        if (variant == "albino")
                            Assert.AreEqual(1f, material.GetFloat("_AlbinoMode"), 0.001f);
                    }
                    finally
                    {
                        if (visual != null) Object.DestroyImmediate(visual);
                    }
                }

                Assert.GreaterOrEqual(renderedCoatColors.Count, 12,
                    "The supported coat variants should resolve to visibly distinct material colors.");

                for (int index = 0; index < markingFamilies.Length; index++)
                {
                    string family = markingFamilies[index];
                    RatData rat = ColonyFactory.CreateRat(
                        "visual-marking-" + index, "Visual Marking " + index, RatSex.Male, 0L, 0,
                        ordinaryGenotype.Clone(), new TraitData(50f, 50f, 50f), RatStage.Adult);
                    rat.coatColorVariant = "black";
                    rat.coatTone = 1f;
                    rat.phenotype = GeneticsSystem.DerivePhenotype(
                        RatStage.Adult, rat.genotype, rat.coatColorVariant, rat.coatTone);
                    rat.markingFamily = family;
                    GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);

                    GameObject visual = factory.CreateStageVisual(visualParent, rat);
                    try
                    {
                        var renderer = visual == null ? null : visual.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        Assert.IsNotNull(renderer, family + " imported renderer");
                        Material material = renderer.sharedMaterials[0];
                        Assert.AreEqual("Rat Habitat/Hand Painted Rat Coat", material.shader.name,
                            family + " must use the shared phenotype shader");
                        Assert.IsTrue(material.GetFloat("_MarkingFamily") >= 0f,
                            family + " must receive a deterministic family code");
                        Assert.AreEqual(family == "Solid" || family == "Self" ? 0f : 1f,
                            material.GetFloat("_SpotStrength"), 0.001f,
                            family + " marking strength must match the recorded family");
                        Assert.AreEqual(GeneticsSystem.NormalizeMarkingFamily(family, rat.genotype),
                            rat.phenotype.markingFamily, family + " recorded family");
                    }
                    finally
                    {
                        if (visual != null) Object.DestroyImmediate(visual);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(visualParent.gameObject);
                Object.DestroyImmediate(factoryHost);
            }
        }

        private static RatData CreateAgeBoundaryRat(float ageDays, float breedingEndAgeDays, float fertility)
        {
            RatData rat = ColonyFactory.CreateRat(
                "age-boundary-" + ageDays.ToString("0"),
                "Boundary",
                RatSex.Female,
                1000000L,
                0,
                GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s"),
                new TraitData(50f, 50f, fertility),
                RatStage.Adult);
            rat.ageDays = ageDays;
            rat.breedingEndAgeDays = breedingEndAgeDays;
            rat.sexualMaturityDays = GameConfig.FemaleSexualMaturityDays;
            rat.baseHealth = 50f;
            rat.baseFertility = fertility;
            rat.baseHealthInitialized = true;
            rat.baseFertilityInitialized = true;
            rat.stage = GrowthSystem.StageForAge(ageDays, rat.sex, breedingEndAgeDays);
            return rat;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            Assert.IsNotNull(target);
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected private field '" + fieldName + "'.");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            Assert.IsNotNull(target);
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected private field '" + fieldName + "'.");
            return (T)field.GetValue(target);
        }

        private static void AssertDeveloperCoat(
            RatVisualFactory factory,
            Transform parent,
            string label,
            string b1,
            string b2,
            string c1,
            string c2,
            string d1,
            string d2,
            string s1,
            string s2,
            string expectedCoatId,
            string expectedTexture,
            bool expectedAlbinoMode,
            bool expectedSpotted)
        {
            var genotype = GeneticsSystem.CreateFounder(b1, b2, c1, c2, d1, d2, s1, s2);
            var rat = ColonyFactory.CreateRat("developer-test-" + label, label, RatSex.Female, 0L, 0, genotype, new TraitData(60f, 100f, 75f), RatStage.Adult);
            Assert.AreEqual(expectedCoatId, rat.phenotype.coatColorId, label + " coat phenotype");
            Assert.AreEqual(expectedSpotted, rat.phenotype.spotted, label + " spotting phenotype");
            Assert.AreEqual(GeneticsSystem.FormatPair("B", b1, b2), GeneticsSystem.FormatPair(rat.genotype, "B"), label + " B locus");
            Assert.AreEqual(GeneticsSystem.FormatPair("C", c1, c2), GeneticsSystem.FormatPair(rat.genotype, "C"), label + " C locus");
            Assert.AreEqual(GeneticsSystem.FormatPair("D", d1, d2), GeneticsSystem.FormatPair(rat.genotype, "D"), label + " D locus");
            Assert.AreEqual(GeneticsSystem.FormatPair("S", s1, s2), GeneticsSystem.FormatPair(rat.genotype, "S"), label + " S locus");

            var visual = factory.CreateStageVisual(parent, rat);
            try
            {
                var renderer = visual == null ? null : visual.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.IsNotNull(renderer, label + " imported SkinnedMeshRenderer");
                var material = renderer.sharedMaterials[0];
                Assert.IsNotNull(material, label + " material");
                Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : material.GetTexture("_BaseMap");
                Assert.IsNotNull(texture, label + " coat texture");
                Assert.AreEqual(expectedTexture, texture.name.ToLowerInvariant(), label + " coat texture");
                if (expectedAlbinoMode || expectedSpotted)
                {
                    Assert.AreEqual("Rat Habitat/Hand Painted Rat Coat", material.shader.name, label + " shader");
                }
                if (expectedAlbinoMode) Assert.AreEqual(1f, material.GetFloat("_AlbinoMode"), 0.001f, label + " albino shader mode");
                if (expectedSpotted) Assert.AreEqual(1f, material.GetFloat("_SpotStrength"), 0.001f, label + " spot shader mode");
                Debug.Log("[Rat Habitat] Editor phenotype material test: rat=" + rat.name +
                    " genotype=" + GeneticsSystem.FormatPair(rat.genotype, "B") + " " + GeneticsSystem.FormatPair(rat.genotype, "C") + " " + GeneticsSystem.FormatPair(rat.genotype, "D") + " " + GeneticsSystem.FormatPair(rat.genotype, "S") +
                    " coatColorId=" + rat.phenotype.coatColorId +
                    " coatColorHex=" + rat.phenotype.coatColorHex +
                    " accentHex=" + rat.phenotype.accentHex +
                    " spotted=" + rat.phenotype.spotted +
                    " selectedTexture=" + texture.name +
                    " selectedMaterial=" + material.name +
                    " shader=" + material.shader.name);
            }
            finally
            {
                if (visual != null) Object.DestroyImmediate(visual);
            }
        }
    }
}
#endif
