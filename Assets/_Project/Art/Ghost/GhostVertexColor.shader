// Soft, slightly toon-like lighting for the ghost companion. Colour comes from the vertex
// colours baked by Tools/blender/export_ghost_fbx.py (stored as sRGB).
Shader "AHC/GhostVertexColor"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Emission ("Emission", Range(0, 1)) = 0
        _Gloss ("Gloss", Range(0, 1)) = 0.3
        _Rim ("Rim light", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Emission;
                half _Gloss;
                half _Rim;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half3 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                half3 c = v.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c = SRGBToLinear(c);
                #endif
                o.color = c * _Tint.rgb;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight();

                half wrap = saturate(dot(n, light.direction) * 0.5 + 0.5);   // half-Lambert: no harsh dark side
                half3 diffuse = i.color * (SampleSH(n) + light.color * wrap * 0.8);
                float3 h = normalize(light.direction + v);
                half spec = pow(saturate(dot(n, h)), lerp(8.0, 96.0, _Gloss)) * _Gloss;
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _Rim;

                half3 col = diffuse + spec * light.color + rim + i.color * _Emission;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
