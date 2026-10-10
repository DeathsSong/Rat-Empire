#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RatHabitat.Tests
{
    public class NestCornerRoutingTests
    {
        private GameObject host;
        private RatHabitatBehavior behavior;
        private Bounds forbidden;
        private const float FrameSeconds = 1f / 60f;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            GrowthSystem.SetSimulationPaused(false);
            GrowthSystem.SetRuntimeSpeed(1f);
            // Match the imported nest footprint, including the authoritative
            // 0.18-unit adult exclusion padding on each side.
            var physical = new Bounds(EnclosureSystem.PairingNestPosition, new Vector3(6.38f,.8f,4.58f));
            EnclosureSystem.RegisterPairingNestBounds(physical);
            forbidden = physical;
            forbidden.Expand(new Vector3(.36f,0,.36f));
            host = new GameObject("Isolated nest corner regression");
            var builder = host.AddComponent<HabitatBuilder>();
            var root = new GameObject("Corner test rat");
            root.transform.SetParent(host.transform);
            root.transform.position = AtCorner(.35f,-.7f);
            behavior = root.AddComponent<RatHabitatBehavior>();
            var rat = ColonyFactory.CreateRat("nest-corner-regression", "Corner test", RatSex.Female,
                0L, 0, new GenotypeData(), new TraitData(50,80,70), RatStage.Adult);
            rat.enclosure = RatEnclosure.Pairing;
            behavior.Configure(builder, rat);
            Set("movementSpeed", .3f);
            Set("spacingTimer", float.MaxValue);
            Set("stateTimer", 100000f);
            Set("currentTarget", null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            EnclosureSystem.ClearPairingNestBounds();
            GrowthSystem.SetRuntimeSpeed(1f);
            GrowthSystem.SetSimulationPaused(false);
        }

        [TestCase(1f,false,false)]
        [TestCase(1f,true,false)]
        [TestCase(3f,false,false)]
        [TestCase(3f,true,false)]
        [TestCase(1f,false,true)]
        [TestCase(1f,true,true)]
        [TestCase(3f,false,true)]
        [TestCase(3f,true,true)]
        public void UpperRightCornerMakesSafeProgressBothDirections(float speed, bool reverse, bool clearanceShell)
        {
            RunCornerTraversal(speed,reverse,clearanceShell,RatEnclosure.Pairing);
        }

        private void RunCornerTraversal(float speed, bool reverse, bool clearanceShell, RatEnclosure enclosure)
        {
            GrowthSystem.SetRuntimeSpeed(speed);
            Vector3 side = AtCorner(clearanceShell ? .02f : .3f,-.7f);
            Vector3 top = AtCorner(-.7f,.3f);
            Vector3 start = reverse ? top : side;
            Vector3 goal = reverse ? side : top;
            behavior.transform.position = start;
            Set("targetPosition", goal);
            Vector3 lastStep = Vector3.zero;
            int reversals = 0;
            float walked = 0f;
            for (int frame = 0; frame < 1500 && Vector3.Distance(behavior.transform.position, goal) > .24f; frame++)
            {
                Vector3 before = behavior.transform.position;
                TravelFrame();
                Vector3 after = behavior.transform.position;
                Assert.IsFalse(EnclosureSystem.IsInsideAdultNestExclusion(enclosure, after),
                    "A detour step must not enter the forbidden nest footprint.");
                Assert.IsTrue(EnclosureSystem.IsNestSafeRoute(enclosure, before, after),
                    "The whole segment, not only its endpoint, must avoid the nest corner.");
                Assert.IsTrue(EnclosureSystem.IsInside(enclosure, after, .38f));
                Vector3 step = after-before;
                float budget = speed == 1f ? .3f*FrameSeconds :
                    GrowthSystem.SimulationVisibleMovementDistanceBudget(FrameSeconds);
                Assert.LessOrEqual(step.magnitude, budget+.0001f, "Routing must walk, never teleport.");
                if (step.sqrMagnitude > 1e-10f && lastStep.sqrMagnitude > 1e-10f &&
                    Vector3.Dot(step.normalized,lastStep.normalized) < -.9f) reversals++;
                if (step.sqrMagnitude > 1e-10f) lastStep = step;
                walked += step.magnitude;
                Assert.AreEqual(goal, Get<Vector3>("targetPosition"), "A valid detour must keep its original goal.");
            }
            Assert.LessOrEqual(Vector3.Distance(behavior.transform.position,goal), .24f,
                "The rat must finish walking around the corner rather than retry a zero-length route.");
            Assert.Greater(walked, Vector3.Distance(start,goal)-.25f);
            Assert.LessOrEqual(reversals, 1, "The corner must not cause back-and-forth oscillation.");
            Assert.AreEqual(0,behavior.BlockedTargetRecoveryCount, "Valid slow routes are not stuck targets.");
        }

        [Test]
        public void FailedDetourReplacesAmbientGoalAndResumesWalking()
        {
            Vector3 start = AtCorner(.4f,-.5f);
            behavior.transform.position = start;
            Vector3 impossibleGoal = forbidden.center;
            impossibleGoal.y = start.y;
            Set("targetPosition", impossibleGoal);
            for (int frame = 0; frame < 60; frame++) TravelFrame();
            Assert.AreNotEqual(impossibleGoal,Get<Vector3>("targetPosition"),
                "A failed detour must not keep retrying an unchanged current-position fallback.");
            Assert.Greater(behavior.BlockedTargetRecoveryCount,0);
            for (int frame = 0; frame < 120; frame++) TravelFrame();
            Assert.Greater(Vector3.Distance(start,behavior.transform.position),.15f);
            Assert.IsFalse(EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing,behavior.transform.position));
        }

        [TestCase(1f)]
        [TestCase(3f)]
        public void NoAvailableDetourReplacesGoalWithoutReusingCurrentPosition(float speed)
        {
            GrowthSystem.SetRuntimeSpeed(speed);
            var definition = EnclosureSystem.GetDefinition(RatEnclosure.Pairing);
            var blockedNest = new Bounds(definition.Center, new Vector3(definition.Width,.8f,3f));
            EnclosureSystem.RegisterPairingNestBounds(blockedNest);
            Vector3 start = new Vector3(definition.Center.x,.45f,definition.Center.z-2f);
            Vector3 unreachableGoal = new Vector3(start.x,start.y,definition.Center.z+2f);
            behavior.transform.position = start;
            Set("targetPosition",unreachableGoal);
            Assert.IsFalse(EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing,start));
            Assert.IsFalse(EnclosureSystem.IsInsideAdultNestExclusion(RatEnclosure.Pairing,unreachableGoal));
            Assert.AreEqual(start,EnclosureSystem.GetNearestOpenFloorPosition(RatEnclosure.Pairing,start,.48f),
                "This reproduces the old unchanged escape fallback outside an impassable nest.");
            Assert.IsFalse((bool)typeof(RatHabitatBehavior).GetMethod("TryBuildNestDetour",PrivateInstance)
                .Invoke(behavior,new object[] { RatEnclosure.Pairing,start,unreachableGoal }));

            TravelFrame();
            Assert.AreEqual(start,behavior.transform.position,"A failed route must not teleport the rat.");
            Assert.AreNotEqual(unreachableGoal,Get<Vector3>("targetPosition"));
            Assert.AreEqual(1,behavior.BlockedTargetRecoveryCount,
                "A proven invalid route must be replaced instead of caching the current point forever.");

            EnclosureSystem.RegisterPairingNestBounds(new Bounds(EnclosureSystem.PairingNestPosition,
                new Vector3(6.38f,.8f,4.58f)));
            for (int frame = 0; frame < 120; frame++) TravelFrame();
            Assert.Greater(Vector3.Distance(start,behavior.transform.position),.15f,
                "After replacing an impossible route, the rat must resume walking to a valid floor goal.");
        }

        [TestCase(1f,false)]
        [TestCase(1f,true)]
        [TestCase(3f,false)]
        [TestCase(3f,true)]
        public void FemaleTankNestCornerAlsoMakesProgress(float speed, bool reverse)
        {
            Get<RatData>("rat").enclosure = RatEnclosure.FemaleColony;
            forbidden = new Bounds(EnclosureSystem.GetNestPosition(RatEnclosure.FemaleColony),
                new Vector3(EnclosureSystem.NestAdultExclusionRadiusX*2f,.8f,
                    EnclosureSystem.NestAdultExclusionRadiusZ*2f));
            // Same traversal contract on the smaller non-imported nest.
            RunCornerTraversal(speed,reverse,false,RatEnclosure.FemaleColony);
        }

        [TestCase(1f)]
        [TestCase(3f)]
        public void CourtshipRoutesAroundCornerToItsOwnGoal(float speed)
        {
            GrowthSystem.SetRuntimeSpeed(speed);
            behavior.transform.position = AtCorner(.3f,-.7f);
            Vector3 goal = AtCorner(-.7f,.3f);
            Set("targetPosition", forbidden.center); // stale ambient target must be ignored
            Assert.IsTrue(behavior.BeginPairingApproach(goal,goal+Vector3.left,1f));
            var update = typeof(RatHabitatBehavior).GetMethod("UpdatePairingApproach",PrivateInstance);
            for (int frame = 0; frame < 1500 && !behavior.PairingApproachAtTarget; frame++)
            {
                Vector3 before = behavior.transform.position;
                float delta = GrowthSystem.SimulationMovementDeltaSeconds(FrameSeconds);
                update.Invoke(behavior,new object[] { delta,GrowthSystem.SimulationMovementTimeBudget(delta),
                    GrowthSystem.SimulationVisibleMovementDistanceBudget(FrameSeconds) });
                Assert.IsTrue(EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing,
                    before,behavior.transform.position));
            }
            Assert.IsTrue(behavior.PairingApproachAtTarget,"A nest detour must not cancel or block courtship.");
            Assert.AreEqual(0,behavior.BlockedTargetRecoveryCount);
        }

        [TestCase(1f)]
        [TestCase(3f)]
        public void NursingMotherStillWalksIntoSharedCaregiverZone(float speed)
        {
            GrowthSystem.SetRuntimeSpeed(speed);
            Get<RatData>("rat").nursing = true;
            Set("nursingCareMovementActive",true);
            behavior.transform.position = AtCorner(.3f,.3f);
            Vector3 goal = EnclosureSystem.GetNestCaregiverPosition(RatEnclosure.Pairing);
            goal.y = behavior.transform.position.y;
            Set("targetPosition",goal);
            for (int frame = 0; frame < 1800 && Vector3.Distance(behavior.transform.position,goal) > .24f; frame++)
                TravelFrame();
            Assert.IsTrue(EnclosureSystem.IsInsideNestCaregiverZone(RatEnclosure.Pairing,behavior.transform.position));
            Assert.LessOrEqual(Vector3.Distance(behavior.transform.position,goal),.24f);
            Assert.AreEqual(0,behavior.BlockedTargetRecoveryCount,"Ambient watchdogs must leave nursing routes alone.");
        }

        [TestCase(.005f)]
        [TestCase(.00005f)]
        public void SmallBlockedStepsRecoverInRealTimeButSlowProgressDoesNot(float requested)
        {
            var track = typeof(RatHabitatBehavior).GetMethod("TrackBlockedAmbientTarget",PrivateInstance);
            for (int frame = 0; frame < 180; frame++)
                Assert.IsFalse((bool)track.Invoke(behavior,new object[] { requested,requested,FrameSeconds }),
                    "Valid tiny movement must not trigger blocked-target recovery.");
            Assert.AreEqual(0,behavior.BlockedTargetRecoveryCount);
            bool recovered = false;
            for (int frame = 0; frame < 45 && !recovered; frame++)
                recovered = (bool)track.Invoke(behavior,new object[] { requested,0f,FrameSeconds });
            Assert.IsTrue(recovered,"Sustained zero progress must recover within 0.75 real seconds even at 1x.");
            Assert.AreEqual(1,behavior.BlockedTargetRecoveryCount);
        }

        [Test]
        public void NestRouteBoundsAgreeWithExclusionAndDetectThinCornerCrossings()
        {
            Bounds routeBounds;
            Assert.IsTrue(EnclosureSystem.TryGetPairingNestAvoidanceBounds(.04f,out routeBounds));
            Assert.AreEqual(forbidden.extents.x+.04f,routeBounds.extents.x,.00001f);
            Assert.AreEqual(forbidden.extents.z+.04f,routeBounds.extents.z,.00001f);
            Assert.IsFalse(EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing,
                AtCorner(-1.123f,1.121f),AtCorner(1f,-1.002f)),
                "Fixed route samples can skip a narrow corner intersection; use a continuous segment check.");
            Assert.IsTrue(EnclosureSystem.IsNestSafeRoute(RatEnclosure.Pairing,
                AtCorner(-1f,.01f),AtCorner(1f,.01f)), "A clear path above the rim stays valid.");
        }

        [TestCase(1f)]
        [TestCase(3f)]
        public void BirthApproachStillEntersTheCaregiverZone(float speed)
        {
            GrowthSystem.SetRuntimeSpeed(speed);
            behavior.transform.position = AtCorner(.3f,.3f);
            Assert.IsTrue(behavior.BeginBirthApproach(EnclosureSystem.GetNestCaregiverPosition(RatEnclosure.Pairing)));
            var update = typeof(RatHabitatBehavior).GetMethod("UpdateBirthApproach",PrivateInstance);
            for (int frame = 0; frame < 1800 && !behavior.BirthApproachAtNest; frame++)
            {
                float delta = GrowthSystem.SimulationMovementDeltaSeconds(FrameSeconds);
                update.Invoke(behavior,new object[] { delta,GrowthSystem.SimulationMovementTimeBudget(delta),
                    GrowthSystem.SimulationVisibleMovementDistanceBudget(FrameSeconds) });
            }
            Assert.IsTrue(behavior.BirthApproachAtNest);
            Assert.IsTrue(EnclosureSystem.IsInsideNestCaregiverZone(RatEnclosure.Pairing,behavior.transform.position));
        }

        private Vector3 AtCorner(float x, float z)
        {
            return new Vector3(forbidden.max.x+x,.45f,forbidden.max.z+z);
        }

        private void TravelFrame()
        {
            float delta = GrowthSystem.SimulationMovementDeltaSeconds(FrameSeconds);
            typeof(RatHabitatBehavior).GetMethod("UpdateTravel",PrivateInstance).Invoke(behavior,new object[] {
                FrameSeconds,delta,FrameSeconds,GrowthSystem.SimulationMovementTimeBudget(delta),
                GrowthSystem.SimulationVisibleMovementDistanceBudget(FrameSeconds) });
        }

        private void Set(string name, object value)
        {
            typeof(RatHabitatBehavior).GetField(name,PrivateInstance).SetValue(behavior,value);
        }

        private T Get<T>(string name)
        {
            return (T)typeof(RatHabitatBehavior).GetField(name,PrivateInstance).GetValue(behavior);
        }
    }

    public class VerticalSliceSystemTests
    {
        [Test]
        public void TankCameraPanScreenDragTracksPointerAndScalesWithZoom()
        {
            var cameraObject = new GameObject("Tank Pan Test Camera");
            var renderTexture = new RenderTexture(1000, 600, 16);
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.targetTexture = renderTexture;
                camera.orthographic = true;
                camera.orthographicSize = 8f;
                camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

                Vector2 drag = new Vector2(120f, -80f);
                Vector3 worldAtZoomOut = TankCameraPanMath.ScreenDeltaToWorldPlane(camera, drag);
                camera.orthographicSize = 4f;
                Vector3 worldAtZoomIn = TankCameraPanMath.ScreenDeltaToWorldPlane(camera, drag);

                Assert.Greater(worldAtZoomOut.magnitude, 0f);
                Assert.AreEqual(0f, worldAtZoomOut.y, 0.0001f);
                Assert.AreEqual(worldAtZoomOut.magnitude * 0.5f,
                    worldAtZoomIn.magnitude, 0.0001f,
                    "Panning preserves the current zoom: the same pixel drag covers less world distance when zoomed in.");
                Assert.Greater(Vector3.Dot(worldAtZoomOut, camera.transform.right * drag.x +
                    camera.transform.up * drag.y), 0f,
                    "The world translation must follow the pointer's screen direction before the camera applies its inverse pan.");
                Assert.AreEqual(4f, camera.orthographicSize, 0.0001f,
                    "Pan calculations do not modify the zoom level, including after zoom-in.");
            }
            finally
            {
                cameraObject.GetComponent<Camera>().targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
        }

        [Test]
        public void TankCameraPanClampsEveryPlayerTankAndNeverLeavesItsBounds()
        {
            foreach (RatEnclosure enclosure in EnclosureSystem.AllEnclosures)
            {
                EnclosureSystem.Definition bounds = EnclosureSystem.GetDefinition(enclosure);
                Vector3 clamped = TankCameraPanMath.ClampTargetToEnclosure(enclosure,
                    new Vector3(bounds.maxX + 1000f, 50f, bounds.minZ - 1000f));
                Assert.That(clamped.x, Is.InRange(bounds.minX, bounds.maxX), enclosure + " horizontal tank bounds");
                Assert.That(clamped.z, Is.InRange(bounds.minZ, bounds.maxZ), enclosure + " depth tank bounds");
                Assert.That(clamped.y, Is.InRange(0.35f, 1.25f), enclosure + " camera height");
            }
        }

        [Test]
        public void TankCameraPanRecentersOnTankOrRatChange()
        {
            var pan = new TankCameraPanState();

            Assert.IsTrue(pan.SetFocus(RatEnclosure.Pairing, null, false));
            pan.SetOffset(new Vector3(2f, 0f, -4f));
            Assert.IsFalse(pan.SetFocus(RatEnclosure.Pairing, null, false));
            Assert.AreEqual(new Vector3(2f, 0f, -4f), pan.Offset,
                "Ordinary updates in the same tank must preserve pan.");

            Assert.IsTrue(pan.SetFocus(RatEnclosure.Breeding, null, false),
                "Switching to the For Sale tank recenters the camera.");
            Assert.AreEqual(Vector3.zero, pan.Offset);
            pan.SetOffset(new Vector3(-1f, 0f, 1f));
            Assert.IsTrue(pan.SetFocus(RatEnclosure.Breeding, "selected-rat", false),
                "Selecting a rat recenters the camera on its inspection target.");
            Assert.AreEqual(Vector3.zero, pan.Offset);
            pan.SetOffset(Vector3.one);
            pan.Recenter();
            Assert.AreEqual(Vector3.zero, pan.Offset,
                "Re-selecting the same rat explicitly recenters the pan without requiring an ID change.");
        }

        [Test]
        public void TankCameraPanStartsOnlyOnWorldAndConsumesDragBeforeSelection()
        {
            Assert.IsTrue(InteractionManager.CanBeginTankPan(true, false, false));
            Assert.IsFalse(InteractionManager.CanBeginTankPan(true, false, true),
                "Buttons, cards, lists, and other raycast UI own their pointer gesture.");
            Assert.IsFalse(InteractionManager.CanBeginTankPan(true, true, false),
                "A modal blocks world input.");
            Assert.IsFalse(InteractionManager.CanBeginTankPan(false, false, false));
            Assert.IsFalse(InteractionManager.HasExceededTankPanThreshold(Vector2.zero,
                new Vector2(8f, 0f), 8f), "A desktop click remains a click below the drag threshold.");
            Assert.IsTrue(InteractionManager.HasExceededTankPanThreshold(Vector2.zero,
                new Vector2(9f, 0f), 8f), "Desktop mouse drag begins after its click threshold.");
            Assert.IsFalse(InteractionManager.HasExceededTankPanThreshold(Vector2.zero,
                new Vector2(24f, 0f), 24f), "A mobile tap remains a tap below the touch drag threshold.");
            Assert.IsTrue(InteractionManager.HasExceededTankPanThreshold(Vector2.zero,
                new Vector2(25f, 0f), 24f), "Touch drag begins after its tap threshold.");

            Assert.IsTrue(InteractionManager.ShouldDispatchWorldSelection(false, false, false, false),
                "A stationary world tap can still select a rat.");
            Assert.IsFalse(InteractionManager.ShouldDispatchWorldSelection(false, false, true, true),
                "A drag that panned the tank must not also select a rat.");
            Assert.IsFalse(InteractionManager.ShouldDispatchWorldSelection(false, true, true, true),
                "Dragging from world onto a button must not activate that button or select a rat.");
            Assert.IsFalse(InteractionManager.ShouldDispatchWorldSelection(true, false, true, false),
                "A gesture that began on UI remains UI-owned even if released over the tank.");
        }

        [Test]
        public void PerformanceProbeNamesStayAlignedAndInvalidAreasAreIgnored()
        {
            FieldInfo namesField = typeof(RuntimePerformanceDiagnostics).GetField(
                "SampleNames", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(namesField, "Development builds must retain the profiler sample name table.");
            string[] names = namesField.GetValue(null) as string[];
            Assert.IsNotNull(names);
            Assert.AreEqual((int)PerformanceProbeArea.Count, names.Length,
                "Every probe enum value must have exactly one profiler name.");
            Assert.AreEqual("Rat Empire/Measured/Maintenance Historical Rat Index",
                names[(int)PerformanceProbeArea.MaintenanceHistoricalRatIndex]);

            Assert.AreEqual(0L, RuntimePerformanceDiagnostics.Begin((PerformanceProbeArea)(-1)),
                "Negative probe values must be ignored without opening a profiler sample.");
            Assert.AreEqual(0L, RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.Count),
                "The Count sentinel is not a valid probe and must be ignored.");
            Assert.AreEqual(0L, RuntimePerformanceDiagnostics.Begin((PerformanceProbeArea)int.MaxValue),
                "Out-of-range probe values must be ignored without throwing.");
        }

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
                        CurrentPanel = "Tank",
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
                    Assert.AreEqual(3,
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
                    Assert.AreEqual(1,
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
                Assert.AreEqual(3,
                    GrowthSystem.LastSimulationStepCount - stepsBefore,
                    "Large elapsed intervals use only the rat's share of the fixed colony-wide work budget.");
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
        public void DailyMaintenanceDeadlineSkipsMissedIntervalsWithoutDroppingGameTime()
        {
            long interval = GameConfig.GameDayMs;
            long firstDeadline = 100L * interval;
            long currentGameTime = firstDeadline + interval * 23L + interval / 3L;

            long nextDeadline = GrowthSystem.AdvanceDeadlinePastNow(firstDeadline, interval, currentGameTime);

            Assert.Greater(nextDeadline, currentGameTime,
                "A coalesced maintenance deadline must be advanced beyond the authoritative current game time.");
            Assert.AreEqual(firstDeadline + interval * 24L, nextDeadline,
                "Skipped daily passes are not replayed, and the original calendar cadence is preserved.");
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
                    8f, -1f, 0, 0, 0, 0, 20, 2, 20, 60, 200, 2, 240, 4f, 1f, 2f, "Tank");
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
                    0f, -1f, 0, 0, 0, 0, 14, 0, 14, 42, 160, 2, 854, 0f, 0f, 0f, "Tank");
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
                    CurrentPanel = "Tank",
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

        [TestCase(2f, RatSex.Female, 425f, "Age: 2 days - Pinkie")]
        [TestCase(44f, RatSex.Female, 425f, "Age: 1 month, 2 weeks - Young Rat")]
        [TestCase(210f, RatSex.Female, 425f, "Age: 7 months - Adult")]
        [TestCase(425f, RatSex.Female, 425f, "Age: 1 year, 2 months - Elderly")]
        public void MyRatsRosterShowsAgeAndAuthoritativeStageOnOneLine(
            float ageDays, RatSex sex, float breedingEndAgeDays, string expected)
        {
            var rat = new RatData
            {
                id = "roster-age-stage-test",
                name = "Roster Test",
                sex = sex,
                ageDays = ageDays,
                breedingEndAgeDays = breedingEndAgeDays,
                // Deliberately stale: the roster must derive stage from the
                // authoritative individualized age rules, not this label.
                stage = RatStage.Pinkie,
            };
            var formatter = typeof(VerticalSliceUI).GetMethod(
                "BuildRatRosterAgeStage",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(formatter);
            Assert.AreEqual(expected, formatter.Invoke(null, new object[] { rat }));
        }

        [Test]
        public void MyRatsRosterFormatsStatsDirectlyAsTheSecondSummaryLine()
        {
            var rat = new RatData
            {
                id = "roster-stats-test",
                traits = new TraitData(32f, 29f, 28f),
            };
            var formatter = typeof(VerticalSliceUI).GetMethod(
                "BuildRatRosterStats",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(formatter);
            Assert.AreEqual("Size 32 • Health 29 • Fertility 28", formatter.Invoke(null, new object[] { rat }));
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
                // Give one summary a deliberately long age string and stale
                // saved stage so this same list test covers wrapping and the
                // authoritative stage formatter on a narrow phone layout.
                save.rats[0].ageDays = 10000f;
                save.rats[0].breedingEndAgeDays = 425f;
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
                RectTransform firstRow = rosterScroll.content.GetChild(0) as RectTransform;
                RectTransform firstHeader = firstRow == null ? null : firstRow.GetChild(0) as RectTransform;
                RectTransform firstInfo = firstHeader == null ? null : firstHeader.Find("My Rats Basic Information") as RectTransform;
                Assert.IsNotNull(firstInfo);
                Text[] summaryLines = firstInfo.GetComponentsInChildren<Text>(true);
                Assert.GreaterOrEqual(summaryLines.Length, 4);
                StringAssert.Contains(ColonyFactory.DisplayName(save.rats[0]), summaryLines[0].text,
                    "The roster preserves the same resolved display name as the colony data.");
                StringAssert.Contains("Female", summaryLines[0].text,
                    "Sex stays visible alongside the rat name after the compact layout change.");
                Assert.AreEqual("Age: 27 years, 4 months - Elderly", summaryLines[1].text,
                    "A stale saved stage must not override the rat's current age and individualized cutoff.");
                Assert.AreEqual("Size 10 • Health 20 • Fertility 30", summaryLines[2].text,
                    "Stats immediately follow the combined age/stage line.");
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

                canvasRect.sizeDelta = new Vector2(320f, 640f);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterScroll.content);
                Canvas.ForceUpdateCanvases();
                Assert.GreaterOrEqual(firstHeader.rect.height, firstInfo.rect.height + 13f,
                    "The card header must expand around wrapped long-age/stats text instead of clipping it on mobile.");

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
        public void SoldRatIsRemovedFromPresentationSelectionAndMovementButHistoryRemains()
        {
            var presenterObject = new GameObject("Sold Rat Presenter Test");
            var root = new GameObject("Sold Rat Existing Visual", typeof(BoxCollider));
            try
            {
                root.transform.SetParent(presenterObject.transform, false);
                SelectableEntity selectable = root.AddComponent<SelectableEntity>();
                selectable.Configure(SelectableKind.Rat, "sold-rat-test", "Sold Rat");
                RatVisualController controller = root.AddComponent<RatVisualController>();
                RatHabitatBehavior behavior = root.AddComponent<RatHabitatBehavior>();
                BoxCollider collider = root.GetComponent<BoxCollider>();

                RatPresenter presenter = presenterObject.AddComponent<RatPresenter>();
                var rat = new RatData
                {
                    id = "sold-rat-test",
                    name = "Sold Rat",
                    sex = RatSex.Female,
                    stage = RatStage.Adult,
                    motherId = "historical-mother",
                    fatherId = "historical-father",
                    removalDisposition = RatRemovalDisposition.Sold,
                    enclosure = RatEnclosure.ForSale,
                };
                var save = new ColonySaveData
                {
                    clock = new ClockData { gameTimeMs = GameConfig.StartGameTimeMs, speed = 1f },
                };
                save.EnsureLists();
                save.rats.Add(rat);
                save.eventLog.Add(new ColonyEventData { message = "Sold Rat was sold" });

                GetPrivateField<Dictionary<string, GameObject>>(presenter, "ratRoots")[rat.id] = root;
                GetPrivateField<Dictionary<string, RatVisualController>>(presenter, "visualControllers")[rat.id] = controller;
                GetPrivateField<Dictionary<string, RatHabitatBehavior>>(presenter, "behaviors")[rat.id] = behavior;
                GetPrivateField<Dictionary<string, RatData>>(presenter, "liveRats")[rat.id] = rat;

                presenter.Render(save, Vector3.zero);

                Assert.IsFalse(root.activeSelf,
                    "A retired rat's existing visual root must be hidden immediately, before deferred destruction.");
                Assert.IsFalse(collider.enabled,
                    "A retired rat must no longer have an active selection collider.");
                Assert.IsFalse(behavior.enabled,
                    "A retired rat's movement behavior must stop immediately.");
                Assert.IsFalse(controller.enabled,
                    "A retired rat's visual controller must stop immediately.");
                Assert.IsFalse(presenter.TryGetRatRoot(rat.id, out Transform ignoredRoot),
                    "Retired rats must not be selectable through the presenter.");
                Assert.IsFalse(presenter.TryGetRatBehavior(rat.id, out RatHabitatBehavior ignoredBehavior),
                    "Retired rats must not be included in movement behavior lookup.");
                Assert.IsFalse(GetPrivateField<Dictionary<string, GameObject>>(presenter, "ratRoots").ContainsKey(rat.id));
                Assert.IsFalse(GetPrivateField<Dictionary<string, RatVisualController>>(presenter, "visualControllers").ContainsKey(rat.id));
                Assert.IsFalse(GetPrivateField<Dictionary<string, RatHabitatBehavior>>(presenter, "behaviors").ContainsKey(rat.id));
                Assert.IsFalse(GetPrivateField<Dictionary<string, RatData>>(presenter, "liveRats").ContainsKey(rat.id));
                Assert.AreSame(rat, save.rats[0], "Presentation cleanup must preserve the historical rat record.");
                Assert.AreEqual("historical-mother", rat.motherId);
                Assert.AreEqual("historical-father", rat.fatherId);
                Assert.AreEqual("Sold Rat was sold", save.eventLog[0].message,
                    "Presentation cleanup must preserve historical event records.");
            }
            finally
            {
                Object.DestroyImmediate(presenterObject);
            }
        }

        [Test]
        public void PresenterRecoversMissingBehaviorForLiveAdultRoot()
        {
            var presenterObject = new GameObject("Birth Route Behavior Recovery Presenter");
            var root = new GameObject("Live Mother Root");
            var duplicate = new GameObject("Abandoned Live Mother Root");
            try
            {
                RatPresenter presenter = presenterObject.AddComponent<RatPresenter>();
                root.transform.SetParent(presenterObject.transform, false);
                duplicate.transform.SetParent(presenterObject.transform, false);
                RatHabitatBehavior existingBehavior = root.AddComponent<RatHabitatBehavior>();
                existingBehavior.enabled = false;
                var mother = new RatData
                {
                    id = "birth-route-recovery-mother",
                    name = "Live Mother",
                    sex = RatSex.Female,
                    stage = RatStage.Adult,
                    removalDisposition = RatRemovalDisposition.None,
                    enclosure = RatEnclosure.Pairing,
                };
                root.AddComponent<SelectableEntity>().Configure(
                    SelectableKind.Rat, mother.id, ColonyFactory.DisplayName(mother));
                duplicate.AddComponent<SelectableEntity>().Configure(
                    SelectableKind.Rat, mother.id, ColonyFactory.DisplayName(mother));

                GetPrivateField<Dictionary<string, GameObject>>(presenter, "ratRoots")[mother.id] = root;
                GetPrivateField<Dictionary<string, RatData>>(presenter, "liveRats")[mother.id] = mother;

                Assert.IsTrue(presenter.TryGetRatBehavior(mother.id, out RatHabitatBehavior recovered));
                Assert.AreSame(existingBehavior, recovered,
                    "A live root's existing component should be recovered rather than duplicated.");
                Assert.IsTrue(recovered.enabled,
                    "A live adult's stale disabled behavior must be re-enabled for birth-route retries.");
                Assert.IsTrue(GetPrivateField<Dictionary<string, RatHabitatBehavior>>(presenter, "behaviors")
                    .ContainsKey(mother.id), "Recovered behavior must restore the presenter lookup index.");

                GetPrivateField<Dictionary<string, RatHabitatBehavior>>(presenter, "behaviors").Remove(mother.id);
                GetPrivateField<Dictionary<string, RatData>>(presenter, "liveRats").Remove(mother.id);
                GetPrivateField<Dictionary<string, GameObject>>(presenter, "ratRoots").Remove(mother.id);
                GetPrivateField<Dictionary<string, RatVisualController>>(presenter, "visualControllers").Remove(mother.id);
                existingBehavior.enabled = false;
                Assert.IsFalse(presenter.TryGetRatBehavior(mother.id, out RatHabitatBehavior staleIndex));
                Assert.IsTrue(presenter.TryGetRatBehavior(mother.id, mother, out recovered),
                    "Birth recovery must rebuild stale presenter indexes from the authoritative mother record and scene root.");
                Assert.AreSame(existingBehavior, recovered);
                Assert.IsTrue(recovered.enabled);
                Assert.IsTrue(GetPrivateField<Dictionary<string, RatData>>(presenter, "liveRats")
                    .ContainsKey(mother.id));
                Assert.IsTrue(presenter.TryGetRatRoot(mother.id, out Transform recoveredRoot));
                Assert.AreSame(root.transform, recoveredRoot,
                    "Recovery must prefer the root that still owns the live movement behavior.");
                Assert.IsFalse(duplicate.activeSelf,
                    "Abandoned duplicate visuals for the same stable rat ID must be retired immediately.");
                Assert.AreEqual(1, presenterObject.GetComponentsInChildren<SelectableEntity>().Length,
                    "One authoritative rat ID must not retain multiple selectable world roots.");
            }
            finally
            {
                Object.DestroyImmediate(presenterObject);
            }
        }

        [Test]
        public void PresenterRenderDoesNotCreateSecondRootWhenControllerIndexIsMissing()
        {
            var presenterObject = new GameObject("Missing Controller Presenter Test");
            var root = new GameObject("Existing Mother Root");
            try
            {
                RatPresenter presenter = presenterObject.AddComponent<RatPresenter>();
                root.transform.SetParent(presenterObject.transform, false);
                root.AddComponent<SelectableEntity>().Configure(
                    SelectableKind.Rat, "controller-index-mother", "Controller Index Mother");
                root.AddComponent<RatVisualController>();
                var mother = new RatData
                {
                    id = "controller-index-mother",
                    name = "Controller Index Mother",
                    sex = RatSex.Female,
                    stage = RatStage.Adult,
                    removalDisposition = RatRemovalDisposition.None,
                    enclosure = RatEnclosure.Pairing,
                };
                var save = new ColonySaveData
                {
                    clock = new ClockData { gameTimeMs = GameConfig.StartGameTimeMs, speed = 1f },
                };
                save.EnsureLists();
                save.rats.Add(mother);
                GetPrivateField<Dictionary<string, GameObject>>(presenter, "ratRoots")[mother.id] = root;
                GetPrivateField<Dictionary<string, RatData>>(presenter, "liveRats")[mother.id] = mother;

                presenter.Render(save, Vector3.zero);

                Assert.IsTrue(presenter.TryGetRatRoot(mother.id, out Transform indexedRoot));
                Assert.AreSame(root.transform, indexedRoot,
                    "Restoring a missing controller index must keep the stable existing root.");
                Assert.AreEqual(1, presenterObject.GetComponentsInChildren<SelectableEntity>().Length,
                    "The missing-controller repair must not create a duplicate visual root.");
                Assert.IsTrue(GetPrivateField<Dictionary<string, RatVisualController>>(presenter,
                    "visualControllers").ContainsKey(mother.id));
                Assert.IsTrue(GetPrivateField<Dictionary<string, RatHabitatBehavior>>(presenter,
                    "behaviors").ContainsKey(mother.id));
            }
            finally
            {
                Object.DestroyImmediate(presenterObject);
            }
        }

        [Test]
        public void SimultaneousDueBirthsRetryFailedTransactionInPlaceBeforeAdvancingQueue()
        {
            const long now = 680000000L;
            ColonySaveData save = CreatePairingTestSave(now, 100f);
            RatData firstMother = save.rats[0];
            RatData sharedFather = save.rats[1];
            save.rats.Remove(sharedFather);
            save.ratIds.Remove(sharedFather.id);

            var mothers = new List<RatData> { firstMother };
            for (int index = 1; index < 3; index++)
            {
                RatData mother = CreateSaleTestRat(save, "queued-birth-mother-" + index,
                    RatSex.Female, 100f, RatStage.Adult);
                mother.enclosure = RatEnclosure.Pairing;
                mother.pairingHabitatAssigned = true;
                mother.reproductiveState = ReproductiveState.Fertile;
                mother.estrousCycleAnchorGameTime = now;
                mother.breedingEndAgeDays = 365f;
                mothers.Add(mother);
            }

            var pregnancies = new List<PregnancyData>();
            foreach (RatData mother in mothers)
            {
                PregnancyData pregnancy = AddActiveSaleTestPregnancy(save, mother, sharedFather, now);
                pregnancy.startedAt = now - GameConfig.PregnancyMs;
                pregnancy.dueAt = now;
                pregnancies.Add(pregnancy);
            }

            var presenterObject = new GameObject("Retrying Birth Queue Presenter");
            var bootstrapObject = new GameObject("Retrying Birth Queue Bootstrap");
            bootstrapObject.SetActive(false);
            var roots = new List<GameObject>();
            var behaviors = new List<RatHabitatBehavior>();
            try
            {
                RatPresenter presenter = presenterObject.AddComponent<RatPresenter>();
                Dictionary<string, GameObject> ratRoots = GetPrivateField<Dictionary<string, GameObject>>(presenter, "ratRoots");
                Dictionary<string, RatHabitatBehavior> behaviorMap = GetPrivateField<Dictionary<string, RatHabitatBehavior>>(presenter, "behaviors");
                Dictionary<string, RatData> liveRats = GetPrivateField<Dictionary<string, RatData>>(presenter, "liveRats");
                Vector3 nestPosition = EnclosureSystem.GetNestCaregiverPosition(RatEnclosure.Pairing);
                for (int index = 0; index < mothers.Count; index++)
                {
                    RatData mother = mothers[index];
                    var root = new GameObject("Birth Queue Mother " + index);
                    roots.Add(root);
                    root.transform.SetParent(presenterObject.transform, false);
                    root.transform.position = index == 0
                        ? nestPosition
                        : nestPosition + new Vector3(index * 1.5f, 0f, 0f);
                    RatHabitatBehavior behavior = root.AddComponent<RatHabitatBehavior>();
                    behavior.Configure(null, mother);
                    behaviors.Add(behavior);
                    ratRoots[mother.id] = root;
                    behaviorMap[mother.id] = behavior;
                    liveRats[mother.id] = mother;
                }

                GameBootstrap bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
                SetPrivateField(bootstrap, "<Save>k__BackingField", save);
                SetPrivateField(bootstrap, "rats", presenter);

                // Restore the missing historical parent only after the first
                // transaction fails, making that failure transient and
                // allowing the same pregnancy to complete on retry.
                LogAssert.Expect(LogType.Warning,
                    "[Rat Habitat] Birth retry " + pregnancies[0].id + ": Parent records are missing.");
                LogAssert.Expect(LogType.Warning,
                    "[Rat Habitat] FinishPregnancyForBatch failed | pregnancyId=" + pregnancies[0].id +
                    " | motherId=" + firstMother.id + " | tank=Pairing Tank | reason=Parent records are missing.");
                MethodInfo process = typeof(GameBootstrap).GetMethod(
                    "ProcessDuePregnanciesAtNest", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(process);
                object[] arguments = { null, false };
                int births = (int)process.Invoke(bootstrap, arguments);

                Assert.AreEqual(0, births);
                Assert.AreEqual(0, ((List<LitterData>)arguments[0]).Count,
                    "A failed transaction must not be returned for pinkie presentation creation.");
                Assert.AreEqual("pending", pregnancies[0].status,
                    "A failed birth keeps its authoritative pregnancy for retry.");
                Assert.AreEqual(1, pregnancies[0].birthAttemptCount,
                    "The failed pregnancy must reach the actual birth transaction.");
                Assert.IsTrue(pregnancies[0].birthWaitingForNest,
                    "The failed transaction is exposed as an explicit safe waiting state.");
                Assert.IsTrue(pregnancies[0].birthApproachStarted,
                    "The persisted approach must remain active while its mother waits at the nest.");
                Assert.IsTrue(behaviors[0].BirthApproachActive);
                Assert.IsTrue(behaviors[0].BirthApproachAtNest);
                Assert.AreEqual(nestPosition, roots[0].transform.position,
                    "A failed transaction must not send the mother back to wandering.");
                Assert.AreEqual(0, save.litters.Count);
                Assert.AreEqual(3, save.rats.Count,
                    "No pups are created by the failed transaction.");
                Assert.IsTrue(GetPrivateField<BirthQueueReservations>(bootstrap, "birthQueueReservations")
                    .IsReservedBy(RatEnclosure.Pairing, firstMother.id),
                    "The failed mother retains the only nest approach reservation.");
                Assert.IsFalse(behaviors[1].BirthApproachActive);
                Assert.IsFalse(behaviors[2].BirthApproachActive,
                    "Later mothers must remain queued until the current birth commits.");

                MethodInfo refreshActivities = typeof(GameBootstrap).GetMethod(
                    "RefreshRatActivities", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(refreshActivities);
                refreshActivities.Invoke(bootstrap, null);
                Assert.AreEqual("Waiting to give birth", bootstrap.CurrentRatActivityLabel(firstMother),
                    "An unrelated activity refresh must not erase the explicit waiting state.");
                Assert.IsTrue(behaviors[0].BirthApproachActive);
                Assert.AreEqual(nestPosition, roots[0].transform.position);

                save.rats.Add(sharedFather);
                save.ratIds.Add(sharedFather.id);
                BreedingSystem.InvalidateReproductiveStateIndexes(save);
                GetPrivateField<Dictionary<string, float>>(bootstrap, "birthRetryAfterRealtime")[pregnancies[0].id] =
                    Time.unscaledTime - 1f;

                arguments = new object[] { null, false };
                births = (int)process.Invoke(bootstrap, arguments);
                Assert.AreEqual(1, births,
                    "After the transient parent-record issue is repaired, the first birth commits before queue advance.");
                Assert.AreEqual(1, ((List<LitterData>)arguments[0]).Count,
                    "Only the committed litter is returned for the subsequent presentation pass.");
                Assert.AreEqual("finished", pregnancies[0].status);
                Assert.IsFalse(pregnancies[0].birthApproachStarted);
                Assert.IsFalse(pregnancies[0].birthWaitingForNest);
                Assert.AreEqual(1, save.litters.Count);
                Assert.AreEqual(3, save.litters[0].pupIds.Count);
                foreach (string pupId in save.litters[0].pupIds)
                    Assert.IsNotNull(BreedingSystem.FindRat(save, pupId),
                        "Every successful birth must create a live pinkie record.");
                Assert.IsTrue(behaviors[1].BirthApproachActive,
                    "Only after the first transaction succeeds may the next mother approach.");

                for (int index = 1; index < mothers.Count; index++)
                {
                    roots[index].transform.position = nestPosition;
                    Assert.IsTrue(behaviors[index].BeginBirthApproach(nestPosition));
                    Assert.IsTrue(behaviors[index].BirthApproachAtNest);
                    arguments = new object[] { null, false };
                    births = (int)process.Invoke(bootstrap, arguments);
                    Assert.AreEqual(1, births,
                        "Each queued mother must reach and complete the birth transaction in turn.");
                    Assert.AreEqual("finished", pregnancies[index].status);
                    Assert.AreEqual(1, pregnancies[index].birthAttemptCount);
                    Assert.IsFalse(pregnancies[index].birthApproachStarted);
                    Assert.IsFalse(pregnancies[index].birthWaitingForNest);
                    Assert.AreEqual(index + 1, save.litters.Count);
                    Assert.AreEqual(pregnancies[index].expectedLitterSize,
                        save.litters[index].pupIds.Count);
                }

                Assert.AreEqual(3, GetPrivateField<int>(bootstrap, "birthQueueSuccessfulBirths"));
                for (int index = 0; index < pregnancies.Count; index++)
                {
                    Assert.AreEqual("finished", pregnancies[index].status);
                    Assert.IsFalse(pregnancies[index].birthApproachStarted,
                        "No completed pregnancy remains stuck in birth-approach state.");
                    Assert.AreEqual(pregnancies[index].expectedLitterSize,
                        save.litters.Find(litter => litter.id == pregnancies[index].litterId).pupIds.Count);
                }
            }
            finally
            {
                Object.DestroyImmediate(bootstrapObject);
                Object.DestroyImmediate(presenterObject);
                for (int index = 0; index < roots.Count; index++)
                    if (roots[index] != null) Object.DestroyImmediate(roots[index]);
            }
        }

        [Test]
        public void StoreMarketAndSellListingsUseIndependentScrollableViewports()
        {
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            Func<ColonySaveData, string, bool> previousSaveInterceptor = SaveSystem.SaveInterceptorForTests;
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
                SetPrivateField(game, "ui", ui);
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

                float buyContentOffset = marketScroll.content.anchoredPosition.y;
                var setStoreCategory = typeof(VerticalSliceUI).GetMethod(
                    "SetStoreCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(setStoreCategory);
                setStoreCategory.Invoke(ui, new[] { System.Enum.Parse(storeCategoryType, "Sell") });
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
                Canvas.ForceUpdateCanvases();
                ScrollRect sellScroll = GetPrivateField<ScrollRect>(ui, "storeRatListScroll");
                float sellMaxScroll = Mathf.Max(0f,
                    sellScroll.content.rect.height - sellScroll.viewport.rect.height);
                Assert.AreEqual(Mathf.Clamp(buyContentOffset, 0f, sellMaxScroll),
                    sellScroll.content.anchoredPosition.y, 1f,
                    "Switching from Buy to Sell preserves the nearest content-space position.");
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

                RatData ratToConfirm = StoreSystem.GetSellableRats(
                    save, game.GameTime, StoreSellFilter.All)[11];
                RectTransform normalCard = sellScroll.content.Find(
                    "Store Management Card " + ratToConfirm.id) as RectTransform;
                Assert.IsNotNull(normalCard);
                float normalCardHeight = normalCard.GetComponent<LayoutElement>().preferredHeight;
                float normalCardRectHeight = normalCard.rect.height;

                // Put the final row near the bottom so the subsequent sale
                // removes content below the current position and requires a
                // clamp to the new bottom rather than a jump to the top.
                sellScroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                float beforeConfirmationOffset = sellScroll.content.anchoredPosition.y;
                SaveSystem.SaveInterceptorForTests = (testSave, source) => true;
                game.RequestSellRat(ratToConfirm.id);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
                Canvas.ForceUpdateCanvases();
                ScrollRect confirmationScroll = GetPrivateField<ScrollRect>(ui, "storeRatListScroll");
                Assert.AreEqual(beforeConfirmationOffset, confirmationScroll.content.anchoredPosition.y, 1f,
                    "Selecting a rat for sale preserves the current list offset.");
                RectTransform confirmationCard = confirmationScroll.content.Find(
                    "Store Management Card " + ratToConfirm.id) as RectTransform;
                Assert.IsNotNull(confirmationCard);
                Assert.AreEqual(normalCardHeight,
                    confirmationCard.GetComponent<LayoutElement>().preferredHeight,
                    0.01f,
                    "Replacing SELL with Confirm/Cancel must preserve the card's preferred height.");
                Assert.AreEqual(normalCardRectHeight, confirmationCard.rect.height, 1f,
                    "Entering sale confirmation must not expand the rat row or shift neighboring listings.");

                game.ConfirmSellSelectedRat();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
                Canvas.ForceUpdateCanvases();
                ScrollRect postSaleScroll = GetPrivateField<ScrollRect>(ui, "storeRatListScroll");
                float postSaleMaxScroll = Mathf.Max(0f,
                    postSaleScroll.content.rect.height - postSaleScroll.viewport.rect.height);
                Assert.AreEqual(Mathf.Clamp(beforeConfirmationOffset, 0f, postSaleMaxScroll),
                    postSaleScroll.content.anchoredPosition.y, 1f,
                    "Confirming a sale preserves the old offset and clamps only when the removed row shortens the list.");
                Assert.IsNull(postSaleScroll.content.Find("Store Management Card " + ratToConfirm.id),
                    "The sold row should be removed while the remaining list stays at the nearest position.");
                Assert.AreEqual(11, postSaleScroll.content.childCount);
            }
            finally
            {
                SaveSystem.SaveInterceptorForTests = previousSaveInterceptor;
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        public void StorePortraitRowsRetryMissingFactoryAndHideUnrenderedTargets()
        {
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            var uiObject = new GameObject("Store Portrait Retry UI", typeof(RectTransform));
            var factoryObject = new GameObject("Store Portrait Retry Factory");
            try
            {
                LogAssert.ignoreFailingMessages = true;
                VerticalSliceUI ui = uiObject.AddComponent<VerticalSliceUI>();
                RatPortraitPreview preview = uiObject.AddComponent<RatPortraitPreview>();
                RatVisualFactory factory = factoryObject.AddComponent<RatVisualFactory>();
                factory.handPaintedRatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/HandPaintedRat/HandPaintedRat.prefab");
                Assert.IsNotNull(factory.handPaintedRatPrefab);

                var portraitObject = new GameObject("Deferred Store Portrait",
                    typeof(RectTransform), typeof(RawImage));
                portraitObject.transform.SetParent(uiObject.transform, false);
                RawImage image = portraitObject.GetComponent<RawImage>();
                RatData rat = ColonyFactory.CreateRat(
                    "store-portrait-retry", "Portrait Retry", RatSex.Female, 0L, 0,
                    GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s"),
                    new TraitData(50f, 50f, 50f), RatStage.Adult);

                Type rowType = typeof(VerticalSliceUI).GetNestedType(
                    "StorePurchaseRowView", BindingFlags.NonPublic);
                Assert.IsNotNull(rowType);
                object row = Activator.CreateInstance(rowType, true);
                rowType.GetField("portrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SetValue(row, image);
                rowType.GetField("portraitRat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SetValue(row, rat);
                var rows = (System.Collections.IList)typeof(VerticalSliceUI).GetField(
                    "storePurchaseRows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
                rows.Add(row);

                SetPrivateField(ui, "portraitPreview", preview);
                SetPrivateField(ui, "activeMainPanel", Enum.Parse(
                    typeof(VerticalSliceUI).GetNestedType("MainPanel", BindingFlags.NonPublic), "Store"));
                SetPrivateField(ui, "storeCategory", Enum.Parse(
                    typeof(VerticalSliceUI).GetNestedType("StoreCategory", BindingFlags.NonPublic), "Buy"));
                SetPrivateField(ui, "missingStorePortraitRetryTimer", -1f);

                var retry = typeof(VerticalSliceUI).GetMethod(
                    "RetryMissingStorePortraits", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(retry);
                retry.Invoke(ui, null);
                Assert.IsNull(image.texture,
                    "A request made before the preview factory is configured should remain retryable.");
                Assert.AreEqual(new Color(0.10f, 0.18f, 0.17f, 1f), image.color,
                    "An unavailable portrait must not display RawImage's white null-texture box.");

                preview.Configure(factory);
                SetPrivateField(ui, "missingStorePortraitRetryTimer", -1f);
                retry.Invoke(ui, null);
                Assert.IsInstanceOf<RenderTexture>(image.texture,
                    "The row should recover its portrait once the presentation factory is ready.");
                Assert.IsTrue(preview.IsPortraitRendered(image.texture),
                    "The recovered target should have completed its first portrait render.");
                Assert.AreEqual(Color.white, image.color);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
                Object.DestroyImmediate(uiObject);
                Object.DestroyImmediate(factoryObject);
            }
        }

        [Test]
        public void MaximumUpgradedStoreRestockReusesRowsAndDoesNotQueueRepeatedSaves()
        {
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            var gameObject = new GameObject("Large Store Restock Test Game");
            var canvasObject = new GameObject("Large Store Restock Test Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            try
            {
                LogAssert.ignoreFailingMessages = true;
                gameObject.SetActive(false);
                GameBootstrap game = gameObject.AddComponent<GameBootstrap>();
                ColonySaveData save = ColonyFactory.CreateNew(1000000L);
                save.colonyCredits = 1000000;
                // Store capacity has no configured ceiling. Exercise a large
                // upgraded market (64 slots) without changing the +1/upgrade rule.
                save.storeQualityUpgradeLevel = 62;
                StoreSystem.RestockNowWithResult(save, save.clock.gameTimeMs);
                Assert.AreEqual(64, save.storeRatListings.Count);
                typeof(GameBootstrap).GetProperty("Save").GetSetMethod(true)
                    .Invoke(game, new object[] { save });

                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(540f, 960f);
                var pageObject = new GameObject("Large Store Test Page",
                    typeof(RectTransform), typeof(ScrollRect));
                pageObject.transform.SetParent(canvasObject.transform, false);
                RectTransform pageRect = pageObject.GetComponent<RectTransform>();
                pageRect.anchorMin = Vector2.zero;
                pageRect.anchorMax = Vector2.one;
                pageRect.offsetMin = Vector2.zero;
                pageRect.offsetMax = new Vector2(0f, -154f);
                ScrollRect pageScroll = pageObject.GetComponent<ScrollRect>();
                var viewportObject = new GameObject("Large Store Page Viewport",
                    typeof(RectTransform), typeof(Image), typeof(RectMask2D));
                viewportObject.transform.SetParent(pageObject.transform, false);
                RectTransform pageViewport = viewportObject.GetComponent<RectTransform>();
                pageViewport.anchorMin = Vector2.zero;
                pageViewport.anchorMax = Vector2.one;
                pageViewport.offsetMin = Vector2.zero;
                pageViewport.offsetMax = Vector2.zero;
                var contentObject = new GameObject("Large Store Page Content",
                    typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                contentObject.transform.SetParent(pageViewport, false);
                RectTransform pageContent = contentObject.GetComponent<RectTransform>();
                pageContent.anchorMin = new Vector2(0f, 1f);
                pageContent.anchorMax = new Vector2(1f, 1f);
                pageContent.pivot = new Vector2(0.5f, 1f);
                pageContent.sizeDelta = Vector2.zero;
                VerticalLayoutGroup pageLayout = contentObject.GetComponent<VerticalLayoutGroup>();
                pageLayout.childControlWidth = true;
                pageLayout.childControlHeight = true;
                pageLayout.childForceExpandWidth = true;
                pageLayout.childForceExpandHeight = false;
                contentObject.GetComponent<ContentSizeFitter>().verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
                pageScroll.viewport = pageViewport;
                pageScroll.content = pageContent;
                pageScroll.vertical = true;

                VerticalSliceUI ui = canvasObject.AddComponent<VerticalSliceUI>();
                SetPrivateField(ui, "game", game);
                SetPrivateField(ui, "content", pageContent);
                SetPrivateField(ui, "pageScroll", pageScroll);
                SetPrivateField(ui, "activeMainPanel", Enum.Parse(
                    typeof(VerticalSliceUI).GetNestedType("MainPanel",
                        System.Reflection.BindingFlags.NonPublic), "Store"));
                SetPrivateField(game, "ui", ui);
                // This layout test focuses on row reuse; portrait preview work
                // is separately scheduled one visual at a time in play mode.
                SetPrivateField<RatPortraitPreview>(ui, "portraitPreview", null);
                typeof(VerticalSliceUI).GetMethod("RebuildContent",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
                Canvas.ForceUpdateCanvases();
                SetPrivateField(ui, "ready", true);

                ScrollRect initialScroll = GetPrivateField<ScrollRect>(ui, "storeRatListScroll");
                RectTransform listContent = initialScroll.content;
                Assert.AreEqual(64, listContent.childCount);
                var originalRows = new GameObject[listContent.childCount];
                for (int index = 0; index < originalRows.Length; index++)
                    originalRows[index] = listContent.GetChild(index).gameObject;
                int rowsCreatedBefore = GetPrivateField<int>(ui, "storePurchaseRowCreateCount");
                int rowsUpdatedBefore = GetPrivateField<int>(ui, "storePurchaseRowUpdateCount");
                int pendingSaveRequestsBefore = SaveSystem.PendingSaveRequestCount;

                StoreSystem.RestockNowWithResult(save,
                    save.clock.gameTimeMs + GameConfig.StoreRestockIntervalGameMs);
                Assert.AreEqual(64, save.storeRatListings.Count,
                    "A restock must preserve the fully upgraded listing capacity.");
                Assert.AreEqual(64, new HashSet<string>(
                    save.storeRatListings.ConvertAll(listing => listing.id), StringComparer.Ordinal).Count,
                    "Restocking must not duplicate listing IDs.");
                Assert.AreEqual(64, new HashSet<string>(
                    save.storeRatListings.ConvertAll(listing => listing.name),
                    StringComparer.OrdinalIgnoreCase).Count,
                    "Batched name allocation must still keep each restock listing unique.");
                ui.Refresh(true);
                Canvas.ForceUpdateCanvases();

                ScrollRect refreshedScroll = GetPrivateField<ScrollRect>(ui, "storeRatListScroll");
                Assert.AreSame(initialScroll, refreshedScroll,
                    "A restock must not rebuild the Store page or replace its ScrollRect.");
                Assert.AreSame(listContent, refreshedScroll.content,
                    "The market list content root should be reused across restocks.");
                Assert.AreEqual(64, refreshedScroll.content.childCount);
                for (int index = 0; index < originalRows.Length; index++)
                    Assert.AreSame(originalRows[index], refreshedScroll.content.GetChild(index).gameObject,
                        "Existing listing row roots should be recycled by slot.");
                Assert.AreEqual(rowsCreatedBefore,
                    GetPrivateField<int>(ui, "storePurchaseRowCreateCount"),
                    "A same-capacity restock must not create duplicate listing rows or visual slots.");
                Assert.IsTrue(GetPrivateField<bool>(ui, "storePurchaseRowRefreshPending"),
                    "Large listing refreshes should be spread over later frames.");
                var processBatch = typeof(VerticalSliceUI).GetMethod(
                    "ProcessStorePurchaseRowsRefresh",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(processBatch);
                int updatesThisBatch = GetPrivateField<int>(ui, "storePurchaseRowUpdateCount") -
                    rowsUpdatedBefore;
                Assert.LessOrEqual(updatesThisBatch, 8,
                    "The initial listing refresh batch must stay within the per-frame work budget.");
                Dictionary<string, Button> purchaseButtons = GetPrivateField<Dictionary<string, Button>>(
                    ui, "storePurchaseButtons");
                foreach (Button button in purchaseButtons.Values)
                    Assert.IsFalse(button.interactable,
                        "Purchase buttons must remain disabled while listing rows are only partially refreshed.");
                typeof(VerticalSliceUI).GetMethod("RefreshLiveStorePurchaseButtons",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);
                foreach (Button button in purchaseButtons.Values)
                    Assert.IsFalse(button.interactable,
                        "Wallet refreshes must not re-enable stale listing rows during a batched refresh.");

                while (GetPrivateField<bool>(ui, "storePurchaseRowRefreshPending"))
                {
                    int previousUpdates = GetPrivateField<int>(ui, "storePurchaseRowUpdateCount");
                    processBatch.Invoke(ui, null);
                    updatesThisBatch = GetPrivateField<int>(ui, "storePurchaseRowUpdateCount") - previousUpdates;
                    Assert.LessOrEqual(updatesThisBatch, 8,
                        "No rendered frame may update more than eight market listing rows.");
                }
                Assert.AreEqual(64,
                    GetPrivateField<int>(ui, "storePurchaseRowUpdateCount") - rowsUpdatedBefore,
                    "Every row should be updated exactly once across the bounded refresh batches.");
                foreach (KeyValuePair<string, Button> entry in purchaseButtons)
                {
                    StoreRatListingData listing = StoreSystem.FindListing(save, entry.Key);
                    Assert.IsNotNull(listing, "Every visible purchase button must reference a current listing.");
                    Assert.AreEqual(save.colonyCredits >= listing.price, entry.Value.interactable,
                        "Buttons should return to their correct affordability state after reconciliation.");
                }
                Assert.AreEqual(pendingSaveRequestsBefore, SaveSystem.PendingSaveRequestCount,
                    "Rendering and reconciling the refreshed market must not issue saves.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(canvasObject);
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
        public void SaveNameNormalizationPreservesLargeHistoryAndIsIdempotent()
        {
            var save = new ColonySaveData();
            save.EnsureLists();
            save.ratNameMigrationVersion = RatNameSystem.CurrentMigrationVersion;
            save.rats.Add(new RatData { id = "active-name-1", name = "Mabel", sex = RatSex.Female });
            save.rats.Add(new RatData { id = "active-name-2", name = "Otto", sex = RatSex.Male });
            const int historySize = 256;
            for (int index = 0; index < historySize; index++)
            {
                string name = "Retired Rat " + index;
                string id = "retired-name-rat-" + index;
                save.retiredRats.Add(new RatData { id = id, name = name, sex = RatSex.Female });
                save.ratNameHistory.Add(new RatNameUseData
                {
                    normalizedName = RatNameSystem.NormalizeForComparison(name),
                    displayName = name,
                    ratId = id,
                    sex = RatSex.Female,
                    lastUsedGameTime = GameConfig.StartGameTimeMs,
                    lastFirstNameUsedGameTime = GameConfig.StartGameTimeMs,
                });
            }

            string activeName1 = save.rats[0].name;
            string activeName2 = save.rats[1].name;
            RatNameSystem.EnsureUniqueNames(save, GameConfig.StartGameTimeMs);
            int normalizedHistoryCount = save.ratNameHistory.Count;
            RatNameSystem.EnsureUniqueNames(save, GameConfig.StartGameTimeMs + 1L);

            Assert.AreEqual(historySize + save.rats.Count, normalizedHistoryCount,
                "Save normalization should add only the missing active-rat name records.");
            Assert.AreEqual(normalizedHistoryCount, save.ratNameHistory.Count,
                "Repeated save normalization must reuse historical name records.");
            Assert.AreEqual(activeName1, save.rats[0].name);
            Assert.AreEqual(activeName2, save.rats[1].name);
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
        public void AutoPinkieNamingSettingPersistsAndSkipsOnlyTheNamingPrompt()
        {
            Func<ColonySaveData, string, bool> priorSaveInterceptor = SaveSystem.SaveInterceptorForTests;
            var gameObject = new GameObject("Auto Pinkie Naming Test Game");
            try
            {
                SaveSystem.SaveInterceptorForTests = (save, source) => true;
                gameObject.SetActive(false);
                GameBootstrap game = gameObject.AddComponent<GameBootstrap>();
                ColonySaveData save = ColonyFactory.CreateNew(1000000L);
                save.welcomePopupPending = false;
                Assert.IsFalse(save.autoNamePinkies, "Manual litter naming remains the safe default.");
                typeof(GameBootstrap).GetProperty("Save").GetSetMethod(true)
                    .Invoke(game, new object[] { save });

                game.ToggleAutoNamePinkies();
                Assert.IsTrue(game.AutoNamePinkiesEnabled);
                ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
                Assert.IsTrue(loaded.autoNamePinkies, "The preference must survive save/load.");

                MethodInfo prepareNaming = typeof(GameBootstrap).GetMethod(
                    "PreparePendingLitterNaming", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(prepareNaming);
                var automaticLitter = new LitterData { id = "automatic-naming-litter" };
                prepareNaming.Invoke(game, new object[] { new List<LitterData> { automaticLitter } });
                Assert.IsFalse(save.pendingNamingLitterIds.Contains(automaticLitter.id));
                Assert.IsFalse(GrowthSystem.SimulationPaused,
                    "Automatic naming must not pause the authoritative simulation.");

                game.ToggleAutoNamePinkies();
                var manualLitter = new LitterData { id = "manual-naming-litter" };
                prepareNaming.Invoke(game, new object[] { new List<LitterData> { manualLitter } });
                Assert.IsTrue(save.pendingNamingLitterIds.Contains(manualLitter.id));
                Assert.IsTrue(GrowthSystem.SimulationPaused,
                    "Turning the preference off must preserve the existing manual naming flow.");
            }
            finally
            {
                SaveSystem.SaveInterceptorForTests = priorSaveInterceptor;
                GrowthSystem.SetSimulationPaused(false);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NewbornNamingModalKeepsOnlyRowsScrollableForSmallAndLargeLitters()
        {
            Func<ColonySaveData, string, bool> priorSaveInterceptor = SaveSystem.SaveInterceptorForTests;
            var gameObject = new GameObject("Pinkie Naming Modal Test Game");
            var canvasObject = new GameObject("Pinkie Naming Modal Test Canvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var eventSystemObject = new GameObject("Pinkie Naming Modal Test EventSystem", typeof(EventSystem));
            try
            {
                SaveSystem.SaveInterceptorForTests = (save, source) => true;
                gameObject.SetActive(false);
                GameBootstrap game = gameObject.AddComponent<GameBootstrap>();
                ColonySaveData save = CreatePendingNamingTestSave(1);
                typeof(GameBootstrap).GetProperty("Save").GetSetMethod(true).Invoke(game, new object[] { save });

                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(540f, 960f);
                scaler.matchWidthOrHeight = 1f;
                canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(540f, 960f);
                eventSystemObject.GetComponent<EventSystem>().enabled = true;

                VerticalSliceUI ui = canvasObject.AddComponent<VerticalSliceUI>();
                SetPrivateField(ui, "game", game);
                SetPrivateField(ui, "canvas", canvas);
                SetPrivateField(ui, "namingOpen", true);
                RectTransform safeRoot = new GameObject("Test Safe Area", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                safeRoot.SetParent(canvasObject.transform, false);
                safeRoot.anchorMin = Vector2.zero;
                safeRoot.anchorMax = Vector2.one;
                safeRoot.offsetMin = Vector2.zero;
                safeRoot.offsetMax = Vector2.zero;
                SetPrivateField(ui, "safeRoot", safeRoot);

                var buildPopup = typeof(VerticalSliceUI).GetMethod(
                    "BuildPendingNamingPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var refreshPopup = typeof(VerticalSliceUI).GetMethod(
                    "RefreshPendingNamingPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                var applyResponsiveLayout = typeof(VerticalSliceUI).GetMethod(
                    "ApplyResponsiveLayout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(buildPopup);
                Assert.IsNotNull(refreshPopup);
                Assert.IsNotNull(applyResponsiveLayout);
                buildPopup.Invoke(ui, null);
                Canvas.ForceUpdateCanvases();
                applyResponsiveLayout.Invoke(ui, null);

                int[] litterSizes = { 1, 5, 9, 12 };
                foreach (int litterSize in litterSizes)
                {
                    save = CreatePendingNamingTestSave(litterSize);
                    typeof(GameBootstrap).GetProperty("Save").GetSetMethod(true).Invoke(game, new object[] { save });
                    refreshPopup.Invoke(ui, null);
                    Canvas.ForceUpdateCanvases();

                    RectTransform namingCard = GetPrivateField<RectTransform>(ui, "namingCard");
                    ScrollRect scroll = GetPrivateField<ScrollRect>(ui, "namingScroll");
                    RectTransform namingContent = GetPrivateField<RectTransform>(ui, "namingContent");
                    Assert.IsNotNull(namingCard);
                    Assert.IsNotNull(scroll);
                    Assert.AreEqual(litterSize, namingContent.childCount,
                        "Every pup gets one row for a litter of " + litterSize + ".");
                    Assert.IsTrue(scroll.vertical);
                    Assert.IsFalse(scroll.horizontal);
                    Assert.AreSame(namingCard, scroll.transform.parent,
                        "Only the pinkie rows live inside the modal ScrollRect.");
                    Assert.AreEqual(namingCard, namingCard.Find("Newborn Naming Actions").parent,
                        "The footer remains fixed outside the scroll viewport.");
                    Assert.AreEqual(namingCard, namingCard.Find("Text").parent,
                        "The modal title remains fixed outside the scroll viewport.");

                    LayoutRebuilder.ForceRebuildLayoutImmediate(namingCard);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                    Canvas.ForceUpdateCanvases();
                    if (litterSize >= 9)
                        Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height,
                            "Large litters must scroll within the fixed row viewport.");

                    Vector3[] cardCorners = new Vector3[4];
                    namingCard.GetWorldCorners(cardCorners);
                    float cardTopLocalY = safeRoot.InverseTransformPoint(cardCorners[1]).y;
                    float cardBottomLocalY = safeRoot.InverseTransformPoint(cardCorners[0]).y;
                    Assert.LessOrEqual(cardTopLocalY, safeRoot.rect.height * 0.5f - 158f + 0.5f,
                        "The dialog starts below the 150-unit fixed navigation header.");
                    Assert.GreaterOrEqual(cardBottomLocalY, -safeRoot.rect.height - 0.5f,
                        "The dialog and fixed footer remain inside the safe-area bottom edge.");

                    for (int rowIndex = 0; rowIndex < litterSize; rowIndex++)
                    {
                        RectTransform row = namingContent.GetChild(rowIndex) as RectTransform;
                        InputField input = row.GetComponentInChildren<InputField>(true);
                        Button randomize = row.GetComponentInChildren<Button>(true);
                        Assert.IsNotNull(input);
                        Assert.IsNotNull(randomize);
                        LayoutElement inputLayout = input.GetComponent<LayoutElement>();
                        LayoutElement randomizeLayout = randomize.GetComponent<LayoutElement>();
                        Assert.AreEqual(1f, inputLayout.flexibleWidth, 0.001f,
                            "The input must take only the space left after label and fixed-width dice button.");
                        Assert.AreEqual(44f, randomizeLayout.minWidth, 0.001f);
                        Assert.AreEqual(44f, randomizeLayout.preferredWidth, 0.001f);
                        Assert.Greater(input.GetComponent<RectTransform>().rect.width, 0f);
                        Assert.GreaterOrEqual(randomize.transform.position.y,
                            row.position.y - row.rect.height * 0.5f - 1f);
                        Assert.LessOrEqual(randomize.transform.position.y,
                            row.position.y + row.rect.height * 0.5f + 1f);

                        randomize.onClick.Invoke();
                        RatData pup = save.rats.Find(candidate => candidate != null && candidate.id == row.name.Substring("Newborn Name ".Length));
                        Assert.IsNotNull(pup);
                        Assert.AreEqual(pup.name, input.text,
                            "The per-row randomizer updates both the saved pending pup and its input.");
                    }

                    RectTransform actions = namingCard.Find("Newborn Naming Actions") as RectTransform;
                    Button[] footerButtons = actions.GetComponentsInChildren<Button>(true);
                    Assert.AreEqual(2, footerButtons.Length);
                    Assert.IsTrue(footerButtons[0].interactable);
                    Assert.IsTrue(footerButtons[1].interactable);
                    StringAssert.Contains("Randomize All", footerButtons[0].GetComponentInChildren<Text>().text);
                    StringAssert.Contains("Continue", footerButtons[1].GetComponentInChildren<Text>().text);
                    if (litterSize == 12)
                    {
                        footerButtons[0].onClick.Invoke();
                        foreach (RatData pup in game.PendingNamingPups)
                            Assert.IsFalse(string.IsNullOrWhiteSpace(pup.name),
                                "Randomize All keeps every large-litter name populated.");
                    }
                }
            }
            finally
            {
                SaveSystem.SaveInterceptorForTests = priorSaveInterceptor;
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        private static ColonySaveData CreatePendingNamingTestSave(int litterSize)
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            RatData mother = save.rats.Find(rat => rat != null && rat.sex == RatSex.Female);
            RatData father = save.rats.Find(rat => rat != null && rat.sex == RatSex.Male);
            var litter = new LitterData
            {
                id = "naming-test-litter-" + litterSize,
                motherId = mother == null ? string.Empty : mother.id,
                fatherId = father == null ? string.Empty : father.id,
                size = litterSize,
                birthTimestamp = save.clock.gameTimeMs,
            };
            for (int index = 0; index < litterSize; index++)
            {
                string id = "naming-test-pup-" + litterSize + "-" + index;
                RatData pup = ColonyFactory.CreateRat(id, "Test Pup " + index,
                    index % 2 == 0 ? RatSex.Female : RatSex.Male,
                    save.clock.gameTimeMs, 1,
                    GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "s", "s"),
                    new TraitData(10f, 10f, 10f), RatStage.Pinkie);
                pup.motherId = litter.motherId;
                pup.fatherId = litter.fatherId;
                pup.litterId = litter.id;
                save.rats.Add(pup);
                litter.pupIds.Add(pup.id);
            }
            save.litters.Add(litter);
            save.pendingNamingLitterIds.Add(litter.id);
            return save;
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
        public void MarketMarkingRollTargetsFifteenPercentMarkedListings()
        {
            Assert.AreEqual(0.15f, GameConfig.StoreFounderMarkingChance,
                "About 15% of new market listings roll a marking; about 85% remain solid.");
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
        public void BatchedBirthPersistsFinishedPregnancyAndWholeLitterInOneSave()
        {
            Func<ColonySaveData, string, bool> priorSaveInterceptor = SaveSystem.SaveInterceptorForTests;
            string persistedJson = null;
            int saveCount = 0;
            SaveSystem.ClearPendingSaveQueueForTests();
            try
            {
                SaveSystem.SaveInterceptorForTests = (currentSave, source) =>
                {
                    saveCount++;
                    persistedJson = SaveSystem.ToJson(currentSave);
                    return true;
                };

                const long gameTime = 2000000L;
                ColonySaveData save = ColonyFactory.CreateNew(gameTime);
                RatData mother = save.rats.Find(rat => rat != null && rat.sex == RatSex.Female);
                RatData father = save.rats.Find(rat => rat != null && rat.sex == RatSex.Male);
                PrepareStarterPairForBreeding(save, gameTime, false);
                PregnancyData pregnancy;
                string reason;
                Assert.IsTrue(BreedingSystem.StartBreeding(
                    save, mother, father, gameTime, out pregnancy, out reason), reason);
                pregnancy.expectedLitterSize = 1;
                pregnancy.dueAt = gameTime;
                saveCount = 0;
                persistedJson = null;

                LitterData litter;
                Assert.IsTrue(BreedingSystem.FinishPregnancyForBatch(
                    save, pregnancy.id, gameTime, out litter, out reason), reason);
                Assert.AreEqual(0, saveCount,
                    "The simulation batch defers storage until announcement and naming state are included.");
                Assert.AreEqual("finished", pregnancy.status);
                Assert.IsNotNull(litter);
                Assert.AreEqual(1, litter.pupIds.Count);
                Assert.AreEqual(litter.id, mother.nursingLitterId,
                    "The mother retains the litter identity used to stay beside her pinkies.");

                // These are the final pieces the live caller adds after the
                // pregnancy transaction and before its single batch commit.
                save.eventLog.Insert(0, new ColonyEventData
                {
                    gameTimeMs = gameTime,
                    message = "The mother has given birth to 1 pup!",
                    category = EventLogPolicy.Birth,
                });
                litter.birthAnnouncementLogged = true;
                save.pendingNamingLitterIds.Add(litter.id);

                Assert.IsTrue(SaveSystem.Save(save, "test/birth-batch"));
                Assert.AreEqual(1, saveCount,
                    "A complete birth batch serializes the full colony exactly once.");
                ColonySaveData restored = SaveSystem.FromJson(persistedJson);
                RatData restoredMother = BreedingSystem.FindRat(restored, mother.id);
                Assert.IsNotNull(restoredMother);
                Assert.AreEqual(litter.id, restoredMother.nursingLitterId,
                    "The family-to-litter presentation link survives save/load.");
                PregnancyData restoredPregnancy = restored.pregnancies.Find(item =>
                    item != null && item.id == pregnancy.id);
                Assert.IsNotNull(restoredPregnancy);
                Assert.AreEqual("finished", restoredPregnancy.status);
                LitterData restoredLitter = restored.litters.Find(item =>
                    item != null && item.id == litter.id);
                Assert.IsNotNull(restoredLitter);
                Assert.AreEqual(1, restoredLitter.pupIds.Count);
                Assert.IsTrue(restoredLitter.birthAnnouncementLogged);
                Assert.IsNotNull(BreedingSystem.FindRat(restored, restoredLitter.pupIds[0]));
                Assert.IsTrue(restored.pendingNamingLitterIds.Contains(litter.id),
                    "The naming blocker is persisted together with the completed litter.");
                Assert.AreEqual("The mother has given birth to 1 pup!", restored.eventLog[0].message);
            }
            finally
            {
                SaveSystem.SaveInterceptorForTests = priorSaveInterceptor;
                SaveSystem.ClearPendingSaveQueueForTests();
            }
        }

        [Test]
        public void BirthCompletesWithAutomaticallySoldFatherAndPreservesHisRetiredHistory()
        {
            const long gameTime = 2500000L;
            ColonySaveData save = ColonyFactory.CreateNew(gameTime);
            RatData mother = save.rats.Find(rat => rat != null && rat.sex == RatSex.Female);
            RatData father = save.rats.Find(rat => rat != null && rat.sex == RatSex.Male);
            PrepareStarterPairForBreeding(save, gameTime, false);

            Assert.IsTrue(BreedingSystem.StartBreeding(
                save, mother, father, gameTime, out PregnancyData pregnancy, out string reason), reason);
            pregnancy.expectedLitterSize = 2;
            pregnancy.dueAt = gameTime;

            Assert.IsTrue(save.rats.Remove(father));
            save.ratIds.Remove(father.id);
            father.removalDisposition = RatRemovalDisposition.Sold;
            father.removedAt = gameTime - GameConfig.GameDayMs;
            father.pregnancyId = pregnancy.id;
            father.reproductiveState = ReproductiveState.Infertile;
            RatActivitySystem.SetCurrent(save, father, "sold", "Sold", father.removedAt,
                "Automatically sold");
            save.retiredRats.Add(father);
            BreedingSystem.InvalidateReproductiveStateIndexes(save);

            Assert.IsNull(BreedingSystem.FindRat(save, father.id),
                "The sold father must no longer be an active colony rat.");
            Assert.AreSame(father, BreedingSystem.FindHistoricalRat(save, father.id),
                "The lineage record remains available in retired history.");

            Assert.IsTrue(BreedingSystem.FinishPregnancyForBatch(
                save, pregnancy.id, gameTime, out LitterData litter, out reason), reason);

            Assert.AreEqual("finished", pregnancy.status);
            Assert.IsNotNull(litter);
            Assert.AreEqual(mother.id, litter.motherId);
            Assert.AreEqual(father.id, litter.fatherId);
            Assert.AreEqual(2, litter.pupIds.Count);
            foreach (string pupId in litter.pupIds)
            {
                RatData pup = BreedingSystem.FindRat(save, pupId);
                Assert.IsNotNull(pup);
                Assert.AreEqual(mother.id, pup.motherId);
                Assert.AreEqual(father.id, pup.fatherId);
            }
            Assert.AreSame(father, BreedingSystem.FindHistoricalRat(save, father.id));
            Assert.AreEqual(RatRemovalDisposition.Sold, father.removalDisposition);
            Assert.AreEqual("Sold", RatActivitySystem.CurrentLabel(save, father, gameTime));
            Assert.AreEqual(ReproductiveState.Infertile, father.reproductiveState,
                "Completing his historical litter must not make a sold rat fertile again.");
            Assert.IsNull(father.pregnancyId,
                "The retired father's stale link to the now-finished pregnancy must be cleared.");
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
                "1x is the baseline calendar rate.");
            Assert.AreEqual(60f, GrowthSystem.SimulationMultiplierForSpeed(2f), 0.0001f,
                "2x advances the calendar by one game hour per real second.");
            Assert.AreEqual(1440f, GrowthSystem.SimulationMultiplierForSpeed(3f), 0.001f,
                "3x advances the calendar by one game day per real second.");
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

                Assert.AreEqual(travelled[0] * GrowthSystem.SimulationMultiplierForSpeed(2f), travelled[1], 0.02f,
                    "2x Transform travel must match the 60x game-time fast-forward rate.");
                Assert.AreEqual(travelled[0] * GrowthSystem.SimulationMultiplierForSpeed(3f), travelled[2], 0.05f,
                    "3x Transform travel must match the 1,440x game-time fast-forward rate.");

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

        [Test]
        public void WorldMovementDistanceMatchesCalendarAcrossLiveSpeedTransitions()
        {
            const float baseWorldSpeed = 0.75f;
            const long realIntervalMs = 1000L;
            float[] speeds = { 1f, 2f, 3f, 1f };
            long realNow = 10000L;
            GrowthSystem.SetSimulationPaused(false);
            ColonySaveData save = ColonyFactory.CreateNew(realNow);
            save.clock.gameTimeMs = GameConfig.StartGameTimeMs;
            save.clock.lastRealTimestamp = realNow;
            save.clock.speed = 1f;
            var root = new GameObject("calendar-synchronized-moving rat");
            Vector3 target = new Vector3(100000f, 0f, 0f);

            try
            {
                for (int segment = 0; segment < speeds.Length; segment++)
                {
                    GrowthSystem.ChangeSpeedAtTimestamp(save, speeds[segment], realNow);
                    long calendarBefore = save.clock.gameTimeMs;
                    long nextRealTimestamp = realNow + realIntervalMs;
                    GrowthSystem.AdvanceClock(save, nextRealTimestamp);
                    long calendarDeltaMs = save.clock.gameTimeMs - calendarBefore;

                    float movementDelta = GrowthSystem.SimulationMovementDeltaSeconds(
                        realIntervalMs / 1000f);
                    float movementBudget = GrowthSystem.SimulationMovementTimeBudget(movementDelta);
                    Vector3 before = root.transform.position;
                    root.transform.position = GrowthSystem.SimulationMovementTargetPosition(
                        before, target, baseWorldSpeed, movementDelta, ref movementBudget);
                    float actualDistance = Vector3.Distance(before, root.transform.position);
                    // One authored movement second equals the 1x calendar rate
                    // (one in-game minute per real second). Normalize the
                    // authoritative clock delta against that baseline so the
                    // comparison measures only the selected 1x/60x/1440x rate.
                    double baselineCalendarMillisecondsPerMovementSecond =
                        GrowthSystem.SimulationMillisecondsPerRealSecond(1f);
                    float calendarMovementSeconds = (float)(calendarDeltaMs /
                        baselineCalendarMillisecondsPerMovementSecond);
                    float calendarDistance = baseWorldSpeed * calendarMovementSeconds;

                    Assert.AreEqual(calendarDistance, actualDistance, 0.002f,
                        "World-space travel must use the same single game-time multiplier as the calendar at " +
                        speeds[segment] + "x (segment " + segment + ").");
                    Assert.LessOrEqual(actualDistance, calendarDistance + 0.002f,
                        "A speed transition must not teleport the rat or add movement backlog.");
                    realNow = nextRealTimestamp;
                }
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RoutineReproductiveMaintenanceScansHistoryOnceAndNeverRunsBirthRepair()
        {
            bool wasCapturing = RuntimePerformanceDiagnostics.CaptureEnabled;
            ColonySaveData save = ColonyFactory.CreateNew(GameConfig.NowMs());
            const int historyCount = 128;
            for (int index = 0; index < historyCount; index++)
            {
                string motherId = "retired-mother-" + index;
                save.litters.Add(new LitterData
                {
                    id = "indexed-history-litter-" + index,
                    motherId = motherId,
                    birthTimestamp = GameConfig.StartGameTimeMs + index,
                    pupIds = new List<string> { "historical-pup-" + index },
                    size = 1,
                });
                save.pregnancies.Add(new PregnancyData
                {
                    id = "indexed-history-pregnancy-" + index,
                    motherId = motherId,
                    fatherId = "retired-father-" + index,
                    status = "finished",
                    finishedAt = GameConfig.StartGameTimeMs + index,
                    litterId = "indexed-history-litter-" + index,
                });
                save.retiredRats.Add(new RatData { id = motherId, name = motherId });
            }

            try
            {
                RuntimePerformanceDiagnostics.SetCaptureEnabled(true);
                RuntimePerformanceDiagnostics.ClearLog();

                // This is the routine runtime path, including a second pass
                // over unchanged data. A finished record with incomplete old
                // history must not be reopened outside load/recovery.
                BreedingSystem.RefreshReproductiveStates(save, save.clock.gameTimeMs);
                BreedingSystem.RefreshReproductiveStates(save, save.clock.gameTimeMs + 1L);
                BreedingSystem.HasDependentPinkies(save, "no-dependent-pinkies");
                BreedingSystem.FindLitterForPup(save, new RatData { id = "historical-pup-10" });
                for (int index = 0; index < historyCount; index++)
                {
                    Assert.AreSame(save.retiredRats[index],
                        BreedingSystem.FindHistoricalRat(save, save.retiredRats[index].id));
                    Assert.AreSame(save.pregnancies[index],
                        BreedingSystem.FindPregnancyForLitter(save, save.litters[index].id));
                }
                BreedingSystem.NextPendingPregnancyDueGameTime(save);

                Assert.AreEqual("finished", save.pregnancies[0].status,
                    "Completed-birth repair is load/recovery-only, not routine maintenance.");
                Assert.AreEqual(1, RuntimePerformanceDiagnostics.WindowCallCount(
                    PerformanceProbeArea.MaintenanceHistoricalLitters),
                    "An unchanged history ledger should be indexed once and reused across routine refreshes.");
                Assert.AreEqual(1, RuntimePerformanceDiagnostics.WindowCallCount(
                    PerformanceProbeArea.MaintenanceHistoricalPregnancies),
                    "An unchanged pregnancy ledger should be indexed once and reused across routine refreshes.");
                Assert.AreEqual(historyCount, RuntimePerformanceDiagnostics.WindowWorkCount(
                    PerformanceProbeArea.MaintenanceHistoricalLitters));
                Assert.AreEqual(historyCount, RuntimePerformanceDiagnostics.WindowWorkCount(
                    PerformanceProbeArea.MaintenanceHistoricalPregnancies));
                Assert.AreEqual(1, RuntimePerformanceDiagnostics.WindowCallCount(
                    PerformanceProbeArea.MaintenanceHistoricalRatIndex),
                    "Historical rat lookups must reuse one active/retired ID index rather than scan the colony per litter.");
                Assert.AreEqual(historyCount + save.rats.Count, RuntimePerformanceDiagnostics.WindowWorkCount(
                    PerformanceProbeArea.MaintenanceHistoricalRatIndex));
                Assert.AreEqual(0, RuntimePerformanceDiagnostics.WindowCallCount(
                    PerformanceProbeArea.MaintenanceBirthRepairRecovery));
            }
            finally
            {
                RuntimePerformanceDiagnostics.ClearLog();
                RuntimePerformanceDiagnostics.SetCaptureEnabled(wasCapturing);
                BreedingSystem.InvalidateReproductiveStateIndexes(save);
            }
        }

        [Test]
        public void MovingColoniesWithPinkiesKeepFastForwardMovementAndBoundedBehaviorWork()
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 1f / 60f;
            const int frameCount = 120;
            int[] colonySizes = { 10, 12, 20, 30 };
            int[] pinkieCounts = { 3, 4, 5, 7 };

            try
            {
                for (int colonyIndex = 0; colonyIndex < colonySizes.Length; colonyIndex++)
                {
                    int ratCount = colonySizes[colonyIndex];
                    int pinkieCount = pinkieCounts[colonyIndex];
                    int movingRatCount = ratCount - pinkieCount;
                    GameObject[] roots = new GameObject[movingRatCount];
                    string[] behaviorIds = new string[movingRatCount];
                    float[] travelBySpeed = new float[3];
                    float[] averageFrameMs = new float[3];
                    float[] worstFrameMs = new float[3];

                    try
                    {
                        for (int ratIndex = 0; ratIndex < movingRatCount; ratIndex++)
                        {
                            roots[ratIndex] = new GameObject("moving colony " + ratCount + " rat " + ratIndex);
                            behaviorIds[ratIndex] = "perf-" + ratCount + "-" + ratIndex;
                        }

                        GrowthSystem.SetBehaviorParticipantCount(movingRatCount);
                        for (int speedIndex = 0; speedIndex < 3; speedIndex++)
                        {
                            GrowthSystem.SetRuntimeSpeed(speedIndex + 1f);
                            long scenarioTicks = 0L;
                            long worstFrameTicks = 0L;
                            for (int frame = 0; frame < frameCount; frame++)
                            {
                                GrowthSystem.ResetBehaviorDiagnosticsForTests();
                                long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
                                float movementDelta = GrowthSystem.SimulationMovementDeltaSeconds(realFrameSeconds);
                                for (int ratIndex = 0; ratIndex < movingRatCount; ratIndex++)
                                {
                                    int ratSteps = GrowthSystem.BeginBehaviorUpdate(
                                        movementDelta, behaviorIds[ratIndex]);
                                    float movementStepDelta = ratSteps <= 0
                                        ? 0f
                                        : movementDelta / ratSteps;
                                    float movementBudget = GrowthSystem.SimulationMovementTimeBudget(movementDelta);
                                    for (int step = 0; step < ratSteps; step++)
                                    {
                                        Vector3 previous = roots[ratIndex].transform.position;
                                        roots[ratIndex].transform.position = GrowthSystem.SimulationMovementTargetPosition(
                                            previous, previous + Vector3.right * 10000f,
                                            baseWorldSpeed, movementStepDelta, ref movementBudget);
                                        travelBySpeed[speedIndex] += Vector3.Distance(
                                            previous, roots[ratIndex].transform.position);
                                    }
                                }
                                long frameTicks = System.Diagnostics.Stopwatch.GetTimestamp() - frameStart;
                                scenarioTicks += frameTicks;
                                if (frameTicks > worstFrameTicks) worstFrameTicks = frameTicks;
                                Assert.LessOrEqual(GrowthSystem.LastSimulationStepCount,
                                    GrowthSystem.MaximumTotalBehaviorStepsPerFrame,
                                    "All rats together must remain within the fixed catch-up work budget.");
                            }

                            averageFrameMs[speedIndex] = (float)RuntimePerformanceDiagnostics.TicksToMilliseconds(
                                scenarioTicks) / frameCount;
                            worstFrameMs[speedIndex] = (float)RuntimePerformanceDiagnostics.TicksToMilliseconds(
                                worstFrameTicks);
                        }

                        Assert.AreEqual(travelBySpeed[0] * GrowthSystem.SimulationMultiplierForSpeed(2f), travelBySpeed[1], 0.002f,
                            ratCount + " total rats (" + pinkieCount + " pinkies): 2x Transform travel follows the 60x clock rate.");
                        Assert.AreEqual(travelBySpeed[0] * GrowthSystem.SimulationMultiplierForSpeed(3f), travelBySpeed[2], 0.01f,
                            ratCount + " total rats (" + pinkieCount + " pinkies): 3x Transform travel follows the 1,440x clock rate.");
                        for (int speedIndex = 0; speedIndex < 3; speedIndex++)
                        {
                            Assert.Less(averageFrameMs[speedIndex], 16.7f,
                                ratCount + " rat movement + bounded behavior work averages below one 60 Hz frame budget.");
                            Assert.Less(worstFrameMs[speedIndex], 100f,
                                "The movement benchmark must not create a long catch-up frame.");
                        }
                    }
                    finally
                    {
                        for (int ratIndex = 0; ratIndex < roots.Length; ratIndex++)
                            if (roots[ratIndex] != null) Object.DestroyImmediate(roots[ratIndex]);
                    }
                }
            }
            finally
            {
                GrowthSystem.SetBehaviorParticipantCount(1);
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
            }
        }

        [Test]
        public void ThreeXVisibleMovementCapLeavesOneXAndTwoXUnchanged()
        {
            const float baseSpeed = 0.75f;
            const float realDelta = 1f / 60f;
            try
            {
                for (int speedIndex = 0; speedIndex < 3; speedIndex++)
                {
                    float speed = speedIndex + 1f;
                    GrowthSystem.SetRuntimeSpeed(speed);
                    float simulationDelta = GrowthSystem.SimulationMovementDeltaSeconds(realDelta);
                    float movementTimeBudget = GrowthSystem.SimulationMovementTimeBudget(simulationDelta);
                    float visibleDistanceBudget = GrowthSystem.SimulationVisibleMovementDistanceBudget(realDelta);
                    float distance = GrowthSystem.SimulationMovementStep(
                        baseSpeed, simulationDelta, ref movementTimeBudget, ref visibleDistanceBudget);
                    float expected = baseSpeed * simulationDelta;
                    if (speed < 3f)
                        Assert.AreEqual(expected, distance, 0.00001f,
                            speed + "x must retain its existing exact calendar-scaled movement.");
                    else
                    {
                        Assert.AreEqual(GrowthSystem.MaximumThreeXVisibleMovementUnitsPerSecond * realDelta,
                            distance, 0.00001f,
                            "Only 3x visible movement is spatially bounded; the calendar delta remains unmodified.");
                        Assert.AreEqual(1440f, GrowthSystem.SimulationMultiplierForSpeed(speed), 0.001f);
                    }
                }
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
            }
        }

        [Test]
        public void EightyFourAdultRatsAtThreeXKeepBoundedMovementAndSpacing()
        {
            const int adultCount = 84;
            const int frameCount = 180;
            const float realFrameSeconds = 1f / 60f;
            GameObject[] roots = new GameObject[adultCount];
            RatHabitatBehavior[] behaviors = new RatHabitatBehavior[adultCount];
            float[] distanceByRat = new float[adultCount];
            HashSet<Vector2Int> initialTargetCells = new HashSet<Vector2Int>();
            int[] initialDirectionCounts = new int[8];
            float minimumDecisionPhase = float.MaxValue;
            float maximumDecisionPhase = float.MinValue;
            int totalBlockedRecoveries = 0;
            GameObject builderRoot = new GameObject("84-rat movement stress habitat");
            HabitatBuilder builder = builderRoot.AddComponent<HabitatBuilder>();
            MethodInfo updateCore = typeof(RatHabitatBehavior).GetMethod("UpdateCore",
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(float) }, null);
            MethodInfo lateUpdateCore = typeof(RatHabitatBehavior).GetMethod("LateUpdateCore",
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(float) }, null);
            Assert.IsNotNull(updateCore);
            Assert.IsNotNull(lateUpdateCore);

            try
            {
                GrowthSystem.SetSimulationPaused(false);
                GrowthSystem.SetRuntimeSpeed(3f);
                GrowthSystem.SetBehaviorParticipantCount(adultCount);
                Vector3 sharedStart = EnclosureSystem.PointInEnclosure(
                    RatEnclosure.FemaleColony, -4f, 0f, 0.45f);
                Assert.IsFalse(EnclosureSystem.IsInsideAdultNestExclusion(
                    RatEnclosure.FemaleColony, sharedStart));

                for (int index = 0; index < adultCount; index++)
                {
                    RatData rat = ColonyFactory.CreateRat(
                        "speed-clump-rat-" + index,
                        "Stress Rat " + index,
                        RatSex.Female,
                        GameConfig.StartGameTimeMs - 120L * 24L * 60L * 60L * 1000L,
                        0,
                        new GenotypeData(),
                        new TraitData { size = 50f, health = 80f, fertility = 50f },
                        RatStage.Adult);
                    rat.enclosure = RatEnclosure.FemaleColony;
                    roots[index] = new GameObject("84-rat stress root " + index);
                    roots[index].transform.position = sharedStart;
                    behaviors[index] = roots[index].AddComponent<RatHabitatBehavior>();
                    behaviors[index].Configure(builder, rat);

                    Vector3 target = behaviors[index].MovementTargetPositionForDiagnostics;
                    Assert.IsTrue(EnclosureSystem.IsInside(RatEnclosure.FemaleColony, target, 0.48f),
                        "Selected ambient goals must remain on the valid tank floor.");
                    Assert.IsFalse(EnclosureSystem.IsInsideAdultNestExclusion(
                        RatEnclosure.FemaleColony, target, 0.02f),
                        "Ambient goals must continue to avoid the nest footprint.");
                    initialTargetCells.Add(new Vector2Int(
                        Mathf.RoundToInt(target.x * 2f), Mathf.RoundToInt(target.z * 2f)));
                    float direction = Mathf.Atan2(target.z - sharedStart.z, target.x - sharedStart.x);
                    if (direction < 0f) direction += Mathf.PI * 2f;
                    int directionBucket = Mathf.Clamp(
                        Mathf.FloorToInt(direction * (8f / (Mathf.PI * 2f))), 0, 7);
                    initialDirectionCounts[directionBucket]++;
                    minimumDecisionPhase = Mathf.Min(minimumDecisionPhase,
                        behaviors[index].MovementDecisionPhaseSeconds);
                    maximumDecisionPhase = Mathf.Max(maximumDecisionPhase,
                        behaviors[index].MovementDecisionPhaseSeconds);
                    Assert.IsTrue(behaviors[index].HasAmbientTargetReservation,
                        "Every ambient movement destination should be registered for crowd-aware selection.");
                    Assert.LessOrEqual(behaviors[index].AmbientTargetCellCrowdForDiagnostics, 3,
                        "A candidate cell already at its approach limit must be rejected.");
                }

                int usedDirectionBuckets = 0;
                int largestDirectionBucket = 0;
                for (int index = 0; index < initialDirectionCounts.Length; index++)
                {
                    if (initialDirectionCounts[index] > 0) usedDirectionBuckets++;
                    largestDirectionBucket = Mathf.Max(largestDirectionBucket, initialDirectionCounts[index]);
                }
                Assert.Greater(initialTargetCells.Count, adultCount * 0.40f,
                    "At 3x, independently seeded rats should not share a small set of destinations.");
                Assert.Greater(maximumDecisionPhase - minimumDecisionPhase, 0.6f,
                    "Per-rat movement decision phases should be staggered.");
                Assert.GreaterOrEqual(usedDirectionBuckets, 6,
                    "The same-start stress case should fan out across multiple travel directions.");
                Assert.Less(largestDirectionBucket, adultCount * 0.40f,
                    "No single initial direction should dominate the colony movement.");

                object[] deltaArgument = { realFrameSeconds };
                for (int frame = 0; frame < frameCount; frame++)
                {
                    for (int index = 0; index < adultCount; index++)
                    {
                        Vector3 before = roots[index].transform.position;
                        updateCore.Invoke(behaviors[index], deltaArgument);
                        lateUpdateCore.Invoke(behaviors[index], deltaArgument);
                        float moved = Vector3.Distance(before, roots[index].transform.position);
                        distanceByRat[index] += moved;
                        Assert.LessOrEqual(moved,
                            GrowthSystem.MaximumThreeXVisibleMovementUnitsPerFrame + 0.001f,
                            "One 3x frame must stay within the rat's safe visible movement budget.");
                    }
                }

                float totalDistance = 0f;
                float nearestNeighborTotal = 0f;
                int movedAdults = 0;
                int largestCloseGroup = 0;
                float meanX = 0f;
                float meanZ = 0f;
                for (int index = 0; index < adultCount; index++)
                {
                    meanX += roots[index].transform.position.x;
                    meanZ += roots[index].transform.position.z;
                    totalDistance += distanceByRat[index];
                    if (distanceByRat[index] > 0.5f) movedAdults++;
                    totalBlockedRecoveries += behaviors[index].BlockedTargetRecoveryCount;

                    float nearest = float.MaxValue;
                    int closeGroup = 0;
                    for (int otherIndex = 0; otherIndex < adultCount; otherIndex++)
                    {
                        if (otherIndex == index) continue;
                        float distance = Vector3.Distance(
                            roots[index].transform.position, roots[otherIndex].transform.position);
                        if (distance < nearest) nearest = distance;
                        if (distance < 0.15f) closeGroup++;
                    }
                    nearestNeighborTotal += nearest;
                    largestCloseGroup = Mathf.Max(largestCloseGroup, closeGroup + 1);
                }

                meanX /= adultCount;
                meanZ /= adultCount;
                float covarianceXX = 0f;
                float covarianceZZ = 0f;
                float covarianceXZ = 0f;
                for (int index = 0; index < adultCount; index++)
                {
                    Assert.IsTrue(EnclosureSystem.IsInside(RatEnclosure.FemaleColony,
                        roots[index].transform.position, 0.48f),
                        "Movement must remain within the active enclosure bounds.");
                    Assert.IsFalse(EnclosureSystem.IsInsideAdultNestExclusion(
                        RatEnclosure.FemaleColony, roots[index].transform.position, 0.02f),
                        "Ambient movement must not enter the nest exclusion zone.");
                    float dx = roots[index].transform.position.x - meanX;
                    float dz = roots[index].transform.position.z - meanZ;
                    covarianceXX += dx * dx;
                    covarianceZZ += dz * dz;
                    covarianceXZ += dx * dz;
                }
                float principalAngle = 0.5f * Mathf.Atan2(
                    2f * covarianceXZ, covarianceXX - covarianceZZ);
                Vector2 perpendicular = new Vector2(-Mathf.Sin(principalAngle), Mathf.Cos(principalAngle));
                int ratsNearPrincipalLine = 0;
                for (int index = 0; index < adultCount; index++)
                {
                    Vector2 offset = new Vector2(
                        roots[index].transform.position.x - meanX,
                        roots[index].transform.position.z - meanZ);
                    if (Mathf.Abs(Vector2.Dot(offset, perpendicular)) < 0.42f)
                        ratsNearPrincipalLine++;
                }

                float averageMovementSpeed = totalDistance /
                    (adultCount * frameCount * realFrameSeconds);
                float averageNearestNeighbor = nearestNeighborTotal / adultCount;
                Assert.GreaterOrEqual(movedAdults, 72,
                    "Most of the 84 adults must make measurable progress during the stress run.");
                Assert.Greater(averageMovementSpeed, 2f,
                    "The 3x safe visible movement target must not regress toward the old ~0.3 u/s clamp stall.");
                Assert.LessOrEqual(18f / averageMovementSpeed, 6f,
                    "Measured movement should remain reasonably close to the safe visible target, not hundreds of times slower.");
                Assert.Greater(averageNearestNeighbor, 0.3f,
                    "Spacing should recover instead of collapsing into a single group point.");
                Assert.LessOrEqual(largestCloseGroup, 8,
                    "No large group should remain stacked within a tiny region.");
                Assert.Less(ratsNearPrincipalLine, adultCount * 0.42f,
                    "The 3x colony should not collapse into one narrow diagonal travel line.");
                Assert.Less(totalBlockedRecoveries, adultCount,
                    "A rat should not repeatedly retry the same blocked boundary or nest route.");
                for (int index = 0; index < adultCount; index++)
                {
                    Assert.LessOrEqual(behaviors[index].TargetWorldMovementSpeed,
                        GrowthSystem.MaximumThreeXVisibleMovementUnitsPerSecond + 0.01f,
                        "Diagnostics should report the bounded visible target, not the raw 1,440x calendar demand.");
                }
            }
            finally
            {
                GrowthSystem.SetBehaviorParticipantCount(1);
                GrowthSystem.SetRuntimeSpeed(1f);
                GrowthSystem.SetSimulationPaused(false);
                for (int index = 0; index < roots.Length; index++)
                    if (roots[index] != null) Object.DestroyImmediate(roots[index]);
                if (builderRoot != null) Object.DestroyImmediate(builderRoot);
            }
        }

        [Test]
        public void WorldSpaceMovementSpeedChangesTakeEffectImmediatelyWithoutTeleporting()
        {
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 1f / 60f;
            const int framesPerSegment = 60;
            float[] speeds = { 1f, 2f, 3f, 1f };
            GameObject[] roots = new GameObject[20];

            try
            {
                for (int index = 0; index < roots.Length; index++)
                    roots[index] = new GameObject("speed transition rat " + index);

                for (int segment = 0; segment < speeds.Length; segment++)
                {
                    GrowthSystem.SetRuntimeSpeed(speeds[segment]);
                    float segmentTravel = 0f;
                    for (int frame = 0; frame < framesPerSegment; frame++)
                    {
                        float movementDelta = GrowthSystem.SimulationMovementDeltaSeconds(realFrameSeconds);
                        for (int index = 0; index < roots.Length; index++)
                        {
                            Vector3 before = roots[index].transform.position;
                            float movementBudget = GrowthSystem.SimulationMovementTimeBudget(movementDelta);
                            roots[index].transform.position = GrowthSystem.SimulationMovementTargetPosition(
                                before, before + Vector3.right * 100000f, baseWorldSpeed,
                                movementDelta, ref movementBudget);
                            float stepDistance = Vector3.Distance(before, roots[index].transform.position);
                            float frameRateMultiplier = GrowthSystem.SimulationMultiplierForSpeed(speeds[segment]);
                            Assert.LessOrEqual(stepDistance, baseWorldSpeed * frameRateMultiplier * realFrameSeconds + 0.0001f,
                                "A speed change may not teleport any rat beyond that frame's game-time movement budget.");
                            segmentTravel += stepDistance;
                        }
                    }

                    Assert.AreEqual(roots.Length * baseWorldSpeed *
                        GrowthSystem.SimulationMultiplierForSpeed(speeds[segment]), segmentTravel, 0.002f,
                        "World-space positions must immediately follow the selected calendar fast-forward rate.");
                }
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(1f);
                for (int index = 0; index < roots.Length; index++)
                    if (roots[index] != null) Object.DestroyImmediate(roots[index]);
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
                        "A speed change must not teleport the rat beyond one frame of game-time travel or apply the multiplier twice.");
                }

                float nextSegmentDistance = root.transform.position.x - positionAtSwitch.x;
                Assert.AreEqual(baseWorldSpeed * startingMultiplier * framesPerSegment * realFrameSeconds,
                    firstSegmentDistance, 0.0001f, "The first segment must use only its selected speed.");
                Assert.AreEqual(baseWorldSpeed * GrowthSystem.SimulationMultiplierForSpeed(nextSpeed) *
                    framesPerSegment * realFrameSeconds,
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
                    "A slow WebGL frame must not discard movement time or weaken the selected game-time multiplier.");
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

                Assert.AreEqual(distances[0] * GrowthSystem.SimulationMultiplierForSpeed(2f), distances[1], 0.02f,
                    "Behavior work caps must not alter the 60x world movement rate at 2x.");
                Assert.AreEqual(distances[0] * GrowthSystem.SimulationMultiplierForSpeed(3f), distances[2], 0.05f,
                    "Behavior work caps must not alter the 1,440x world movement rate at 3x.");
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

        [Test]
        public void PendingLitterNamesArePersistedBeforeSimulationUnpauses()
        {
            Func<ColonySaveData, string, bool> priorSaveInterceptor = SaveSystem.SaveInterceptorForTests;
            string savedJson = null;
            bool savingWhilePaused = false;
            GameObject gameObject = new GameObject("Pending Name Save Ordering Test");
            try
            {
                SaveSystem.SaveInterceptorForTests = (currentSave, source) =>
                {
                    savingWhilePaused = GrowthSystem.SimulationPaused;
                    savedJson = SaveSystem.ToJson(currentSave);
                    return true;
                };
                gameObject.SetActive(false);
                GameBootstrap game = gameObject.AddComponent<GameBootstrap>();
                ColonySaveData save = CreatePendingNamingTestSave(1);
                // The welcome popup independently owns a pause on fresh saves;
                // this test isolates the completed naming-modal transition.
                save.welcomePopupPending = false;
                typeof(GameBootstrap).GetProperty("Save").GetSetMethod(true).Invoke(game, new object[] { save });
                RatData pup = game.PendingNamingPups[0];
                GrowthSystem.SetSimulationPaused(true);

                string error;
                Assert.IsTrue(game.TryRenamePendingPup(pup.id, "Persisted Name", out error), error);
                Assert.IsTrue(game.ApprovePendingLitterNames());
                Assert.IsTrue(savingWhilePaused,
                    "The validated names and cleared naming queue must be passed to storage while still paused.");
                Assert.IsFalse(GrowthSystem.SimulationPaused,
                    "The pause is released only after the updated save has been written.");

                ColonySaveData persisted = SaveSystem.FromJson(savedJson);
                RatData persistedPup = BreedingSystem.FindRat(persisted, pup.id);
                Assert.IsNotNull(persistedPup);
                Assert.AreEqual("Persisted Name", persistedPup.name);
                Assert.IsFalse(persisted.pendingNamingLitterIds.Contains(pup.litterId));
            }
            finally
            {
                SaveSystem.SaveInterceptorForTests = priorSaveInterceptor;
                GrowthSystem.SetSimulationPaused(false);
                Object.DestroyImmediate(gameObject);
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
        public void MovementSpeedTransitionsFollowCalendarFastForwardAndDoNotCompound()
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
                        "A speed transition must affect only the current interval and must not compound the game-time multiplier.");
                    totalDistance += segmentDistance;
                }

                float expectedTotalMultiplier = 0f;
                for (int index = 0; index < segmentSpeeds.Length; index++)
                    expectedTotalMultiplier += GrowthSystem.SimulationMultiplierForSpeed(segmentSpeeds[index]);
                Assert.AreEqual(baseWorldSpeed * expectedTotalMultiplier, totalDistance, 0.001f,
                    "Each real-time segment must use exactly its corresponding game-time multiplier.");
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
                        GrowthSystem.SimulationMovementDeltaSeconds(realFrameSeconds);
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

                Assert.AreEqual(measuredFrameDistances[0] * GrowthSystem.SimulationMultiplierForSpeed(2f),
                    measuredFrameDistances[1], 0.0001f);
                Assert.AreEqual(measuredFrameDistances[0] * GrowthSystem.SimulationMultiplierForSpeed(3f),
                    measuredFrameDistances[2], 0.0001f);

                GrowthSystem.SetRuntimeSpeed(3f);
                float longFrameSimulationDelta = GrowthSystem.SimulationMovementDeltaSeconds(1f);
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
            Assert.IsFalse(eligible.hasPreviousSaleTank,
                "A permanently sold rat must not retain a return destination.");
            Assert.IsFalse(EnclosureSystem.CanReturnFromForSale(eligible),
                "Retired/automatically sold rats must not expose the My Rats return action.");
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
            StringAssert.Contains("assigned to Pairing Tank", result.autoSaleMessage);
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

        [Test]
        public void RestockCoalescesMissedCyclesAndProcessesAutomaticSaleOnce()
        {
            const long start = 1900000000L;
            ColonySaveData save = ColonyFactory.CreateNew(start);
            long firstRestockAt = save.storeNextRestockGameTime;
            RatData eligible = CreateAutoSaleTestRat(save,
                "coalesced-restock-sale", "Coalesced Sale", RatSex.Male, 120f);
            int walletBefore = save.colonyCredits;
            int expectedPayout = StoreSystem.CalculateSaleValue(save, eligible, firstRestockAt);
            long interval = GameConfig.StoreRestockIntervalGameMs;
            long jumpedGameTime = firstRestockAt + interval * 5L + interval / 2L;

            bool processed = StoreSystem.AdvanceRestock(save, jumpedGameTime, out StoreRestockResult result);

            Assert.IsTrue(processed);
            Assert.AreEqual(6, save.storeRestockCycle,
                "The market cycle advances past all elapsed deadlines without replaying individual restock passes.");
            Assert.AreEqual(firstRestockAt + interval * 6L, save.storeNextRestockGameTime);
            Assert.Greater(save.storeNextRestockGameTime, jumpedGameTime);
            Assert.AreEqual(1, result.soldRatCount,
                "A catch-up restock performs its automatic-sale pass only once.");
            Assert.AreEqual(expectedPayout, result.creditedDollars);
            Assert.AreEqual(walletBefore + expectedPayout, save.colonyCredits);

            bool repeated = StoreSystem.AdvanceRestock(save, jumpedGameTime, out StoreRestockResult repeatedResult);
            Assert.IsFalse(repeated, "The same elapsed restock window must not be processed again next frame.");
            Assert.AreEqual(0, repeatedResult.soldRatCount);
            Assert.AreEqual(walletBefore + expectedPayout, save.colonyCredits,
                "A restock catch-up cannot duplicate an automatic-sale payout.");
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
            RatData otherStarter = save.rats[1];
            otherStarter.enclosure = otherStarter.sex == RatSex.Male
                ? RatEnclosure.MaleColony
                : RatEnclosure.FemaleColony;
            otherStarter.pairingHabitatAssigned = false;
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
                "Both an adult and a pinkie occupy a persisted Pairing Tank slot.");
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
            Assert.AreEqual(0.005f, GameConfig.MarkingMutationRate, 0.000001f);
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
            Assert.AreEqual(0.009975f, preview.solidParentSpontaneousMarkingChance, 0.0000001f,
                "0.5% per inherited allele gives 0.9975% per solid-parent offspring, not 0.5% or 5%.");
            Assert.AreEqual(0.15f, GameConfig.StoreFounderMarkingChance);
            Assert.AreEqual(0.08f, GeneticsSystem.SecondaryMarkingInheritanceChance);
            Assert.AreEqual(0.005f, GameConfig.SpontaneousHairlessChance);

            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            try
            {
                int[] firstRun = CountSeededMutationRecords(solidGenotype, 12000, 481516);
                int[] repeatedRun = CountSeededMutationRecords(solidGenotype, 12000, 481516);
                CollectionAssert.AreEqual(firstRun, repeatedRun,
                    "The same random seed must reproduce the same inherited mutations.");

                // Each B/C/D locus has two inherited alleles at 0.25%; S has
                // two at 0.5%. These deterministic bounds distinguish the rates
                // without relying on an exact count from a random distribution.
                Assert.That(firstRun[0], Is.InRange(35, 85), "B-locus mutation count");
                Assert.That(firstRun[1], Is.InRange(35, 85), "C-locus mutation count");
                Assert.That(firstRun[2], Is.InRange(35, 85), "D-locus mutation count");
                Assert.That(firstRun[3], Is.InRange(85, 155),
                    "24,000 inherited S alleles at 0.5% should average 120 mutations.");

                RatData solidMother = new RatData { id = "solid-mother", genotype = solidGenotype.Clone(), markingFamily = "Solid" };
                RatData solidFather = new RatData { id = "solid-father", genotype = solidGenotype.Clone(), markingFamily = "Self" };
                const int solidParentOffspringCount = 50000;
                int markedPups = CountSeededSpontaneouslyMarkedOffspring(
                    solidMother, solidFather, solidParentOffspringCount, 481516);
                TestContext.WriteLine("Solid-parent marked offspring: {0}/{1} ({2:0.####}%). " +
                    "S mutation records: {3}/24000 inherited alleles.", markedPups,
                    solidParentOffspringCount, markedPups * 100d / solidParentOffspringCount, firstRun[3]);
                // Expected 498.75; +/-75 is over three binomial standard deviations.
                Assert.That(markedPups, Is.InRange(425, 575),
                    "Solid s/s parents should produce about 1% marked offspring, with most remaining solid.");

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

        [TestCase("S", "s", "s", "s", 0.5025f)]
        [TestCase("S", "s", "S", "s", 0.75f)]
        [TestCase("S", "S", "s", "s", 0.995025f)]
        public void MarkedParentInheritanceKeepsMendelianRulesWithRareAlleleMutation(
            string m1, string m2, string f1, string f2, float expectedMarkedRate)
        {
            GenotypeData mother = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", m1, m2);
            GenotypeData father = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", f1, f2);
            var previousRandomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(846201);
                const int offspringCount = 20000;
                int marked = 0;
                for (int index = 0; index < offspringCount; index++)
                {
                    GenotypeData child = GeneticsSystem.InheritGenotype(mother, father, index + 1L);
                    LocusData marking = GeneticsSystem.GetLocus(child, "S");
                    if (marking.firstAllele == "S" || marking.secondAllele == "S") marked++;
                }
                // Expected Mendelian transmission plus the unchanged symmetric
                // allele-mutation rule, now at p=.005: .5025, .75, .995025.
                double expected = offspringCount * expectedMarkedRate;
                double tolerance = 4d * Math.Sqrt(offspringCount * expectedMarkedRate * (1d-expectedMarkedRate));
                Assert.That(marked, Is.InRange(expected-tolerance, expected+tolerance),
                    "Marked parents must transmit existing S alleles normally, not use the solid-parent mutation rate.");
                Assert.AreEqual(GeneticsSystem.FormatPair("S", m1, m2), GeneticsSystem.FormatPair(mother, "S"));
                Assert.AreEqual(GeneticsSystem.FormatPair("S", f1, f2), GeneticsSystem.FormatPair(father, "S"));
                TestContext.WriteLine("{0} x {1}: {2}/{3} marked ({4:0.####}%), expected {5:0.####}%.",
                    GeneticsSystem.FormatPair(mother, "S"), GeneticsSystem.FormatPair(father, "S"),
                    marked, offspringCount, marked * 100d / offspringCount, expectedMarkedRate * 100d);
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
        public void PairingOpportunityCadenceMatchesPreviousOneXBaselineAndIsWallClockStable()
        {
            double previousOneXIntervalRealMs = GameConfig.PairingCheckIntervalMs * 1000d /
                GrowthSystem.SimulationMillisecondsPerRealSecond(1f);
            Assert.AreEqual(GameConfig.PairingCheckIntervalRealMs, previousOneXIntervalRealMs, 0.001d,
                "The restored wall-clock interval must equal the former 30-game-second cadence at 1x.");

            const long realNow = 123456789L;
            float[] speeds = { 1f, 2f, 3f };
            double[] expectedGameMsPerCheck = { 30000d, 1800000d, 43200000d };
            for (int index = 0; index < speeds.Length; index++)
            {
                long deadline = GameConfig.NextPairingCheckRealTimestamp(realNow);
                Assert.AreEqual(GameConfig.PairingCheckIntervalRealMs, deadline - realNow,
                    "Pairing opportunities stay on the same wall-clock cadence at " + speeds[index] + "x.");
                double advancedGameMs = GrowthSystem.SimulationMillisecondsPerRealSecond(speeds[index]) *
                    GameConfig.PairingCheckIntervalRealMs / 1000d;
                Assert.AreEqual(expectedGameMsPerCheck[index], advancedGameMs, 0.001d,
                    "The real-time scheduler must not alter the authoritative calendar's selected speed.");
            }
        }

        [Test]
        public void PairingSchedulerRecoversFertileWindowsCrossedByFastClockChecksAtEverySpeed()
        {
            PairingHabitatSystem.ResetDiagnostics();
            const long anchorTime = 560000000L;
            long fertileWindowMs = (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs);
            // A single wall-clock check may be delayed well beyond the old
            // two-second lookback (for example, while a courtship is active).
            // The scheduler must inspect the complete unsampled game-time
            // interval without replaying multiple pairing attempts.
            long scanStart = anchorTime -
                (long)(GameConfig.EstrousCycleDays * GameConfig.GameDayMs * 3f);
            long scanEnd = anchorTime + fertileWindowMs + 1000L;
            float[] speeds = { 1f, 2f, 3f };

            foreach (float speed in speeds)
            {
                ColonySaveData save = CreatePairingTestSave(anchorTime, 80f);
                save.clock.speed = speed;
                RatData chosenMale;
                RatData chosenFemale;

                Assert.IsTrue(PairingHabitatSystem.TryChoosePair(
                    save, scanStart, scanEnd, speed, out chosenMale, out chosenFemale),
                    "A recent fertile interval must remain eligible at " + speed + "x even if the latest timestamp is just past the window.");
                Assert.AreSame(save.rats[0], chosenFemale);
                Assert.IsTrue(BreedingSystem.IsBreedEligibleAtOpportunity(
                    save, chosenFemale, scanEnd, scanStart, out _));

                PairingSpeedDiagnosticsSnapshot speedStats = PairingHabitatSystem.DiagnosticsForSpeed(speed);
                Assert.AreEqual(1, speedStats.checks);
                Assert.AreEqual(1, speedStats.eligiblePairsFound);
                Assert.AreEqual(1, speedStats.pairingAttempts);
                Assert.AreEqual(1, speedStats.recoveredFertileWindows);
            }

            Assert.AreEqual(1, PairingHabitatSystem.DiagnosticsForSpeed(1f).checks);
            Assert.AreEqual(1, PairingHabitatSystem.DiagnosticsForSpeed(2f).checks);
            Assert.AreEqual(1, PairingHabitatSystem.DiagnosticsForSpeed(3f).checks);
        }

        [Test]
        public void PairingApproachWatchdogsUseRealTimeAndTheSamePairCompletesAtEverySpeed()
        {
            PairingHabitatSystem.ResetDiagnostics();
            const long startGameTime = 570000000L;
            const long elapsedRealTimeMs = 2000L;
            float[] speeds = { 1f, 2f, 3f };
            bool previousPaused = GrowthSystem.SimulationPaused;
            FieldInfo runtimeSpeedField = typeof(GrowthSystem).GetField(
                "runtimeSimulationSpeed", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(runtimeSpeedField);
            float previousRuntimeSpeed = (float)runtimeSpeedField.GetValue(null);
            MethodInfo advanceWatchdogs = typeof(GameBootstrap).GetMethod(
                "AdvancePairingApproachWatchdogs", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(advanceWatchdogs,
                "The focused test must exercise the watchdog helper used by the runtime update.");

            try
            {
                foreach (float speed in speeds)
                {
                    UnityEngine.Random.InitState(81273);
                    GrowthSystem.SetSimulationPaused(false);
                    GrowthSystem.SetRuntimeSpeed(speed);
                    ColonySaveData save = CreatePairingTestSave(startGameTime, 100f);
                    save.clock.speed = speed;
                    long endGameTime = startGameTime + (long)(
                        GrowthSystem.SimulationMillisecondsPerRealSecond(speed) *
                        elapsedRealTimeMs / 1000d);
                    RatData female = save.rats[0];
                    RatData male = save.rats[1];
                    RatData chosenMale;
                    RatData chosenFemale;

                    Assert.IsTrue(PairingHabitatSystem.TryChoosePair(
                        save, startGameTime + 1L, endGameTime, speed,
                        out chosenMale, out chosenFemale),
                        "The same two-real-second pairing opportunity should be found at " + speed + "x.");
                    Assert.AreSame(male, chosenMale);
                    Assert.AreSame(female, chosenFemale);
                    PairingHabitatSystem.RecordApproachStarted(speed);

                    // Model a physically progressing approach across the same
                    // two real seconds at every game speed. The runtime helper
                    // consumes unscaled seconds; accelerated calendar time is
                    // intentionally not passed to either watchdog.
                    float approachTimeout = 18f;
                    float stalledSeconds = 0f;
                    const int approachFrames = 40;
                    const float realFrameSeconds = 0.05f;
                    for (int frame = 0; frame < approachFrames; frame++)
                        AdvancePairingWatchdogsForTest(
                            advanceWatchdogs, ref approachTimeout, ref stalledSeconds,
                            realFrameSeconds, true, true);
                    Assert.AreEqual(16f, approachTimeout, 0.0001f,
                        "Approach timeout must burn two real seconds at " + speed + "x.");
                    Assert.AreEqual(0f, stalledSeconds, 0.0001f,
                        "World-space movement must reset the real-time stall timer at " + speed + "x.");

                    float stalledTimeout = 18f;
                    float stationarySeconds = 0f;
                    for (int frame = 0; frame < 49; frame++)
                        AdvancePairingWatchdogsForTest(
                            advanceWatchdogs, ref stalledTimeout, ref stationarySeconds,
                            realFrameSeconds, true, false);
                    Assert.AreEqual(2.45f, stationarySeconds, 0.001f,
                        "A blocked route accumulates wall-clock stall time, not accelerated game time.");
                    AdvancePairingWatchdogsForTest(
                        advanceWatchdogs, ref stalledTimeout, ref stationarySeconds,
                        realFrameSeconds, true, false);
                    Assert.That(stationarySeconds, Is.InRange(2.49f, 2.51f),
                        "The stall threshold is reached only after about 2.5 real seconds at " + speed + "x.");

                    string reason;
                    bool usedCommittedWindow;
                    Assert.IsTrue(BreedingSystem.IsBreedEligibleForCommittedPairingOpportunity(
                        save, female, endGameTime, true, out reason, out usedCommittedWindow), reason);
                    Assert.AreEqual(speed == 3f, usedCommittedWindow,
                        "Only 3x advances beyond this 1-game-day fertile window during the simulated approach.");

                    // The interaction completes after the same amount of game
                    // time, while its watchdog advances only by the much
                    // shorter real interval appropriate to the selected rate.
                    PairingHabitatSystem.RecordInteractionStarted(speed);
                    float interactionTimeout = 2.25f + 1.5f;
                    float interactionStall = 0f;
                    float realSecondsForInteraction = 2.25f /
                        GrowthSystem.SimulationBehaviorDeltaSeconds(1f);
                    AdvancePairingWatchdogsForTest(
                        advanceWatchdogs, ref interactionTimeout, ref interactionStall,
                        realSecondsForInteraction, false, false);
                    Assert.Greater(interactionTimeout, 0f,
                        "The game-time interaction must finish before its real-time watchdog at " + speed + "x.");
                    PairingHabitatSystem.RecordInteractionCompleted(speed);

                    // Resolve this exact male/female pair with identical seed
                    // and chance. The 3x case reaches this roll even though
                    // its original fertile window ended during the approach.
                    UnityEngine.Random.InitState(81273);
                    long resolutionGameTime = endGameTime + (long)Math.Round(
                        GrowthSystem.SimulationMillisecondsPerRealSecond(speed) * realSecondsForInteraction);
                    bool conceived;
                    PregnancyData pregnancy;
                    Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                        save, female, male, resolutionGameTime, 1f, true, speed,
                        out conceived, out reason, out pregnancy), reason);
                    Assert.IsTrue(conceived);
                    Assert.IsNotNull(pregnancy);
                }

                foreach (float speed in speeds)
                {
                    PairingSpeedDiagnosticsSnapshot stats = PairingHabitatSystem.DiagnosticsForSpeed(speed);
                    Assert.AreEqual(1, stats.checks, "One coalesced check at " + speed + "x.");
                    Assert.AreEqual(1, stats.eligiblePairsFound, "One eligible pair at " + speed + "x.");
                    Assert.AreEqual(1, stats.pairingAttempts, "One normal attempt at " + speed + "x.");
                    Assert.AreEqual(1, stats.pairSelected, "The selected pair is attributed at " + speed + "x.");
                    Assert.AreEqual(1, stats.approachStarted, "Approach reaches its runtime stage at " + speed + "x.");
                    Assert.AreEqual(0, stats.approachCancelled, "No watchdog/route cancellation at " + speed + "x.");
                    Assert.AreEqual(0, stats.routeFailures);
                    Assert.AreEqual(1, stats.interactionStarted);
                    Assert.AreEqual(1, stats.interactionCompleted);
                    Assert.AreEqual(0, stats.interactionCancelled);
                    Assert.AreEqual(1, stats.conceptionRolls, "Same conception stage reached at " + speed + "x.");
                    Assert.AreEqual(1, stats.successfulConceptions, "The deterministic test roll succeeds at " + speed + "x.");
                    Assert.AreEqual(0, stats.failedConceptionRolls);
                    Assert.AreEqual(speed == 3f ? 1 : 0, stats.recoveredFertileWindows);
                }
            }
            finally
            {
                GrowthSystem.SetRuntimeSpeed(previousRuntimeSpeed);
                GrowthSystem.SetSimulationPaused(previousPaused);
            }
        }

        [Test]
        public void TenSimultaneousDueBirthsSerializeDeliveryApproachAndKeepFamiliesInNest()
        {
            const long now = 580000000L;
            ColonySaveData save = CreatePairingTestSave(now, 100f);
            RatData sharedFather = save.rats[1];
            var genotype = GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s");
            var mothers = new List<RatData>();
            var pregnancies = new List<PregnancyData>();

            for (int index = 0; index < 10; index++)
            {
                RatData mother;
                if (index == 0)
                {
                    mother = save.rats[0];
                }
                else
                {
                    mother = ColonyFactory.CreateRat(
                        "queue-mother-" + index, "Queue Mother " + index, RatSex.Female,
                        now - (100L * GameConfig.GameDayMs), 0, genotype.Clone(),
                        new TraitData(75f, 80f, 90f), RatStage.Adult);
                    mother.enclosure = RatEnclosure.Pairing;
                    mother.pairingHabitatAssigned = true;
                    mother.ageDays = 100f;
                    mother.sexualMaturityDays = 70f;
                    mother.breedingEndAgeDays = 700f;
                    save.rats.Add(mother);
                    save.ratIds.Add(mother.id);
                }

                PregnancyData pregnancy = new PregnancyData
                {
                    id = "due-pregnancy-" + index,
                    motherId = mother.id,
                    fatherId = sharedFather.id,
                    startedAt = now - GameConfig.PregnancyMs,
                    dueAt = now,
                    gestationDurationMs = GameConfig.PregnancyMs,
                    expectedLitterSize = 3,
                    status = "pending",
                };
                mother.pregnancyId = pregnancy.id;
                mother.reproductiveState = ReproductiveState.Pregnant;
                save.pregnancies.Add(pregnancy);
                mothers.Add(mother);
                pregnancies.Add(pregnancy);
            }
            BreedingSystem.InvalidateReproductiveStateIndexes(save);

            var reservations = new BirthQueueReservations();
            int approaches = 0;
            int waiting = 0;
            for (int index = 0; index < pregnancies.Count; index++)
            {
                PregnancyData pregnancy = pregnancies[index];
                if (reservations.TryReserve(RatEnclosure.Pairing, pregnancy.motherId))
                {
                    pregnancy.birthApproachStarted = true;
                    approaches++;
                }
                else
                {
                    pregnancy.birthWaitingForNest = true;
                    waiting++;
                }
            }

            Assert.AreEqual(1, approaches, "Only one mother may use the shared delivery approach at a time.");
            Assert.AreEqual(9, waiting, "All other due mothers remain safely queued.");
            Assert.AreEqual(1, reservations.Count);
            Assert.AreEqual("Going to give birth", RatActivitySystem.CurrentLabel(save, mothers[0], now));
            for (int index = 1; index < mothers.Count; index++)
                Assert.AreEqual("Waiting to give birth", RatActivitySystem.CurrentLabel(save, mothers[index], now));

            // Model a timed-out route. The pregnancy is still pending, the
            // stale approach flag is cleared, and the mother remains queued
            // while its real-time retry cooldown elapses.
            PregnancyData retryPregnancy = pregnancies[0];
            retryPregnancy.birthApproachStarted = false;
            retryPregnancy.birthApproachStartedAt = 0L;
            retryPregnancy.birthWaitingForNest = true;
            Assert.IsTrue(reservations.Release(RatEnclosure.Pairing, retryPregnancy.motherId));
            Assert.AreEqual("pending", retryPregnancy.status);
            Assert.IsFalse(retryPregnancy.birthApproachStarted,
                "A route timeout must never leave the persisted approach flag stuck on.");

            // Completed mothers remain in the Pairing Tank with their litters
            // while the delivery approach is immediately reused by the next
            // queued mother. The reservation serializes delivery only; it does
            // not reserve the shared nest for one nursing family.
            int successfulBeforeRetry = 0;
            while (successfulBeforeRetry < 9)
            {
                Assert.IsTrue(reservations.TryGetOccupant(RatEnclosure.Pairing, out string activeMother));
                PregnancyData activePregnancy = pregnancies.Find(item => item.motherId == activeMother);
                Assert.IsNotNull(activePregnancy);
                Assert.IsTrue(activePregnancy.birthApproachStarted);
                Assert.IsFalse(activePregnancy.birthWaitingForNest);

                activePregnancy.status = "finished";
                RatData nursingMother = mothers.Find(item => item.id == activeMother);
                nursingMother.nursing = true;
                nursingMother.nursingLitterId = "litter-for-" + activeMother;
                activePregnancy.birthApproachStarted = false;
                activePregnancy.birthApproachStartedAt = 0L;
                Assert.IsTrue(reservations.Release(RatEnclosure.Pairing, activeMother));
                successfulBeforeRetry++;

                PregnancyData next = pregnancies.Find(item => item != retryPregnancy &&
                    item.status == "pending" && item.birthWaitingForNest);
                Assert.IsNotNull(next, "A queued due pregnancy should automatically become the next owner.");
                Assert.IsTrue(reservations.TryReserve(RatEnclosure.Pairing, next.motherId));
                next.birthWaitingForNest = false;
                next.birthApproachStarted = true;

                int activeFlags = 0;
                foreach (PregnancyData pending in pregnancies)
                    if (pending.status == "pending" && pending.birthApproachStarted) activeFlags++;
                Assert.AreEqual(1, activeFlags, "Only one mother may approach the shared delivery spot.");
            }

            Assert.IsFalse(reservations.TryGetOccupant(RatEnclosure.Pairing, out _));
            Assert.AreEqual("pending", retryPregnancy.status,
                "The retrying pregnancy must not be discarded while other queued births complete.");
            Assert.IsTrue(retryPregnancy.birthWaitingForNest);
            Assert.IsTrue(reservations.TryReserve(RatEnclosure.Pairing, retryPregnancy.motherId),
                "Once the retry cooldown expires, the retained pregnancy can claim the free nest.");
            retryPregnancy.birthWaitingForNest = false;
            retryPregnancy.birthApproachStarted = true;
            retryPregnancy.status = "finished";
            RatData retryingMother = mothers.Find(item => item.id == retryPregnancy.motherId);
            retryingMother.nursing = true;
            retryingMother.nursingLitterId = "litter-for-" + retryPregnancy.motherId;
            retryPregnancy.birthApproachStarted = false;
            Assert.IsTrue(reservations.Release(RatEnclosure.Pairing, retryPregnancy.motherId));

            Assert.AreEqual(0, reservations.Count);
            Assert.AreEqual(10, mothers.FindAll(item => item.nursing &&
                !string.IsNullOrEmpty(item.nursingLitterId) &&
                item.enclosure == RatEnclosure.Pairing).Count,
                "Previously delivered mothers stay in the same tank with their pinkies while later births proceed.");
            foreach (PregnancyData pregnancy in pregnancies)
            {
                Assert.AreEqual("finished", pregnancy.status);
                Assert.IsFalse(pregnancy.birthApproachStarted,
                    "Completed pregnancies must not retain a stale approach flag.");
                Assert.IsFalse(pregnancy.birthWaitingForNest);
            }
        }

        private static void AdvancePairingWatchdogsForTest(
            MethodInfo method, ref float phaseTimeout, ref float stalledSeconds,
            float unscaledDeltaSeconds, bool walking, bool eitherRatMoved)
        {
            object[] arguments =
            {
                phaseTimeout,
                stalledSeconds,
                unscaledDeltaSeconds,
                walking,
                eitherRatMoved,
            };
            method.Invoke(null, arguments);
            phaseTimeout = (float)arguments[0];
            stalledSeconds = (float)arguments[1];
        }

        [Test]
        public void CommittedFertileWindowDoesNotBypassCurrentCooldownRecoveryOrPregnancy()
        {
            const long gameTime = 575000000L;
            ColonySaveData save = CreatePairingTestSave(gameTime, 100f);
            save.clock.speed = 3f;
            RatData female = save.rats[0];
            RatData male = save.rats[1];
            long afterFertileWindow = gameTime + 2L * GameConfig.GameDayMs;
            string reason;
            bool usedCommittedWindow;

            Assert.IsTrue(BreedingSystem.IsBreedEligibleForCommittedPairingOpportunity(
                save, female, afterFertileWindow, true, out reason, out usedCommittedWindow));
            Assert.IsTrue(usedCommittedWindow);

            female.breedingCooldownUntil = afterFertileWindow + 1L;
            Assert.IsFalse(BreedingSystem.IsBreedEligibleForCommittedPairingOpportunity(
                save, female, afterFertileWindow, true, out reason, out usedCommittedWindow),
                "The committed window must not bypass an active breeding cooldown.");

            female.breedingCooldownUntil = 0L;
            female.nursing = true;
            female.reproductiveState = ReproductiveState.Nursing;
            female.recoveryUntil = afterFertileWindow + 1L;
            Assert.IsFalse(BreedingSystem.IsBreedEligibleForCommittedPairingOpportunity(
                save, female, afterFertileWindow, true, out reason, out usedCommittedWindow),
                "The committed window must not bypass post-birth recovery while nursing.");

            female.nursing = false;
            female.reproductiveState = ReproductiveState.Fertile;
            female.recoveryUntil = 0L;
            bool conceived;
            Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                save, female, male, afterFertileWindow, 1f, true,
                out conceived, out reason), reason);
            Assert.IsTrue(conceived);
            Assert.IsFalse(BreedingSystem.IsBreedEligibleForCommittedPairingOpportunity(
                save, female, afterFertileWindow, true, out reason, out usedCommittedWindow),
                "The committed window must not bypass a current pregnancy.");
        }

        [Test]
        public void PairingSelectionUsesTimestampAuthoritativeStageAfterFastForward()
        {
            const long gameTime = 580000000L;
            ColonySaveData save = CreatePairingTestSave(gameTime, 100f);
            save.clock.speed = 3f;
            // Stage maintenance can lag behind a large fast-forward batch;
            // reproductive eligibility already derives stage from timestamp.
            save.rats[0].stage = RatStage.YoungRat;

            RatData male;
            RatData female;
            Assert.IsTrue(PairingHabitatSystem.TryChoosePair(
                save, gameTime, gameTime, 3f, out male, out female));
            Assert.AreSame(save.rats[0], female);
        }

        [Test]
        public void PairingDiagnosticsSeparateCooldownCapacityAndConceptionBySelectedSpeed()
        {
            PairingHabitatSystem.ResetDiagnostics();
            const long gameTime = 565000000L;
            ColonySaveData save = CreatePairingTestSave(gameTime, 80f);
            save.clock.speed = 3f;
            save.rats[0].breedingCooldownUntil = gameTime + 1L;

            RatData chosenMale;
            RatData chosenFemale;
            Assert.IsFalse(PairingHabitatSystem.TryChoosePair(
                save, gameTime, gameTime, 3f, out chosenMale, out chosenFemale));
            PairingHabitatSystem.RecordCapacityBlocked("Pairing Tank is full.", 3f);

            PairingSpeedDiagnosticsSnapshot blocked = PairingHabitatSystem.DiagnosticsForSpeed(3f);
            Assert.AreEqual(1, blocked.checks);
            Assert.AreEqual(1, blocked.cooldownBlockedChecks);
            Assert.AreEqual(1, blocked.cooldownBlockedCandidates);
            Assert.AreEqual(1, blocked.capacityBlockedAttempts);

            save = CreatePairingTestSave(gameTime, 100f);
            save.clock.speed = 3f;
            bool conceived;
            string reason;
            Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                save, save.rats[0], save.rats[1], gameTime, 1f, out conceived, out reason), reason);
            Assert.IsTrue(conceived);
            PairingSpeedDiagnosticsSnapshot resolved = PairingHabitatSystem.DiagnosticsForSpeed(3f);
            Assert.AreEqual(1, resolved.conceptionRolls);
            Assert.AreEqual(1, resolved.successfulConceptions);
            Assert.AreEqual(0, PairingHabitatSystem.DiagnosticsForSpeed(1f).conceptionRolls);

            float chance1x = BreedingSystem.CalculateConceptionChance(
                save.rats[0], save.rats[1], GameConfig.PairingPregnancyChance, 0f, GameConfig.PairingPregnancyChance);
            save.clock.speed = 1f;
            float chanceAt1x = BreedingSystem.CalculateConceptionChance(
                save.rats[0], save.rats[1], GameConfig.PairingPregnancyChance, 0f, GameConfig.PairingPregnancyChance);
            save.clock.speed = 2f;
            float chanceAt2x = BreedingSystem.CalculateConceptionChance(
                save.rats[0], save.rats[1], GameConfig.PairingPregnancyChance, 0f, GameConfig.PairingPregnancyChance);
            save.clock.speed = 3f;
            float chanceAt3x = BreedingSystem.CalculateConceptionChance(
                save.rats[0], save.rats[1], GameConfig.PairingPregnancyChance, 0f, GameConfig.PairingPregnancyChance);
            Assert.AreEqual(chance1x, chanceAt1x, 0.000001f);
            Assert.AreEqual(chance1x, chanceAt2x, 0.000001f);
            Assert.AreEqual(chance1x, chanceAt3x, 0.000001f);

            PairingHabitatSystem.RecordSkippedChecks(2f, 4L);
            Assert.AreEqual(4, PairingHabitatSystem.DiagnosticsForSpeed(2f).skippedChecks);
            Assert.AreEqual(0, PairingHabitatSystem.DiagnosticsForSpeed(1f).skippedChecks);
        }

        [Test]
        public void PairingDiagnosticsSeparateAvailableChecksCooldownBlocksFailedRollsAndConceptions()
        {
            PairingHabitatSystem.ResetDiagnostics();
            const long gameTime = 550000000L;
            var save = CreatePairingTestSave(gameTime, 80f);

            RatData chosenMale;
            RatData chosenFemale;
            Assert.IsTrue(PairingHabitatSystem.TryChoosePair(save, gameTime, out chosenMale, out chosenFemale));
            PairingDiagnosticsSnapshot diagnostics = PairingHabitatSystem.Diagnostics;
            Assert.AreEqual(1, diagnostics.checks);
            Assert.AreEqual(1, diagnostics.checksWithEligiblePair);
            Assert.AreEqual(1, diagnostics.lastEligibleMales);
            Assert.AreEqual(1, diagnostics.lastEligibleFemales);
            Assert.AreEqual(1, diagnostics.lastEligiblePairCombinations);
            Assert.AreEqual(2, diagnostics.lastPairingOccupants);

            bool conceived;
            string reason;
            Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                save, chosenFemale, chosenMale, gameTime, 0f, out conceived, out reason), reason);
            Assert.IsFalse(conceived);
            diagnostics = PairingHabitatSystem.Diagnostics;
            Assert.AreEqual(1, diagnostics.conceptionRolls);
            Assert.AreEqual(1, diagnostics.failedConceptionRolls);
            Assert.AreEqual(0, diagnostics.successfulConceptions);

            Assert.IsFalse(PairingHabitatSystem.TryChoosePair(
                save, gameTime + 1L, out chosenMale, out chosenFemale),
                "The saved per-rat cooldown must block the immediate next check.");
            diagnostics = PairingHabitatSystem.Diagnostics;
            Assert.AreEqual(1, diagnostics.checksWithoutEligiblePair);
            Assert.AreEqual(2, diagnostics.blockedCooldownCandidates);

            long retryAt = gameTime + GameConfig.PairingAttemptCooldownMs + 1L;
            Assert.IsTrue(PairingHabitatSystem.TryChoosePair(save, retryAt, out chosenMale, out chosenFemale));
            Assert.IsTrue(PairingHabitatSystem.ResolvePair(
                save, chosenFemale, chosenMale, retryAt, 1f, out conceived, out reason), reason);
            Assert.IsTrue(conceived);
            diagnostics = PairingHabitatSystem.Diagnostics;
            Assert.AreEqual(2, diagnostics.conceptionRolls);
            Assert.AreEqual(1, diagnostics.failedConceptionRolls);
            Assert.AreEqual(1, diagnostics.successfulConceptions);
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
                "Moved to Pairing Tank"));

            string json = SaveSystem.ToJson(save);
            ColonySaveData restored = SaveSystem.FromJson(json);
            RatData restoredRat = BreedingSystem.FindRat(restored, ratId);

            Assert.IsNotNull(restoredRat);
            Assert.IsNotNull(restoredRat.activity);
            Assert.AreEqual("Moving habitats", restoredRat.activity.currentActivityLabel,
                "The activity data belongs to the rat ID, not its display name.");
            Assert.AreEqual(2, restoredRat.activity.history.Count);
            Assert.AreEqual("Moved to Pairing Tank", restoredRat.activity.history[0].message);
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

        [TestCase(RatEnclosure.MaleColony)]
        [TestCase(RatEnclosure.FemaleColony)]
        [TestCase(RatEnclosure.Pairing)]
        public void FavoriteCannotEnterSaleTankButManualSaleEligibilityAndValueStayUnchanged(RatEnclosure origin)
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatSex sex = origin == RatEnclosure.MaleColony ? RatSex.Male : RatSex.Female;
            RatData favorite = CreateSaleTestRat(save, "sale-favorite-individual", sex, 100f, RatStage.Adult);
            favorite.enclosure = origin;
            favorite.pairingHabitatAssigned = origin == RatEnclosure.Pairing;
            int value = StoreSystem.CalculateSaleValue(save, favorite, now);
            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, favorite.id, true));

            Assert.IsTrue(StoreSystem.CanSellRat(save, favorite, now),
                "This protection applies to the unattended Sale Tank, not deliberate manual sales.");
            Assert.AreEqual(value, StoreSystem.CalculateSaleValue(save, favorite, now));
            Assert.IsFalse(EnclosureSystem.CanEnterForSaleTank(save, favorite, now));
            Assert.IsFalse(EnclosureSystem.TryAssignToForSale(save, favorite, now, 20, out string reason));
            Assert.AreEqual(EnclosureSystem.FavoriteSaleTankRestriction, reason);
            Assert.AreEqual(origin, favorite.enclosure);
            Assert.AreEqual(origin == RatEnclosure.Pairing, favorite.pairingHabitatAssigned);
            Assert.IsFalse(favorite.hasPreviousSaleTank);
            Assert.IsTrue(favorite.isFavorite);
        }

        [Test]
        public void BulkSaleTankMoveSkipsFavoritesWithoutConsumingCapacityAndReportsTheirCount()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData favoriteA = CreateSaleTestRat(save, "sale-bulk-favorite-a", RatSex.Female, 100f, RatStage.Adult);
            RatData eligibleA = CreateSaleTestRat(save, "sale-bulk-nonfavorite-a", RatSex.Male, 100f, RatStage.Adult);
            RatData favoriteB = CreateSaleTestRat(save, "sale-bulk-favorite-b", RatSex.Male, 100f, RatStage.Adult);
            RatData eligibleB = CreateSaleTestRat(save, "sale-bulk-nonfavorite-b", RatSex.Female, 100f, RatStage.Adult);
            favoriteA.isFavorite = favoriteB.isFavorite = true;

            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 1,
                out int moved, out string reason, out List<RatData> movedRats));
            Assert.AreEqual(1, moved);
            Assert.AreEqual(1, movedRats.Count);
            Assert.IsFalse(movedRats[0].isFavorite);
            Assert.AreEqual(1, EnclosureSystem.CountForSaleRats(save));
            Assert.AreEqual(RatEnclosure.FemaleColony, favoriteA.enclosure);
            Assert.AreEqual(RatEnclosure.MaleColony, favoriteB.enclosure);
            Assert.IsFalse(favoriteA.hasPreviousSaleTank);
            Assert.IsFalse(favoriteB.hasPreviousSaleTank);
            Assert.IsTrue(movedRats[0].hasPreviousSaleTank);
            StringAssert.Contains("Skipped 2 favorite rats", reason);
            StringAssert.Contains("1 eligible rat could not be moved because the tank is full", reason);
            Assert.AreEqual(4, save.rats.Count);
            Assert.IsTrue(eligibleA.enclosure == RatEnclosure.ForSale || eligibleB.enclosure == RatEnclosure.ForSale);
        }

        [Test]
        public void AutomaticAssignmentCannotSendFavoriteBreedingParticipantsToSaleTank()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData female = CreateSaleTestRat(save, "sale-auto-favorite-female", RatSex.Female, 100f, RatStage.Adult);
            RatData male = CreateSaleTestRat(save, "sale-auto-favorite-male", RatSex.Male, 100f, RatStage.Adult);
            female.isFavorite = male.isFavorite = true;
            EnclosureSystem.SetBreedingPair(female.id, male.id);
            try
            {
                EnclosureSystem.RecalculateAssignments(save);
                Assert.AreEqual(RatEnclosure.FemaleColony, female.enclosure);
                Assert.AreEqual(RatEnclosure.MaleColony, male.enclosure);
                Assert.AreEqual(0, EnclosureSystem.CountForSaleRats(save));
                Assert.IsFalse(female.hasPreviousSaleTank);
                Assert.IsFalse(male.hasPreviousSaleTank);
            }
            finally { EnclosureSystem.ClearBreedingPair(); }
        }

        [Test]
        public void FavoritingSaleTankRatRequiresReturnFirstAndSurvivesSaveLoad()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData rat = CreateSaleTestRat(save, "sale-star-guard", RatSex.Female, 100f, RatStage.Adult);
            rat.enclosure = RatEnclosure.Pairing;
            rat.pairingHabitatAssigned = true;
            Assert.IsTrue(EnclosureSystem.TryAssignToForSale(save, rat, now, 20, out string reason), reason);

            Assert.IsFalse(RatFavoriteSystem.SetFavorite(save, rat.id, true, out reason));
            StringAssert.Contains("Remove this rat from the For Sale Tank", reason);
            Assert.IsFalse(rat.isFavorite);
            Assert.AreEqual(RatEnclosure.ForSale, rat.enclosure);
            Assert.IsTrue(rat.hasPreviousSaleTank);
            Assert.AreEqual(RatEnclosure.Pairing, rat.previousSaleTank);
            Assert.IsTrue(EnclosureSystem.TryReturnFromForSale(save, rat, 10,
                out RatEnclosure destination, out reason), reason);
            Assert.AreEqual(RatEnclosure.Pairing, destination);
            Assert.IsTrue(RatFavoriteSystem.SetFavorite(save, rat.id, true, out reason), reason);

            ColonySaveData restored = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData loaded = BreedingSystem.FindRat(restored, rat.id);
            Assert.IsNotNull(loaded);
            Assert.IsTrue(loaded.isFavorite);
            Assert.AreEqual(RatEnclosure.Pairing, loaded.enclosure);
            Assert.IsFalse(EnclosureSystem.TryAssignToForSale(restored, loaded, now, 20, out reason));
            Assert.IsFalse(loaded.hasPreviousSaleTank);
        }

        [Test]
        public void LegacyFavoriteInSaleTankCannotBeAutomaticallySoldOrPaidOut()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData favorite = CreateSaleTestRat(save, "sale-legacy-favorite", RatSex.Female, 100f, RatStage.Adult);
            RatData ordinary = CreateSaleTestRat(save, "sale-ordinary-auto", RatSex.Male, 100f, RatStage.Adult);
            favorite.enclosure = ordinary.enclosure = RatEnclosure.ForSale;
            favorite.isFavorite = true; // legacy/stale state that bypasses the new admission guard
            favorite.hasPreviousSaleTank = true;
            favorite.previousSaleTank = RatEnclosure.FemaleColony;
            favorite.motherId = "favorite-historical-mother";
            int walletBefore = save.colonyCredits;
            int ordinaryValue = StoreSystem.CalculateSaleValue(save, ordinary, now);

            StoreRestockResult result = StoreSystem.RestockNowWithResult(save, now);
            Assert.AreEqual(1, result.soldRatCount);
            Assert.AreEqual(1, result.skippedRatCount);
            Assert.AreEqual(ordinaryValue, result.creditedDollars);
            Assert.AreEqual(walletBefore + ordinaryValue, save.colonyCredits);
            Assert.AreEqual(RatRemovalDisposition.None, favorite.removalDisposition);
            Assert.IsTrue(save.rats.Contains(favorite));
            Assert.IsFalse(save.retiredRats.Contains(favorite));
            Assert.IsTrue(favorite.isFavorite);
            Assert.IsTrue(favorite.hasPreviousSaleTank);
            Assert.AreEqual("favorite-historical-mother", favorite.motherId);
            StringAssert.Contains("favorite (protected from automatic sale)", result.autoSaleMessage);
            Assert.AreEqual(0, StoreSystem.RestockNowWithResult(save, now).soldRatCount);
            Assert.AreEqual(walletBefore + ordinaryValue, save.colonyCredits);
        }

        [Test]
        public void LegacyFavoriteSaleAssignmentReconcilesToNormalTankWithoutLosingRat()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData favorite = CreateSaleTestRat(save, "sale-legacy-favorite-reconcile", RatSex.Male, 100f, RatStage.Adult);
            favorite.isFavorite = true;
            favorite.enclosure = RatEnclosure.ForSale;
            Assert.IsTrue(EnclosureSystem.RecalculateAssignments(save));
            Assert.AreEqual(RatEnclosure.MaleColony, favorite.enclosure);
            Assert.IsTrue(favorite.isFavorite);
            Assert.AreSame(favorite, BreedingSystem.FindRat(save, favorite.id));
            Assert.AreEqual(1, save.rats.Count);
            Assert.AreEqual(0, save.retiredRats.Count);
            Assert.AreEqual(0, EnclosureSystem.CountForSaleRats(save));
        }

        [Test]
        public void PregnantRatCannotBeMovedToSaleTankAndShowsAuthoritativeReason()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData mother = CreateSaleTestRat(save, "sale-pregnant-mother", RatSex.Female,
                100f, RatStage.Adult);
            AddActiveSaleTestPregnancy(save, mother, null, now);

            Assert.IsFalse(StoreSystem.CanSellRat(save, mother, now),
                "The authoritative sale eligibility check must reject an active pregnancy.");
            Assert.AreEqual("Pregnant rats cannot be sold.",
                StoreSystem.SaleRestrictionReason(save, mother, now));
            Assert.IsFalse(EnclosureSystem.TryAssignToForSale(save, mother, now, 20, out string reason));
            Assert.AreEqual("Pregnant rats cannot be sold.", reason);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            Assert.IsFalse(mother.hasPreviousSaleTank,
                "A rejected transfer must not create a return-tank record.");
        }

        [Test]
        public void BulkSaleTankMoveSkipsPregnantRatsAndReportsTheirCount()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData eligible = CreateSaleTestRat(save, "sale-bulk-eligible", RatSex.Male,
                100f, RatStage.Adult);
            RatData mother = CreateSaleTestRat(save, "sale-bulk-pregnant", RatSex.Female,
                100f, RatStage.Adult);
            AddActiveSaleTestPregnancy(save, mother, null, now);

            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(
                save, now, 20, out int moved, out string reason));

            Assert.AreEqual(1, moved);
            Assert.AreEqual(RatEnclosure.ForSale, eligible.enclosure);
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            Assert.IsFalse(mother.hasPreviousSaleTank);
            StringAssert.Contains("Skipped 1 pregnant rat; pregnant rats cannot be sold.", reason);
        }

        [Test]
        public void AutomaticAssignmentDoesNotMovePregnantBreedingParticipantsToSaleTank()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData mother = CreateSaleTestRat(save, "sale-auto-pregnant", RatSex.Female,
                100f, RatStage.Adult);
            RatData father = CreateSaleTestRat(save, "sale-auto-pregnant-father", RatSex.Male,
                100f, RatStage.Adult);
            AddActiveSaleTestPregnancy(save, mother, father, now);
            EnclosureSystem.SetBreedingPair(mother.id, father.id);

            try
            {
                EnclosureSystem.RecalculateAssignments(save);
                Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure,
                    "Automatic breeding-participant routing must apply the shared pregnancy sale block.");
                Assert.AreEqual(RatEnclosure.ForSale, father.enclosure,
                    "The unrelated, eligible male keeps the existing automatic assignment behavior.");
            }
            finally
            {
                EnclosureSystem.ClearBreedingPair();
            }
        }

        [Test]
        public void RatInSaleTankCannotBeSelectedOrResolvedAsAParent()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreatePairingTestSave(now, 100f);
            RatData female = save.rats[0];
            RatData male = save.rats[1];
            female.enclosure = RatEnclosure.ForSale;
            female.pairingHabitatAssigned = false;
            male.enclosure = RatEnclosure.Pairing;
            int pregnancyCountBefore = save.pregnancies.Count;

            Assert.IsFalse(PairingHabitatSystem.TryChoosePair(
                save, now, now, 1f, out RatData selectedMale, out RatData selectedFemale));
            Assert.IsNull(selectedFemale,
                "The pairing selector only considers rats assigned to the Pairing Tank.");
            Assert.IsFalse(PairingHabitatSystem.ResolvePair(save, female, male, now,
                1f, out bool conceived, out string reason));
            Assert.IsFalse(conceived);
            StringAssert.Contains("Both rats must be in the Pairing Tank", reason);
            Assert.AreEqual(pregnancyCountBefore, save.pregnancies.Count,
                "A Sale Tank resident cannot become pregnant through the pairing runtime.");
            Assert.IsTrue(StoreSystem.CanSellRat(save, female, now),
                "Sale eligibility does not grant breeding eligibility; sale-tank assignment remains the breeding gate.");
        }

        [Test]
        public void PregnantRatAlreadyInSaleTankIsNotAutomaticallySold()
        {
            EnclosureSystem.ClearBreedingPair();
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData mother = CreateSaleTestRat(save, "sale-already-pregnant", RatSex.Female,
                100f, RatStage.Adult);
            AddActiveSaleTestPregnancy(save, mother, null, now);
            mother.enclosure = RatEnclosure.ForSale;
            mother.previousSaleTank = RatEnclosure.FemaleColony;
            mother.hasPreviousSaleTank = true;
            int walletBefore = save.colonyCredits;

            StoreRestockResult result = StoreSystem.RestockNowWithResult(save, now);

            Assert.AreEqual(0, result.soldRatCount);
            Assert.AreEqual(1, result.skippedRatCount);
            Assert.AreEqual(walletBefore, save.colonyCredits);
            Assert.IsTrue(save.rats.Contains(mother));
            Assert.AreEqual(RatRemovalDisposition.None, mother.removalDisposition);
            Assert.IsTrue(mother.hasPreviousSaleTank,
                "A blocked automatic sale must not erase the existing return destination.");
            StringAssert.Contains("(pregnant)", result.autoSaleMessage);
        }

        [Test]
        public void SaleTankBulkMoveUsesRemainingCapacityAndReportsSkippedCount()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            RatData first = CreateSaleTestRat(save, "sale-capacity-1", RatSex.Male, 100f, RatStage.Adult);
            RatData second = CreateSaleTestRat(save, "sale-capacity-2", RatSex.Female, 100f, RatStage.Adult);
            int moved;
            string reason;

            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 1, out moved, out reason));
            Assert.AreEqual(1, moved);
            Assert.AreEqual(1, EnclosureSystem.CountForSaleRats(save));
            Assert.AreEqual(1, (first.enclosure == RatEnclosure.ForSale ? 1 : 0) +
                (second.enclosure == RatEnclosure.ForSale ? 1 : 0));
            StringAssert.Contains("Moved 1 rat to the For Sale Tank.", reason);
            StringAssert.Contains("1 eligible rat could not be moved because the tank is full.", reason);
        }

        [Test]
        public void SaleTankBulkMoveOf300RatsFills20SlotsOnceAndReportsTheRemainder()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            var save = CreateEmptySaleTestSave(now);
            for (int index = 0; index < 300; index++)
            {
                RatSex sex = (index & 1) == 0 ? RatSex.Male : RatSex.Female;
                CreateSaleTestRat(save, "sale-bulk-large-" + index, sex, 100f, RatStage.Adult);
            }

            int moved;
            string reason;
            List<RatData> movedRats;
            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 20,
                out moved, out reason, out movedRats));

            Assert.AreEqual(20, moved);
            Assert.AreEqual(20, movedRats.Count);
            var movedIds = new HashSet<string>(StringComparer.Ordinal);
            int outsideSaleCount = 0;
            foreach (RatData rat in save.rats)
                if (rat.enclosure != RatEnclosure.ForSale) outsideSaleCount++;
            foreach (RatData rat in movedRats)
            {
                Assert.IsTrue(movedIds.Add(rat.id), "The batch must never process one rat more than once.");
                Assert.IsTrue(rat.hasPreviousSaleTank, "Every moved rat retains its return destination.");
            }
            Assert.AreEqual(20, movedIds.Count);
            Assert.AreEqual(20, EnclosureSystem.CountForSaleRats(save));
            Assert.AreEqual(300, save.rats.Count, "Tank moves must not duplicate or delete colony records.");
            Assert.AreEqual(280, outsideSaleCount);
            Assert.AreEqual("Moved 20 rats to the For Sale Tank. 280 eligible rats could not be moved because the tank is full.",
                reason);

            List<RatData> secondBatch;
            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 20,
                out moved, out reason, out secondBatch));
            Assert.AreEqual(0, moved, "A second click at capacity cannot move any rat a second time.");
            Assert.AreEqual(0, secondBatch.Count);
            Assert.AreEqual(20, EnclosureSystem.CountForSaleRats(save));
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
        public void SaleTankIndividualMoveCanReturnRatToItsPreviousTank()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData female = CreateSaleTestRat(save, "sale-return-female", RatSex.Female,
                100f, RatStage.Adult);
            string reason;

            Assert.IsTrue(EnclosureSystem.TryAssignToForSale(save, female, now, 2, out reason), reason);
            Assert.AreEqual(RatEnclosure.ForSale, female.enclosure);
            Assert.IsTrue(female.hasPreviousSaleTank);
            Assert.AreEqual(RatEnclosure.FemaleColony, female.previousSaleTank);

            RatEnclosure returnedTo;
            Assert.IsTrue(EnclosureSystem.TryReturnFromForSale(save, female, 10,
                out returnedTo, out reason), reason);
            Assert.AreEqual(RatEnclosure.FemaleColony, returnedTo);
            Assert.AreEqual(RatEnclosure.FemaleColony, female.enclosure);
            Assert.IsFalse(female.pairingHabitatAssigned);
            Assert.IsFalse(female.hasPreviousSaleTank,
                "Returning completes the pending sale-tank round trip; the next entry records a fresh origin.");
            Assert.AreEqual(1, save.rats.Count,
                "Returning changes tank assignment only and cannot duplicate or delete the rat.");
        }

        [Test]
        public void SaleTankBulkMoveRecordsAndRestoresEachPreviousTank()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData male = CreateSaleTestRat(save, "sale-return-bulk-male", RatSex.Male,
                100f, RatStage.Adult);
            RatData female = CreateSaleTestRat(save, "sale-return-bulk-female", RatSex.Female,
                100f, RatStage.Adult);

            int moved;
            string reason;
            Assert.IsTrue(EnclosureSystem.TryAssignAllSellableToForSale(save, now, 2,
                out moved, out reason), reason);
            Assert.AreEqual(2, moved);
            Assert.IsTrue(male.hasPreviousSaleTank);
            Assert.IsTrue(female.hasPreviousSaleTank);
            Assert.AreEqual(RatEnclosure.MaleColony, male.previousSaleTank);
            Assert.AreEqual(RatEnclosure.FemaleColony, female.previousSaleTank);

            RatEnclosure destination;
            Assert.IsTrue(EnclosureSystem.TryReturnFromForSale(save, male, 10,
                out destination, out reason), reason);
            Assert.AreEqual(RatEnclosure.MaleColony, destination);
            Assert.IsTrue(EnclosureSystem.TryReturnFromForSale(save, female, 10,
                out destination, out reason), reason);
            Assert.AreEqual(RatEnclosure.FemaleColony, destination);
            Assert.AreEqual(2, save.rats.Count);
        }

        [Test]
        public void SaleTankReturnWaitsWhenPreviousPairingTankIsFull()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData returning = CreateSaleTestRat(save, "sale-return-pairing", RatSex.Female,
                100f, RatStage.Adult);
            returning.enclosure = RatEnclosure.ForSale;
            returning.hasPreviousSaleTank = true;
            returning.previousSaleTank = RatEnclosure.Pairing;
            for (int index = 0; index < 10; index++)
            {
                RatData occupant = CreateSaleTestRat(save, "pairing-return-full-" + index,
                    RatSex.Male, 100f, RatStage.Adult);
                occupant.enclosure = RatEnclosure.Pairing;
            }

            RatEnclosure destination;
            string reason;
            Assert.IsFalse(EnclosureSystem.TryReturnFromForSale(save, returning, 10,
                out destination, out reason));
            StringAssert.Contains("Pairing Tank is full", reason);
            Assert.AreEqual(RatEnclosure.ForSale, returning.enclosure);
            Assert.IsTrue(returning.hasPreviousSaleTank,
                "A blocked return keeps the destination so retrying after capacity changes remains possible.");
            Assert.AreEqual(11, save.rats.Count);
        }

        [Test]
        public void SaleTankPreviousDestinationPersistsThroughSaveLoadAndLegacyFallbackIsSafe()
        {
            long now = GameConfig.StartGameTimeMs + 100L * GameConfig.GameDayMs;
            ColonySaveData save = CreateEmptySaleTestSave(now);
            RatData female = CreateSaleTestRat(save, "sale-return-save-female", RatSex.Female,
                100f, RatStage.Adult);
            female.enclosure = RatEnclosure.Pairing;
            female.pairingHabitatAssigned = true;
            string reason;
            Assert.IsTrue(EnclosureSystem.TryAssignToForSale(save, female, now, 2, out reason), reason);

            ColonySaveData restored = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData restoredFemale = BreedingSystem.FindRat(restored, female.id);
            Assert.IsNotNull(restoredFemale);
            Assert.IsTrue(restoredFemale.hasPreviousSaleTank);
            Assert.AreEqual(RatEnclosure.Pairing, restoredFemale.previousSaleTank);
            RatEnclosure destination;
            Assert.IsTrue(EnclosureSystem.TryReturnFromForSale(restored, restoredFemale, 10,
                out destination, out reason), reason);
            Assert.AreEqual(RatEnclosure.Pairing, destination);
            Assert.IsTrue(restoredFemale.pairingHabitatAssigned);

            RatData legacyMale = CreateSaleTestRat(restored, "sale-return-legacy-male", RatSex.Male,
                100f, RatStage.Adult);
            legacyMale.enclosure = RatEnclosure.ForSale;
            legacyMale.hasPreviousSaleTank = false;
            Assert.IsTrue(EnclosureSystem.MigrateMissingSaleReturnTanks(restored));
            Assert.IsTrue(legacyMale.hasPreviousSaleTank);
            Assert.AreEqual(RatEnclosure.MaleColony, legacyMale.previousSaleTank,
                "Legacy Sale Tank residents use their sex-appropriate colony tank rather than guessing Pairing.");
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
            PairingHabitatSystem.ResetDiagnostics();
            Assert.IsFalse(EnclosureSystem.TryPrepareForSaleBirth(fullSave, fullMother, blockedPregnancy,
                UpgradeSystem.PairingHabitatCapacity(fullSave),
                out movedFamily, out reason));
            StringAssert.Contains("12/10 Pairing Tank spaces", reason);
            StringAssert.Contains("Pairing Tank Capacity", reason);
            StringAssert.Contains("Move All Out of Pairing Tank", reason);
            StringAssert.Contains("retries automatically", reason);
            Assert.AreEqual(EventLogPolicy.Birth,
                EventLogPolicy.CategoryForMessage("Birth delayed — " + reason),
                "A capacity-blocked birth must become a player-visible Birth event.");
            Assert.AreEqual("pending", blockedPregnancy.status);
            Assert.AreEqual(RatEnclosure.ForSale, fullMother.enclosure);
            Assert.AreEqual(8, fullSave.rats.FindAll(rat => rat.enclosure == RatEnclosure.Pairing).Count,
                "A blocked transfer must not partially move the mother or litter into a full habitat.");
            Assert.AreEqual(originalRatCount, fullSave.rats.Count);
            Assert.AreEqual(1, PairingHabitatSystem.Diagnostics.capacityBlockedMoves,
                "Capacity-blocked family transfers should be visible in pairing diagnostics.");
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

        private static PregnancyData AddActiveSaleTestPregnancy(
            ColonySaveData save, RatData mother, RatData father, long gameTime)
        {
            var pregnancy = new PregnancyData
            {
                id = "pregnancy-" + mother.id,
                motherId = mother.id,
                fatherId = father == null ? "historical-father" : father.id,
                startedAt = gameTime,
                dueAt = gameTime + GameConfig.PregnancyMs,
                gestationDurationMs = GameConfig.PregnancyMs,
                status = "pending",
                expectedLitterSize = 3,
            };
            save.pregnancies.Add(pregnancy);
            mother.pregnancyId = pregnancy.id;
            mother.reproductiveState = ReproductiveState.Pregnant;
            BreedingSystem.InvalidateReproductiveStateIndexes(save);
            return pregnancy;
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
            Assert.AreEqual(litter.id, mother.nursingLitterId);
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
            Assert.IsTrue(string.IsNullOrEmpty(mother.nursingLitterId));
            Assert.AreEqual(RatEnclosure.FemaleColony, mother.enclosure);
            foreach (var pupId in litter.pupIds)
            {
                RatData pup = BreedingSystem.FindRat(save, pupId);
                Assert.AreEqual(RatEnclosure.FemaleColony, pup.enclosure);
            }
        }

        [Test]
        public void MultipleNursingMothersCanShareTheNestBesideTheirOwnPinkieClusters()
        {
            RatEnclosure tank = RatEnclosure.Pairing;
            Vector3 nest = EnclosureSystem.GetNestPosition(tank);
            string[] litterIds = { "family-litter-alpha", "family-litter-beta" };
            float[] angles = { 0f, Mathf.PI };

            for (int index = 0; index < litterIds.Length; index++)
            {
                Vector3 familyCenter = EnclosureSystem.GetNestLitterCenter(tank, litterIds[index]);
                Vector3 motherTarget = EnclosureSystem.GetNestFamilyCaregiverPosition(
                    tank, litterIds[index], angles[index], 0.42f);
                Vector3 pinkiePosition = EnclosureSystem.GetPinkiePosition(
                    tank, "pinkie-" + index, litterIds[index], 0, nest);

                Assert.IsTrue(EnclosureSystem.IsInsideNestCaregiverZone(tank, motherTarget),
                    "Each nursing mother must stay inside the shared nest caregiver area.");
                Assert.Less(Vector3.Distance(motherTarget, pinkiePosition), 1.05f,
                    "Each mother target remains near her own litter's pinkie cluster.");
                Assert.Less(Vector3.Distance(familyCenter, nest), 0.25f,
                    "All litter clusters remain together in the same shared nest.");
            }

            Assert.AreNotEqual(
                EnclosureSystem.GetNestFamilyCaregiverPosition(tank, litterIds[0], angles[0], 0.42f),
                EnclosureSystem.GetNestFamilyCaregiverPosition(tank, litterIds[1], angles[1], 0.42f),
                "Different mothers receive individual presentation targets; neither owns the nest globally.");
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

        [Test]
        public void MarkingPlacementsAreStablePerRatAndVaryWithoutMarkingSolidRats()
        {
            var factoryHost = new GameObject("Marking Placement Variation Factory");
            var visualParent = new GameObject("Marking Placement Variation Parent").transform;
            var factory = factoryHost.AddComponent<RatVisualFactory>();
            factory.handPaintedRatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/HandPaintedRat/HandPaintedRat.prefab");
            Assert.IsNotNull(factory.handPaintedRatPrefab);

            try
            {
                GenotypeData genotype = GeneticsSystem.CreateFounder(
                    "B", "B", "C", "C", "D", "D", "S", "S");
                RatData firstRat = CreateMarkingVariationTestRat("mark-variation-a", "Blaze", genotype);
                RatData sameRat = CreateMarkingVariationTestRat("mark-variation-a", "Blaze", genotype.Clone());
                RatData otherRat = CreateMarkingVariationTestRat("mark-variation-b", "Blaze", genotype.Clone());
                RatData solidRat = CreateMarkingVariationTestRat("mark-variation-solid", "Self", genotype.Clone());

                GameObject firstVisual = factory.CreateStageVisual(visualParent, firstRat);
                GameObject sameVisual = factory.CreateStageVisual(visualParent, sameRat);
                GameObject otherVisual = factory.CreateStageVisual(visualParent, otherRat);
                GameObject solidVisual = factory.CreateStageVisual(visualParent, solidRat);
                try
                {
                    Material first = firstVisual.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials[0];
                    Material same = sameVisual.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials[0];
                    Material other = otherVisual.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials[0];
                    Material solid = solidVisual.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials[0];
                    Assert.IsTrue(first.HasProperty("_HotspotNoiseOffset"));
                    Assert.IsTrue(first.HasProperty("_SpeckleSettings"));
                    Assert.IsTrue(first.HasProperty("_SpeckleSeed"));
                    CollectionAssert.AreEqual(first.GetVectorArray("_HotspotCenters"),
                        same.GetVectorArray("_HotspotCenters"),
                        "The same ID/genotype must reconstruct identical anatomical probabilities.");
                    CollectionAssert.AreEqual(first.GetVectorArray("_HotspotRadii"),
                        same.GetVectorArray("_HotspotRadii"));
                    Assert.AreNotEqual(first.GetVector("_HotspotNoiseOffset"),
                        other.GetVector("_HotspotNoiseOffset"),
                        "Distinct IDs must receive different organic fields.");
                    Assert.AreSame(first.GetTexture("_HotspotNoise"), same.GetTexture("_HotspotNoise"),
                        "Noise must be shared, not regenerated per visual.");
                    Assert.AreEqual(first.GetVector("_SpeckleSettings"),
                        same.GetVector("_SpeckleSettings"),
                        "Speckle activation, density, spacing, and size must be stable after reload.");
                    Assert.AreEqual(first.GetFloat("_SpeckleSeed"),
                        same.GetFloat("_SpeckleSeed"), 0.000001f);
                    Assert.AreEqual(1f, other.GetVector("_SpeckleSettings").x, 0.001f,
                        "This deterministic marked-rat fixture exercises the enabled speckle path.");
                    Assert.AreEqual(1f, first.GetFloat("_SpotStrength"), 0.001f);
                    Assert.AreEqual(0f, solid.GetFloat("_SpotStrength"), 0.001f);
                    Assert.AreEqual(0f, solid.GetVector("_SpeckleSettings").x, 0.001f,
                        "Self/solid rats must not receive visual speckles.");
                    foreach (var hotspot in solid.GetVectorArray("_HotspotCenters"))
                        Assert.AreEqual(0f,hotspot.w,"Self/solid must remain unmarked everywhere.");
                }
                finally
                {
                    Object.DestroyImmediate(firstVisual);
                    Object.DestroyImmediate(sameVisual);
                    Object.DestroyImmediate(otherVisual);
                    Object.DestroyImmediate(solidVisual);
                }
            }
            finally
            {
                Object.DestroyImmediate(visualParent.gameObject);
                Object.DestroyImmediate(factoryHost);
            }
        }

        [Test]
        public void HairlessSpontaneousRateIsHalfPercentAndIndependentOfCoatMutations()
        {
            var normal = new LocusData("Hr","Hr","Hr");
            int visible = 0;
            for (int i=0;i<10000;i++)
            {
                var history = new List<MutationRecordData>();
                var result = GeneticsSystem.InheritHairless(normal,normal,.1f,.9f,
                    (i+.5f)/10000f,123L,history);
                if (result.firstAllele == "hr" && result.secondAllele == "hr")
                {
                    visible++;
                    Assert.AreEqual(1,history.Count);
                    Assert.AreEqual("Hr",history[0].locus);
                    Assert.AreEqual("spontaneous",history[0].parentRole);
                    Assert.AreEqual(123L,history[0].recordedAt);
                }
                else Assert.IsEmpty(history);
            }
            Assert.AreEqual(50,visible,"0.5% is visible pups, not a per-allele chance.");
            Assert.AreEqual(.005f,GeneticsSystem.MutationRateForLocus("S"));
            Assert.AreEqual(.0025f,GeneticsSystem.MutationRateForLocus("B"));
            var randomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(11982);
                int inheritedVisible = 0;
                var founder = GeneticsSystem.CreateFounder("B","B","C","C","D","D","s","s");
                for (int i=0;i<10000;i++)
                    if (GeneticsSystem.IsHairless(GeneticsSystem.InheritGenotype(founder,founder,1L)))
                        inheritedVisible++;
                Assert.That(inheritedVisible,Is.InRange(25,80),"Seeded production inheritance path.");
            }
            finally { UnityEngine.Random.state = randomState; }
        }

        [TestCase("Hr","Hr","Hr","hr",0)]
        [TestCase("Hr","hr","Hr","hr",1)]
        [TestCase("Hr","hr","hr","hr",2)]
        [TestCase("hr","hr","hr","hr",4)]
        [TestCase("Hr","Hr","hr","hr",0)]
        public void HairlessCarrierAndExpressingInheritanceIsMendelian(
            string m1,string m2,string f1,string f2,int expectedOfFour)
        {
            int visible=0;
            for (int m=0;m<2;m++) for (int f=0;f<2;f++)
            {
                var history = new List<MutationRecordData>();
                var gene = GeneticsSystem.InheritHairless(new LocusData("Hr",m1,m2),
                    new LocusData("Hr",f1,f2),m*.5f+.1f,f*.5f+.1f,0f,1L,history);
                if (gene.firstAllele=="hr" && gene.secondAllele=="hr") visible++;
                Assert.IsEmpty(history,"Inherited expression is not a spontaneous mutation.");
            }
            Assert.AreEqual(expectedOfFour,visible);
        }

        [Test]
        public void HairlessSaveMigrationPreservesRatsColorsAndGenetics()
        {
            var save = CreateEmptySaleTestSave(GameConfig.StartGameTimeMs);
            RatData rat = CreateSaleTestRat(save,"hairless-save",RatSex.Female,100f,RatStage.Adult);
            rat.genotype.hairless = new LocusData("Hr","hr","hr");
            rat.markingFamily="Blaze"; rat.markingColorHex="#a8673f";
            rat.secondaryMarkingFamily="Berkshire"; rat.secondaryMarkingColorHex="#eee4ce";
            var restored = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData loaded = restored.rats.Find(item=>item.id==rat.id);
            Assert.IsNotNull(loaded);
            Assert.IsTrue(loaded.phenotype.hairless);
            StringAssert.Contains("Hairless",loaded.phenotype.coatColorLabel);
            Assert.AreEqual(rat.markingColorHex,loaded.markingColorHex);
            Assert.AreEqual(rat.secondaryMarkingColorHex,loaded.secondaryMarkingColorHex);
            Assert.IsTrue(GeneticsSystem.IsHairless(loaded.genotype.Clone()));
            // Real pre-feature JSON has no optional field. Remove it explicitly
            // rather than relying on a constructor default masking migration.
            string legacy = JsonUtility.ToJson(save,false).Replace(
                "\"hairless\":{\"locus\":\"Hr\",\"firstAllele\":\"hr\",\"secondAllele\":\"hr\"},","");
            Assert.IsFalse(legacy.Contains("\"locus\":\"Hr\""));
            var migrated = SaveSystem.FromJson(legacy);
            Assert.AreEqual(restored.rats.Count,migrated.rats.Count);
            RatData old = migrated.rats.Find(item=>item.id==rat.id);
            Assert.IsFalse(GeneticsSystem.IsHairless(old.genotype));
            Assert.AreEqual("Hr/Hr",GeneticsSystem.FormatPair(old.genotype,"Hr"));
            Assert.AreEqual(loaded.coatColorVariant,old.coatColorVariant);
            Assert.AreEqual(loaded.markingFamily,old.markingFamily);
        }

        [Test]
        public void HotspotSeedsAreCachedAsymmetricAndStableAcrossRenameAndReload()
        {
            var genotype = GeneticsSystem.CreateFounder("B","B","C","C","D","D","S","S");
            var first = CreateMarkingVariationTestRat("hotspot-a","Berkshire",genotype);
            var pattern = RatVisualFactory.GetHotspotPattern(first);
            first.name="Completely different name";
            var reloaded = JsonUtility.FromJson<RatData>(JsonUtility.ToJson(first));
            Assert.AreSame(pattern,RatVisualFactory.GetHotspotPattern(reloaded));
            Assert.AreNotEqual(pattern.centers[6].w,pattern.centers[7].w,"Independent cheeks.");
            Assert.AreNotEqual(pattern.radii[8],pattern.radii[9],"Independent leg coverage.");
            var signatures = new HashSet<Vector4>();
            for (int i=0;i<300;i++)
            {
                var rat = CreateMarkingVariationTestRat("hotspot-stress-"+i,"Berkshire",genotype.Clone());
                var sample = RatVisualFactory.GetHotspotPattern(rat);
                signatures.Add(sample.centers[3]);
                Assert.AreSame(sample,RatVisualFactory.GetHotspotPattern(rat));
                RatVisualFactory.ReleaseTransientPreviewPatternsForRat(rat);
            }
            Assert.AreEqual(300,signatures.Count);
            first.markingFamily="Self";
            foreach (var center in RatVisualFactory.GetHotspotPattern(first).centers)
                Assert.AreEqual(0f,center.w);
        }

        [Test]
        public void HotspotCoatShaderCompilesAndRendersDistinctPhenotypeColors()
        {
            // Material property assertions alone missed the all-white regression:
            // require the actual surface shader passes and GPU output to work.
            var pixels = RatHabitat.Editor.RatCoatShaderValidation.ValidateColors();
            Assert.Less(pixels[0].grayscale, pixels[3].grayscale * .6f,
                "A solid dark rat must not render white like an albino.");
            Assert.Greater(pixels[1].b, pixels[1].r * 1.6f,
                "Blue phenotype pigment must reach the rendered surface.");
            Assert.Greater(pixels[2].r, pixels[2].b * 1.4f,
                "Warm phenotype pigment must reach the rendered surface.");
        }

        [Test]
        public void HotspotRestCoordinatesAndHairlessUseSharedAgeAppropriateMesh()
        {
            var host = new GameObject("Hotspot visual regression");
            var factory=host.AddComponent<RatVisualFactory>();
            factory.handPaintedRatPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/HandPaintedRat/HandPaintedRat.prefab");
            try
            {
                var genotype=GeneticsSystem.CreateFounder("B","B","C","C","D","D","S","S");
                var furred=CreateMarkingVariationTestRat("rest-furred","Blaze",genotype);
                var bald=CreateMarkingVariationTestRat("rest-bald","Blaze",genotype.Clone());
                bald.genotype.hairless=new LocusData("Hr","hr","hr");
                bald.phenotype=GeneticsSystem.DerivePhenotype(RatStage.Adult,bald.genotype,
                    bald.coatColorVariant,bald.coatTone);
                var normalVisual=factory.CreateStageVisual(host.transform,furred);
                var baldVisual=factory.CreateStageVisual(host.transform,bald);
                var normalRenderer=normalVisual.GetComponentInChildren<SkinnedMeshRenderer>();
                var baldRenderer=baldVisual.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.AreSame(normalRenderer.sharedMesh,baldRenderer.sharedMesh,
                    "Hairless adults must use the adult model, not an enlarged pinkie.");
                Assert.AreEqual(normalVisual.GetComponentsInChildren<Renderer>().Length,
                    baldVisual.GetComponentsInChildren<Renderer>().Length);
                var coords=new List<Vector4>(); normalRenderer.sharedMesh.GetUVs(2,coords);
                Assert.AreEqual(normalRenderer.sharedMesh.vertexCount,coords.Count);
                float minX=float.MaxValue,maxX=float.MinValue;
                foreach (var coord in coords) { minX=Mathf.Min(minX,coord.x); maxX=Mathf.Max(maxX,coord.x); }
                Assert.Greater(maxX-minX,.95f,
                    "Rest coordinates retain distinct left/right positions despite shared UV0.");
                var material=baldRenderer.sharedMaterials[0];
                Assert.AreEqual(1f,material.GetFloat("_HairlessMode"));
                Assert.Less(material.GetColor("_SkinColor").grayscale,.8f);
                Assert.IsFalse(ShaderUtil.ShaderHasError(material.shader));
                factory.ApplyPhenotype(baldVisual,bald);
                CollectionAssert.AreEqual(RatVisualFactory.GetHotspotPattern(bald).centers,
                    material.GetVectorArray("_HotspotCenters"));
                Assert.AreSame(material.GetTexture("_HotspotNoise"),
                    normalRenderer.sharedMaterials[0].GetTexture("_HotspotNoise"));
                bald.stage=RatStage.YoungRat;
                bald.phenotype=GeneticsSystem.DerivePhenotype(bald.stage,bald.genotype);
                var young=factory.CreateStageVisual(host.transform,bald);
                Assert.AreSame(normalRenderer.sharedMesh,young.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void SecondaryMarkingInheritanceIsRareDeterministicAndUsesBothParents()
        {
            GenotypeData markedGenotype = GeneticsSystem.CreateFounder(
                "B", "B", "C", "C", "D", "D", "S", "S");
            RatData mother = CreateMarkedColorInheritanceParent(
                "two-color-mother", "Blaze", "#181818");
            RatData father = CreateMarkedColorInheritanceParent(
                "two-color-father", "Merle", "#eeeeee");
            string motherColor = RatVisualFactory.ResolveMarkingColorHex(mother);
            string fatherColor = RatVisualFactory.ResolveMarkingColorHex(father);
            Assert.AreNotEqual(motherColor, fatherColor,
                "The test parents must contribute visibly distinct marking colors.");

            bool foundMulticolorPup = false;
            for (int index = 0; index < 1000; index++)
            {
                string pupId = "two-color-pup-" + index;
                string primaryColor;
                string secondaryFamily;
                string secondaryColor;
                bool inherited = GeneticsSystem.TryInheritSecondaryMarking(
                    mother, father, markedGenotype, "Blaze", "two-color-litter",
                    pupId, out primaryColor, out secondaryFamily, out secondaryColor);
                if (!inherited)
                {
                    Assert.IsNull(primaryColor);
                    Assert.IsNull(secondaryFamily);
                    Assert.IsNull(secondaryColor);
                    continue;
                }

                Assert.AreEqual(motherColor, primaryColor,
                    "With the maternal family selected as primary, the primary hue comes from the mother.");
                Assert.AreEqual("Merle", secondaryFamily,
                    "The secondary marking family must come from the other parent.");
                Assert.AreEqual(fatherColor, secondaryColor,
                    "The secondary hue must be inherited from the father, not independently randomized.");
                Assert.AreNotEqual(primaryColor, secondaryColor);

                string repeatedPrimary;
                string repeatedFamily;
                string repeatedSecondary;
                Assert.IsTrue(GeneticsSystem.TryInheritSecondaryMarking(
                    mother, father, markedGenotype, "Blaze", "two-color-litter", pupId,
                    out repeatedPrimary, out repeatedFamily, out repeatedSecondary));
                Assert.AreEqual(primaryColor, repeatedPrimary);
                Assert.AreEqual(secondaryFamily, repeatedFamily);
                Assert.AreEqual(secondaryColor, repeatedSecondary,
                    "The same parent/litter/pup/genotype key must never reroll.");
                foundMulticolorPup = true;
                break;
            }

            Assert.IsTrue(foundMulticolorPup);

            int cohortMulticolorCount = 0;
            const int cohortSize = 1000;
            for (int index = 0; index < cohortSize; index++)
            {
                string primaryColor;
                string secondaryFamily;
                string secondaryColor;
                if (GeneticsSystem.TryInheritSecondaryMarking(
                    mother, father, markedGenotype, "Blaze", "two-color-rate-litter",
                    "rate-pup-" + index, out primaryColor, out secondaryFamily, out secondaryColor))
                    cohortMulticolorCount++;
            }
            Assert.That(cohortMulticolorCount, Is.InRange(55, 105),
                "Most offspring should retain the normal single-color appearance; the rare second layer targets 8%.");

            string ignoredPrimary;
            string ignoredFamily;
            string ignoredColor;
            Assert.IsFalse(GeneticsSystem.TryInheritSecondaryMarking(
                mother, father, markedGenotype, "Self", "solid-child-litter", "solid-child",
                out ignoredPrimary, out ignoredFamily, out ignoredColor),
                "A solid/self offspring has no primary markings to split into a second color.");
        }

        [Test]
        public void SecondaryMarkingSaveLoadAndVisualRefreshPreserveBothColors()
        {
            ColonySaveData save = ColonyFactory.CreateNew(1000000L);
            RatData rat = ColonyFactory.CreateRat(
                "saved-two-color-rat", "Two Color", RatSex.Female,
                save.clock.gameTimeMs - 100L * GameConfig.GameDayMs, 1,
                GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "S", "S"),
                new TraitData(50f, 50f, 50f), RatStage.Adult);
            rat.ageDays = 100f;
            rat.markingFamily = "Blaze";
            rat.markingColorHex = "#e8d5ad";
            rat.secondaryMarkingFamily = "Merle";
            rat.secondaryMarkingColorHex = "#263746";
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            save.rats.Add(rat);
            save.ratIds.Add(rat.id);

            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData loadedRat = BreedingSystem.FindRat(loaded, rat.id);
            Assert.IsNotNull(loadedRat);
            Assert.AreEqual(rat.markingColorHex, loadedRat.markingColorHex);
            Assert.AreEqual(rat.secondaryMarkingFamily, loadedRat.secondaryMarkingFamily);
            Assert.AreEqual(rat.secondaryMarkingColorHex, loadedRat.secondaryMarkingColorHex);

            RatData legacyRat = JsonUtility.FromJson<RatData>(
                "{\"id\":\"legacy-marked-rat\",\"markingFamily\":\"Blaze\"}");
            Assert.IsNotNull(legacyRat);
            Assert.IsTrue(string.IsNullOrEmpty(legacyRat.markingColorHex));
            Assert.IsTrue(string.IsNullOrEmpty(legacyRat.secondaryMarkingFamily));
            Assert.IsTrue(string.IsNullOrEmpty(legacyRat.secondaryMarkingColorHex),
                "Old saves safely keep the existing derived single-color marking behavior.");
            Assert.IsNotEmpty(RatVisualFactory.ResolveMarkingColorHex(legacyRat),
                "A legacy rat without color overrides continues using its stable derived marking color.");

            var factoryHost = new GameObject("Secondary Marking Visual Test Factory");
            var visualParent = new GameObject("Secondary Marking Visual Test Parent").transform;
            var factory = factoryHost.AddComponent<RatVisualFactory>();
            factory.handPaintedRatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/HandPaintedRat/HandPaintedRat.prefab");
            Assert.IsNotNull(factory.handPaintedRatPrefab);
            GameObject visual = null;
            try
            {
                visual = factory.CreateStageVisual(visualParent, loadedRat);
                Assert.IsNotNull(visual);
                Material material = visual.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials[0];
                Assert.AreEqual(1f, material.GetFloat("_SecondarySpotStrength"), 0.001f);
                AssertColorHex(loadedRat.markingColorHex, material.GetColor("_SpotColor"));
                AssertColorHex(loadedRat.secondaryMarkingColorHex,
                    material.GetColor("_SecondarySpotColor"));
                factory.ApplyPhenotype(visual, loadedRat);
                material = visual.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials[0];
                Assert.AreEqual(1f, material.GetFloat("_SecondarySpotStrength"), 0.001f,
                    "A normal material refresh must retain the saved second marking layer.");
                AssertColorHex(loadedRat.markingColorHex, material.GetColor("_SpotColor"));
                AssertColorHex(loadedRat.secondaryMarkingColorHex,
                    material.GetColor("_SecondarySpotColor"));
            }
            finally
            {
                if (visual != null) Object.DestroyImmediate(visual);
                Object.DestroyImmediate(visualParent.gameObject);
                Object.DestroyImmediate(factoryHost);
            }
        }

        private static RatData CreateMarkedColorInheritanceParent(string id, string family, string coatHex)
        {
            var rat = new RatData
            {
                id = id,
                name = id,
                genotype = GeneticsSystem.CreateFounder("B", "B", "C", "C", "D", "D", "S", "S"),
                markingFamily = family,
                phenotype = new PhenotypeData
                {
                    furRevealed = true,
                    spotted = true,
                    coatColorHex = coatHex,
                    coatColorId = "brown",
                },
            };
            return rat;
        }

        private static void AssertColorHex(string expectedHex, Color actual)
        {
            Color expected;
            Assert.IsTrue(ColorUtility.TryParseHtmlString(expectedHex, out expected));
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.004f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.004f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.004f));
        }

        private static RatData CreateMarkingVariationTestRat(
            string id, string family, GenotypeData genotype)
        {
            RatData rat = ColonyFactory.CreateRat(id, id, RatSex.Female, 0L, 0,
                genotype, new TraitData(50f, 50f, 50f), RatStage.Adult);
            rat.coatColorVariant = "black";
            rat.coatTone = 1f;
            rat.phenotype = GeneticsSystem.DerivePhenotype(
                RatStage.Adult, rat.genotype, rat.coatColorVariant, rat.coatTone);
            rat.markingFamily = family;
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, family);
            return rat;
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
