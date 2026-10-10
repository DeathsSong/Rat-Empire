# Organic markings and hairless inheritance

Marking families and the existing B/C/D/S coat genetics remain saved unchanged.
Families now bias anatomical probability fields instead of fixed UV polygons or
face templates. Twelve independent, warped concentrations cover head, shoulders,
back, chest, belly, rump, cheeks and four legs. Coverage, aspect, strength and
asymmetry come from stable rat ID + coat genotype + family, never name or pose.
Self/solid remains unmarked. Primary/secondary inherited colors and occasional
speckles retain their existing data and probabilities. Store markings remain 15%.

The shared imported mesh gets cached UV3 rest-position coordinates and UV4
feature ownership. Unlike UV0, these preserve separate left/right positions even
when texture islands are mirrored. Animation cannot move the pattern through the
surface. Marking boundaries are narrow noise-distorted contours, not feature-mask
threshold outlines. Exposed ears/paws and painted eye/nose/mouth detail are protected.

One shared 32-cubed noise texture (128 KiB), one cached rest-coordinate mesh per
source mesh, and cached 12-hotspot parameter blocks are used. No per-rat texture,
extra renderer or GameObject is generated. Repeated parameter lookup on an
unchanged rat uses a weak-reference cache without string/array allocation. Recycled
store rows release their descriptor cache. This does not claim GPU performance
has been measured in a 300-rat rendered scene.

## Hairless

`GameConfig.SpontaneousHairlessChance` is the independent mutation setting: 0.005.
It is a **0.5% visible hr/hr probability per pup from two non-carrier Hr/Hr
parents**, not an allele mutation probability. Such an event records Hr/Hr → hr/hr
in the pup's persisted mutation history. It does not change S/B/C/D mutation rates.

| Parents | Visible hairless offspring |
| --- | --- |
| Hr/Hr × Hr/Hr | 0.5% spontaneous |
| Hr/Hr × Hr/hr | 0%; half carriers |
| Hr/Hr × hr/hr | 0%; all carriers |
| Hr/hr × Hr/hr | 25% inherited |
| Hr/hr × hr/hr | 50% inherited |
| hr/hr × hr/hr | 100% inherited |

Carrier/expressing crosses use Mendelian inheritance without an extra mutation
roll. The optional `genotype.hairless` locus preserves the four existing coat-locus
seed keys; missing/invalid legacy alleles normalize to Hr/Hr. Clone and JSON saves
preserve the trait. Coat information includes Hairless, and breeding preview shows
the pair-specific visible probability. The normal young/adult model and stage
scale remain in use, with pigmented taupe/gray-pink skin, stable pores/subtle folds,
and subdued inherited skin markings. No pinkie model/material is substituted.

## Focused verification and comparison

Editor tests named `Hairless*`, `Hotspot*`, the existing marking-placement test,
and secondary-marking tests cover inheritance, de novo rate/history, old saves,
color persistence, seed stability, shared mesh/noise resources and age models.
Existing visual tests now assert hotspot parameters rather than removed template
parameters; their stability, variety, solid-coat and speckle contracts remain.

Open **Rat Empire → Diagnostics → Markings and Hairless Comparison**. This uses
an isolated preview scene, not Main, GameBootstrap, PlayerPrefs or colony saves.
It shows four Hooded seeds, two close-up Blaze faces, Berkshire legs and a forced
adult Hairless example. Its export button writes `Temp/MarkingHotspotsComparison.png`.
Batch entry point: `RatHabitat.Editor.RatMarkingComparisonWindow.CaptureBatch`
(requires graphics; do not use `-nographics`). Images are cached, not regenerated
per repaint.

Current verification: C# compile passed. Direct checks against the compiled
production genetics confirmed 50/10,000 stratified spontaneous expressions,
five Mendelian crosses, mutation history, legacy defaults and cloning. Direct
hotspot checks also passed stable rename/cache lookup, 300 distinct descriptors,
descriptor release, and Self remaining unmarked. Unity
focused execution exited 199 because LicenseClient-horse refused its IPC
connection. Unity JSON/graphics tests and the rendered comparison remain
unverified until that licensing connection works. No WebGL build or push.

### White-coat rendering regression (October 9)

The Editor log identified an HLSL syntax error: `point` is reserved and cannot
be used as the noise sample/local coordinate variable. After renaming it, a
native Unity shader compile exposed a second error: separate rest-position and
feature varyings needed 11 interpolators, exceeding the Standard ForwardBase
SM3 limit of 10. Packing the only consumed feature channel into rest-position W
fixes that limit without raising the shader target or changing saved genetics.

`HotspotCoatShaderCompilesAndRendersDistinctPhenotypeColors` now checks actual
shader pass compilation and GPU-rendered dark, blue, warm and albino colors,
instead of relying only on material property values or a C# compile.

The shared validation method passed in a separate minimal Unity 2022.3.40f1
project using D3D11, outside the restricted licensing sandbox. Rendered RGB:
dark (0.239, 0.251, 0.267), blue (0.239, 0.306, 0.588), warm
(0.565, 0.341, 0.267), albino (0.784, 0.784, 0.773). All shader passes compiled
without errors. Log: `Temp/CoatShaderValidation-fixed-20261009.log`.
The C# compile also passed with zero warnings/errors. This check did not run
Main/GameBootstrap or access colony saves. The full project graphics/save-load
tests and in-game/WebGL visual comparison have not been rerun. No WebGL build,
commit or push was performed.

### October 10 batch verification and WebGL handoff

Spontaneous S-locus marking mutation is now 0.005 per inherited allele:
`1 - (1 - 0.005)^2 = 0.9975%` per offspring of two solid s/s parents, before
albino masking. Normal marked-parent inheritance, 15% store markings, secondary
marking colors, hairless rarity and B/C/D mutation settings are unchanged.
The targeted native Unity genetics run passed all 14 cases; the seeded
solid-parent cohort produced 512 marked offspring in 50,000 (1.024%).

The latest native Unity favorite/Sale Tank checks passed 20/20, and all 24
new nest-corner routing cases passed. Two existing birth-queue integration
checks remain failing: `SimultaneousDueBirthsRetryFailedTransactionInPlaceBeforeAdvancingQueue`
does not reach its expected birth transaction, and
`TenSimultaneousDueBirthsSerializeDeliveryApproachAndKeepFamiliesInNest` does not
set the expected active approach flag. Both failures were also reproduced with
the earlier routing implementation. They are unresolved, not waived. The full
current suite has not been rerun.

The user's Unity WebGL build completed successfully on October 10 at 02:12 PDT.
The published loader, data, framework and WASM files are copied from that output;
the responsive published HTML/CSS is retained. The cache revision is
`rat-empire-webgl-20261010-2e5378ba`, using the WASM SHA-256 prefix.
This handoff checks compilation and artifact identity, not a new browser or
mobile gameplay smoke test. No colony save was loaded or changed.
