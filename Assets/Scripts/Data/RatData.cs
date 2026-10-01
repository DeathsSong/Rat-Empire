using System;
using System.Collections.Generic;

namespace RatHabitat
{
    public enum RatSex
    {
        Female,
        Male
    }

    public enum RatStage
    {
        Pinkie,
        YoungRat,
        Adult,
        Mature,
        Elderly,
        // Compatibility alias for saves/tests written before the explicit
        // Mature/Elderly stages existed. Stage refresh always recalculates
        // the correct value from age and the persisted breeding cutoff.
        Senior = Mature
    }

    public enum ReproductiveState
    {
        Immature,
        Fertile,
        Pregnant,
        Nursing,
        Recovery,
        Infertile
    }

    public enum RatRemovalDisposition
    {
        None,
        Sold,
        Euthanized,
        NaturalDeath,
        Deleted
    }

    /// <summary>
    /// The physical habitat zone currently assigned to a rat. This is saved
    /// with the rat and is refreshed by EnclosureSystem whenever pregnancy,
    /// nursing, growth, deletion, or loading changes colony relationships.
    /// </summary>
    public enum RatEnclosure
    {
        MaleColony,
        FemaleColony,
        Nursery,
        Breeding,
        Pairing
    }

    public enum SelectableKind
    {
        Rat,
        HabitatObject
    }

    public enum HabitatObjectType
    {
        Food,
        Water,
        Nest,
        Hide,
        ExerciseWheel
    }

    [Serializable]
    public class TraitData
    {
        public float size;
        public float health;
        public float fertility;

        public TraitData() { }

        public TraitData(float size, float health, float fertility)
        {
            this.size = size;
            this.health = health;
            this.fertility = fertility;
        }
    }

    [Serializable]
    public class RatActivityEntryData
    {
        public long gameTimeMs;
        public string activityKey;
        public string activityLabel;
        public string message;
    }

    [Serializable]
    public class RatActivityData
    {
        public string currentActivityKey;
        public string currentActivityLabel;
        public long currentActivityAt;
        public List<RatActivityEntryData> history = new List<RatActivityEntryData>();
    }

    [Serializable]
    public class LocusData
    {
        public string locus;
        public string firstAllele;
        public string secondAllele;

        public LocusData() { }

        public LocusData(string locus, string firstAllele, string secondAllele)
        {
            this.locus = locus;
            this.firstAllele = firstAllele;
            this.secondAllele = secondAllele;
        }

        public LocusData Clone()
        {
            return new LocusData(locus, firstAllele, secondAllele);
        }
    }

    [Serializable]
    public class MutationRecordData
    {
        public string locus;
        public string parentRole;
        public string from;
        public string to;
        public long recordedAt;
    }

    [Serializable]
    public class GenotypeData
    {
        public List<LocusData> loci = new List<LocusData>();
        public List<MutationRecordData> mutations = new List<MutationRecordData>();

        public GenotypeData Clone()
        {
            var copy = new GenotypeData();
            if (loci != null)
            {
                foreach (var locus in loci)
                {
                    if (locus != null) copy.loci.Add(locus.Clone());
                }
            }
            if (mutations == null) return copy;
            foreach (var mutation in mutations)
            {
                if (mutation == null) continue;
                copy.mutations.Add(new MutationRecordData
                {
                    locus = mutation.locus,
                    parentRole = mutation.parentRole,
                    from = mutation.from,
                    to = mutation.to,
                    recordedAt = mutation.recordedAt,
                });
            }
            return copy;
        }
    }

    [Serializable]
    public class PhenotypeData
    {
        public bool furRevealed;
        public string coatColorId;
        public string coatColorLabel;
        public string coatColorHex;
        public string accentHex;
        public bool spotted;
        public string markingsLabel;
        public string markingFamily;
    }

    [Serializable]
    public class RatData
    {
        public string id;
        public string name;
        // True only after the player explicitly confirms or randomizes this
        // rat's name. Automatic migration and future save passes must never
        // overwrite a player-owned name.
        public bool nameWasPlayerAssigned;
        public string species = "rat";
        public RatSex sex;
        public RatStage stage;
        public int generation;
        public string motherId;
        public string fatherId;
        public string litterId;
        public long birthTimestamp;
        public long growthTimestamp;
        public float ageDays;
        // Developer growth changes the saved growth anchor without rewriting the
        // original birth timestamp. Regular timestamp growth remains the default.
        public bool developerGrowthOverride;
        public float growthAnchorAgeDays;
        public GenotypeData genotype = new GenotypeData();
        public PhenotypeData phenotype = new PhenotypeData();
        // Friendly, deterministic marking family selected for this rat. The
        // genotype still remains authoritative for inheritance and albino
        // masking; this field keeps the visual phenotype stable across reloads.
        public string markingFamily;
        // Presentation-only coat variation derived from the stable rat ID and
        // genotype. The B/C/D loci remain authoritative; these persisted
        // values keep the expanded natural palette stable across reloads.
        public string coatColorVariant;
        public float coatTone = -1f;
        public TraitData traits = new TraitData();
        public string pregnancyId;
        public long breedingCooldownUntil;
        public RatEnclosure enclosure = RatEnclosure.FemaleColony;
        // Player-assigned Pairing Habitat placement is independent of sex,
        // age, fertility, and reproductive state. Keep it explicit so an
        // enclosure reconciliation cannot infer a different destination.
        public bool pairingHabitatAssigned;
        public bool nursing;
        // Persisted nursing interaction state. nursingUntil is the biological
        // weaning deadline; these fields describe the short visible care
        // interaction and must not be confused with it.
        public string nursingPupId;
        public long nursingInteractionUntil;
        public long nursingRetryAt;
        // Stable ID of the presentation-only caregiving interaction currently
        // playing. This is intentionally separate from combat and has no
        // gameplay damage semantics. Older saves leave it empty and default
        // to the safe sniffing interaction during migration/resume.
        public string nursingInteractionType;
        // Authoritative life/reproduction state. These values are persisted so
        // a reload cannot silently reroll a rat's lifespan or fertile window.
        public float expectedLifespanDays;
        public float sexualMaturityDays;
        public float breedingEndAgeDays;
        public long estrousCycleAnchorGameTime;
        public long nursingUntil;
        public long recoveryUntil;
        public ReproductiveState reproductiveState = ReproductiveState.Immature;
        public float baseHealth;
        public float baseFertility;
        // These flags distinguish a legitimate inherited value of zero from
        // an older save that never initialized the age-decline baselines.
        // Never infer missing data from the stat value itself.
        public bool baseHealthInitialized;
        public bool baseFertilityInitialized;
        // Marks the two randomized founders created for a genuinely new game.
        // This is persisted so biology refreshes can preserve their absolute
        // beginner-stat cap without affecting bred offspring or developer rats.
        public bool isStarterRat;
        // Activity is owned by the rat record rather than the display name so
        // duplicate names remain independent and the history survives sale,
        // euthanasia, natural death, and browser save/load.
        public RatActivityData activity = new RatActivityData();
        // Retired records remain available for historical parent/litter views
        // but are no longer active in the habitat simulation.
        public RatRemovalDisposition removalDisposition = RatRemovalDisposition.None;
        public long removedAt;
        // Natural death is detected by the scheduled growth pass. Keep the
        // announcement guard on the retired rat so it cannot replay after a
        // save/load or browser refresh.
        public bool naturalDeathAnnouncementLogged;
        // Persisted per-pup rotation marker. Zero means the pup has not yet
        // received a nursing turn in this colony cycle.
        public long lastNursedAt;
    }

    [Serializable]
    public class ClockData
    {
        public long gameTimeMs;
        public long lastRealTimestamp;
        public long gameStartTimestamp;
        public float speed = 1f;
    }

    [Serializable]
    public class PregnancyData
    {
        public string id;
        public string motherId;
        public string fatherId;
        public long startedAt;
        public long dueAt;
        public long gestationDurationMs;
        public long finishedAt;
        public string status = "pending";
        public string litterId;
        // Chosen once when conception begins. Keeping this on the pregnancy
        // record prevents a reload, UI refresh, or delayed birth resolution
        // from rerolling the litter size.
        public int expectedLitterSize;
        // Persisted guard for the single colony-wide conception announcement.
        // It belongs to the pregnancy record so reloads cannot announce it
        // twice through a stale selection or profile reference.
        public bool pregnancyAnnouncementLogged;
        // Birth is resolved only after the mother reaches the nest. Persisting
        // the approach state lets save/load resume the walk without replaying
        // the birth or turning the due timestamp into a teleport instruction.
        public bool birthApproachStarted;
        public long birthApproachStartedAt;
        // Birth resolution is a persisted, retryable transaction. A litter
        // may be prepared while the pregnancy is still pending; only after
        // that prepared result is saved can the pregnancy be finalized.
        public int birthAttemptCount;
        public long lastBirthAttemptAt;
        public string birthFailureReason;
        public int birthCommitState;
    }

    [Serializable]
    public class DedicatedBreedingSessionData
    {
        public string id;
        public string motherId;
        public string fatherId;
        public long startedAt;
        public long endsAt;
        public float successChance;
        public string status = "active";
        public long resolvedAt;
        public bool conceptionSucceeded;
    }

    [Serializable]
    public class LitterData
    {
        public string id;
        public string motherId;
        public string fatherId;
        public string litterName;
        public List<string> pupIds = new List<string>();
        public int size;
        public int generation;
        public long birthTimestamp;
        public long weaningTimestamp;
        // Persisted guard for the single colony-wide birth announcement.
        // Keeping it on the litter prevents a reload or UI refresh from
        // replaying the same birth message.
        public bool birthAnnouncementLogged;
        // Persisted guard for the single colony-wide fully-weaned alert.
        // The litter remains in history after weaning, so this prevents a
        // reload or a later UI refresh from replaying the same event.
        public bool weaningAnnouncementLogged;
    }

    [Serializable]
    public class StoreRatListingData
    {
        public string id;
        public string name;
        public RatSex sex;
        public int price;
        // Legacy JSON has no version and defaults to zero, triggering the
        // one-time saved-price migration in StoreSystem.
        public int pricingVersion;
        public string markingFamily;
        public string coatColorVariant;
        public float coatTone = -1f;
        public GenotypeData genotype = new GenotypeData();
        public TraitData traits = new TraitData();
    }

    [Serializable]
    public class RatNameUseData
    {
        public string normalizedName;
        public string displayName;
        public string firstName;
        public string secondName;
        public RatSex sex;
        public string ratId;
        public long lastUsedGameTime;
        public long lastUsedRealTimestamp;
        public long lastFirstNameUsedGameTime;
        public long lastSecondNameUsedGameTime;
    }

    [Serializable]
    public class HabitatObjectData
    {
        public string id;
        public HabitatObjectType type;
        public string label;
        public float condition;
        public long lastServicedAt;
    }

    [Serializable]
    public class ColonyEventData
    {
        public long gameTimeMs;
        public string message;
        // Stable data-driven category used to filter the live top alert.
        // Older saves leave this empty and are migrated from the message.
        public string category;
    }

    [Serializable]
    public class AlertPreferenceData
    {
        public string category;
        public bool enabled = true;
    }

    [Serializable]
    public class ColonySaveData
    {
        public int schemaVersion = GameConfig.SaveVersion;
        public string colonyName = "New Generation";
        public long createdAt;
        public long updatedAt;
        // True only for a genuinely new/reset colony until the player
        // dismisses the one-time welcome modal. Missing in older saves,
        // which correctly defaults to false and never interrupts them.
        public bool welcomePopupPending;
        // Browser display preference. The initialized bit distinguishes an
        // intentional opt-out from an older JSON record that predates this
        // setting; older colonies therefore receive the gameplay-friendly
        // default of keeping the screen awake.
        public bool keepScreenAwake = true;
        public bool keepScreenAwakePreferenceInitialized;
        public ClockData clock = new ClockData();
        public List<string> ratIds = new List<string>();
        public List<RatData> rats = new List<RatData>();
        public List<RatData> retiredRats = new List<RatData>();
        public List<PregnancyData> pregnancies = new List<PregnancyData>();
        public List<DedicatedBreedingSessionData> breedingSessions = new List<DedicatedBreedingSessionData>();
        public List<LitterData> litters = new List<LitterData>();
        public List<HabitatObjectData> habitatObjects = new List<HabitatObjectData>();
        public int colonyCredits = 250;
        public int lifetimeSaleCredits;
        public bool currencyInitialized;
        public bool storeInventoryInitialized;
        // Purchased colony upgrades. Missing fields in older JSON deserialize
        // to zero and are migrated safely by UpgradeSystem.
        public int colonyCapacityUpgradeLevel;
        public int storeQualityUpgradeLevel;
        public List<StoreRatListingData> storeRatListings = new List<StoreRatListingData>();
        // In-game timestamp for the next market refresh. This is deliberately
        // separate from real time so changing UI pages cannot reroll stock.
        public long storeNextRestockGameTime;
        public int storeRestockCycle;
        public List<string> usedLitterNames = new List<string>();
        // Names remain in this history after sale or death. It lets the
        // allocator prefer never-used/long-unused names without changing
        // established living names during a UI refresh.
        public List<RatNameUseData> ratNameHistory = new List<RatNameUseData>();
        public int ratNameMigrationVersion;
        // Player-provided names are additive to the built-in pools and are
        // kept separate by sex so a custom list can be edited without
        // changing the shipped name data.
        public List<string> customMaleRatNames = new List<string>();
        public List<string> customFemaleRatNames = new List<string>();
        // Birth creates and saves the pups before the player names them. A
        // queue of litter IDs makes the naming modal recoverable after reload
        // and supports multiple births resolved in one simulation pass.
        public List<string> pendingNamingLitterIds = new List<string>();
        // Top-alert preferences are separate from event history. Disabling a
        // category hides its live banner but never removes its history row.
        public List<AlertPreferenceData> alertPreferences = new List<AlertPreferenceData>();
        // My Rats view preferences are persisted independently from rat data.
        // The string form keeps this compatible with older JSON and lets the
        // Fertility-next-opportunity migration remain explicit.
        public string myRatsSortField = "Name";
        public bool myRatsSortAscending = true;
        public string myRatsSexFilter = "All";
        // Absolute real-time deadline for the next automatic Pairing Habitat
        // evaluation. Persisting the deadline keeps save/load from resetting
        // the 30-second cadence.
        public long pairingNextCheckRealTimestamp;
        // Authoritative simulation-clock deadline. The legacy real-time field
        // remains for backward-compatible save reads, but new checks use this
        // value so 1x/2x/4x speed affects pairing consistently with biology.
        public long pairingNextCheckGameTime;
        // The player-facing event history is intentionally small. Events are
        // stored newest-first so the UI can render the same order without
        // sorting or rerolling anything after a save/load.
        public List<ColonyEventData> eventLog = new List<ColonyEventData>();

        public void EnsureLists()
        {
            if (!keepScreenAwakePreferenceInitialized)
            {
                keepScreenAwake = true;
                keepScreenAwakePreferenceInitialized = true;
            }
            clock ??= new ClockData();
            ratIds ??= new List<string>();
            rats ??= new List<RatData>();
            retiredRats ??= new List<RatData>();
            pregnancies ??= new List<PregnancyData>();
            breedingSessions ??= new List<DedicatedBreedingSessionData>();
            litters ??= new List<LitterData>();
            habitatObjects ??= new List<HabitatObjectData>();
            storeRatListings ??= new List<StoreRatListingData>();
            usedLitterNames ??= new List<string>();
            ratNameHistory ??= new List<RatNameUseData>();
            customMaleRatNames ??= new List<string>();
            customFemaleRatNames ??= new List<string>();
            pendingNamingLitterIds ??= new List<string>();
            alertPreferences ??= new List<AlertPreferenceData>();
            eventLog ??= new List<ColonyEventData>();
            if (string.IsNullOrEmpty(myRatsSortField)) myRatsSortField = "Name";
            if (string.IsNullOrEmpty(myRatsSexFilter)) myRatsSexFilter = "All";
        }
    }
}
