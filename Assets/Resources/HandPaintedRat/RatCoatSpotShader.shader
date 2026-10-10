Shader "Rat Habitat/Hand Painted Rat Coat"
{
    Properties
    {
        _MainTex ("Hand-Painted Coat", 2D) = "white" {}
        _FeatureSourceTex ("Feature Detail Source", 2D) = "white" {}
        _Color ("Coat Color", Color) = (1, 1, 1, 1)
        _AccentColor ("Coat Accent", Color) = (1, 1, 1, 1)
        _HotspotNoise ("Shared Rest Pose Noise", 3D) = "white" {}
        _HotspotNoiseOffset ("Stable Hotspot Seed / Coverage", Vector) = (0,0,0,0.45)
        _HairlessMode ("Hairless Skin", Range(0,1)) = 0
        _SkinColor ("Natural Pigmented Skin", Color) = (0.68,0.48,0.43,1)
        _FeatureMask ("Stable Skinned Feature Mask", 2D) = "black" {}
        _EyeMask ("Precise Eye UV Mask", 2D) = "black" {}
        _SpotColor ("Spot Color", Color) = (1, 1, 1, 1)
        _SecondarySpotColor ("Secondary Inherited Marking Color", Color) = (1, 1, 1, 1)
        _SecondarySpotStrength ("Secondary Marking Strength", Range(0, 1)) = 0
        _SecondaryMarkingFamily ("Secondary Marking Family", Float) = 0
        _SpotSeed ("Spot Seed", Float) = 0
        _SpotStrength ("Spot Strength", Range(0, 1)) = 0
        _AlbinoMode ("Albino Neutralization", Range(0, 1)) = 0
        _AlbinoBodyColor ("Albino Body Color", Color) = (0.98, 0.965, 0.92, 1)
        _PinkEyeMode ("Pink Eye Phenotype", Range(0, 1)) = 0
        _EyeColor ("Eye Color", Color) = (0.015, 0.012, 0.012, 1)
        _MarkingFamily ("Marking Family", Float) = 0
        _SpeckleSettings ("Stable Marking Speckles", Vector) = (0, 0, 0, 0)
        _SpeckleColor ("Marking Speckle Color", Color) = (0.35, 0.30, 0.28, 1)
        _SpeckleSeed ("Marking Speckle Seed", Float) = 0
        _RatModelBoundsMin ("Rat Model Bounds Min", Vector) = (0, 0, 0, 0)
        _RatModelBoundsSize ("Rat Model Bounds Size", Vector) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _FeatureSourceTex;
        sampler3D _HotspotNoise;
        float4 _HotspotCenters[12];
        float4 _HotspotRadii[12];
        float4 _HotspotNoiseOffset;
        float _HairlessMode;
        fixed4 _SkinColor;
        sampler2D _FeatureMask;
        sampler2D _EyeMask;
        fixed4 _Color;
        fixed4 _AccentColor;
        fixed4 _SpotColor;
        fixed4 _SecondarySpotColor;
        float _SecondarySpotStrength;
        float _SecondaryMarkingFamily;
        float _SpotSeed;
        float _SpotStrength;
        float _AlbinoMode;
        fixed4 _AlbinoBodyColor;
        float _PinkEyeMode;
        fixed4 _EyeColor;
        float _MarkingFamily;
        float4 _SpeckleSettings;
        fixed4 _SpeckleColor;
        float _SpeckleSeed;
        float4 _RatModelBoundsMin;
        float4 _RatModelBoundsSize;


        float StableSpeckleHash(float2 value)
        {
            value = frac(value * float2(0.1031, 0.1030));
            value += dot(value, value.yx + 33.33);
            return frac((value.x + value.y) * value.x);
        }

        struct Input
        {
            float2 uv_MainTex;
            // Pack the only feature channel used by the surface into rest.w.
            // Separate float3 + float4 varyings exceed SM3's interpolator
            // limit in the generated Standard ForwardBase lighting pass.
            float4 ratRest;
        };

        void vert(inout appdata_full vertex, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input,data);
            // Static per-vertex attributes, NOT the animated object position.
            data.ratRest = float4(vertex.texcoord2.xyz, vertex.texcoord3.w);
        }

        float RestNoise(float3 samplePosition)
        {
            return tex3D(_HotspotNoise,samplePosition / 32.0).r;
        }

        float OrganicCoverage(float3 rest)
        {
            float3 seed = _HotspotNoiseOffset.xyz;
            float3 warp = float3(RestNoise(rest*7.0+seed),
                RestNoise(rest*7.0+seed+11.1),RestNoise(rest*7.0+seed+23.7))-.5;
            float3 warpedRest = rest+warp*.15;
            float broad = RestNoise(rest*13.0+seed+41.7)-.5;
            float detail = RestNoise(rest*73.0+seed+7.4)-.5;
            float field = 0.0;
            [unroll] for (int index=0;index<12;index++)
            {
                float3 offset = (warpedRest-_HotspotCenters[index].xyz)*_HotspotRadii[index].xyz;
                float probability = exp2(-dot(offset,offset)*1.6)*_HotspotCenters[index].w;
                // Smoothly merging concentrations prevents repeated circles.
                field = max(field,probability)+min(field,probability)*.12;
            }
            field += broad*.32+detail*.08;
            float threshold = _HotspotNoiseOffset.w;
            // Narrow irregular fur edge: no UV seam threshold or broad halo.
            float feather = max(.024,fwidth(field)*.75);
            return smoothstep(threshold-feather,threshold+feather,field);
        }

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            // The main albedo texture is deliberately neutral for albinos.
            // Keep the original imported map on a separate feature-only
            // sampler so eyes and other authored details remain available
            // without allowing pink/beige fur pixels into albino albedo.
            fixed4 source = tex2D(_FeatureSourceTex, input.uv_MainTex);
            float sourceLuminance = saturate(dot(source.rgb, float3(0.299, 0.587, 0.114)));
            // A very low-amplitude, UV-stable variation breaks up flat
            // plastic-looking blocks without introducing animated noise or
            // changing the saved phenotype. The per-rat seed keeps two rats
            // with the same coat family from looking like exact clones.
            float furNoise = sin(dot(input.uv_MainTex, float2(83.17, 47.31)) + _SpotSeed * 6.2831853);
            furNoise = 0.975 + 0.05 * (furNoise * 0.5 + 0.5);
            // The recorded phenotype is the color authority. The imported
            // hand-painted map contains broad light/dark body shapes from
            // the source asset; using its luminance here makes those shapes
            // look like hard coat markings and overwhelms the generated
            // phenotype. Keep only a restrained, centered shading signal so
            // the coat stays softly fur-like without inheriting those
            // geometric source regions.
            float sourceShading = (sourceLuminance - 0.5) * 0.04;
            float furValue = (0.96 + sourceShading) * furNoise;
            fixed3 fur = _Color.rgb * furValue;
            float accentWave = sin(dot(input.uv_MainTex, float2(17.13, 31.71)) + _SpotSeed * 3.17);
            float accentAmount = saturate(0.035 + (accentWave * 0.5 + 0.5) * 0.05);
            fur = lerp(fur, _AccentColor.rgb * (0.95 + sourceLuminance * 0.10), accentAmount);

            // Keep a small amount of the hand-painted source around feature
            // boundaries. This preserves natural ear/nose/tail shading on
            // the single-mesh import without allowing its old coat hue to
            // override the recorded phenotype.
            fixed4 painted = fixed4(lerp(fur, source.rgb, 0.025), source.a);
            // The imported rat currently carries some eyes, mouth edges,
            // whisker roots, and tail segmentation in the same UV texture as
            // the fur. Preserve a restrained amount of those dark source
            // pixels for ordinary coats too; otherwise a strongly tinted
            // phenotype can wash the features out. The later albino branch
            // uses the same signal with a stronger, neutralized treatment.
            float visibleFeatureSignal = 1.0 - smoothstep(0.08, 0.28, sourceLuminance);
            painted.rgb = lerp(painted.rgb, source.rgb, visibleFeatureSignal * 0.34);
            // Feature masks are generated from the imported mesh bones. Use
            // them to distinguish genuine skin/detail islands from the broad
            // beige source-fur map before the albino branch preserves pink
            // accents.
            float4 featureMaskForSkin = tex2D(_FeatureMask, input.uv_MainTex);
            float skinFeatureRegion = saturate(max(featureMaskForSkin.r,
                max(featureMaskForSkin.g, featureMaskForSkin.b)));
            float ownedSkinRegion = smoothstep(0.34, 0.92, skinFeatureRegion);
            // Keep the source luminance as the detail signal. Using the
            // phenotype-tinted sample here would make a dark coat turn every
            // dark fur pixel into a false eye/mouth feature on albinos.
            float paintedLuminance = sourceLuminance;
            // Albino remains recognizably hand-painted through luminance
            // variation, but the source beige/brown hue is removed. The
            // original map is intentionally retained: on the imported rat it
            // contains the face and tail details as pixels on the same mesh.
            fixed3 albinoFur = _AlbinoBodyColor.rgb * (0.82 + saturate(paintedLuminance) * 0.21);

            // Source pixels for eyes, mouth, nose edges, whisker roots, and
            // tail segmentation can be mid-dark rather than nearly black.
            // Keep that detail visible after neutralizing the fur, while
            // leaving ordinary fur shadows white. The wider, soft threshold
            // is important because these details are baked into the same
            // skinned mesh texture on the imported rat.
            float darkFeatureSignal = (1.0 - smoothstep(0.12, 0.42, paintedLuminance)) *
                ownedSkinRegion;
            fixed3 darkFeature = fixed3(0.07, 0.055, 0.06) *
                (0.82 + saturate(paintedLuminance) * 0.45);
            // Albino fur must not inherit pink/red source pixels on the face
            // or legs. Eye color is restored separately below from
            // _EyeColor, so removing this skin-color blend does not change
            // the existing albino eye appearance.
            fixed3 featureColor = darkFeature;
            fixed3 albinoPainted = lerp(albinoFur, featureColor, darkFeatureSignal * 0.94);

            // Do not preserve source pink/red color on albino fur. The
            // explicit eye pass below remains the only albino fur-shader
            // color exception, keeping the recorded pink/red eye color.
            painted.rgb = lerp(painted.rgb, albinoPainted, _AlbinoMode);
            float whiteBlend = 0.0;
            if (_SpotStrength > .001)
                whiteBlend = OrganicCoverage(input.ratRest.xyz)*_SpotStrength;
            // Exposed ears/paws and painted eye/nose/mouth detail remain readable.
            // Ownership suppresses skin detail, never adds a white joint seam.
            whiteBlend *= 1.0-smoothstep(.35,.95,input.ratRest.w);
            whiteBlend *= 1.0-darkFeatureSignal*.92;


            // Mature tails are a separate verified submesh/material. Keeping
            // them out of this coat shader prevents body masks, markings, or
            // fallback textures from recoloring the tail or the rump.
            // Split only the existing stable marking coverage between the two
            // inherited parental colors. UV-anchored cells keep the pattern
            // fixed on the skinned surface with no decals or extra renderers.
            float secondaryFrequency = lerp(4.0, 9.0,
                saturate(_SecondaryMarkingFamily / 20.0));
            float secondaryBlend = 0.0;
            if (_SecondarySpotStrength > 0.001 && whiteBlend > 0.005)
            {
                float secondaryHash = RestNoise(input.ratRest.xyz*secondaryFrequency*2.0+
                    _HotspotNoiseOffset.xyz+17.3);
                float secondaryRegion = smoothstep(0.46, 0.54, secondaryHash) *
                    saturate(_SecondarySpotStrength);
                secondaryBlend = whiteBlend * secondaryRegion;
            }
            float primaryBlend = whiteBlend - secondaryBlend;
            fixed3 coatWithMarkings = lerp(painted.rgb, _SpotColor.rgb, primaryBlend);
            coatWithMarkings = lerp(coatWithMarkings, _SecondarySpotColor.rgb, secondaryBlend);

            // Speckles are a stable, shader-only overlay shared by the
            // existing body/face/belly/leg marking coverage. A uniform gate
            // skips the procedural work for most rats, and all coordinates
            // are UV-anchored so markings cannot flicker as the rat moves.
            if (_SpeckleSettings.x > 0.5 && whiteBlend > 0.005)
            {
                float speckleFrequency = _SpeckleSettings.z;
                float2 speckleGrid = (input.ratRest.xz+input.ratRest.y*float2(.31,.67)) *
                    float2(speckleFrequency, speckleFrequency * 0.72);
                float2 speckleCell = floor(speckleGrid);
                float2 speckleLocal = frac(speckleGrid);
                float2 speckleSeedOffset = float2(_SpeckleSeed * 31.7,
                    _SpeckleSeed * 47.9);
                float cellChance = StableSpeckleHash(speckleCell + speckleSeedOffset);
                if (cellChance < _SpeckleSettings.y)
                {
                    float randomX = StableSpeckleHash(speckleCell + speckleSeedOffset +
                        float2(19.1, 7.7));
                    float randomY = StableSpeckleHash(speckleCell + speckleSeedOffset +
                        float2(3.7, 29.3));
                    float shapeVariation = StableSpeckleHash(speckleCell + speckleSeedOffset +
                        float2(41.3, 13.9));
                    float tintVariation = StableSpeckleHash(speckleCell + speckleSeedOffset +
                        float2(11.7, 53.1));
                    float2 speckleCenter = lerp(float2(0.18, 0.18),
                        float2(0.82, 0.82), float2(randomX, randomY));
                    float radius = lerp(0.18, 0.31, shapeVariation) *
                        _SpeckleSettings.w;
                    float aspect = lerp(0.68, 1.48, tintVariation);
                    float speckleDistance = length((speckleLocal - speckleCenter) /
                        float2(radius * aspect, radius));
                    float fleck = 1.0 - smoothstep(0.58, 1.02, speckleDistance);

                    // Derivative expansion keeps a few flecks along the
                    // feathered edge of a mark without painting the rest of
                    // the coat. It also lets face and limb marks share the
                    // same behavior despite their different UV sizes.
                    float expandedMarking = saturate(whiteBlend +
                        max(fwidth(whiteBlend), 0.008) * 1.35);
                    float markingCoverage = smoothstep(0.025, 0.34, expandedMarking);
                    float fleckOpacity = fleck * markingCoverage * 0.68;
                    fixed3 fleckColor = lerp(_SpeckleColor.rgb, _AccentColor.rgb,
                        0.05 + tintVariation * 0.18);
                    fleckColor *= 0.92 + shapeVariation * 0.14;
                    coatWithMarkings = lerp(coatWithMarkings, fleckColor, fleckOpacity);
                }
            }

            // The source texture contains eye detail, but its hue can be
            // recolored when the whole imported mesh is tinted. Restore a
            // small, localized eye signal last so ordinary rats always have
            // black eyes and albino/pink-eye phenotypes retain pink/red eyes.
            // IMPORTANT: featureMask.r is deliberately not used here. Its
            // head/ear ownership is broad and would recolor an entire face
            // when multiplied by _EyeColor. _EyeMask is a separate, stable
            // UV mask containing only the two actual eye islands; the source
            // luminance gate rejects surrounding fur and ear pixels.
            float eyeMask = tex2D(_EyeMask, input.uv_MainTex).r;
            float eyePixelSignal = 1.0 - smoothstep(0.06, 0.22, sourceLuminance);
            float eyeRegion = saturate(eyeMask * eyePixelSignal);
            // Drive albino's final visible body color here, after markings
            // and tail sampling. This prevents any imported pink/beige map,
            // spot color, or broad feature overlay from becoming albino fur.
            // The eye pass remains the only red/pink exception on an albino.
            fixed3 albinoFinal = albinoFur;
            fixed3 finalAlbedo = _AlbinoMode > 0.5
                ? lerp(albinoFinal, _EyeColor.rgb, eyeRegion)
                : lerp(coatWithMarkings, _EyeColor.rgb, eyeRegion);
            if (_HairlessMode > .5)
            {
                float pores = RestNoise(input.ratRest.xyz*125.0+_HotspotNoiseOffset.xyz);
                float folds = sin(input.ratRest.y*78.0+
                    RestNoise(input.ratRest.xyz*18.0)*5.0)*.012;
                fixed3 skin = _SkinColor.rgb*(.94+pores*.08+folds);
                // Inherited markings express as subtler skin pigmentation.
                skin = lerp(skin,_SpotColor.rgb,primaryBlend*.48);
                skin = lerp(skin,_SecondarySpotColor.rgb,secondaryBlend*.48);
                skin = lerp(skin,source.rgb,visibleFeatureSignal*.65);
                finalAlbedo = lerp(skin,_EyeColor.rgb,eyeRegion);
            }
            output.Albedo = finalAlbedo;
            output.Metallic = 0.0;
            output.Smoothness = lerp(0.08,0.27,_HairlessMode);
            output.Alpha = painted.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
