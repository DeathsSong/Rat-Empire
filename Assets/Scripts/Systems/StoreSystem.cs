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
            "Solid", "Solid", "Solid", "Solid", "Hooded", "Hooded",
            "Berkshire", "Berkshire", "Capped", "Bareback", "Variegated",
            "Irish", "Blaze", "Dalmatian-style", "Mismarked hooded"
        };

        public static void EnsureStoreState(ColonySaveData save)
        {
            if (save == null) return;
            save.EnsureLists();
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
            rat.birthTimestamp = gameTime - (30L * GameConfig.GameDayMs);
            rat.growthTimestamp = rat.birthTimestamp;
            rat.ageDays = 30f;
            rat.enclosure = rat.sex == RatSex.Male ? RatEnclosure.MaleColony : RatEnclosure.FemaleColony;
            return rat;
        }

        private static void CreateInventory(ColonySaveData save, int seed, int cycle)
        {
            var random = new Random(seed);
            save.storeRatListings.Clear();

            // Generate the pair from one shared low-stat baseline. Each rat
            // gets a small deterministic deviation, so the starter pair feels
            // coordinated without becoming identical clones.
            float sharedBaseline = NextTrait(random, GameConfig.StoreLowTraitMinimum, GameConfig.StoreLowTraitMaximum);

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
                sharedBaseline));
            save.storeRatListings.Add(CreateListing(
                "store_adult_female_" + cycle,
                RatSex.Female,
                FemaleNames[random.Next(FemaleNames.Length)],
                femaleFamily,
                random,
                sharedBaseline));
        }

        private static StoreRatListingData CreateListing(
            string id,
            RatSex sex,
            string name,
            string markingFamily,
            Random random,
            float sharedBaseline)
        {
            var genotype = CreateGenotype(random, markingFamily);
            GeneticsSystem.Normalize(genotype);
            float minimum = GameConfig.StoreLowTraitMinimum;
            float maximum = GameConfig.StoreLowTraitMaximum;
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
            bool solid = string.Equals(markingFamily, "Solid", StringComparison.OrdinalIgnoreCase);
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
