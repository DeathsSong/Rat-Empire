using System;
using System.Collections.Generic;

namespace RatHabitat
{
    /// <summary>
    /// Owns persistent friendly-name allocation. Rat IDs remain the identity
    /// used by gameplay; this system only guarantees that player-facing names
    /// are unambiguous among active colony rats and current market listings.
    /// </summary>
    public static class RatNameSystem
    {
        private static long CooldownMs
        {
            get { return (long)(GameConfig.RatNameReuseCooldownDays * GameConfig.GameDayMs); }
        }

        public static bool EnsureUniqueNames(ColonySaveData save, long gameTime)
        {
            if (save == null) return false;
            save.EnsureLists();
            bool changed = false;
            var occupied = new HashSet<string>(StringComparer.Ordinal);

            // List order is the established order for legacy saves: the first
            // living record keeps its name and only later duplicates move.
            foreach (RatData rat in save.rats)
            {
                if (rat == null) continue;
                string normalizedDisplay = ColonyFactory.NormalizeDisplayName(rat.name);
                if (!string.Equals(normalizedDisplay, rat.name, StringComparison.Ordinal))
                {
                    rat.name = normalizedDisplay;
                    changed = true;
                }

                string key = NormalizeForComparison(rat.name);
                if (string.IsNullOrEmpty(key) || occupied.Contains(key))
                {
                    string previous = rat.name;
                    rat.name = AllocateName(save, rat.id, rat.sex, gameTime, occupied);
                    key = NormalizeForComparison(rat.name);
                    RewriteEventNames(save, previous, rat.name);
                    changed = true;
                }
                occupied.Add(key);
                EnsureHistoryEntry(save, rat.name, rat.sex, rat.id, gameTime);
            }

            // Retired records do not participate in active uniqueness, but
            // their names remain part of the reuse history.
            foreach (RatData rat in save.retiredRats)
            {
                if (rat == null || string.IsNullOrWhiteSpace(rat.name)) continue;
                EnsureHistoryEntry(save, rat.name, rat.sex, rat.id, rat.removedAt > 0L ? rat.removedAt : gameTime);
            }

            // Market names must not collide with a living rat or each other.
            foreach (StoreRatListingData listing in save.storeRatListings)
            {
                if (listing == null) continue;
                string key = NormalizeForComparison(listing.name);
                if (string.IsNullOrEmpty(key) || occupied.Contains(key))
                {
                    string previous = listing.name;
                    listing.name = AllocateName(save, listing.id, listing.sex, gameTime, occupied);
                    key = NormalizeForComparison(listing.name);
                    RewriteEventNames(save, previous, listing.name);
                    changed = true;
                }
                occupied.Add(key);
                EnsureHistoryEntry(save, listing.name, listing.sex, listing.id, gameTime);
            }

            if (save.ratNameMigrationVersion < 1)
            {
                save.ratNameMigrationVersion = 1;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Assigns a name to a newly-created rat without changing an already
        /// unique established name. Callers use the stable ID as the only
        /// deterministic input, so rebuilding UI cannot reroll a name.
        /// </summary>
        public static bool EnsureUniqueName(ColonySaveData save, RatData rat, long gameTime)
        {
            if (save == null || rat == null) return false;
            save.EnsureLists();
            var occupied = CollectOccupiedNames(save, rat.id);
            string key = NormalizeForComparison(rat.name);
            bool changed = false;
            if (string.IsNullOrEmpty(key) || occupied.Contains(key))
            {
                string previous = rat.name;
                rat.name = AllocateName(save, rat.id, rat.sex, gameTime, occupied);
                RewriteEventNames(save, previous, rat.name);
                changed = true;
            }
            EnsureHistoryEntry(save, rat.name, rat.sex, rat.id, gameTime);
            return changed;
        }

        /// <summary>
        /// Records a name-use event such as a sale or natural death. This is
        /// intentionally separate from the living-name uniqueness pass.
        /// </summary>
        public static void RecordUsage(ColonySaveData save, string name, RatSex sex, string ratId, long gameTime)
        {
            if (save == null || string.IsNullOrWhiteSpace(name)) return;
            save.EnsureLists();
            RatNameUseData entry = FindHistory(save, name);
            if (entry == null)
            {
                entry = new RatNameUseData
                {
                    normalizedName = NormalizeForComparison(name),
                    displayName = ColonyFactory.NormalizeDisplayName(name),
                };
                save.ratNameHistory.Add(entry);
            }
            entry.sex = sex;
            entry.ratId = ratId;
            entry.lastUsedGameTime = gameTime;
            entry.lastUsedRealTimestamp = GameConfig.NowMs();
        }

        public static string NormalizeForComparison(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = new List<char>(value.Length);
            string lower = value.Trim().ToLowerInvariant();
            foreach (char character in lower)
            {
                if (char.IsLetterOrDigit(character)) chars.Add(character);
            }
            return new string(chars.ToArray());
        }

        private static string AllocateName(
            ColonySaveData save,
            string stableId,
            RatSex sex,
            long gameTime,
            HashSet<string> occupied)
        {
            string[] pool = sex == RatSex.Female ? GameConfig.FemaleRatNames : GameConfig.MaleRatNames;
            int start = StableHash(stableId) % Math.Max(1, pool.Length);
            int bestIndex = -1;
            long bestLastUsed = long.MaxValue;

            for (int offset = 0; offset < pool.Length; offset++)
            {
                int index = (start + offset) % pool.Length;
                string candidate = pool[index];
                string key = NormalizeForComparison(candidate);
                if (occupied.Contains(key)) continue;
                RatNameUseData history = FindHistory(save, candidate);
                long lastUsed = history == null ? 0L : history.lastUsedGameTime;
                bool cooldownExpired = lastUsed <= 0L || gameTime - lastUsed >= CooldownMs;
                if (!cooldownExpired) continue;
                // Unused names win first. Thereafter the oldest name wins; the
                // stable start offset resolves equal timestamps deterministically.
                if (bestIndex < 0 || lastUsed < bestLastUsed)
                {
                    bestIndex = index;
                    bestLastUsed = lastUsed;
                }
                if (lastUsed <= 0L) break;
            }

            if (bestIndex >= 0) return pool[bestIndex];

            // A real colony is unlikely to exhaust the expanded pools. This
            // deterministic suffix still guarantees uniqueness if it does.
            string fallbackBase = pool.Length == 0 ? (sex == RatSex.Female ? "Mabel" : "Otto") : pool[start];
            for (int suffix = 2; suffix < 100000; suffix++)
            {
                string candidate = fallbackBase + " " + suffix;
                if (!occupied.Contains(NormalizeForComparison(candidate))) return candidate;
            }
            return fallbackBase + " " + StableHash(stableId);
        }

        private static HashSet<string> CollectOccupiedNames(ColonySaveData save, string exceptId)
        {
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            if (save.rats != null)
            {
                foreach (RatData rat in save.rats)
                {
                    if (rat == null || rat.id == exceptId) continue;
                    string key = NormalizeForComparison(rat.name);
                    if (!string.IsNullOrEmpty(key)) occupied.Add(key);
                }
            }
            if (save.storeRatListings != null)
            {
                foreach (StoreRatListingData listing in save.storeRatListings)
                {
                    if (listing == null || listing.id == exceptId) continue;
                    string key = NormalizeForComparison(listing.name);
                    if (!string.IsNullOrEmpty(key)) occupied.Add(key);
                }
            }
            return occupied;
        }

        private static RatNameUseData FindHistory(ColonySaveData save, string name)
        {
            string key = NormalizeForComparison(name);
            if (save == null || save.ratNameHistory == null || string.IsNullOrEmpty(key)) return null;
            foreach (RatNameUseData entry in save.ratNameHistory)
            {
                if (entry != null && entry.normalizedName == key) return entry;
            }
            return null;
        }

        private static void EnsureHistoryEntry(ColonySaveData save, string name, RatSex sex, string ratId, long gameTime)
        {
            if (save == null || string.IsNullOrWhiteSpace(name)) return;
            RatNameUseData entry = FindHistory(save, name);
            if (entry != null) return;
            save.ratNameHistory.Add(new RatNameUseData
            {
                normalizedName = NormalizeForComparison(name),
                displayName = ColonyFactory.NormalizeDisplayName(name),
                sex = sex,
                ratId = ratId,
                lastUsedGameTime = gameTime,
                lastUsedRealTimestamp = GameConfig.NowMs(),
            });
        }

        private static void RewriteEventNames(ColonySaveData save, string previous, string replacement)
        {
            if (save == null || save.eventLog == null || string.IsNullOrWhiteSpace(previous) ||
                string.IsNullOrWhiteSpace(replacement) || string.Equals(previous, replacement, StringComparison.Ordinal)) return;
            foreach (ColonyEventData eventData in save.eventLog)
            {
                if (eventData == null || string.IsNullOrEmpty(eventData.message)) continue;
                eventData.message = eventData.message.Replace(previous, replacement);
            }
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
    }
}
