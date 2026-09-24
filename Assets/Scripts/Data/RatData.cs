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
        Senior
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
        // Retired records remain available for historical parent/litter views
        // but are no longer active in the habitat simulation.
        public RatRemovalDisposition removalDisposition = RatRemovalDisposition.None;
        public long removedAt;
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
    }

    [Serializable]
    public class StoreRatListingData
    {
        public string id;
        public string name;
        public RatSex sex;
        public int price;
        public string markingFamily;
        public string coatColorVariant;
        public float coatTone = -1f;
        public GenotypeData genotype = new GenotypeData();
        public TraitData traits = new TraitData();
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
    }

    [Serializable]
    public class ColonySaveData
    {
        public int schemaVersion = GameConfig.SaveVersion;
        public string colonyName = "New Generation";
        public long createdAt;
        public long updatedAt;
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
        public List<StoreRatListingData> storeRatListings = new List<StoreRatListingData>();
        // In-game timestamp for the next market refresh. This is deliberately
        // separate from real time so changing UI pages cannot reroll stock.
        public long storeNextRestockGameTime;
        public int storeRestockCycle;
        public List<string> usedLitterNames = new List<string>();
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
            eventLog ??= new List<ColonyEventData>();
        }
    }
}
