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

            // New-game founders use the same randomized name/genetics/coat/
            // marking pipeline as Store rats. The pair is generated once here
            // and then persisted with the save, so loading never rerolls it.
            RatData femaleStarter;
            RatData maleStarter;
            StoreSystem.CreateStarterPair(save.clock.gameTimeMs, StableSeed(now), out femaleStarter, out maleStarter);
            save.rats.Add(femaleStarter);
            save.rats.Add(maleStarter);
            save.ratIds.Add(femaleStarter.id);
            save.ratIds.Add(maleStarter.id);

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

        public static bool MigrateLegacyStarterStats(ColonySaveData save)
        {
            if (save == null) return false;
            bool changed = false;
            int capacityLevelBefore = save.colonyCapacityUpgradeLevel;
            int qualityLevelBefore = save.storeQualityUpgradeLevel;
            UpgradeSystem.EnsureState(save);
            changed |= capacityLevelBefore != save.colonyCapacityUpgradeLevel ||
                qualityLevelBefore != save.storeQualityUpgradeLevel;
            changed |= NormalizeDisplayNames(save);
            if (save.rats != null)
            {
                foreach (var rat in save.rats)
                {
                    if (!IsBeginnerFounder(rat)) continue;

                    if (rat.traits == null) rat.traits = new TraitData();
                    float size = Mathf.Clamp(rat.traits.size, 0f, GameConfig.StarterBeginnerTraitMaximum);
                    float health = Mathf.Clamp(rat.traits.health, 0f, GameConfig.StarterBeginnerTraitMaximum);
                    float fertility = Mathf.Clamp(rat.traits.fertility, 0f, GameConfig.StarterBeginnerTraitMaximum);
                    if (!Mathf.Approximately(rat.traits.size, size) ||
                        !Mathf.Approximately(rat.traits.health, health) ||
                        !Mathf.Approximately(rat.traits.fertility, fertility) ||
                        !Mathf.Approximately(rat.baseHealth, health) ||
                        !Mathf.Approximately(rat.baseFertility, fertility))
                    {
                        rat.traits.size = size;
                        rat.traits.health = health;
                        rat.traits.fertility = fertility;
                        rat.baseHealth = health;
                        rat.baseFertility = fertility;
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
                (rat.isStarterRat || rat.id == "rat_mabel" || rat.id == "rat_otto");
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

        /// <summary>
        /// Formats a rat name for player-facing text without changing the
        /// persisted name. Sex symbols are display-only and are stripped first
        /// so a rebuilt UI cannot append them twice.
        /// </summary>
        public static string DisplayName(RatData rat)
        {
            return rat == null ? "Unknown" : DisplayName(rat.name, rat.sex);
        }

        public static string DisplayName(StoreRatListingData listing)
        {
            return listing == null ? "Unknown" : DisplayName(listing.name, listing.sex);
        }

        public static string DisplayName(string name, RatSex sex)
        {
            string result = NormalizeDisplayName(name);
            if (string.IsNullOrWhiteSpace(result)) result = "Unknown";
            result = RemoveTrailingSexSymbol(result);
            return result + " " + SexSymbol(sex);
        }

        public static string SexSymbol(RatSex sex)
        {
            return sex == RatSex.Female ? "♀" : "♂";
        }

        private static string RemoveTrailingSexSymbol(string value)
        {
            string result = value == null ? string.Empty : value.TrimEnd();
            if (result.EndsWith("♂", StringComparison.Ordinal) ||
                result.EndsWith("♀", StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - 1).TrimEnd();
            }
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

        private static int StableSeed(long value)
        {
            unchecked
            {
                ulong mixed = (ulong)value;
                mixed ^= mixed >> 33;
                mixed *= 0xff51afd7ed558ccdUL;
                mixed ^= mixed >> 33;
                mixed *= 0xc4ceb9fe1a85ec53UL;
                mixed ^= mixed >> 33;
                return (int)(mixed & 0x7fffffff);
            }
        }

        public static string NewId(string prefix)
        {
            return prefix + "_" + Guid.NewGuid().ToString("N");
        }
    }
}
