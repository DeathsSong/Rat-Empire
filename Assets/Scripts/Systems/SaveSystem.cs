using System;
using System.IO;
using UnityEngine;

namespace RatHabitat
{
    public static class SaveSystem
    {
        private const string FileName = "rat-habitat-save.json";

        public static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, FileName); }
        }

        public static ColonySaveData LoadOrCreate()
        {
            ColonySaveData save = null;
            try
            {
                if (File.Exists(SavePath))
                {
                    string json = File.ReadAllText(SavePath);
                    if (!string.IsNullOrWhiteSpace(json)) save = JsonUtility.FromJson<ColonySaveData>(json);
                }
            }
            catch (Exception exception)
            {
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
                save.schemaVersion = GameConfig.SaveVersion;
                save.updatedAt = GameConfig.NowMs();
                string json = JsonUtility.ToJson(save, true);
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
                if (File.Exists(SavePath)) File.Delete(SavePath);
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
            return save == null ? string.Empty : JsonUtility.ToJson(save, true);
        }

        public static ColonySaveData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var save = JsonUtility.FromJson<ColonySaveData>(json);
                if (save == null) return null;
                save.EnsureLists();
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
    }
}
