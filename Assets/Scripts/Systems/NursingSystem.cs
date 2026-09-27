using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Coordinates the occasional visible mother/pup care interaction. The
    /// biological relationship is always read from the pup's saved motherId
    /// and litter record; display names are never used for ownership.
    /// </summary>
    public static class NursingSystem
    {
        private sealed class InteractionDefinition
        {
            public readonly string id;
            public readonly string animationState;
            public readonly string animationFallback;
            public readonly float durationSeconds;
            public readonly float weight;

            public InteractionDefinition(string id, string animationState,
                string animationFallback, float durationSeconds, float weight)
            {
                this.id = id;
                this.animationState = animationState;
                this.animationFallback = animationFallback;
                this.durationSeconds = durationSeconds;
                this.weight = Mathf.Max(0f, weight);
            }
        }

        // Data-driven presentation choices. The rat behavior only receives an
        // animation state from this catalog; it never interprets "Attack" as
        // combat. Additional care animations can be added here later without
        // changing the nursing route, cooldown, or biological systems.
        private static readonly InteractionDefinition[] InteractionDefinitions =
        {
            new InteractionDefinition("sniffing", "HandPaintedRat_Sniffing", "Sniffing",
                GameConfig.NursingInteractionDurationSeconds, GameConfig.NursingSniffInteractionWeight),
            new InteractionDefinition("grooming", "HandPaintedRat_Attack", "Attack",
                GameConfig.NursingGroomingInteractionDurationSeconds, GameConfig.NursingGroomingInteractionWeight),
        };

        public const string DefaultInteractionId = "sniffing";

        private static readonly long NursingCooldownMs =
            (long)(GameConfig.NursingInteractionCooldownHours * 60f * 60f * 1000f);
        private static readonly long NursingRetryCooldownMs = 30L * 1000L;

        public static string NormalizeInteractionId(string interactionId)
        {
            if (FindDefinition(interactionId) != null) return interactionId;
            return DefaultInteractionId;
        }

        public static string AnimationStateFor(string interactionId)
        {
            InteractionDefinition definition = FindDefinition(NormalizeInteractionId(interactionId));
            return definition == null ? "HandPaintedRat_Sniffing" : definition.animationState;
        }

        public static string AnimationFallbackFor(string interactionId)
        {
            InteractionDefinition definition = FindDefinition(NormalizeInteractionId(interactionId));
            return definition == null ? "Sniffing" : definition.animationFallback;
        }

        public static float DurationSecondsFor(string interactionId)
        {
            InteractionDefinition definition = FindDefinition(NormalizeInteractionId(interactionId));
            return definition == null ? GameConfig.NursingInteractionDurationSeconds : definition.durationSeconds;
        }

        /// <summary>
        /// Chooses one care animation from stable mother/pup/time inputs. The
        /// choice is saved immediately by the caller, so a browser reload can
        /// resume the same interaction instead of rerolling its visual.
        /// </summary>
        public static string ChooseInteractionId(string motherId, string pupId, long gameTime)
        {
            int seed = StableSeed((motherId ?? string.Empty) + "|" +
                (pupId ?? string.Empty) + "|" +
                (gameTime / Math.Max(1L, NursingCooldownMs)));
            double roll = (seed % 100000) / 99999d;
            float totalWeight = 0f;
            for (int index = 0; index < InteractionDefinitions.Length; index++)
                totalWeight += InteractionDefinitions[index].weight;

            if (totalWeight <= 0f) return DefaultInteractionId;
            float cursor = (float)(roll * totalWeight);
            for (int index = 0; index < InteractionDefinitions.Length; index++)
            {
                cursor -= InteractionDefinitions[index].weight;
                if (cursor <= 0f) return InteractionDefinitions[index].id;
            }
            return InteractionDefinitions[InteractionDefinitions.Length - 1].id;
        }

        /// <summary>
        /// Converts the persisted simulated deadline back to the behavior
        /// timer's scaled seconds. This lets an active care animation resume
        /// at the same simulation-time point after a reload or render refresh.
        /// </summary>
        public static float BehaviorSecondsFromGameMilliseconds(long gameMilliseconds)
        {
            if (gameMilliseconds <= 0L) return 0.75f;
            return Mathf.Max(0.75f, (float)(gameMilliseconds / GrowthSystem.GameMillisecondsPerBehaviorSecond));
        }

        /// <summary>
        /// Returns the next simulation timestamp at which a nursing pass can
        /// change anything. This lets the main loop sleep between the short
        /// interaction deadline and the per-pup cooldown instead of allocating
        /// and sorting nursing candidates every rendered frame.
        /// </summary>
        public static long NextOpportunityGameTime(ColonySaveData save, long gameTime)
        {
            if (save == null || save.rats == null) return long.MaxValue;
            long next = long.MaxValue;

            foreach (RatData mother in save.rats)
            {
                if (!IsMotherCandidate(save, mother)) continue;
                if (mother.nursingInteractionUntil > gameTime)
                {
                    next = Math.Min(next, mother.nursingInteractionUntil);
                    continue;
                }
                if (mother.nursingRetryAt > gameTime)
                {
                    next = Math.Min(next, mother.nursingRetryAt);
                    continue;
                }

                bool hasEligiblePup = false;
                foreach (RatData pup in save.rats)
                {
                    if (pup == null || pup.stage != RatStage.Pinkie ||
                        pup.removalDisposition != RatRemovalDisposition.None ||
                        pup.enclosure != mother.enclosure ||
                        !IsRecordedPup(save, mother, pup)) continue;

                    hasEligiblePup = true;
                    long availableAt = pup.lastNursedAt <= 0L
                        ? gameTime
                        : pup.lastNursedAt + NursingCooldownMs;
                    if (availableAt <= gameTime) return gameTime;
                    next = Math.Min(next, availableAt);
                }

                // A valid mother with no current pup candidate will be woken
                // by a later stage/relationship pass rather than polled each
                // frame. The local flag documents that this is intentional.
                if (!hasEligiblePup && next == long.MaxValue) continue;
            }

            return next;
        }

        public static bool Tick(ColonySaveData save, long gameTime, RatPresenter presenter)
        {
            if (save == null || presenter == null || save.rats == null) return false;
            bool changed = false;

            foreach (RatData mother in save.rats)
            {
                if (!IsMotherCandidate(save, mother)) continue;

                if (mother.nursingInteractionUntil > 0L &&
                    gameTime >= mother.nursingInteractionUntil)
                {
                    mother.nursingInteractionUntil = 0L;
                    mother.nursingPupId = null;
                    mother.nursingInteractionType = null;
                    changed = true;
                }
                if (mother.nursingRetryAt > 0L && gameTime >= mother.nursingRetryAt)
                {
                    mother.nursingRetryAt = 0L;
                    changed = true;
                }

                if (mother.nursingInteractionUntil > gameTime || mother.nursingRetryAt > gameTime)
                    continue;

                RatData pup = ChooseNextPup(save, mother, gameTime);
                if (pup == null) continue;
                string interactionId = ChooseInteractionId(mother.id, pup.id, gameTime);
                float durationSeconds = DurationSecondsFor(interactionId);
                if (!presenter.BeginNursingInteraction(mother.id, pup.id,
                    interactionId, durationSeconds))
                {
                    // A presenter can be temporarily unavailable during a
                    // habitat rebuild. Back off in simulation time instead
                    // of waking the full colony maintenance pass every frame.
                    mother.nursingRetryAt = gameTime + NursingRetryCooldownMs;
                    changed = true;
                    continue;
                }

                pup.lastNursedAt = gameTime;
                mother.nursingPupId = pup.id;
                mother.nursingRetryAt = 0L;
                mother.nursingInteractionType = NormalizeInteractionId(interactionId);
                mother.nursingInteractionUntil = gameTime +
                    BehaviorSecondsToGameMilliseconds(durationSeconds);
                RatActivitySystem.SetCurrent(save, mother, "nursing",
                    "Caring for pinkies", gameTime, "Caring for pinkies");
                // Keep the pinkie's current nest activity stable while still
                // recording the meaningful nursing event in its ID-bound
                // history. Pinkies do not have an ambient movement behavior
                // that could otherwise clear a transient current label.
                RatActivitySystem.Record(save, pup, "nursing", "Nursing", gameTime,
                    "Nursing with mother");
                changed = true;
            }

            return changed;
        }

        private static bool IsMotherCandidate(ColonySaveData save, RatData mother)
        {
            if (mother == null || mother.sex != RatSex.Female ||
                (mother.stage != RatStage.Adult && mother.stage != RatStage.Mature) ||
                mother.removalDisposition != RatRemovalDisposition.None) return false;
            return mother.nursing || EnclosureSystem.HasDependentPinkies(save, mother.id);
        }

        private static RatData ChooseNextPup(ColonySaveData save, RatData mother, long gameTime)
        {
            var candidates = new List<RatData>();
            foreach (RatData pup in save.rats)
            {
                if (pup == null || pup.stage != RatStage.Pinkie ||
                    pup.removalDisposition != RatRemovalDisposition.None ||
                    pup.enclosure != mother.enclosure) continue;
                if (!IsRecordedPup(save, mother, pup)) continue;
                if (pup.lastNursedAt > 0L && gameTime - pup.lastNursedAt < NursingCooldownMs) continue;
                candidates.Add(pup);
            }

            candidates.Sort((left, right) =>
            {
                int result = left.lastNursedAt.CompareTo(right.lastNursedAt);
                if (result != 0) return result;
                return string.CompareOrdinal(left.id, right.id);
            });
            return candidates.Count == 0 ? null : candidates[0];
        }

        private static bool IsRecordedPup(ColonySaveData save, RatData mother, RatData pup)
        {
            if (pup == null || mother == null) return false;
            if (pup.motherId == mother.id) return true;
            if (string.IsNullOrEmpty(pup.litterId) || save.litters == null) return false;
            foreach (LitterData litter in save.litters)
            {
                if (litter == null || litter.id != pup.litterId || litter.motherId != mother.id ||
                    litter.pupIds == null) continue;
                if (litter.pupIds.Contains(pup.id)) return true;
            }
            return false;
        }

        private static long BehaviorSecondsToGameMilliseconds(float behaviorSeconds)
        {
            return Math.Max(1L, (long)Math.Round(
                behaviorSeconds * GrowthSystem.GameMillisecondsPerBehaviorSecond));
        }

        private static InteractionDefinition FindDefinition(string interactionId)
        {
            if (string.IsNullOrEmpty(interactionId)) return null;
            for (int index = 0; index < InteractionDefinitions.Length; index++)
            {
                if (string.Equals(InteractionDefinitions[index].id, interactionId,
                    StringComparison.Ordinal)) return InteractionDefinitions[index];
            }
            return null;
        }

        private static int StableSeed(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < value.Length; index++) hash = hash * 31 + value[index];
                return hash & 0x7fffffff;
            }
        }
    }
}
