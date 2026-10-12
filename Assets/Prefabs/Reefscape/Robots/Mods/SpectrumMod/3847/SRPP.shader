// Self-reinforced polypropylene: a woven tape sheet that looks like carbon fiber (black by default).
// The CAD parts' UVs are inconsistent, so the weave is projected from the part's three axes instead (triplanar), in the
// part's own space scaled to meters: the same real size on every part, and fixed to the part as it moves.
Shader "GammaRay/SRPP"
{
    Properties
    {
        _BaseColor("Color", Color) = (0.07, 0.07, 0.075, 1)
        [NoScaleOffset] _WeaveMap("Weave", 2D) = "white" {}
        [NoScaleOffset][Normal] _WeaveNormal("Weave normal", 2D) = "bump" {}
        _NormalStrength("Weave depth", Range(0, 2)) = 1.3
        _TileSize("Weave tile size (m, 8 tapes)", Float) = 0.02
        _Smoothness("Smoothness", Range(0, 1)) = 0.75
        _BlendSharpness("Projection blend sharpness", Range(1, 16)) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_WeaveMap); SAMPLER(sampler_WeaveMap);
            TEXTURE2D(_WeaveNormal); SAMPLER(sampler_WeaveNormal);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _NormalStrength;
                float _TileSize;
                half _Smoothness;
                half _BlendSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 weavePos : TEXCOORD1;  // object space in meters, in tiles
                float3 normalOS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                // The object's scale, so the weave is sized in meters whatever the CAD import scale is
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21),
                                      length(UNITY_MATRIX_M._m02_m12_m22));
                output.weavePos = input.positionOS.xyz * scale / _TileSize;
                output.normalOS = input.normalOS;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalOS);
                float3 weights = pow(abs(n), _BlendSharpness);
                weights /= weights.x + weights.y + weights.z;
                float3 p = input.weavePos;

                half3 albedo = SAMPLE_TEXTURE2D(_WeaveMap, sampler_WeaveMap, p.zy).rgb * weights.x
                             + SAMPLE_TEXTURE2D(_WeaveMap, sampler_WeaveMap, p.xz).rgb * weights.y
                             + SAMPLE_TEXTURE2D(_WeaveMap, sampler_WeaveMap, p.xy).rgb * weights.z;

                // Triplanar normal mapping, whiteout blend: each projection's tangent-space normal swizzled onto its axis
                half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_WeaveNormal, sampler_WeaveNormal, p.zy), _NormalStrength);
                half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_WeaveNormal, sampler_WeaveNormal, p.xz), _NormalStrength);
                half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_WeaveNormal, sampler_WeaveNormal, p.xy), _NormalStrength);
                tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
                float3 normalOS = normalize(tx.zyx * weights.x + ty.xzy * weights.y + tz.xyz * weights.z);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = TransformObjectToWorldNormal(normalOS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo * _BaseColor.rgb;
                surface.metallic = 0;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // Shadows and depth (SSAO) as for any opaque Lit surface
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
