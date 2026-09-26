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
        private static readonly long NursingCooldownMs =
            (long)(GameConfig.NursingInteractionCooldownHours * 60f * 60f * 1000f);

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
                    changed = true;
                }

                if (mother.nursingInteractionUntil > gameTime) continue;

                RatData pup = ChooseNextPup(save, mother, gameTime);
                if (pup == null) continue;
                if (!presenter.BeginNursingInteraction(mother.id, pup.id,
                    GameConfig.NursingInteractionDurationSeconds)) continue;

                pup.lastNursedAt = gameTime;
                mother.nursingPupId = pup.id;
                mother.nursingInteractionUntil = gameTime +
                    BehaviorSecondsToGameMilliseconds(GameConfig.NursingInteractionDurationSeconds);
                RatActivitySystem.SetCurrent(save, mother, "caring_for_pinkies",
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
            float speed = Mathf.Max(1f, GrowthSystem.RuntimeSimulationSpeed);
            double gameMillisecondsPerRealMillisecond =
                GrowthSystem.SimulationMillisecondsPerRealMillisecond(speed);
            double gameMillisecondsPerBehaviorSecond =
                gameMillisecondsPerRealMillisecond * 1000d / speed;
            return Math.Max(1L, (long)Math.Round(behaviorSeconds * gameMillisecondsPerBehaviorSecond));
        }
    }
}
