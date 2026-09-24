using System;

namespace RatHabitat
{
    public static class ColonyFactory
    {
        public static ColonySaveData CreateNew(long now)
        {
            var save = new ColonySaveData
            {
                schemaVersion = GameConfig.SaveVersion,
                colonyName = "New Generation",
                createdAt = now,
                updatedAt = now,
                colonyCredits = GameConfig.StartingColonyCredits,
                currencyInitialized = true,
                storeInventoryInitialized = false,
                pairingNextCheckRealTimestamp = now + GameConfig.PairingCheckIntervalMs,
                pairingNextCheckGameTime = GameConfig.StartGameTimeMs + GameConfig.PairingCheckIntervalMs,
                clock = new ClockData
                {
                    gameTimeMs = GameConfig.StartGameTimeMs,
                    lastRealTimestamp = now,
                    gameStartTimestamp = now,
                    speed = 1f,
                },
            };

            var mabel = CreateRat(
                "rat_mabel", "Mabel", RatSex.Female, save.clock.gameTimeMs - 48L * GameConfig.GameDayMs, 0,
                GeneticsSystem.CreateFounder("B", "b", "C", "C", "D", "D", "s", "s"),
                new TraitData(48f, 82f, 76f), RatStage.Adult);
            var otto = CreateRat(
                "rat_otto", "Otto", RatSex.Male, save.clock.gameTimeMs - 53L * GameConfig.GameDayMs, 0,
                GeneticsSystem.CreateFounder("b", "b", "C", "C", "D", "D", "S", "s"),
                new TraitData(56f, 78f, 69f), RatStage.Adult);
            save.rats.Add(mabel);
            save.rats.Add(otto);
            save.ratIds.Add(mabel.id);
            save.ratIds.Add(otto.id);

            save.habitatObjects.Add(new HabitatObjectData { id = "object_food", type = HabitatObjectType.Food, label = "Food dish", condition = 86f, lastServicedAt = save.clock.gameTimeMs });
            save.habitatObjects.Add(new HabitatObjectData { id = "object_water", type = HabitatObjectType.Water, label = "Water bottle", condition = 94f, lastServicedAt = save.clock.gameTimeMs });
            save.habitatObjects.Add(new HabitatObjectData { id = "object_nest", type = HabitatObjectType.Nest, label = "Nest corner", condition = 100f, lastServicedAt = save.clock.gameTimeMs });
            save.habitatObjects.Add(new HabitatObjectData { id = "object_hide", type = HabitatObjectType.Hide, label = "Wooden hide", condition = 100f, lastServicedAt = save.clock.gameTimeMs });
            save.habitatObjects.Add(new HabitatObjectData { id = "object_exercise_wheel", type = HabitatObjectType.ExerciseWheel, label = "Exercise wheel", condition = 100f, lastServicedAt = save.clock.gameTimeMs });
            EnclosureSystem.RecalculateAssignments(save);
            StoreSystem.EnsureStoreState(save);
            LitterNameSystem.EnsureLitterNames(save);
            return save;
        }

        public static void EnsureDefaultHabitatObjects(ColonySaveData save)
        {
            if (save == null) return;
            save.EnsureLists();
            EnsureHabitatObject(save, "object_food", HabitatObjectType.Food, "Food dish");
            EnsureHabitatObject(save, "object_water", HabitatObjectType.Water, "Water bottle");
            EnsureHabitatObject(save, "object_nest", HabitatObjectType.Nest, "Nest corner");
            EnsureHabitatObject(save, "object_hide", HabitatObjectType.Hide, "Wooden hide");
            EnsureHabitatObject(save, "object_exercise_wheel", HabitatObjectType.ExerciseWheel, "Exercise wheel");
        }

        private static void EnsureHabitatObject(ColonySaveData save, string id, HabitatObjectType type, string label)
        {
            foreach (var item in save.habitatObjects)
            {
                if (item != null && item.id == id) return;
            }
            save.habitatObjects.Add(new HabitatObjectData
            {
                id = id,
                type = type,
                label = label,
                condition = 100f,
                lastServicedAt = save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs,
            });
        }

        public static RatData CreateRat(string id, string name, RatSex sex, long birthTimestamp, int generation, GenotypeData genotype, TraitData traits, RatStage stageOverride)
        {
            var rat = new RatData
            {
                id = id,
                name = name,
                sex = sex,
                stage = stageOverride,
                generation = generation,
                birthTimestamp = birthTimestamp,
                growthTimestamp = birthTimestamp,
                traits = traits ?? new TraitData(),
                genotype = genotype ?? new GenotypeData(),
                motherId = null,
                fatherId = null,
                litterId = null,
                pregnancyId = null,
                breedingCooldownUntil = 0,
                enclosure = stageOverride == RatStage.Pinkie
                    ? RatEnclosure.Nursery
                    : sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony,
                nursing = false,
            };
            GeneticsSystem.Normalize(rat.genotype);
            GeneticsSystem.EnsureCoatAppearance(rat);
            rat.phenotype = GeneticsSystem.DerivePhenotype(rat.stage, rat.genotype,
                rat.coatColorVariant, rat.coatTone);
            rat.markingFamily = GeneticsSystem.DefaultMarkingFamily(rat.genotype);
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            rat.ageDays = 0f;
            GrowthSystem.EnsureBiologyDefaults(rat);
            return rat;
        }

        public static string NewId(string prefix)
        {
            return prefix + "_" + Guid.NewGuid().ToString("N");
        }
    }
}
