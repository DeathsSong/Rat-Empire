using System;
using System.Collections.Generic;

namespace RatHabitat
{
    /// <summary>
    /// Persistent, deterministic friendly-name allocation. Rat IDs remain the
    /// identity used by gameplay; names are presentation data and are never
    /// rerolled by a UI refresh.
    /// </summary>
    public static class RatNameSystem
    {
        public const int CurrentMigrationVersion = 2;
        public const int MaximumNameLength = 24;
        private static readonly Dictionary<string, RatNameUseData> SaveNameHistoryIndex =
            new Dictionary<string, RatNameUseData>(StringComparer.Ordinal);

        private static long CooldownMs
        {
            get { return (long)(GameConfig.RatNameReuseCooldownDays * GameConfig.GameDayMs); }
        }

        public static bool EnsureUniqueNames(ColonySaveData save, long gameTime)
        {
            if (save == null) return false;
            save.EnsureLists();
            bool changed = MigrateLegacyNames(save, gameTime);
            // Save normalization runs on every full-colony write. Build the
            // history lookup once so ensuring entries for each active/retired
            // rat is O(rats + history), not a repeated linear search through
            // the entire historical name ledger for every rat.
            SaveNameHistoryIndex.Clear();
            foreach (RatNameUseData entry in save.ratNameHistory)
            {
                if (entry == null || string.IsNullOrEmpty(entry.normalizedName) ||
                    SaveNameHistoryIndex.ContainsKey(entry.normalizedName)) continue;
                SaveNameHistoryIndex.Add(entry.normalizedName, entry);
            }
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            var playerNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (RatData rat in save.rats)
            {
                if (rat == null || !rat.nameWasPlayerAssigned) continue;
                string key = NormalizeForComparison(rat.name);
                if (!string.IsNullOrEmpty(key)) playerNames.Add(key);
            }

            foreach (RatData rat in save.rats)
            {
                if (rat == null) continue;
                if (!rat.nameWasPlayerAssigned)
                {
                    string normalized = ColonyFactory.NormalizeDisplayName(rat.name);
                    if (!string.Equals(normalized, rat.name, StringComparison.Ordinal))
                    {
                        rat.name = normalized;
                        changed = true;
                    }
                }
                string key = NormalizeForComparison(rat.name);
                bool duplicate = string.IsNullOrEmpty(key) || occupied.Contains(key) ||
                    (!rat.nameWasPlayerAssigned && playerNames.Contains(key));
                bool autoRepairAllowed = !rat.nameWasPlayerAssigned || !playerNames.Contains(key);
                if (duplicate && autoRepairAllowed)
                {
                    string previous = rat.name;
                    rat.name = AllocateName(save, rat.id, rat.sex, gameTime, occupied);
                    key = NormalizeForComparison(rat.name);
                    RewriteEventNames(save, previous, rat.name);
                    changed = true;
                }
                if (!string.IsNullOrEmpty(key)) occupied.Add(key);
                EnsureHistoryEntry(save, rat.name, rat.sex, rat.id, gameTime, SaveNameHistoryIndex);
            }

            foreach (RatData rat in save.retiredRats)
            {
                if (rat == null || string.IsNullOrWhiteSpace(rat.name)) continue;
                EnsureHistoryEntry(save, rat.name, rat.sex, rat.id,
                    rat.removedAt > 0L ? rat.removedAt : gameTime, SaveNameHistoryIndex);
            }

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
                if (!string.IsNullOrEmpty(key)) occupied.Add(key);
                EnsureHistoryEntry(save, listing.name, listing.sex, listing.id, gameTime, SaveNameHistoryIndex);
            }

            if (save.ratNameMigrationVersion < CurrentMigrationVersion)
            {
                save.ratNameMigrationVersion = CurrentMigrationVersion;
                changed = true;
            }
            foreach (StoreRatListingData listing in save.storeRatListings)
            {
                if (listing == null || !HasGeneratedSuffix(listing.name)) continue;
                string previous = listing.name;
                listing.name = AllocateName(save, listing.id, listing.sex, gameTime,
                    CollectOccupiedNames(save, listing.id));
                RewriteEventNames(save, previous, listing.name);
                changed = true;
            }
            SaveNameHistoryIndex.Clear();
            return changed;
        }

        public static bool EnsureUniqueName(ColonySaveData save, RatData rat, long gameTime)
        {
            if (save == null || rat == null) return false;
            save.EnsureLists();
            string key = NormalizeForComparison(rat.name);
            var occupied = CollectOccupiedNames(save, rat.id);
            bool changed = false;
            RatData directParent = FindDirectNameParent(save, rat);
            bool directLineageSuffix = directParent != null && IsSameLineageBase(rat.name, directParent.name);
            if (string.IsNullOrEmpty(key) || occupied.Contains(key) ||
                (!rat.nameWasPlayerAssigned && HasGeneratedSuffix(rat.name) && !directLineageSuffix))
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
        /// A direct parent match is the only path allowed to create Jr/Roman
        /// suffixes. It is called after a newborn has its parent IDs.
        /// </summary>
        public static bool EnsureBirthName(ColonySaveData save, RatData pup, long gameTime)
        {
            if (save == null || pup == null) return false;
            save.EnsureLists();
            RatData parent = FindDirectNameParent(save, pup);
            if (parent != null && IsSameLineageBase(pup.name, parent.name))
            {
                string inherited = BuildLineageName(parent.name);
                if (!string.Equals(pup.name, inherited, StringComparison.Ordinal))
                {
                    pup.name = inherited;
                    EnsureUniqueName(save, pup, gameTime);
                    return true;
                }
            }
            return EnsureUniqueName(save, pup, gameTime);
        }

        public static string GenerateAvailableName(ColonySaveData save, string stableId, RatSex sex, long gameTime)
        {
            if (save == null) return sex == RatSex.Female ? "Mabel" : "Otto";
            save.EnsureLists();
            return AllocateName(save, stableId, sex, gameTime, CollectOccupiedNames(save, null));
        }

        /// <summary>
        /// Generates a name deterministically from a stable entity ID and an
        /// independent persisted generation seed. Market stock uses this so
        /// each colony/restock gets a varied draw even though listing IDs are
        /// intentionally predictable. The chosen name is then saved on the
        /// listing and is never regenerated by reloads or UI refreshes.
        /// </summary>
        public static string GenerateAvailableName(
            ColonySaveData save,
            string stableId,
            RatSex sex,
            long gameTime,
            int deterministicSelectionSeed)
        {
            if (save == null) return sex == RatSex.Female ? "Mabel" : "Otto";
            save.EnsureLists();
            string selectionId = (stableId ?? string.Empty) + "|selection|" + deterministicSelectionSeed;
            return AllocateName(save, selectionId, sex, gameTime, CollectOccupiedNames(save, null));
        }

        /// <summary>
        /// Stable fallback for factory paths without a colony save context.
        /// Custom names are only considered by GenerateAvailableName, where
        /// they are still treated as complete entries rather than combined.
        /// </summary>
        public static string GeneratedName(string stableId, RatSex sex)
        {
            string[] pool = sex == RatSex.Female ? GameConfig.FemaleRatNames : GameConfig.MaleRatNames;
            if (pool == null || pool.Length == 0) return sex == RatSex.Female ? "Mabel" : "Otto";

            bool wantsDoubleName = WantsDoubleName(stableId);
            int start = StableHash((stableId ?? string.Empty) + "|name-choice") % pool.Length;
            for (int pass = 0; pass < 2; pass++)
            {
                bool targetDoubleName = pass == 0 ? wantsDoubleName : !wantsDoubleName;
                for (int offset = 0; offset < pool.Length; offset++)
                {
                    string candidate = pool[(start + offset) % pool.Length];
                    NameParts parts = SplitName(candidate);
                    bool candidateIsDoubleName = !string.IsNullOrEmpty(parts.second);
                    if (candidateIsDoubleName != targetDoubleName) continue;
                    return candidate;
                }
            }
            return pool[start];
        }

        public static bool IsNameAvailable(ColonySaveData save, string name, string exceptId)
        {
            if (save == null) return false;
            string key = NormalizeForComparison(name);
            return !string.IsNullOrEmpty(key) && !CollectOccupiedNames(save, exceptId).Contains(key);
        }

        public static bool TrySanitizePlayerName(string raw, out string sanitized, out string error)
        {
            sanitized = string.Empty;
            error = string.Empty;
            if (raw == null) { error = "Enter a name."; return false; }
            string value = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            value = string.Join(" ", parts);
            if (string.IsNullOrEmpty(value)) { error = "Enter a name."; return false; }
            if (string.IsNullOrEmpty(NormalizeForComparison(value))) { error = "Enter a name with at least one letter or number."; return false; }
            if (value.Length > MaximumNameLength) { error = "Names must be 24 characters or fewer."; return false; }
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) { error = "That name contains an invalid character."; return false; }
            sanitized = value;
            return true;
        }

        public static string CustomNamesText(ColonySaveData save, RatSex sex)
        {
            if (save == null) return string.Empty;
            save.EnsureLists();
            return string.Join("\n", sex == RatSex.Female ? save.customFemaleRatNames : save.customMaleRatNames);
        }

        public static bool SetCustomNames(ColonySaveData save, RatSex sex, string multiline)
        {
            if (save == null) return false;
            save.EnsureLists();
            List<string> target = sex == RatSex.Female ? save.customFemaleRatNames : save.customMaleRatNames;
            var next = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string[] lines = (multiline ?? string.Empty).Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
            {
                string name;
                string error;
                if (!TrySanitizePlayerName(line, out name, out error)) continue;
                if (seen.Add(NormalizeForComparison(name))) next.Add(name);
            }
            bool changed = target.Count != next.Count;
            if (!changed)
                for (int i = 0; i < target.Count; i++)
                    if (!string.Equals(target[i], next[i], StringComparison.Ordinal)) { changed = true; break; }
            if (changed) { target.Clear(); target.AddRange(next); }
            return changed;
        }

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
            SetNameParts(entry, name, gameTime);
        }

        public static string NormalizeForComparison(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string lower = value.Trim().ToLowerInvariant();
            var chars = new List<char>(lower.Length);
            foreach (char character in lower)
                if (char.IsLetterOrDigit(character)) chars.Add(character);
            return new string(chars.ToArray());
        }

        private static string[] BuildPool(ColonySaveData save, RatSex sex)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> custom = sex == RatSex.Female ? save.customFemaleRatNames : save.customMaleRatNames;
            foreach (string name in custom) AddPoolCandidate(result, seen, name, true);
            string[] builtIn = sex == RatSex.Female ? GameConfig.FemaleRatNames : GameConfig.MaleRatNames;
            foreach (string name in builtIn) AddPoolCandidate(result, seen, name, false);
            if (result.Count == 0) result.Add(sex == RatSex.Female ? "Mabel" : "Otto");
            return result.ToArray();
        }

        private static void AddPoolCandidate(List<string> result, HashSet<string> seen, string value, bool allowSuffixed)
        {
            string name;
            string error;
            if (!TrySanitizePlayerName(value, out name, out error)) return;
            if (!allowSuffixed && HasGeneratedSuffix(name)) return;
            if (seen.Add(NormalizeForComparison(name))) result.Add(name);
        }

        private static string AllocateName(ColonySaveData save, string stableId, RatSex sex, long gameTime, HashSet<string> occupied)
        {
            string[] pool = BuildPool(save, sex);
            int start = StableHash((stableId ?? string.Empty) + "|name-choice") % Math.Max(1, pool.Length);
            bool wantsDoubleName = WantsDoubleName(stableId);

            // Keep the requested name shape when its candidates exist. Within
            // that shape, prefer never-used/old first and second names. Only
            // relax the reuse cooldown before falling back to the other shape.
            int best = FindBestCandidate(save, pool, occupied, gameTime, start, wantsDoubleName, true);
            if (best < 0) best = FindBestCandidate(save, pool, occupied, gameTime, start, wantsDoubleName, false);
            if (best < 0) best = FindBestCandidate(save, pool, occupied, gameTime, start, !wantsDoubleName, true);
            if (best < 0) best = FindBestCandidate(save, pool, occupied, gameTime, start, !wantsDoubleName, false);
            if (best >= 0) return pool[best];

            string fallback = pool[start];
            for (int offset = 0; offset < pool.Length; offset++)
            {
                string candidate = pool[(start + offset) % pool.Length];
                bool candidateIsDoubleName = !string.IsNullOrEmpty(SplitName(candidate).second);
                if (candidateIsDoubleName == wantsDoubleName)
                {
                    fallback = candidate;
                    break;
                }
            }
            for (int suffix = 2; suffix < 100000; suffix++)
            {
                string candidate = fallback + " " + suffix;
                if (!occupied.Contains(NormalizeForComparison(candidate))) return candidate;
            }
            return fallback + " " + StableHash(stableId);
        }

        private static int FindBestCandidate(ColonySaveData save, string[] pool,
            HashSet<string> occupied, long gameTime, int start, bool wantsDoubleName,
            bool respectReuseCooldown)
        {
            int best = -1;
            long bestScore = long.MaxValue;
            for (int offset = 0; offset < pool.Length; offset++)
            {
                int index = (start + offset) % pool.Length;
                string candidate = pool[index];
                NameParts parts = SplitName(candidate);
                bool isDoubleName = !string.IsNullOrEmpty(parts.second);
                if (isDoubleName != wantsDoubleName ||
                    occupied.Contains(NormalizeForComparison(candidate))) continue;

                long firstLast = LatestFirstNameUse(save, parts.first);
                long secondLast = isDoubleName ? LatestSecondNameUse(save, parts.second) : 0L;
                if (respectReuseCooldown &&
                    ((firstLast > 0L && gameTime - firstLast < CooldownMs) ||
                     (secondLast > 0L && gameTime - secondLast < CooldownMs))) continue;

                RatNameUseData fullNameUse = FindHistory(save, candidate);
                long fullNameLast = fullNameUse == null ? 0L : fullNameUse.lastUsedGameTime;
                long score = Math.Max(fullNameLast, Math.Max(firstLast, secondLast));
                if (score < bestScore)
                {
                    best = index;
                    bestScore = score;
                }
                if (score == 0L) break;
            }
            return best;
        }

        private static bool WantsDoubleName(string stableId)
        {
            double chance = Math.Max(0d, Math.Min(1d, GameConfig.RatDoubleNameChance));
            int threshold = (int)Math.Round(chance * 10000d);
            return StableHash((stableId ?? string.Empty) + "|name-shape") % 10000 < threshold;
        }

        private static HashSet<string> CollectOccupiedNames(ColonySaveData save, string exceptId)
        {
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            if (save.rats != null)
                foreach (RatData rat in save.rats)
                    if (rat != null && rat.id != exceptId && !string.IsNullOrEmpty(NormalizeForComparison(rat.name))) occupied.Add(NormalizeForComparison(rat.name));
            if (save.storeRatListings != null)
                foreach (StoreRatListingData listing in save.storeRatListings)
                    if (listing != null && listing.id != exceptId && !string.IsNullOrEmpty(NormalizeForComparison(listing.name))) occupied.Add(NormalizeForComparison(listing.name));
            return occupied;
        }

        private static RatData FindDirectNameParent(ColonySaveData save, RatData pup)
        {
            RatData mother = FindRat(save, pup.motherId);
            if (mother != null) return mother;
            return FindRat(save, pup.fatherId);
        }

        private static RatData FindRat(ColonySaveData save, string id)
        {
            if (save == null || string.IsNullOrEmpty(id) || save.rats == null) return null;
            foreach (RatData rat in save.rats) if (rat != null && rat.id == id) return rat;
            return null;
        }

        private static bool IsSameLineageBase(string child, string parent)
        {
            return !string.IsNullOrEmpty(NormalizeForComparison(child)) &&
                string.Equals(LineageBaseName(child), LineageBaseName(parent), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLineageName(string parent)
        {
            string baseName = LineageBaseName(parent);
            int generation = LineageSuffixNumber(parent);
            // Jr is treated as the second generation in the player-facing
            // convention, so the next direct descendant is explicitly III.
            int next = generation == 1 ? 3 : generation + 1;
            return generation <= 0 ? baseName + " Jr" : baseName + " " + ToRoman(next);
        }

        private static string LineageBaseName(string name)
        {
            string value = ColonyFactory.NormalizeDisplayName(name);
            string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && (string.Equals(parts[parts.Length - 1], "Jr", StringComparison.OrdinalIgnoreCase) || IsRoman(parts[parts.Length - 1])))
                return string.Join(" ", parts, 0, parts.Length - 1);
            return value;
        }

        private static int LineageSuffixNumber(string name)
        {
            string[] parts = ColonyFactory.NormalizeDisplayName(name).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return 0;
            if (string.Equals(parts[parts.Length - 1], "Jr", StringComparison.OrdinalIgnoreCase)) return 1;
            return IsRoman(parts[parts.Length - 1]) ? FromRoman(parts[parts.Length - 1]) : 0;
        }

        private static bool HasGeneratedSuffix(string name)
        {
            string[] parts = ColonyFactory.NormalizeDisplayName(name).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;
            string last = parts[parts.Length - 1];
            return string.Equals(last, "Jr", StringComparison.OrdinalIgnoreCase) || IsRoman(last);
        }

        private static bool IsRoman(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++) if ("IVXLCDMivxlcdm".IndexOf(value[i]) < 0) return false;
            return true;
        }

        private static int FromRoman(string value)
        {
            int total = 0;
            int previous = 0;
            for (int i = value.Length - 1; i >= 0; i--)
            {
                char numeral = char.ToUpperInvariant(value[i]);
                int current = numeral == 'I' ? 1 : numeral == 'V' ? 5 : numeral == 'X' ? 10 : numeral == 'L' ? 50 : numeral == 'C' ? 100 : numeral == 'D' ? 500 : 1000;
                if (current < previous) total -= current; else { total += current; previous = current; }
            }
            return total;
        }

        private static string ToRoman(int value)
        {
            int[] numbers = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] numerals = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            string result = string.Empty;
            for (int i = 0; i < numbers.Length; i++) while (value >= numbers[i]) { result += numerals[i]; value -= numbers[i]; }
            return result;
        }

        private struct NameParts
        {
            public string first;
            public string second;
        }

        private static NameParts SplitName(string value)
        {
            string[] parts = ColonyFactory.NormalizeDisplayName(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int namePartCount = parts.Length;
            if (namePartCount > 1 && IsGeneratedSuffixToken(parts[namePartCount - 1])) namePartCount--;
            return new NameParts
            {
                first = namePartCount == 0 ? string.Empty : parts[0],
                second = namePartCount > 1 ? parts[namePartCount - 1] : string.Empty,
            };
        }

        private static bool IsGeneratedSuffixToken(string value)
        {
            if (string.Equals(value, "Jr", StringComparison.OrdinalIgnoreCase) || IsRoman(value)) return true;
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++) if (!char.IsDigit(value[i])) return false;
            return true;
        }

        private static void SetNameParts(RatNameUseData entry, string name, long gameTime)
        {
            NameParts parts = SplitName(name);
            entry.firstName = parts.first;
            entry.secondName = parts.second;
            entry.lastFirstNameUsedGameTime = gameTime;
            entry.lastSecondNameUsedGameTime = string.IsNullOrEmpty(parts.second) ? 0L : gameTime;
        }

        private static RatNameUseData FindHistory(ColonySaveData save, string name)
        {
            string key = NormalizeForComparison(name);
            if (save == null || save.ratNameHistory == null || string.IsNullOrEmpty(key)) return null;
            foreach (RatNameUseData entry in save.ratNameHistory) if (entry != null && entry.normalizedName == key) return entry;
            return null;
        }

        private static long LatestFirstNameUse(ColonySaveData save, string firstName)
        {
            long latest = 0L;
            if (save == null || save.ratNameHistory == null) return latest;
            foreach (RatNameUseData entry in save.ratNameHistory)
                if (entry != null && string.Equals(entry.firstName, firstName, StringComparison.OrdinalIgnoreCase))
                    latest = Math.Max(latest, entry.lastFirstNameUsedGameTime);
            return latest;
        }

        private static long LatestSecondNameUse(ColonySaveData save, string secondName)
        {
            long latest = 0L;
            if (save == null || save.ratNameHistory == null) return latest;
            foreach (RatNameUseData entry in save.ratNameHistory)
                if (entry != null && string.Equals(entry.secondName, secondName, StringComparison.OrdinalIgnoreCase))
                    latest = Math.Max(latest, entry.lastSecondNameUsedGameTime);
            return latest;
        }

        private static void EnsureHistoryEntry(ColonySaveData save, string name, RatSex sex, string ratId, long gameTime)
        {
            if (save == null || string.IsNullOrWhiteSpace(name)) return;
            RatNameUseData entry = FindHistory(save, name);
            if (entry == null)
            {
                entry = new RatNameUseData
                {
                    normalizedName = NormalizeForComparison(name),
                    displayName = ColonyFactory.NormalizeDisplayName(name),
                    sex = sex,
                    ratId = ratId,
                };
                save.ratNameHistory.Add(entry);
            }
            if (entry.lastUsedGameTime <= 0L) entry.lastUsedGameTime = gameTime;
            if (entry.lastFirstNameUsedGameTime <= 0L) SetNameParts(entry, name, entry.lastUsedGameTime);
        }

        private static void EnsureHistoryEntry(ColonySaveData save, string name, RatSex sex,
            string ratId, long gameTime, Dictionary<string, RatNameUseData> historyIndex)
        {
            if (save == null || string.IsNullOrWhiteSpace(name)) return;
            string key = NormalizeForComparison(name);
            if (string.IsNullOrEmpty(key)) return;
            if (!historyIndex.TryGetValue(key, out RatNameUseData entry) || entry == null)
            {
                entry = new RatNameUseData
                {
                    normalizedName = key,
                    displayName = ColonyFactory.NormalizeDisplayName(name),
                    sex = sex,
                    ratId = ratId,
                };
                save.ratNameHistory.Add(entry);
                historyIndex[key] = entry;
            }
            if (entry.lastUsedGameTime <= 0L) entry.lastUsedGameTime = gameTime;
            if (entry.lastFirstNameUsedGameTime <= 0L) SetNameParts(entry, name, entry.lastUsedGameTime);
        }

        private static bool MigrateLegacyNames(ColonySaveData save, long gameTime)
        {
            if (save.ratNameMigrationVersion >= CurrentMigrationVersion) return false;
            bool changed = false;
            foreach (RatData rat in save.rats)
            {
                if (rat == null || rat.nameWasPlayerAssigned || !HasGeneratedSuffix(rat.name)) continue;
                RatData parent = FindDirectNameParent(save, rat);
                if (parent != null && IsSameLineageBase(rat.name, parent.name)) continue;
                string previous = rat.name;
                rat.name = AllocateName(save, rat.id, rat.sex, gameTime, CollectOccupiedNames(save, rat.id));
                RewriteEventNames(save, previous, rat.name);
                changed = true;
            }
            return changed;
        }

        private static void RewriteEventNames(ColonySaveData save, string previous, string replacement)
        {
            if (save == null || save.eventLog == null || string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(replacement) || previous == replacement) return;
            foreach (ColonyEventData eventData in save.eventLog)
                if (eventData != null && !string.IsNullOrEmpty(eventData.message)) eventData.message = eventData.message.Replace(previous, replacement);
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
