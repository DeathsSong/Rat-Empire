using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Authoritative simulation clock, life-stage, lifespan, and age-trait
    /// system. Presentation code may display these values, but must not make
    /// independent timing decisions.
    /// </summary>
    public static class GrowthSystem
    {
        public static bool AdvanceClock(ColonySaveData save, long realNow)
        {
            if (save == null) return false;
            save.EnsureLists();
            if (save.clock.lastRealTimestamp <= 0)
            {
                save.clock.lastRealTimestamp = realNow;
                if (save.clock.gameTimeMs <= 0) save.clock.gameTimeMs = GameConfig.StartGameTimeMs;
                if (save.clock.gameStartTimestamp <= 0) save.clock.gameStartTimestamp = realNow;
                save.clock.speed = NormalizeSpeed(save.clock.speed);
                return true;
            }

            long elapsed = Math.Max(0L, realNow - save.clock.lastRealTimestamp);
            save.clock.speed = NormalizeSpeed(save.clock.speed);
            save.clock.gameTimeMs += (long)Math.Round(
                elapsed * SimulationMillisecondsPerRealMillisecond(save.clock.speed));
            save.clock.lastRealTimestamp = realNow;
            return elapsed > 0L;
        }

        public static float NormalizeSpeed(float speed)
        {
            if (Mathf.Abs(speed - 2f) < 0.01f) return 2f;
            // The former 4x mode is migrated to the new fastest 3x mode.
            if (Mathf.Abs(speed - 3f) < 0.01f || Mathf.Abs(speed - 4f) < 0.01f) return 3f;
            return 1f;
        }

        /// <summary>
        /// Converts a visible simulation mode into simulated milliseconds per
        /// real millisecond. The three modes intentionally use stepped
        /// biological rates rather than a linear multiplier:
        /// 1x = one game minute/real second, 2x = one game hour/real second,
        /// and 3x = one game day/real second.
        /// </summary>
        public static double SimulationMillisecondsPerRealMillisecond(float speed)
        {
            switch ((int)NormalizeSpeed(speed))
            {
                case 2:
                    return 60d * 60d;
                case 3:
                    return 24d * 60d * 60d;
                default:
                    return 60d;
            }
        }

        public static double SimulationMillisecondsPerRealSecond(float speed)
        {
            return SimulationMillisecondsPerRealMillisecond(speed) * 1000d;
        }

        public static bool IsSupportedSpeed(float speed)
        {
            return Mathf.Abs(NormalizeSpeed(speed) - speed) < 0.01f;
        }

        public static void EnsureBiologyDefaults(RatData rat)
        {
            if (rat == null) return;
            rat.traits ??= new TraitData();
            int seed = StableSeed(rat.id);
            int lifespanRange = Mathf.Max(1, Mathf.RoundToInt(GameConfig.MaximumLifespanDays - GameConfig.MinimumLifespanDays));
            int breedingRange = Mathf.Max(1, Mathf.RoundToInt(GameConfig.MaximumBreedingEndDays - GameConfig.MinimumBreedingEndDays));

            if (rat.expectedLifespanDays <= 0f)
                rat.expectedLifespanDays = GameConfig.MinimumLifespanDays + Mathf.Abs(seed % lifespanRange);
            if (rat.sexualMaturityDays <= 0f)
            {
                float maturity = rat.sex == RatSex.Female ? GameConfig.FemaleSexualMaturityDays : GameConfig.MaleSexualMaturityDays;
                rat.sexualMaturityDays = maturity + (Mathf.Abs(seed / 17) % 15);
            }
            if (rat.breedingEndAgeDays <= 0f)
            {
                rat.breedingEndAgeDays = GameConfig.MinimumBreedingEndDays + Mathf.Abs(seed / 31 % breedingRange);
                rat.breedingEndAgeDays = Mathf.Max(rat.breedingEndAgeDays, rat.sexualMaturityDays + 30f);
            }
            if (rat.estrousCycleAnchorGameTime <= 0L)
                rat.estrousCycleAnchorGameTime = rat.birthTimestamp + (long)(rat.sexualMaturityDays * GameConfig.GameDayMs);
            if (rat.baseHealth <= 0f) rat.baseHealth = rat.traits.health > 0f ? rat.traits.health : 50f;
            if (rat.baseFertility <= 0f) rat.baseFertility = rat.traits.fertility > 0f ? rat.traits.fertility : 50f;

            // Only a female can carry a pregnancy. The father remains a
            // historical participant in PregnancyData and stays breedable.
            if (rat.sex == RatSex.Female && !string.IsNullOrEmpty(rat.pregnancyId))
                rat.reproductiveState = ReproductiveState.Pregnant;
            else if (rat.nursing) rat.reproductiveState = ReproductiveState.Nursing;
            else if (rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat)
                rat.reproductiveState = ReproductiveState.Immature;
            else if (rat.reproductiveState == ReproductiveState.Immature)
                rat.reproductiveState = ReproductiveState.Fertile;
        }

        public static bool RefreshRatStages(ColonySaveData save)
        {
            if (save == null) return false;
            save.EnsureLists();
            bool changed = false;
            long gameTime = save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs;
            var naturalDeaths = new List<RatData>();

            foreach (var rat in save.rats)
            {
                if (rat == null) continue;
                EnsureBiologyDefaults(rat);
                RatStage oldStage = rat.stage;
                if (rat.developerGrowthOverride)
                {
                    float anchorAge = rat.growthAnchorAgeDays;
                    float elapsedDays = rat.growthTimestamp > 0L
                        ? Mathf.Max(0f, (gameTime - rat.growthTimestamp) / (float)GameConfig.GameDayMs)
                        : 0f;
                    rat.ageDays = Mathf.Max(anchorAge, anchorAge + elapsedDays);
                }
                else
                {
                    rat.ageDays = Mathf.Max(0f, (gameTime - rat.birthTimestamp) / (float)GameConfig.GameDayMs);
                }

                if (rat.ageDays >= rat.expectedLifespanDays)
                {
                    naturalDeaths.Add(rat);
                    continue;
                }

                rat.stage = StageForAge(rat.ageDays, rat.sex);
                GeneticsSystem.EnsureCoatAppearance(rat);
                rat.phenotype = GeneticsSystem.DerivePhenotype(rat.stage, rat.genotype,
                    rat.coatColorVariant, rat.coatTone);
                if (string.IsNullOrEmpty(rat.markingFamily)) rat.markingFamily = GeneticsSystem.DefaultMarkingFamily(rat.genotype);
                GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
                ApplyAgeDecline(rat);
                if (oldStage != rat.stage) changed = true;
            }

            foreach (var rat in naturalDeaths)
            {
                if (rat == null || !save.rats.Remove(rat)) continue;
                rat.removalDisposition = RatRemovalDisposition.NaturalDeath;
                rat.removedAt = gameTime;
                rat.reproductiveState = ReproductiveState.Infertile;
                BreedingSystem.CancelPregnanciesForRat(save, rat.id, gameTime);
                save.retiredRats.Add(rat);
                changed = true;
            }
            return changed;
        }

        private static void ApplyAgeDecline(RatData rat)
        {
            if (rat == null || rat.traits == null) return;
            float healthAge = Mathf.Max(0f, rat.ageDays - GameConfig.HealthDeclineStartDays);
            float fertilityAge = Mathf.Max(0f, rat.ageDays - GameConfig.FertilityDeclineStartDays);
            rat.traits.health = Mathf.Clamp(rat.baseHealth - healthAge * GameConfig.HealthDeclinePerDay, 1f, 100f);
            rat.traits.fertility = Mathf.Clamp(rat.baseFertility - fertilityAge * GameConfig.FertilityDeclinePerDay, 0f, 100f);
        }

        public static RatStage StageForAge(float ageDays)
        {
            return StageForAge(ageDays, RatSex.Female);
        }

        public static RatStage StageForAge(float ageDays, RatSex sex)
        {
            if (ageDays < GameConfig.PinkieStageDays) return RatStage.Pinkie;
            float maturity = sex == RatSex.Female ? GameConfig.FemaleSexualMaturityDays : GameConfig.MaleSexualMaturityDays;
            if (ageDays < maturity) return RatStage.YoungRat;
            if (ageDays < GameConfig.SeniorStartDays) return RatStage.Adult;
            return RatStage.Senior;
        }

        public static float MinimumAgeForStage(RatStage stage)
        {
            return MinimumAgeForStage(stage, RatSex.Female);
        }

        public static float MinimumAgeForStage(RatStage stage, RatSex sex)
        {
            switch (stage)
            {
                case RatStage.YoungRat: return GameConfig.PinkieStageDays;
                case RatStage.Adult: return sex == RatSex.Female ? GameConfig.FemaleSexualMaturityDays : GameConfig.MaleSexualMaturityDays;
                case RatStage.Senior: return GameConfig.SeniorStartDays;
                default: return 0f;
            }
        }

        public static bool AdvanceRatToNextStage(RatData rat, long gameTime)
        {
            if (rat == null || rat.stage == RatStage.Adult || rat.stage == RatStage.Senior) return false;
            RatStage next = rat.stage == RatStage.Pinkie ? RatStage.YoungRat : RatStage.Adult;
            EnsureBiologyDefaults(rat);
            rat.developerGrowthOverride = true;
            rat.growthTimestamp = gameTime;
            rat.growthAnchorAgeDays = Mathf.Max(rat.ageDays, MinimumAgeForStage(next, rat.sex));
            rat.ageDays = rat.growthAnchorAgeDays;
            rat.stage = next;
            GeneticsSystem.EnsureCoatAppearance(rat);
            rat.phenotype = GeneticsSystem.DerivePhenotype(rat.stage, rat.genotype,
                rat.coatColorVariant, rat.coatTone);
            if (string.IsNullOrEmpty(rat.markingFamily)) rat.markingFamily = GeneticsSystem.DefaultMarkingFamily(rat.genotype);
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            if (rat.stage == RatStage.Adult && rat.reproductiveState == ReproductiveState.Immature)
                rat.reproductiveState = ReproductiveState.Fertile;
            return true;
        }

        public static int AdvanceAllPinkiesToYoung(ColonySaveData save)
        {
            return AdvanceAllMatching(save, RatStage.Pinkie);
        }

        public static int AdvanceAllYoungToAdults(ColonySaveData save)
        {
            return AdvanceAllMatching(save, RatStage.YoungRat);
        }

        private static int AdvanceAllMatching(ColonySaveData save, RatStage from)
        {
            if (save == null) return 0;
            int count = 0;
            long gameTime = save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs;
            foreach (var rat in save.rats)
            {
                if (rat == null || rat.stage != from) continue;
                if (AdvanceRatToNextStage(rat, gameTime)) count++;
            }
            return count;
        }

        public static string StageLabel(RatStage stage)
        {
            switch (stage)
            {
                case RatStage.Pinkie: return "Pinkie";
                case RatStage.YoungRat: return "Young Rat";
                case RatStage.Senior: return "Senior";
                default: return "Adult";
            }
        }

        public static string FormatAge(float ageDays)
        {
            int totalDays = Mathf.Max(0, Mathf.FloorToInt(ageDays));
            if (totalDays < 7) return UnitLabel(totalDays, "day");
            if (totalDays < 30)
            {
                int weeks = totalDays / 7;
                int days = totalDays % 7;
                return UnitLabel(weeks, "week") + (days == 0 ? string.Empty : ", " + UnitLabel(days, "day"));
            }
            if (totalDays < 365)
            {
                int months = totalDays / 30;
                int weeks = (totalDays % 30) / 7;
                return UnitLabel(months, "month") + (weeks == 0 ? string.Empty : ", " + UnitLabel(weeks, "week"));
            }
            int years = totalDays / 365;
            int remainingMonths = (totalDays % 365) / 30;
            return UnitLabel(years, "year") + (remainingMonths == 0 ? string.Empty : ", " + UnitLabel(remainingMonths, "month"));
        }

        private static int StableSeed(string value)
        {
            unchecked
            {
                int hash = 17;
                if (!string.IsNullOrEmpty(value))
                {
                    for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                }
                return hash == int.MinValue ? int.MaxValue : hash;
            }
        }

        private static string UnitLabel(int value, string singular)
        {
            return value + " " + singular + (value == 1 ? string.Empty : "s");
        }
    }
}
