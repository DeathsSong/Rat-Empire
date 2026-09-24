using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    public static class BreedingSystem
    {
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
            reason = string.Empty;
            if (rat == null)
            {
                reason = "Rat not found.";
                return false;
            }
            ClearLegacyMalePregnancyState(save, rat);
            GrowthSystem.EnsureBiologyDefaults(rat);
            if (rat.stage != RatStage.Adult)
            {
                reason = rat.stage == RatStage.Senior ? "Past the normal breeding age." : "Adult rats only.";
                return false;
            }
            if (FindPendingPregnancyForMother(save, rat.id) != null ||
                (rat.sex == RatSex.Female && !string.IsNullOrEmpty(rat.pregnancyId)))
            {
                reason = "Currently pregnant.";
                return false;
            }
            if (FindActiveDedicatedSession(save, rat.id) != null)
            {
                reason = "Occupied by a dedicated breeding session.";
                return false;
            }
            if (rat.breedingCooldownUntil > gameTime)
            {
                reason = "On breeding cooldown.";
                return false;
            }
            if (rat.reproductiveState == ReproductiveState.Pregnant ||
                rat.reproductiveState == ReproductiveState.Nursing ||
                rat.reproductiveState == ReproductiveState.Recovery ||
                rat.reproductiveState == ReproductiveState.Infertile ||
                rat.reproductiveState == ReproductiveState.Immature)
            {
                reason = rat.reproductiveState == ReproductiveState.Recovery
                    ? "Resting after nursing."
                    : "Not currently fertile.";
                return false;
            }
            if (rat.ageDays < rat.sexualMaturityDays || rat.ageDays >= rat.breedingEndAgeDays)
            {
                reason = "Outside the breeding age window.";
                return false;
            }
            if (rat.sex == RatSex.Female && !IsInFertileWindow(rat, gameTime))
            {
                reason = "Outside the fertile window.";
                return false;
            }
            return true;
        }

        public static bool IsInFertileWindow(RatData rat, long gameTime)
        {
            if (rat == null || rat.sex != RatSex.Female) return true;
            GrowthSystem.EnsureBiologyDefaults(rat);
            long cycleMs = Math.Max(1L, (long)(GameConfig.EstrousCycleDays * GameConfig.GameDayMs));
            long windowMs = Math.Max(1L, (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs));
            long elapsed = gameTime - rat.estrousCycleAnchorGameTime;
            elapsed %= cycleMs;
            if (elapsed < 0L) elapsed += cycleMs;
            return elapsed < windowMs;
        }

        /// <summary>
        /// Calculates conception from both fertility values. Fertility is
        /// combined geometrically so a very low-fertility parent meaningfully
        /// limits the pair, then curved so low values fall off faster. The
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
            float fertilityCurve = Mathf.Pow(geometricMean, 1.5f);
            float habitatMaximum = Mathf.Clamp(habitatBaseChance + habitatBonus, 0f, cap);
            return Mathf.Clamp01(habitatMaximum * fertilityCurve);
        }

        public static string ConceptionChanceLabel(RatData female, RatData male, float habitatBaseChance, float habitatBonus, float cap)
        {
            return Mathf.RoundToInt(CalculateConceptionChance(female, male, habitatBaseChance, habitatBonus, cap) * 100f) + "%";
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
            minimum = Mathf.Max(1, Mathf.FloorToInt(GameConfig.MinimumLitterSizeAtFullFertility * litterFactor));
            maximum = Mathf.Max(minimum, Mathf.FloorToInt(GameConfig.MaximumLitterSizeAtFullFertility * litterFactor));
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
            if (rat == null) return "Unknown";
            ClearLegacyMalePregnancyState(save, rat);
            GrowthSystem.EnsureBiologyDefaults(rat);
            DedicatedBreedingSessionData session = FindActiveDedicatedSession(save, rat.id);
            if (session != null) return "Breeding session — ends in " + FormatDuration(Math.Max(0L, session.endsAt - gameTime));

            PregnancyData pregnancy = FindPendingPregnancyForMother(save, rat.id);
            if (pregnancy != null)
                return "Pregnant — birth in " + FormatDuration(Math.Max(0L, pregnancy.dueAt - gameTime));
            if (rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat || rat.reproductiveState == ReproductiveState.Immature)
                return "Immature";
            if (rat.stage == RatStage.Senior || rat.ageDays >= rat.breedingEndAgeDays || rat.reproductiveState == ReproductiveState.Infertile)
                return "Past breeding age";
            if (rat.nursing || rat.reproductiveState == ReproductiveState.Nursing)
                return "Nursing — weaning in " + FormatDuration(Math.Max(0L, rat.nursingUntil - gameTime));
            if (rat.reproductiveState == ReproductiveState.Recovery)
                return "Recovery — fertile again in " + FormatDuration(Math.Max(0L, rat.recoveryUntil - gameTime));
            if (rat.sex == RatSex.Female)
            {
                long cycleMs = Math.Max(1L, (long)(GameConfig.EstrousCycleDays * GameConfig.GameDayMs));
                long windowMs = Math.Max(1L, (long)(GameConfig.EstrousFertileWindowDays * GameConfig.GameDayMs));
                long elapsed = gameTime - rat.estrousCycleAnchorGameTime;
                elapsed %= cycleMs;
                if (elapsed < 0L) elapsed += cycleMs;
                if (elapsed < windowMs)
                    return "Fertile — window ends in " + FormatDuration(windowMs - elapsed);
                return "Next fertile window: " + FormatDuration(cycleMs - elapsed);
            }
            return "Fertile";
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
                PregnancyData pending = FindPendingPregnancyForMother(save, rat.id);
                if (rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat)
                {
                    rat.reproductiveState = ReproductiveState.Immature;
                }
                else if (rat.stage == RatStage.Senior || rat.ageDays >= rat.breedingEndAgeDays)
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
                else if (rat.reproductiveState == ReproductiveState.Infertile)
                {
                    rat.reproductiveState = ReproductiveState.Infertile;
                }
                else
                {
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
                reason = first.name + ": " + firstReason;
                return false;
            }
            if (!IsBreedEligible(save, second, gameTime, out secondReason))
            {
                reason = second.name + ": " + secondReason;
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

        private static PregnancyData CreatePregnancy(ColonySaveData save, RatData parentA, RatData parentB, long gameTime)
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
                    mother.stage == RatStage.Adult && father.stage == RatStage.Adult &&
                    FindPendingPregnancy(save, mother.id) == null;
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
                string name = GameConfig.PupNames[i % GameConfig.PupNames.Length] + " " + (save.rats.Count + i + 1);
                var pup = ColonyFactory.CreateRat(
                    ColonyFactory.NewId("rat"),
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
                if (rat == null || rat.stage != RatStage.Adult || rat.enclosure != RatEnclosure.Pairing) continue;

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
            conceptionSucceeded = false;
            reason = string.Empty;
            if (save == null || female == null || male == null)
            {
                reason = "The pairing rats are no longer available.";
                return false;
            }

            string femaleReason;
            string maleReason;
            if (!BreedingSystem.IsBreedEligible(save, female, gameTime, out femaleReason))
            {
                reason = female.name + ": " + femaleReason;
                return false;
            }
            if (!BreedingSystem.IsBreedEligible(save, male, gameTime, out maleReason))
            {
                reason = male.name + ": " + maleReason;
                return false;
            }

            float chance = BreedingSystem.CalculateConceptionChance(
                female,
                male,
                pregnancyChance,
                0f,
                Mathf.Clamp01(pregnancyChance));
            if (UnityEngine.Random.value > chance) return true;

            PregnancyData pregnancy;
            if (!BreedingSystem.StartBreeding(save, female, male, gameTime, out pregnancy, out reason)) return false;
            conceptionSucceeded = true;
            return true;
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
