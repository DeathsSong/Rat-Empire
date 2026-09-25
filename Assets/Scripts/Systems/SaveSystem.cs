using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Persists the colony on every supported platform. WebGL uses an explicit
    /// localStorage bridge because the browser must retain the save outside of
    /// Unity's in-memory state and because IDBFS writes are not guaranteed to
    /// be flushed before a page is closed.
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "rat-habitat-save.json";

        // Keep these keys stable. They are intentionally independent of the
        // Unity build hash so a new GitHub Pages build can read an existing
        // colony from the same origin.
        private const string BrowserSaveKey = "rat-habitat-save-v1";
        private const string BrowserBackupKey = "rat-habitat-save-v1-backup";
        private const string BrowserCorruptBackupKey = "rat-habitat-save-v1-corrupt";
        private const string BrowserStorageVersionKey = "rat-habitat-save-v1-storage-version";
        private const int BrowserStorageVersion = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string RatHabitatBrowserRead(string key);

        [DllImport("__Internal")]
        private static extern int RatHabitatBrowserWrite(string key, string value);

        [DllImport("__Internal")]
        private static extern void RatHabitatBrowserRemove(string key);

        [DllImport("__Internal")]
        private static extern void RatHabitatBrowserRegisterLifecycle(string key);

        [DllImport("__Internal")]
        private static extern void RatHabitatBrowserFlush(string key);
#endif

        private static bool UsesBrowserStorage
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, FileName); }
        }

        /// <summary>
        /// Non-blocking startup/recovery text consumed by GameBootstrap. This
        /// is deliberately separate from the event log so a recovery warning
        /// can be shown even when the player has not opened the log panel.
        /// </summary>
        public static string LastLoadMessage { get; private set; }

        public static ColonySaveData LoadOrCreate()
        {
            LastLoadMessage = string.Empty;
            ColonySaveData save = null;
            string serialized = null;
            string source = string.Empty;

            try
            {
                if (UsesBrowserStorage)
                {
                    RegisterBrowserLifecycle();
                    serialized = ReadBrowser(BrowserSaveKey);
                    if (!string.IsNullOrWhiteSpace(serialized)) source = "browser";
                }

                // Keep the existing desktop/mobile file as a compatible
                // fallback. On WebGL the stable localStorage record is the
                // authoritative source, while other platforms keep using the
                // normal persistent-data file.
                if (string.IsNullOrWhiteSpace(serialized) && File.Exists(SavePath))
                {
                    serialized = File.ReadAllText(SavePath);
                    if (!string.IsNullOrWhiteSpace(serialized)) source = "file";
                }

                if (!string.IsNullOrWhiteSpace(serialized))
                {
                    save = TryParse(serialized);
                    if (save == null)
                    {
                        BackupCorruptSave(serialized, source);
                        save = TryParse(ReadRecoveryBackup(source));
                        if (save != null)
                        {
                            LastLoadMessage = "Save recovered from backup. Your colony was restored safely.";
                        }
                        else if (source == "browser")
                        {
                            // A legacy/partially-written browser record may
                            // still have a valid persistent-data fallback.
                            string fileFallback = TryReadFileSave();
                            save = TryParse(fileFallback);
                            if (save != null)
                                LastLoadMessage = "Browser save recovered from the local backup file.";
                        }

                        if (save == null)
                        {
                            LastLoadMessage = "Save recovery: the previous save was unreadable. A backup was kept and a new colony was started.";
                            Debug.LogWarning("Rat Habitat save recovery started a new colony; the unreadable save was backed up.");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                LastLoadMessage = "Save recovery: the previous save could not be read. A new colony was started safely.";
                Debug.LogWarning("Rat Habitat save could not be loaded: " + exception.Message);
            }

            long now = GameConfig.NowMs();
            // An empty active colony is valid after selling/euthanasia. Do not
            // recreate founders and erase the persisted retired history.
            if (save == null)
            {
                save = ColonyFactory.CreateNew(now);
                Save(save);
                return save;
            }

            save.EnsureLists();
            EventLogPolicy.Prune(save);
            ColonyFactory.MigrateLegacyStarterStats(save);
            StoreSystem.EnsureStoreState(save);
            LitterNameSystem.EnsureLitterNames(save);
            // Older local saves predate the selectable exercise wheel. Add
            // only the missing default habitat record so the new interaction
            // path can expose the already-rendered object without touching any
            // rat, breeding, genetics, or growth data.
            ColonyFactory.EnsureDefaultHabitatObjects(save);
            if (save.schemaVersion <= 0) save.schemaVersion = GameConfig.SaveVersion;
            if (save.clock.gameStartTimestamp <= 0) save.clock.gameStartTimestamp = now;
            if (save.clock.gameTimeMs <= 0) save.clock.gameTimeMs = GameConfig.StartGameTimeMs;
            if (save.clock.speed <= 0f) save.clock.speed = 1f;
            foreach (var rat in save.rats)
            {
                if (rat == null) continue;
                if (rat.genotype == null) rat.genotype = new GenotypeData();
                if (rat.traits == null) rat.traits = new TraitData();
                if (string.IsNullOrEmpty(rat.id)) rat.id = ColonyFactory.NewId("rat");
                // Migrate older saves that only stored the enclosure enum.
                if (rat.enclosure == RatEnclosure.Pairing) rat.pairingHabitatAssigned = true;
                GrowthSystem.EnsureBiologyDefaults(rat);
                GeneticsSystem.Normalize(rat.genotype);
                GeneticsSystem.EnsureCoatAppearance(rat);
                rat.phenotype = GeneticsSystem.DerivePhenotype(rat.stage, rat.genotype,
                    rat.coatColorVariant, rat.coatTone);
                if (string.IsNullOrEmpty(rat.markingFamily)) rat.markingFamily = GeneticsSystem.DefaultMarkingFamily(rat.genotype);
                GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
                if (!save.ratIds.Contains(rat.id)) save.ratIds.Add(rat.id);
            }
            RatActivitySystem.EnsureSaveState(save,
                save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
            GrowthSystem.AdvanceClock(save, now);
            GrowthSystem.RefreshRatStages(save);
            BreedingSystem.RefreshReproductiveStates(save, save.clock.gameTimeMs);
            StoreSystem.AdvanceRestock(save, save.clock.gameTimeMs);
            // Reconcile legacy saves and all relationship-driven placement
            // state before the first scene render. New enclosure fields are
            // backward-compatible with older JSON because this pass derives
            // them from the authoritative stage/sex/pregnancy/litter data.
            EnclosureSystem.RecalculateAssignments(save);
            Save(save);
            return save;
        }

        public static bool Save(ColonySaveData save)
        {
            if (save == null) return false;
            try
            {
                save.EnsureLists();
                EventLogPolicy.Prune(save);
                RatActivitySystem.EnsureSaveState(save, save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
                save.schemaVersion = GameConfig.SaveVersion;
                save.updatedAt = GameConfig.NowMs();
                string json = JsonUtility.ToJson(save, true);

                if (UsesBrowserStorage)
                {
                    RegisterBrowserLifecycle();
                    string previous = ReadBrowser(BrowserSaveKey);
                    // Only replace the recovery backup with a valid previous
                    // document. A corrupt current document must not destroy a
                    // usable backup while recovery is being completed.
                    if (TryParse(previous) != null)
                        WriteBrowser(BrowserBackupKey, previous);
                    if (!WriteBrowser(BrowserSaveKey, json)) return false;
                    WriteBrowser(BrowserStorageVersionKey, BrowserStorageVersion.ToString());
                    // localStorage writes are synchronous; this second call
                    // makes the intended flush explicit for pagehide/unload.
                    FlushBrowser(BrowserSaveKey);
                    return true;
                }

                if (File.Exists(SavePath))
                {
                    string backupPath = SavePath + ".bak";
                    File.Copy(SavePath, backupPath, true);
                }
                File.WriteAllText(SavePath, json);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rat Habitat save could not be written: " + exception.Message);
                return false;
            }
        }

        public static bool DeleteLocalSave()
        {
            try
            {
                if (UsesBrowserStorage)
                {
                    RemoveBrowser(BrowserSaveKey);
                    RemoveBrowser(BrowserBackupKey);
                    RemoveBrowser(BrowserCorruptBackupKey);
                    RemoveBrowser(BrowserStorageVersionKey);
                    FlushBrowser(BrowserSaveKey);
                }

                if (File.Exists(SavePath)) File.Delete(SavePath);
                if (File.Exists(SavePath + ".bak")) File.Delete(SavePath + ".bak");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rat Habitat local save could not be deleted: " + exception.Message);
                return false;
            }
        }

        public static string ToJson(ColonySaveData save)
        {
            if (save == null) return string.Empty;
            save.EnsureLists();
            EventLogPolicy.Prune(save);
            RatActivitySystem.EnsureSaveState(save, save.clock.gameTimeMs);
            return JsonUtility.ToJson(save, true);
        }

        public static ColonySaveData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var save = JsonUtility.FromJson<ColonySaveData>(json);
                if (save == null) return null;
                save.EnsureLists();
                EventLogPolicy.Prune(save);
                RatActivitySystem.EnsureSaveState(save,
                    save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
                ColonyFactory.MigrateLegacyStarterStats(save);
                foreach (var rat in save.rats)
                    if (rat != null && rat.enclosure == RatEnclosure.Pairing) rat.pairingHabitatAssigned = true;
                ColonyFactory.EnsureDefaultHabitatObjects(save);
                StoreSystem.EnsureStoreState(save);
                LitterNameSystem.EnsureLitterNames(save);
                GrowthSystem.RefreshRatStages(save);
                BreedingSystem.RefreshReproductiveStates(save, save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
                StoreSystem.AdvanceRestock(save, save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
                EnclosureSystem.RecalculateAssignments(save);
                return save;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rat Habitat JSON import failed: " + exception.Message);
                return null;
            }
        }

        private static ColonySaveData TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonUtility.FromJson<ColonySaveData>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rat Habitat save JSON is unreadable: " + exception.Message);
                return null;
            }
        }

        private static string TryReadFileSave()
        {
            try
            {
                return File.Exists(SavePath) ? File.ReadAllText(SavePath) : string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rat Habitat fallback save could not be read: " + exception.Message);
                return string.Empty;
            }
        }

        private static string ReadRecoveryBackup(string source)
        {
            if (source == "browser" && UsesBrowserStorage)
                return ReadBrowser(BrowserBackupKey);

            try
            {
                string backupPath = SavePath + ".bak";
                return File.Exists(backupPath) ? File.ReadAllText(backupPath) : string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rat Habitat recovery backup could not be read: " + exception.Message);
                return string.Empty;
            }
        }

        private static void BackupCorruptSave(string serialized, string source)
        {
            try
            {
                if (source == "browser" && UsesBrowserStorage)
                {
                    if (!string.IsNullOrWhiteSpace(serialized))
                        WriteBrowser(BrowserCorruptBackupKey, serialized);
                    return;
                }

                if (File.Exists(SavePath))
                {
                    string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    File.Copy(SavePath, SavePath + ".corrupt-" + stamp, true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Rat Habitat could not preserve the corrupt save backup: " + exception.Message);
            }
        }

        private static void RegisterBrowserLifecycle()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RatHabitatBrowserRegisterLifecycle(BrowserSaveKey);
#endif
        }

        private static string ReadBrowser(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return RatHabitatBrowserRead(key);
#else
            return string.Empty;
#endif
        }

        private static bool WriteBrowser(string key, string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return RatHabitatBrowserWrite(key, value) != 0;
#else
            return false;
#endif
        }

        private static void RemoveBrowser(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RatHabitatBrowserRemove(key);
#endif
        }

        private static void FlushBrowser(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RatHabitatBrowserFlush(key);
#endif
        }
    }

    /// <summary>
    /// Defines the deliberately small set of colony-wide events that may be
    /// shown in the header banner/history. RatActivitySystem remains the
    /// source for detailed per-rat behavior such as breeding, exploring,
    /// sleeping, recovery, and cooldowns.
    /// </summary>
    public static class EventLogPolicy
    {
        public static bool IsAllowed(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string lower = message.Trim().ToLowerInvariant();

            // These are the only global event categories. Match stable event
            // wording rather than a broad keyword so UI guidance, breeding
            // activity, movement, and diagnostics cannot leak into the
            // top-right banner or persistent history.
            return lower.Contains("restocked") ||
                lower.Contains(" died") || lower.StartsWith("died") ||
                lower.Contains(" was sold") ||
                lower.Contains(" became pregnant") ||
                lower.StartsWith("birth:") || lower.Contains(" gave birth");
        }

        /// <summary>
        /// Removes legacy/non-whitelisted entries when an existing browser or
        /// file save is loaded. This keeps old event history from reappearing
        /// after a WebGL refresh while preserving the newest allowed events.
        /// </summary>
        public static bool Prune(ColonySaveData save)
        {
            if (save == null) return false;
            save.EnsureLists();
            bool changed = false;
            for (int index = save.eventLog.Count - 1; index >= 0; index--)
            {
                ColonyEventData entry = save.eventLog[index];
                if (entry == null || !IsAllowed(entry.message))
                {
                    save.eventLog.RemoveAt(index);
                    changed = true;
                }
            }

            while (save.eventLog.Count > 10)
            {
                save.eventLog.RemoveAt(save.eventLog.Count - 1);
                changed = true;
            }
            return changed;
        }
    }
}
