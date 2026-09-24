using System;
using UnityEngine;

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
                // Founders are intentionally beginner-level, with small
                // deterministic differences rather than identical stats.
                StarterTraits("rat_mabel"), RatStage.Adult);
            var otto = CreateRat(
                "rat_otto", "Otto", RatSex.Male, save.clock.gameTimeMs - 53L * GameConfig.GameDayMs, 0,
                GeneticsSystem.CreateFounder("b", "b", "C", "C", "D", "D", "S", "s"),
                StarterTraits("rat_otto"), RatStage.Adult);
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

        private static TraitData StarterTraits(string stableId)
        {
            // Deterministic pseudo-random beginner quality. These are absolute
            // stat values, not percentages: every founder value is in the
            // inclusive 0-15 beginner range while the pair remains distinct.
            unchecked
            {
                uint hash = 2166136261u;
                string key = stableId ?? string.Empty;
                for (int index = 0; index < key.Length; index++)
                    hash = (hash ^ key[index]) * 16777619u;
                return new TraitData(
                    hash % 16u,
                    (hash >> 3) % 16u,
                    (hash >> 6) % 16u);
            }
        }

        public static bool MigrateLegacyStarterStats(ColonySaveData save)
        {
            if (save == null) return false;
            bool changed = false;
            changed |= NormalizeDisplayNames(save);
            if (save.rats != null)
            {
                foreach (var rat in save.rats)
                {
                    if (!IsBeginnerFounder(rat)) continue;

                    TraitData starter = StarterTraits(rat.id);
                    if (rat.traits == null) rat.traits = new TraitData();
                    if (!Mathf.Approximately(rat.traits.size, starter.size) ||
                        !Mathf.Approximately(rat.traits.health, starter.health) ||
                        !Mathf.Approximately(rat.traits.fertility, starter.fertility) ||
                        !Mathf.Approximately(rat.baseHealth, starter.health) ||
                        !Mathf.Approximately(rat.baseFertility, starter.fertility))
                    {
                        rat.traits.size = starter.size;
                        rat.traits.health = starter.health;
                        rat.traits.fertility = starter.fertility;
                        rat.baseHealth = starter.health;
                        rat.baseFertility = starter.fertility;
                        changed = true;
                    }
                }
            }

            if (save.schemaVersion < GameConfig.SaveVersion)
            {
                save.schemaVersion = GameConfig.SaveVersion;
                changed = true;
            }
            return changed;
        }

        public static bool IsBeginnerFounder(RatData rat)
        {
            return rat != null && rat.generation == 0 &&
                rat.removalDisposition == RatRemovalDisposition.None &&
                string.IsNullOrEmpty(rat.motherId) && string.IsNullOrEmpty(rat.fatherId) &&
                (rat.id == "rat_mabel" || rat.id == "rat_otto");
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
                name = string.IsNullOrWhiteSpace(name) ? GeneratedName(id, sex) : NormalizeDisplayName(name),
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

        /// <summary>
        /// Returns a stable friendly name from the sex-specific pool. The
        /// stable ID is used only to select a name; it is never displayed.
        /// </summary>
        public static string GeneratedName(string id, RatSex sex)
        {
            string[] pool = sex == RatSex.Female ? GameConfig.FemaleRatNames : GameConfig.MaleRatNames;
            if (pool == null || pool.Length == 0) return sex == RatSex.Female ? "Mabel" : "Otto";
            return pool[StableHash(id) % pool.Length];
        }

        /// <summary>
        /// Migrates old generated names such as "Mabel 4" while preserving
        /// arbitrary names and every rat's internal ID.
        /// </summary>
        public static string NormalizeDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string result = value.Trim();
            int end = result.Length;
            int digitStart = end;
            while (digitStart > 0 && char.IsDigit(result[digitStart - 1])) digitStart--;
            if (digitStart < end && digitStart > 0 && char.IsWhiteSpace(result[digitStart - 1]))
                result = result.Substring(0, digitStart).TrimEnd();
            return result;
        }

        public static bool NormalizeDisplayNames(ColonySaveData save)
        {
            if (save == null) return false;
            save.EnsureLists();
            bool changed = false;
            foreach (var rat in save.rats)
                changed |= NormalizeRatName(rat);
            foreach (var rat in save.retiredRats)
                changed |= NormalizeRatName(rat);
            foreach (var listing in save.storeRatListings)
            {
                if (listing == null) continue;
                string normalized = NormalizeDisplayName(listing.name);
                if (normalized != listing.name)
                {
                    listing.name = normalized;
                    changed = true;
                }
            }
            return changed;
        }

        private static bool NormalizeRatName(RatData rat)
        {
            if (rat == null) return false;
            string normalized = NormalizeDisplayName(rat.name);
            if (normalized == rat.name) return false;
            rat.name = normalized;
            return true;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string key = value ?? string.Empty;
                for (int i = 0; i < key.Length; i++) hash = (hash ^ key[i]) * 16777619u;
                return (int)(hash & 0x7fffffff);
            }
        }

        public static string NewId(string prefix)
        {
            return prefix + "_" + Guid.NewGuid().ToString("N");
        }
    }
}
