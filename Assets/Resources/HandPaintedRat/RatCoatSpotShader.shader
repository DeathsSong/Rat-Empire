Shader "Rat Habitat/Hand Painted Rat Coat"
{
    Properties
    {
        _MainTex ("Hand-Painted Coat", 2D) = "white" {}
        _Color ("Coat Color", Color) = (1, 1, 1, 1)
        _SpotMask ("Body Spot UV Mask", 2D) = "black" {}
        _SpotPattern ("Organic Spot Pattern", 2D) = "black" {}
        _SpotColor ("Spot Color", Color) = (1, 1, 1, 1)
        _SpotSeed ("Spot Seed", Float) = 0
        _SpotStrength ("Spot Strength", Range(0, 1)) = 0
        _AlbinoMode ("Albino Neutralization", Range(0, 1)) = 0
        _AlbinoBodyColor ("Albino Body Color", Color) = (0.98, 0.965, 0.92, 1)
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
        fixed4 _SpotColor;
        float _SpotSeed;
        float _SpotStrength;
        float _AlbinoMode;
        fixed4 _AlbinoBodyColor;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 painted = tex2D(_MainTex, input.uv_MainTex) * _Color;
            float paintedLuminance = dot(painted.rgb, float3(0.299, 0.587, 0.114));
            // Albino remains recognizably hand-painted through luminance
            // variation, but the source beige/brown hue is removed. This
            // prevents albino from reading as diluted brown while leaving
            // the source texture and the shared imported material untouched.
            fixed3 albinoPainted = _AlbinoBodyColor.rgb * (0.86 + saturate(paintedLuminance) * 0.18);
            // Preserve pink/red accent pixels from the supplied hand-painted
            // texture (ears, nose, paws, and eye accents) without allowing
            // the source beige/tan coat to leak back into the albino body.
            float pinkSignal = saturate((painted.r - painted.g) * 5.0) *
                saturate(1.0 - abs(painted.g - painted.b) * 8.0);
            fixed3 pinkAccent = fixed3(1.0, 0.58, 0.62) * (0.82 + saturate(painted.r) * 0.16);
            albinoPainted = lerp(albinoPainted, pinkAccent, pinkSignal * 0.9);
            float darkFeatureSignal = saturate((0.38 - paintedLuminance) * 4.0);
            fixed3 paleEyeAccent = fixed3(0.92, 0.5, 0.54);
            albinoPainted = lerp(albinoPainted, paleEyeAccent, darkFeatureSignal * 0.72);
            painted.rgb = lerp(painted.rgb, albinoPainted, _AlbinoMode);
            float bodyMask = tex2D(_SpotMask, input.uv_MainTex).r;
            // The organic patch mask is generated once per rat visual from
            // its stable ID/genetics. Keeping this shader-side operation to a
            // single lookup avoids stamping identical procedural circles or
            // running random/noise work every rendered frame.
            float spots = tex2D(_SpotPattern, input.uv_MainTex).r;

            float whiteBlend = saturate(bodyMask * spots * _SpotStrength);
            output.Albedo = lerp(painted.rgb, _SpotColor.rgb, whiteBlend);
            output.Metallic = 0.0;
            output.Smoothness = 0.0;
            output.Alpha = painted.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
