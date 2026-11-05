Shader "Womenace/VisionText"
{
    // Divination's foresight labels, drawn in the visions' style over a quad mesh the runtime
    // builds. The glyph atlas (scripts/vfx/make_vision_glyphs.py) is a signed distance field that
    // gives crisp purple letters, and a soft halo around them is eaten by
    // the same rising world-space noise the afterimages use, so the text reads as smoke. Drawn
    // over everything, since a label is read rather than placed in the scene.
    Properties
    {
        _MainTex ("Font atlas", 2D) = "white" {}
        _NoiseTex ("Smoke noise", 2D) = "grey" {}
        [HDR] _FaceColor ("Letter colour", Color) = (1.6, 1.1, 2.4, 1)
        [HDR] _GlowColor ("Halo colour", Color) = (0.9, 0.3, 1.8, 1)
        _FaceSoftness ("Letter edge softness", Range(0.005, 0.2)) = 0.05
        _HaloReach ("Halo reach", Range(0, 0.5)) = 0.32
        _OutlineReach ("Dark outline reach", Range(0, 0.5)) = 0.13
        [HDR] _OutlineColor ("Dark outline colour", Color) = (0.06, 0.01, 0.12, 1)
        _NoiseScale ("Noise scale", Float) = 3
        _RiseSpeed ("Rise speed", Float) = 0.5
    }
    SubShader
    {
        // HDRP only draws its forward transparent pass within the transparent queue range, so the
        // label sits at its top rather than in Overlay (4000), which HDRP never draws. ZTest Always
        // keeps it in front of everything all the same.
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+50" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _NoiseTex_ST, _FaceColor, _GlowColor, _OutlineColor;
                float _FaceSoftness, _HaloReach, _OutlineReach, _NoiseScale, _RiseSpeed;
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
                float3 positionRWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionRWS);
                output.colour = input.colour;
                output.uv = input.uv;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                // The atlas keeps the distance field in alpha, 0.5 on the glyph edge.
                float sdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                float face = smoothstep(0.5 - _FaceSoftness, 0.5 + _FaceSoftness, sdf);

                float3 p = GetAbsolutePositionWS(input.positionRWS);
                float2 rise = float2(0, -_Time.y * _RiseSpeed);
                float smoke = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xy * _NoiseScale + rise).r;
                smoke = saturate(smoke * 0.75 + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.zy * _NoiseScale * 1.9 + rise * 1.6).r * 0.4);

                // A dark outline hugs the letters so they hold up on snow and bright ground, and the
                // smoky halo beyond it fades out and breaks up in the noise.
                float outline = smoothstep(0.5 - _OutlineReach, 0.5 - _OutlineReach * 0.4, sdf) * (1.0 - face);
                float halo = smoothstep(0.5 - _HaloReach, 0.5 - _OutlineReach, sdf) * (1.0 - face) * (1.0 - outline);
                halo *= saturate((smoke - 0.25) / 0.5);

                float pulse = 0.9 + 0.1 * sin(_Time.y * 2.1);
                float3 colour = _FaceColor.rgb * face * (0.85 + smoke * 0.3) + _OutlineColor.rgb * outline * 0.9 + _GlowColor.rgb * halo * 0.8;
                float opacity = saturate(face + outline * 0.9 + halo * 0.6) * input.colour.a;
                return float4(colour * input.colour.a * pulse, opacity);
            }
            ENDHLSL
        }
    }
}
