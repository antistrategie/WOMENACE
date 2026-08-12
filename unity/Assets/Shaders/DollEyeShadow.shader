// GFL2's eye darkening layer, for MENACE.
//
// Eyes are three stacked materials in the game: the eyeball, a darkening layer
// over it, and a highlight layer. This is the darkening one, which is what puts
// the lid's shadow across the top of the iris. It is its own shader rather than a
// branch of the doll shader because a blend mode is fixed per pass, so the three
// layers cannot share one.
//
// Transcribed from the decompiled fragment, which is four instructions:
//
//   r0   = tex.Sample(uv)
//   r0.rgb = r0.rgb * _MainColor.rgb - 1
//   o0.rgb = _MultiplyIntensity * r0.rgb + 1
//   o0.a   = 1
//
// which is lerp(1, mask.rgb * colour, intensity) against Blend DstColor Zero. The
// mask is used directly as the multiplier, so white leaves the eye alone and dark
// darkens it. There is no alpha test: the white regions are the no-op, which is
// why the layer can cover more than the iris without tinting what it overhangs.
Shader "Womenace/DollEyeShadow"
{
    Properties
    {
        // The game calls this _MainTex and labels it "Mask". Named _BaseMap here
        // so the bake wires the source material's own texture into it.
        _BaseMap ("Mask", 2D) = "white" {}
        _MainColour ("Main Colour", Color) = (1, 1, 1, 1)
        _MultiplyIntensity ("Multiply Intensity", Range(0, 1)) = 1
    }

    SubShader
    {
        // Geometry+459, as the game has it: after the eyeball it darkens, and
        // after the highlight layer at +458.
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+459"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend DstColor Zero
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
                float _MultiplyIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            float4 frag(Varyings input) : SV_TARGET
            {
                float4 mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                // No exposure multiply, and that is not an oversight: this is a
                // ratio applied to what is already in the buffer, not a quantity
                // of light. Scaling a multiplier by exposure would change how much
                // it darkens depending on how bright the scene is.
                float3 src = lerp(1.0, mask.rgb * _MainColour.rgb, _MultiplyIntensity);
                return float4(src, 1.0);
            }
            ENDHLSL
        }
    }
}
