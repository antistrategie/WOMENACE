// Womenace/DollToon for face skin, with GFL2's hair-to-face band. GFL2 marks the band through
// the stencil: the face tags its pixels, the hair's offset silhouette (Womenace/DollToonHair's
// HairShadow pass) marks the tagged pixels it covers, and the face is shaded twice, normally
// where unmarked and with the key shut off where marked. Here the face tags user bit 64 in the
// prepass (both the depth and the motion vector pass, because HDRP draws a skinned doll in only
// the latter), the hair sets bit 128, and this shader sits a queue after the hair so the mark is
// down before the face shades. Everything else is Womenace/DollToon's, through the same includes.
Shader "Womenace/DollToonFace"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        // 256x16 ramp atlas, four bands of four rows. Imports linear, not sRGB:
        // the gradients are already linear because the source atlas is RGBAHalf.
        _RampMap ("Ramp Atlas", 2D) = "white" {}
        // Tangent-space normal. Surface detail lives here, so without it every
        // material shades off the interpolated vertex normal and fabric reads as
        // a flat sheet.
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0, 2)) = 1
        // Packed R=roughness G=metallic B=occlusion, measured on the game's own
        // maps.
        // Black by default. A real RMO map never has zero occlusion (the game's
        // own measure 234-239 of 255), so an all-black mask is a safe sentinel
        // for "this material ships no RMO", which switches the GGX term off and
        // leaves occlusion at 1. Hair and face take other terms entirely.
        _MaskMap ("RMO (R rough, G metal, B occlusion)", 2D) = "black" {}
        // Some materials ship _smo rather than _rmo, whose R is smoothness: an
        // inverted roughness. The suffix cannot be read from inside a shader, so
        // whatever wires the map sets this alongside it.
        _MaskRoughnessInverted ("Mask R is smoothness (_smo)", Float) = 0
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        // Hair specular. Defaults to black so a material without an _spc map
        // contributes nothing, which is how the term stays off everywhere the
        // game does not enable _ANISOTROPIC_SPECULAR.
        _SpecularMap ("Hair Specular (_spc)", 2D) = "black" {}
        // Off by default: the streak samples _spc with the hair mesh's own
        // TEXCOORD1, and only real coordinates make it mean anything. A ripped
        // game mesh carries the set natively. A PMX source cannot, so
        // scripts/doll/transfer_hair_uv.py writes the game's own set into the
        // glTF from the character's dumped hair mesh, and the bake raises this
        // only for hair whose transfer has run. A non-zero value is also what
        // routes a material off GGX and onto the anisotropic hair term.
        _MatCapIntensity ("MatCap Intensity (needs a real UV1)", Range(0, 8)) = 0
        _MatCapUVOffset ("MatCap UV Offset", Range(-0.2, 0.2)) = 0.025
        _BaseTint ("Base Tint", Color) = (1, 1, 1, 1)

        // iblColor's intensity. 0.6038 is the value the captured frame runs it at.
        _IblIntensity ("IBL Intensity", Range(0, 4)) = 0.6038
        // How much of the key (directional) light reaches this renderer, 1 everywhere but where
        // the strategy diorama hands a doll its room's light probe occlusion of the sun.
        [HideInInspector] _KeyOcclusion ("Key Occlusion", Float) = 1
        // The way a window's light comes to a doll the strategy diorama lights by it (xyz), in
        // place of the sun's (w above a half).
        [HideInInspector] _KeyDirection ("Key Direction", Vector) = (0, 0, 0, 0)
        [HideInInspector] _KeyColour ("Key Colour", Vector) = (0, 0, 0, 0)
        [HideInInspector] _FaceRootUpright ("Face Root Upright", Float) = 0
        // Set by the strategy diorama on a doll standing in a GFL2 room: GFL2's own ramp and
        // ambient, and the room's reflection probe in place of the sky.
        [HideInInspector] _RoomLit ("Room Lit", Float) = 0
        [HideInInspector] _UseRoomReflection ("Use Room Reflection", Float) = 0
        [HideInInspector][NoScaleOffset] _RoomReflection ("Room Reflection", Cube) = "black" {}

        // FaceSDF. The face takes no N dot L at all: an authored threshold map,
        // swept by the light's compass angle around the character, supplies the
        // ramp's U instead. Off unless the material declares it, because the map
        // is meaningless on any surface but a face and a texture cannot be
        // detected from inside a shader.
        _UseBlendTex ("Use FaceSDF", Float) = 0
        _SdfMap ("Face SDF (R sweep, A selector)", 2D) = "black" {}
        _BlendSmoothness ("FaceSDF Penumbra", Range(0.001, 1)) = 0.1
        _FaceSpecIntensity ("Face Glint Intensity", Float) = 1
        _SpecUVYOffset ("Face Glint V Offset", Float) = 0





    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        LOD 200

        HLSLINCLUDE
        #pragma target 4.5
        #include "DollToonLighting.hlsl"
        ENDHLSL

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Stencil
            {
                Ref 64
                WriteMask 64
                Comp Always
                Pass Replace
            }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #include "DollDepthPass.hlsl"
            ENDHLSL
        }

        UsePass "Womenace/DollToon/SHADOWCASTER"

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            // 32 is HDRP's ObjectMotionVector bit, as in Womenace/DollToon, and 64 the face tag.
            Stencil
            {
                Ref 96
                WriteMask 96
                Comp Always
                Pass Replace
            }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vertMotion
            #pragma fragment fragMotion
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile _ WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #include "DollMotionPass.hlsl"
            ENDHLSL
        }

        // The face where the hair's silhouette did not mark it.
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Stencil
            {
                Ref 0
                ReadMask 128
                Comp Equal
            }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #include "DollToonForward.hlsl"
            ENDHLSL
        }

        // GFL2's GFCharFaceShadow: the marked pixels re-shaded with the key shut off.
        Pass
        {
            Name "FaceBand"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Stencil
            {
                Ref 128
                ReadMask 128
                Comp Equal
            }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #define DOLL_FACE_BAND
            #include "DollToonForward.hlsl"
            ENDHLSL
        }
    }
}
