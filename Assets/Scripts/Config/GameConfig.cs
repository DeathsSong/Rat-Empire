using System;

namespace RatHabitat
{
    public static class GameConfig
    {
        public const int SaveVersion = 7;
        public const long GameDayMs = 24L * 60L * 60L * 1000L;
        public const long StartGameTimeMs = 8L * 60L * 60L * 1000L;
        // Biological timing is expressed in simulated days. The legacy
        // PregnancyMs name remains for compatibility with older callers.
        public const float GestationDays = 22f;
        public const long PregnancyMs = 22L * GameDayMs;
        public const float WeaningDays = 21f;
        // A pup must be both fully weaned and six weeks old before it can be
        // sold.  Keep this separate from PinkieStageDays and WeaningDays so a
        // visual growth transition cannot accidentally unlock a sale.
        public const float PupSaleMinimumAgeDays = 42f;
        public const float NursingInteractionCooldownHours = 6f;
        public const float NursingInteractionDurationSeconds = 2.5f;
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
        public const float RecoveryDays = 60f;
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
        public const int StarterAdultRatPrice = 100;
        public const long StoreRestockIntervalGameMs = GameDayMs;
        public const int StoreRestockListingCount = 2;
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
        public const float MutationRate = 0.0025f;
        // Visual scale endpoints are presentation-only. GrowthSystem uses
        // them as the endpoints of its age-based uniform curve; they do not
        // change the saved age, stage, phenotype, or biology rules above.
        public const float PinkieVisualScale = 0.28f;
        // Presentation-only local offset on the imported pinkie visual under
        // Rat Visual Stage. This matches the manually corrected reference
        // visual and does not move the stable gameplay rat root.
        // The replacement FBX sits correctly on the nest with this visual
        // offset. The stable rat root and nest position remain unchanged.
        public const float PinkieVisualVerticalOffset = -0.135f;
        // Pairing Habitat's nest surface is visibly higher than the normal
        // nursery surface in the imported scene. Lift the stable pinkie root
        // by the manually matched +0.225 world-unit reference amount.
        public const float PairingPinkieVerticalLift = 0.225f;
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

        // Display names are intentionally not unique. Rat IDs remain the
        // identity used by selection, breeding, history, and save data, so a
        // repeated friendly name is safe and much more natural than adding a
        // numeric suffix every time a name is reused.
        public static readonly string[] MaleRatNames =
        {
            "Otto", "Randy", "Branch", "Biscuit", "Pickle", "Peanut", "Milo", "Jasper",
            "Theo", "Gus", "Waffles", "Truffle", "Oatmeal", "Nugget", "Pippin", "Toby",
            "Mochi", "Bean", "Sprout", "Pancake", "Toast", "Cricket", "Hobbes", "Bramble"
        };
        public static readonly string[] FemaleRatNames =
        {
            "Mabel", "Olive", "Mochi", "Noodle", "Peaches", "Marmalade", "Clover", "Bonnie",
            "Honey", "Pudding", "Daisy", "Hazel", "Maple", "Poppy", "Willow", "Maisie",
            "Taffy", "Toffee", "Cinnamon", "Biscuit", "Muffin", "Juniper", "Winnie", "Pearl"
        };
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
    }
}
