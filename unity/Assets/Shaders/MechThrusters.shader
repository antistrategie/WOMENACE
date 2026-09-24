Shader "Womenace/MechThrusters"
{
    Properties
    {
        [Enum(UVAll, 0, UVAllNew, 1, BaseBland, 2)] _SourceVariant ("Source shader", Float) = 0
        [Enum(CustomData, 0, Material, 1)] _ShaderMode ("Parameter source", Float) = 1
        _MainTex ("Particle texture", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _MaskTex2 ("Second mask", 2D) = "white" {}
        _TurbulenceTex ("Distortion noise", 2D) = "grey" {}
        _DissolveTex ("Dissolve texture", 2D) = "white" {}
        [HDR] _MainColor ("Main colour", Color) = (1, 1, 1, 1)
        [HDR] _FixColor ("BaseBland colour", Color) = (1, 1, 1, 1)
        [HDR] _DissolveEdgeColor ("Dissolve edge colour", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Float) = 1
        _Contrast ("Contrast", Float) = 1
        _Alpha ("Opacity", Float) = 1
        _AdditionalAlpha ("Additional opacity", Range(0, 1)) = 1
        _ExposureWeight ("HDRP exposure weight", Range(0, 1)) = 0
        [Toggle] _AlphaIsR ("Red channel opacity", Float) = 0
        [Toggle] _UseMask ("Use mask", Float) = 0
        [Toggle] _UseMask2 ("Use second mask", Float) = 0
        [Toggle] _UseMask2AsColor ("Second mask colours UVAll", Float) = 0
        [Toggle] _UseTurbulence ("Use turbulence", Float) = 0
        [Toggle] _UseDissolve ("Use dissolve", Float) = 0
        _MainPannerX ("Main horizontal speed", Float) = 0
        _MainPannerY ("Main vertical speed", Float) = 0
        _MainTexPannerX ("BaseBland horizontal speed", Float) = 0
        _MainTexPannerY ("BaseBland vertical speed", Float) = 0
        _MaskPannerX ("Mask horizontal speed", Float) = 0
        _MaskPannerY ("Mask vertical speed", Float) = 0
        _MaskPannerX2 ("Second mask horizontal speed", Float) = 0
        _MaskPannerY2 ("Second mask vertical speed", Float) = 0
        _TurbulencePannerX ("Noise horizontal speed", Float) = 0
        _TurbulencePannerY ("Noise vertical speed", Float) = 0
        _TurbulencePower ("Noise strength", Float) = 0
        _DissolvePannerX ("Dissolve horizontal speed", Float) = 0
        _DissolvePannerY ("Dissolve vertical speed", Float) = 0
        _Dissolve ("Dissolve amount", Range(0, 1)) = 0
        _DissolveHardness ("Dissolve hardness", Range(0, 0.99)) = 0
        _DissolveEdgeWidth ("Dissolve edge width", Range(0, 1)) = 0
        [Toggle] _SOFTPARTICLES ("Soft particles", Float) = 0
        _SoftParticleNear ("Soft particle near distance", Float) = 0
        _SoftParticleFar ("Soft particle fade multiplier", Float) = 2
        _Cutout ("Depth-writing alpha cutout", Range(0, 1)) = 0.5
        [Enum(Off, 0, On, 1)] _DepthMode ("Write depth", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull mode", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        [Enum(UnityEngine.Rendering.BlendMode)] SrcBlend ("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] DstBlend ("Destination blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] SrcAlphaBlend ("Source alpha blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] DstAlphaBlend ("Destination alpha blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull [_CullMode]
            ZWrite [_DepthMode]
            ZTest [_ZTest]
            Blend [SrcBlend] [DstBlend], [SrcAlphaBlend] [DstAlphaBlend]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // CN shader CAB-40f38b1dc72e7dc3b604956f43c3b5db, DXBC variants
            // UVAll 12/780 and UVAllNew 56/824 establish the UV and dissolve maths.
            // This is the Sinbreaker material subset, not a general GFL2 shader.
            // Source fog, TAA compensation and global scene brightness are not
            // HDRP contracts. Exposure is an explicit port setting instead.
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
                float4 _MainTex_ST, _MaskTex_ST, _MaskTex2_ST;
                float4 _TurbulenceTex_ST, _DissolveTex_ST;
                float4 _MainColor, _FixColor, _DissolveEdgeColor;
                float _SourceVariant, _ShaderMode, _Brightness, _Contrast;
                float _Alpha, _AdditionalAlpha, _ExposureWeight, _AlphaIsR;
                float _UseMask, _UseMask2, _UseMask2AsColor, _UseTurbulence, _UseDissolve;
                float _MainPannerX, _MainPannerY, _MainTexPannerX, _MainTexPannerY;
                float _MaskPannerX, _MaskPannerY, _MaskPannerX2, _MaskPannerY2;
                float _TurbulencePannerX, _TurbulencePannerY, _TurbulencePower;
                float _DissolvePannerX, _DissolvePannerY, _Dissolve, _DissolveHardness, _DissolveEdgeWidth;
                float _SOFTPARTICLES, _SoftParticleNear, _SoftParticleFar, _DepthMode, _Cutout;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 colour : COLOR;
                // SetActiveVertexStreams: Position, Normal, Color, UV, UV2,
                // Custom1XYZW, Custom2XYZW. UV2 fills TEXCOORD0.zw so custom
                // vectors align with the source DXBC input signatures.
                float4 uv : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                float4 custom2 : TEXCOORD2;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                float4 custom2 : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.colour = input.colour;
                output.uv = input.uv.xy;
                output.custom1 = input.custom1;
                output.custom2 = input.custom2;
                return output;
            }
            float2 Pan(float2 uv, float4 st, float2 speed)
            {
                return uv * st.xy + st.zw + _Time.y * speed;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                bool baseBland = _SourceVariant > 1.5;
                bool newUVAll = _SourceVariant > 0.5 && !baseBland;
                float2 mainUV;
                float2 distortion = 0.0;
                if (baseBland)
                {
                    mainUV = Pan(input.uv, _MainTex_ST, float2(_MainTexPannerX, _MainTexPannerY));
                }
                else
                {
                    // All ten source materials use mode 1. Mode 0 is retained
                    // with the stream meanings verified in the source bytecode.
                    float2 tiling = lerp(input.custom2.xy, _MainTex_ST.xy, _ShaderMode);
                    float2 offset = lerp(input.custom1.xy, _MainTex_ST.zw, _ShaderMode);
                    mainUV = input.uv * tiling + offset + _Time.y * float2(_MainPannerX, _MainPannerY);
                    if (_UseTurbulence > 0.5)
                    {
                        float2 noiseUV = Pan(input.uv, _TurbulenceTex_ST, float2(_TurbulencePannerX, _TurbulencePannerY));
                        float power = lerp(input.custom1.w, _TurbulencePower, _ShaderMode);
                        distortion = (SAMPLE_TEXTURE2D(_TurbulenceTex, sampler_TurbulenceTex, noiseUV).rg - 0.5) * power;
                        mainUV += distortion;
                    }
                }
                float4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, mainUV);
                float4 tint = baseBland ? _FixColor : _MainColor;
                float3 colour = baseBland ? texel.rgb : pow(max(texel.rgb, 0.0), max(_Contrast, 0.01));
                colour *= tint.rgb * input.colour.rgb * _Brightness;
                float opacity = baseBland ? texel.a : lerp(texel.a, texel.r, _AlphaIsR);
                if (!baseBland)
                {
                    float2 auxiliaryDistortion = newUVAll ? distortion : float2(0.0, 0.0);
                    if (_UseMask > 0.5)
                    {
                        float2 maskUV = Pan(input.uv, _MaskTex_ST, float2(_MaskPannerX, _MaskPannerY));
                        opacity *= SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUV + auxiliaryDistortion).r;
                        if (_UseMask2 > 0.5)
                        {
                            float2 maskUV2 = Pan(input.uv, _MaskTex2_ST, float2(_MaskPannerX2, _MaskPannerY2));
                            float4 mask2 = SAMPLE_TEXTURE2D(_MaskTex2, sampler_MaskTex2, maskUV2 + auxiliaryDistortion);
                            if (!newUVAll && _UseMask2AsColor > 0.5)
                                colour *= mask2.rgb;
                            else
                                opacity *= mask2.r;
                        }
                    }
                    if (_UseDissolve > 0.5)
                    {
                        float2 dissolveUV = Pan(input.uv, _DissolveTex_ST, float2(_DissolvePannerX, _DissolvePannerY));
                        float noise = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, dissolveUV + distortion).r;
                        float amount = lerp(input.custom1.z, _Dissolve, _ShaderMode);
                        float edge = lerp(input.custom2.z, _DissolveEdgeWidth, _ShaderMode);
                        float edgeWeight = lerp(input.custom2.w, 1.0, _ShaderMode);
                        float hardness = min(_DissolveHardness, 0.99);
                        float threshold = amount * (edge + 1.0);
                        float interior = saturate((noise + 1.0 - hardness - threshold * (2.0 - hardness)) / (1.0 - hardness));
                        float coverage = saturate((noise + 1.0 - hardness - (threshold - edge) * (2.0 - hardness)) / (1.0 - hardness));
                        colour *= lerp(_DissolveEdgeColor.rgb * edgeWeight, 1.0, interior);
                        opacity *= coverage;
                    }
                }
                opacity *= input.colour.a * tint.a * _AdditionalAlpha * (baseBland ? 1.0 : _Alpha);
                opacity = saturate(opacity);
                if (!baseBland)
                {
                    clip(opacity - min(_DepthMode, _Cutout));
                    opacity = lerp(opacity, 1.0, _DepthMode);
                }
                // UVAllNew DXBC variant 770 multiplies the depth difference by
                // _SoftParticleFar directly, despite its source inspector name.
                // HDRP depth replaces the source URP depth texture.
                if (_SOFTPARTICLES > 0.5)
                {
                    float sceneDepth = LinearEyeDepth(LoadCameraDepth(uint2(input.positionCS.xy)), _ZBufferParams);
                    float particleDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    opacity *= saturate((sceneDepth - particleDepth - _SoftParticleNear)
                        * _SoftParticleFar);
                }
                colour *= lerp(1.0, GetCurrentExposureMultiplier(), _ExposureWeight);
                return float4(colour, opacity);
            }
            ENDHLSL
        }
    }
}
