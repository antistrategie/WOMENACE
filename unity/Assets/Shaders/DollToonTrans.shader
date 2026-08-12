// The transparent variant of Womenace/DollToon: the same GFL2 shading with the
// albedo's alpha carried through to a SrcAlpha blend. GFL2 ships this split the
// same way, uber beside ubertrans, and routes a material to the trans variant
// when its _da albedo actually carries coverage.
//
// First user: the mech's logo decals, whose sheet measures 88.6% fully
// transparent with a 9% soft fringe, too soft for a cutout. The same shader is
// the route for any future doll whose outfit carries real transparency.
//
// What a transparent surface deliberately does not have here:
//
//   depth prepass    a blended pixel has no single depth, so nothing is
//                    recorded and screen-space effects composite against
//                    whatever sits behind. For a decal that is the surface it
//                    floats on, which is exactly right. For open sheer cloth
//                    it is the accepted cost, the same one the game pays.
//   motion vectors   same reasoning. A decal's pixels take the chassis's
//                    motion, which is correct because the decal moves with it.
//   outline          the game draws no outline through transparency.
//
// The shadow caster stays, alpha-clipped, so an opaque-enough region still
// blocks light while the clear sheet around it casts nothing.
Shader "Womenace/DollToonTrans"
{
    Properties
    {
        _BaseMap ("Base Map (alpha = coverage)", 2D) = "white" {}
        // 256x16 ramp atlas, four bands of four rows. Imports linear, not sRGB:
        // the gradients are already linear because the source atlas is RGBAHalf.
        _RampMap ("Ramp Atlas", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0, 2)) = 1
        _MaskMap ("RMO (R rough, G metal, B occlusion)", 2D) = "black" {}
        _MaskRoughnessInverted ("Mask R is smoothness (_smo)", Float) = 0
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _SpecularMap ("Hair Specular (_spc)", 2D) = "black" {}
        _MatCapIntensity ("MatCap Intensity (needs a real UV1)", Range(0, 8)) = 0
        _MatCapUVOffset ("MatCap UV Offset", Range(-0.2, 0.2)) = 0.025
        _BaseTint ("Base Tint", Color) = (1, 1, 1, 1)
        _IblIntensity ("IBL Intensity", Range(0, 4)) = 0.6038
        _UseBlendTex ("Use FaceSDF", Float) = 0
        _SdfMap ("Face SDF (R sweep, A selector)", 2D) = "black" {}
        _BlendSmoothness ("FaceSDF Penumbra", Range(0.001, 1)) = 0.1
        // Below this coverage a texel casts no shadow. Half, because the sheet
        // around a decal is fully clear and the mark itself nearly opaque.
        _ShadowClip ("Shadow Caster Clip", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }
        LOD 200

        HLSLINCLUDE
        #pragma target 4.5
        #include "DollToonLighting.hlsl"
        float _ShadowClip;
        ENDHLSL

        // Shadow caster, alpha-clipped. Without the clip the whole decal sheet
        // would shadow as a solid quad, which is worse than no shadow at all.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            // Off, as the game's shadow caster is.
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            ShadowVaryings vertShadow(Attributes input)
            {
                ShadowVaryings output;
                output.positionCS =
                    TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            void fragShadow(ShadowVaryings input)
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _ShadowClip);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            // Off for the same reason as the opaque variant: sheer cloth has
            // insides that are meant to be seen. For a decal shell it is
            // harmless, the back face hides inside the surface it floats on.
            Cull Off
            // No depth write: a blended pixel has no single depth to record,
            // and the surface behind, which does write depth, keeps ZTest
            // meaningful here.
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // The same pass-scoped keyword sets the opaque forward pass declares.
            // A #pragma inside an include is invisible to Unity, so they repeat here.
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2

            #define DOLL_FORWARD_TRANSPARENT
            #include "DollToonForward.hlsl"
            ENDHLSL
        }
    }
}
