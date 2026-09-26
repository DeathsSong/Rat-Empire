using System;
using System.Collections.Generic;

namespace RatHabitat
{
    /// <summary>
    /// Persists meaningful activity transitions on each rat. This is separate
    /// from the colony event log: the colony log is global, while this history
    /// belongs to one stable RatData ID and follows that rat into retired data.
    /// </summary>
    public static class RatActivitySystem
    {
        public const int MaximumHistoryEntries = 12;
        private const string DefaultKey = "exploring";
        private const string DefaultLabel = "Exploring";

        public static void EnsureSaveState(ColonySaveData save, long gameTimeMs)
        {
            if (save == null) return;
            save.EnsureLists();
            EnsureList(save.rats, gameTimeMs);
            EnsureList(save.retiredRats, gameTimeMs);
        }

        private static void EnsureList(List<RatData> rats, long gameTimeMs)
        {
            if (rats == null) return;
            foreach (var rat in rats)
            {
                if (rat == null) continue;
                Ensure(rat, gameTimeMs);
            }
        }

        public static RatActivityData Ensure(RatData rat, long gameTimeMs)
        {
            if (rat == null) return null;
            if (rat.activity == null) rat.activity = new RatActivityData();
            if (rat.activity.history == null) rat.activity.history = new List<RatActivityEntryData>();
            if (string.IsNullOrEmpty(rat.activity.currentActivityKey))
            {
                rat.activity.currentActivityKey = DefaultKey;
                rat.activity.currentActivityLabel = DefaultLabel;
                rat.activity.currentActivityAt = gameTimeMs;
            }
            if (string.IsNullOrEmpty(rat.activity.currentActivityLabel))
                rat.activity.currentActivityLabel = DefaultLabel;
            Trim(rat.activity.history);
            return rat.activity;
        }

        public static bool SetCurrent(
            ColonySaveData save,
            RatData rat,
            string activityKey,
            string activityLabel,
            long gameTimeMs,
            string message = null)
        {
            if (rat == null) return false;
            RatActivityData activity = Ensure(rat, gameTimeMs);
            string key = string.IsNullOrEmpty(activityKey) ? DefaultKey : activityKey;
            string label = string.IsNullOrEmpty(activityLabel) ? DefaultLabel : activityLabel;
            bool changed = !string.Equals(activity.currentActivityKey, key, StringComparison.Ordinal) ||
                !string.Equals(activity.currentActivityLabel, label, StringComparison.Ordinal);
            if (!changed) return false;

            activity.currentActivityKey = key;
            activity.currentActivityLabel = label;
            activity.currentActivityAt = gameTimeMs;
            AddHistory(activity, gameTimeMs, key, label, string.IsNullOrEmpty(message) ? label : message);
            return true;
        }

        public static bool Record(
            ColonySaveData save,
            RatData rat,
            string activityKey,
            string activityLabel,
            long gameTimeMs,
            string message = null)
        {
            if (rat == null) return false;
            RatActivityData activity = Ensure(rat, gameTimeMs);
            string key = string.IsNullOrEmpty(activityKey) ? DefaultKey : activityKey;
            string label = string.IsNullOrEmpty(activityLabel) ? DefaultLabel : activityLabel;
            string text = string.IsNullOrEmpty(message) ? label : message;
            if (activity.history.Count > 0)
            {
                RatActivityEntryData latest = activity.history[0];
                if (latest != null && latest.gameTimeMs == gameTimeMs &&
                    string.Equals(latest.activityKey, key, StringComparison.Ordinal) &&
                    string.Equals(latest.message, text, StringComparison.Ordinal)) return false;
            }
            AddHistory(activity, gameTimeMs, key, label, text);
            return true;
        }

        private static void AddHistory(
            RatActivityData activity,
            long gameTimeMs,
            string activityKey,
            string activityLabel,
            string message)
        {
            activity.history.Insert(0, new RatActivityEntryData
            {
                gameTimeMs = gameTimeMs,
                activityKey = activityKey,
                activityLabel = activityLabel,
                message = message,
            });
            Trim(activity.history);
        }

        private static void Trim(List<RatActivityEntryData> history)
        {
            if (history == null) return;
            while (history.Count > MaximumHistoryEntries) history.RemoveAt(history.Count - 1);
        }

        public static string CurrentLabel(ColonySaveData save, RatData rat, long gameTimeMs)
        {
            if (rat == null) return "Unknown";
            switch (rat.removalDisposition)
            {
                case RatRemovalDisposition.Sold: return "Sold";
                case RatRemovalDisposition.Euthanized: return "Euthanized";
                case RatRemovalDisposition.NaturalDeath: return "Deceased";
            }

            if (save != null)
            {
                DedicatedBreedingSessionData session = BreedingSystem.FindActiveDedicatedSession(save, rat.id);
                if (session != null) return "Breeding";
                if (BreedingSystem.FindPendingPregnancyForMother(save, rat.id) != null) return "Pregnant";
            }
            if (rat.nursing || rat.reproductiveState == ReproductiveState.Nursing)
            {
                if (rat.nursingInteractionUntil > gameTimeMs && rat.activity != null &&
                    string.Equals(rat.activity.currentActivityKey, "nursing", System.StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(rat.activity.currentActivityLabel))
                    return rat.activity.currentActivityLabel;
                return "Nursing";
            }
            if (rat.reproductiveState == ReproductiveState.Recovery) return "Recovering";
            RatActivityData activity = Ensure(rat, gameTimeMs);
            return string.IsNullOrEmpty(activity.currentActivityLabel) ? DefaultLabel : activity.currentActivityLabel;
        }

        public static string CurrentKey(ColonySaveData save, RatData rat, long gameTimeMs)
        {
            if (rat == null) return "unknown";
            switch (rat.removalDisposition)
            {
                case RatRemovalDisposition.Sold: return "sold";
                case RatRemovalDisposition.Euthanized: return "euthanized";
                case RatRemovalDisposition.NaturalDeath: return "deceased";
            }
            if (save != null)
            {
                if (BreedingSystem.FindActiveDedicatedSession(save, rat.id) != null) return "breeding";
                if (BreedingSystem.FindPendingPregnancyForMother(save, rat.id) != null) return "pregnant";
            }
            if (rat.nursing || rat.reproductiveState == ReproductiveState.Nursing) return "nursing";
            if (rat.reproductiveState == ReproductiveState.Recovery) return "recovery";
            return Ensure(rat, gameTimeMs).currentActivityKey;
        }

        /// <summary>
        /// Applies authoritative biological activity transitions. Ambient
        /// behavior is supplied by GameBootstrap after this pass, so this
        /// method never creates per-frame idle entries.
        /// </summary>
        public static bool RefreshAuthoritativeActivities(ColonySaveData save, long gameTimeMs)
        {
            if (save == null) return false;
            bool changed = false;
            foreach (var rat in save.rats)
            {
                if (rat == null) continue;
                Ensure(rat, gameTimeMs);
                if (rat.removalDisposition != RatRemovalDisposition.None) continue;

                string key = null;
                string label = null;
                if (BreedingSystem.FindActiveDedicatedSession(save, rat.id) != null)
                {
                    key = "breeding";
                    label = "Breeding";
                }
                else if (BreedingSystem.FindPendingPregnancyForMother(save, rat.id) != null)
                {
                    key = "pregnant";
                    label = "Pregnant";
                }
                else if (rat.nursing || rat.reproductiveState == ReproductiveState.Nursing)
                {
                    key = "nursing";
                    label = rat.nursingInteractionUntil > gameTimeMs
                        ? "Caring for pinkies"
                        : "Nursing";
                }
                else if (rat.reproductiveState == ReproductiveState.Recovery)
                {
                    key = "recovery";
                    label = "Recovering";
                }

                if (key != null)
                {
                    changed |= SetCurrent(save, rat, key, label, gameTimeMs);
                }
            }
            return changed;
        }
    }
}
