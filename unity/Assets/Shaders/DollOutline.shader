// GFL2's inverted-hull outline, for MENACE.
//
// The same geometry drawn again with front faces culled, so back faces show only
// where the expansion pushes them outside the silhouette. Its own material rather
// than a pass on the surface shader, because HDRP draws only the first pass carrying
// a given LightMode tag: the outline pass that lived on the surface shader compiled,
// shipped, and never once executed.
//
// Transcribed from the game's GFOutline vertex program. The important part, and the
// part easiest to get wrong, is that the offset is applied in **clip space**, not by
// moving the vertex in the world. The game projects the vertex, then slides the
// projected position sideways, bounded to between roughly 1.2 and 2.5
// resolution-scaled pixels. A world-space extrude instead displaces geometry in
// three dimensions, which lets a head's hull cross into hair in front of it and draw
// the face's silhouette through it. Sliding a projected position cannot do that,
// because nothing moves in depth.
//
// Two substitutions, both forced by the source. The game reads a smoothed normal
// from vertex colour RGB and a per-vertex width from vertex colour A, tapering to
// zero at hair strand tips and at mesh boundaries. A PMX port carries neither, so
// this uses the mesh normal and a width of 1. The visible cost is that the hull can
// tear at a hard normal seam, which is why the face is not outlined yet.
Shader "Womenace/DollOutline"
{
    Properties
    {
        // Sampled so the contour can carry the surface's own colour, as the game's
        // fragment does. Bound by the bake from the source material.
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseTint ("Base Tint", Color) = (1, 1, 1, 1)

        // A multiplier over a fixed base thickness of 1/1920 m, not a distance. The
        // game's observed range is 0-3, at 3.0 for hair and cloth and 0.75 for the
        // face.
        _OutlineWidth ("Outline Width (0-3)", Range(0, 3)) = 3
        _OutlineColour ("Outline Colour", Color) = (0.01, 0.01, 0.01, 1)
        _OutlineShadowColour ("Outline Shadow Colour", Color) = (0.011, 0.009, 0.007, 1)
        _OutlineIntensity ("Outline Intensity", Range(0, 4)) = 1

        // Pushes the hull away from the camera in clip space. On a closed shell this
        // can sit at the game's zero, because back faces are already behind the front
        // faces and lose the depth test wherever the surface covers them. Open
        // geometry has no such luck: a hair card's back face is the same surface at
        // the same depth, so without a nudge the hull ties the depth test and can
        // paint over the card it belongs to.
        _OutlineZBias ("Outline Z Bias", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #pragma target 4.5

        // ShaderVariables carries the light lists and the light data structs along
        // with the transforms, so nothing else needs including for those.
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseTint;
            float _OutlineWidth;
            float4 _OutlineColour;
            float4 _OutlineShadowColour;
            float _OutlineIntensity;
            float _OutlineZBias;
        CBUFFER_END

        // The player's outline switch. A global rather than a material property,
        // so one write at load reaches every outline draw in the game, and
        // inverted so an unset global, in any context that never runs the mod's
        // settings code, leaves outlines on. Both passes collapse on it: a hull
        // whose colour pass vanished while its motion pass still drew would be
        // invisible geometry writing depth.
        float _WomenaceOutlinesOff;

        // A single clipped point: zero-area triangles outside the depth range,
        // so a disabled hull rasterises nothing.
        float4 CollapseIfDisabled(float4 clip)
        {
            return _WomenaceOutlinesOff > 0.5 ? float4(0.0, 0.0, 2.0, 1.0) : clip;
        }

        // The expansion, returned in normalised device space so that one offset can
        // be applied to any clip position by scaling it with that position's own w.
        // The motion vector pass needs it that way: it applies the same offset to two
        // clip positions with two different w values.
        float2 OutlineOffsetNDC(float3 positionRWS, float3 normalWS)
        {
            // World-space thickness: offset by 1/1920 m along the view-space normal,
            // project both, take the delta. clipB.w is this vertex's projected w,
            // which is what turns a clip-space delta into a device-space one.
            float3 positionVS = TransformWorldToView(positionRWS);
            float3 nVS = normalize(TransformWorldToViewDir(normalWS));
            float4 clipA = mul(UNITY_MATRIX_P, float4(positionVS + nVS * (1.0 / 1920.0), 1.0));
            float4 clipB = mul(UNITY_MATRIX_P, float4(positionVS, 1.0));
            float2 delta = (clipA.xy - clipB.xy) * 1.3 * _OutlineWidth / max(1e-6, clipB.w);

            // Pixel floor and cap, so the contour stays between roughly 1.2 and 2.5
            // resolution-scaled pixels: world-proportional in the mid range, floored
            // at distance, capped close up.
            float3 nCS = normalize(mul(UNITY_MATRIX_VP, float4(normalWS, 0.0)).xyz);
            float2 px = 2.0 / _ScreenSize.xy;
            float2 lo = px * nCS.xy * 1.2;
            float2 hi = px * nCS.xy * (_ScreenSize.y / 1080.0) * 2.5;
            return min(abs(hi), max(abs(lo), abs(delta))) * sign(nCS.xy);
        }

        // Reverse-Z, so subtracting pushes the hull further away.
        void OutlinePushBack(inout float4 clip) { clip.z -= _OutlineZBias * 0.001; }
        ENDHLSL

        // Object motion vectors for the contour. Without this the rim keeps
        // camera-only motion while the body behind it carries its own, and the two
        // disagree along exactly the silhouette, which is where it shows most.
        // See DollMotionVectors.hlsl.
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vertMotion
            #pragma fragment DollMotionFragment
            #include "DollMotionVectors.hlsl"

            DollMotionVaryings vertMotion(DollMotionAttributes input)
            {
                DollMotionVaryings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                float2 offsetNDC = OutlineOffsetNDC(positionRWS, normalWS);

                // Expansion and push-back both reproduced, and nothing else applied,
                // so the depth written here is exactly the depth the forward pass
                // writes. Miss either and this pass records a different hull from the
                // visible one; add anything to it and the forward pass fails its own
                // depth test, because a renderer with motion vectors is dropped from
                // the depth prepass and this is the depth it tests against.
                float4 clip = TransformWorldToHClip(positionRWS);
                clip.xy += offsetNDC * clip.w;
                OutlinePushBack(clip);
                output.positionCS = CollapseIfDisabled(clip);

                output.currentCS = mul(UNITY_MATRIX_UNJITTERED_VP, float4(positionRWS, 1.0));
                output.currentCS.xy += offsetNDC * output.currentCS.w;

                if (DollMotionSuppressed())
                {
                    output.previousCS = float4(0.0, 0.0, 0.0, 1.0);
                }
                else
                {
                    // The same offset goes on both positions, so the expansion cancels
                    // out of the difference and what survives is the surface's own
                    // motion. Measuring an expanded position against an unexpanded one
                    // would instead report the expansion, all one to two pixels of it,
                    // as movement every frame.
                    //
                    // It is the current frame's offset in both places. Rebuilding it
                    // against last frame's view would need the previous view and
                    // projection separately and HDRP exposes only their product; the
                    // two differ by however far the normal turned on screen in one
                    // frame, a fraction of the pixel or two the offset is bounded to.
                    output.previousCS = mul(UNITY_MATRIX_PREV_VP,
                        float4(DollPreviousPositionRWS(input), 1.0));
                    output.previousCS.xy += offsetNDC * output.previousCS.w;
                }
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

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
                float3 positionOS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));

                float4 clip = TransformWorldToHClip(positionRWS);
                clip.xy += OutlineOffsetNDC(positionRWS, normalWS) * clip.w;
                OutlinePushBack(clip);

                output.positionCS = CollapseIfDisabled(clip);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float4 frag(Varyings input) : SV_TARGET
            {
                // The contour samples the albedo and tints it, choosing between two
                // colours by where the vertex sits relative to the light around the
                // character's vertical axis, both in object space.
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;

                float3 lightWS = float3(0.0, 1.0, 0.0);
                float3 keyColour = 0.0;
                if (_DirectionalLightCount > 0)
                {
                    lightWS = -_DirectionalLightDatas[0].forward;
                    keyColour = _DirectionalLightDatas[0].color;
                }

                float d = 0.5;
                float2 pxz = input.positionOS.xz;
                float2 lxz = TransformWorldToObjectDir(lightWS).xz;
                if (dot(pxz, pxz) > 1e-8 && dot(lxz, lxz) > 1e-8)
                    d = dot(normalize(pxz), normalize(lxz)) * 0.5 + 0.5;

                float3 tint = lerp(_OutlineShadowColour.rgb, _OutlineColour.rgb, d);
                float3 colour = albedo * _BaseTint.rgb * tint * _OutlineIntensity;

                // The game closes with a multiply by the main light colour, so the
                // contour tracks the scene rather than sitting at a fixed level. The
                // divide by PI is the same illuminance-to-radiance step the surface
                // uses, and the exposure multiply puts it in the pre-exposed buffer
                // everything else is written into.
                return float4(colour * keyColour / PI * GetCurrentExposureMultiplier(), 1.0);
            }
            ENDHLSL
        }
    }
}
