using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Single source of truth for the colony's physical rat zones. Simulation
    /// code owns the assignment; presentation and behavior ask this system for
    /// placement, bounds, and target points instead of maintaining independent
    /// position checks.
    /// </summary>
    public static class EnclosureSystem
    {
        private const float MinimumMovementMargin = 0.38f;
        private const float NurseryNestY = 0.28f;
        // Nursery keeps its existing visual footprint. The Pairing nest is a
        // The imported Pairing nest is approximately 6.38 x 4.58 world units
        // including its rounded rims. Keep the movement exclusion just beyond
        // that footprint so adult and young rats route around it without
        // making pinkie placement or the mother-side birth position invalid.
        public const float NestAdultExclusionRadiusX = 1.82f;
        public const float NestAdultExclusionRadiusZ = 1.40f;
        public const float PairingNestAdultExclusionRadiusX = 3.32f;
        public const float PairingNestAdultExclusionRadiusZ = 2.46f;

        // Male and Female retain the original full-depth cage dimensions.
        // Nursery and Breeding occupy a separate lower row in the previously
        // unused open space; no cage is scaled to make room for another one.
        private static readonly Definition MaleDefinition = new Definition(
            RatEnclosure.MaleColony, "Male Cage", -5.35f, -0.65f, -5.95f, 8.15f,
            new Color(0.54f, 0.68f, 0.48f), new Color(0.24f, 0.43f, 0.30f));
        private static readonly Definition FemaleDefinition = new Definition(
            RatEnclosure.FemaleColony, "Female Cage", 0.65f, 5.35f, -5.95f, 8.15f,
            new Color(0.58f, 0.68f, 0.51f), new Color(0.30f, 0.42f, 0.28f));
        private static readonly Definition NurseryDefinition = new Definition(
            RatEnclosure.Nursery, "Nursery", -5.35f, -0.65f, -13.85f, -7.45f,
            new Color(0.72f, 0.62f, 0.48f), new Color(0.45f, 0.29f, 0.20f));
        private static readonly Definition BreedingDefinition = new Definition(
            RatEnclosure.Breeding, "Breeding", 0.65f, 5.35f, -13.85f, -7.45f,
            new Color(0.50f, 0.58f, 0.72f), new Color(0.25f, 0.32f, 0.52f));
        // Pairing is a complete second habitat placed beside the existing
        // 2x2 layout. It is deliberately outside the normal overview frame,
        // but it uses the same footprint so rats remain fully simulated.
        private static readonly Definition PairingDefinition = new Definition(
            RatEnclosure.Pairing, "Pairing Habitat", 12.00f, 22.70f, -13.85f, 8.15f,
            new Color(0.48f, 0.55f, 0.42f), new Color(0.23f, 0.34f, 0.25f));

        private static string activeBreedingMotherId;
        private static string activeBreedingFatherId;

        public static readonly RatEnclosure[] AllEnclosures =
        {
            RatEnclosure.MaleColony,
            RatEnclosure.FemaleColony,
            RatEnclosure.Nursery,
            RatEnclosure.Breeding,
            RatEnclosure.Pairing,
        };

        public sealed class Definition
        {
            public readonly RatEnclosure enclosure;
            public readonly string label;
            public readonly float minX;
            public readonly float maxX;
            public readonly float minZ;
            public readonly float maxZ;
            public readonly Color floorColor;
            public readonly Color barrierColor;

            public Definition(RatEnclosure enclosure, string label, float minX, float maxX,
                float minZ, float maxZ, Color floorColor, Color barrierColor)
            {
                this.enclosure = enclosure;
                this.label = label;
                this.minX = minX;
                this.maxX = maxX;
                this.minZ = minZ;
                this.maxZ = maxZ;
                this.floorColor = floorColor;
                this.barrierColor = barrierColor;
            }

            public float Width { get { return maxX - minX; } }
            public float Depth { get { return maxZ - minZ; } }
            public Vector3 Center { get { return new Vector3((minX + maxX) * 0.5f, 0.45f, (minZ + maxZ) * 0.5f); } }
        }

        public static Definition GetDefinition(RatEnclosure enclosure)
        {
            switch (enclosure)
            {
                case RatEnclosure.MaleColony:
                    return MaleDefinition;
                case RatEnclosure.Nursery:
                    return NurseryDefinition;
                case RatEnclosure.Breeding:
                    return BreedingDefinition;
                case RatEnclosure.Pairing:
                    return PairingDefinition;
                default:
                    return FemaleDefinition;
            }
        }

        public static string Label(RatEnclosure enclosure)
        {
            return GetDefinition(enclosure).label;
        }

        public static Vector3 NurseryNestPosition
        {
            get { return new Vector3(-3.0f, NurseryNestY, -10.55f); }
        }

        public static Vector3 PairingNestPosition
        {
            get { return new Vector3(17.35f, NurseryNestY, -10.55f); }
        }

        public static bool HasNest(RatEnclosure enclosure)
        {
            return enclosure == RatEnclosure.Nursery || enclosure == RatEnclosure.Pairing;
        }

        public static Vector3 GetNestPosition(RatEnclosure enclosure)
        {
            return enclosure == RatEnclosure.Pairing ? PairingNestPosition : NurseryNestPosition;
        }

        public static bool IsInsideAdultNestExclusion(RatEnclosure enclosure, Vector3 position)
        {
            if (!HasNest(enclosure)) return false;
            Vector3 nest = GetNestPosition(enclosure);
            float radiusX = enclosure == RatEnclosure.Pairing
                ? PairingNestAdultExclusionRadiusX : NestAdultExclusionRadiusX;
            float radiusZ = enclosure == RatEnclosure.Pairing
                ? PairingNestAdultExclusionRadiusZ : NestAdultExclusionRadiusZ;
            return Mathf.Abs(position.x - nest.x) < radiusX &&
                Mathf.Abs(position.z - nest.z) < radiusZ;
        }

        public static void SetBreedingPair(string motherId, string fatherId)
        {
            activeBreedingMotherId = motherId;
            activeBreedingFatherId = fatherId;
        }

        public static void ClearBreedingPair()
        {
            activeBreedingMotherId = null;
            activeBreedingFatherId = null;
        }

        public static bool IsActiveBreedingParticipant(RatData rat)
        {
            if (rat == null || string.IsNullOrEmpty(rat.id)) return false;
            return rat.id == activeBreedingMotherId || rat.id == activeBreedingFatherId;
        }

        public static bool IsActiveBreedingParticipant(ColonySaveData save, RatData rat)
        {
            return IsActiveBreedingParticipant(rat) ||
                (save != null && rat != null && BreedingSystem.FindActiveDedicatedSession(save, rat.id) != null);
        }

        /// <summary>
        /// Derives all assignments from current stage, sex, pending pregnancy,
        /// and dependent Pinkie records. The enum and nursing flag are then
        /// persisted on RatData so save/load retains the resolved state.
        /// </summary>
        public static bool RecalculateAssignments(ColonySaveData save)
        {
            if (save == null) return false;
            save.EnsureLists();
            bool changed = false;
            foreach (var rat in save.rats)
            {
                if (rat == null) continue;

                bool hasDependentPinkies = rat.sex == RatSex.Female && HasDependentPinkies(save, rat.id);
                bool nursingState = rat.sex == RatSex.Female &&
                    (hasDependentPinkies || rat.reproductiveState == ReproductiveState.Nursing);
                RatEnclosure desired = DesiredEnclosure(save, rat, hasDependentPinkies);
                if (rat.nursing != nursingState)
                {
                    rat.nursing = nursingState;
                    changed = true;
                }
                if (rat.enclosure != desired)
                {
                    rat.enclosure = desired;
                    changed = true;
                }
            }
            return changed;
        }

        public static bool HasDependentPinkies(ColonySaveData save, string motherId)
        {
            if (save == null || string.IsNullOrEmpty(motherId)) return false;
            save.EnsureLists();
            foreach (var rat in save.rats)
            {
                if (rat == null || rat.stage != RatStage.Pinkie) continue;
                if (rat.motherId == motherId) return true;

                // Older saves may have retained the litter relationship but
                // not copied motherId onto every pup. Use that relationship as
                // a compatibility fallback, while still checking the live
                // Pinkie record so deleted/removed pups cannot keep a mother
                // in the Nursery indefinitely.
                if (string.IsNullOrEmpty(rat.litterId)) continue;
                foreach (var litter in save.litters)
                {
                    if (litter != null && litter.id == rat.litterId && litter.motherId == motherId) return true;
                }
            }
            return false;
        }

        public static bool IsPregnant(ColonySaveData save, RatData rat)
        {
            if (save == null || rat == null || rat.sex != RatSex.Female) return false;
            PregnancyData pending = BreedingSystem.FindPendingPregnancy(save, rat.id);
            if (pending != null && pending.motherId == rat.id) return true;
            if (string.IsNullOrEmpty(rat.pregnancyId)) return false;
            foreach (var pregnancy in save.pregnancies)
            {
                if (pregnancy != null && pregnancy.id == rat.pregnancyId &&
                    pregnancy.status == "pending" && pregnancy.motherId == rat.id) return true;
            }
            return false;
        }

        public static RatEnclosure DesiredEnclosure(ColonySaveData save, RatData rat, bool hasDependentPinkies)
        {
            if (IsActiveBreedingParticipant(save, rat)) return RatEnclosure.Breeding;
            // Pairing is a player-assigned habitat. Pregnancy, birth, nursing,
            // and growth must not pull its residents into the normal Nursery
            // or colony zones; only an explicit remove action can do that.
            if (rat != null && (rat.pairingHabitatAssigned || rat.enclosure == RatEnclosure.Pairing)) return RatEnclosure.Pairing;
            if (rat == null || rat.stage == RatStage.Pinkie) return RatEnclosure.Nursery;
            if (rat.sex == RatSex.Male) return RatEnclosure.MaleColony;
            if (hasDependentPinkies || rat.reproductiveState == ReproductiveState.Nursing || IsPregnant(save, rat))
                return RatEnclosure.Nursery;
            return RatEnclosure.FemaleColony;
        }

        public static RatEnclosure StandardEnclosure(ColonySaveData save, RatData rat)
        {
            if (rat == null || rat.stage == RatStage.Pinkie) return RatEnclosure.Nursery;
            if (rat.sex == RatSex.Male) return RatEnclosure.MaleColony;
            bool dependentPinkies = rat.sex == RatSex.Female && HasDependentPinkies(save, rat.id);
            return dependentPinkies || rat.reproductiveState == ReproductiveState.Nursing || IsPregnant(save, rat)
                ? RatEnclosure.Nursery
                : RatEnclosure.FemaleColony;
        }

        public static bool IsInside(RatEnclosure enclosure, Vector3 position, float margin)
        {
            Definition definition = GetDefinition(enclosure);
            return position.x >= definition.minX + margin && position.x <= definition.maxX - margin &&
                position.z >= definition.minZ + margin && position.z <= definition.maxZ - margin;
        }

        public static Vector3 ClampToEnclosure(RatEnclosure enclosure, Vector3 position, float margin = MinimumMovementMargin)
        {
            return ClampToEnclosureInternal(enclosure, position, margin, false);
        }

        /// <summary>
        /// Clamps only to the cage walls. Ambient movement uses this variant
        /// after it has selected a nest-safe waypoint, so touching the nest
        /// never projects a rat back to a distant spawn position.
        /// </summary>
        public static Vector3 ClampToEnclosureBounds(RatEnclosure enclosure, Vector3 position,
            float margin = MinimumMovementMargin)
        {
            return ClampToEnclosureInternal(enclosure, position, margin, true);
        }

        /// <summary>
        /// Pinkies are the only rats allowed inside the nest footprint. All
        /// other placement and movement paths use ClampToEnclosure(), while
        /// newborn placement explicitly opts into this bounds-only variant.
        /// </summary>
        public static Vector3 ClampToEnclosureAllowNest(RatEnclosure enclosure, Vector3 position, float margin = MinimumMovementMargin)
        {
            return ClampToEnclosureInternal(enclosure, position, margin, true);
        }

        private static Vector3 ClampToEnclosureInternal(RatEnclosure enclosure, Vector3 position, float margin, bool allowNest)
        {
            Definition definition = GetDefinition(enclosure);
            float safeMargin = Mathf.Clamp(margin, 0f, Mathf.Min(definition.Width, definition.Depth) * 0.45f);
            position.x = Mathf.Clamp(position.x, definition.minX + safeMargin, definition.maxX - safeMargin);
            position.z = Mathf.Clamp(position.z, definition.minZ + safeMargin, definition.maxZ - safeMargin);

            if (!allowNest && IsInsideAdultNestExclusion(enclosure, position))
            {
                Vector3 nest = GetNestPosition(enclosure);
                Vector2 offset = new Vector2(position.x - nest.x, position.z - nest.z);
                if (offset.sqrMagnitude < 0.0001f) offset = Vector2.right;
                float radiusX = enclosure == RatEnclosure.Pairing
                    ? PairingNestAdultExclusionRadiusX : NestAdultExclusionRadiusX;
                float radiusZ = enclosure == RatEnclosure.Pairing
                    ? PairingNestAdultExclusionRadiusZ : NestAdultExclusionRadiusZ;
                float distanceToXEdge = radiusX - Mathf.Abs(offset.x);
                float distanceToZEdge = radiusZ - Mathf.Abs(offset.y);
                if (distanceToXEdge <= distanceToZEdge)
                {
                    position.x = nest.x + Mathf.Sign(offset.x) * radiusX;
                }
                else
                {
                    position.z = nest.z + Mathf.Sign(offset.y) * radiusZ;
                }
                // Keep the projected point inside the cage if a future nest
                // is moved close to a wall.
                position.x = Mathf.Clamp(position.x, definition.minX + safeMargin, definition.maxX - safeMargin);
                position.z = Mathf.Clamp(position.z, definition.minZ + safeMargin, definition.maxZ - safeMargin);
            }

            return position;
        }

        public static Vector3 GetNestSidePosition(RatEnclosure enclosure)
        {
            Vector3 nest = GetNestPosition(enclosure);
            float radiusX = enclosure == RatEnclosure.Pairing
                ? PairingNestAdultExclusionRadiusX : NestAdultExclusionRadiusX;
            return ClampToEnclosure(enclosure,
                nest + Vector3.right * (radiusX + 0.08f), 0.48f);
        }

        public static Vector3 GetSpawnPosition(RatEnclosure enclosure, int slot)
        {
            Definition definition = GetDefinition(enclosure);
            int column = slot % 2;
            int row = slot / 2;
            float x = definition.minX + 0.78f + column * Mathf.Max(0.95f, definition.Width - 1.56f);
            float z = definition.minZ + 1.15f + row * 1.45f;
            if (z > definition.maxZ - 1.0f)
            {
                z = definition.minZ + 1.15f + (row % 5) * 1.25f;
                x = definition.minX + 0.78f + ((row / 5) % 2) * Mathf.Max(0.95f, definition.Width - 1.56f);
            }
            float rootY = 0.45f + (enclosure == RatEnclosure.Pairing ? GameConfig.PairingAdultRootVerticalLift : 0f);
            return ClampToEnclosure(enclosure, new Vector3(x, rootY, z));
        }

        public static Vector3 GetPinkiePosition(int slot, Vector3 fallbackNestPosition)
        {
            return GetPinkiePosition(RatEnclosure.Nursery, null, slot, fallbackNestPosition);
        }

        public static Vector3 GetPinkiePosition(string ratId, int slot, Vector3 fallbackNestPosition)
        {
            return GetPinkiePosition(RatEnclosure.Nursery, ratId, slot, fallbackNestPosition);
        }

        public static Vector3 GetPinkiePosition(RatEnclosure enclosure, string ratId, int slot, Vector3 fallbackNestPosition)
        {
            return GetPinkiePosition(enclosure, ratId, null, slot, fallbackNestPosition);
        }

        /// <summary>
        /// Returns a compact, irregular nest cluster position. The litter seed
        /// keeps siblings together while the rat ID gives each pinkie its own
        /// stable angle and radius. The returned Y is only a pre-visualization
        /// placeholder; RatPresenter resolves the final surface height from
        /// the actual nest renderer and pinkie bounds after the model exists.
        /// </summary>
        public static Vector3 GetPinkiePosition(RatEnclosure enclosure, string ratId, string litterSeed,
            int slot, Vector3 fallbackNestPosition)
        {
            Vector3 nest = enclosure == RatEnclosure.Pairing
                ? PairingNestPosition
                : (fallbackNestPosition == Vector3.zero ? NurseryNestPosition : fallbackNestPosition);
            string litterKey = string.IsNullOrEmpty(litterSeed)
                ? (string.IsNullOrEmpty(ratId) ? "unassigned-litter" : ratId)
                : litterSeed;
            string seedKey = litterKey + "|pinkie|" + (ratId ?? string.Empty) + "|" + Mathf.Max(0, slot);

            // A random polar scatter is deliberately used instead of cells,
            // columns, or rows. The shared litter center makes a birth read as
            // one compact litter while each seed produces a different pose.
            float angle = StableUnit(seedKey + "|angle") * Mathf.PI * 2f;
            float radius = 0.14f + StableUnit(seedKey + "|radius") *
                (enclosure == RatEnclosure.Pairing ? 0.52f : 0.34f);
            float centerX = (StableUnit(litterKey + "|center-x") - 0.5f) *
                (enclosure == RatEnclosure.Pairing ? 0.30f : 0.24f);
            float centerZ = (StableUnit(litterKey + "|center-z") - 0.5f) *
                (enclosure == RatEnclosure.Pairing ? 0.22f : 0.18f);
            Vector3 position = nest + new Vector3(
                centerX + Mathf.Cos(angle) * radius * 0.96f,
                nest.y,
                centerZ + Mathf.Sin(angle) * radius * 0.70f);
            return ClampToEnclosureAllowNest(enclosure, position, 0.42f);
        }

        private static int StableNestSeed(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                int hash = 23;
                for (int index = 0; index < value.Length; index++)
                {
                    hash = hash * 31 + value[index];
                }
                return hash & 0x7fffffff;
            }
        }

        private static float StableUnit(string value)
        {
            int seed = StableNestSeed(value);
            return (seed % 100000) / 99999f;
        }

        /// <summary>
        /// Fallback targets are zone-local so a zone with no saved prop still
        /// has meaningful movement points. Their ids are stable and are not
        /// saved as gameplay objects.
        /// </summary>
        public static List<RatBehaviorTarget> GetZoneTargets(RatEnclosure enclosure)
        {
            var targets = new List<RatBehaviorTarget>();
            switch (enclosure)
            {
                case RatEnclosure.MaleColony:
                    AddTarget(targets, "male_food_spot", "Food corner", RatBehaviorTargetKind.Food, -3.75f, -3.2f);
                    AddTarget(targets, "male_hide_spot", "Male hide", RatBehaviorTargetKind.Hide, -2.55f, 3.15f);
                    AddTarget(targets, "male_explore_spot", "Male explore point", RatBehaviorTargetKind.Toy, -4.35f, 7.35f);
                    break;
                case RatEnclosure.FemaleColony:
                    AddTarget(targets, "female_explore_front", "Female explore point", RatBehaviorTargetKind.Toy, 1.35f, 2.05f);
                    AddTarget(targets, "female_rest_spot", "Female rest point", RatBehaviorTargetKind.Hide, 2.15f, 4.15f);
                    AddTarget(targets, "female_explore_back", "Female explore point", RatBehaviorTargetKind.Tunnel, 1.55f, 6.55f);
                    break;
                case RatEnclosure.Nursery:
                    AddTarget(targets, "nursery_water_spot", "Nursery water point", RatBehaviorTargetKind.Water, -4.35f, -12.75f);
                    AddTarget(targets, "nursery_explore_spot", "Nursery explore point", RatBehaviorTargetKind.Toy, -1.55f, -8.25f);
                    break;
                case RatEnclosure.Breeding:
                    AddTarget(targets, "breeding_meet_spot", "Breeding meet point", RatBehaviorTargetKind.Toy, 3.0f, -10.55f);
                    AddTarget(targets, "breeding_rest_spot", "Breeding rest point", RatBehaviorTargetKind.Hide, 1.55f, -8.35f);
                    AddTarget(targets, "breeding_explore_spot", "Breeding explore point", RatBehaviorTargetKind.Toy, 4.45f, -12.75f);
                    break;
                case RatEnclosure.Pairing:
                    AddTarget(targets, "pairing_meet_spot", "Pairing meet point", RatBehaviorTargetKind.Toy, 14.65f, -1.15f);
                    AddTarget(targets, "pairing_explore_spot", "Pairing explore point", RatBehaviorTargetKind.Toy, 20.25f, 5.85f);
                    AddTarget(targets, "pairing_rest_spot", "Pairing rest spot", RatBehaviorTargetKind.Hide, 20.35f, -8.25f);
                    break;
            }
            return targets;
        }

        public static bool IsBehaviorPointAllowed(RatEnclosure enclosure, Vector3 position)
        {
            if (!IsInside(enclosure, position, 0f)) return false;
            // Behavior targets are shared by adult and young rats. Keep the
            // enlarged nest's full safety footprint out of that target list;
            // pinkies do not wander and are placed directly by RatPresenter.
            return !IsInsideAdultNestExclusion(enclosure, position);
        }

        private static void AddTarget(List<RatBehaviorTarget> targets, string id, string label,
            RatBehaviorTargetKind kind, float x, float z)
        {
            targets.Add(new RatBehaviorTarget
            {
                id = id,
                label = label,
                kind = kind,
                position = new Vector3(x, 0.45f, z),
            });
        }
    }
}
