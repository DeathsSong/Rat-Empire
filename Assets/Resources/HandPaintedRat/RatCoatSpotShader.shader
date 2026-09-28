Shader "Rat Habitat/Hand Painted Rat Coat"
{
    Properties
    {
        _MainTex ("Hand-Painted Coat", 2D) = "white" {}
        _Color ("Coat Color", Color) = (1, 1, 1, 1)
        _AccentColor ("Coat Accent", Color) = (1, 1, 1, 1)
        _SpotMask ("Body Spot UV Mask", 2D) = "black" {}
        _SpotPattern ("Organic Spot Pattern", 2D) = "black" {}
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

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
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
            // The recorded phenotype is the color authority. Use the
            // authored map's luminance for fur shading and retain only a
            // restrained amount of its source hue, so a Russian blue, fawn,
            // mink, or champagne rat cannot render as the same grey/brown
            // texture simply because it shares a source UV map.
            float furValue = (0.82 + sourceLuminance * 0.30) * furNoise;
            fixed3 fur = _Color.rgb * furValue;
            float accentWave = sin(dot(input.uv_MainTex, float2(17.13, 31.71)) + _SpotSeed * 3.17);
            float accentAmount = saturate(0.08 + (accentWave * 0.5 + 0.5) * 0.16);
            fur = lerp(fur, _AccentColor.rgb * (0.88 + sourceLuminance * 0.20), accentAmount);

            // Keep a small amount of the hand-painted source around feature
            // boundaries. This preserves natural ear/nose/tail shading on
            // the single-mesh import without allowing its old coat hue to
            // override the recorded phenotype.
            fixed4 painted = fixed4(lerp(fur, source.rgb, 0.16), source.a);
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
            float bodyMask = tex2D(_SpotMask, input.uv_MainTex).r;
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

            float whiteBlend = saturate(softenedBodyMask * softenedSpots * _SpotStrength);
            // The imported asset's body mask intentionally avoids several
            // face, leg, and underside UV islands. Add a small object-space
            // contribution so the recorded marking family is visible on the
            // actual skinned mesh, not just in profile text. worldPos is
            // converted through the rat visual's object transform, so these
            // regions follow bones, rotation, and animation naturally.
            float3 ratLocal = mul(unity_WorldToObject, float4(input.worldPos, 1.0)).xyz;
            float3 modelSize = max(_RatModelBoundsSize.xyz, float3(0.0001, 0.0001, 0.0001));
            float3 ratUv = saturate((ratLocal - _RatModelBoundsMin.xyz) / modelSize);
            float centeredX = ratUv.x - 0.5;
            // The imported mesh is authored with the head at its positive local
            // Z end. Normalizing against the mesh bounds is important because
            // the FBX is authored in centimetres; hard-coded world-style
            // coordinates made the old face/leg regions miss the mesh entirely.
            float headEnd = smoothstep(0.52, 0.76, ratUv.z);
            float muzzleEnd = smoothstep(0.70, 0.94, ratUv.z);
            float faceWidth = 1.0 - smoothstep(0.18, 0.48, abs(centeredX));
            float faceHeight = smoothstep(0.18, 0.34, ratUv.y) *
                (1.0 - smoothstep(0.84, 0.98, ratUv.y));
            // A low-frequency deterministic offset makes the facial region
            // slightly asymmetric without changing the stored genetics.
            float faceOffset = sin(_SpotSeed * 19.37 + ratUv.z * 11.0) * 0.07;
            float stripe = 1.0 - smoothstep(0.035, 0.20,
                abs(centeredX + faceOffset));
            float cheek = smoothstep(0.18, 0.34, abs(centeredX + faceOffset)) *
                (1.0 - smoothstep(0.38, 0.49, abs(centeredX + faceOffset)));
            float facePattern = lerp(cheek, stripe, step(11.5, _MarkingFamily) *
                step(_MarkingFamily, 14.5));
            float faceRegion = saturate(headEnd * faceWidth * faceHeight *
                (0.58 + 0.42 * facePattern) *
                (0.72 + 0.28 * muzzleEnd));

            float lowerBody = 1.0 - smoothstep(0.22, 0.50, ratUv.y);
            float legSide = smoothstep(0.18, 0.35, abs(centeredX));
            float frontLeg = smoothstep(0.52, 0.78, ratUv.z);
            float rearLeg = 1.0 - smoothstep(0.18, 0.42, ratUv.z);
            float legEndVariation = 0.78 + 0.22 *
                (sin(_SpotSeed * 13.1 + ratUv.z * 17.0 + ratUv.x * 5.0) * 0.5 + 0.5);
            float legRegion = saturate(lowerBody * legSide *
                (0.54 + 0.46 * max(frontLeg, rearLeg)) * legEndVariation);
            float bellyRegion = saturate(lowerBody *
                (1.0 - smoothstep(0.12, 0.34, abs(centeredX))) *
                (0.76 + 0.24 * sin(_SpotSeed * 7.7 + ratUv.z * 9.0)));
            // Carry the marking softly over the rump/tail base as well, while
            // leaving the narrow tail-detail pixels available to the feature
            // preservation signal above.
            float rumpRegion = (1.0 - smoothstep(0.08, 0.30, ratUv.z)) *
                smoothstep(0.20, 0.44, ratUv.y) *
                (1.0 - smoothstep(0.22, 0.46, abs(centeredX)));
            float featureBlend = saturate(faceRegion * _FaceMarkingStrength +
                legRegion * _LegMarkingStrength + bellyRegion * _BellyMarkingStrength +
                rumpRegion * _BellyMarkingStrength * 0.42);
            // Break up the object-space feature fields with a stable, very
            // low-frequency fur variation before feathering them. This keeps
            // face/leg/belly markings organic without animated noise or a
            // separate decal floating over the skinned mesh.
            float featureVariation = sin(ratUv.x * 23.0 + ratUv.z * 9.0 + _SpotSeed * 2.3) * 0.08 +
                sin(ratUv.y * 17.0 - ratUv.z * 13.0 + _SpotSeed * 4.1) * 0.05;
            featureBlend = smoothstep(0.08, 0.86,
                saturate(featureBlend + featureVariation * 0.30));
            // Keep dark eye/mouth/tail detail readable when a white facial or
            // belly region crosses the same imported texture island.
            featureBlend *= saturate(1.0 - darkFeatureSignal * 0.72);
            whiteBlend = max(whiteBlend, featureBlend);

            // Adult and young rats use the mature skin treatment below. The
            // pinkie prefab returns before this shader is assigned, so its
            // dedicated pinkie material can never leak onto mature stages.
            // The transition is intentionally broad and feathered around the
            // rump/tail base so it does not create a hard color seam.
            float tailAxis = 1.0 - smoothstep(0.015, 0.26, ratUv.z);
            float tailHeight = 1.0 - smoothstep(0.16, 0.43, abs(ratUv.y - 0.39));
            float tailWidth = 1.0 - smoothstep(0.10, 0.40, abs(centeredX));
            float tailRegion = saturate(tailAxis * tailHeight * tailWidth);
            tailRegion *= 0.76 + 0.24 *
                (sin(ratUv.x * 29.0 + ratUv.y * 11.0 + _SpotSeed * 3.7) * 0.5 + 0.5);
            fixed3 coatWithMarkings = lerp(painted.rgb, _SpotColor.rgb, whiteBlend);
            coatWithMarkings = lerp(coatWithMarkings, _MatureTailColor.rgb,
                tailRegion * _MatureTailStrength);

            // The source texture contains eye detail, but its hue can be
            // recolored when the whole imported mesh is tinted. Restore a
            // small, localized eye signal last so ordinary rats always have
            // black eyes and albino/pink-eye phenotypes retain pink/red eyes.
            float eyeFront = smoothstep(0.62, 0.79, ratUv.z) *
                (1.0 - smoothstep(0.86, 0.98, ratUv.z));
            float eyeBand = 1.0 - smoothstep(0.30, 0.47, abs(ratUv.y - 0.49));
            float eyeSide = 1.0 - smoothstep(0.035, 0.13, abs(abs(centeredX) - 0.18));
            float eyeRegion = saturate(eyeFront * eyeBand * eyeSide);
            eyeRegion *= 0.88 + 0.12 *
                (sin(ratUv.x * 43.0 + ratUv.z * 17.0 + _SpotSeed) * 0.5 + 0.5);
            output.Albedo = lerp(coatWithMarkings, _EyeColor.rgb, eyeRegion);
            output.Metallic = 0.0;
            output.Smoothness = 0.08;
            output.Alpha = painted.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
