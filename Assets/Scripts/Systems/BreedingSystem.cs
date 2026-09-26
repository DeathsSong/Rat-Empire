using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RatHabitat
{
    public static class BreedingSystem
    {
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
            RatData active = FindRat(save, id);
            if (active != null || save == null || string.IsNullOrEmpty(id)) return active;
            save.EnsureLists();
            foreach (var rat in save.retiredRats)
            {
                if (rat != null && rat.id == id) return rat;
            }
            return null;
        }

        public static PregnancyData FindPendingPregnancy(ColonySaveData save, string ratId)
        {
            if (save == null) return null;
            foreach (var pregnancy in save.pregnancies)
            {
                if (pregnancy != null && pregnancy.status == "pending" &&
                    (pregnancy.motherId == ratId || pregnancy.fatherId == ratId)) return pregnancy;
            }
            return null;
        }

        // A pregnancy record references both parents for history, but only the
        // mother is biologically pregnant. Keep this lookup separate from the
        // relationship lookup above so fathers are not blocked from breeding.
        public static PregnancyData FindPendingPregnancyForMother(ColonySaveData save, string ratId)
        {
            if (save == null || string.IsNullOrEmpty(ratId)) return null;
            foreach (var pregnancy in save.pregnancies)
            {
                if (pregnancy != null && pregnancy.status == "pending" && pregnancy.motherId == ratId)
                    return pregnancy;
            }
            return null;
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
                foreach (var pregnancy in save.pregnancies)
                {
                    if (pregnancy != null && pregnancy.status == "pending" &&
                        pregnancy.id == rat.pregnancyId && pregnancy.motherId == rat.id)
                        return pregnancy;
                }
            }

            // Legacy saves can contain the correct pending mother record but
            // no active ID on the RatData object yet. This remains record-based
            // rather than trusting a stale ReproductiveState/display label.
            return FindPendingPregnancyForMother(save, rat.id);
        }

        public static PregnancyData FindPregnancyForLitter(ColonySaveData save, string litterId)
        {
            if (save == null || string.IsNullOrEmpty(litterId)) return null;
            foreach (var pregnancy in save.pregnancies)
            {
                if (pregnancy != null && pregnancy.status == "finished" && pregnancy.litterId == litterId)
                    return pregnancy;
            }
            return null;
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
            save.EnsureLists();
            foreach (var session in save.breedingSessions)
            {
                if (session == null || session.status != "active") continue;
                if (session.motherId == ratId || session.fatherId == ratId) return session;
            }
            return null;
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
            RefreshReproductiveStates(save, gameTime);
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

            DedicatedBreedingSessionData session = FindActiveDedicatedSession(save, rat.id);
            if (session != null)
            {
                string label = "Breeding session — ends in " + FormatDuration(Math.Max(0L, session.endsAt - gameTime));
                return Unavailable(ReproductiveState.Fertile, label, "Occupied by a dedicated breeding session.", session.endsAt);
            }

            PregnancyData pregnancy = FindActivePregnancyForMother(save, rat);
            if (pregnancy != null)
            {
                string label = "Pregnant — birth in " + FormatDuration(Math.Max(0L, pregnancy.dueAt - gameTime));
                return Unavailable(ReproductiveState.Pregnant, label, "Currently pregnant.", pregnancy.dueAt);
            }

            if (rat.nursing || rat.reproductiveState == ReproductiveState.Nursing)
            {
                string label = "Nursing — weaning in " + FormatDuration(Math.Max(0L, rat.nursingUntil - gameTime));
                return Unavailable(ReproductiveState.Nursing, label, "Nursing — weaning in " +
                    FormatDuration(Math.Max(0L, rat.nursingUntil - gameTime)) + ".", rat.nursingUntil);
            }

            if (rat.reproductiveState == ReproductiveState.Recovery)
            {
                string label = "Recovering — fertile again in " + FormatDuration(Math.Max(0L, rat.recoveryUntil - gameTime));
                return Unavailable(ReproductiveState.Recovery, label, label + ".", rat.recoveryUntil);
            }

            // Mature stage begins at one year, but mature rats can still
            // breed during the individualized decline period. Only the
            // persisted breeding-end age is the past-breeding cutoff.
            if (rat.ageDays >= rat.breedingEndAgeDays)
            {
                return Unavailable(ReproductiveState.Infertile, "Past breeding age", "Past breeding age.");
            }

            // The randomized sexualMaturityDays value is authoritative even
            // while the visual life stage already reads Adult.
            if (rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat ||
                rat.ageDays < rat.sexualMaturityDays)
            {
                long remaining = Math.Max(0L, (long)((rat.sexualMaturityDays - rat.ageDays) * GameConfig.GameDayMs));
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
            if (rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat ||
                rat.ageDays < rat.sexualMaturityDays || rat.ageDays >= rat.breedingEndAgeDays) return false;
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
                    return "Breeding unavailable — pregnant";
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
        /// Compares two My Rats entries for the Pregnancy sort. Ascending puts
        /// active pregnancies first and orders them by soonest due date. The
        /// remaining rats use the authoritative reproductive state order so
        /// they remain grouped consistently instead of being classified from
        /// a display string or stale saved label.
        /// </summary>
        public static int ComparePregnancySort(
            ColonySaveData save,
            RatData first,
            RatData second,
            long gameTime,
            bool ascending)
        {
            bool firstPregnant = FindActivePregnancyForMother(save, first) != null;
            bool secondPregnant = FindActivePregnancyForMother(save, second) != null;
            int result;

            if (firstPregnant != secondPregnant)
            {
                // Ascending means pregnant rats have priority at the top.
                result = firstPregnant ? -1 : 1;
            }
            else if (firstPregnant)
            {
                long firstDue = PregnancyDueSortValue(FindActivePregnancyForMother(save, first));
                long secondDue = PregnancyDueSortValue(FindActivePregnancyForMother(save, second));
                result = firstDue.CompareTo(secondDue);
            }
            else
            {
                ReproductiveState firstState = GetReproductiveStatus(save, first, gameTime).state;
                ReproductiveState secondState = GetReproductiveStatus(save, second, gameTime).state;
                result = PregnancyStateSortRank(firstState).CompareTo(PregnancyStateSortRank(secondState));
            }

            if (!ascending) result = -result;
            return result;
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
            if (save == null) return false;
            save.EnsureLists();
            bool changed = false;
            foreach (var rat in save.rats)
            {
                if (rat == null) continue;
                if (ClearLegacyMalePregnancyState(save, rat)) changed = true;
                GrowthSystem.EnsureBiologyDefaults(rat);
                ReproductiveState oldState = rat.reproductiveState;
                PregnancyData pending = FindActivePregnancyForMother(save, rat);
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
                    bool stillNursing = EnclosureSystem.HasDependentPinkies(save, rat.id);
                    if (stillNursing)
                    {
                        rat.nursing = true;
                        rat.reproductiveState = ReproductiveState.Nursing;
                    }
                    else
                    {
                        rat.nursing = false;
                        rat.recoveryUntil = Math.Max(rat.recoveryUntil,
                            gameTime + (long)(GameConfig.RecoveryDays * GameConfig.GameDayMs));
                        rat.reproductiveState = ReproductiveState.Recovery;
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
            RatActivitySystem.SetCurrent(save, parentA, "breeding", "Breeding", gameTime);
            RatActivitySystem.SetCurrent(save, parentB, "breeding", "Breeding", gameTime);
            reason = string.Empty;
            return true;
        }

        public static int ResolveDueDedicatedBreedingSessions(ColonySaveData save, long gameTime, out List<DedicatedBreedingSessionData> resolved)
        {
            resolved = new List<DedicatedBreedingSessionData>();
            if (save == null) return 0;
            save.EnsureLists();
            foreach (var session in save.breedingSessions)
            {
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
        }

        public static bool FinishPregnancy(ColonySaveData save, string pregnancyId, long gameTime, out LitterData litter, out string reason)
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

            var mother = FindRat(save, pregnancy.motherId);
            var father = FindRat(save, pregnancy.fatherId);
            if (mother == null || father == null)
            {
                reason = "Parent records are missing.";
                return false;
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

            for (int i = 0; i < litterSize; i++)
            {
                RatSex sex = UnityEngine.Random.Range(0, 2) == 0 ? RatSex.Female : RatSex.Male;
                string pupId = ColonyFactory.NewId("rat");
                string name = ColonyFactory.GeneratedName(pupId, sex);
                var pup = ColonyFactory.CreateRat(
                    pupId,
                    name,
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
                pup.markingFamily = GeneticsSystem.ResolveOffspringMarkingFamily(mother, father, pup.genotype);
                // Preserve the stable coat-family appearance through
                // inheritance. The variant is chosen from both parents and
                // the pup's stable ID, so a UI refresh or reload never
                // rerolls an offspring's visible color.
                pup.coatColorVariant = GeneticsSystem.ResolveOffspringCoatColorVariant(
                    mother, father, pup.genotype, pup.id);
                pup.coatTone = GeneticsSystem.DefaultCoatTone(pup.id, pup.genotype);
                // A Pairing Habitat litter stays with its mother there for
                // pregnancy, birth, nursing, and growth. Normal pregnancies
                // continue using the existing Nursery assignment.
                pup.enclosure = mother.enclosure == RatEnclosure.Pairing
                    ? RatEnclosure.Pairing
                    : RatEnclosure.Nursery;
                pup.pairingHabitatAssigned = pup.enclosure == RatEnclosure.Pairing;
                GeneticsSystem.EnsureCoatAppearance(pup);
                pup.phenotype = GeneticsSystem.DerivePhenotype(RatStage.Pinkie, pup.genotype,
                    pup.coatColorVariant, pup.coatTone);
                GeneticsSystem.ApplyMarkingFamily(pup.phenotype, pup.markingFamily);
                RatActivitySystem.SetCurrent(save, pup, "nest", "Resting in nest", gameTime);
                save.rats.Add(pup);
                save.ratIds.Add(pup.id);
                litter.pupIds.Add(pup.id);
            }

            pregnancy.status = "finished";
            pregnancy.finishedAt = gameTime;
            pregnancy.litterId = litterId;
            mother.pregnancyId = null;
            father.pregnancyId = null;
            mother.nursing = true;
            mother.nursingUntil = litter.weaningTimestamp;
            mother.recoveryUntil = litter.weaningTimestamp + (long)(GameConfig.RecoveryDays * GameConfig.GameDayMs);
            mother.reproductiveState = ReproductiveState.Nursing;
            father.reproductiveState = ReproductiveState.Fertile;
            mother.breedingCooldownUntil = gameTime + GameConfig.BreedingCooldownMs;
            father.breedingCooldownUntil = gameTime + GameConfig.BreedingCooldownMs;
            RatActivitySystem.SetCurrent(save, mother, "nursing", "Nursing", gameTime);
            RatActivitySystem.Record(save, mother, "birth", "Giving birth", gameTime, "Giving birth");
            RatActivitySystem.Record(save, mother, "caring", "Caring for pinkies", gameTime, "Caring for pinkies");
            RatActivitySystem.SetCurrent(save, father, "exploring", "Exploring", gameTime);
            save.litters.Add(litter);
            return true;
        }

        public static int FinishDuePregnancies(ColonySaveData save, long gameTime, out List<LitterData> newLitters)
        {
            newLitters = new List<LitterData>();
            if (save == null) return 0;
            var pendingIds = new List<string>();
            foreach (var pregnancy in save.pregnancies)
            {
                if (pregnancy != null && pregnancy.status == "pending" && pregnancy.dueAt <= gameTime) pendingIds.Add(pregnancy.id);
            }
            foreach (var id in pendingIds)
            {
                LitterData litter;
                string reason;
                if (FinishPregnancy(save, id, gameTime, out litter, out reason) && litter != null) newLitters.Add(litter);
            }
            return newLitters.Count;
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
        }
    }

    /// <summary>
    /// Handles the real-time automatic pairing pass. Rat assignment and
    /// pregnancy records remain owned by RatData/BreedingSystem; this class
    /// only chooses one-to-one eligible candidates and starts pregnancies.
    /// </summary>
    public static class PairingHabitatSystem
    {
        /// <summary>
        /// Selects one eligible male/female pair without resolving conception.
        /// The presentation layer uses this boundary to stage a physical
        /// approach and sniffing interaction before the existing conception
        /// calculation is made.
        /// </summary>
        public static bool TryChoosePair(ColonySaveData save, long gameTime, out RatData male, out RatData female)
        {
            male = null;
            female = null;
            if (save == null) return false;

            BreedingSystem.RefreshReproductiveStates(save, gameTime);

            var males = new List<RatData>();
            var females = new List<RatData>();
            foreach (var rat in save.rats)
            {
                if (rat == null || (rat.stage != RatStage.Adult && rat.stage != RatStage.Mature) ||
                    rat.enclosure != RatEnclosure.Pairing) continue;

                string reason;
                if (!BreedingSystem.IsBreedEligible(save, rat, gameTime, out reason)) continue;
                if (rat.sex == RatSex.Male) males.Add(rat);
                else if (rat.sex == RatSex.Female) females.Add(rat);
            }

            Shuffle(males);
            Shuffle(females);
            int pairCount = Mathf.Min(males.Count, females.Count);
            if (pairCount <= 0) return false;

            male = males[0];
            female = females[0];
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
            if (save == null || female == null || male == null)
            {
                reason = "The pairing rats are no longer available.";
                return false;
            }
            if (female.enclosure != RatEnclosure.Pairing || male.enclosure != RatEnclosure.Pairing)
            {
                reason = "Both rats must be in the Pairing Habitat.";
                return false;
            }

            string femaleReason;
            string maleReason;
            if (!CanResolveParticipant(save, female, gameTime, allowFertilityWindowElapsed, out femaleReason))
            {
                reason = ColonyFactory.DisplayName(female) + ": " + femaleReason;
                return false;
            }
            if (!CanResolveParticipant(save, male, gameTime, false, out maleReason))
            {
                reason = ColonyFactory.DisplayName(male) + ": " + maleReason;
                return false;
            }

            float chance = BreedingSystem.CalculateConceptionChance(
                female,
                male,
                pregnancyChance,
                0f,
                Mathf.Clamp01(pregnancyChance));
            if (UnityEngine.Random.value > chance)
            {
                // A failed resolution is still a real biological attempt.
                // Persist a cooldown on both participants so the next
                // scheduled pairing pass cannot immediately select the same
                // pair again or replay the interaction every frame.
                ApplyPairingAttemptCooldown(female, male, gameTime);
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
            return true;
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
