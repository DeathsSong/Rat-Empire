Shader "Rat Habitat/Diagnostic Rat Visual"
{
    Properties
    {
        _Color ("Diagnostic Color", Color) = (0.5, 0.5, 0.5, 1)
        _Mode ("Diagnostic Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _LightColor0;
            float _Mode;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldTangent : TEXCOORD2;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldTangent = UnityObjectToWorldDir(input.tangent.xyz);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 normal = normalize(input.worldNormal);
                float3 tangent = normalize(input.worldTangent);

                if (_Mode > 3.5)
                    return fixed4(tangent * 0.5 + 0.5, 1.0);
                if (_Mode > 2.5)
                    return fixed4(normal * 0.5 + 0.5, 1.0);
                if (_Mode > 1.5)
                    return _Color;
                if (_Mode > 0.5)
                    return _Color;

                float3 lightDirection = normalize(UnityWorldSpaceLightDir(input.worldPosition));
                float diffuse = saturate(dot(normal, lightDirection));
                float3 lit = _Color.rgb * (0.28 + diffuse * 0.72) * _LightColor0.rgb;
                return fixed4(lit, 1.0);
            }
            ENDCG
        }
    }
}
