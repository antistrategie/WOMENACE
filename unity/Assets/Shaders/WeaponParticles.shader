Shader "Womenace/WeaponParticles"
{
    Properties
    {
        _MainTex ("Particle texture", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _MaskTex2 ("Second mask", 2D) = "white" {}
        _TurbulenceTex ("Distortion noise", 2D) = "grey" {}
        _DissolveTex ("Dissolve mask", 2D) = "white" {}
        [HDR] _MainColor ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Float) = 1
        _Contrast ("Contrast", Float) = 1
        _Alpha ("Opacity", Range(0, 1)) = 1
        _ExposureWeight ("Exposure weight", Range(0, 1)) = 1
        [Toggle] _AlphaFromRed ("Red channel opacity", Float) = 0
        [Toggle] _UseMask ("Use mask", Float) = 0
        [Toggle] _UseMask2 ("Use second mask", Float) = 0
        _PanX ("Horizontal speed", Float) = 0
        _PanY ("Vertical speed", Float) = 0
        _MaskPanX ("Mask horizontal speed", Float) = 0
        _MaskPanY ("Mask vertical speed", Float) = 0
        _MaskPanX2 ("Second mask horizontal speed", Float) = 0
        _MaskPanY2 ("Second mask vertical speed", Float) = 0
        [Toggle] _UseTurbulence ("Distort texture coordinates", Float) = 0
        _TurbulencePower ("Distortion strength", Float) = 0
        _TurbulencePanX ("Noise horizontal speed", Float) = 0
        _TurbulencePanY ("Noise vertical speed", Float) = 0
        [Toggle] _UseDissolve ("Dissolve", Float) = 0
        _Dissolve ("Dissolve amount", Range(0, 1)) = 0
        _DissolveHardness ("Dissolve hardness", Range(0, 1)) = 0.5
        _DissolvePanX ("Dissolve horizontal speed", Float) = 0
        _DissolvePanY ("Dissolve vertical speed", Float) = 0
        _FresnelPower ("Rim falloff (zero disables)", Float) = 0
        _GlitchStrength ("Scanline distortion", Float) = 0
        _GlitchSpeed ("Scanline speed", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SourceBlend ("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DestinationBlend ("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend [_SourceBlend] [_DestinationBlend]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_MaskTex2);
            SAMPLER(sampler_MaskTex2);
            TEXTURE2D(_TurbulenceTex);
            SAMPLER(sampler_TurbulenceTex);
            TEXTURE2D(_DissolveTex);
            SAMPLER(sampler_DissolveTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _MaskTex_ST, _MainColor;
                float4 _MaskTex2_ST;
                float4 _TurbulenceTex_ST, _DissolveTex_ST;
                float _Brightness, _Contrast, _Alpha, _ExposureWeight, _AlphaFromRed, _UseMask;
                float _PanX, _PanY, _MaskPanX, _MaskPanY;
                float _UseMask2, _MaskPanX2, _MaskPanY2;
                float _UseTurbulence, _TurbulencePower, _TurbulencePanX, _TurbulencePanY;
                float _UseDissolve, _Dissolve, _DissolveHardness, _DissolvePanX, _DissolvePanY;
                float _FresnelPower, _GlitchStrength, _GlitchSpeed;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.colour = input.colour;
                output.uv = input.uv;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw + _Time.y * float2(_PanX, _PanY);
                if (_UseTurbulence > 0.5)
                {
                    float2 noiseUV = input.uv * _TurbulenceTex_ST.xy + _TurbulenceTex_ST.zw
                        + _Time.y * float2(_TurbulencePanX, _TurbulencePanY);
                    uv += (SAMPLE_TEXTURE2D(_TurbulenceTex, sampler_TurbulenceTex, noiseUV).rg - 0.5) * _TurbulencePower;
                }
                if (_GlitchStrength > 0.0)
                {
                    float scan = floor(input.uv.y * 100.0) + floor(_Time.y * _GlitchSpeed);
                    uv.x += (frac(sin(scan * 12.9898) * 43758.5453) - 0.5) * _GlitchStrength;
                }
                float4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float opacity = lerp(sample.a, sample.r, _AlphaFromRed);
                float2 maskUV = input.uv * _MaskTex_ST.xy + _MaskTex_ST.zw + _Time.y * float2(_MaskPanX, _MaskPanY);
                opacity *= lerp(1.0, SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUV).r, _UseMask);
                if (_UseMask2 > 0.5)
                {
                    float2 maskUV2 = input.uv * _MaskTex2_ST.xy + _MaskTex2_ST.zw
                        + _Time.y * float2(_MaskPanX2, _MaskPanY2);
                    opacity *= SAMPLE_TEXTURE2D(_MaskTex2, sampler_MaskTex2, maskUV2).r;
                }
                if (_UseDissolve > 0.5)
                {
                    float2 dissolveUV = input.uv * _DissolveTex_ST.xy + _DissolveTex_ST.zw
                        + _Time.y * float2(_DissolvePanX, _DissolvePanY);
                    float dissolve = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, dissolveUV).r;
                    opacity *= saturate((dissolve - _Dissolve) / max(0.001, 1.0 - _DissolveHardness));
                }
                if (_FresnelPower > 0.0)
                {
                    float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    opacity *= pow(saturate(1.0 - abs(dot(normalize(input.normalWS), view))), _FresnelPower);
                }
                float3 colour = pow(max(sample.rgb, 0.0), _Contrast) * input.colour.rgb * _MainColor.rgb * _Brightness;
                // GFL2's decorative glows use display colours, so they can opt out of
                // MENACE's physical exposure without changing the firing effects.
                colour *= lerp(1.0, GetCurrentExposureMultiplier(), _ExposureWeight);
                return float4(colour, saturate(opacity * input.colour.a * _MainColor.a * _Alpha));
            }
            ENDHLSL
        }
    }
}
