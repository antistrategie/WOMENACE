// Womenace/DollToon for hair, with GFL2's GFHairShadow pass added: the hair's silhouette, slid down
// a few millimetres, marks the face pixels Womenace/DollToonFace re-shades as the hair-to-face
// band. Its own shader so that no other material pays for the extra pass. The surface passes are
// Womenace/DollToon's own.
Shader "Womenace/DollToonHair"
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
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }
        LOD 200

        HLSLINCLUDE
        #pragma target 4.5
        #include "DollToonLighting.hlsl"
        ENDHLSL

        UsePass "Womenace/DollToon/DEPTHFORWARDONLY"
        UsePass "Womenace/DollToon/SHADOWCASTER"
        UsePass "Womenace/DollToon/MOTIONVECTORS"
        UsePass "Womenace/DollToon/FORWARDONLY"

        // GFL2's GFHairShadow: the hair redrawn 7.5 mm down and up to 4.5 mm sideways with the key,
        // into the stencil only. Its depth test runs against the prepass, so only hair in front of
        // the face counts, and it marks only pixels the face's own prepass tagged (bit 64, a user
        // bit HDRP leaves alone), setting bit 128 for Womenace/DollToonFace's band pass. The face
        // shader sits a queue later, so the mark is down before the face shades. HDRP draws
        // SRPDefaultUnlit in its forward opaque list.
        Pass
        {
            Name "HairShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Stencil
            {
                Ref 192
                ReadMask 64
                WriteMask 128
                Comp Equal
                Pass Replace
            }
            Cull Off
            ZWrite Off
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertHairShadow
            #pragma fragment fragHairShadow

            float4 vertHairShadow(Attributes input) : SV_POSITION
            {
                float3 L, colour;
                GetKeyLight(L, colour);
                // GFL2's offsets are world metres on a character at scale 1.
                float scale = length(UNITY_MATRIX_M._m00_m10_m20);
                float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz)
                    + float3(-0.0045 * L.x, -0.0075, 0.0) * scale;
                return TransformWorldToHClip(positionRWS);
            }

            void fragHairShadow() {}
            ENDHLSL
        }

    }
}
