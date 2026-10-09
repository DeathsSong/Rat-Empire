using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RatHabitat
{
    public static class BreedingSystem
    {
        // Reused indexes keep a reproductive-state refresh linear in the live
        // colony plus its history. This refresh runs on the Unity main thread.
        private static readonly Dictionary<string, PregnancyData> PendingPregnancyByMother =
            new Dictionary<string, PregnancyData>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PregnancyData> PendingPregnancyById =
            new Dictionary<string, PregnancyData>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PregnancyData> PendingPregnancyByParticipant =
            new Dictionary<string, PregnancyData>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PregnancyData> FinishedPregnancyByLitterId =
            new Dictionary<string, PregnancyData>(StringComparer.Ordinal);
        private static readonly List<PregnancyData> PendingPregnancyRecords = new List<PregnancyData>();
        private static readonly Dictionary<string, RatData> HistoricalRatById =
            new Dictionary<string, RatData>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> LatestBirthByMother =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> MotherByLitterId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LitterData> LitterById =
            new Dictionary<string, LitterData>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LitterData> LitterByPupId =
            new Dictionary<string, LitterData>(StringComparer.Ordinal);
        private static readonly Dictionary<string, DedicatedBreedingSessionData> ActiveSessionByParticipant =
            new Dictionary<string, DedicatedBreedingSessionData>(StringComparer.Ordinal);
        private static readonly List<DedicatedBreedingSessionData> ActiveSessionRecords =
            new List<DedicatedBreedingSessionData>();
        private static readonly HashSet<string> MothersWithDependentPinkies =
            new HashSet<string>(StringComparer.Ordinal);
        private static ColonySaveData indexedSave;
        private static int indexedRatCount = -1;
        private static int indexedRetiredRatCount = -1;
        private static int indexedLitterCount = -1;
        private static int indexedPregnancyCount = -1;
        private static int indexedSessionCount = -1;
        private static bool reproductiveIndexesReady;
        private static long nextPendingPregnancyDueGameTime = long.MaxValue;
        private static long nextActiveSessionDueGameTime = long.MaxValue;
        private static long nextRecoveryTransitionGameTime = long.MaxValue;

        public static long NextPendingPregnancyDueGameTime(ColonySaveData save)
        {
            EnsureReproductiveStateIndexes(save);
            return nextPendingPregnancyDueGameTime;
        }

        public static long NextActiveSessionDueGameTime(ColonySaveData save)
        {
            EnsureReproductiveStateIndexes(save);
            return nextActiveSessionDueGameTime;
        }

        public static long NextRecoveryTransitionGameTime(ColonySaveData save)
        {
            EnsureReproductiveStateIndexes(save);
            return nextRecoveryTransitionGameTime;
        }

        public static List<PregnancyData> GetIndexedPendingPregnancies(ColonySaveData save)
        {
            EnsureReproductiveStateIndexes(save);
            return PendingPregnancyRecords;
        }

        public static bool HasDependentPinkies(ColonySaveData save, string motherId)
        {
            if (save == null || string.IsNullOrEmpty(motherId)) return false;
            EnsureReproductiveStateIndexes(save);
            return MothersWithDependentPinkies.Contains(motherId);
        }

        public static void CopyDependentPinkieMotherIds(ColonySaveData save, HashSet<string> destination)
        {
            if (save == null || destination == null) return;
            EnsureReproductiveStateIndexes(save);
            destination.Clear();
            destination.UnionWith(MothersWithDependentPinkies);
        }

        public static LitterData FindLitterForPup(ColonySaveData save, RatData rat)
        {
            if (save == null || rat == null) return null;
            EnsureReproductiveStateIndexes(save);
            if (!string.IsNullOrEmpty(rat.litterId) && LitterById.TryGetValue(rat.litterId, out LitterData litter))
                return litter;
            if (!string.IsNullOrEmpty(rat.id) && LitterByPupId.TryGetValue(rat.id, out litter))
                return litter;
            return null;
        }

        public static void InvalidateReproductiveStateIndexes(ColonySaveData save)
        {
            if (save == null || ReferenceEquals(indexedSave, save)) reproductiveIndexesReady = false;
        }

        private static void EnsureReproductiveStateIndexes(ColonySaveData save)
        {
            if (save == null) return;
            save.EnsureLists();
            if (reproductiveIndexesReady && ReferenceEquals(indexedSave, save) &&
                indexedRatCount == save.rats.Count && indexedLitterCount == save.litters.Count &&
                indexedRetiredRatCount == save.retiredRats.Count &&
                indexedPregnancyCount == save.pregnancies.Count && indexedSessionCount == save.breedingSessions.Count)
                return;
            BuildReproductiveStateIndexes(save);
        }

        /// <summary>
        /// Recovery timestamps are historical after the state has returned to
        /// fertile. Only a rat still in Recovery should wake the full colony
        /// maintenance pass at that timestamp.
        /// </summary>
        public static bool IsRecoveryTransitionDue(RatData rat, long gameTime)
        {
            return rat != null && rat.reproductiveState == ReproductiveState.Recovery &&
                rat.recoveryUntil > 0L && rat.recoveryUntil <= gameTime;
        }

        public struct ReproductiveStatus
        {
            public ReproductiveState state;
            public bool canBreed;
            public string label;
            public string eligibilityReason;
            // When the current state is temporarily unavailable, this is the
            // authoritative next time at which the rat can be considered for
            // breeding. UI summaries can use it without reimplementing the
            // maturity, cooldown, or fertile-window calculations.
            public long nextAvailableAt;
        }

        /// <summary>
        /// Stable, data-only ordering information for the My Rats Breeding
        /// sort. The UI must not derive this from a localized/display label.
        /// </summary>
        public struct BreedingOpportunitySortInfo
        {
            public bool availableNow;
            public bool upcoming;
            public long nextAvailableAt;
            public ReproductiveState state;
        }

        public static RatData FindRat(ColonySaveData save, string id)
        {
            if (save == null || string.IsNullOrEmpty(id)) return null;
            foreach (var rat in save.rats)
            {
                if (rat != null && rat.id == id) return rat;
            }
            return null;
        }

        public static RatData FindHistoricalRat(ColonySaveData save, string id)
        {
            if (save == null || string.IsNullOrEmpty(id)) return null;
            EnsureReproductiveStateIndexes(save);
            return HistoricalRatById.TryGetValue(id, out RatData rat) ? rat : null;
        }

        public static PregnancyData FindPendingPregnancy(ColonySaveData save, string ratId)
        {
            if (save == null || string.IsNullOrEmpty(ratId)) return null;
            EnsureReproductiveStateIndexes(save);
            if (!PendingPregnancyByParticipant.TryGetValue(ratId, out PregnancyData pregnancy) ||
                pregnancy == null || pregnancy.status != "pending" ||
                (pregnancy.motherId != ratId && pregnancy.fatherId != ratId)) return null;
            EnsurePregnancyTiming(pregnancy);
            return pregnancy;
        }

        // A pregnancy record references both parents for history, but only the
        // mother is biologically pregnant. Keep this lookup separate from the
        // relationship lookup above so fathers are not blocked from breeding.
        public static PregnancyData FindPendingPregnancyForMother(ColonySaveData save, string ratId)
        {
            if (save == null || string.IsNullOrEmpty(ratId)) return null;
            EnsureReproductiveStateIndexes(save);
            if (!PendingPregnancyByMother.TryGetValue(ratId, out PregnancyData pregnancy) ||
                pregnancy == null || pregnancy.status != "pending" || pregnancy.motherId != ratId) return null;
            EnsurePregnancyTiming(pregnancy);
            return pregnancy;
        }

        /// <summary>
        /// Returns the active pregnancy that belongs to this female. The
        /// persisted pregnancyId is preferred and must point at a pending
        /// record for the same mother. The motherId fallback keeps older saves
        /// loadable when they predate pregnancyId being written; the save
        /// migration/refresh path repairs that missing link immediately.
        /// </summary>
        public static PregnancyData FindActivePregnancyForMother(ColonySaveData save, RatData rat)
        {
            if (save == null || rat == null || rat.sex != RatSex.Female) return null;
            save.EnsureLists();

            if (!string.IsNullOrEmpty(rat.pregnancyId))
            {
                EnsureReproductiveStateIndexes(save);
                if (PendingPregnancyById.TryGetValue(rat.pregnancyId, out PregnancyData indexedPregnancy) &&
                    indexedPregnancy != null && indexedPregnancy.status == "pending" &&
                    indexedPregnancy.motherId == rat.id)
                {
                    EnsurePregnancyTiming(indexedPregnancy);
                    return indexedPregnancy;
                }
            }

            // Legacy saves can contain the correct pending mother record but
            // no active ID on the RatData object yet. This remains record-based
            // rather than trusting a stale ReproductiveState/display label.
            return FindPendingPregnancyForMother(save, rat.id);
        }

        /// <summary>
        /// Migrates legacy pending pregnancies that did not persist a start
        /// time. The due timestamp remains authoritative; the configured
        /// gestation duration supplies the missing origin safely.
        /// </summary>
        public static bool EnsurePregnancyTiming(PregnancyData pregnancy)
        {
            if (pregnancy == null) return false;
            bool changed = false;
            long duration = pregnancy.gestationDurationMs > 0L
                ? pregnancy.gestationDurationMs
                : GameConfig.PregnancyMs;
            if (pregnancy.gestationDurationMs <= 0L)
            {
                pregnancy.gestationDurationMs = duration;
                changed = true;
            }
            if (pregnancy.startedAt <= 0L && pregnancy.dueAt > 0L)
            {
                pregnancy.startedAt = Math.Max(0L, pregnancy.dueAt - duration);
                changed = true;
            }
            else if (pregnancy.dueAt <= 0L && pregnancy.startedAt > 0L)
            {
                pregnancy.dueAt = pregnancy.startedAt + duration;
                changed = true;
            }
            return changed;
        }

        public static float PregnancyProgress01(PregnancyData pregnancy, long gameTime)
        {
            if (pregnancy == null) return 0f;
            EnsurePregnancyTiming(pregnancy);
            long duration = pregnancy.gestationDurationMs > 0L
                ? pregnancy.gestationDurationMs
                : GameConfig.PregnancyMs;
            if (duration <= 0L || pregnancy.dueAt <= pregnancy.startedAt) return 0f;
            return Mathf.Clamp01((gameTime - pregnancy.startedAt) / (float)duration);
        }

        public static string PregnancyProgressLabel(
            ColonySaveData save,
            RatData rat,
            long gameTime)
        {
            PregnancyData pregnancy = FindActivePregnancyForMother(save, rat);
            if (pregnancy == null) return string.Empty;
            int percentage = Mathf.Clamp(Mathf.RoundToInt(PregnancyProgress01(pregnancy, gameTime) * 100f), 0, 100);
            long remainingMs = Math.Max(0L, pregnancy.dueAt - gameTime);
            return "Pregnant — " + percentage + "%  •  " + FormatDuration(remainingMs);
        }

        public static PregnancyData FindPregnancyForLitter(ColonySaveData save, string litterId)
        {
            if (save == null || string.IsNullOrEmpty(litterId)) return null;
            EnsureReproductiveStateIndexes(save);
            return FinishedPregnancyByLitterId.TryGetValue(litterId, out PregnancyData pregnancy)
                ? pregnancy
                : null;
        }

        private static bool ClearLegacyMalePregnancyState(ColonySaveData save, RatData rat)
        {
            if (rat == null || rat.sex != RatSex.Male) return false;

            // Older saves assigned the shared pregnancy ID to the father too.
            // A male can never be pregnant, so migrate that stale state when it
            // is encountered and leave the father available for future breeding.
            if (!string.IsNullOrEmpty(rat.pregnancyId))
            {
                rat.pregnancyId = null;
                if (rat.reproductiveState == ReproductiveState.Pregnant)
                    rat.reproductiveState = ReproductiveState.Fertile;
                return true;
            }
            return false;
        }

        public static DedicatedBreedingSessionData FindActiveDedicatedSession(ColonySaveData save, string ratId)
        {
            if (save == null || string.IsNullOrEmpty(ratId)) return null;
            EnsureReproductiveStateIndexes(save);
            if (!ActiveSessionByParticipant.TryGetValue(ratId, out DedicatedBreedingSessionData session) ||
                session == null || session.status != "active" ||
                (session.motherId != ratId && session.fatherId != ratId)) return null;
            return session;
        }

        public static List<RatData> GetFertileAdultFemales(ColonySaveData save, long gameTime)
        {
            return GetFertileAdultRats(save, RatSex.Female, gameTime);
        }

        public static List<RatData> GetFertileAdultMales(ColonySaveData save, long gameTime)
        {
            return GetFertileAdultRats(save, RatSex.Male, gameTime);
        }

        private static List<RatData> GetFertileAdultRats(ColonySaveData save, RatSex sex, long gameTime)
        {
            var result = new List<RatData>();
            if (save == null) return result;
            foreach (var rat in save.rats)
            {
                if (rat == null || rat.sex != sex) continue;
                string reason;
                if (IsBreedEligible(save, rat, gameTime, out reason)) result.Add(rat);
            }
            return result;
        }

        public static bool IsBreedEligible(ColonySaveData save, RatData rat, long gameTime, out string reason)
        {
            if (rat == null)
            {
                reason = "Rat not found.";
                return false;
            }
            ReproductiveStatus status = GetReproductiveStatus(save, rat, gameTime);
            reason = status.eligibilityReason;
            return status.canBreed;
        }

        /// <summary>
        /// Returns whether a female's timestamp-defined fertile window touched
        /// the closed interval (fromGameTime, throughGameTime]. Pairing checks
        /// are wall-clock rate limited, so at 3x one rendered/check interval
        /// can cross a complete fertile boundary. Only the window is latched;
        /// age, pregnancy, recovery, cooldown, and session rules are still
        /// evaluated at the current authoritative game time.
        /// </summary>
        public static bool FertileWindowOverlapsInterval(
            RatData rat, long fromGameTime, long throughGameTime)
        {
            if (rat == null || rat.sex != RatSex.Female || throughGameTime <= fromGameTime)
                return false;

            long cycleMs = Math.Max(1L, (long)(GameConfig.EstrousCycleDays * GameConfig.GameDayMs));
            long windowMs = Math.Max(1L, (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs));
            long intervalMs = throughGameTime - fromGameTime;
            if (intervalMs >= cycleMs) return true;

            long phase = (fromGameTime - rat.estrousCycleAnchorGameTime) % cycleMs;
            if (phase < 0L) phase += cycleMs;
            if (phase < windowMs) return true;

            long untilNextWindow = cycleMs - phase;
            return untilNextWindow <= intervalMs;
        }

        /// <summary>
        /// Applies current reproductive eligibility while recovering only a
        /// fertile-window boundary that was crossed since the prior scheduled
        /// check. This prevents 3x clock jumps from turning a briefly sampled
        /// window into a missed breeding opportunity.
        /// </summary>
        public static bool IsBreedEligibleAtOpportunity(
            ColonySaveData save, RatData rat, long gameTime,
            long opportunityWindowStartGameTime, out string reason)
        {
            ReproductiveStatus status = GetReproductiveStatus(save, rat, gameTime);
            reason = status.eligibilityReason;
            if (status.canBreed) return true;

            if (rat != null && rat.sex == RatSex.Female &&
                status.state == ReproductiveState.Fertile &&
                !string.IsNullOrEmpty(reason) &&
                reason.StartsWith("Outside the fertile window", StringComparison.Ordinal) &&
                FertileWindowOverlapsInterval(rat, opportunityWindowStartGameTime, gameTime))
            {
                reason = string.Empty;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Single source of truth for the player-facing reproductive state and
        /// the corresponding breeding eligibility. Keeping these decisions in
        /// one result prevents an adult who is still below her randomized
        /// sexual maturity age from being labelled Fertile while the Breed
        /// action rejects her.
        /// </summary>
        public static ReproductiveStatus GetReproductiveStatus(ColonySaveData save, RatData rat, long gameTime)
        {
            if (rat == null)
            {
                return new ReproductiveStatus
                {
                    state = ReproductiveState.Infertile,
                    canBreed = false,
                    label = "Unknown",
                    eligibilityReason = "Rat not found.",
                };
            }

            ClearLegacyMalePregnancyState(save, rat);
            GrowthSystem.EnsureBiologyDefaults(rat);
            float currentAgeDays = GrowthSystem.AgeDaysAt(rat, gameTime);
            RatStage currentStage = GrowthSystem.StageForAge(
                currentAgeDays, rat.sex, rat.breedingEndAgeDays);

            DedicatedBreedingSessionData session = FindActiveDedicatedSession(save, rat.id);
            if (session != null)
            {
                string label = "Breeding session — ends in " + FormatDuration(Math.Max(0L, session.endsAt - gameTime));
                return Unavailable(ReproductiveState.Fertile, label, "Occupied by a dedicated breeding session.", session.endsAt);
            }

            PregnancyData pregnancy = FindActivePregnancyForMother(save, rat);
            if (pregnancy != null)
            {
                string label = PregnancyProgressLabel(save, rat, gameTime);
                return Unavailable(ReproductiveState.Pregnant, label, "Currently pregnant.", pregnancy.dueAt);
            }

            // Reproductive recovery is owned by the birth timestamp and is
            // deliberately shorter than weaning. A mother may still be
            // nursing/caring after recovery ends, so nursing alone must not
            // block the normal live fertile-window calculation.
            if ((rat.nursing || rat.reproductiveState == ReproductiveState.Nursing) &&
                rat.recoveryUntil > gameTime)
            {
                string label = "Recovering — fertile again in " + FormatDuration(Math.Max(0L, rat.recoveryUntil - gameTime));
                return Unavailable(ReproductiveState.Recovery, label, label + ".", rat.recoveryUntil);
            }

            if (rat.reproductiveState == ReproductiveState.Recovery && rat.recoveryUntil > gameTime)
            {
                string label = "Recovering — fertile again in " + FormatDuration(Math.Max(0L, rat.recoveryUntil - gameTime));
                return Unavailable(ReproductiveState.Recovery, label, label + ".", rat.recoveryUntil);
            }

            // Mature stage begins at one year, but mature rats can still
            // breed during the individualized decline period. Only the
            // persisted breeding-end age is the past-breeding cutoff.
            if (currentAgeDays >= rat.breedingEndAgeDays)
            {
                return Unavailable(ReproductiveState.Infertile, "Past breeding age", "Past breeding age.");
            }

            // The randomized sexualMaturityDays value is authoritative even
            // while the visual life stage already reads Adult.
            if (currentStage == RatStage.Pinkie || currentStage == RatStage.YoungRat ||
                currentAgeDays < rat.sexualMaturityDays)
            {
                long remaining = Math.Max(0L, (long)((rat.sexualMaturityDays - currentAgeDays) * GameConfig.GameDayMs));
                string label = "Immature — breeding available in " + FormatDuration(remaining);
                return Unavailable(ReproductiveState.Immature, label, label + ".", gameTime + remaining);
            }

            if (rat.breedingCooldownUntil > gameTime)
            {
                string label = "Breeding cooldown — available in " +
                    FormatDuration(Math.Max(0L, rat.breedingCooldownUntil - gameTime));
                return Unavailable(ReproductiveState.Fertile, label, label + ".", rat.breedingCooldownUntil);
            }

            if (rat.sex == RatSex.Female)
            {
                long cycleMs = Math.Max(1L, (long)(GameConfig.EstrousCycleDays * GameConfig.GameDayMs));
                long windowMs = Math.Max(1L, (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs));
                long elapsed = gameTime - rat.estrousCycleAnchorGameTime;
                elapsed %= cycleMs;
                if (elapsed < 0L) elapsed += cycleMs;
                if (elapsed < windowMs)
                {
                    string label = "Fertile" + AgeDeclineLabelSuffix(rat) + " — window ends in " + FormatDuration(windowMs - elapsed);
                    return Available(ReproductiveState.Fertile, label);
                }

                string nextWindow = "Next fertile window in " + FormatDuration(cycleMs - elapsed) + AgeDeclineLabelSuffix(rat);
                return Unavailable(ReproductiveState.Fertile, nextWindow,
                    "Outside the fertile window — next window in " + FormatDuration(cycleMs - elapsed) + ".",
                    gameTime + cycleMs - elapsed);
            }

            return Available(ReproductiveState.Fertile, "Fertile" + AgeDeclineLabelSuffix(rat));
        }

        private static ReproductiveStatus Available(ReproductiveState state, string label)
        {
            return new ReproductiveStatus
            {
                state = state,
                canBreed = true,
                label = label,
                eligibilityReason = string.Empty,
                nextAvailableAt = 0L,
            };
        }

        private static ReproductiveStatus Unavailable(ReproductiveState state, string label, string reason)
        {
            return Unavailable(state, label, reason, 0L);
        }

        private static ReproductiveStatus Unavailable(ReproductiveState state, string label, string reason, long nextAvailableAt)
        {
            return new ReproductiveStatus
            {
                state = state,
                canBreed = false,
                label = label,
                eligibilityReason = reason,
                nextAvailableAt = nextAvailableAt,
            };
        }

        public static bool IsInFertileWindow(RatData rat, long gameTime)
        {
            if (rat == null || rat.sex != RatSex.Female) return true;
            GrowthSystem.EnsureBiologyDefaults(rat);
            float currentAgeDays = GrowthSystem.AgeDaysAt(rat, gameTime);
            RatStage currentStage = GrowthSystem.StageForAge(
                currentAgeDays, rat.sex, rat.breedingEndAgeDays);
            if (currentStage == RatStage.Pinkie || currentStage == RatStage.YoungRat ||
                currentAgeDays < rat.sexualMaturityDays || currentAgeDays >= rat.breedingEndAgeDays) return false;
            long cycleMs = Math.Max(1L, (long)(GameConfig.EstrousCycleDays * GameConfig.GameDayMs));
            long windowMs = Math.Max(1L, (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs));
            long elapsed = gameTime - rat.estrousCycleAnchorGameTime;
            elapsed %= cycleMs;
            if (elapsed < 0L) elapsed += cycleMs;
            return elapsed < windowMs;
        }

        /// <summary>
        /// Returns the smooth age-based effectiveness multiplier. A rat is
        /// fully effective through exactly 365 days, then declines
        /// deterministically to zero at its own persisted cutoff.
        /// </summary>
        public static float AgeBreedingEffectiveness(RatData rat)
        {
            if (rat == null) return 0f;
            GrowthSystem.EnsureBiologyDefaults(rat);
            float start = GameConfig.BreedingAgeDeclineStartDays;
            float end = rat.breedingEndAgeDays;
            if (rat.ageDays <= start) return 1f;
            if (end <= start || rat.ageDays >= end) return 0f;

            float progress = Mathf.InverseLerp(start, end, rat.ageDays);
            return 1f - Mathf.SmoothStep(0f, 1f, progress);
        }

        public static bool IsAgeBreedingDeclining(RatData rat)
        {
            if (rat == null) return false;
            return rat.ageDays > GameConfig.BreedingAgeDeclineStartDays &&
                rat.ageDays < rat.breedingEndAgeDays;
        }

        private static string AgeDeclineLabelSuffix(RatData rat)
        {
            return IsAgeBreedingDeclining(rat) ? " — age-related decline" : string.Empty;
        }

        /// <summary>
        /// Calculates conception from both fertility values. Fertility is
        /// combined geometrically so a very low-fertility parent meaningfully
        /// limits the pair, then curved gently so low-stat beginner rats remain
        /// viable. The
        /// habitat base chance and bonus remain separate inputs, allowing the
        /// dedicated two-hour session to be better than automatic pairing.
        /// </summary>
        public static float CalculateConceptionChance(
            RatData female,
            RatData male,
            float habitatBaseChance,
            float habitatBonus,
            float cap)
        {
            float femaleFertility = female == null || female.traits == null ? 0f : Mathf.Clamp01(female.traits.fertility / 100f);
            float maleFertility = male == null || male.traits == null ? 0f : Mathf.Clamp01(male.traits.fertility / 100f);
            float geometricMean = Mathf.Sqrt(femaleFertility * maleFertility);
            float fertilityCurve = geometricMean <= 0f
                ? 0f
                : Mathf.Pow(geometricMean, GameConfig.ConceptionFertilityCurveExponent);
            float ageEffectiveness = Mathf.Min(
                AgeBreedingEffectiveness(female),
                AgeBreedingEffectiveness(male));
            float habitatMaximum = Mathf.Clamp(habitatBaseChance + habitatBonus, 0f, cap);
            return Mathf.Clamp01(habitatMaximum * fertilityCurve * ageEffectiveness);
        }

        public static string ConceptionChanceLabel(RatData female, RatData male, float habitatBaseChance, float habitatBonus, float cap)
        {
            return FormatChancePercent(CalculateConceptionChance(
                female, male, habitatBaseChance, habitatBonus, cap));
        }

        /// <summary>
        /// Keeps small nonzero conception chances visible in the UI. Whole
        /// percentages stay compact, while low values retain enough precision
        /// to avoid displaying a real chance as 0%.
        /// </summary>
        public static string FormatChancePercent(float chance)
        {
            float percent = Mathf.Clamp01(chance) * 100f;
            if (percent <= 0f) return "0%";
            if (percent < 1f)
            {
                return percent.ToString("0.###", CultureInfo.InvariantCulture) + "%";
            }
            if (percent < 10f)
            {
                return percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            }
            return percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        public static void GetLitterSizeRange(RatData mother, RatData father, out int minimum, out int maximum)
        {
            float maternalFertility = mother == null || mother.traits == null ? 0f : Mathf.Clamp01(mother.traits.fertility / 100f);
            float paternalFertility = father == null || father.traits == null ? 0f : Mathf.Clamp01(father.traits.fertility / 100f);
            // The mother's fertility carries most of the capacity; the father
            // contributes a smaller health/fertility component. This gives
            // the requested approximately 18/9/4 maxima at 100/50/25% when
            // both parents share the same fertility.
            float litterFactor = Mathf.Clamp01((maternalFertility * 0.8f) + (paternalFertility * 0.2f));
            float ageEffectiveness = Mathf.Min(
                AgeBreedingEffectiveness(mother),
                AgeBreedingEffectiveness(father));
            float ageAdjustedLitterFactor = litterFactor * ageEffectiveness;
            minimum = Mathf.Max(1, Mathf.FloorToInt(GameConfig.MinimumLitterSizeAtFullFertility * ageAdjustedLitterFactor));
            maximum = Mathf.Max(minimum, Mathf.FloorToInt(GameConfig.MaximumLitterSizeAtFullFertility * ageAdjustedLitterFactor));
        }

        private static int CalculateExpectedLitterSize(RatData mother, RatData father)
        {
            int minimum;
            int maximum;
            GetLitterSizeRange(mother, father, out minimum, out maximum);
            return UnityEngine.Random.Range(minimum, maximum + 1);
        }

        public static string ReproductiveStateLabel(ColonySaveData save, RatData rat, long gameTime)
        {
            return GetReproductiveStatus(save, rat, gameTime).label;
        }

        /// <summary>
        /// Compact summary for list cards. It is derived from the same
        /// ReproductiveStatus used by breeding validation, so a card cannot
        /// claim that breeding is available when the action would reject it.
        /// </summary>
        public static string BreedingAvailabilityLabel(ColonySaveData save, RatData rat, long gameTime)
        {
            ReproductiveStatus status = GetReproductiveStatus(save, rat, gameTime);
            if (status.canBreed)
                return "Breeding available now" + (IsAgeBreedingDeclining(rat) ? " — age-related decline" : string.Empty);

            switch (status.state)
            {
                case ReproductiveState.Pregnant:
                    return PregnancyProgressLabel(save, rat, gameTime);
                case ReproductiveState.Nursing:
                    return "Breeding unavailable — nursing";
                case ReproductiveState.Recovery:
                    return "Breeding unavailable — recovering";
                case ReproductiveState.Infertile:
                    return "Breeding unavailable — past breeding age";
            }

            if (status.nextAvailableAt > gameTime)
                return "Breeding available in " + FormatDuration(status.nextAvailableAt - gameTime) +
                    (IsAgeBreedingDeclining(rat) ? " — age-related decline" : string.Empty);
            return "Breeding unavailable — past breeding age";
        }

        /// <summary>
        /// Compares two My Rats entries for the Pregnancy sort. The caller
        /// filters the list to active pregnancies; this comparator defensively
        /// keeps any accidental non-pregnant entries after them. Pregnancy
        /// ordering is always soonest due date first.
        /// </summary>
        public static int ComparePregnancySort(
            ColonySaveData save,
            RatData first,
            RatData second,
            long gameTime,
            bool ascending)
        {
            PregnancyData firstPregnancy = FindActivePregnancyForMother(save, first);
            PregnancyData secondPregnancy = FindActivePregnancyForMother(save, second);
            bool firstPregnant = firstPregnancy != null;
            bool secondPregnant = secondPregnancy != null;

            int firstRank = firstPregnant ? 0 : PregnancyStateSortRank(
                first != null && EnclosureSystem.HasDependentPinkies(save, first.id)
                    ? ReproductiveState.Nursing
                    : GetReproductiveStatus(save, first, gameTime).state);
            int secondRank = secondPregnant ? 0 : PregnancyStateSortRank(
                second != null && EnclosureSystem.HasDependentPinkies(save, second.id)
                    ? ReproductiveState.Nursing
                    : GetReproductiveStatus(save, second, gameTime).state);
            int rankComparison = firstRank.CompareTo(secondRank);
            if (rankComparison != 0) return ascending ? rankComparison : -rankComparison;

            if (firstPregnant && secondPregnant)
            {
                int dueComparison = PregnancyDueSortValue(firstPregnancy)
                    .CompareTo(PregnancyDueSortValue(secondPregnancy));
                if (dueComparison != 0) return ascending ? dueComparison : -dueComparison;
            }
            int stableComparison = CompareStableRatId(first, second);
            return ascending ? stableComparison : -stableComparison;
        }

        /// <summary>
        /// Orders by the next real breeding opportunity: available now,
        /// then an upcoming fertile/cooldown opportunity, then temporarily or
        /// permanently unavailable rats. It uses GetReproductiveStatus so
        /// pregnancy, nursing, recovery, maturity, cooldown, age cutoff, and
        /// fertile-window rules stay in one place.
        /// </summary>
        public static int CompareBreedingSort(
            ColonySaveData save,
            RatData first,
            RatData second,
            long gameTime)
        {
            BreedingOpportunitySortInfo firstInfo = GetBreedingOpportunitySortInfo(save, first, gameTime);
            BreedingOpportunitySortInfo secondInfo = GetBreedingOpportunitySortInfo(save, second, gameTime);
            int firstRank = OpportunityRank(firstInfo, gameTime);
            int secondRank = OpportunityRank(secondInfo, gameTime);
            int result = firstRank.CompareTo(secondRank);
            if (result != 0) return result;

            if (firstRank == 1)
            {
                result = firstInfo.nextAvailableAt.CompareTo(secondInfo.nextAvailableAt);
                if (result != 0) return result;
            }
            else if (firstRank == 2)
            {
                long firstNext = firstInfo.nextAvailableAt > gameTime ? firstInfo.nextAvailableAt : long.MaxValue;
                long secondNext = secondInfo.nextAvailableAt > gameTime ? secondInfo.nextAvailableAt : long.MaxValue;
                result = firstNext.CompareTo(secondNext);
                if (result != 0) return result;
                result = PregnancyStateSortRank(firstInfo.state).CompareTo(PregnancyStateSortRank(secondInfo.state));
                if (result != 0) return result;
            }

            return CompareStableRatId(first, second);
        }

        /// <summary>
        /// Compatibility wrapper for callers from older UI builds. The
        /// next-opportunity ordering is now exposed as the Breeding sort.
        /// </summary>
        public static int CompareFertilitySort(
            ColonySaveData save,
            RatData first,
            RatData second,
            long gameTime)
        {
            return CompareBreedingSort(save, first, second, gameTime);
        }

        /// <summary>
        /// Numeric My Rats Fertility sort. This intentionally reads the
        /// current stored fertility stat and never evaluates breeding
        /// eligibility or changes reproductive state.
        /// </summary>
        public static int CompareFertilityStatSort(
            RatData first,
            RatData second,
            bool ascending)
        {
            float firstFertility = first == null || first.traits == null ? 0f : first.traits.fertility;
            float secondFertility = second == null || second.traits == null ? 0f : second.traits.fertility;
            int result = firstFertility.CompareTo(secondFertility);
            if (!ascending) result = -result;
            if (result != 0) return result;
            return CompareStableRatId(first, second);
        }

        public static BreedingOpportunitySortInfo GetBreedingOpportunitySortInfo(
            ColonySaveData save,
            RatData rat,
            long gameTime)
        {
            ReproductiveStatus status = GetReproductiveStatus(save, rat, gameTime);
            return new BreedingOpportunitySortInfo
            {
                availableNow = status.canBreed,
                upcoming = !status.canBreed && status.state == ReproductiveState.Fertile &&
                    status.nextAvailableAt > gameTime,
                nextAvailableAt = status.nextAvailableAt,
                state = status.state,
            };
        }

        private static int OpportunityRank(BreedingOpportunitySortInfo info, long gameTime)
        {
            if (info.availableNow) return 0;
            if (info.upcoming) return 1;
            return 2;
        }

        private static int CompareStableRatId(RatData first, RatData second)
        {
            return string.Compare(first == null ? string.Empty : first.id,
                second == null ? string.Empty : second.id,
                System.StringComparison.Ordinal);
        }

        private static long PregnancyDueSortValue(PregnancyData pregnancy)
        {
            return pregnancy == null || pregnancy.dueAt <= 0L ? long.MaxValue : pregnancy.dueAt;
        }

        private static int PregnancyStateSortRank(ReproductiveState state)
        {
            switch (state)
            {
                case ReproductiveState.Pregnant: return 0;
                case ReproductiveState.Nursing: return 1;
                case ReproductiveState.Recovery: return 2;
                case ReproductiveState.Fertile: return 3;
                case ReproductiveState.Immature: return 4;
                case ReproductiveState.Infertile: return 5;
                default: return 6;
            }
        }

        private static string FormatDuration(long milliseconds)
        {
            long hoursTotal = Math.Max(0L, milliseconds) / (60L * 60L * 1000L);
            long days = hoursTotal / 24L;
            long hours = hoursTotal % 24L;
            return days + " days, " + hours + " hours";
        }

        public static bool RefreshReproductiveStates(ColonySaveData save, long gameTime)
        {
            // Birth-transaction repair is explicitly opt-in for load/recovery.
            // Ordinary UI, pairing, and maintenance refreshes must never walk
            // completed historical pregnancies looking for old write gaps.
            return RefreshReproductiveStates(save, gameTime, false);
        }

        /// <summary>
        /// Reconciles current reproductive state. Finished birth transaction
        /// repair is required after load, but not on routine runtime deadlines:
        /// the birth transaction itself retries failures while the app is live.
        /// Skipping the historical repair scan here avoids rescanning every
        /// past litter and pup during ordinary maintenance.
        /// </summary>
        public static bool RefreshReproductiveStates(
            ColonySaveData save, long gameTime, bool repairFinishedBirthTransactions)
        {
            if (save == null) return false;
            save.EnsureLists();
            bool changed = false;
            if (repairFinishedBirthTransactions)
            {
                long repairSample = RuntimePerformanceDiagnostics.Begin(
                    PerformanceProbeArea.MaintenanceBirthRepairRecovery);
                int transactionsScanned;
                int transactionsReopened;
                changed = RepairBirthTransactions(save, gameTime,
                    out transactionsScanned, out transactionsReopened);
                RuntimePerformanceDiagnostics.End(
                    PerformanceProbeArea.MaintenanceBirthRepairRecovery, repairSample);
                RuntimePerformanceDiagnostics.RecordMaintenanceWorkCount(
                    PerformanceProbeArea.MaintenanceBirthRepairRecovery,
                    transactionsScanned, transactionsReopened);
                if (changed) InvalidateReproductiveStateIndexes(save);
            }

            // Reuse the indexed history while the save's relationship lists
            // are unchanged. Rebuilding here on every maintenance deadline
            // made even a no-pregnancy colony pay for a complete history scan
            // every simulated day. Mutators invalidate the cache explicitly;
            // list-count changes are detected by Ensure as a fallback.
            EnsureReproductiveStateIndexes(save);
            nextRecoveryTransitionGameTime = long.MaxValue;
            foreach (var rat in save.rats)
            {
                if (rat == null) continue;
                if (ClearLegacyMalePregnancyState(save, rat)) changed = true;
                GrowthSystem.EnsureBiologyDefaults(rat);
                ReproductiveState oldState = rat.reproductiveState;
                long migratedRecoveryDeadline;
                if (!LatestBirthByMother.TryGetValue(rat.id ?? string.Empty, out long latestBirth))
                    latestBirth = 0L;
                migratedRecoveryDeadline = latestBirth <= 0L ? 0L : latestBirth +
                    (long)(GameConfig.RecoveryDays * GameConfig.GameDayMs);
                if (rat.sex == RatSex.Female && migratedRecoveryDeadline > 0L &&
                    rat.recoveryUntil > migratedRecoveryDeadline &&
                    (rat.nursing || rat.reproductiveState == ReproductiveState.Nursing ||
                        rat.reproductiveState == ReproductiveState.Recovery))
                {
                    // Older saves calculated recovery from weaning and used a
                    // 60-day constant. Rebase that stale deadline to the
                    // birth timestamp plus the current configurable period.
                    rat.recoveryUntil = migratedRecoveryDeadline;
                    changed = true;
                }
                PregnancyData pending = null;
                if (rat.sex == RatSex.Female)
                {
                    if (!string.IsNullOrEmpty(rat.pregnancyId) &&
                        PendingPregnancyById.TryGetValue(rat.pregnancyId, out PregnancyData idMatch) &&
                        idMatch.motherId == rat.id)
                        pending = idMatch;
                    if (pending == null)
                        PendingPregnancyByMother.TryGetValue(rat.id ?? string.Empty, out pending);
                }
                if (rat.sex == RatSex.Female)
                {
                    if (pending != null && rat.pregnancyId != pending.id)
                    {
                        rat.pregnancyId = pending.id;
                        changed = true;
                    }
                    else if (pending == null && !string.IsNullOrEmpty(rat.pregnancyId))
                    {
                        // A stale ID must not keep a female visibly pregnant
                        // after its record was completed or removed.
                        rat.pregnancyId = null;
                        changed = true;
                    }
                }
                if (rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat ||
                    rat.ageDays < rat.sexualMaturityDays)
                {
                    rat.reproductiveState = ReproductiveState.Immature;
                }
                else if (rat.ageDays >= rat.breedingEndAgeDays)
                {
                    rat.reproductiveState = ReproductiveState.Infertile;
                }
                else if (pending != null && pending.motherId == rat.id)
                {
                    rat.reproductiveState = ReproductiveState.Pregnant;
                }
                else if (rat.nursing || rat.reproductiveState == ReproductiveState.Nursing)
                {
                    // The live dependent-pinkie records are authoritative for
                    // nursing. If the last pinkie is removed or reaches Young,
                    // the mother exits nursing immediately and begins recovery
                    // instead of remaining stuck in Nursery on an old timer.
                    bool stillNursing = MothersWithDependentPinkies.Contains(rat.id ?? string.Empty);
                    if (stillNursing)
                    {
                        rat.nursing = true;
                        // Once the post-birth deadline has elapsed, leave the
                        // state available for live fertile-window evaluation
                        // even while the mother continues caring for pups.
                        rat.reproductiveState = rat.recoveryUntil > gameTime
                            ? ReproductiveState.Recovery
                            : ReproductiveState.Fertile;
                    }
                    else
                    {
                        rat.nursing = false;
                        rat.nursingPupId = null;
                        rat.nursingInteractionUntil = 0L;
                        rat.nursingInteractionType = null;
                        rat.recoveryUntil = Math.Max(rat.recoveryUntil, gameTime);
                        rat.reproductiveState = rat.recoveryUntil > gameTime
                            ? ReproductiveState.Recovery
                            : ReproductiveState.Fertile;
                    }
                }
                else if (rat.reproductiveState == ReproductiveState.Recovery)
                {
                    rat.reproductiveState = gameTime >= rat.recoveryUntil
                        ? ReproductiveState.Fertile
                        : ReproductiveState.Recovery;
                }
                else
                {
                    // Infertile was historically left sticky after the old
                    // 270-365 day cutoff. Once migration extends a rat's
                    // cutoff, age is authoritative and the rat can return
                    // to normal fertile-window evaluation.
                    rat.reproductiveState = ReproductiveState.Fertile;
                }
                if (oldState != rat.reproductiveState) changed = true;
                if (rat.reproductiveState == ReproductiveState.Recovery && rat.recoveryUntil > 0L &&
                    rat.recoveryUntil < nextRecoveryTransitionGameTime)
                    nextRecoveryTransitionGameTime = rat.recoveryUntil;
            }
            return changed;
        }

        private static void BuildReproductiveStateIndexes(ColonySaveData save)
        {
            PendingPregnancyByMother.Clear();
            PendingPregnancyById.Clear();
            PendingPregnancyByParticipant.Clear();
            FinishedPregnancyByLitterId.Clear();
            PendingPregnancyRecords.Clear();
            HistoricalRatById.Clear();
            LatestBirthByMother.Clear();
            MotherByLitterId.Clear();
            LitterById.Clear();
            LitterByPupId.Clear();
            ActiveSessionByParticipant.Clear();
            ActiveSessionRecords.Clear();
            MothersWithDependentPinkies.Clear();
            nextPendingPregnancyDueGameTime = long.MaxValue;
            nextActiveSessionDueGameTime = long.MaxValue;
            nextRecoveryTransitionGameTime = long.MaxValue;

            if (save.litters != null)
            {
                long litterScan = RuntimePerformanceDiagnostics.Begin(
                    PerformanceProbeArea.MaintenanceHistoricalLitters);
                foreach (LitterData litter in save.litters)
                {
                    if (litter == null) continue;
                    if (!string.IsNullOrEmpty(litter.id))
                    {
                        LitterById[litter.id] = litter;
                        if (!string.IsNullOrEmpty(litter.motherId)) MotherByLitterId[litter.id] = litter.motherId;
                    }
                    if (litter.pupIds != null)
                    {
                        foreach (string pupId in litter.pupIds)
                            if (!string.IsNullOrEmpty(pupId) && !LitterByPupId.ContainsKey(pupId))
                                LitterByPupId.Add(pupId, litter);
                    }
                    if (string.IsNullOrEmpty(litter.motherId)) continue;
                    if (litter.birthTimestamp <= 0L) continue;
                    if (!LatestBirthByMother.TryGetValue(litter.motherId, out long existingBirth) ||
                        litter.birthTimestamp > existingBirth)
                        LatestBirthByMother[litter.motherId] = litter.birthTimestamp;
                }
                RuntimePerformanceDiagnostics.End(
                    PerformanceProbeArea.MaintenanceHistoricalLitters, litterScan);
                RuntimePerformanceDiagnostics.RecordMaintenanceWorkCount(
                    PerformanceProbeArea.MaintenanceHistoricalLitters, save.litters.Count, 0);
            }

            if (save.pregnancies != null)
            {
                long pregnancyScan = RuntimePerformanceDiagnostics.Begin(
                    PerformanceProbeArea.MaintenanceHistoricalPregnancies);
                foreach (PregnancyData pregnancy in save.pregnancies)
                {
                    if (pregnancy == null) continue;
                    EnsurePregnancyTiming(pregnancy);
                    if (pregnancy.status == "finished" && !string.IsNullOrEmpty(pregnancy.litterId) &&
                        !FinishedPregnancyByLitterId.ContainsKey(pregnancy.litterId))
                        FinishedPregnancyByLitterId.Add(pregnancy.litterId, pregnancy);
                    if (pregnancy.status != "pending") continue;
                    if (!string.IsNullOrEmpty(pregnancy.id))
                        PendingPregnancyById[pregnancy.id] = pregnancy;
                    if (!string.IsNullOrEmpty(pregnancy.motherId) &&
                        !PendingPregnancyByMother.ContainsKey(pregnancy.motherId))
                        PendingPregnancyByMother[pregnancy.motherId] = pregnancy;
                    if (!string.IsNullOrEmpty(pregnancy.motherId) &&
                        !PendingPregnancyByParticipant.ContainsKey(pregnancy.motherId))
                        PendingPregnancyByParticipant[pregnancy.motherId] = pregnancy;
                    if (!string.IsNullOrEmpty(pregnancy.fatherId) &&
                        !PendingPregnancyByParticipant.ContainsKey(pregnancy.fatherId))
                        PendingPregnancyByParticipant[pregnancy.fatherId] = pregnancy;
                    PendingPregnancyRecords.Add(pregnancy);
                    if (pregnancy.dueAt > 0L && pregnancy.dueAt < nextPendingPregnancyDueGameTime)
                        nextPendingPregnancyDueGameTime = pregnancy.dueAt;
                }
                RuntimePerformanceDiagnostics.End(
                    PerformanceProbeArea.MaintenanceHistoricalPregnancies, pregnancyScan);
                RuntimePerformanceDiagnostics.RecordMaintenanceWorkCount(
                    PerformanceProbeArea.MaintenanceHistoricalPregnancies, save.pregnancies.Count, 0);
            }

            if (save.breedingSessions != null)
            {
                foreach (DedicatedBreedingSessionData session in save.breedingSessions)
                {
                    if (session == null || session.status != "active") continue;
                    ActiveSessionRecords.Add(session);
                    if (!string.IsNullOrEmpty(session.motherId) &&
                        !ActiveSessionByParticipant.ContainsKey(session.motherId))
                        ActiveSessionByParticipant[session.motherId] = session;
                    if (!string.IsNullOrEmpty(session.fatherId) &&
                        !ActiveSessionByParticipant.ContainsKey(session.fatherId))
                        ActiveSessionByParticipant[session.fatherId] = session;
                    if (session.endsAt > 0L && session.endsAt < nextActiveSessionDueGameTime)
                        nextActiveSessionDueGameTime = session.endsAt;
                }
            }

            long historicalRatIndexSample = RuntimePerformanceDiagnostics.Begin(
                PerformanceProbeArea.MaintenanceHistoricalRatIndex);
            if (save.rats != null)
            foreach (RatData pup in save.rats)
            {
                if (pup == null) continue;
                if (!string.IsNullOrEmpty(pup.id) && !HistoricalRatById.ContainsKey(pup.id))
                    HistoricalRatById.Add(pup.id, pup);
                if (pup.reproductiveState == ReproductiveState.Recovery && pup.recoveryUntil > 0L &&
                    pup.recoveryUntil < nextRecoveryTransitionGameTime)
                    nextRecoveryTransitionGameTime = pup.recoveryUntil;
                if (pup.stage != RatStage.Pinkie) continue;
                if (!string.IsNullOrEmpty(pup.motherId))
                {
                    MothersWithDependentPinkies.Add(pup.motherId);
                    continue;
                }

                if (!string.IsNullOrEmpty(pup.litterId) &&
                    MotherByLitterId.TryGetValue(pup.litterId, out string motherId))
                    MothersWithDependentPinkies.Add(motherId);
            }
            if (save.retiredRats != null)
                foreach (RatData retiredRat in save.retiredRats)
                    if (retiredRat != null && !string.IsNullOrEmpty(retiredRat.id) &&
                        !HistoricalRatById.ContainsKey(retiredRat.id))
                        HistoricalRatById.Add(retiredRat.id, retiredRat);
            RuntimePerformanceDiagnostics.End(
                PerformanceProbeArea.MaintenanceHistoricalRatIndex, historicalRatIndexSample);
            RuntimePerformanceDiagnostics.RecordMaintenanceWorkCount(
                PerformanceProbeArea.MaintenanceHistoricalRatIndex,
                (save.rats == null ? 0 : save.rats.Count) +
                (save.retiredRats == null ? 0 : save.retiredRats.Count), 0);

            indexedSave = save;
            indexedRatCount = save.rats.Count;
            indexedRetiredRatCount = save.retiredRats.Count;
            indexedLitterCount = save.litters.Count;
            indexedPregnancyCount = save.pregnancies.Count;
            indexedSessionCount = save.breedingSessions.Count;
            reproductiveIndexesReady = true;
        }

        /// <summary>
        /// Repairs the one state that must never be silently accepted: a
        /// completed pregnancy with no durable litter. Older builds could
        /// clear the pregnancy before a browser save completed. Reopening the
        /// record makes the normal due/arrival path retry it safely.
        /// </summary>
        private static bool RepairBirthTransactions(
            ColonySaveData save, long gameTime, out int transactionsScanned, out int transactionsReopened)
        {
            transactionsScanned = save == null || save.pregnancies == null ? 0 : save.pregnancies.Count;
            transactionsReopened = 0;
            if (save == null || save.pregnancies == null) return false;

            // Recovery runs only while loading/importing a save. Build the
            // identity maps once so a corrupt legacy save with many completed
            // pregnancies does not nest a litter scan and rat scan per record.
            var litterById = new Dictionary<string, LitterData>(StringComparer.Ordinal);
            if (save.litters != null)
            {
                long litterScan = RuntimePerformanceDiagnostics.Begin(
                    PerformanceProbeArea.MaintenanceHistoricalLitters);
                foreach (LitterData litter in save.litters)
                    if (litter != null && !string.IsNullOrEmpty(litter.id)) litterById[litter.id] = litter;
                RuntimePerformanceDiagnostics.End(
                    PerformanceProbeArea.MaintenanceHistoricalLitters, litterScan);
                RuntimePerformanceDiagnostics.RecordMaintenanceWorkCount(
                    PerformanceProbeArea.MaintenanceHistoricalLitters, save.litters.Count, 0);
            }

            var knownRatIds = new HashSet<string>(StringComparer.Ordinal);
            var activeRatById = new Dictionary<string, RatData>(StringComparer.Ordinal);
            if (save.rats != null)
                foreach (RatData rat in save.rats)
                    if (rat != null && !string.IsNullOrEmpty(rat.id))
                    {
                        knownRatIds.Add(rat.id);
                        activeRatById[rat.id] = rat;
                    }
            if (save.retiredRats != null)
                foreach (RatData rat in save.retiredRats)
                    if (rat != null && !string.IsNullOrEmpty(rat.id)) knownRatIds.Add(rat.id);

            bool changed = false;
            foreach (PregnancyData pregnancy in save.pregnancies)
            {
                if (pregnancy == null || pregnancy.status != "finished" || string.IsNullOrEmpty(pregnancy.litterId)) continue;
                litterById.TryGetValue(pregnancy.litterId, out LitterData litter);
                bool litterComplete = litter != null && litter.pupIds != null && litter.pupIds.Count > 0 &&
                    (litter.size <= 0 || litter.pupIds.Count == litter.size);
                if (litterComplete)
                    foreach (string pupId in litter.pupIds)
                        if (string.IsNullOrEmpty(pupId) || !knownRatIds.Contains(pupId))
                        {
                            litterComplete = false;
                            break;
                        }
                if (litterComplete) continue;

                pregnancy.status = "pending";
                pregnancy.finishedAt = 0L;
                pregnancy.litterId = null;
                pregnancy.birthCommitState = 0;
                pregnancy.birthFailureReason = "Previous birth had no saved litter; pregnancy reopened for retry.";
                activeRatById.TryGetValue(pregnancy.motherId ?? string.Empty, out RatData mother);
                if (mother != null)
                {
                    mother.pregnancyId = pregnancy.id;
                    mother.reproductiveState = ReproductiveState.Pregnant;
                }
                changed = true;
                transactionsReopened++;
                UnityEngine.Debug.LogWarning("[Rat Habitat] Reopened incomplete birth " + pregnancy.id + " at game time " + gameTime + ".");
            }
            return changed;
        }

        public static List<RatData> GetEligibleMates(ColonySaveData save, RatData parentA, long gameTime)
        {
            var mates = new List<RatData>();
            if (save == null || parentA == null) return mates;
            string ignored;
            if (!IsBreedEligible(save, parentA, gameTime, out ignored)) return mates;
            foreach (var candidate in save.rats)
            {
                if (candidate == null || candidate.id == parentA.id || candidate.sex == parentA.sex) continue;
                if (IsBreedEligible(save, candidate, gameTime, out ignored)) mates.Add(candidate);
            }
            return mates;
        }

        public static bool ValidatePair(ColonySaveData save, RatData first, RatData second, long gameTime, out string reason)
        {
            reason = string.Empty;
            if (first == null || second == null || first.id == second.id)
            {
                reason = "Choose two different rats.";
                return false;
            }
            if (first.sex == second.sex)
            {
                reason = "Choose one adult male and one adult female.";
                return false;
            }
            string firstReason;
            string secondReason;
            if (!IsBreedEligible(save, first, gameTime, out firstReason))
            {
                reason = ColonyFactory.DisplayName(first) + ": " + firstReason;
                return false;
            }
            if (!IsBreedEligible(save, second, gameTime, out secondReason))
            {
                reason = ColonyFactory.DisplayName(second) + ": " + secondReason;
                return false;
            }
            return true;
        }

        public static bool StartBreeding(ColonySaveData save, RatData parentA, RatData parentB, long gameTime, out PregnancyData pregnancy, out string reason)
        {
            pregnancy = null;
            if (!ValidatePair(save, parentA, parentB, gameTime, out reason)) return false;

            pregnancy = CreatePregnancy(save, parentA, parentB, gameTime);
            reason = string.Empty;
            return true;
        }

        internal static PregnancyData CreatePregnancy(ColonySaveData save, RatData parentA, RatData parentB, long gameTime)
        {
            var mother = parentA.sex == RatSex.Female ? parentA : parentB;
            var father = parentA.sex == RatSex.Male ? parentA : parentB;
            var pregnancy = new PregnancyData
            {
                id = ColonyFactory.NewId("pregnancy"),
                motherId = mother.id,
                fatherId = father.id,
                startedAt = gameTime,
                dueAt = gameTime + GameConfig.PregnancyMs,
                gestationDurationMs = GameConfig.PregnancyMs,
                finishedAt = 0L,
                status = "pending",
                litterId = null,
                expectedLitterSize = CalculateExpectedLitterSize(mother, father),
            };
            save.pregnancies.Add(pregnancy);
            InvalidateReproductiveStateIndexes(save);
            mother.pregnancyId = pregnancy.id;
            // The father remains a historical participant, not a pregnant rat.
            // His ID is preserved on PregnancyData for litter history.
            father.pregnancyId = null;
            mother.reproductiveState = ReproductiveState.Pregnant;
            father.reproductiveState = ReproductiveState.Fertile;
            RatActivitySystem.SetCurrent(save, mother, "pregnant", "Pregnant", gameTime);
            RatActivitySystem.SetCurrent(save, father, "exploring", "Exploring", gameTime);
            return pregnancy;
        }

        public static bool StartDedicatedBreedingSession(
            ColonySaveData save,
            RatData parentA,
            RatData parentB,
            long gameTime,
            out DedicatedBreedingSessionData session,
            out string reason)
        {
            session = null;
            if (!ValidatePair(save, parentA, parentB, gameTime, out reason)) return false;
            if (FindActiveDedicatedSession(save, parentA.id) != null || FindActiveDedicatedSession(save, parentB.id) != null)
            {
                reason = "One of these rats is already occupied by a dedicated breeding session.";
                return false;
            }

            var mother = parentA.sex == RatSex.Female ? parentA : parentB;
            var father = parentA.sex == RatSex.Male ? parentA : parentB;
            session = new DedicatedBreedingSessionData
            {
                id = ColonyFactory.NewId("breeding-session"),
                motherId = mother.id,
                fatherId = father.id,
                startedAt = gameTime,
                endsAt = gameTime + (long)(GameConfig.DedicatedBreedingSessionHours * 60f * 60f * 1000f),
                successChance = CalculateConceptionChance(
                    mother,
                    father,
                    GameConfig.PairingPregnancyChance,
                    GameConfig.DedicatedBreedingSuccessBonus,
                    GameConfig.DedicatedBreedingSuccessCap),
                status = "active",
                resolvedAt = 0L,
                conceptionSucceeded = false,
            };
            save.EnsureLists();
            save.breedingSessions.Add(session);
            InvalidateReproductiveStateIndexes(save);
            RatActivitySystem.SetCurrent(save, parentA, "breeding", "Breeding", gameTime);
            RatActivitySystem.SetCurrent(save, parentB, "breeding", "Breeding", gameTime);
            reason = string.Empty;
            return true;
        }

        public static int ResolveDueDedicatedBreedingSessions(ColonySaveData save, long gameTime, out List<DedicatedBreedingSessionData> resolved)
        {
            resolved = new List<DedicatedBreedingSessionData>();
            if (save == null) return 0;
            EnsureReproductiveStateIndexes(save);
            for (int index = 0; index < ActiveSessionRecords.Count; index++)
            {
                DedicatedBreedingSessionData session = ActiveSessionRecords[index];
                if (session == null || session.status != "active" || session.endsAt > gameTime) continue;
                var mother = FindRat(save, session.motherId);
                var father = FindRat(save, session.fatherId);
                bool valid = mother != null && father != null && mother.sex == RatSex.Female && father.sex == RatSex.Male &&
                    mother.stage != RatStage.Pinkie && mother.stage != RatStage.YoungRat &&
                    father.stage != RatStage.Pinkie && father.stage != RatStage.YoungRat &&
                    mother.ageDays < mother.breedingEndAgeDays && father.ageDays < father.breedingEndAgeDays &&
                    FindPendingPregnancyForMother(save, mother.id) == null;
                if (valid && UnityEngine.Random.value <= Mathf.Clamp(session.successChance, 0f, GameConfig.DedicatedBreedingSuccessCap))
                {
                    CreatePregnancy(save, mother, father, gameTime);
                    session.conceptionSucceeded = true;
                }
                else
                {
                    session.conceptionSucceeded = false;
                }
                session.status = "finished";
                session.resolvedAt = gameTime;
                resolved.Add(session);
            }
            if (resolved.Count > 0) InvalidateReproductiveStateIndexes(save);
            return resolved.Count;
        }

        public static void CancelDedicatedSessionsForRat(ColonySaveData save, string ratId, long gameTime)
        {
            if (save == null || string.IsNullOrEmpty(ratId)) return;
            save.EnsureLists();
            foreach (var session in save.breedingSessions)
            {
                if (session == null || session.status != "active" ||
                    (session.motherId != ratId && session.fatherId != ratId)) continue;
                session.status = "cancelled";
                session.resolvedAt = gameTime;
                session.conceptionSucceeded = false;
            }
            InvalidateReproductiveStateIndexes(save);
        }

        public static bool FinishPregnancy(ColonySaveData save, string pregnancyId, long gameTime, out LitterData litter, out string reason)
        {
            return FinishPregnancyInternal(save, pregnancyId, gameTime, true, out litter, out reason);
        }

        /// <summary>
        /// Creates/finalizes a birth without intermediate storage writes. The caller must
        /// persist the complete colony state, including any announcement or naming queue,
        /// before returning control to the browser event loop.
        /// </summary>
        public static bool FinishPregnancyForBatch(
            ColonySaveData save,
            string pregnancyId,
            long gameTime,
            out LitterData litter,
            out string reason)
        {
            return FinishPregnancyInternal(save, pregnancyId, gameTime, false, out litter, out reason);
        }

        private static bool FinishPregnancyInternal(
            ColonySaveData save,
            string pregnancyId,
            long gameTime,
            bool persistDurablePhases,
            out LitterData litter,
            out string reason)
        {
            litter = null;
            reason = string.Empty;
            if (save == null)
            {
                reason = "Save data unavailable.";
                return false;
            }

            PregnancyData pregnancy = null;
            foreach (var item in save.pregnancies)
            {
                if (item != null && item.id == pregnancyId)
                {
                    pregnancy = item;
                    break;
                }
            }
            if (pregnancy == null || pregnancy.status != "pending")
            {
                reason = "No pending pregnancy found.";
                return false;
            }

            pregnancy.birthAttemptCount++;
            pregnancy.lastBirthAttemptAt = gameTime;

            var mother = FindRat(save, pregnancy.motherId);
            var father = FindRat(save, pregnancy.fatherId);
            if (mother == null || father == null)
            {
                reason = "Parent records are missing.";
                MarkBirthBlocked(pregnancy, gameTime, reason);
                return false;
            }

            // A prepared litter is the durable middle state of the birth
            // transaction. It lets a browser save failure retry the final
            // pregnancy-state commit without generating another litter or
            // another set of pups.
            if (!string.IsNullOrEmpty(pregnancy.litterId))
            {
                LitterData prepared = FindLitterById(save, pregnancy.litterId);
                if (prepared == null || !PreparedLitterIsComplete(save, prepared, pregnancy.expectedLitterSize))
                {
                    reason = "A prepared litter is incomplete; pregnancy retained for retry.";
                    MarkBirthBlocked(pregnancy, gameTime, reason);
                    return false;
                }
                litter = prepared;
                if (!FinalizePreparedBirth(save, pregnancy, mother, father, litter, gameTime,
                    persistDurablePhases, out reason))
                    return false;
                return true;
            }

            if (pregnancy.expectedLitterSize <= 0)
                pregnancy.expectedLitterSize = CalculateExpectedLitterSize(mother, father);
            int litterSize = Mathf.Clamp(
                pregnancy.expectedLitterSize,
                GameConfig.MinimumLitterSize,
                GameConfig.MaximumLitterSizeAtFullFertility);
            string litterId = ColonyFactory.NewId("litter");
            int generation = Math.Max(mother.generation, father.generation) + 1;
            litter = new LitterData
            {
                id = litterId,
                motherId = mother.id,
                fatherId = father.id,
                size = litterSize,
                generation = generation,
                birthTimestamp = gameTime,
                weaningTimestamp = gameTime + (long)(GameConfig.WeaningDays * GameConfig.GameDayMs),
            };
            LitterNameSystem.AssignName(save, litter);

            var createdPups = new List<RatData>(litterSize);
            try
            {
                for (int i = 0; i < litterSize; i++)
                {
                    RatSex sex = UnityEngine.Random.Range(0, 2) == 0 ? RatSex.Female : RatSex.Male;
                    string pupId = ColonyFactory.NewId("rat");
                    var pup = ColonyFactory.CreateRat(
                        pupId,
                        RatNameSystem.GenerateAvailableName(save, pupId, sex, gameTime),
                        sex,
                        gameTime,
                        generation,
                        GeneticsSystem.InheritGenotype(mother.genotype, father.genotype, gameTime),
                        GeneticsSystem.InheritTraits(mother.traits, father.traits),
                        RatStage.Pinkie);
                    pup.motherId = mother.id;
                    pup.fatherId = father.id;
                    pup.litterId = litterId;
                    pup.birthTimestamp = gameTime;
                    pup.growthTimestamp = gameTime;
                    pup.ageDays = 0f;
                    pup.developerGrowthOverride = false;
                    pup.growthAnchorAgeDays = 0f;
                    pup.markingFamily = GeneticsSystem.ResolveOffspringMarkingFamily(
                        mother, father, pup.genotype, pup.id);
                    // Preserve the stable coat-family appearance through
                    // inheritance. The variant is chosen from both parents and
                    // the pup's stable ID, so a UI refresh or reload never
                    // rerolls an offspring's visible color.
                    pup.coatColorVariant = GeneticsSystem.ResolveOffspringCoatColorVariant(
                        mother, father, pup.genotype, pup.id);
                    pup.coatTone = GeneticsSystem.DefaultCoatTone(pup.id, pup.genotype);
                    // A litter always stays with its mother in the habitat where
                    // the birth occurred. Nursery is no longer a valid runtime
                    // destination; Pairing therefore remains a valid nest
                    // habitat, while ordinary litters remain in Female/Breeding
                    // as appropriate.
                    pup.enclosure = mother.enclosure == RatEnclosure.Nursery
                        ? EnclosureSystem.StandardEnclosure(save, mother)
                        : mother.enclosure;
                    if (pup.enclosure == RatEnclosure.Nursery)
                        pup.enclosure = mother.sex == RatSex.Male
                            ? RatEnclosure.MaleColony
                            : RatEnclosure.FemaleColony;
                    pup.pairingHabitatAssigned = pup.enclosure == RatEnclosure.Pairing;
                    GeneticsSystem.EnsureCoatAppearance(pup);
                    pup.phenotype = GeneticsSystem.DerivePhenotype(RatStage.Pinkie, pup.genotype,
                        pup.coatColorVariant, pup.coatTone);
                    GeneticsSystem.ApplyMarkingFamily(pup.phenotype, pup.markingFamily);
                    RatActivitySystem.SetCurrent(save, pup, "nest", "Resting in nest", gameTime);
                    save.rats.Add(pup);
                    save.ratIds.Add(pup.id);
                    // Parent IDs are set before naming so the only automatic
                    // Jr/Roman suffixes that can be produced are direct-lineage
                    // names. Otherwise the normal stable, non-repeating pool
                    // is used.
                    RatNameSystem.EnsureBirthName(save, pup, gameTime);
                    createdPups.Add(pup);
                    litter.pupIds.Add(pup.id);
                }
            }
            catch (Exception exception)
            {
                foreach (RatData pup in createdPups)
                {
                    save.rats.Remove(pup);
                    save.ratIds.Remove(pup.id);
                }
                reason = "Litter creation failed and the pregnancy was retained: " + exception.Message;
                MarkBirthBlocked(pregnancy, gameTime, reason);
                return false;
            }

            // Commit the litter and its pups while the pregnancy remains
            // pending. This save is the durable proof that the litter exists.
            pregnancy.litterId = litterId;
            pregnancy.birthCommitState = 1;
            pregnancy.birthFailureReason = string.Empty;
            save.litters.Add(litter);
            InvalidateReproductiveStateIndexes(save);
            if (persistDurablePhases && !SaveSystem.Save(save))
            {
                reason = "Litter was created but could not be saved; retrying birth.";
                MarkBirthBlocked(pregnancy, gameTime, reason);
                return false;
            }

            if (!FinalizePreparedBirth(save, pregnancy, mother, father, litter, gameTime,
                persistDurablePhases, out reason))
                return false;
            return true;
        }

        private static bool FinalizePreparedBirth(
            ColonySaveData save,
            PregnancyData pregnancy,
            RatData mother,
            RatData father,
            LitterData litter,
            long gameTime,
            bool persistImmediately,
            out string reason)
        {
            reason = string.Empty;
            string oldMotherPregnancyId = mother.pregnancyId;
            bool oldMotherNursing = mother.nursing;
            long oldMotherNursingUntil = mother.nursingUntil;
            long oldMotherRecoveryUntil = mother.recoveryUntil;
            long oldMotherCooldown = mother.breedingCooldownUntil;
            ReproductiveState oldMotherState = mother.reproductiveState;
            string oldFatherPregnancyId = father.pregnancyId;
            ReproductiveState oldFatherState = father.reproductiveState;
            long oldFatherCooldown = father.breedingCooldownUntil;

            pregnancy.status = "finished";
            InvalidateReproductiveStateIndexes(save);
            pregnancy.finishedAt = gameTime;
            pregnancy.birthCommitState = 2;
            pregnancy.birthFailureReason = string.Empty;
            mother.pregnancyId = null;
            father.pregnancyId = null;
            mother.nursing = true;
            mother.nursingUntil = litter.weaningTimestamp;
            // Recovery starts on the birth frame. It must not be extended by
            // the independent 21-day weaning or 42-day sale timers.
            mother.recoveryUntil = gameTime + (long)(GameConfig.RecoveryDays * GameConfig.GameDayMs);
            mother.reproductiveState = ReproductiveState.Recovery;
            father.reproductiveState = ReproductiveState.Fertile;
            mother.breedingCooldownUntil = gameTime + GameConfig.BreedingCooldownMs;
            father.breedingCooldownUntil = gameTime + GameConfig.BreedingCooldownMs;
            RatActivitySystem.SetCurrent(save, mother, "nursing", "Nursing", gameTime);
            RatActivitySystem.Record(save, mother, "birth", "Giving birth", gameTime, "Giving birth");
            RatActivitySystem.Record(save, mother, "caring", "Caring for pinkies", gameTime, "Caring for pinkies");
            RatActivitySystem.SetCurrent(save, father, "exploring", "Exploring", gameTime);

            // The normal API retains the durable prepared-litter checkpoint and
            // final-state write. The live simulation batches this transaction
            // with its announcement/naming state and performs one atomic save.
            if (!persistImmediately) return true;
            if (SaveSystem.Save(save)) return true;

            // Do not leave an in-memory finished pregnancy when its final
            // commit did not reach browser storage. The prepared litter stays
            // attached so the next pass can retry exactly this pregnancy.
            pregnancy.status = "pending";
            InvalidateReproductiveStateIndexes(save);
            pregnancy.finishedAt = 0L;
            pregnancy.birthCommitState = 1;
            pregnancy.birthFailureReason = "Final birth state could not be saved; retrying.";
            mother.pregnancyId = oldMotherPregnancyId ?? pregnancy.id;
            mother.nursing = oldMotherNursing;
            mother.nursingUntil = oldMotherNursingUntil;
            mother.recoveryUntil = oldMotherRecoveryUntil;
            mother.breedingCooldownUntil = oldMotherCooldown;
            mother.reproductiveState = oldMotherState;
            father.pregnancyId = oldFatherPregnancyId;
            father.reproductiveState = oldFatherState;
            father.breedingCooldownUntil = oldFatherCooldown;
            reason = pregnancy.birthFailureReason;
            return false;
        }

        private static LitterData FindLitterById(ColonySaveData save, string litterId)
        {
            if (save == null || save.litters == null || string.IsNullOrEmpty(litterId)) return null;
            EnsureReproductiveStateIndexes(save);
            return LitterById.TryGetValue(litterId, out LitterData litter) ? litter : null;
        }

        private static bool PreparedLitterIsComplete(ColonySaveData save, LitterData litter, int expectedSize)
        {
            if (litter == null || litter.pupIds == null || litter.pupIds.Count == 0) return false;
            if (expectedSize > 0 && litter.pupIds.Count != expectedSize) return false;
            foreach (string pupId in litter.pupIds)
                if (FindHistoricalRat(save, pupId) == null) return false;
            return true;
        }

        public static bool MarkBirthBlocked(PregnancyData pregnancy, long gameTime, string reason)
        {
            if (pregnancy == null || string.IsNullOrEmpty(reason)) return false;
            bool changed = !string.Equals(pregnancy.birthFailureReason, reason, StringComparison.Ordinal);
            pregnancy.lastBirthAttemptAt = gameTime;
            pregnancy.birthFailureReason = reason;
            if (changed)
                UnityEngine.Debug.LogWarning("[Rat Habitat] Birth retry " + pregnancy.id + ": " + reason);
            return changed;
        }

        /// <summary>
        /// Returns due pregnancies without resolving them. GameBootstrap uses
        /// this list to start/resume each mother's nest approach and only calls
        /// FinishPregnancy after the live behavior confirms arrival.
        /// </summary>
        public static List<PregnancyData> GetDuePendingPregnancies(ColonySaveData save, long gameTime)
        {
            var due = new List<PregnancyData>();
            if (save == null || save.pregnancies == null) return due;
            List<PregnancyData> pending = GetIndexedPendingPregnancies(save);
            for (int index = 0; index < pending.Count; index++)
            {
                PregnancyData pregnancy = pending[index];
                if (pregnancy == null || pregnancy.status != "pending") continue;
                if (pregnancy.dueAt <= gameTime) due.Add(pregnancy);
            }
            return due;
        }

        public static void CancelPregnanciesForRat(ColonySaveData save, string ratId, long gameTime)
        {
            if (save == null || string.IsNullOrEmpty(ratId)) return;
            foreach (var pregnancy in save.pregnancies)
            {
                if (pregnancy == null || pregnancy.status != "pending" ||
                    (pregnancy.motherId != ratId && pregnancy.fatherId != ratId)) continue;
                pregnancy.status = "cancelled";
                pregnancy.finishedAt = gameTime;
                var mother = FindRat(save, pregnancy.motherId);
                var father = FindRat(save, pregnancy.fatherId);
                if (mother != null && mother.pregnancyId == pregnancy.id)
                {
                    mother.pregnancyId = null;
                    mother.reproductiveState = ReproductiveState.Recovery;
                    mother.recoveryUntil = Math.Max(mother.recoveryUntil, gameTime);
                }
                if (father != null && father.pregnancyId == pregnancy.id)
                {
                    father.pregnancyId = null;
                    father.reproductiveState = ReproductiveState.Fertile;
                }
            }
            InvalidateReproductiveStateIndexes(save);
        }
    }

    public struct PairingSpeedDiagnosticsSnapshot
    {
        public long checks;
        public long eligiblePairsFound;
        public long pairingAttempts;
        public long cooldownBlockedChecks;
        public long cooldownBlockedCandidates;
        public long capacityBlockedAttempts;
        public long conceptionRolls;
        public long successfulConceptions;
        public long failedConceptionRolls;
        public long skippedChecks;
        public long recoveredFertileWindows;
    }

    public struct PairingDiagnosticsSnapshot
    {
        public long checks;
        public long checksWithEligiblePair;
        public long checksWithoutEligiblePair;
        public long pairSelections;
        public long conceptionRolls;
        public long failedConceptionRolls;
        public long successfulConceptions;
        public long blockedResolutionAttempts;
        public long blockedPregnancyCandidates;
        public long blockedRecoveryCandidates;
        public long blockedCooldownCandidates;
        public long blockedFertileWindowCandidates;
        public long blockedAgeCandidates;
        public long blockedSessionCandidates;
        public long blockedOtherCandidates;
        public long capacityBlockedMoves;
        public long routeFailures;
        public int lastEligibleMales;
        public int lastEligibleFemales;
        public int lastEligiblePairCombinations;
        public int lastPairingOccupants;
        public int lastPairingCapacity;
        public string lastNoPairReason;
    }

    /// <summary>
    /// Handles the real-time automatic pairing pass. Rat assignment and
    /// pregnancy records remain owned by RatData/BreedingSystem; this class
    /// only chooses one-to-one eligible candidates and starts pregnancies.
    /// </summary>
    public static class PairingHabitatSystem
    {
        // Reuse the main-thread scratch lists instead of allocating each check.
        private static readonly List<RatData> MaleCandidates = new List<RatData>();
        private static readonly List<RatData> FemaleCandidates = new List<RatData>();
        private static PairingDiagnosticsSnapshot diagnostics;
        // Fixed three-speed buckets avoid allocations in the periodic pairing
        // loop. The selected saved speed is recorded at each decision point.
        private static readonly PairingSpeedDiagnosticsSnapshot[] diagnosticsBySpeed =
            new PairingSpeedDiagnosticsSnapshot[3];

        public static PairingDiagnosticsSnapshot Diagnostics { get { return diagnostics; } }

        public static void ResetDiagnostics()
        {
            diagnostics = new PairingDiagnosticsSnapshot();
            for (int index = 0; index < diagnosticsBySpeed.Length; index++)
                diagnosticsBySpeed[index] = new PairingSpeedDiagnosticsSnapshot();
        }

        public static void RecordCapacityBlocked(string reason)
        {
            RecordCapacityBlocked(reason, GrowthSystem.RuntimeSimulationSpeed);
        }

        public static void RecordCapacityBlocked(string reason, float speed)
        {
            diagnostics.capacityBlockedMoves++;
            diagnosticsBySpeed[SpeedBucketIndex(speed)].capacityBlockedAttempts++;
            diagnostics.lastNoPairReason = string.IsNullOrEmpty(reason) ? "Pairing Tank capacity is full." : reason;
        }

        public static void RecordRouteFailure()
        {
            diagnostics.routeFailures++;
        }

        public static void RecordSkippedChecks(float speed, long skippedChecks)
        {
            if (skippedChecks <= 0L) return;
            diagnosticsBySpeed[SpeedBucketIndex(speed)].skippedChecks += skippedChecks;
        }

        public static PairingSpeedDiagnosticsSnapshot DiagnosticsForSpeed(float speed)
        {
            return diagnosticsBySpeed[SpeedBucketIndex(speed)];
        }

        private static int SpeedBucketIndex(float speed)
        {
            switch ((int)GrowthSystem.NormalizeSpeed(speed))
            {
                case 2: return 1;
                case 3: return 2;
                default: return 0;
            }
        }

        private static float SaveSimulationSpeed(ColonySaveData save)
        {
            return save == null || save.clock == null
                ? GrowthSystem.RuntimeSimulationSpeed
                : GrowthSystem.NormalizeSpeed(save.clock.speed);
        }

        public static string DiagnosticsSummary(ColonySaveData save)
        {
            PairingDiagnosticsSnapshot value = diagnostics;
            int capacity = UpgradeSystem.PairingHabitatCapacity(save);
            int occupants = CountPairingOccupants(save);
            string lastReason = string.IsNullOrEmpty(value.lastNoPairReason) ? "none" : value.lastNoPairReason;
            return "Pairing checks " + value.checks + " • eligible checks " + value.checksWithEligiblePair +
                " • no-pair checks " + value.checksWithoutEligiblePair + " • last eligible M/F " +
                value.lastEligibleMales + "/" + value.lastEligibleFemales + " (" +
                value.lastEligiblePairCombinations + " combinations) • Pairing Tank " + occupants + "/" + capacity +
                " • pair selections " + value.pairSelections + " • conception rolls " + value.conceptionRolls +
                " (failed " + value.failedConceptionRolls + ", successful " + value.successfulConceptions +
                ") • blocked before roll " + value.blockedResolutionAttempts + " • candidate blocks pregnancy/recovery/cooldown/window/age/session/other " +
                value.blockedPregnancyCandidates + "/" + value.blockedRecoveryCandidates + "/" +
                value.blockedCooldownCandidates + "/" + value.blockedFertileWindowCandidates + "/" +
                value.blockedAgeCandidates + "/" + value.blockedSessionCandidates + "/" +
                value.blockedOtherCandidates + " • capacity blocks " + value.capacityBlockedMoves +
                " • route failures " + value.routeFailures + " • last no-pair/block reason: " + lastReason +
                "\nPer-speed • " + FormatSpeedDiagnostics("1x", diagnosticsBySpeed[0]) +
                " • " + FormatSpeedDiagnostics("2x", diagnosticsBySpeed[1]) +
                " • " + FormatSpeedDiagnostics("3x", diagnosticsBySpeed[2]);
        }

        private static string FormatSpeedDiagnostics(string label, PairingSpeedDiagnosticsSnapshot value)
        {
            return label + " checks/eligible pairs/attempts=" + value.checks + "/" +
                value.eligiblePairsFound + "/" + value.pairingAttempts +
                " cooldown-blocked checks/candidates=" + value.cooldownBlockedChecks + "/" +
                value.cooldownBlockedCandidates + " capacity blocks=" +
                value.capacityBlockedAttempts + " rolls/conceptions=" + value.conceptionRolls + "/" +
                value.successfulConceptions + " failed=" + value.failedConceptionRolls +
                " skipped checks=" + value.skippedChecks +
                " recovered windows=" + value.recoveredFertileWindows;
        }

        /// <summary>
        /// Selects one eligible male/female pair without resolving conception.
        /// The presentation layer uses this boundary to stage a physical
        /// approach and sniffing interaction before the existing conception
        /// calculation is made.
        /// </summary>
        public static bool TryChoosePair(ColonySaveData save, long gameTime, out RatData male, out RatData female)
        {
            return TryChoosePair(save, gameTime, gameTime, SaveSimulationSpeed(save), out male, out female);
        }

        public static bool TryChoosePair(
            ColonySaveData save, long opportunityWindowStartGameTime, long gameTime,
            float speed, out RatData male, out RatData female)
        {
            diagnostics.checks++;
            PairingSpeedDiagnosticsSnapshot speedDiagnostics =
                diagnosticsBySpeed[SpeedBucketIndex(speed)];
            speedDiagnostics.checks++;
            diagnosticsBySpeed[SpeedBucketIndex(speed)] = speedDiagnostics;
            long cooldownCandidatesBeforeCheck = diagnostics.blockedCooldownCandidates;
            male = null;
            female = null;
            MaleCandidates.Clear();
            FemaleCandidates.Clear();
            diagnostics.lastEligibleMales = 0;
            diagnostics.lastEligibleFemales = 0;
            diagnostics.lastEligiblePairCombinations = 0;
            diagnostics.lastPairingOccupants = CountPairingOccupants(save);
            diagnostics.lastPairingCapacity = UpgradeSystem.PairingHabitatCapacity(save);
            diagnostics.lastNoPairReason = string.Empty;
            if (save == null)
            {
                diagnostics.checksWithoutEligiblePair++;
                diagnostics.lastNoPairReason = "No colony save is loaded.";
                return false;
            }
            // Maintenance/load owns the full reproductive reconciliation.
            // Candidate eligibility below is timestamp-authoritative and
            // uses the cached pregnancy/session indexes; a pairing attempt
            // must not trigger birth recovery or rebuild all colony history.
            foreach (var rat in save.rats)
            {
                if (rat == null || rat.removalDisposition != RatRemovalDisposition.None ||
                    (rat.stage != RatStage.Adult && rat.stage != RatStage.Mature) ||
                    rat.enclosure != RatEnclosure.Pairing) continue;

                BreedingSystem.ReproductiveStatus reproductiveStatus =
                    BreedingSystem.GetReproductiveStatus(save, rat, gameTime);
                string reason = reproductiveStatus.eligibilityReason;
                if (!reproductiveStatus.canBreed)
                {
                    bool recoveredWindow = rat.sex == RatSex.Female &&
                        reproductiveStatus.state == ReproductiveState.Fertile &&
                        !string.IsNullOrEmpty(reason) &&
                        reason.StartsWith("Outside the fertile window", StringComparison.Ordinal) &&
                        BreedingSystem.FertileWindowOverlapsInterval(
                            rat, opportunityWindowStartGameTime, gameTime);
                    if (!recoveredWindow)
                    {
                        RecordCandidateBlock(reason, speed);
                        continue;
                    }
                    speedDiagnostics = diagnosticsBySpeed[SpeedBucketIndex(speed)];
                    speedDiagnostics.recoveredFertileWindows++;
                    diagnosticsBySpeed[SpeedBucketIndex(speed)] = speedDiagnostics;
                }
                if (rat.sex == RatSex.Male) MaleCandidates.Add(rat);
                else if (rat.sex == RatSex.Female) FemaleCandidates.Add(rat);
            }

            diagnostics.lastEligibleMales = MaleCandidates.Count;
            diagnostics.lastEligibleFemales = FemaleCandidates.Count;
            diagnostics.lastEligiblePairCombinations = MaleCandidates.Count * FemaleCandidates.Count;
            speedDiagnostics = diagnosticsBySpeed[SpeedBucketIndex(speed)];
            speedDiagnostics.eligiblePairsFound += diagnostics.lastEligiblePairCombinations;
            diagnosticsBySpeed[SpeedBucketIndex(speed)] = speedDiagnostics;
            Shuffle(MaleCandidates);
            Shuffle(FemaleCandidates);
            int pairCount = Mathf.Min(MaleCandidates.Count, FemaleCandidates.Count);
            if (pairCount <= 0)
            {
                if (diagnostics.blockedCooldownCandidates > cooldownCandidatesBeforeCheck)
                {
                    speedDiagnostics = diagnosticsBySpeed[SpeedBucketIndex(speed)];
                    speedDiagnostics.cooldownBlockedChecks++;
                    diagnosticsBySpeed[SpeedBucketIndex(speed)] = speedDiagnostics;
                }
                diagnostics.checksWithoutEligiblePair++;
                diagnostics.lastNoPairReason = NoPairReason(MaleCandidates.Count, FemaleCandidates.Count);
                return false;
            }

            diagnostics.checksWithEligiblePair++;
            diagnostics.pairSelections++;
            speedDiagnostics = diagnosticsBySpeed[SpeedBucketIndex(speed)];
            speedDiagnostics.pairingAttempts++;
            diagnosticsBySpeed[SpeedBucketIndex(speed)] = speedDiagnostics;
            male = MaleCandidates[0];
            female = FemaleCandidates[0];
            return true;
        }

        /// <summary>
        /// Resolves one already-staged approach. This deliberately preserves
        /// the previous Pairing Habitat fertility/conception calculation and
        /// only runs it after the visual interaction has completed.
        /// </summary>
        public static bool ResolvePair(
            ColonySaveData save,
            RatData female,
            RatData male,
            long gameTime,
            float pregnancyChance,
            out bool conceptionSucceeded,
            out string reason)
        {
            return ResolvePair(save, female, male, gameTime, pregnancyChance, false,
                out conceptionSucceeded, out reason, out _);
        }

        /// <summary>
        /// Resolves a pairing after the physical interaction has begun. The
        /// ordinary overload requires the female to be fertile at the exact
        /// resolution time. A committed interaction may pass
        /// allowFertilityWindowElapsed when the female was validated at the
        /// moment the interaction started; an estrous window ending during
        /// that same interaction must not turn it into a second attempt.
        /// All other biological and habitat checks remain live.
        /// </summary>
        public static bool ResolvePair(
            ColonySaveData save,
            RatData female,
            RatData male,
            long gameTime,
            float pregnancyChance,
            bool allowFertilityWindowElapsed,
            out bool conceptionSucceeded,
            out string reason)
        {
            PregnancyData ignoredPregnancy;
            return ResolvePair(save, female, male, gameTime, pregnancyChance,
                allowFertilityWindowElapsed, out conceptionSucceeded, out reason,
                out ignoredPregnancy);
        }

        public static bool ResolvePair(
            ColonySaveData save,
            RatData female,
            RatData male,
            long gameTime,
            float pregnancyChance,
            bool allowFertilityWindowElapsed,
            out bool conceptionSucceeded,
            out string reason,
            out PregnancyData createdPregnancy)
        {
            conceptionSucceeded = false;
            reason = string.Empty;
            createdPregnancy = null;
            float attemptSpeed = SaveSimulationSpeed(save);
            if (save == null || female == null || male == null)
            {
                reason = "The pairing rats are no longer available.";
                diagnostics.blockedResolutionAttempts++;
                return false;
            }
            if (female.enclosure != RatEnclosure.Pairing || male.enclosure != RatEnclosure.Pairing)
            {
                reason = "Both rats must be in the Pairing Tank.";
                diagnostics.blockedResolutionAttempts++;
                diagnostics.blockedOtherCandidates++;
                return false;
            }

            string femaleReason;
            string maleReason;
            if (!CanResolveParticipant(save, female, gameTime, allowFertilityWindowElapsed, out femaleReason))
            {
                reason = ColonyFactory.DisplayName(female) + ": " + femaleReason;
                RecordResolutionBlock(femaleReason, attemptSpeed);
                return false;
            }
            if (!CanResolveParticipant(save, male, gameTime, false, out maleReason))
            {
                reason = ColonyFactory.DisplayName(male) + ": " + maleReason;
                RecordResolutionBlock(maleReason, attemptSpeed);
                return false;
            }

            float chance = BreedingSystem.CalculateConceptionChance(
                female,
                male,
                pregnancyChance,
                0f,
                Mathf.Clamp01(pregnancyChance));
            diagnostics.conceptionRolls++;
            PairingSpeedDiagnosticsSnapshot speedDiagnostics =
                diagnosticsBySpeed[SpeedBucketIndex(attemptSpeed)];
            speedDiagnostics.conceptionRolls++;
            if (chance <= 0f || UnityEngine.Random.value > chance)
            {
                // A failed resolution is still a real biological attempt.
                // Persist a cooldown on both participants so the next
                // scheduled pairing pass cannot immediately select the same
                // pair again or replay the interaction every frame.
                ApplyPairingAttemptCooldown(female, male, gameTime);
                diagnostics.failedConceptionRolls++;
                speedDiagnostics.failedConceptionRolls++;
                diagnosticsBySpeed[SpeedBucketIndex(attemptSpeed)] = speedDiagnostics;
                return true;
            }

            // CanResolveParticipant performed the complete live validation.
            // Create the pregnancy directly here so a committed interaction
            // can finish after the female's fertile window closes without
            // re-running the pre-interaction window gate.
            createdPregnancy = BreedingSystem.CreatePregnancy(save, female, male, gameTime);
            reason = string.Empty;
            // Apply this immediately rather than waiting until birth. The
            // pregnancy state blocks the mother; the cooldown also prevents
            // the same pair from being staged again during the current cycle.
            ApplyPairingAttemptCooldown(female, male, gameTime);
            conceptionSucceeded = true;
            diagnostics.successfulConceptions++;
            speedDiagnostics.successfulConceptions++;
            diagnosticsBySpeed[SpeedBucketIndex(attemptSpeed)] = speedDiagnostics;
            return true;
        }

        private static void RecordCandidateBlock(string reason, float speed)
        {
            switch (ClassifyBlock(reason))
            {
                case PairingBlockKind.Pregnancy: diagnostics.blockedPregnancyCandidates++; break;
                case PairingBlockKind.Recovery: diagnostics.blockedRecoveryCandidates++; break;
                case PairingBlockKind.Cooldown:
                    diagnostics.blockedCooldownCandidates++;
                    diagnosticsBySpeed[SpeedBucketIndex(speed)].cooldownBlockedCandidates++;
                    break;
                case PairingBlockKind.FertileWindow: diagnostics.blockedFertileWindowCandidates++; break;
                case PairingBlockKind.Age: diagnostics.blockedAgeCandidates++; break;
                case PairingBlockKind.Session: diagnostics.blockedSessionCandidates++; break;
                default: diagnostics.blockedOtherCandidates++; break;
            }
        }

        private static void RecordResolutionBlock(string reason, float speed)
        {
            diagnostics.blockedResolutionAttempts++;
            RecordCandidateBlock(reason, speed);
        }

        private enum PairingBlockKind { Other, Pregnancy, Recovery, Cooldown, FertileWindow, Age, Session }

        private static PairingBlockKind ClassifyBlock(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return PairingBlockKind.Other;
            if (reason.IndexOf("pregnan", StringComparison.OrdinalIgnoreCase) >= 0) return PairingBlockKind.Pregnancy;
            if (reason.IndexOf("recover", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("nursing", StringComparison.OrdinalIgnoreCase) >= 0) return PairingBlockKind.Recovery;
            if (reason.IndexOf("cooldown", StringComparison.OrdinalIgnoreCase) >= 0) return PairingBlockKind.Cooldown;
            if (reason.IndexOf("fertile window", StringComparison.OrdinalIgnoreCase) >= 0) return PairingBlockKind.FertileWindow;
            if (reason.IndexOf("age", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("immature", StringComparison.OrdinalIgnoreCase) >= 0) return PairingBlockKind.Age;
            if (reason.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("occupied", StringComparison.OrdinalIgnoreCase) >= 0) return PairingBlockKind.Session;
            return PairingBlockKind.Other;
        }

        private static string NoPairReason(int eligibleMales, int eligibleFemales)
        {
            if (eligibleMales == 0 && eligibleFemales == 0) return "No eligible males or females in Pairing Tank.";
            if (eligibleMales == 0) return "No eligible male in Pairing Tank.";
            return "No eligible female in Pairing Tank.";
        }

        private static int CountPairingOccupants(ColonySaveData save)
        {
            if (save == null || save.rats == null) return 0;
            int count = 0;
            foreach (RatData rat in save.rats)
                if (rat != null && rat.enclosure == RatEnclosure.Pairing) count++;
            return count;
        }

        private static bool CanResolveParticipant(
            ColonySaveData save,
            RatData rat,
            long gameTime,
            bool allowFertilityWindowElapsed,
            out string reason)
        {
            if (BreedingSystem.IsBreedEligible(save, rat, gameTime, out reason)) return true;
            if (!allowFertilityWindowElapsed || rat == null || rat.sex != RatSex.Female) return false;

            // GetReproductiveStatus remains authoritative here. The only
            // unavailable result that a committed interaction may carry past
            // its start is the female's live fertile window having elapsed.
            BreedingSystem.ReproductiveStatus status = BreedingSystem.GetReproductiveStatus(save, rat, gameTime);
            if (status.state == ReproductiveState.Fertile &&
                !string.IsNullOrEmpty(status.eligibilityReason) &&
                status.eligibilityReason.StartsWith("Outside the fertile window", StringComparison.Ordinal))
            {
                reason = string.Empty;
                return true;
            }
            reason = status.eligibilityReason;
            return false;
        }

        public static void ApplyPairingAttemptCooldown(RatData female, RatData male, long gameTime)
        {
            long until = gameTime + GameConfig.PairingAttemptCooldownMs;
            if (female != null) female.breedingCooldownUntil = Math.Max(female.breedingCooldownUntil, until);
            if (male != null) male.breedingCooldownUntil = Math.Max(male.breedingCooldownUntil, until);
        }

        public static int Evaluate(ColonySaveData save, long gameTime, float pregnancyChance)
        {
            RatData male;
            RatData female;
            if (!TryChoosePair(save, gameTime, out male, out female)) return 0;

            bool conceptionSucceeded;
            string reason;
            if (!ResolvePair(save, female, male, gameTime, pregnancyChance, out conceptionSucceeded, out reason)) return 0;
            return conceptionSucceeded ? 1 : 0;
        }

        private static void Shuffle(List<RatData> rats)
        {
            for (int index = rats.Count - 1; index > 0; index--)
            {
                int swapIndex = UnityEngine.Random.Range(0, index + 1);
                RatData value = rats[index];
                rats[index] = rats[swapIndex];
                rats[swapIndex] = value;
            }
        }
    }
}
