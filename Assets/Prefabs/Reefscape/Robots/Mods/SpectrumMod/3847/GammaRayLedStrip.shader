// GammaRayLeds' strip shader: samples the 1-pixel-tall LED texture by position along the strip's longest
// side (_Axis/_Min/_Max, object space, set per strip by GammaRayLeds). The LED strip mesh (borrowed from
// 2910) has no UVs along its length, so sampling by UV lit the whole strip with a single pixel.
Shader "GammaRay/LedStrip"
{
    Properties
    {
        _Texture2D("LEDs", 2D) = "black" {}
        _intensity("Intensity", Float) = 2
        _Axis("Axis (object space)", Vector) = (0, 0, 1, 0)
        _Min("Min along axis", Float) = 0
        _Max("Max along axis", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Texture2D);
            SAMPLER(sampler_Texture2D);
            CBUFFER_START(UnityPerMaterial)
                float4 _Texture2D_ST;
                float4 _Axis;
                float _intensity, _Min, _Max;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float t : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.t = (dot(i.positionOS.xyz, _Axis.xyz) - _Min) / (_Max - _Min);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_Texture2D, sampler_Texture2D, float2(saturate(i.t), 0.5)).rgb;
                return half4(c * _intensity, 1);
            }
            ENDHLSL
        }
    }
}
