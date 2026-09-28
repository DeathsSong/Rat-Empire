Shader "Rat Habitat/Mature Rat Tail Skin"
{
    Properties
    {
        _MainTex ("Mature Tail Skin", 2D) = "gray" {}
        _Color ("Tail Skin Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 180

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 skin = tex2D(_MainTex, input.uv_MainTex);
            output.Albedo = skin.rgb * _Color.rgb;
            output.Metallic = 0.0;
            output.Smoothness = 0.12;
            output.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
