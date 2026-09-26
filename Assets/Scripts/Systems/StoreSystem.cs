using System;

namespace RatHabitat
{
    /// <summary>
    /// Persisted Rat Market inventory and purchase rules. Listings are data,
    /// not UI state, so rebuilding the Store panel never rerolls a rat.
    /// </summary>
    public static class StoreSystem
    {
        private static readonly string[] MaleNames = GameConfig.MaleRatNames;
        private static readonly string[] FemaleNames = GameConfig.FemaleRatNames;

        // Weighted toward common pet-rat patterns while keeping rarer families
        // in the market. The selected family is persisted on the listing.
        private static readonly string[] MarkingFamilies =
        {
            "Solid", "Solid", "Solid", "Self", "Hooded", "Hooded",
            "Broken hooded", "Berkshire", "Berkshire", "Bareback", "Capped",
            "Mask", "Patch", "Black-eye white", "Variegated", "Variberk",
            "Irish", "Blaze", "Lightning blaze Siamese", "Badger blaze Siamese",
            "Dalmatian-style", "Dominant white spotted", "White side", "Merle", "Tabby/Marble",
            "Mismarked hooded"
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
            if (save == null) return false;
            EnsureStoreState(save);
            if (save.storeNextRestockGameTime <= 0L)
                save.storeNextRestockGameTime = gameTime + GameConfig.StoreRestockIntervalGameMs;
            if (gameTime < save.storeNextRestockGameTime) return false;

            RestockNow(save, gameTime);
            return true;
        }

        public static void RestockNow(ColonySaveData save, long gameTime)
        {
            if (save == null) return;
            save.EnsureLists();
            save.storeRestockCycle = Math.Max(0, save.storeRestockCycle) + 1;
            int seed = StableHash((save.createdAt == 0 ? GameConfig.NowMs() : save.createdAt) +
                "|restock|" + save.storeRestockCycle);
            CreateInventory(save, seed, save.storeRestockCycle);
            save.storeInventoryInitialized = true;
            save.storeNextRestockGameTime = gameTime + GameConfig.StoreRestockIntervalGameMs;
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
            string maleFamily = PickMarkingFamily(random);
            string femaleFamily = random.NextDouble() < GameConfig.StoreSharedMarkingFamilyChance
                ? maleFamily
                : PickMarkingFamily(random);

            string maleId = "starter_male_" + Math.Abs(seed).ToString("X8");
            string femaleId = "starter_female_" + Math.Abs(seed).ToString("X8");
            male = CreateGeneratedAdultRat(maleId, RatSex.Male,
                MaleNames[random.Next(MaleNames.Length)], maleFamily, random, sharedBaseline,
                gameTime, GameConfig.StarterMaleMinimumAgeDays, GameConfig.StarterMaleMaximumAgeDays,
                true);
            female = CreateGeneratedAdultRat(femaleId, RatSex.Female,
                FemaleNames[random.Next(FemaleNames.Length)], femaleFamily, random, sharedBaseline,
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
            rat.stage = GrowthSystem.StageForAge(rat.ageDays, rat.sex);
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
            rat.enclosure = rat.sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony;
            return rat;
        }

        private static void CreateInventory(ColonySaveData save, int seed, int cycle)
        {
            var random = new Random(seed);
            save.storeRatListings.Clear();
            int qualityCap = UpgradeSystem.StoreQualityCap(save);

            // Generate the pair from one shared low-stat baseline. Each rat
            // gets a small deterministic deviation, so the starter pair feels
            // coordinated without becoming identical clones.
            float sharedBaseline = NextTrait(random, GameConfig.StoreLowTraitMinimum, qualityCap);

            string maleFamily = PickMarkingFamily(random);
            string femaleFamily = random.NextDouble() < GameConfig.StoreSharedMarkingFamilyChance
                ? maleFamily
                : PickMarkingFamily(random);

            save.storeRatListings.Add(CreateListing(
                "store_adult_male_" + cycle,
                RatSex.Male,
                MaleNames[random.Next(MaleNames.Length)],
                maleFamily,
                random,
                sharedBaseline,
                qualityCap));
            save.storeRatListings.Add(CreateListing(
                "store_adult_female_" + cycle,
                RatSex.Female,
                FemaleNames[random.Next(FemaleNames.Length)],
                femaleFamily,
                random,
                sharedBaseline,
                qualityCap));
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
            var genotype = CreateGenotype(random, markingFamily);
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
            rat.coatColorVariant = GeneticsSystem.DefaultCoatColorVariant(id, genotype);
            rat.coatTone = GeneticsSystem.DefaultCoatTone(id, genotype);
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
            return new StoreRatListingData
            {
                id = id,
                name = name,
                sex = sex,
                price = GameConfig.StarterAdultRatPrice,
                markingFamily = markingFamily,
                coatColorVariant = coatColorVariant,
                coatTone = GeneticsSystem.DefaultCoatTone(id, genotype),
                genotype = genotype,
                traits = new TraitData(
                    NearSharedTrait(random, sharedBaseline, minimum, maximum),
                    NearSharedTrait(random, sharedBaseline, minimum, maximum),
                    NearSharedTrait(random, sharedBaseline, minimum, maximum)),
            };
        }

        private static GenotypeData CreateGenotype(Random random, string markingFamily)
        {
            string blackAllele = random.Next(2) == 0 ? "B" : "b";
            string otherBlackAllele = random.Next(2) == 0 ? "B" : "b";
            string diluteAllele = random.Next(3) == 0 ? "d" : "D";
            string otherDiluteAllele = random.Next(3) == 0 ? "d" : "D";
            bool albino = random.NextDouble() < 0.08;
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

        private static string PickMarkingFamily(Random random)
        {
            return MarkingFamilies[random.Next(MarkingFamilies.Length)];
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
                if (listing.price <= 0) listing.price = GameConfig.StarterAdultRatPrice;
                if (listing.traits == null) listing.traits = new TraitData();
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
