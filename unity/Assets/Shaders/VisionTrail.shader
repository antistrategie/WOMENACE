Shader "Womenace/VisionTrail"
{
    // Nemesis's foresight ribbons: a strip of purple smoke along an enemy's planned path, or
    // from an attacker to the unit it means to hit. UV0.x runs along the strip in metres and
    // UV0.y across it; UV1.x is the position along the strip from 0 to 1. Noise flows toward
    // the far end, the edges are soft, and both ends fade in.
    Properties
    {
        _NoiseTex ("Smoke noise", 2D) = "grey" {}
        [HDR] _Color ("Colour", Color) = (1.1, 0.45, 2.0, 1)
        _FlowSpeed ("Flow speed", Float) = 0.6
        _NoiseScale ("Noise scale", Float) = 0.45
        _Alpha ("Opacity", Range(0, 1)) = 0.85
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
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST, _Color;
                float _FlowSpeed, _NoiseScale, _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 along : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float along : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv;
                output.along = input.along.x;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 uv = float2(input.uv.x * _NoiseScale - t, input.uv.y * 0.6);
                float a = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r;
                float b = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * 2.1 + float2(-t * 0.7, 0.37)).r;
                float smoke = saturate(a * 0.7 + b * 0.45);

                // Soft across the width, with the noise eating into the edges.
                float across = 1.0 - abs(input.uv.y * 2.0 - 1.0);
                float edge = saturate((across - (1.0 - smoke) * 0.55) / 0.35);
                float ends = smoothstep(0.0, 0.08, input.along) * smoothstep(1.0, 0.85, input.along);

                float opacity = saturate(edge * (0.35 + smoke * 0.8) * ends * _Alpha);
                float3 colour = _Color.rgb * (0.6 + smoke * 0.7);
                return float4(colour * opacity, opacity);
            }
            ENDHLSL
        }
    }
}
