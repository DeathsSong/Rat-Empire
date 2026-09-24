using System;

namespace RatHabitat
{
    public static class GameConfig
    {
        public const int SaveVersion = 3;
        public const long GameDayMs = 24L * 60L * 60L * 1000L;
        public const long StartGameTimeMs = 8L * 60L * 60L * 1000L;
        // Biological timing is expressed in simulated days. The legacy
        // PregnancyMs name remains for compatibility with older callers.
        public const float GestationDays = 22f;
        public const long PregnancyMs = 22L * GameDayMs;
        public const float WeaningDays = 21f;
        public const float MaleSexualMaturityDays = 56f;
        public const float FemaleSexualMaturityDays = 70f;
        public const float SeniorStartDays = 365f;
        public const float MinimumLifespanDays = 540f;
        public const float MaximumLifespanDays = 1080f;
        public const float MinimumBreedingEndDays = 270f;
        public const float MaximumBreedingEndDays = 365f;
        public const float EstrousCycleDays = 4.5f;
        public const float EstrousFertileWindowDays = 1f;
        public const float RecoveryDays = 60f;
        public const float HealthDeclineStartDays = 365f;
        public const float FertilityDeclineStartDays = 270f;
        public const float HealthDeclinePerDay = 0.035f;
        public const float FertilityDeclinePerDay = 0.06f;
        public const long BreedingCooldownMs = 30L * 1000L;
        public const long PairingCheckIntervalMs = 30L * 1000L;
        // Applies to a resolved Pairing Habitat attempt, including a failed
        // conception. This is serialized on each RatData record so a save or
        // reload cannot cause the same pair to retry immediately.
        public const long PairingAttemptCooldownMs = 30L * 1000L;
        public const float PairingPregnancyChance = 0.20f;
        public const float DedicatedBreedingSessionHours = 2f;
        public const float DedicatedBreedingSuccessBonus = 0.25f;
        public const float DedicatedBreedingSuccessCap = 0.95f;
        public const int EuthanasiaCostDollars = 100;
        public const int StartingColonyCredits = 250;
        public const int StarterAdultRatPrice = 100;
        public const long StoreRestockIntervalGameMs = GameDayMs;
        public const int StoreRestockListingCount = 2;
        public const float StoreSharedMarkingFamilyChance = 0.70f;
        public const float StoreLowTraitMinimum = 34f;
        public const float StoreLowTraitMaximum = 58f;
        // A full-fertility female can carry a realistic 6-18 pinkie litter.
        // Lower fertility scales both limits down, with a successful
        // pregnancy always clamped to at least one pinkie.
        public const int MinimumLitterSizeAtFullFertility = 6;
        public const int MaximumLitterSizeAtFullFertility = 18;
        public const float PinkieStageDays = 21f;
        // Kept as the legacy neutral threshold. GrowthSystem uses the sex
        // specific maturity thresholds above for the authoritative stage.
        public const float YoungStageDays = 56f;
        public const int SellCreditBase = 25;
        public const float SellCreditTraitMultiplier = 0.5f;
        public const float TraitVariation = 4f;
        public const float MutationRate = 0.0025f;
        // Visual stage values are presentation-only. They do not change the
        // saved age, stage, phenotype, or growth rules above.
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
        // The imported young/adult model sits too high relative to the raised
        // Pairing Habitat floor. This is a visual-child offset only; the
        // stable gameplay root, collider, movement targets, and nest remain
        // unchanged. The value matches the manual 0.092 -> -0.728 reference.
        public const float PairingAdultVisualVerticalOffset = -0.82f;
        // Pairing Habitat gameplay roots use the screenshot reference of
        // local Y 0.972 instead of the normal 0.45 spawn height. This is a
        // root-height correction for young/adult rats; X/Z placement remains
        // owned by the live movement system.
        public const float PairingAdultRootVerticalLift = 0.522f;
        public const float YoungVisualScale = 0.68f;
        public const float AdultVisualScale = 1f;
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
        public const float HabitatWidth = 12f;
        public const float HabitatDepth = 16f;
        // The overview now spans the original full-depth Male/Female cages
        // plus the separate lower Nursery/Breeding row. This frames the
        // taller group without shrinking any enclosure geometry.
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
