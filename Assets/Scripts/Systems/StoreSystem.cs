using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace RatHabitat
{
    public enum StoreSellFilter
    {
        All,
        Males,
        Females,
        Favorites,
    }

    public struct StoreRestockResult
    {
        public int soldRatCount;
        public int creditedDollars;
        public int skippedRatCount;
        public List<string> skippedRatDetails;
        public string autoSaleMessage;
    }

    /// <summary>
    /// Sell-tab filter selection is presentation state and always starts at
    /// All when the Sell page is opened; it is deliberately not save data.
    /// </summary>
    public sealed class StoreSellFilterState
    {
        public StoreSellFilter Selected { get; private set; } = StoreSellFilter.All;

        public void ResetForSellPanelOpen()
        {
            Selected = StoreSellFilter.All;
        }

        public void Select(StoreSellFilter filter)
        {
            switch (filter)
            {
                case StoreSellFilter.Males:
                case StoreSellFilter.Females:
                case StoreSellFilter.Favorites:
                    Selected = filter;
                    break;
                default:
                    Selected = StoreSellFilter.All;
                    break;
            }
        }
    }

    /// <summary>
    /// Persisted Rat Market inventory and purchase rules. Listings are data,
    /// not UI state, so rebuilding the Store panel never rerolls a rat.
    /// </summary>
    public static class StoreSystem
    {
        /// <summary>
        /// Returns only rats allowed by the authoritative sale rules, then
        /// narrows that eligible set by the Sell-tab filter. UI filters never
        /// grant sale eligibility; request/confirm paths recheck it as well.
        /// </summary>
        public static List<RatData> GetSellableRats(ColonySaveData save, long gameTime, StoreSellFilter filter)
        {
            var result = new List<RatData>();
            if (save == null || save.rats == null) return result;

            for (int index = 0; index < save.rats.Count; index++)
            {
                RatData rat = save.rats[index];
                if (rat == null || !CanSellRat(save, rat, gameTime)) continue;

                bool matches = filter == StoreSellFilter.All ||
                    (filter == StoreSellFilter.Males && rat.sex == RatSex.Male) ||
                    (filter == StoreSellFilter.Females && rat.sex == RatSex.Female) ||
                    (filter == StoreSellFilter.Favorites && rat.isFavorite);
                if (matches) result.Add(rat);
            }
            return result;
        }

        public struct PurchasePriceBreakdown
        {
            public int basePrice;
            public int traitAdjustment;
            public int markingPremium;
            public int finalPrice;
        }

        // Weighted toward common pet-rat patterns while keeping rarer families
        // in the market. Solid/Self are excluded: the listing-level chance
        // roll handles unmarked rats separately. The selected family is saved.
        private static readonly string[] MarkingFamilies =
        {
            "Hooded", "Hooded", "Hooded", "Hooded", "Hooded", "Hooded",
            "Berkshire", "Berkshire", "Berkshire", "Berkshire", "Berkshire",
            "Broken hooded", "Broken hooded", "Broken hooded", "Broken hooded",
            "Bareback", "Bareback", "Bareback",
            "Capped", "Capped", "Capped",
            "Mask", "Mask", "Mask",
            "Patch", "Patch", "Patch",
            "Irish", "Irish", "Irish",
            "Blaze", "Blaze", "Blaze",
            "Black-eye white", "Variegated", "Variberk", "Lightning blaze Siamese",
            "Badger blaze Siamese", "Dalmatian-style", "Dominant white spotted",
            "White side", "Merle", "Tabby/Marble", "Mismarked hooded"
        };

        public static void EnsureStoreState(ColonySaveData save)
        {
            if (save == null) return;
            save.EnsureLists();
            UpgradeSystem.EnsureState(save);
            if (!save.currencyInitialized)
            {
                // Older saves used the same serialized integer for the
                // colony balance. Preserve it when present and only seed a
                // new balance when the old field was empty.
                if (save.colonyCredits <= 0) save.colonyCredits = GameConfig.StartingColonyCredits;
                save.currencyInitialized = true;
            }

            long currentGameTime = save.clock == null || save.clock.gameTimeMs <= 0L
                ? GameConfig.StartGameTimeMs
                : save.clock.gameTimeMs;

            if (!save.storeInventoryInitialized)
            {
                save.storeRestockCycle = 0;
                CreateInventory(save, StableHash((save.createdAt == 0 ? GameConfig.NowMs() : save.createdAt).ToString()), 0);
                save.storeInventoryInitialized = true;
                save.storeNextRestockGameTime = currentGameTime + GameConfig.StoreRestockIntervalGameMs;
                RatNameSystem.EnsureUniqueNames(save, currentGameTime);
                RecordListingNameUsage(save, currentGameTime);
                return;
            }

            // Migrate older saves that already had listings but predate the
            // persisted restock deadline and marking-family field.
            RepairListings(save);
            if (save.storeNextRestockGameTime <= 0L)
                save.storeNextRestockGameTime = currentGameTime + GameConfig.StoreRestockIntervalGameMs;
        }

        public static bool AdvanceRestock(ColonySaveData save, long gameTime)
        {
            StoreRestockResult ignored;
            return AdvanceRestock(save, gameTime, out ignored);
        }

        public static bool AdvanceRestock(ColonySaveData save, long gameTime, out StoreRestockResult result)
        {
            result = default(StoreRestockResult);
            if (save == null) return false;
            EnsureStoreState(save);
            if (save.storeNextRestockGameTime <= 0L)
                save.storeNextRestockGameTime = gameTime + GameConfig.StoreRestockIntervalGameMs;
            if (gameTime < save.storeNextRestockGameTime) return false;

            long interval = Math.Max(1L, GameConfig.StoreRestockIntervalGameMs);
            long overdueIntervals = (gameTime - save.storeNextRestockGameTime) / interval;
            long cyclesDue = overdueIntervals + 1L;
            long firstDueAt = save.storeNextRestockGameTime;
            result = RestockNowWithResult(save, gameTime, cyclesDue, firstDueAt);
            return true;
        }

        public static void RestockNow(ColonySaveData save, long gameTime)
        {
            RestockNowWithResult(save, gameTime);
        }

        public static StoreRestockResult RestockNowWithResult(ColonySaveData save, long gameTime)
        {
            return RestockNowWithResult(save, gameTime, 1, gameTime);
        }

        private static StoreRestockResult RestockNowWithResult(
            ColonySaveData save,
            long gameTime,
            long elapsedRestockCycles,
            long firstDueAt)
        {
            if (save == null) return default(StoreRestockResult);
            save.EnsureLists();
            StoreRestockResult result = AutomaticallySellForSaleRats(save, gameTime);
            long nextCycle = (long)Math.Max(0, save.storeRestockCycle) + Math.Max(1L, elapsedRestockCycles);
            save.storeRestockCycle = (int)Math.Min(int.MaxValue, nextCycle);
            int seed = StableHash((save.createdAt == 0 ? GameConfig.NowMs() : save.createdAt) +
                "|restock|" + save.storeRestockCycle);
            CreateInventory(save, seed, save.storeRestockCycle);
            save.storeInventoryInitialized = true;
            long interval = Math.Max(1L, GameConfig.StoreRestockIntervalGameMs);
            long cycles = Math.Max(1L, elapsedRestockCycles);
            long maxCycles = (long.MaxValue - firstDueAt) / interval;
            save.storeNextRestockGameTime = cycles > maxCycles
                ? long.MaxValue
                : firstDueAt + cycles * interval;
            RatNameSystem.EnsureUniqueNames(save, gameTime);
            RecordListingNameUsage(save, gameTime);
            result.autoSaleMessage = BuildAutomaticSaleMessage(result.skippedRatDetails, result);
            if (!string.IsNullOrEmpty(result.autoSaleMessage))
                RecordAutomaticSaleEvent(save, gameTime, result.autoSaleMessage);
            return result;
        }

        private static StoreRestockResult AutomaticallySellForSaleRats(ColonySaveData save, long gameTime)
        {
            var result = new StoreRestockResult { skippedRatDetails = new List<string>() };
            if (save == null || save.rats == null) return result;

            var retiredIds = new HashSet<string>(StringComparer.Ordinal);
            if (save.retiredRats != null)
                foreach (RatData retired in save.retiredRats)
                    if (retired != null && !string.IsNullOrEmpty(retired.id)) retiredIds.Add(retired.id);

            for (int index = save.rats.Count - 1; index >= 0; index--)
            {
                RatData rat = save.rats[index];
                if (rat == null || rat.enclosure != RatEnclosure.ForSale) continue;

                string reason = AutomaticSaleRestriction(save, rat, gameTime, retiredIds);
                if (!string.IsNullOrEmpty(reason))
                {
                    result.skippedRatCount++;
                    if (result.skippedRatDetails.Count < 10)
                        result.skippedRatDetails.Add(ColonyFactory.DisplayName(rat) + " (" + reason + ")");
                    continue;
                }

                int saleValue = CalculateSaleValue(save, rat, gameTime);
                if (saleValue <= 0)
                {
                    result.skippedRatCount++;
                    if (result.skippedRatDetails.Count < 10)
                        result.skippedRatDetails.Add(ColonyFactory.DisplayName(rat) + " (not eligible for sale)");
                    continue;
                }

                // Removing the active record before recording the payout is
                // the idempotence guard: later restocks and reloads only scan
                // active For Sale residents, while the retired record keeps
                // every lineage field intact for family history.
                save.rats.RemoveAt(index);
                BreedingSystem.CancelDedicatedSessionsForRat(save, rat.id, gameTime);
                rat.removalDisposition = RatRemovalDisposition.Sold;
                rat.removedAt = gameTime;
                EnclosureSystem.ClearSaleReturnTank(rat);
                rat.reproductiveState = ReproductiveState.Infertile;
                rat.pregnancyId = null;
                RatActivitySystem.SetCurrent(save, rat, "sold", "Sold", gameTime, "Automatically sold");
                save.retiredRats.Add(rat);
                RatNameSystem.RecordUsage(save, rat.name, rat.sex, rat.id, gameTime);
                save.colonyCredits += saleValue;
                save.lifetimeSaleCredits += saleValue;
                result.soldRatCount++;
                result.creditedDollars += saleValue;
                if (!string.IsNullOrEmpty(rat.id)) retiredIds.Add(rat.id);
            }
            return result;
        }

        private static string AutomaticSaleRestriction(
            ColonySaveData save, RatData rat, long gameTime, HashSet<string> retiredIds)
        {
            if (rat == null) return "missing rat record";
            if (rat.removalDisposition != RatRemovalDisposition.None ||
                (!string.IsNullOrEmpty(rat.id) && retiredIds != null && retiredIds.Contains(rat.id)))
                return "already retired";
            if (!CanSellRat(save, rat, gameTime))
            {
                string saleReason = SaleRestrictionReason(save, rat, gameTime);
                return string.IsNullOrEmpty(saleReason) ? "not eligible for sale" : saleReason;
            }
            if (EnclosureSystem.IsPregnant(save, rat) || rat.reproductiveState == ReproductiveState.Pregnant)
                return "pregnant";
            if (rat.nursing || EnclosureSystem.HasDependentPinkies(save, rat.id))
                return "nursing with dependent pinkies";
            if (rat.pairingHabitatAssigned)
                return "assigned to Pairing Tank";
            return string.Empty;
        }

        private static string BuildAutomaticSaleMessage(List<string> skippedDetails, StoreRestockResult result)
        {
            if (result.soldRatCount <= 0 && result.skippedRatCount <= 0) return string.Empty;
            string message = result.soldRatCount + (result.soldRatCount == 1
                ? " rat automatically sold for $" : " rats automatically sold for $") +
                result.creditedDollars + ".";
            if (result.skippedRatCount <= 0) return message;
            if (result.soldRatCount == 0) message = "No rats automatically sold.";
            message += " " + result.skippedRatCount + " left in For Sale Tank: ";
            if (skippedDetails != null && skippedDetails.Count > 0)
                message += string.Join(", ", skippedDetails.ToArray());
            else
                message += "sale restriction applies";
            if (result.skippedRatCount > (skippedDetails == null ? 0 : skippedDetails.Count))
                message += ", and others";
            return message;
        }

        private static void RecordAutomaticSaleEvent(ColonySaveData save, long gameTime, string message)
        {
            if (save == null || string.IsNullOrWhiteSpace(message)) return;
            save.eventLog.Insert(0, new ColonyEventData
            {
                gameTimeMs = gameTime,
                message = message,
                category = EventLogPolicy.Sale,
            });
            while (save.eventLog.Count > 10) save.eventLog.RemoveAt(save.eventLog.Count - 1);
        }

        /// <summary>
        /// Generates the randomized male/female founders for a new colony.
        /// It deliberately uses the same name, genotype, coat-variation, and
        /// marking pipeline as market listings, but supplies the separate
        /// absolute 0-15 beginner-stat range and an adult starting age.
        /// </summary>
        public static void CreateStarterPair(long gameTime, int seed, out RatData female, out RatData male)
        {
            var random = new Random(seed);
            float sharedBaseline = NextTrait(random,
                GameConfig.StarterBeginnerTraitMinimum,
                GameConfig.StarterBeginnerTraitMaximum);
            // The founders establish a solid-color breeding line. The S-locus
            // remains s/s, so later spotted offspring can arise through the
            // existing inherited-allele mutation path.
            const string maleFamily = "Solid";
            const string femaleFamily = "Solid";

            string maleId = "starter_male_" + Math.Abs(seed).ToString("X8");
            string femaleId = "starter_female_" + Math.Abs(seed).ToString("X8");
            // Keep the existing random stream aligned so changing the name
            // selector does not also change the founders' genetics or stats.
            random.Next(GameConfig.MaleRatNames.Length);
            male = CreateGeneratedAdultRat(maleId, RatSex.Male,
                RatNameSystem.GeneratedName(maleId, RatSex.Male), maleFamily, random, sharedBaseline,
                gameTime, GameConfig.StarterMaleMinimumAgeDays, GameConfig.StarterMaleMaximumAgeDays,
                true);
            random.Next(GameConfig.FemaleRatNames.Length);
            female = CreateGeneratedAdultRat(femaleId, RatSex.Female,
                RatNameSystem.GeneratedName(femaleId, RatSex.Female), femaleFamily, random, sharedBaseline,
                gameTime, GameConfig.StarterFemaleMinimumAgeDays, GameConfig.StarterFemaleMaximumAgeDays,
                true);

            // Extremely unlikely identical draws should still produce a pair
            // that is not a stat-for-stat clone while remaining within 0-15.
            if (Math.Abs(male.traits.size - female.traits.size) < 0.001f &&
                Math.Abs(male.traits.health - female.traits.health) < 0.001f &&
                Math.Abs(male.traits.fertility - female.traits.fertility) < 0.001f)
            {
                male.traits.fertility = Math.Min(GameConfig.StarterBeginnerTraitMaximum,
                    male.traits.fertility + 1f);
                male.baseFertility = male.traits.fertility;
            }

            // The randomized starter pair is placed together in the main
            // Pairing Habitat so the first breeding loop is immediately
            // playable. This is an explicit player-assigned placement and is
            // preserved by EnclosureSystem during save/load.
            male.enclosure = RatEnclosure.Pairing;
            male.pairingHabitatAssigned = true;
            female.enclosure = RatEnclosure.Pairing;
            female.pairingHabitatAssigned = true;
        }

        public static string GetRestockLabel(ColonySaveData save, long gameTime)
        {
            if (save == null) return "Market schedule unavailable.";
            EnsureStoreState(save);
            long remaining = Math.Max(0L, save.storeNextRestockGameTime - gameTime);
            long days = remaining / GameConfig.GameDayMs;
            long hours = (remaining % GameConfig.GameDayMs) / (60L * 60L * 1000L);
            if (days > 0L) return "Next restock in " + days + "d " + hours + "h";
            long minutes = (remaining % (60L * 60L * 1000L)) / (60L * 1000L);
            return "Next restock in " + hours + "h " + minutes + "m";
        }

        public static StoreRatListingData FindListing(ColonySaveData save, string listingId)
        {
            if (save == null || string.IsNullOrEmpty(listingId)) return null;
            foreach (var listing in save.storeRatListings)
            {
                if (listing != null && listing.id == listingId) return listing;
            }
            return null;
        }

        public static RatData CreatePreviewRat(StoreRatListingData listing, long gameTime)
        {
            if (listing == null) return null;
            RatData rat = ColonyFactory.CreateRat(
                listing.id,
                listing.name,
                listing.sex,
                gameTime - (30L * GameConfig.GameDayMs),
                0,
                listing.genotype == null ? new GenotypeData() : listing.genotype.Clone(),
                CloneTraits(listing.traits),
                RatStage.Adult);
            // Store listings are adult previews. Give the preview the same
            // persisted biological maturity curve as a purchased rat instead
            // of leaving ageDays at CreateRat's newborn default, which would
            // make the new age-based visual scale render an adult listing as
            // a tiny pinkie.
            GrowthSystem.EnsureBiologyDefaults(rat);
            float adultPreviewAge = Math.Max(30f, rat.sexualMaturityDays);
            rat.birthTimestamp = gameTime - (long)(adultPreviewAge * GameConfig.GameDayMs);
            rat.growthTimestamp = rat.birthTimestamp;
            rat.ageDays = adultPreviewAge;
            rat.stage = GrowthSystem.StageForAge(rat);
            if (!string.IsNullOrEmpty(listing.coatColorVariant)) rat.coatColorVariant = listing.coatColorVariant;
            if (listing.coatTone > 0f) rat.coatTone = listing.coatTone;
            rat.phenotype = GeneticsSystem.DerivePhenotype(RatStage.Adult, rat.genotype,
                rat.coatColorVariant, rat.coatTone);
            rat.markingFamily = GeneticsSystem.NormalizeMarkingFamily(listing.markingFamily, rat.genotype);
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            return rat;
        }

        public static RatData CreatePurchasedRat(StoreRatListingData listing, long gameTime)
        {
            if (listing == null) return null;
            RatData rat = CreatePreviewRat(listing, gameTime);
            rat.id = ColonyFactory.NewId("store_rat");
            rat.isFavorite = false;
            rat.enclosure = rat.sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony;
            return rat;
        }

        /// <summary>
        /// Returns whether an active rat may be sold. This is intentionally
        /// based on the authoritative saved age, litter weaning timestamp,
        /// and individual breeding cutoff rather than on a UI label.
        /// </summary>
        public static bool CanSellRat(RatData rat)
        {
            // Legacy callers without a save context can still enforce the
            // age/stage part of the rule. GameBootstrap uses the overload
            // below so it can verify the persisted litter deadline as well.
            return rat != null && !GrowthSystem.IsElderly(rat) &&
                rat.ageDays >= GameConfig.PupSaleMinimumAgeDays;
        }

        public static bool CanSellRat(ColonySaveData save, RatData rat, long gameTime)
        {
            if (rat == null || GrowthSystem.IsElderly(rat)) return false;
            if (!IsPup(rat)) return true;
            if (rat.ageDays < GameConfig.PupSaleMinimumAgeDays) return false;
            return IsFullyWeaned(save, rat, gameTime);
        }

        /// <summary>
        /// Returns the player-facing reason for a blocked sale. This method is
        /// shared by profile, Store, My Rats, and the underlying action guard
        /// so a stale button cannot bypass the same restriction.
        /// </summary>
        public static string SaleRestrictionReason(ColonySaveData save, RatData rat, long gameTime)
        {
            if (rat == null) return "That rat is no longer available for sale.";
            if (GrowthSystem.IsElderly(rat)) return "Elderly rats cannot be sold.";
            if (!IsPup(rat)) return string.Empty;

            LitterData litter = FindLitterForPup(save, rat);
            long weaningAt = litter == null ? 0L : litter.weaningTimestamp;
            bool weaned = IsFullyWeaned(save, rat, gameTime);
            float ageRemaining = Mathf.Max(0f, GameConfig.PupSaleMinimumAgeDays - rat.ageDays);
            float weaningRemaining = weaningAt > gameTime
                ? Mathf.Max(0f, (weaningAt - gameTime) / (float)GameConfig.GameDayMs)
                : 0f;

            if (ageRemaining > 0f && weaningRemaining > 0f)
            {
                if (weaningRemaining >= ageRemaining)
                    return "Too young to sell — weaning completes in " + FormatRemainingDays(weaningRemaining);
                return "Too young to sell — available in " + FormatRemainingDays(ageRemaining);
            }
            if (ageRemaining > 0f)
            {
                return "Too young to sell — available in " + FormatRemainingDays(ageRemaining);
            }
            if (!weaned)
            {
                return weaningRemaining > 0f
                    ? "Not fully weaned — weaning completes in " + FormatRemainingDays(weaningRemaining)
                    : "Not fully weaned.";
            }
            return string.Empty;
        }

        /// <summary>
        /// Preserves the established health/fertility sale formula, then
        /// applies the breeding system's same smooth age-effectiveness factor
        /// once decline begins. Elderly rats intentionally have no price.
        /// </summary>
        public static int CalculateSaleValue(RatData rat)
        {
            if (rat == null) return GameConfig.SellCreditBase;
            if (!CanSellRat(rat)) return 0;

            TraitData traits = rat.traits;
            if (traits == null) return GameConfig.SellCreditBase;
            float quality = (traits.health + traits.fertility) * 0.5f;
            float baseValue = GameConfig.SellCreditBase + quality * GameConfig.SellCreditTraitMultiplier;
            float ageMultiplier = Mathf.Lerp(GameConfig.MatureSaleValueMinimumMultiplier, 1f,
                BreedingSystem.AgeBreedingEffectiveness(rat));
            return Mathf.Max(1, Mathf.RoundToInt(baseValue * ageMultiplier));
        }

        /// <summary>
        /// Purchase formula for newly generated or legacy-migrated listings.
        /// Prices are saved on each listing, so this function is not called by
        /// normal UI refreshes to reroll an existing listing's price.
        /// </summary>
        public static int CalculatePurchasePrice(TraitData traits, string markingFamily, GenotypeData genotype)
        {
            return GetPurchasePriceBreakdown(traits, markingFamily, genotype).finalPrice;
        }

        public static PurchasePriceBreakdown GetPurchasePriceBreakdown(
            TraitData traits, string markingFamily, GenotypeData genotype)
        {
            traits = traits ?? new TraitData();
            float averageQuality = (Mathf.Clamp(traits.health, 0f, 100f) +
                Mathf.Clamp(traits.fertility, 0f, 100f)) * 0.5f;
            var breakdown = new PurchasePriceBreakdown
            {
                basePrice = GameConfig.StorePurchaseBasePrice,
                // Round half dollars upward, matching ordinary currency
                // expectations (for example, average quality 12.5 -> $13).
                traitAdjustment = Mathf.FloorToInt(
                    averageQuality * GameConfig.StorePurchaseTraitMultiplier + 0.5f),
                markingPremium = CalculateVisibleMarkingPremium(markingFamily, genotype),
            };
            breakdown.finalPrice = breakdown.basePrice + breakdown.traitAdjustment + breakdown.markingPremium;
            return breakdown;
        }

        private static int CalculateVisibleMarkingPremium(string markingFamily, GenotypeData genotype)
        {
            if (GeneticsSystem.IsAlbinoGenotype(genotype) ||
                string.Equals((markingFamily ?? string.Empty).Trim(), "Albino masking", StringComparison.OrdinalIgnoreCase))
                return 0;

            string normalized = GeneticsSystem.NormalizeMarkingFamily(markingFamily, genotype);
            if (string.Equals(normalized, "Solid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Self", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Albino masking", StringComparison.OrdinalIgnoreCase))
                return 0;

            switch (normalized)
            {
                case "Variegated":
                case "Variberk":
                case "Black-eye white":
                case "White side":
                case "Dalmatian-style":
                case "Dominant white spotted":
                case "Merle":
                case "Tabby/Marble":
                case "Lightning blaze Siamese":
                case "Badger blaze Siamese":
                case "Mismarked hooded":
                    return GameConfig.StorePurchaseRareMarkingPremium;
                default:
                    return GameConfig.StorePurchaseCommonMarkingPremium;
            }
        }

        public static int CalculateSaleValue(ColonySaveData save, RatData rat, long gameTime)
        {
            if (rat == null) return GameConfig.SellCreditBase;
            if (!CanSellRat(save, rat, gameTime)) return 0;

            TraitData traits = rat.traits;
            if (traits == null) return GameConfig.SellCreditBase;
            float quality = (traits.health + traits.fertility) * 0.5f;
            float baseValue = GameConfig.SellCreditBase + quality * GameConfig.SellCreditTraitMultiplier;
            float ageMultiplier = Mathf.Lerp(GameConfig.MatureSaleValueMinimumMultiplier, 1f,
                BreedingSystem.AgeBreedingEffectiveness(rat));
            return Mathf.Max(1, Mathf.RoundToInt(baseValue * ageMultiplier));
        }

        private static bool IsPup(RatData rat)
        {
            return rat != null && (rat.ageDays < GameConfig.PupSaleMinimumAgeDays ||
                rat.stage == RatStage.Pinkie || rat.stage == RatStage.YoungRat);
        }

        private static bool IsFullyWeaned(ColonySaveData save, RatData rat, long gameTime)
        {
            LitterData litter = FindLitterForPup(save, rat);
            if (litter == null || litter.weaningTimestamp <= 0L)
            {
                // Legacy records may have no litter row. Once the biological
                // weaning age has passed, they are safe to treat as weaned;
                // younger records remain blocked conservatively.
                return rat != null && rat.ageDays >= GameConfig.WeaningDays;
            }
            return gameTime >= litter.weaningTimestamp;
        }

        private static LitterData FindLitterForPup(ColonySaveData save, RatData rat)
        {
            return BreedingSystem.FindLitterForPup(save, rat);
        }

        private static string FormatRemainingDays(float days)
        {
            if (days <= 0.01f) return "less than 1 hour";
            int wholeDays = Mathf.FloorToInt(days);
            int hours = Mathf.Clamp(Mathf.CeilToInt((days - wholeDays) * 24f), 0, 23);
            if (wholeDays > 0 && hours > 0) return wholeDays + " days, " + hours + " hours";
            if (wholeDays > 0) return wholeDays + (wholeDays == 1 ? " day" : " days");
            return hours + (hours == 1 ? " hour" : " hours");
        }

        private static void CreateInventory(ColonySaveData save, int seed, int cycle)
        {
            var random = new Random(seed);
            save.storeRatListings.Clear();
            int qualityCap = UpgradeSystem.StoreQualityCap(save);
            int listingCount = UpgradeSystem.StoreListingCount(save);

            // Generate listings from one shared low-stat baseline. Each rat
            // gets a small deterministic deviation, so the market feels
            // coordinated without becoming identical clones.
            float sharedBaseline = NextTrait(random, GameConfig.StoreLowTraitMinimum, qualityCap);

            long namingGameTime = save.clock == null ? GameConfig.StartGameTimeMs : save.clock.gameTimeMs;
            var usedMarkedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < listingCount; index++)
            {
                RatSex sex = index % 2 == 0 ? RatSex.Male : RatSex.Female;
                string family = PickMarketMarkingFamily(random, usedMarkedFamilies);
                string sexName = sex == RatSex.Male ? "male" : "female";
                // Preserve legacy identifiers for the original two cards;
                // subsequent entries add the index and remain stable per restock.
                string id = index == 0
                    ? "store_adult_male_" + cycle
                    : index == 1
                        ? "store_adult_female_" + cycle
                        : "store_adult_" + sexName + "_" + cycle + "_" + index;
                string name = RatNameSystem.GenerateAvailableName(save, id, sex, namingGameTime, seed);
                save.storeRatListings.Add(CreateListing(
                    id, sex, name, family, random, sharedBaseline, qualityCap));
            }
        }

        private static void RecordListingNameUsage(ColonySaveData save, long gameTime)
        {
            if (save == null || save.storeRatListings == null) return;
            foreach (StoreRatListingData listing in save.storeRatListings)
                if (listing != null)
                    RatNameSystem.RecordUsage(save, listing.name, listing.sex, listing.id, gameTime);
        }

        private static RatData CreateGeneratedAdultRat(
            string id,
            RatSex sex,
            string name,
            string markingFamily,
            Random random,
            float sharedBaseline,
            long gameTime,
            float minimumAgeDays,
            float maximumAgeDays,
            bool starter)
        {
            var genotype = CreateGenotype(random, markingFamily, allowAlbino: !starter);
            GeneticsSystem.Normalize(genotype);
            float minimum = starter ? GameConfig.StarterBeginnerTraitMinimum : GameConfig.StoreLowTraitMinimum;
            float maximum = starter ? GameConfig.StarterBeginnerTraitMaximum : GameConfig.StoreLowTraitMaximum;
            float ageDays = (float)(minimumAgeDays + random.NextDouble() * (maximumAgeDays - minimumAgeDays));
            RatData rat = ColonyFactory.CreateRat(
                id,
                name,
                sex,
                gameTime - (long)(ageDays * GameConfig.GameDayMs),
                0,
                genotype,
                new TraitData(
                    NearSharedTrait(random, sharedBaseline, minimum, maximum),
                    NearSharedTrait(random, sharedBaseline, minimum, maximum),
                    NearSharedTrait(random, sharedBaseline, minimum, maximum)),
                RatStage.Adult);
            rat.isStarterRat = starter;
            // CreateRat initializes the persistent sexual-maturity threshold
            // from the same stable ID used by the biology system. Keep the
            // randomized starting age just above that threshold so both
            // founders are genuinely adult and the early pair is usable.
            if (starter)
                ageDays = Math.Min(maximumAgeDays, Math.Max(ageDays, rat.sexualMaturityDays + 1f));
            rat.birthTimestamp = gameTime - (long)(ageDays * GameConfig.GameDayMs);
            rat.growthTimestamp = rat.birthTimestamp;
            rat.estrousCycleAnchorGameTime = rat.birthTimestamp +
                (long)(rat.sexualMaturityDays * GameConfig.GameDayMs);
            rat.ageDays = ageDays;
            rat.baseHealth = rat.traits.health;
            rat.baseFertility = rat.traits.fertility;
            rat.coatColorVariant = starter
                ? GameConfig.StarterSolidCoatColorVariant
                : GeneticsSystem.DefaultCoatColorVariant(id, genotype);
            rat.coatTone = starter ? 1f : GeneticsSystem.DefaultCoatTone(id, genotype);
            rat.phenotype = GeneticsSystem.DerivePhenotype(RatStage.Adult, genotype,
                rat.coatColorVariant, rat.coatTone);
            rat.markingFamily = GeneticsSystem.NormalizeMarkingFamily(markingFamily, genotype);
            GeneticsSystem.ApplyMarkingFamily(rat.phenotype, rat.markingFamily);
            return rat;
        }

        private static StoreRatListingData CreateListing(
            string id,
            RatSex sex,
            string name,
            string markingFamily,
            Random random,
            float sharedBaseline,
            int qualityCap)
        {
            var genotype = CreateGenotype(random, markingFamily);
            GeneticsSystem.Normalize(genotype);
            float minimum = GameConfig.StoreLowTraitMinimum;
            float maximum = qualityCap;
            string coatColorVariant = GeneticsSystem.DefaultCoatColorVariant(id, genotype);
            var traits = new TraitData(
                NearSharedTrait(random, sharedBaseline, minimum, maximum),
                NearSharedTrait(random, sharedBaseline, minimum, maximum),
                NearSharedTrait(random, sharedBaseline, minimum, maximum));
            return new StoreRatListingData
            {
                id = id,
                name = name,
                sex = sex,
                price = CalculatePurchasePrice(traits, markingFamily, genotype),
                markingFamily = markingFamily,
                coatColorVariant = coatColorVariant,
                coatTone = GeneticsSystem.DefaultCoatTone(id, genotype),
                genotype = genotype,
                traits = traits,
                pricingVersion = GameConfig.StorePurchasePricingVersion,
            };
        }

        private static GenotypeData CreateGenotype(Random random, string markingFamily, bool allowAlbino = true)
        {
            string blackAllele = random.Next(2) == 0 ? "B" : "b";
            string otherBlackAllele = random.Next(2) == 0 ? "B" : "b";
            string diluteAllele = random.Next(3) == 0 ? "d" : "D";
            string otherDiluteAllele = random.Next(3) == 0 ? "d" : "D";
            bool albino = allowAlbino && random.NextDouble() < 0.08;
            string c1 = albino ? "c" : (random.Next(4) == 0 ? "c" : "C");
            string c2 = albino ? "c" : (random.Next(4) == 0 ? "c" : "C");
            bool solid = string.Equals(markingFamily, "Solid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(markingFamily, "Self", StringComparison.OrdinalIgnoreCase);
            string s1 = solid ? "s" : "S";
            string s2 = solid ? "s" : (random.Next(2) == 0 ? "S" : "s");
            return GeneticsSystem.CreateFounder(
                blackAllele, otherBlackAllele,
                c1, c2,
                diluteAllele, otherDiluteAllele,
                s1, s2);
        }

        private static float NextTrait(Random random, float minimum, float maximum)
        {
            return (float)(minimum + (random.NextDouble() * (maximum - minimum)));
        }

        private static float NearSharedTrait(Random random, float baseline, float minimum, float maximum)
        {
            float deviation = (float)((random.NextDouble() * 2.0 - 1.0) * 4.0);
            return Math.Max(minimum, Math.Min(maximum, baseline + deviation));
        }

        private static string PickMarkingFamily(Random random, HashSet<string> usedFamilies)
        {
            int availableWeight = 0;
            for (int index = 0; index < MarkingFamilies.Length; index++)
                if (!usedFamilies.Contains(MarkingFamilies[index]))
                    availableWeight++;

            // When every distinct marking has already appeared in a very
            // large upgraded market, resume weighted selection with repeats.
            if (availableWeight == 0)
                return MarkingFamilies[random.Next(MarkingFamilies.Length)];

            int selectedWeight = random.Next(availableWeight);
            for (int index = 0; index < MarkingFamilies.Length; index++)
            {
                string family = MarkingFamilies[index];
                if (usedFamilies.Contains(family)) continue;
                if (selectedWeight-- > 0) continue;
                usedFamilies.Add(family);
                return family;
            }

            return MarkingFamilies[random.Next(MarkingFamilies.Length)];
        }

        private static string PickMarketMarkingFamily(Random random, HashSet<string> usedFamilies)
        {
            return random.NextDouble() < GameConfig.StoreFounderMarkingChance
                ? PickMarkingFamily(random, usedFamilies)
                : "Solid";
        }

        private static void RepairListings(ColonySaveData save)
        {
            foreach (var listing in save.storeRatListings)
            {
                if (listing == null) continue;
                listing.name = ColonyFactory.NormalizeDisplayName(listing.name);
                if (listing.genotype == null) listing.genotype = new GenotypeData();
                GeneticsSystem.Normalize(listing.genotype);
                listing.markingFamily = GeneticsSystem.NormalizeMarkingFamily(listing.markingFamily, listing.genotype);
                if (string.IsNullOrEmpty(listing.coatColorVariant))
                    listing.coatColorVariant = GeneticsSystem.DefaultCoatColorVariant(listing.id, listing.genotype);
                if (listing.coatTone <= 0f || float.IsNaN(listing.coatTone) || float.IsInfinity(listing.coatTone))
                    listing.coatTone = GeneticsSystem.DefaultCoatTone(listing.id, listing.genotype);
                if (listing.traits == null) listing.traits = new TraitData();

                // Legacy saves commonly retain the former fixed $100 price.
                // Recalculate once from the already-persisted data, without
                // rerolling listing identity, name, traits, or appearance.
                if (listing.pricingVersion < GameConfig.StorePurchasePricingVersion)
                {
                    listing.price = CalculatePurchasePrice(
                        listing.traits, listing.markingFamily, listing.genotype);
                    listing.pricingVersion = GameConfig.StorePurchasePricingVersion;
                }
            }
        }

        private static TraitData CloneTraits(TraitData source)
        {
            return source == null ? new TraitData() : new TraitData(source.size, source.health, source.fertility);
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                return hash == int.MinValue ? int.MaxValue : Math.Abs(hash);
            }
        }
    }
}
