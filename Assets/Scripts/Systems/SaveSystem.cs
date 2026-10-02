using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
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
        private const float PendingSaveDebounceSeconds = 0.35f;
        private const float MinimumQueuedSaveIntervalSeconds = 1.5f;
        private const float BackupRefreshIntervalSeconds = 30f;

        // Browser lifecycle callbacks can arrive while a Unity callback is
        // already saving. Keep the managed side one-shot and re-entrant safe
        // so a pagehide/visibility transition cannot recursively enter the
        // JSON and JS interop path.
        private static bool saveInProgress;
        private static bool browserLifecycleRegistered;
        private static bool browserReadInProgress;
        private static bool browserWriteInProgress;
        private static bool browserRemoveInProgress;
        private static bool browserFlushInProgress;
        private static bool browserLifecyclePollUnavailable;
        private static ColonySaveData pendingSave;
        private static bool pendingSaveDirty;
        private static float pendingSaveDueAt;
        private static float lastSaveWriteAt = float.NegativeInfinity;
        private static int pendingSaveRequestCount;
        private static int pendingSaveCoalescedRequestCount;
        private static string pendingSaveFirstSource;
        private static string pendingSaveLastSource;
        private static string lastKnownValidBrowserSave;
        private static float lastBrowserBackupWriteAt = float.NegativeInfinity;
        private static float lastFileBackupWriteAt = float.NegativeInfinity;

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

        [DllImport("__Internal")]
        private static extern int RatHabitatBrowserConsumeInactiveElapsedSeconds();
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
        public static bool HasPendingSave { get { return pendingSaveDirty; } }
        public static int PendingSaveRequestCount { get { return pendingSaveRequestCount; } }
        public static int PendingSaveCoalescedRequestCount { get { return pendingSaveCoalescedRequestCount; } }

#if UNITY_EDITOR
        // Keeps editor tests from leaking a queued live-save reference into a
        // later test. This intentionally drops only the transient queue; it
        // never writes or deletes persistent save data.
        public static void ClearPendingSaveQueueForTests()
        {
            ClearPendingSaveQueue();
        }
#endif

        public static ColonySaveData LoadOrCreate()
        {
            ClearPendingSaveQueue();
            lastKnownValidBrowserSave = null;
            lastBrowserBackupWriteAt = float.NegativeInfinity;
            lastFileBackupWriteAt = float.NegativeInfinity;
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
                    if (save != null && (source == "browser" || source == "file"))
                        lastKnownValidBrowserSave = serialized;
                    if (save == null)
                    {
                        BackupCorruptSave(serialized, source);
                        string recoveryJson = ReadRecoveryBackup(source);
                        save = TryParse(recoveryJson);
                        if (save != null)
                        {
                            if (source == "browser") lastKnownValidBrowserSave = recoveryJson;
                            LastLoadMessage = "Save recovered from backup. Your colony was restored safely.";
                        }
                        else if (source == "browser")
                        {
                            // A legacy/partially-written browser record may
                            // still have a valid persistent-data fallback.
                            string fileFallback = TryReadFileSave();
                            save = TryParse(fileFallback);
                            if (save != null)
                            {
                                lastKnownValidBrowserSave = fileFallback;
                                LastLoadMessage = "Browser save recovered from the local backup file.";
                            }
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
            EnsureMyRatsSortState(save);
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
            // A new colony may be reloaded while its welcome modal is still
            // waiting for acknowledgement. Do not let the elapsed wall-clock
            // time between browser sessions advance the paused colony.
            if (save.welcomePopupPending || (save.pendingNamingLitterIds != null && save.pendingNamingLitterIds.Count > 0))
                save.clock.lastRealTimestamp = now;
            foreach (var rat in save.rats)
            {
                if (rat == null) continue;
                if (rat.genotype == null) rat.genotype = new GenotypeData();
                if (rat.traits == null) rat.traits = new TraitData();
                if (string.IsNullOrEmpty(rat.id)) rat.id = ColonyFactory.NewId("rat");
                // Migrate older saves that only stored the enclosure enum.
                if (rat.enclosure == RatEnclosure.Pairing) rat.pairingHabitatAssigned = true;
                // Nursing fields were added after the original browser save
                // schema. JsonUtility leaves them at their safe defaults;
                // sanitize malformed negative values without touching valid
                // per-pup rotation timestamps.
                if (rat.lastNursedAt < 0L) rat.lastNursedAt = 0L;
                if (rat.nursingInteractionUntil < 0L) rat.nursingInteractionUntil = 0L;
                if (rat.nursingRetryAt < 0L) rat.nursingRetryAt = 0L;
                if (rat.nursingInteractionUntil > 0L)
                    rat.nursingInteractionType = NursingSystem.NormalizeInteractionId(rat.nursingInteractionType);
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
            RatNameSystem.EnsureUniqueNames(save, save.clock.gameTimeMs);
            // Reconcile legacy saves and all relationship-driven placement
            // state before the first scene render. New enclosure fields are
            // backward-compatible with older JSON because this pass derives
            // them from the authoritative stage/sex/pregnancy/litter data.
            EnclosureSystem.RecalculateAssignments(save);
            Save(save);
            return save;
        }

        public static bool Save(ColonySaveData save, [CallerMemberName] string source = null)
        {
            if (save == null) return false;
            if (saveInProgress) return true;

            bool saved = PersistImmediately(save, source ?? "Save");
            if (saved && pendingSaveDirty && ReferenceEquals(pendingSave, save))
                ClearPendingSaveQueue();
            return saved;
        }

        /// <summary>
        /// Marks the live save dirty and coalesces repeated routine requests.
        /// The first request establishes a deadline; later changes replace the
        /// pending snapshot without postponing that deadline indefinitely.
        /// </summary>
        public static bool QueueSave(ColonySaveData save, [CallerMemberName] string source = null)
        {
            if (save == null) return false;
            string safeSource = string.IsNullOrEmpty(source) ? "Unknown" : source;
            bool coalesced = pendingSaveDirty;
            if (!pendingSaveDirty)
            {
                pendingSaveDirty = true;
                pendingSaveFirstSource = safeSource;
                pendingSaveRequestCount = 0;
                pendingSaveCoalescedRequestCount = 0;
                float now = Time.realtimeSinceStartup;
                pendingSaveDueAt = Mathf.Max(now + PendingSaveDebounceSeconds,
                    lastSaveWriteAt + MinimumQueuedSaveIntervalSeconds);
            }
            else
            {
                pendingSaveCoalescedRequestCount++;
            }
            pendingSave = save;
            pendingSaveLastSource = safeSource;
            pendingSaveRequestCount++;
            RuntimePerformanceDiagnostics.RecordSaveQueueRequest(safeSource, coalesced);
            return true;
        }

        /// <summary>Flushes one queued save at most when its coalescing deadline is due.</summary>
        public static bool FlushPendingSaveIfDue()
        {
            if (!pendingSaveDirty || Time.realtimeSinceStartup < pendingSaveDueAt) return false;
            return FlushPendingSave();
        }

        /// <summary>Persists the newest queued state once, or leaves it queued to retry on failure.</summary>
        public static bool FlushPendingSave()
        {
            if (!pendingSaveDirty || pendingSave == null) return false;
            string source = BuildPendingSaveSource();
            ColonySaveData save = pendingSave;
            if (PersistImmediately(save, source))
            {
                ClearPendingSaveQueue();
                return true;
            }
            pendingSaveDueAt = Time.realtimeSinceStartup + MinimumQueuedSaveIntervalSeconds;
            return false;
        }

        private static string BuildPendingSaveSource()
        {
            if (pendingSaveRequestCount <= 1 || pendingSaveFirstSource == pendingSaveLastSource)
                return pendingSaveLastSource ?? pendingSaveFirstSource ?? "Queued";
            return (pendingSaveFirstSource ?? "Queued") + " → " +
                (pendingSaveLastSource ?? "Queued") + " (" + pendingSaveCoalescedRequestCount + " coalesced)";
        }

        private static void ClearPendingSaveQueue()
        {
            pendingSave = null;
            pendingSaveDirty = false;
            pendingSaveDueAt = 0f;
            pendingSaveRequestCount = 0;
            pendingSaveCoalescedRequestCount = 0;
            pendingSaveFirstSource = null;
            pendingSaveLastSource = null;
        }

        private static bool PersistImmediately(ColonySaveData save, string source)
        {
            if (save == null) return false;
            if (saveInProgress) return true;

            long performanceSaveSample = RuntimePerformanceDiagnostics.Begin(PerformanceProbeArea.SaveStorage);
            saveInProgress = true;
            try
            {
                bool saved = SaveInternal(save, source ?? "Save");
                if (saved) lastSaveWriteAt = Time.realtimeSinceStartup;
                return saved;
            }
            finally
            {
                saveInProgress = false;
                RuntimePerformanceDiagnostics.End(PerformanceProbeArea.SaveStorage, performanceSaveSample);
            }
        }

        private static bool SaveInternal(ColonySaveData save, string source)
        {
            if (save == null) return false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.BeginSample("Rat Empire/Save/Serialize and Storage");
#endif
            try
            {
                save.EnsureLists();
                EnsureMyRatsSortState(save);
                // Finalize any legacy trait-baseline migration before the
                // document is serialized. This keeps explicit zero baselines
                // and inherited values stable even when a caller saves
                // immediately after a birth or stat change.
                ColonyFactory.MigrateLegacyStarterStats(save);
                RatNameSystem.EnsureUniqueNames(save,
                    save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
                EventLogPolicy.Prune(save);
                RatActivitySystem.EnsureSaveState(save, save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
                save.schemaVersion = GameConfig.SaveVersion;
                save.updatedAt = GameConfig.NowMs();
                // Compact JSON materially reduces the synchronous WebGL string
                // and localStorage payload while preserving the exact save data.
                string json = JsonUtility.ToJson(save, false);
                int serializedBytes = Encoding.UTF8.GetByteCount(json);
                RuntimePerformanceDiagnostics.RecordSaveSerializedData(source, serializedBytes);

                if (UsesBrowserStorage)
                {
                    RegisterBrowserLifecycle();
                    float now = Time.realtimeSinceStartup;
                    // The previous save was validated once at load or after a
                    // successful write. Rotate that known-good snapshot only
                    // periodically, instead of reading and deserializing the
                    // entire old colony on every persistence operation.
                    bool backupDue = now - lastBrowserBackupWriteAt >= BackupRefreshIntervalSeconds;
                    if (backupDue && !string.IsNullOrEmpty(lastKnownValidBrowserSave) &&
                        WriteBrowser(BrowserBackupKey, lastKnownValidBrowserSave))
                        lastBrowserBackupWriteAt = now;
                    if (!WriteBrowser(BrowserSaveKey, json))
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        UnityEngine.Profiling.Profiler.EndSample();
#endif
                        return false;
                    }
                    lastKnownValidBrowserSave = json;
                    WriteBrowser(BrowserStorageVersionKey, BrowserStorageVersion.ToString());
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    UnityEngine.Profiling.Profiler.EndSample();
#endif
                    return true;
                }

                if (File.Exists(SavePath) &&
                    Time.realtimeSinceStartup - lastFileBackupWriteAt >= BackupRefreshIntervalSeconds)
                {
                    string backupPath = SavePath + ".bak";
                    File.Copy(SavePath, backupPath, true);
                    lastFileBackupWriteAt = Time.realtimeSinceStartup;
                }
                File.WriteAllText(SavePath, json);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                UnityEngine.Profiling.Profiler.EndSample();
#endif
                return true;
            }
            catch (Exception exception)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                UnityEngine.Profiling.Profiler.EndSample();
#endif
                Debug.LogWarning("Rat Habitat save could not be written: " + exception.Message);
                return false;
            }
        }

        public static bool DeleteLocalSave()
        {
            ClearPendingSaveQueue();
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
            EnsureMyRatsSortState(save);
            ColonyFactory.MigrateLegacyStarterStats(save);
            RatNameSystem.EnsureUniqueNames(save,
                save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
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
                EnsureMyRatsSortState(save);
                EventLogPolicy.Prune(save);
                RatActivitySystem.EnsureSaveState(save,
                    save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
                ColonyFactory.MigrateLegacyStarterStats(save);
                RatNameSystem.EnsureUniqueNames(save,
                    save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs);
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
            if (browserLifecycleRegistered) return;
            browserLifecycleRegistered = true;
            try
            {
                RatHabitatBrowserRegisterLifecycle(BrowserSaveKey);
            }
            catch
            {
                // A missing/unsupported browser bridge must never make the
                // save path re-enter or break game startup.
                browserLifecycleRegistered = false;
            }
#endif
        }

        private static string ReadBrowser(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserReadInProgress) return string.Empty;
            browserReadInProgress = true;
            try
            {
                return RatHabitatBrowserRead(key);
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                browserReadInProgress = false;
            }
#else
            return string.Empty;
#endif
        }

        private static bool WriteBrowser(string key, string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserWriteInProgress) return false;
            browserWriteInProgress = true;
            try
            {
                return RatHabitatBrowserWrite(key, value) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                browserWriteInProgress = false;
            }
#else
            return false;
#endif
        }

        private static void RemoveBrowser(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserRemoveInProgress) return;
            browserRemoveInProgress = true;
            try
            {
                RatHabitatBrowserRemove(key);
            }
            catch
            {
            }
            finally
            {
                browserRemoveInProgress = false;
            }
#endif
        }

        private static void FlushBrowser(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserFlushInProgress) return;
            browserFlushInProgress = true;
            try
            {
                RatHabitatBrowserFlush(key);
            }
            catch
            {
            }
            finally
            {
                browserFlushInProgress = false;
            }
#endif
        }

        /// <summary>
        /// Consumes the browser lifecycle bridge's hidden-page elapsed-time
        /// signal. The persisted real timestamp remains the authoritative
        /// clock input: consuming this signal only makes the resume edge
        /// explicit and calls the same AdvanceClock path used every frame.
        /// This avoids replaying visual frames or applying the speed
        /// multiplier a second time.
        /// </summary>
        public static bool ResumeFromBrowserLifecycle(ColonySaveData save, bool simulationPaused)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (save == null || browserLifecyclePollUnavailable) return false;
            try
            {
                int elapsedSeconds = RatHabitatBrowserConsumeInactiveElapsedSeconds();
                if (elapsedSeconds <= 0) return false;

                long now = GameConfig.NowMs();
                if (simulationPaused)
                {
                    // A welcome modal intentionally pauses the colony. Do
                    // not accumulate background wall time while it is open.
                    if (save.clock != null) save.clock.lastRealTimestamp = now;
                    return true;
                }

                // AdvanceClock reads the existing saved timestamp and the
                // existing speed mapping. No alternate elapsed-time math is
                // introduced here, so 1x/2x/3x remain unchanged.
                return GrowthSystem.AdvanceClock(save, now);
            }
            catch
            {
                // A stale build or unavailable browser export must never
                // break the game loop. Stop polling after the first failure.
                browserLifecyclePollUnavailable = true;
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Drops browser-hidden elapsed time without advancing or editing the
        /// colony clock. Used only by transient performance isolation so the
        /// test mode cannot modify persistent colony data.
        /// </summary>
        public static void DiscardBrowserLifecycleElapsed()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserLifecyclePollUnavailable) return;
            try { RatHabitatBrowserConsumeInactiveElapsedSeconds(); }
            catch { browserLifecyclePollUnavailable = true; }
#endif
        }

        /// <summary>
        /// Normalizes the persisted My Rats sort preference. Earlier builds
        /// used Fertility for the next-opportunity ordering; that ordering is
        /// now named Breeding, while Fertility is the numeric stat sort.
        /// </summary>
        public static void EnsureMyRatsSortState(ColonySaveData save)
        {
            if (save == null) return;
            save.EnsureLists();

            string field = save.myRatsSortField;
            if (string.IsNullOrEmpty(field)) field = "Name";

            switch (field.Trim().ToLowerInvariant())
            {
                case "fertilitynextopportunity":
                case "fertilityopportunity":
                case "fertility (next opportunity)":
                case "next opportunity":
                case "generation":
                    save.myRatsSortField = "Breeding";
                    save.myRatsSortAscending = true;
                    break;
                case "breeding":
                    save.myRatsSortField = "Breeding";
                    save.myRatsSortAscending = true;
                    break;
                case "fertility":
                    save.myRatsSortField = "Fertility";
                    break;
                case "name":
                case "age":
                case "size":
                case "health":
                case "sex":
                case "pregnancy":
                    save.myRatsSortField = char.ToUpperInvariant(field.Trim()[0]) + field.Trim().Substring(1);
                    break;
                default:
                    save.myRatsSortField = "Name";
                    save.myRatsSortAscending = true;
                    break;
            }

            switch ((save.myRatsSexFilter ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "males": save.myRatsSexFilter = "Males"; break;
                case "females": save.myRatsSexFilter = "Females"; break;
                default: save.myRatsSexFilter = "All"; break;
            }
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
        public const string StoreRestocked = "store_restocked";
        public const string Pregnancy = "pregnancy";
        public const string Birth = "birth";
        public const string NaturalDeath = "natural_death";
        public const string Euthanasia = "euthanasia";
        public const string Sale = "sale";
        public const string FullyWeaned = "fully_weaned";
        public const string OtherImportant = "other_important";

        // Keep this list as the single source of truth for the Settings UI,
        // preference migration, and message categorization. New categories
        // can be added here without changing the alert controls.
        public static readonly string[] CategoryIds =
        {
            StoreRestocked,
            Pregnancy,
            Birth,
            NaturalDeath,
            Euthanasia,
            Sale,
            FullyWeaned,
            OtherImportant,
        };

        public static string CategoryLabel(string category)
        {
            switch (category)
            {
                case StoreRestocked: return "Store restocked";
                case Pregnancy: return "Rat became pregnant";
                case Birth: return "Rat gave birth";
                case NaturalDeath: return "Rat died naturally";
                case Euthanasia: return "Rat was euthanized";
                case Sale: return "Rat was sold";
                case FullyWeaned: return "Rat became fully weaned";
                case OtherImportant: return "Other important colony events";
                default: return "Important colony event";
            }
        }

        public static void EnsureAlertPreferences(ColonySaveData save)
        {
            if (save == null) return;
            if (save.alertPreferences == null)
                save.alertPreferences = new List<AlertPreferenceData>();

            for (int index = 0; index < CategoryIds.Length; index++)
            {
                string category = CategoryIds[index];
                bool found = false;
                for (int preferenceIndex = 0; preferenceIndex < save.alertPreferences.Count; preferenceIndex++)
                {
                    AlertPreferenceData preference = save.alertPreferences[preferenceIndex];
                    if (preference == null || !string.Equals(preference.category, category, StringComparison.Ordinal)) continue;
                    found = true;
                    break;
                }

                // New and old saves receive the important-alert defaults. A
                // later explicit toggle is preserved because it already has
                // a preference entry.
                if (!found)
                {
                    save.alertPreferences.Add(new AlertPreferenceData
                    {
                        category = category,
                        enabled = true,
                    });
                }
            }
        }

        public static bool IsCategoryEnabled(ColonySaveData save, string category)
        {
            if (save == null || string.IsNullOrEmpty(category)) return false;
            EnsureAlertPreferences(save);
            for (int index = 0; index < save.alertPreferences.Count; index++)
            {
                AlertPreferenceData preference = save.alertPreferences[index];
                if (preference != null && string.Equals(preference.category, category, StringComparison.Ordinal))
                    return preference.enabled;
            }
            return true;
        }

        public static void SetCategoryEnabled(ColonySaveData save, string category, bool enabled)
        {
            if (save == null || string.IsNullOrEmpty(category)) return;
            EnsureAlertPreferences(save);
            for (int index = 0; index < save.alertPreferences.Count; index++)
            {
                AlertPreferenceData preference = save.alertPreferences[index];
                if (preference != null && string.Equals(preference.category, category, StringComparison.Ordinal))
                {
                    preference.enabled = enabled;
                    return;
                }
            }
            save.alertPreferences.Add(new AlertPreferenceData { category = category, enabled = enabled });
        }

        public static void ResetPreferences(ColonySaveData save)
        {
            if (save == null) return;
            EnsureAlertPreferences(save);
            for (int index = 0; index < CategoryIds.Length; index++)
                SetCategoryEnabled(save, CategoryIds[index], true);
        }

        public static bool AllCategoriesDisabled(ColonySaveData save)
        {
            if (save == null) return false;
            EnsureAlertPreferences(save);
            for (int index = 0; index < CategoryIds.Length; index++)
                if (IsCategoryEnabled(save, CategoryIds[index])) return false;
            return true;
        }

        public static string CategoryForMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            string lower = message.Trim().ToLowerInvariant();

            if (lower.Contains("restocked")) return StoreRestocked;
            if (lower.Contains("became pregnant") || lower.Contains(" is pregnant")) return Pregnancy;
            if (lower.StartsWith("birth:") || lower.Contains(" gave birth") || lower.Contains(" has given birth to")) return Birth;
            if (lower.Contains("was euthanized")) return Euthanasia;
            if (lower.Contains(" was sold") || lower.StartsWith("sold ")) return Sale;
            if (lower.Contains("fully weaned")) return FullyWeaned;
            if (lower.Contains(" died") || lower.StartsWith("died")) return NaturalDeath;

            // Keep this category reserved for future explicit colony-wide
            // announcements. Purchases, upgrades, movement, breeding
            // attempts, activity changes, cooldowns, and diagnostics were
            // intentionally not global alerts in the existing build.
            return null;
        }

        public static bool IsAllowed(string message)
        {
            return CategoryForMessage(message) != null;
        }

        public static bool IsAlertEnabled(ColonySaveData save, ColonyEventData entry)
        {
            if (entry == null) return false;
            string category = string.IsNullOrEmpty(entry.category)
                ? CategoryForMessage(entry.message)
                : entry.category;
            return !string.IsNullOrEmpty(category) && IsCategoryEnabled(save, category);
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
            EnsureAlertPreferences(save);
            bool changed = false;
            for (int index = save.eventLog.Count - 1; index >= 0; index--)
            {
                ColonyEventData entry = save.eventLog[index];
                if (entry == null || !IsAllowed(entry.message))
                {
                    save.eventLog.RemoveAt(index);
                    changed = true;
                }
                else
                {
                    string category = CategoryForMessage(entry.message);
                    if (!string.Equals(entry.category, category, StringComparison.Ordinal))
                    {
                        entry.category = category;
                        changed = true;
                    }
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
