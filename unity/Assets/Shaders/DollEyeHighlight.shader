// GFL2's eye highlight layer, for MENACE.
//
// The third of the three stacked eye materials: the additive glint that sits over
// the iris. Its own shader for the same reason as the darkening layer, a blend
// mode being fixed per pass.
//
// Transcribed from the decompiled fragment:
//
//   r0.x   = saturate(dot(normal, lightDir))
//   r0.x   = r0.x * 0.8 + 0.2
//   r1     = tex.Sample(uv) * _MainColor
//   o0.rgb = _SpecularIntensity * r1.rgb * r0.x
//   o0.a   = r1.a
//
// against Blend One One. The 0.2 floor keeps a fifth of the glint alive in full
// shadow, the same shape the hair specular uses with its 0.1. The mask is nearly
// black over its own footprint, so it adds nothing except where the glint is
// painted.
//
// Note what is absent: no light colour. The game adds this in its own display
// space rather than scaling it by the scene's illuminance.
Shader "Womenace/DollEyeHighlight"
{
    Properties
    {
        _BaseMap ("Mask", 2D) = "black" {}
        _MainColour ("Main Colour", Color) = (1, 1, 1, 1)
        _SpecularIntensity ("Specular Intensity", Range(0, 2)) = 1
    }

    SubShader
    {
        // Geometry+458, as the game has it: over the eyeball, under the darkening
        // layer at +459.
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+458"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _MainColour;
                float _SpecularIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 frag(Varyings input) : SV_TARGET
            {
                // The scene's key light, read from HDRP rather than pushed in.
                float3 L = float3(0.0, 1.0, 0.0);
                if (_DirectionalLightCount > 0)
                    L = -_DirectionalLightDatas[0].forward;

                float ndotl = saturate(dot(normalize(input.normalWS), L));
                float attenuation = ndotl * 0.8 + 0.2;

                float4 mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _MainColour;
                // Written display-referred rather than pre-exposed. The game adds
                // this without a light colour or an illuminance, so there is no
                // radiance to convert: the buffer it lands in has already been
                // scaled to roughly unit range by exposure, which is the closest
                // thing to the space the game adds it in. A units judgement, not a
                // transcription, and the one part of this layer that is.
                return float4(_SpecularIntensity * mask.rgb * attenuation, mask.a);
            }
            ENDHLSL
        }
    }
}
