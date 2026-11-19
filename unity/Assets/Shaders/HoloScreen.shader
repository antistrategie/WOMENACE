// A screen-filling hologram: draws a texture (another camera's render) additively over the room,
// so black is transparent and lit content adds light, tinted and lightly scanlined. Used for the
// strategy diorama's holotable, where the system map camera renders into the texture at full
// screen size: its projection stays the vanilla one, so the map's planet labels and clicks line
// up with the hologram without any remapping. Backdrop darkens the room behind lit content (alpha
// from the content's brightness), so faint orbits and labels stay readable over a bright room. At 0
// the blend is purely additive. The holotable's planet projection uses the same shader on a quad
// standing over the table, depth tested so dolls in front of it hide it. Texture Rect picks a part
// of the texture for a quad, for several quads that share one texture (the mission cards), and
// Reveal scans a quad in from its left edge, with a plain bright edge or a ragged one. A mesh's
// vertex colours scale what it draws, so parts of one mesh can be lit apart, and their alpha
// scales its plate, so a part can be left out altogether. A mesh without them draws as it is.
// Cut By Alpha is for a picture with an alpha channel of its own, whose clear parts must add no
// light.
Shader "Womenace/HoloScreen"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Screen", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.75, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _ScanLines ("Scan Lines (per screen height)", Float) = 360
        _ScanDepth ("Scan Line Depth", Range(0, 1)) = 0.18
        _Backdrop ("Backdrop", Float) = 0
        _EdgeFade ("Edge Fade (fraction of the quad)", Range(0, 0.5)) = 0
        _UvRect ("Texture Rect (scale xy, offset zw)", Vector) = (1, 1, 0, 0)
        _Plate ("Plate (darkens the room behind what the texture drew)", Range(0, 1)) = 0
        _Reveal ("Reveal (scanned in from the left, 0 to 1)", Range(0, 1)) = 1
        _RevealRagged ("Reveal Ragged (the scan's edge broken into bands with a glow behind it)", Range(0, 1)) = 0
        _RevealGlow ("Reveal Glow (the colour trailing a ragged scan)", Color) = (0.35, 0.9, 1, 1)
        _RevealBands ("Reveal Bands (how many a ragged scan's edge is broken into)", Float) = 54
        _CutByAlpha ("Cut By Alpha (a picture with colour left in its clear parts)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+100" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Intensity;
                float _ScanLines;
                float _ScanDepth;
                float _Backdrop;
                float _EdgeFade;
                float4 _UvRect;
                float _Plate;
                float _Reveal;
                float _RevealRagged;
                float4 _RevealGlow;
                float _RevealBands;
                float _CutByAlpha;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv * _UvRect.xy + _UvRect.zw);
                // A picture's clear parts may still hold colour (an image file's, or what its
                // compression left there), which would be added as light. Cut By Alpha drops it.
                float3 c = texel.rgb * lerp(1.0, texel.a, _CutByAlpha);
                float scan = 1.0 - _ScanDepth * step(0.5, frac(input.uv.y * _ScanLines));
                float2 edge = min(input.uv, 1.0 - input.uv);
                float fade = _EdgeFade > 0 ? smoothstep(0.0, _EdgeFade, edge.x) * smoothstep(0.0, _EdgeFade, edge.y) : 1.0;
                float3 colour = c * input.color.rgb * _Tint.rgb * _Intensity * scan * fade * GetCurrentExposureMultiplier();
                colour = max(0.0, colour);
                colour = (colour == colour) ? colour : 0.0;
                // The plate follows the texture's own alpha: a panel drawn into the texture darkens
                // the room behind itself, and the empty texture round it darkens nothing.
                float alpha = max(saturate(dot(c, float3(0.2126, 0.7152, 0.0722)) * _Backdrop), _Plate * fade * saturate(texel.a * 1.5) * input.color.a);
                // Scanned in from the left: the quad shows up to the scan's place, behind a bright
                // edge that is gone once the scan is through.
                if (_Reveal < 1.0)
                {
                    // Ragged, the edge is broken into bands down the quad, each a little ahead of
                    // or behind the scan and flickering, with a glow of its own colour trailing it
                    // over what has just been shown and a thin bright line at the very front.
                    float band = floor(input.uv.y * _RevealBands);
                    // The frame count is wrapped so the sine's argument stays small enough for
                    // the hash to keep its float precision however long the game has run.
                    float frame = fmod(floor(_Time.y * 24.0), 1024.0);
                    float lot = frac(sin(band * 12.9898 + frame * 4.1414) * 43758.5453);
                    float there = step(0.001, _Reveal);
                    float front = _Reveal + (lot - 0.5) * 0.07 * _RevealRagged * there;
                    float shown = step(input.uv.x, front);
                    float plain = saturate(1.0 - abs(input.uv.x - front) / 0.02) * there;
                    float line_ = saturate(1.0 - abs(input.uv.x - front) / 0.004) * there;
                    float trail = saturate(1.0 - (front - input.uv.x) / (0.05 + 0.1 * lot)) * shown * there;
                    float3 glow = lerp(_Tint.rgb * 2.0 * plain, _RevealGlow.rgb * (trail * trail * 1.6) + float3(1.0, 1.0, 1.0) * (line_ * 2.5), _RevealRagged);
                    colour = colour * shown + glow * (_Intensity * GetCurrentExposureMultiplier());
                    alpha *= shown;
                }
                return float4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
