using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// The four-locus MVP genetics contract. All gameplay inheritance uses this
    /// class; the preview calls the deterministic methods in this same class.
    /// </summary>
    public static class GeneticsSystem
    {
        // A rare, non-gameplay marking-color inheritance variation. Keep this
        // separate from the S-locus mutation rate and store listing odds.
        public const float SecondaryMarkingInheritanceChance = 0.08f;

        // These labels are presentation-friendly names for the stable spotting
        // family. They do not replace the B/C/D/S loci used by inheritance.
        public static readonly string[] MarkingFamilies =
        {
            "Solid", "Self", "Hooded", "Broken hooded", "Berkshire", "Bareback",
            "Capped", "Mask", "Patch", "Black-eye white", "Variegated", "Variberk",
            "Irish", "Blaze", "Lightning blaze Siamese", "Badger blaze Siamese",
            "Dalmatian-style", "Dominant white spotted", "White side", "Merle", "Tabby/Marble",
            "Mismarked hooded"
        };

        // These are visual phenotype families layered on top of the existing
        // B/C/D loci. The loci still decide the black/brown, dilution, and
        // albino rules; a stable ID/genotype seed chooses the natural shade
        // within that genetic family.
        private static readonly string[] BlackCoatVariants =
        {
            "black", "black marten", "silvermane", "tonkinese", "roan", "agouti marten", "agouti",
            "tabby/marble"
        };
        private static readonly string[] BrownCoatVariants =
        {
            "agouti", "mink", "chocolate", "beige", "champagne", "fawn", "cinnamon",
            "russian cinnamon", "burmese", "aussie mink", "coffee", "agouti marten", "seal point siamese",
            "merle"
        };
        private static readonly string[] DilutedBlackCoatVariants =
        {
            "russian blue", "blue agouti", "american blue", "russian dove", "blue marten",
            "dove gray", "lilac", "silver fawn", "roan", "blue point siamese", "tabby/marble"
        };
        private static readonly string[] DilutedBrownCoatVariants =
        {
            "beige", "champagne", "fawn", "cinnamon", "russian cinnamon", "silver fawn",
            "lilac", "pink-eye platinum", "russian dove", "pink-eye white", "blue marten", "himalayan",
            "lightning blaze siamese"
        };

        [Serializable]
        public class GeneOutcomePreview
        {
            public string genotype;
            public float percentage;
        }

        [Serializable]
        public class LocusPreviewData
        {
            public string locus;
            public string parentA;
            public string parentB;
            public List<GeneOutcomePreview> outcomes = new List<GeneOutcomePreview>();
        }

        [Serializable]
        public class FurOutcomePreview
        {
            public string colorLabel;
            public string markingsLabel;
            public string colorHex;
            public float percentage;
        }

        [Serializable]
        public class TraitRangePreview
        {
            public string label;
            public float minimum;
            public float maximum;
            public float expected;
        }

        [Serializable]
        public class BreedingPreviewData
        {
            public List<LocusPreviewData> loci = new List<LocusPreviewData>();
            public List<FurOutcomePreview> furOutcomes = new List<FurOutcomePreview>();
            public List<TraitRangePreview> traitRanges = new List<TraitRangePreview>();
            // Both rates are per inherited allele, not per offspring.
            public float sLocusMutationChance;
            public float bcdMutationChance;
            public float visibleHairlessChance;

            // Two independent inherited s alleles: 1 - (1-p)^2 = p*(2-p).
            // This is the chance of gaining a marking allele, before albino
            // masking, not the inheritance probability for marked parents.
            public float solidParentSpontaneousMarkingChance
            {
                get { return sLocusMutationChance * (2f - sLocusMutationChance); }
            }
        }

        private class WeightedGenotype
        {
            public GenotypeData genotype;
            public float probability;
        }

        public static GenotypeData CreateFounder(
            string b1, string b2,
            string c1, string c2,
            string d1, string d2,
            string s1, string s2)
        {
            var genotype = new GenotypeData();
            genotype.loci.Add(new LocusData("B", b1, b2));
            genotype.loci.Add(new LocusData("C", c1, c2));
            genotype.loci.Add(new LocusData("D", d1, d2));
            genotype.loci.Add(new LocusData("S", s1, s2));
            Normalize(genotype);
            return genotype;
        }

        public static void Normalize(GenotypeData genotype)
        {
            if (genotype == null) return;
            if (genotype.loci == null) genotype.loci = new List<LocusData>();
            if (genotype.mutations == null) genotype.mutations = new List<MutationRecordData>();
            if (genotype.hairless == null)
                genotype.hairless = new LocusData("Hr", "Hr", "Hr");
            genotype.hairless.locus = "Hr";
            if (!GameConfig.IsValidAllele("Hr", genotype.hairless.firstAllele))
                genotype.hairless.firstAllele = "Hr";
            if (!GameConfig.IsValidAllele("Hr", genotype.hairless.secondAllele))
                genotype.hairless.secondAllele = "Hr";

            var normalized = new List<LocusData>();
            foreach (var locus in GameConfig.Loci)
            {
                LocusData found = null;
                foreach (var candidate in genotype.loci)
                {
                    if (candidate != null && candidate.locus == locus)
                    {
                        found = candidate;
                        break;
                    }
                }

                string first = found == null ? GameConfig.DominantAllele(locus) : found.firstAllele;
                string second = found == null ? GameConfig.DominantAllele(locus) : found.secondAllele;
                if (!GameConfig.IsValidAllele(locus, first)) first = GameConfig.DominantAllele(locus);
                if (!GameConfig.IsValidAllele(locus, second)) second = GameConfig.DominantAllele(locus);
                normalized.Add(new LocusData(locus, first, second));
            }
            genotype.loci = normalized;
        }

        public static LocusData GetLocus(GenotypeData genotype, string locus)
        {
            if (genotype == null) return new LocusData(locus, GameConfig.DominantAllele(locus), GameConfig.DominantAllele(locus));
            Normalize(genotype);
            if (locus == "Hr") return genotype.hairless;
            foreach (var item in genotype.loci)
            {
                if (item.locus == locus) return item;
            }
            return new LocusData(locus, GameConfig.DominantAllele(locus), GameConfig.DominantAllele(locus));
        }

        public static string FormatPair(GenotypeData genotype, string locus)
        {
            var pair = GetLocus(genotype, locus);
            return FormatPair(locus, pair.firstAllele, pair.secondAllele);
        }

        public static string FormatPair(string locus, string first, string second)
        {
            var dominant = GameConfig.DominantAllele(locus);
            if (first == dominant && second != dominant) return first + "/" + second;
            if (second == dominant && first != dominant) return second + "/" + first;
            return first + "/" + second;
        }

        public static GenotypeData InheritGenotype(GenotypeData mother, GenotypeData father, long recordedAt)
        {
            if (mother == null) mother = new GenotypeData();
            if (father == null) father = new GenotypeData();
            Normalize(mother);
            Normalize(father);
            var child = new GenotypeData();

            foreach (var locus in GameConfig.Loci)
            {
                var motherPair = GetLocus(mother, locus);
                var fatherPair = GetLocus(father, locus);
                string inheritedFromMother = UnityEngine.Random.Range(0, 2) == 0 ? motherPair.firstAllele : motherPair.secondAllele;
                string inheritedFromFather = UnityEngine.Random.Range(0, 2) == 0 ? fatherPair.firstAllele : fatherPair.secondAllele;

                inheritedFromMother = ApplyMutation(locus, inheritedFromMother, "mother", recordedAt, child.mutations);
                inheritedFromFather = ApplyMutation(locus, inheritedFromFather, "father", recordedAt, child.mutations);
                child.loci.Add(new LocusData(locus, inheritedFromMother, inheritedFromFather));
            }

            child.hairless = InheritHairless(mother.hairless, father.hairless,
                UnityEngine.Random.value, UnityEngine.Random.value,
                UnityEngine.Random.value, recordedAt, child.mutations);
            Normalize(child);
            return child;
        }

        // Explicit rolls make the inheritance contract testable without changing
        // Unity's shared RNG, and births persist the result rather than rerolling.
        public static LocusData InheritHairless(LocusData mother, LocusData father,
            float maternalRoll, float paternalRoll, float mutationRoll, long recordedAt,
            List<MutationRecordData> mutations)
        {
            string m1 = mother == null ? "Hr" : mother.firstAllele;
            string m2 = mother == null ? "Hr" : mother.secondAllele;
            string f1 = father == null ? "Hr" : father.firstAllele;
            string f2 = father == null ? "Hr" : father.secondAllele;
            var result = new LocusData("Hr", maternalRoll < 0.5f ? m1 : m2,
                paternalRoll < 0.5f ? f1 : f2);
            if (m1 == "Hr" && m2 == "Hr" && f1 == "Hr" && f2 == "Hr" &&
                mutationRoll < GameConfig.SpontaneousHairlessChance)
            {
                result.firstAllele = result.secondAllele = "hr";
                if (mutations != null) mutations.Add(new MutationRecordData
                {
                    locus = "Hr", parentRole = "spontaneous", from = "Hr/Hr",
                    to = "hr/hr", recordedAt = recordedAt
                });
            }
            return result;
        }

        public static bool IsHairless(GenotypeData genotype)
        {
            return genotype != null && genotype.hairless != null &&
                genotype.hairless.firstAllele == "hr" && genotype.hairless.secondAllele == "hr";
        }

        private static string ApplyMutation(
            string locus,
            string allele,
            string parentRole,
            long recordedAt,
            List<MutationRecordData> mutations)
        {
            if (UnityEngine.Random.value >= MutationRateForLocus(locus)) return allele;
            string mutated = allele == GameConfig.DominantAllele(locus)
                ? GameConfig.RecessiveAllele(locus)
                : GameConfig.DominantAllele(locus);
            mutations.Add(new MutationRecordData
            {
                locus = locus,
                parentRole = parentRole,
                from = allele,
                to = mutated,
                recordedAt = recordedAt,
            });
            return mutated;
        }

        public static float MutationRateForLocus(string locus)
        {
            // Hr uses a whole-trait event, never the coat's per-allele mutator.
            if (locus == "Hr") return 0f;
            return string.Equals(locus, "S", StringComparison.Ordinal)
                ? GameConfig.MarkingMutationRate
                : GameConfig.MutationRate;
        }

        /// <summary>
        /// Developer fixture helper: changes one solid S-locus allele to the
        /// dominant spotting allele and records it through the same persisted
        /// mutation history used by normal inheritance.
        /// </summary>
        public static bool ForceMarkingMutation(GenotypeData genotype, string parentRole, long recordedAt)
        {
            if (genotype == null) return false;
            Normalize(genotype);
            LocusData marking = GetLocus(genotype, "S");
            if (marking == null) return false;

            string from;
            if (marking.firstAllele == "s")
            {
                from = marking.firstAllele;
                marking.firstAllele = "S";
            }
            else if (marking.secondAllele == "s")
            {
                from = marking.secondAllele;
                marking.secondAllele = "S";
            }
            else
            {
                return false;
            }

            if (genotype.mutations == null) genotype.mutations = new List<MutationRecordData>();
            genotype.mutations.Add(new MutationRecordData
            {
                locus = "S",
                parentRole = string.IsNullOrEmpty(parentRole) ? "developer-test" : parentRole,
                from = from,
                to = "S",
                recordedAt = recordedAt,
            });
            Normalize(genotype);
            return true;
        }

        public static TraitData InheritTraits(TraitData mother, TraitData father)
        {
            // Null parent trait data is missing data; a numeric zero on a
            // real parent is intentional and must remain zero in the
            // midpoint calculation.
            return new TraitData(
                Vary(Midpoint(mother == null ? 0f : mother.size, father == null ? 0f : father.size)),
                Vary(Midpoint(mother == null ? 0f : mother.health, father == null ? 0f : father.health)),
                Vary(Midpoint(mother == null ? 0f : mother.fertility, father == null ? 0f : father.fertility)));
        }

        private static float Vary(float midpoint)
        {
            float safeMidpoint = ClampTrait(midpoint);
            if (safeMidpoint <= 0f) return 0f;
            return Mathf.Clamp(safeMidpoint + UnityEngine.Random.Range(-GameConfig.TraitVariation, GameConfig.TraitVariation), 0f, 100f);
        }

        private static float Midpoint(float first, float second)
        {
            return (ClampTrait(first) + ClampTrait(second)) * 0.5f;
        }

        private static float ClampTrait(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Mathf.Clamp(value, 0f, 100f);
        }

        public static PhenotypeData DerivePhenotype(
            RatStage stage,
            GenotypeData genotype,
            string coatColorVariant = null,
            float coatTone = 1f)
        {
            if (genotype == null) genotype = new GenotypeData();
            Normalize(genotype);
            var phenotype = new PhenotypeData();
            phenotype.hairless = IsHairless(genotype);
            if (stage == RatStage.Pinkie)
            {
                phenotype.furRevealed = false;
                phenotype.coatColorId = "unknown";
                phenotype.coatColorLabel = "Unknown";
                phenotype.coatColorHex = "#ef8e8e";
                phenotype.accentHex = "#ffb4b4";
                phenotype.spotted = false;
                phenotype.markingsLabel = "Hidden while Pinkie";
                return phenotype;
            }

            bool albino = IsRecessive(genotype, "C");
            bool diluted = IsRecessive(genotype, "D");
            bool black = HasDominant(genotype, "B");
            bool spotted = HasDominant(genotype, "S");

            phenotype.furRevealed = true;
            phenotype.spotted = spotted;
            phenotype.markingsLabel = spotted ? "White spotting" : "Solid coat";

            if (albino)
            {
                phenotype.coatColorId = "albino";
                phenotype.coatColorLabel = "Albino";
                phenotype.coatColorHex = "#f1eee7";
                phenotype.accentHex = "#e98c8c";
                phenotype.spotted = false;
                phenotype.markingsLabel = "Albino masking";
                if (phenotype.hairless) phenotype.coatColorLabel = "Hairless • Albino";
                return phenotype;
            }

            if (black)
            {
                phenotype.coatColorId = diluted ? "diluted-black" : "black";
                phenotype.coatColorLabel = diluted ? "Diluted black" : "Black";
                phenotype.coatColorHex = diluted ? "#818993" : "#272a30";
                phenotype.accentHex = diluted ? "#c3c9d1" : "#8d949d";
            }
            else
            {
                phenotype.coatColorId = diluted ? "diluted-brown" : "brown";
                phenotype.coatColorLabel = diluted ? "Diluted brown" : "Brown";
                phenotype.coatColorHex = diluted ? "#b58c78" : "#8a5534";
                phenotype.accentHex = diluted ? "#e1b9a1" : "#d49b68";
            }
            if (!string.IsNullOrEmpty(coatColorVariant))
                ApplyCoatColorVariant(phenotype, coatColorVariant, coatTone);
            if (phenotype.hairless) phenotype.coatColorLabel = "Hairless • " + phenotype.coatColorLabel;
            return phenotype;
        }

        /// <summary>
        /// Assigns a stable natural shade without changing the authoritative
        /// genotype. Old saves with no variant are migrated from their rat ID
        /// and genotype, so the same rat never rerolls on a UI refresh/load.
        /// </summary>
        public static void EnsureCoatAppearance(RatData rat)
        {
            if (rat == null) return;
            if (rat.genotype == null) rat.genotype = new GenotypeData();
            Normalize(rat.genotype);
            string stableId = string.IsNullOrEmpty(rat.id) ? rat.name : rat.id;
            // C/c c/c is an authoritative albino override. Repair old saves
            // that accidentally stored a normal shade for an albino rat so
            // every visual path receives the same white phenotype.
            if (IsRecessive(rat.genotype, "C"))
            {
                rat.coatColorVariant = "albino";
                rat.coatTone = 1f;
                return;
            }

            if (string.IsNullOrEmpty(rat.coatColorVariant) ||
                rat.coatColorVariant.Equals("albino", System.StringComparison.OrdinalIgnoreCase) ||
                !IsKnownCoatColorVariant(rat.coatColorVariant))
                rat.coatColorVariant = DefaultCoatColorVariant(stableId, rat.genotype);
            if (rat.coatTone <= 0f || float.IsNaN(rat.coatTone) || float.IsInfinity(rat.coatTone))
                rat.coatTone = DefaultCoatTone(stableId, rat.genotype);
        }

        public static bool IsKnownCoatColorVariant(string variant)
        {
            if (string.IsNullOrEmpty(variant)) return false;
            switch (variant.ToLowerInvariant())
            {
                case "black":
                case "chocolate":
                case "agouti":
                case "mink":
                case "russian blue":
                case "blue agouti":
                case "dove gray":
                case "dove-gray":
                case "blue-gray":
                case "beige":
                case "champagne":
                case "fawn":
                case "cinnamon":
                case "russian cinnamon":
                case "silver fawn":
                case "silver-fawn":
                case "american blue":
                case "black marten":
                case "tonkinese":
                case "burmese":
                case "roan":
                case "agouti marten":
                case "aussie mink":
                case "coffee":
                case "blue marten":
                case "pink-eye platinum":
                case "russian dove":
                case "pink-eye white":
                case "lilac":
                case "silvermane":
                case "seal point siamese":
                case "blue point siamese":
                case "himalayan":
                case "merle":
                case "tabby/marble":
                case "lightning blaze siamese":
                case "brown":
                case "diluted-black":
                case "diluted-brown":
                    return true;
                default:
                    return false;
            }
        }

        public static string DefaultCoatColorVariant(string stableId, GenotypeData genotype)
        {
            if (genotype == null) genotype = new GenotypeData();
            Normalize(genotype);
            if (IsRecessive(genotype, "C")) return "albino";

            bool black = HasDominant(genotype, "B");
            bool diluted = IsRecessive(genotype, "D");
            bool carriesAlbino = HasAllele(genotype, "C", "c");
            string[] palette = black
                ? (diluted ? DilutedBlackCoatVariants : BlackCoatVariants)
                : (diluted ? DilutedBrownCoatVariants : BrownCoatVariants);
            uint seed = StableColorHash((stableId ?? string.Empty) + "|" + GenotypeKey(genotype));
            if (carriesAlbino && seed % 17u == 0u)
                return diluted ? "pink-eye white" : "pink-eye platinum";
            return palette[seed % (uint)palette.Length];
        }

        public static float DefaultCoatTone(string stableId, GenotypeData genotype)
        {
            uint seed = StableColorHash((stableId ?? string.Empty) + "|tone|" + GenotypeKey(genotype));
            // Muted variation only: 0.94 through 1.06 of the family value.
            return 0.94f + (seed % 13u) * 0.01f;
        }

        public static void ApplyCoatColorVariant(PhenotypeData phenotype, string variant, float tone = 1f)
        {
            if (phenotype == null || !phenotype.furRevealed || phenotype.coatColorId == "albino" ||
                string.IsNullOrEmpty(variant)) return;

            Color baseColor;
            Color accentColor;
            string label;
            switch (variant.ToLowerInvariant())
            {
                case "black": label = "Black"; baseColor = Hex("272a30"); accentColor = Hex("8d949d"); break;
                case "agouti": label = "Agouti"; baseColor = Hex("806044"); accentColor = Hex("b99568"); break;
                case "mink": label = "Mink"; baseColor = Hex("897064"); accentColor = Hex("c2a697"); break;
                case "russian blue": label = "Russian blue"; baseColor = Hex("66727f"); accentColor = Hex("aab7c3"); break;
                case "blue agouti": label = "Blue agouti"; baseColor = Hex("74767a"); accentColor = Hex("afb1b1"); break;
                case "dove gray":
                case "dove-gray": label = "Dove gray"; baseColor = Hex("9299a0"); accentColor = Hex("c8cdd0"); break;
                case "blue-gray": label = "Blue-gray"; baseColor = Hex("7e8792"); accentColor = Hex("b8c0c8"); break;
                case "beige": label = "Beige"; baseColor = Hex("b39a79"); accentColor = Hex("d7bd98"); break;
                case "champagne": label = "Champagne"; baseColor = Hex("d1b99b"); accentColor = Hex("f0ddc2"); break;
                case "fawn": label = "Fawn"; baseColor = Hex("c48d5b"); accentColor = Hex("e3b17e"); break;
                case "cinnamon": label = "Cinnamon"; baseColor = Hex("a56d4b"); accentColor = Hex("d19a6a"); break;
                case "russian cinnamon": label = "Russian cinnamon"; baseColor = Hex("ae6d35"); accentColor = Hex("d59652"); break;
                case "silver fawn":
                case "silver-fawn": label = "Silver fawn"; baseColor = Hex("bda090"); accentColor = Hex("e1cbb8"); break;
                case "american blue": label = "American blue"; baseColor = Hex("788391"); accentColor = Hex("b3bfca"); break;
                case "black marten": label = "Black marten"; baseColor = Hex("393b40"); accentColor = Hex("81858d"); break;
                case "tonkinese": label = "Tonkinese"; baseColor = Hex("71584d"); accentColor = Hex("aa806b"); break;
                case "burmese": label = "Burmese"; baseColor = Hex("806047"); accentColor = Hex("bb916c"); break;
                case "roan": label = "Roan"; baseColor = Hex("a6a3a0"); accentColor = Hex("dfdcd7"); break;
                case "agouti marten": label = "Agouti marten"; baseColor = Hex("786e65"); accentColor = Hex("b5a99d"); break;
                case "aussie mink": label = "Aussie mink"; baseColor = Hex("9d8278"); accentColor = Hex("ceb2a4"); break;
                case "coffee": label = "Coffee"; baseColor = Hex("826351"); accentColor = Hex("b58d73"); break;
                case "chocolate":
                case "brown": label = "Chocolate"; baseColor = Hex("684536"); accentColor = Hex("b6815d"); break;
                case "blue marten": label = "Blue marten"; baseColor = Hex("8a8f99"); accentColor = Hex("c2c7ce"); break;
                case "pink-eye platinum": label = "Pink-eye platinum"; baseColor = Hex("e1dedb"); accentColor = Hex("faf8f3"); break;
                case "russian dove": label = "Russian dove"; baseColor = Hex("b9b7b3"); accentColor = Hex("dfdcda"); break;
                case "pink-eye white": label = "Pink-eye white"; baseColor = Hex("f5f2eb"); accentColor = Hex("fffdf8"); break;
                case "lilac": label = "Lilac"; baseColor = Hex("8b7d91"); accentColor = Hex("b8adba"); break;
                case "silvermane": label = "Silvermane"; baseColor = Hex("4f5359"); accentColor = Hex("a7adb5"); break;
                case "seal point siamese": label = "Seal point Siamese"; baseColor = Hex("e3d6c5"); accentColor = Hex("8b735f"); break;
                case "blue point siamese": label = "Blue point Siamese"; baseColor = Hex("d8dde1"); accentColor = Hex("7b8793"); break;
                case "himalayan": label = "Himalayan"; baseColor = Hex("f1eee8"); accentColor = Hex("b8a896"); break;
                case "merle": label = "Merle"; baseColor = Hex("877b76"); accentColor = Hex("c0b6af"); break;
                case "tabby/marble": label = "Tabby / Marble"; baseColor = Hex("5e6066"); accentColor = Hex("a6a8ad"); break;
                case "lightning blaze siamese": label = "Lightning blaze Siamese"; baseColor = Hex("e7ded0"); accentColor = Hex("9a806b"); break;
                default:
                    return;
            }

            if (tone <= 0f) tone = 1f;
            baseColor = ToneColor(baseColor, tone);
            accentColor = ToneColor(accentColor, tone);
            phenotype.coatColorLabel = label;
            phenotype.coatColorHex = ToHex(baseColor);
            phenotype.accentHex = ToHex(accentColor);
        }

        private static Color Hex(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString("#" + value, out color) ? color : Color.gray;
        }

        private static Color ToneColor(Color color, float tone)
        {
            float hue;
            float saturation;
            float value;
            Color.RGBToHSV(color, out hue, out saturation, out value);
            saturation = Mathf.Clamp(saturation * Mathf.Lerp(0.96f, 1.04f, Mathf.InverseLerp(0.94f, 1.06f, tone)), 0f, 1f);
            value = Mathf.Clamp01(value * tone);
            return Color.HSVToRGB(hue, saturation, value);
        }

        private static string ToHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(color).ToLowerInvariant();
        }

        private static uint StableColorHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (value != null)
                {
                    for (int index = 0; index < value.Length; index++)
                        hash = (hash ^ value[index]) * 16777619u;
                }
                return hash == 0u ? 1u : hash;
            }
        }

        public static string NormalizeMarkingFamily(string family, GenotypeData genotype)
        {
            if (string.IsNullOrEmpty(family))
                return HasDominant(genotype, "S") ? "Dalmatian-style" : "Solid";
            string normalizedFamily = family.Trim().ToLowerInvariant();
            switch (normalizedFamily)
            {
                case "self": return "Self";
                case "solid": return "Solid";
                case "broken hooded": return "Broken hooded";
                case "hooded": return "Hooded";
                case "berkshire": return "Berkshire";
                case "bareback": return "Bareback";
                case "cap":
                case "capped": return "Capped";
                case "mask": return "Mask";
                case "patch": return "Patch";
                case "black-eye white": return "Black-eye white";
                case "variegated": return "Variegated";
                case "variberk": return "Variberk";
                case "irish": return "Irish";
                case "blaze":
                case "facial blaze": return "Blaze";
                case "lightning blaze siamese": return "Lightning blaze Siamese";
                case "badger blaze siamese": return "Badger blaze Siamese";
                case "dalmatian-style": return "Dalmatian-style";
                case "dalmatian": return "Dalmatian-style";
                case "dominant white spotted": return "Dominant white spotted";
                case "white side": return "White side";
                case "merle": return "Merle";
                case "tabby/marble": return "Tabby/Marble";
                case "mismarked hooded": return "Mismarked hooded";
            }
            for (int i = 0; i < MarkingFamilies.Length; i++)
            {
                if (string.Equals(MarkingFamilies[i], family, StringComparison.OrdinalIgnoreCase))
                    return MarkingFamilies[i];
            }
            return HasDominant(genotype, "S") ? "Dalmatian-style" : "Solid";
        }

        public static void ApplyMarkingFamily(PhenotypeData phenotype, string family)
        {
            if (phenotype == null) return;
            if (!phenotype.furRevealed)
            {
                phenotype.markingsLabel = "Hidden while Pinkie";
                phenotype.markingFamily = family;
                return;
            }
            if (phenotype.coatColorId == "albino")
            {
                phenotype.spotted = false;
                phenotype.markingFamily = "Albino masking";
                phenotype.markingsLabel = "Albino masking";
                return;
            }
            string normalized = string.IsNullOrEmpty(family)
                ? (phenotype.spotted ? "Dalmatian-style" : "Solid")
                : NormalizeMarkingFamily(family, null);
            phenotype.markingFamily = normalized;
            phenotype.spotted = normalized != "Solid" && normalized != "Self";
            phenotype.markingsLabel = normalized == "Solid" || normalized == "Self"
                ? "Self / solid coat" : normalized;
        }

        public static string ResolveOffspringCoatColorVariant(
            RatData mother, RatData father, GenotypeData childGenotype, string childId)
        {
            if (childGenotype == null) childGenotype = new GenotypeData();
            Normalize(childGenotype);
            if (IsRecessive(childGenotype, "C")) return "albino";

            string motherVariant = mother == null ? string.Empty : mother.coatColorVariant;
            string fatherVariant = father == null ? string.Empty : father.coatColorVariant;
            if (!IsKnownCoatColorVariant(motherVariant)) motherVariant = string.Empty;
            if (!IsKnownCoatColorVariant(fatherVariant)) fatherVariant = string.Empty;
            if (!string.IsNullOrEmpty(motherVariant) && motherVariant == fatherVariant)
                return motherVariant;
            if (string.IsNullOrEmpty(motherVariant)) return string.IsNullOrEmpty(fatherVariant)
                ? DefaultCoatColorVariant(childId, childGenotype) : fatherVariant;
            if (string.IsNullOrEmpty(fatherVariant)) return motherVariant;

            uint seed = StableColorHash((childId ?? string.Empty) + "|inherited-coat|" + GenotypeKey(childGenotype));
            return (seed & 1u) == 0u ? motherVariant : fatherVariant;
        }

        private static bool HasAllele(GenotypeData genotype, string locus, string allele)
        {
            var pair = GetLocus(genotype, locus);
            return pair.firstAllele == allele || pair.secondAllele == allele;
        }

        public static string DefaultMarkingFamily(GenotypeData genotype)
        {
            return NormalizeMarkingFamily(null, genotype);
        }

        public static string ResolveOffspringMarkingFamily(
            RatData mother, RatData father, GenotypeData childGenotype, string stableChildId = null)
        {
            string fallback = DefaultMarkingFamily(childGenotype);
            string motherFamily = NormalizeMarkingFamily(mother == null ? null : mother.markingFamily,
                mother == null ? childGenotype : mother.genotype);
            string fatherFamily = NormalizeMarkingFamily(father == null ? null : father.markingFamily,
                father == null ? childGenotype : father.genotype);
            bool childSpotted = HasDominant(childGenotype, "S");
            if (!childSpotted) return "Solid";
            bool motherSolid = IsSolidMarkingFamily(motherFamily);
            bool fatherSolid = IsSolidMarkingFamily(fatherFamily);
            if (motherFamily == fatherFamily && !motherSolid) return motherFamily;
            if (!motherSolid && fatherSolid) return motherFamily;
            if (!fatherSolid && motherSolid) return fatherFamily;
            if (!motherSolid && !fatherSolid)
            {
                string stableKey = string.IsNullOrEmpty(stableChildId)
                    ? (mother == null ? string.Empty : mother.id) + "|" +
                      (father == null ? string.Empty : father.id) + "|" + GenotypeKey(childGenotype)
                    : stableChildId;
                return (StableColorHash(stableKey + "|marking-family") & 1u) == 0u
                    ? motherFamily
                    : fatherFamily;
            }
            if (!fatherSolid) return fatherFamily;
            return fallback;
        }

        /// <summary>
        /// Occasionally preserves each marked parent's visible marking color
        /// on one pup. The stable key includes both parents, litter/pup IDs,
        /// families, colors, and inherited genotype; the stored fields then
        /// make the outcome durable across save/load and visual refreshes.
        /// </summary>
        public static bool TryInheritSecondaryMarking(
            RatData mother,
            RatData father,
            GenotypeData childGenotype,
            string childPrimaryFamily,
            string litterId,
            string pupId,
            out string primaryColorHex,
            out string secondaryFamily,
            out string secondaryColorHex)
        {
            primaryColorHex = null;
            secondaryFamily = null;
            secondaryColorHex = null;
            if (!HasColoredMarkings(mother) || !HasColoredMarkings(father)) return false;

            string motherFamily = NormalizeMarkingFamily(mother.markingFamily, mother.genotype);
            string fatherFamily = NormalizeMarkingFamily(father.markingFamily, father.genotype);
            string childFamily = NormalizeMarkingFamily(childPrimaryFamily, childGenotype);
            if (IsSolidMarkingFamily(childFamily)) return false;

            string motherColor = RatVisualFactory.ResolveMarkingColorHex(mother);
            string fatherColor = RatVisualFactory.ResolveMarkingColorHex(father);
            if (!MarkingColorsAreDistinct(motherColor, fatherColor)) return false;

            string stableKey = (mother.id ?? string.Empty) + "|" + (father.id ?? string.Empty) + "|" +
                (litterId ?? string.Empty) + "|" + (pupId ?? string.Empty) + "|" +
                childFamily + "|" + motherFamily + "|" + fatherFamily + "|" +
                motherColor + "|" + fatherColor + "|" + GenotypeKey(childGenotype);
            uint roll = StableColorHash(stableKey + "|secondary-marking-roll") % 10000u;
            if (roll >= (uint)(SecondaryMarkingInheritanceChance * 10000f)) return false;

            // Keep the normal primary-family inheritance decision intact. If
            // both parents share that family, choose the primary color source
            // deterministically; the other parent supplies the second layer.
            bool motherIsPrimary = childFamily == motherFamily && childFamily != fatherFamily;
            bool fatherIsPrimary = childFamily == fatherFamily && childFamily != motherFamily;
            if (!motherIsPrimary && !fatherIsPrimary)
                motherIsPrimary = (StableColorHash(stableKey + "|primary-marking-parent") & 1u) == 0u;

            primaryColorHex = motherIsPrimary ? motherColor : fatherColor;
            secondaryFamily = motherIsPrimary ? fatherFamily : motherFamily;
            secondaryColorHex = motherIsPrimary ? fatherColor : motherColor;
            return true;
        }

        private static bool HasColoredMarkings(RatData rat)
        {
            if (rat == null || rat.phenotype == null || !rat.phenotype.spotted ||
                IsAlbinoGenotype(rat.genotype)) return false;
            return !IsSolidMarkingFamily(NormalizeMarkingFamily(rat.markingFamily, rat.genotype));
        }

        private static bool MarkingColorsAreDistinct(string firstHex, string secondHex)
        {
            Color first;
            Color second;
            if (!ColorUtility.TryParseHtmlString(firstHex, out first) ||
                !ColorUtility.TryParseHtmlString(secondHex, out second)) return false;
            return Vector3.Distance(new Vector3(first.r, first.g, first.b),
                new Vector3(second.r, second.g, second.b)) >= 0.12f;
        }

        private static bool IsSolidMarkingFamily(string family)
        {
            return string.Equals(family, "Solid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(family, "Self", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasDominant(GenotypeData genotype, string locus)
        {
            var pair = GetLocus(genotype, locus);
            return pair.firstAllele == GameConfig.DominantAllele(locus) || pair.secondAllele == GameConfig.DominantAllele(locus);
        }

        public static bool IsAlbinoGenotype(GenotypeData genotype)
        {
            return IsRecessive(genotype, "C");
        }

        private static bool IsRecessive(GenotypeData genotype, string locus)
        {
            var pair = GetLocus(genotype, locus);
            var recessive = GameConfig.RecessiveAllele(locus);
            return pair.firstAllele == recessive && pair.secondAllele == recessive;
        }

        public static BreedingPreviewData BuildPreview(RatData parentA, RatData parentB)
        {
            var preview = new BreedingPreviewData
            {
                sLocusMutationChance = GameConfig.MarkingMutationRate,
                bcdMutationChance = GameConfig.MutationRate,
            };
            if (parentA == null || parentB == null) return preview;

            if (parentA.genotype == null) parentA.genotype = new GenotypeData();
            if (parentB.genotype == null) parentB.genotype = new GenotypeData();
            Normalize(parentA.genotype);
            Normalize(parentB.genotype);
            foreach (var locus in GameConfig.Loci)
            {
                var locusPreview = new LocusPreviewData
                {
                    locus = locus,
                    parentA = FormatPair(parentA.genotype, locus),
                    parentB = FormatPair(parentB.genotype, locus),
                };
                AddLocusOutcomes(locusPreview, GetLocus(parentA.genotype, locus), GetLocus(parentB.genotype, locus));
                preview.loci.Add(locusPreview);
            }
            var hairlessPreview = new LocusPreviewData
            {
                locus = "Hr", parentA = FormatPair(parentA.genotype, "Hr"),
                parentB = FormatPair(parentB.genotype, "Hr")
            };
            AddLocusOutcomes(hairlessPreview, parentA.genotype.hairless, parentB.genotype.hairless);
            preview.loci.Add(hairlessPreview);
            float maternalHairless = (parentA.genotype.hairless.firstAllele == "hr" ? .5f : 0f) +
                (parentA.genotype.hairless.secondAllele == "hr" ? .5f : 0f);
            float paternalHairless = (parentB.genotype.hairless.firstAllele == "hr" ? .5f : 0f) +
                (parentB.genotype.hairless.secondAllele == "hr" ? .5f : 0f);
            preview.visibleHairlessChance = maternalHairless*paternalHairless;
            if (maternalHairless == 0f && paternalHairless == 0f)
            {
                preview.visibleHairlessChance = GameConfig.SpontaneousHairlessChance;
                hairlessPreview.outcomes[0].percentage = (1f-GameConfig.SpontaneousHairlessChance)*100f;
                hairlessPreview.outcomes.Add(new GeneOutcomePreview
                    { genotype = "hr/hr (spontaneous)", percentage = GameConfig.SpontaneousHairlessChance*100f });
            }

            var weighted = new List<WeightedGenotype>
            {
                new WeightedGenotype { genotype = new GenotypeData(), probability = 1f }
            };
            foreach (var locus in GameConfig.Loci)
            {
                var next = new List<WeightedGenotype>();
                var motherPair = GetLocus(parentA.genotype, locus);
                var fatherPair = GetLocus(parentB.genotype, locus);
                var pairs = new List<LocusData>
                {
                    new LocusData(locus, motherPair.firstAllele, fatherPair.firstAllele),
                    new LocusData(locus, motherPair.firstAllele, fatherPair.secondAllele),
                    new LocusData(locus, motherPair.secondAllele, fatherPair.firstAllele),
                    new LocusData(locus, motherPair.secondAllele, fatherPair.secondAllele),
                };
                foreach (var current in weighted)
                {
                    foreach (var pair in pairs)
                    {
                        var child = current.genotype.Clone();
                        child.loci.Add(pair.Clone());
                        next.Add(new WeightedGenotype { genotype = child, probability = current.probability * 0.25f });
                    }
                }
                weighted = MergeWeightedGenotypes(next);
            }

            var furBuckets = new Dictionary<string, FurOutcomePreview>();
            foreach (var item in weighted)
            {
                string previewVariant = DefaultCoatColorVariant("breeding-preview|" + GenotypeKey(item.genotype), item.genotype);
                var phenotype = DerivePhenotype(RatStage.YoungRat, item.genotype,
                    previewVariant, DefaultCoatTone("breeding-preview|" + GenotypeKey(item.genotype), item.genotype));
                string previewMarkingFamily = DefaultMarkingFamily(item.genotype);
                ApplyMarkingFamily(phenotype, previewMarkingFamily);
                string key = phenotype.coatColorLabel + "|" + phenotype.markingsLabel;
                FurOutcomePreview bucket;
                if (!furBuckets.TryGetValue(key, out bucket))
                {
                    bucket = new FurOutcomePreview
                    {
                        colorLabel = phenotype.coatColorLabel,
                        markingsLabel = phenotype.markingsLabel,
                        colorHex = phenotype.coatColorHex,
                    };
                    furBuckets.Add(key, bucket);
                }
                bucket.percentage += item.probability * 100f;
            }
            foreach (var bucket in furBuckets.Values)
            {
                bucket.percentage = RoundPercent(bucket.percentage);
                preview.furOutcomes.Add(bucket);
            }

            preview.traitRanges.Add(BuildTraitRange("Size", parentA.traits == null ? 0f : parentA.traits.size, parentB.traits == null ? 0f : parentB.traits.size));
            preview.traitRanges.Add(BuildTraitRange("Health", parentA.traits == null ? 0f : parentA.traits.health, parentB.traits == null ? 0f : parentB.traits.health));
            preview.traitRanges.Add(BuildTraitRange("Fertility", parentA.traits == null ? 0f : parentA.traits.fertility, parentB.traits == null ? 0f : parentB.traits.fertility));
            return preview;
        }

        private static void AddLocusOutcomes(LocusPreviewData preview, LocusData parentA, LocusData parentB)
        {
            var buckets = new Dictionary<string, GeneOutcomePreview>();
            string[] motherAlleles = { parentA.firstAllele, parentA.secondAllele };
            string[] fatherAlleles = { parentB.firstAllele, parentB.secondAllele };
            for (int i = 0; i < motherAlleles.Length; i++)
            {
                for (int j = 0; j < fatherAlleles.Length; j++)
                {
                    string key = FormatPair(preview.locus, motherAlleles[i], fatherAlleles[j]);
                    GeneOutcomePreview outcome;
                    if (!buckets.TryGetValue(key, out outcome))
                    {
                        outcome = new GeneOutcomePreview { genotype = key };
                        buckets.Add(key, outcome);
                    }
                    outcome.percentage += 25f;
                }
            }
            foreach (var outcome in buckets.Values)
            {
                outcome.percentage = RoundPercent(outcome.percentage);
                preview.outcomes.Add(outcome);
            }
        }

        private static List<WeightedGenotype> MergeWeightedGenotypes(List<WeightedGenotype> values)
        {
            var buckets = new Dictionary<string, WeightedGenotype>();
            foreach (var value in values)
            {
                string key = GenotypeKey(value.genotype);
                WeightedGenotype existing;
                if (!buckets.TryGetValue(key, out existing))
                {
                    existing = new WeightedGenotype { genotype = value.genotype, probability = 0f };
                    buckets.Add(key, existing);
                }
                existing.probability += value.probability;
            }
            return new List<WeightedGenotype>(buckets.Values);
        }

        private static string GenotypeKey(GenotypeData genotype)
        {
            var parts = new List<string>();
            foreach (var locus in GameConfig.Loci) parts.Add(FormatPair(genotype, locus));
            return string.Join("|", parts.ToArray());
        }

        private static TraitRangePreview BuildTraitRange(string label, float first, float second)
        {
            float expected = (first + second) * 0.5f;
            return new TraitRangePreview
            {
                label = label,
                minimum = Mathf.Clamp(expected - GameConfig.TraitVariation, 0f, 100f),
                maximum = Mathf.Clamp(expected + GameConfig.TraitVariation, 0f, 100f),
                expected = expected,
            };
        }

        private static float RoundPercent(float value)
        {
            return Mathf.Round(value * 10f) / 10f;
        }
    }
}
