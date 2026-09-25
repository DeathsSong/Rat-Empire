using System;

namespace RatHabitat
{
    /// <summary>
    /// Persisted colony upgrades. Upgrade levels are part of ColonySaveData so
    /// the UI can be rebuilt freely without rerolling existing market stock.
    /// </summary>
    public static class UpgradeSystem
    {
        public static void EnsureState(ColonySaveData save)
        {
            if (save == null) return;
            if (save.colonyCapacityUpgradeLevel < 0) save.colonyCapacityUpgradeLevel = 0;
            if (save.storeQualityUpgradeLevel < 0) save.storeQualityUpgradeLevel = 0;
        }

        public static int StoreQualityCap(ColonySaveData save)
        {
            EnsureState(save);
            int level = save == null ? 0 : save.storeQualityUpgradeLevel;
            return GameConfig.BaseStoreQualityCap + level * GameConfig.StoreQualityUpgradeStep;
        }

        public static int ColonyCapacity(ColonySaveData save)
        {
            EnsureState(save);
            int level = save == null ? 0 : save.colonyCapacityUpgradeLevel;
            return GameConfig.BaseColonyCapacity + level * GameConfig.ColonyCapacityUpgradeStep;
        }

        public static int StoreQualityUpgradeCost(ColonySaveData save)
        {
            EnsureState(save);
            int level = save == null ? 0 : save.storeQualityUpgradeLevel;
            return GameConfig.StoreQualityUpgradeBaseCost + level * GameConfig.StoreQualityUpgradeCostStep;
        }

        public static int ColonyCapacityUpgradeCost(ColonySaveData save)
        {
            EnsureState(save);
            int level = save == null ? 0 : save.colonyCapacityUpgradeLevel;
            return GameConfig.ColonyCapacityUpgradeBaseCost + level * GameConfig.ColonyCapacityUpgradeCostStep;
        }

        public static bool PurchaseStoreQualityUpgrade(ColonySaveData save, out int newCap)
        {
            newCap = StoreQualityCap(save);
            if (save == null) return false;
            int cost = StoreQualityUpgradeCost(save);
            if (save.colonyCredits < cost) return false;
            save.colonyCredits -= cost;
            save.storeQualityUpgradeLevel++;
            newCap = StoreQualityCap(save);
            return true;
        }

        public static bool PurchaseColonyCapacityUpgrade(ColonySaveData save, out int newCapacity)
        {
            newCapacity = ColonyCapacity(save);
            if (save == null) return false;
            int cost = ColonyCapacityUpgradeCost(save);
            if (save.colonyCredits < cost) return false;
            save.colonyCredits -= cost;
            save.colonyCapacityUpgradeLevel++;
            newCapacity = ColonyCapacity(save);
            return true;
        }
    }
}
