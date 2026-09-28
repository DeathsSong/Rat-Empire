Shader "Rat Habitat/Hand Painted Rat Coat"
{
    Properties
    {
        _MainTex ("Hand-Painted Coat", 2D) = "white" {}
        _Color ("Coat Color", Color) = (1, 1, 1, 1)
        _AccentColor ("Coat Accent", Color) = (1, 1, 1, 1)
        _SpotMask ("Body Spot UV Mask", 2D) = "black" {}
        _SpotPattern ("Organic Spot Pattern", 2D) = "black" {}
        _FeatureMask ("Stable Skinned Feature Mask", 2D) = "black" {}
        _SpotColor ("Spot Color", Color) = (1, 1, 1, 1)
        _SpotSeed ("Spot Seed", Float) = 0
        _SpotStrength ("Spot Strength", Range(0, 1)) = 0
        _AlbinoMode ("Albino Neutralization", Range(0, 1)) = 0
        _AlbinoBodyColor ("Albino Body Color", Color) = (0.98, 0.965, 0.92, 1)
        _PinkEyeMode ("Pink Eye Phenotype", Range(0, 1)) = 0
        _EyeColor ("Eye Color", Color) = (0.015, 0.012, 0.012, 1)
        _MatureTailColor ("Mature Tail Skin", Color) = (0.56, 0.39, 0.38, 1)
        _MatureTailStrength ("Mature Tail Strength", Range(0, 1)) = 1
        _MarkingFamily ("Marking Family", Float) = 0
        _FaceMarkingStrength ("Face Marking Strength", Range(0, 1)) = 0
        _LegMarkingStrength ("Leg Marking Strength", Range(0, 1)) = 0
        _BellyMarkingStrength ("Belly Marking Strength", Range(0, 1)) = 0
        _RatModelBoundsMin ("Rat Model Bounds Min", Vector) = (0, 0, 0, 0)
        _RatModelBoundsSize ("Rat Model Bounds Size", Vector) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _SpotMask;
        sampler2D _SpotPattern;
        sampler2D _FeatureMask;
        fixed4 _Color;
        fixed4 _AccentColor;
        fixed4 _SpotColor;
        float _SpotSeed;
        float _SpotStrength;
        float _AlbinoMode;
        fixed4 _AlbinoBodyColor;
        float _PinkEyeMode;
        fixed4 _EyeColor;
        fixed4 _MatureTailColor;
        float _MatureTailStrength;
        float _MarkingFamily;
        float _FaceMarkingStrength;
        float _LegMarkingStrength;
        float _BellyMarkingStrength;
        float4 _RatModelBoundsMin;
        float4 _RatModelBoundsSize;
        float4 _SpotMask_TexelSize;

        float SampleFeatheredBodyMask(float2 uv)
        {
            // The shared imported mask is intentionally broad, but its
            // original edge is a hard polygon. A small weighted kernel keeps
            // that one shared texture cheap while removing the visible UV
            // cutoff from every rat.
            float2 texel = max(_SpotMask_TexelSize.xy, float2(1.0 / 512.0, 1.0 / 512.0));
            float center = tex2D(_SpotMask, uv).r * 4.0;
            float cardinals =
                tex2D(_SpotMask, uv + float2(texel.x, 0.0)).r +
                tex2D(_SpotMask, uv - float2(texel.x, 0.0)).r +
                tex2D(_SpotMask, uv + float2(0.0, texel.y)).r +
                tex2D(_SpotMask, uv - float2(0.0, texel.y)).r;
            float diagonals =
                tex2D(_SpotMask, uv + texel).r +
                tex2D(_SpotMask, uv + float2(texel.x, -texel.y)).r +
                tex2D(_SpotMask, uv + float2(-texel.x, texel.y)).r +
                tex2D(_SpotMask, uv - texel).r;
            return saturate((center + cardinals * 2.0 + diagonals) / 16.0);
        }

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 source = tex2D(_MainTex, input.uv_MainTex);
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
            float darkFeatureSignal = 1.0 - smoothstep(0.12, 0.42, paintedLuminance);
            fixed3 darkFeature = fixed3(0.07, 0.055, 0.06) *
                (0.82 + saturate(paintedLuminance) * 0.45);
            fixed3 pinkFeature = fixed3(0.66, 0.18, 0.28) *
                (0.72 + saturate(paintedLuminance) * 0.24);
            fixed3 featureColor = lerp(darkFeature, pinkFeature, _PinkEyeMode);
            fixed3 albinoPainted = lerp(albinoFur, featureColor, darkFeatureSignal * 0.94);

            // Preserve pink/red accent pixels from the supplied hand-painted
            // texture (ears, nose, paws, and eye accents) without allowing
            // the source beige/tan coat to leak back into the albino body.
            float pinkSignal = saturate((source.r - source.g) * 5.0) *
                saturate(1.0 - abs(source.g - source.b) * 8.0);
            fixed3 pinkAccent = fixed3(1.0, 0.58, 0.62) * (0.82 + saturate(painted.r) * 0.16);
            albinoPainted = lerp(albinoPainted, pinkAccent, pinkSignal * 0.78);
            painted.rgb = lerp(painted.rgb, albinoPainted, _AlbinoMode);
            float bodyMask = SampleFeatheredBodyMask(input.uv_MainTex);
            // The organic patch mask is generated once per rat visual from
            // its stable ID/genetics. Keeping this shader-side operation to a
            // single lookup avoids stamping identical procedural circles or
            // running random/noise work every rendered frame.
            float spots = tex2D(_SpotPattern, input.uv_MainTex).r;
            float familyEdge = lerp(0.08, 0.13, saturate(_MarkingFamily / 20.0));
            // Sample a feathered body mask rather than treating the UV mask as
            // a binary decal. A small stable UV disturbance keeps the edge
            // irregular without adding animated noise or per-rat textures.
            float edgeVariation = sin(input.uv_MainTex.x * 37.0 +
                input.uv_MainTex.y * 19.0 + _SpotSeed * 4.7) * 0.035;
            float softenedSpots = smoothstep(
                familyEdge * 0.25 + edgeVariation,
                0.62 + edgeVariation,
                spots);
            float softenedBodyMask = smoothstep(0.02, 0.92, bodyMask);

            // The organic pattern is the marking boundary. The legacy body
            // mask is used only as a very gentle falloff, not as a silhouette
            // cutoff; multiplying by it was what reproduced the old polygon
            // shaped white patches on the imported mesh.
            float bodyFalloff = lerp(0.90, 1.0, softenedBodyMask);
            float whiteBlend = saturate(bodyFalloff * softenedSpots * _SpotStrength);
            // Feature regions are generated once from the imported mesh's
            // bone weights and rasterized into a shared UV mask. Sampling UVs
            // here keeps markings attached to the skinned surface while the
            // head, ears, legs, and tail animate; using current world/object
            // position would make the white regions slide between bones.
            float4 featureMask = tex2D(_FeatureMask, input.uv_MainTex);
            // Do not treat weakly weighted transition vertices as markings.
            // Those vertices sit at shoulders, hips, elbows, and knees; using
            // their small blended values creates the thin white joint seams.
            // Require a feature to own the vertex before it can contribute a
            // marking. The old lower thresholds painted the small blended
            // bone weights at shoulders, hips, elbows, and knees. Those
            // transition pixels appeared as thin white joint seams on every
            // moving pose, even when the intended marking was elsewhere.
            // The feature mask is built from clear bone ownership rather
            // than every skinning influence. Keep its final threshold high
            // enough that a bilinear pixel at a torso/limb transition cannot
            // become a one-pixel white seam while the owned head and leg
            // regions remain available for real facial and sock markings.
            float faceRegion = smoothstep(0.74, 0.98, featureMask.r);
            float legRegion = smoothstep(0.78, 0.99, featureMask.g);
            float bellyRegion = smoothstep(0.38, 0.86, featureMask.a);
            float featureVariation = sin(input.uv_MainTex.x * 23.0 +
                input.uv_MainTex.y * 9.0 + _SpotSeed * 2.3) * 0.08 +
                sin(input.uv_MainTex.y * 17.0 - input.uv_MainTex.x * 13.0 +
                _SpotSeed * 4.1) * 0.05;
            faceRegion *= 0.90 + 0.10 *
                (sin(input.uv_MainTex.x * 17.0 + _SpotSeed) * 0.5 + 0.5);
            legRegion *= 0.86 + 0.14 *
                (sin(input.uv_MainTex.y * 31.0 + _SpotSeed * 1.7) * 0.5 + 0.5);
            bellyRegion *= 0.90 + 0.10 *
                (sin(input.uv_MainTex.x * 11.0 - input.uv_MainTex.y * 7.0 + _SpotSeed) * 0.5 + 0.5);
            float featureBlend = saturate(faceRegion * _FaceMarkingStrength +
                legRegion * _LegMarkingStrength + bellyRegion * _BellyMarkingStrength);
            // Break up the stable UV feature fields with a low-frequency
            // deterministic variation before feathering them. This keeps
            // face/leg/belly markings organic without animated noise or a
            // separate decal floating over the skinned mesh.
            // Feather only the actual feature coverage. Adding variation
            // before the threshold made tiny edge values become isolated
            // bright lines; modulating after the threshold keeps organic
            // variation inside the marking without outlining the seam.
            featureBlend = smoothstep(0.34, 0.92, featureBlend);
            featureBlend *= saturate(0.94 + featureVariation * 0.35);
            // Keep dark eye/mouth/tail detail readable when a white facial or
            // belly region crosses the same imported texture island.
            featureBlend *= saturate(1.0 - darkFeatureSignal * 0.48);
            whiteBlend = max(whiteBlend, featureBlend);

            // Adult and young rats use the mature skin treatment below. The
            // pinkie prefab returns before this shader is assigned, so its
            // dedicated pinkie material can never leak onto mature stages.
            // The imported mesh has a single renderer, but its tail vertices
            // occupy the upper/back edge of the object bounds and a distinct
            // UV island. The old mask looked for a mid-height side region,
            // evaluated to zero on this mesh, and left the tail using the
            // hand-painted/pinkie-like appearance. Gate both the measured
            // object-space tail strip and its UV island so the actual tail is
            // recolored without painting the rump.
            // The blue channel is the tail-bone UV region generated by the
            // mesh audit. It remains stable through tail animation and does
            // not recolor nearby body triangles.
            float tailRegion = smoothstep(0.42, 0.86,
                tex2D(_FeatureMask, input.uv_MainTex).b);
            tailRegion *= 0.84 + 0.16 *
                (sin(input.uv_MainTex.x * 29.0 + input.uv_MainTex.y * 11.0 +
                _SpotSeed * 3.7) * 0.5 + 0.5);
            // The mature imported mesh has the tail in the same skinned
            // renderer as the body. Replace the source body's pinkie-like
            // tail pixels with a restrained skin texture: broad tonal
            // variation plus fine, UV-attached rings keeps the tail readable
            // while it bends and rotates with the rig. This is procedural and
            // shared by all rats, so it does not allocate a texture/material
            // per rat and cannot change saved phenotype data.
            float tailFine = sin(input.uv_MainTex.x * 211.0 +
                input.uv_MainTex.y * 97.0 + _SpotSeed * 5.1) * 0.5 + 0.5;
            float tailBroad = sin(input.uv_MainTex.x * 31.0 -
                input.uv_MainTex.y * 18.0 + _SpotSeed * 2.7) * 0.5 + 0.5;
            float tailRings = sin((input.uv_MainTex.x + input.uv_MainTex.y * 0.37) *
                148.0 + _SpotSeed * 1.9) * 0.5 + 0.5;
            float tailSkinValue = 0.88 + tailBroad * 0.10 +
                (tailFine - 0.5) * 0.045 + (tailRings - 0.5) * 0.035;
            fixed3 matureTail = _MatureTailColor.rgb * tailSkinValue;
            fixed3 coatWithMarkings = lerp(painted.rgb, _SpotColor.rgb, whiteBlend);
            coatWithMarkings = lerp(coatWithMarkings, matureTail,
                tailRegion * _MatureTailStrength);

            // The source texture contains eye detail, but its hue can be
            // recolored when the whole imported mesh is tinted. Restore a
            // small, localized eye signal last so ordinary rats always have
            // black eyes and albino/pink-eye phenotypes retain pink/red eyes.
            float eyeRegion = saturate(darkFeatureSignal * featureMask.r * 1.35);
            output.Albedo = lerp(coatWithMarkings, _EyeColor.rgb, eyeRegion);
            output.Metallic = 0.0;
            output.Smoothness = 0.08;
            output.Alpha = painted.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
