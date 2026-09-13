Shader "Womenace/WeaponParticles"
{
    Properties
    {
        _MainTex ("Particle texture", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        [HDR] _MainColor ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Float) = 1
        _Contrast ("Contrast", Float) = 1
        _Alpha ("Opacity", Range(0, 1)) = 1
        _ExposureWeight ("Exposure weight", Range(0, 1)) = 1
        [Toggle] _AlphaFromRed ("Red channel opacity", Float) = 0
        [Toggle] _UseMask ("Use mask", Float) = 0
        _PanX ("Horizontal speed", Float) = 0
        _PanY ("Vertical speed", Float) = 0
        _MaskPanX ("Mask horizontal speed", Float) = 0
        _MaskPanY ("Mask vertical speed", Float) = 0
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
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _MaskTex_ST, _MainColor;
                float _Brightness, _Contrast, _Alpha, _ExposureWeight, _AlphaFromRed, _UseMask;
                float _PanX, _PanY, _MaskPanX, _MaskPanY;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.colour = input.colour;
                output.uv = input.uv;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw + _Time.y * float2(_PanX, _PanY);
                float4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float opacity = lerp(sample.a, sample.r, _AlphaFromRed);
                float2 maskUV = input.uv * _MaskTex_ST.xy + _MaskTex_ST.zw + _Time.y * float2(_MaskPanX, _MaskPanY);
                opacity *= lerp(1.0, SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUV).r, _UseMask);
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
