using System;
using System.Collections.Generic;

namespace RatHabitat
{
    public static class GameConfig
    {
        public const int SaveVersion = 9;
        public const long GameDayMs = 24L * 60L * 60L * 1000L;
        public const long StartGameTimeMs = 8L * 60L * 60L * 1000L;
        // Biological timing is expressed in simulated days. The legacy
        // PregnancyMs name remains for compatibility with older callers.
        public const float GestationDays = 22f;
        public const long PregnancyMs = 22L * GameDayMs;
        public const float WeaningDays = 21f;
        // Names are not reused immediately. The allocator still falls back
        // deterministically when a pool is exhausted, but normal colonies
        // should have many generations of unused names available first.
        public const float RatNameReuseCooldownDays = 90f;
        // Stable-ID name generation keeps single names as the norm while
        // allowing occasional complete two-part names from the same pools.
        public const float RatDoubleNameChance = 0.12f;
        // A pup must be both fully weaned and six weeks old before it can be
        // sold.  Keep this separate from PinkieStageDays and WeaningDays so a
        // visual growth transition cannot accidentally unlock a sale.
        public const float PupSaleMinimumAgeDays = 42f;
        public const float NursingInteractionCooldownHours = 6f;
        // These are presentation durations. NursingSystem owns the catalog
        // and can add more interactions without changing the rat behavior
        // state machine.
        public const float NursingInteractionDurationSeconds = 2.5f;
        public const float NursingGroomingInteractionDurationSeconds = 1.9f;
        public const float NursingSniffInteractionWeight = 0.68f;
        public const float NursingGroomingInteractionWeight = 0.32f;
        public const float MaleSexualMaturityDays = 56f;
        public const float FemaleSexualMaturityDays = 70f;
        public const float MatureStartDays = 365f;
        // Kept as a source-compatibility alias for older callers. New code
        // should use MatureStartDays and the individualized breeding cutoff.
        public const float SeniorStartDays = MatureStartDays;
        public const float MinimumLifespanDays = 540f;
        public const float MaximumLifespanDays = 1080f;
        // Breeding-end ages were previously randomized from 270-365 days.
        // Keep the same individualized range, extended by exactly one year.
        public const float BreedingAgeDeclineStartDays = 365f;
        public const float BreedingEndAgeMigrationDays = 365f;
        public const float MinimumBreedingEndDays = 635f;
        public const float MaximumBreedingEndDays = 730f;
        // Age-related sale value declines use the same smooth effectiveness
        // factor as breeding. Elderly rats cannot be sold, so this floor only
        // applies to still-sellable Mature rats approaching their cutoff.
        public const float MatureSaleValueMinimumMultiplier = 0.35f;
        public const float EstrousCycleDays = 4.5f;
        public const float EstrousFertileWindowDays = 1f;
        // Post-birth reproductive recovery is independent from pup weaning
        // and the six-week sale restriction. Mothers can resume the normal
        // fertile-window calculation while still caring for their litter.
        public const float RecoveryDays = 7f;
        // Presentation-only inner nest zone for a mother caring for pinkies.
        // These values are deliberately configurable and are clamped against
        // the measured nest footprint by EnclosureSystem.
        public const float NestCaregiverInset = 0.56f;
        public const float NestCaregiverBodyMargin = 0.42f;
        public const float NestCaregiverMinimumHalfExtent = 0.25f;
        public const float NestCaregiverMaximumHalfExtentFraction = 0.74f;
        public const float HealthDeclineStartDays = 365f;
        // Keep raw fertility decline aligned with the reproductive decline
        // boundary. A naturally low fertility trait is not an age stage.
        public const float FertilityDeclineStartDays = BreedingAgeDeclineStartDays;
        public const float HealthDeclinePerDay = 0.035f;
        public const float FertilityDeclinePerDay = 0.06f;
        public const long BreedingCooldownMs = 30L * 1000L;
        public const long PairingCheckIntervalMs = 30L * 1000L;
        // Applies to a resolved Pairing Habitat attempt, including a failed
        // conception. This is serialized on each RatData record so a save or
        // reload cannot cause the same pair to retry immediately.
        public const long PairingAttemptCooldownMs = 30L * 1000L;
        public const float PairingPregnancyChance = 0.20f;
        // A gentler curve keeps low-fertility beginner pairs viable while the
        // geometric mean still prevents a strong parent from masking a weak
        // one. At 1/1 fertility this produces about 1.1% in Pairing and 2.5%
        // in a dedicated session, while 100/100 remains at the habitat max.
        public const float ConceptionFertilityCurveExponent = 0.625f;
        public const float DedicatedBreedingSessionHours = 2f;
        public const float DedicatedBreedingSuccessBonus = 0.25f;
        public const float DedicatedBreedingSuccessCap = 0.95f;
        public const int EuthanasiaCostDollars = 100;
        public const int StartingColonyCredits = 250;
        // Rat Market purchase price formula (calculated once per restock and
        // persisted on StoreRatListingData):
        // base + average(Health, Fertility) * trait multiplier + visible-marking
        // premium. At the normal 0-15 beginner range, unmarked rats cost about
        // $100-$115; maxed traits cost $200 before a modest marking premium.
        // Size is intentionally excluded and solid/self/albino-masked coats
        // never receive a marking premium.
        public const int StorePurchaseBasePrice = 100;
        public const float StorePurchaseTraitMultiplier = 1f;
        public const int StorePurchaseCommonMarkingPremium = 8;
        public const int StorePurchaseRareMarkingPremium = 20;
        public const int StorePurchasePricingVersion = 1;
        // Development-only runtime performance capture. A one-hour sample ring
        // is bounded and small enough for WebGL while retaining long runs.
        public const float PerformanceLogSampleIntervalSeconds = 1f;
        public const int PerformanceLogSampleCapacity = 3600;
        public const int PerformanceLogSpikeCapacity = 256;
        public const float PerformanceLagSpikeThresholdMs = 100f;
        public const float PerformanceCriticalSpikeThresholdMs = 250f;
        // WebGL often has no GPU timer. Keep the part of a slow frame that is
        // not explained by the sampled CPU main-thread time explicitly marked
        // as unattributed rather than calling it GPU time.
        public const float PerformanceUnattributedFrameGapThresholdMs = 50f;
        public const int PerformanceLogRecentDisplayCount = 8;
        // Market founders are almost always solid; breeding is the primary
        // source of new marked lines.
        public const float StoreFounderMarkingChance = 0.01f;
        // Both new-game starters share one solid visual coat while their
        // underlying black/dilution alleles remain independently generated.
        public const string StarterSolidCoatColorVariant = "agouti";
        public const int StarterAdultRatPrice = StorePurchaseBasePrice;
        public const long StoreRestockIntervalGameMs = GameDayMs;
        // Legacy base-count name is retained for source compatibility.
        public const int StoreRestockListingCount = 2;
        public const int StoreListingCapacityUpgradeStep = 1;
        public const float StoreSharedMarkingFamilyChance = 0.70f;
        // New-game founders are deliberately weak but still varied. These
        // are absolute stat values, not percentages.
        public const float StarterBeginnerTraitMinimum = 0f;
        public const float StarterBeginnerTraitMaximum = 15f;
        public const float StarterMaleMinimumAgeDays = MaleSexualMaturityDays;
        public const float StarterMaleMaximumAgeDays = 86f;
        public const float StarterFemaleMinimumAgeDays = FemaleSexualMaturityDays;
        public const float StarterFemaleMaximumAgeDays = 100f;
        // Store quality is intentionally a small, absolute beginner range.
        // UpgradeSystem raises the maximum for future listings by exactly five
        // points per purchased level without changing existing rats/listings.
        public const float StoreLowTraitMinimum = 0f;
        public const float StoreLowTraitMaximum = 15f;
        public const int BaseStoreQualityCap = 15;
        public const int StoreQualityUpgradeStep = 5;
        public const int StoreQualityUpgradeBaseCost = 100;
        public const int StoreQualityUpgradeCostStep = 100;
        public const int BaseColonyCapacity = 20;
        public const int ColonyCapacityUpgradeStep = 5;
        public const int ColonyCapacityUpgradeBaseCost = 150;
        public const int ColonyCapacityUpgradeCostStep = 100;
        // Pairing Habitat placement has its own starting limit. This is an
        // admission limit for that enclosure, separate from the overall
        // colony capacity and from breeding eligibility.
        public const int BasePairingHabitatCapacity = 10;
        // Each purchase adds five occupant slots. Pricing follows the colony
        // capacity upgrade's escalating $150 + $100 per level progression.
        public const int PairingHabitatCapacityUpgradeStep = 5;
        public const int PairingHabitatCapacityUpgradeBaseCost = 150;
        public const int PairingHabitatCapacityUpgradeCostStep = 100;
        // A full-fertility female can carry a realistic 6-18 pinkie litter.
        // Lower fertility scales both limits down, with a successful
        // pregnancy always clamped to at least one pinkie.
        public const int MinimumLitterSizeAtFullFertility = 6;
        public const int MaximumLitterSizeAtFullFertility = 18;
        // Pinkies remain newborns for exactly one week. GrowthSystem is the
        // single authority for this threshold; all habitat/UI consumers read
        // the resulting RatStage rather than maintaining their own cutoff.
        public const float PinkieStageDays = 7f;
        // Kept as the legacy neutral threshold. GrowthSystem uses the sex
        // specific maturity thresholds above for the authoritative stage.
        public const float YoungStageDays = 56f;
        public const int SellCreditBase = 25;
        public const float SellCreditTraitMultiplier = 0.5f;
        public const float TraitVariation = 4f;
        // B/C/D pigment mutations retain the original rate. S-locus marking
        // mutations use a separate per-inherited-allele rate: 5% per allele
        // gives solid s/s parents a 9.75% chance of at least one spontaneous
        // marking allele in a pup (1 - 0.95^2), while remaining non-guaranteed.
        public const float MutationRate = 0.0025f;
        public const float MarkingMutationRate = 0.05f;
        // Visual scale endpoints are presentation-only. GrowthSystem uses
        // them as the endpoints of its age-based uniform curve; they do not
        // change the saved age, stage, phenotype, or biology rules above.
        public const float PinkieVisualScale = 0.28f;
        // Presentation-only local offset on the imported pinkie visual under
        // Rat Visual Stage. This matches the manually corrected reference
        // visual and does not move the stable gameplay rat root.
        // The replacement FBX sits correctly on the nest with this visual
        // offset. The stable rat root and nest position remain unchanged.
        // Pairing Habitat's nest surface is visibly higher than the normal
        // nursery surface in the imported scene. Lift the stable pinkie root
        // by the manually matched +0.225 world-unit reference amount.
        // The imported young/adult model uses a raised Pairing gameplay root.
        // Keep the visual child's lowest mesh point just above the cage floor;
        // this is visual-only and does not change the stable root, collider,
        // movement targets, or nest. Root lift 0.522 + this offset places the
        // normalized model at the generated floor top (0.235) with clearance.
        public const float PairingAdultVisualVerticalOffset = -0.728f;
        // The generated full-size cage floor is a 0.24-unit slab centered at
        // Y 0.115, so its actual visible upper surface is Y 0.235.
        public const float PairingHabitatFloorTop = 0.235f;
        public const float PairingAdultGroundClearance = 0.012f;
        // Pairing Habitat gameplay roots use the screenshot reference of
        // local Y 0.972 instead of the normal 0.45 spawn height. This is a
        // root-height correction for young/adult rats; X/Z placement remains
        // owned by the live movement system.
        public const float PairingAdultRootVerticalLift = 0.522f;
        // Young Rats should be only slightly larger than the pinkie visual at
        // the seven-day transition. GrowthSystem interpolates from this value
        // to the size-dependent adult endpoint at the rat's saved sexual
        // maturity age.
        public const float YoungVisualScale = 0.36f;
        public const float AdultVisualScale = 1f;
        // AdultVisualScale remains the neutral size-50 baseline. These bounds
        // map the persisted 0-100 Size trait to a noticeable but safe uniform
        // presentation scale without changing gameplay colliders or movement.
        public const float AdultSizeVisualScaleMinimum = 0.82f;
        public const float AdultSizeVisualScaleMaximum = 1.18f;
        public const float PinkieToYoungVisualTransitionSeconds = 0.85f;
        public const float YoungToAdultVisualTransitionSeconds = 1.05f;
        public const float ImportedRatTargetHeight = 0.5f;
        public const float ImportedRatModelScale = 1f;
        // The live imported mesh still presents its nose opposite the stable
        // root's travel frame after RatVisualFactory's visual-root correction.
        // Apply one explicit stable-root half-turn so the visible nose follows
        // the measured movement delta. Change only if the source forward axis
        // changes on a future FBX reimport.
        public const float ImportedRatMovementFacingOffsetDegrees = 180f;
        // Legacy aliases retained for older callers and save tooling.
        public const int MinimumLitterSize = 1;
        public const int MaximumLitterSize = MaximumLitterSizeAtFullFertility;
        // Legacy aliases now match the common full-size enclosure footprint
        // used by every horizontal habitat page.
        public const float HabitatWidth = 10.7f;
        public const float HabitatDepth = 22f;
        // Each page frames one complete enclosure. The camera shifts between
        // the horizontal row rather than shrinking a multi-habitat overview.
        public const float CameraDefaultOrthographicSize = 10.8f;
        public const float CameraMinimumOrthographicSize = 7.5f;
        public const float CameraMaximumOrthographicSize = 16f;

        // If the purchased/imported FBX is placed below Assets/Resources, the
        // factory can use it at runtime on both Windows and Android. The
        // Inspector slot on Rat Visual Factory remains the preferred explicit
        // assignment when the asset lives elsewhere in Assets.
        public const string HandPaintedRatResourcePath = "HandPaintedRat/HandPaintedRat";
        // Pinkies use the dedicated imported model prefab. Adult rats must
        // never be used as the pinkie visual.
        public const string PinkiePrototypeResourcePath = "HandPaintedRat_Pinkie";

        public static readonly string[] Loci = { "B", "C", "D", "S" };

        // The first names preserve the established starter/store vocabulary.
        // The pool is expanded from distinct sex-specific bases. Each base
        // also has two natural compound forms, giving the allocator more than
        // 300 deterministic choices per sex without cross-sex collisions.
        public static readonly string[] MaleRatNames = ExpandRatNamePool(
            new[]
            {
                "Otto", "Randy", "Branch", "Biscuit", "Pickle", "Peanut", "Milo", "Jasper",
                "Theo", "Gus", "Waffles", "Truffle", "Oatmeal", "Nugget", "Pippin", "Toby",
                "Bean", "Sprout", "Pancake", "Toast", "Cricket", "Hobbes", "Bramble", "Archie",
                "Alfie", "Alvin", "Amos", "Arthur", "Augustus", "Barney", "Basil", "Benji",
                "Benny", "Bo", "Bowie", "Bruno", "Buster", "Calvin", "Chester", "Clark",
                "Clyde", "Cosmo", "Dexter", "Douglas", "Edgar", "Eddie", "Elvis", "Ernie",
                "Felix", "Finnegan", "Finn", "Floyd", "Frankie", "Fritz", "George", "Gizmo",
                "Gordon", "Harley", "Harvey", "Henry", "Hugo", "Iggy", "Irwin", "Jack",
                "Jackson", "Jake", "Joey", "Jonah", "Jude", "Kirby", "Leo", "Leon",
                "Lewis", "Loki", "Louie", "Mac", "Mango", "Marley", "Marshall", "Max",
                "Merlin", "Mickey", "Morris", "Monty", "Murray", "Nico", "Oliver", "Oscar",
                "Otis", "Ozzy", "Percy", "Peter", "Porter", "Ralph", "Remy", "Rex",
                "Riley", "Robin", "Rocket", "Romeo", "Roscoe", "Rufus", "Rusty", "Sammy",
                "Scout", "Simon", "Snoopy", "Sonny", "Stanley", "Stewie", "Teddy", "Thomas",
                "Tiger", "Tucker", "Wallace", "Watson", "Winston", "Wolfie", "Yogi", "Ziggy"
            }, "Paws", "Bear");
        public static readonly string[] FemaleRatNames = ExpandRatNamePool(
            new[]
            {
                "Mabel", "Olive", "Noodle", "Peaches", "Marmalade", "Clover", "Bonnie", "Honey",
                "Pudding", "Daisy", "Hazel", "Maple", "Poppy", "Willow", "Maisie", "Taffy",
                "Toffee", "Cinnamon", "Muffin", "Juniper", "Winnie", "Pearl", "Abby", "Addie",
                "Alice", "Amber", "Annie", "Apple", "April", "Athena", "Aurora", "Autumn",
                "Bailey", "Bambi", "Beatrice", "Bella", "Betsy", "Birdie", "Blossom", "Bluebell",
                "Buffy", "Callie", "Cassie", "Celeste", "Chanel", "Charlotte", "Chloe", "Clara",
                "Coco", "Coral", "Darcy", "Delilah", "Dolly", "Ellie", "Elsie", "Emma",
                "Esme", "Eva", "Evie", "Fern", "Flora", "Frida", "Gemma", "Gigi",
                "Ginger", "Gloria", "Grace", "Gracie", "Greta", "Gypsy", "Harper", "Heidi",
                "Holly", "Hope", "Iris", "Isla", "Ivy", "Jade", "Jasmine", "Jellybean",
                "Jemma", "Jill", "Josie", "Katie", "Kiki", "Lacey", "Lana", "Lavender",
                "Lily", "Lola", "Lottie", "Lulu", "Luna", "Lyra", "Macy", "Maggie",
                "Marigold", "Melody", "Mia", "Mimi", "Minnie", "Mocha", "Molly", "Nala",
                "Nellie", "Nina", "Nova", "Opal", "Orchid", "Paisley", "Penny", "Phoebe",
                "Piper", "Pixie", "Polly", "Queenie", "Raven", "Rosie", "Ruby", "Sadie",
                "Sally", "Sasha", "Scarlet", "Serena", "Shelby", "Skye", "Snowdrop", "Sophie",
                "Stella", "Sugar", "Sunny", "Suzie", "Tessa", "Tilly", "Trudy", "Violet",
                "Vivian", "Wren", "Yara", "Zelda", "Zoe"
            }, "Mae", "Belle");
        // Kept as a compatibility alias for older tooling. These are
        // individual rat names, not litter names.
        public static readonly string[] PupNames =
        {
            "Pip", "Noodle", "Mochi", "Bean", "Peanut", "Dumpling", "Sprout", "Mallow",
            "Muffin", "Button", "Pebble", "Taffy", "Toffee", "Poppy", "Cricket", "Bramble"
        };

        public static string DominantAllele(string locus)
        {
            switch (locus)
            {
                case "B": return "B";
                case "C": return "C";
                case "D": return "D";
                default: return "S";
            }
        }

        public static string RecessiveAllele(string locus)
        {
            switch (locus)
            {
                case "B": return "b";
                case "C": return "c";
                case "D": return "d";
                default: return "s";
            }
        }

        public static bool IsValidAllele(string locus, string allele)
        {
            return allele == DominantAllele(locus) || allele == RecessiveAllele(locus);
        }

        public static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static string[] ExpandRatNamePool(string[] bases, string suffixA, string suffixB)
        {
            var result = new List<string>(bases.Length * 3);
            foreach (string value in bases) result.Add(value);
            foreach (string value in bases) result.Add(value + " " + suffixA);
            foreach (string value in bases) result.Add(value + " " + suffixB);
            return result.ToArray();
        }
    }
}
