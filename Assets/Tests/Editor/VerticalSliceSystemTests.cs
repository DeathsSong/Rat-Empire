#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
            var save = ColonyFactory.CreateNew(1000000L);
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
        public void GeneratedRatNamesUseFriendlyPoolsAndMigrateNumericSuffixes()
        {
            string maleName = ColonyFactory.GeneratedName("same-rat-id", RatSex.Male);
            string femaleName = ColonyFactory.GeneratedName("same-rat-id", RatSex.Female);
            Assert.IsFalse(char.IsDigit(maleName[maleName.Length - 1]));
            Assert.IsFalse(char.IsDigit(femaleName[femaleName.Length - 1]));
            Assert.AreEqual("Mabel", ColonyFactory.NormalizeDisplayName("Mabel 4"));
            Assert.AreEqual("Randy", ColonyFactory.NormalizeDisplayName("Randy 12"));
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

        [Test]
        public void EnclosuresFollowPregnancyBirthAndDependentPinkieGrowth()
        {
            var save = ColonyFactory.CreateNew(1000000L);
            RatData mother = save.rats[0];
            RatData father = save.rats[1];
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
