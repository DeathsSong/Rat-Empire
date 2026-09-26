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
        // Presentation behavior has its own small real-time delta because
        // movement and animation should be 1x/2x/3x faster, while biological
        // deadlines continue to use the authoritative simulated game clock
        // below (1 minute/hour/day per real second). Keeping this value here
        // prevents individual behaviors from inventing their own speed path.
        private static float runtimeSimulationSpeed = 1f;
        private static bool simulationPaused;
        private static float lastMovementRealDeltaSeconds;
        private static float lastMovementSimulationDeltaSeconds;

        public static float RuntimeSimulationSpeed { get { return simulationPaused ? 0f : runtimeSimulationSpeed; } }
        public static bool SimulationPaused { get { return simulationPaused; } }

        public static float LastMovementRealDeltaSeconds { get { return lastMovementRealDeltaSeconds; } }
        public static float LastMovementSimulationDeltaSeconds { get { return lastMovementSimulationDeltaSeconds; } }

        public static void SetRuntimeSpeed(float speed)
        {
            runtimeSimulationSpeed = NormalizeSpeed(speed);
        }

        public static void SetSimulationPaused(bool paused)
        {
            simulationPaused = paused;
            if (paused)
            {
                lastMovementRealDeltaSeconds = 0f;
                lastMovementSimulationDeltaSeconds = 0f;
            }
        }

        /// <summary>
        /// Returns the exact simulation-time delta for presentation behavior.
        /// Do not discard elapsed real time here: dropping a slow WebGL frame
        /// makes world-space movement lag behind the accelerated game clock.
        /// MoveTowards callers already clamp their spatial step at the target.
        /// </summary>
        public static float SimulationMovementDeltaSeconds(float realDeltaSeconds)
        {
            if (simulationPaused || realDeltaSeconds <= 0f) return 0f;
            float simulationDelta = realDeltaSeconds * runtimeSimulationSpeed;
            lastMovementRealDeltaSeconds = realDeltaSeconds;
            lastMovementSimulationDeltaSeconds = simulationDelta;
            return simulationDelta;
        }

        // Timed behavior and movement share the same clock. Keep the older
        // name as an alias for biological/presentation timers already using it.
        public static float SimulationBehaviorDeltaSeconds(float realDeltaSeconds)
        {
            return SimulationMovementDeltaSeconds(realDeltaSeconds);
        }

        /// <summary>
        /// Converts a rat's authored world-space speed into one frame's
        /// distance. This is the single path used by every position-writing
        /// movement step, so 2x and 3x change actual travel time rather than
        /// only Animator playback.
        /// </summary>
        public static float SimulationMovementStep(float baseWorldSpeed, float realDeltaSeconds)
        {
            return Mathf.Max(0f, baseWorldSpeed) * SimulationMovementDeltaSeconds(realDeltaSeconds);
        }

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
                SetRuntimeSpeed(save.clock.speed);
                return true;
            }

            long elapsed = Math.Max(0L, realNow - save.clock.lastRealTimestamp);
            save.clock.speed = NormalizeSpeed(save.clock.speed);
            SetRuntimeSpeed(save.clock.speed);
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

            // Zero is a valid inherited stat. Sanitize only non-finite input
            // and clamp the authored values; never use a value threshold to
            // decide whether a trait exists.
            rat.traits.size = ClampTraitValue(rat.traits.size);
            rat.traits.health = ClampTraitValue(rat.traits.health);
            rat.traits.fertility = ClampTraitValue(rat.traits.fertility);

            // The two new-game founders are intentionally absolute beginner
            // values. Keep this guard here, at the authoritative biology
            // boundary, so stage refreshes, save loading, and phenotype
            // rebuilds cannot restore an old 0-100 default over their saved
            // values. Bred offspring and developer/test rats do not match the
            // founder identity check and are never reduced here.
            bool beginnerFounder = ColonyFactory.IsBeginnerFounder(rat);
            if (beginnerFounder)
            {
                rat.traits.size = Mathf.Clamp(rat.traits.size, 0f, 15f);
                rat.traits.health = Mathf.Clamp(rat.traits.health, 0f, 15f);
                rat.traits.fertility = Mathf.Clamp(rat.traits.fertility, 0f, 15f);
                rat.baseHealth = rat.traits.health;
                rat.baseFertility = rat.traits.fertility;
                rat.baseHealthInitialized = true;
                rat.baseFertilityInitialized = true;
            }

            int seed = StableSeed(rat.id);
            int lifespanRange = Mathf.Max(1, Mathf.RoundToInt(GameConfig.MaximumLifespanDays - GameConfig.MinimumLifespanDays));
            int breedingRange = Mathf.Max(1, Mathf.RoundToInt(GameConfig.MaximumBreedingEndDays - GameConfig.MinimumBreedingEndDays) + 1);

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
            if (!rat.baseHealthInitialized)
            {
                rat.baseHealth = rat.traits.health;
                rat.baseHealthInitialized = true;
            }
            if (!rat.baseFertilityInitialized)
            {
                rat.baseFertility = rat.traits.fertility;
                rat.baseFertilityInitialized = true;
            }
            rat.baseHealth = ClampTraitValue(rat.baseHealth);
            rat.baseFertility = ClampTraitValue(rat.baseFertility);

            // Only a female can carry a pregnancy. The father remains a
            // historical participant in PregnancyData and stays breedable.
            if (rat.sex == RatSex.Female && !string.IsNullOrEmpty(rat.pregnancyId))
                rat.reproductiveState = ReproductiveState.Pregnant;
            else if (rat.nursing) rat.reproductiveState = ReproductiveState.Nursing;
            else if (rat.ageDays >= rat.breedingEndAgeDays)
                rat.reproductiveState = ReproductiveState.Infertile;
            else if (rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat ||
                rat.ageDays < rat.sexualMaturityDays)
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

                rat.stage = StageForAge(rat);
                GeneticsSystem.EnsureCoatAppearance(rat);
                rat.phenotype = GeneticsSystem.DerivePhenotype(rat.stage, rat.genotype,
                    rat.coatColorVariant, rat.coatTone);
                if (string.IsNullOrEmpty(rat.markingFamily)) rat.markingFamily = GeneticsSystem.DefaultMarkingFamily(rat.genotype);
                GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
                ApplyAgeDecline(rat);
                if (oldStage != rat.stage)
                {
                    changed = true;
                    RatActivitySystem.Record(save, rat, "growth", "Growing", gameTime,
                        "Grew to " + StageLabel(rat.stage));
                }
            }

            foreach (var rat in naturalDeaths)
            {
                if (rat == null || !save.rats.Remove(rat)) continue;
                rat.removalDisposition = RatRemovalDisposition.NaturalDeath;
                rat.removedAt = gameTime;
                rat.reproductiveState = ReproductiveState.Infertile;
                RatActivitySystem.SetCurrent(save, rat, "deceased", "Deceased", gameTime, "Died");
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
            rat.traits.health = Mathf.Clamp(rat.baseHealth - healthAge * GameConfig.HealthDeclinePerDay, 0f, 100f);
            rat.traits.fertility = Mathf.Clamp(rat.baseFertility - fertilityAge * GameConfig.FertilityDeclinePerDay, 0f, 100f);
        }

        private static float ClampTraitValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Mathf.Clamp(value, 0f, 100f);
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
            if (ageDays < GameConfig.MatureStartDays) return RatStage.Adult;
            return RatStage.Mature;
        }

        /// <summary>
        /// Calculates the complete life stage for a persisted rat. The
        /// Elderly transition is individualized, so callers that have a rat
        /// record must use this overload instead of the age/sex-only helper.
        /// </summary>
        public static RatStage StageForAge(float ageDays, RatSex sex, float breedingEndAgeDays)
        {
            if (ageDays < GameConfig.PinkieStageDays) return RatStage.Pinkie;
            float maturity = sex == RatSex.Female ? GameConfig.FemaleSexualMaturityDays : GameConfig.MaleSexualMaturityDays;
            if (ageDays < maturity) return RatStage.YoungRat;
            if (ageDays < GameConfig.MatureStartDays) return RatStage.Adult;
            if (breedingEndAgeDays > 0f && ageDays >= breedingEndAgeDays) return RatStage.Elderly;
            return RatStage.Mature;
        }

        public static RatStage StageForAge(RatData rat)
        {
            if (rat == null) return RatStage.Adult;
            EnsureBiologyDefaults(rat);
            return StageForAge(rat.ageDays, rat.sex, rat.breedingEndAgeDays);
        }

        public static bool IsElderly(RatData rat)
        {
            if (rat == null) return false;
            EnsureBiologyDefaults(rat);
            return rat.ageDays >= rat.breedingEndAgeDays;
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
                case RatStage.Mature: return GameConfig.MatureStartDays;
                case RatStage.Elderly: return GameConfig.MatureStartDays;
                default: return 0f;
            }
        }

        /// <summary>
        /// Returns the uniform presentation scale for a rat's actual age.
        /// Stage labels remain biological states, while this curve prevents a
        /// seven-day Young Rat from appearing adult-sized in one frame. The
        /// maturity value is persisted on RatData, so the same rat keeps the
        /// same curve after saving, loading, or moving between views.
        /// </summary>
        public static float VisualScaleForAge(RatData rat)
        {
            if (rat == null) return GameConfig.AdultVisualScale;
            // A valid Size value of zero is meaningful and must remain the
            // smallest adult endpoint. Only a missing Traits object falls
            // back to the neutral size-50 presentation.
            float size = rat.traits == null ? 50f : rat.traits.size;
            return VisualScaleForAge(rat.ageDays, rat.sex, rat.sexualMaturityDays, size);
        }

        public static float VisualScaleForAge(float ageDays, RatSex sex, float sexualMaturityDays)
        {
            // Preserve the original overload for callers that only have age
            // data. A neutral size maps exactly to AdultVisualScale.
            return VisualScaleForAge(ageDays, sex, sexualMaturityDays, 50f);
        }

        /// <summary>
        /// Maps the persisted 0-100 Size trait to the final adult presentation
        /// scale. This affects only the visual model; gameplay dimensions and
        /// navigation remain unchanged.
        /// </summary>
        public static float AdultVisualScaleForSize(float size)
        {
            float normalizedSize = Mathf.Clamp01(ClampTraitValue(size) / 100f);
            return Mathf.Lerp(GameConfig.AdultSizeVisualScaleMinimum,
                GameConfig.AdultSizeVisualScaleMaximum, normalizedSize);
        }

        public static float VisualScaleForAge(float ageDays, RatSex sex, float sexualMaturityDays, float size)
        {
            float age = Mathf.Max(0f, ageDays);
            float pinkieEnd = Mathf.Max(0.001f, GameConfig.PinkieStageDays);
            float youngScale = Mathf.Max(0.001f, GameConfig.YoungVisualScale);
            float pinkieScale = Mathf.Max(0.001f, GameConfig.PinkieVisualScale);
            float adultScale = AdultVisualScaleForSize(size);

            // The imported pinkie visual grows gently during its first week,
            // ending at the same small scale used by the first Young Rat
            // model. This keeps the model swap at day seven visually stable.
            if (age < pinkieEnd)
            {
                float progress = Mathf.SmoothStep(0f, 1f, age / pinkieEnd);
                return Mathf.Lerp(pinkieScale, youngScale, progress);
            }

            float maturity = sexualMaturityDays;
            if (maturity <= pinkieEnd)
                maturity = sex == RatSex.Female
                    ? GameConfig.FemaleSexualMaturityDays
                    : GameConfig.MaleSexualMaturityDays;
            maturity = Mathf.Max(pinkieEnd + 0.001f, maturity);
            if (age >= maturity) return adultScale;

            float adultProgress = Mathf.InverseLerp(pinkieEnd, maturity, age);
            adultProgress = Mathf.SmoothStep(0f, 1f, adultProgress);
            return Mathf.Lerp(youngScale, adultScale, adultProgress);
        }

        public static bool AdvanceRatToNextStage(RatData rat, long gameTime)
        {
            if (rat == null || rat.stage == RatStage.Adult || rat.stage == RatStage.Mature ||
                rat.stage == RatStage.Elderly) return false;
            RatStage next = rat.stage == RatStage.Pinkie ? RatStage.YoungRat : RatStage.Adult;
            EnsureBiologyDefaults(rat);
            rat.developerGrowthOverride = true;
            rat.growthTimestamp = gameTime;
            float nextStageMinimumAge = MinimumAgeForStage(next, rat.sex);
            if (next == RatStage.Adult) nextStageMinimumAge = Mathf.Max(nextStageMinimumAge, rat.sexualMaturityDays);
            rat.growthAnchorAgeDays = Mathf.Max(rat.ageDays, nextStageMinimumAge);
            rat.ageDays = rat.growthAnchorAgeDays;
            rat.stage = next;
            GeneticsSystem.EnsureCoatAppearance(rat);
            rat.phenotype = GeneticsSystem.DerivePhenotype(rat.stage, rat.genotype,
                rat.coatColorVariant, rat.coatTone);
            if (string.IsNullOrEmpty(rat.markingFamily)) rat.markingFamily = GeneticsSystem.DefaultMarkingFamily(rat.genotype);
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            if (rat.stage == RatStage.Adult && rat.ageDays >= rat.sexualMaturityDays &&
                rat.reproductiveState == ReproductiveState.Immature)
                rat.reproductiveState = ReproductiveState.Fertile;
            RatActivitySystem.Record(null, rat, "growth", "Growing", gameTime,
                "Grew to " + StageLabel(rat.stage));
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
                case RatStage.Mature: return "Mature";
                case RatStage.Elderly: return "Elderly";
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
