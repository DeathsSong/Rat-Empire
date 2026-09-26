#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RatHabitat.Tests
{
    public class VerticalSliceSystemTests
    {
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
            rat.stage = RatStage.Mature;

            ColonySaveData loaded = SaveSystem.FromJson(SaveSystem.ToJson(save));
            RatData loadedRat = loaded.rats.Find(candidate => candidate.id == rat.id);
            Assert.IsNotNull(loadedRat);
            Assert.AreEqual(RatStage.Elderly, loadedRat.stage);
            Assert.IsFalse(StoreSystem.CanSellRat(loadedRat));
            Assert.AreEqual(0, StoreSystem.CalculateSaleValue(loadedRat));
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

            Assert.AreEqual(oneX * 2f, twoX, 0.00001f,
                "2x should advance moment-to-moment rat behavior twice as quickly as 1x.");
            Assert.AreEqual(oneX * 3f, threeX, 0.00001f,
                "3x should advance moment-to-moment rat behavior three times as quickly as 1x.");
        }

        [Test]
        public void WorldMovementStepProducesProportionalTravelTimes()
        {
            const float distance = 6f;
            const float baseWorldSpeed = 0.75f;
            const float realFrameSeconds = 0.037f;
            const int frameCount = 180;

            float[] travelled = new float[3];
            for (int speedIndex = 0; speedIndex < travelled.Length; speedIndex++)
            {
                float speed = speedIndex + 1f;
                GrowthSystem.SetRuntimeSpeed(speed);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    travelled[speedIndex] += GrowthSystem.SimulationMovementStep(
                        baseWorldSpeed, realFrameSeconds);
                }
            }
            GrowthSystem.SetRuntimeSpeed(1f);

            Assert.AreEqual(travelled[0] * 2f, travelled[1], 0.0001f,
                "2x world-space travel must cover twice the distance in the same real time.");
            Assert.AreEqual(travelled[0] * 3f, travelled[2], 0.0001f,
                "3x world-space travel must cover three times the distance in the same real time.");

            float oneXDuration = distance / baseWorldSpeed;
            float twoXDuration = distance / (baseWorldSpeed * 2f);
            float threeXDuration = distance / (baseWorldSpeed * 3f);
            Assert.AreEqual(oneXDuration * 0.5f, twoXDuration, 0.0001f);
            Assert.AreEqual(oneXDuration / 3f, threeXDuration, 0.0001f);
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
            for (int index = 0; index < originalListings.Count; index++)
            {
                StoreRatListingData current = save.storeRatListings[index];
                StoreRatListingData original = originalListings[index];
                Assert.AreEqual(original.traits.size, current.traits.size);
                Assert.AreEqual(original.traits.health, current.traits.health);
                Assert.AreEqual(original.traits.fertility, current.traits.fertility);
            }

            StoreSystem.RestockNow(save, save.clock.gameTimeMs);
            foreach (var listing in save.storeRatListings)
            {
                Assert.LessOrEqual(listing.traits.size, 20f);
                Assert.LessOrEqual(listing.traits.health, 20f);
                Assert.LessOrEqual(listing.traits.fertility, 20f);
            }
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
            Assert.AreEqual(RatEnclosure.Nursery, mother.enclosure);
            Assert.IsFalse(mother.nursing);
            Assert.AreEqual(RatEnclosure.MaleColony, father.enclosure);

            LitterData litter;
            Assert.IsTrue(BreedingSystem.FinishPregnancy(save, pregnancy.id, 2001000L, out litter, out reason), reason);
            EnclosureSystem.RecalculateAssignments(save);
            Assert.AreEqual(RatEnclosure.Nursery, mother.enclosure);
            Assert.IsTrue(mother.nursing);
            foreach (var pupId in litter.pupIds)
            {
                Assert.AreEqual(RatEnclosure.Nursery, BreedingSystem.FindRat(save, pupId).enclosure);
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
                Assert.AreEqual(pup.sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony, pup.enclosure);
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
                RatEnclosure.Nursery,
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
            Vector3 nurseryOpenPoint = EnclosureSystem.PointInEnclosure(RatEnclosure.Nursery, 3f, 4f);
            Assert.IsTrue(EnclosureSystem.IsBehaviorPointAllowed(RatEnclosure.FemaleColony, femalePoint));
            Assert.IsFalse(EnclosureSystem.IsBehaviorPointAllowed(RatEnclosure.Nursery, EnclosureSystem.NurseryNestPosition));
            Assert.IsTrue(EnclosureSystem.IsBehaviorPointAllowed(RatEnclosure.Nursery, nurseryOpenPoint));
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
