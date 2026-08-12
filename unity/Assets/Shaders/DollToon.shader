// GFL2 character shading for MENACE, written against HDRP.
//
// The shading model is GFL2's own, not HDRP's. The reverse-engineered composite
// is
//
//   (directDiffuse * (albedo + specular)) * mainLightColor
//       + ambientDiffuse * occlusion + iblColor + emissive
//
// where directDiffuse is a ramp-atlas sample rather than a Lambert term. So this
// keeps its own arithmetic and takes the scene's lighting from HDRP: transforms,
// exposure, the light lists, the shadow maps and the pass structure.
//
// Two values in the game's shipped ShaderConfig make the pipeline's own headers
// necessary rather than optional:
//
//   CameraRelativeRendering = 1  world space in a shader has the camera at the
//                                origin, so hand-rolled absolute positions
//                                disagree with every HDRP pass, and disagree
//                                differently in a camera view than a shadow view
//   PreExposition = 1            HDRP scales lighting by a per-frame exposure
//                                multiplier before writing to the buffer, and
//                                anything that does not join in is mis-scaled
//                                against everything around it
//
// Units. HDRP's diffuse for a directional light is albedo * NdotL * lux / PI,
// and this shader is that with the ramp standing in for NdotL, so the two agree
// without a conversion constant. Every light's illuminance comes from HDRP's own
// light data, so there is no brightness trim and nothing to keep in sync with
// managed code.
//
// Display mapping is deliberately absent. MENACE runs a full HDRP post stack,
// tonemap included, over the whole frame. A tonemap or a grade inside one
// material would be applied at the wrong point in that chain, so this shader
// writes scene-referred radiance and lets the frame finish it.
Shader "Womenace/DollToon"
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
        // game mesh carries the set natively; a PMX source cannot, so
        // scripts/doll/transfer_hair_uv.py writes the game's own set into the
        // glTF from the character's dumped hair mesh, and the bake raises this
        // only for hair whose transfer has run. A non-zero value is also what
        // routes a material off GGX and onto the anisotropic hair term.
        _MatCapIntensity ("MatCap Intensity (needs a real UV1)", Range(0, 8)) = 0
        _MatCapUVOffset ("MatCap UV Offset", Range(-0.2, 0.2)) = 0.025
        _BaseTint ("Base Tint", Color) = (1, 1, 1, 1)

        // iblColor's intensity. 0.6038 is the value the captured frame runs it at.
        _IblIntensity ("IBL Intensity", Range(0, 4)) = 0.6038

        // FaceSDF. The face takes no N dot L at all: an authored threshold map,
        // swept by the light's compass angle around the character, supplies the
        // ramp's U instead. Off unless the material declares it, because the map
        // is meaningless on any surface but a face and a texture cannot be
        // detected from inside a shader.
        _UseBlendTex ("Use FaceSDF", Float) = 0
        _SdfMap ("Face SDF (R sweep, A selector)", 2D) = "black" {}
        _BlendSmoothness ("FaceSDF Penumbra", Range(0.001, 1)) = 0.1





    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }
        LOD 200

        HLSLINCLUDE
        #pragma target 4.5
        #include "DollToonLighting.hlsl"
        ENDHLSL

        // Depth and normal prepass for the surface. Without the depth the buffer has
        // no record of the model and every screen space effect reading it composites
        // as though nothing were there; without the normal, HDRP's occlusion has
        // nothing to integrate around and computes this surface's shadowing from
        // whatever another surface left behind.
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            // Matches the forward pass. Depth that records only front faces
            // disagrees with colour that draws both, and every effect reading
            // depth then composites against a surface that is not the one drawn.
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            // The pipeline sets these per frame, so they are multi_compile: a
            // shader_feature variant would not exist when it asked for one.
            // WRITE_NORMAL_BUFFER is on whenever the camera is in forward lit mode,
            // which is the mode these materials render in.
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 tangentWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            DepthVaryings vertDepth(Attributes input)
            {
                DepthVaryings output;
                output.positionCS =
                    TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(
                    TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            void fragDepth(DepthVaryings input, bool isFrontFace : SV_IsFrontFace
            #ifdef WRITE_MSAA_DEPTH
                , out float4 outDepthColour : SV_Target0
            #endif
            #ifdef WRITE_NORMAL_BUFFER
                , out float4 outNormalBuffer : DOLL_SV_TARGET_NORMAL_DEPTHPASS
            #endif
            )
            {
            #ifdef WRITE_MSAA_DEPTH
                outDepthColour = DollDepthAsColour(input.positionCS);
            #endif
            #ifdef WRITE_NORMAL_BUFFER
                DollWriteNormalBuffer(
                    DollShadingNormal(input.normalWS, input.tangentWS, input.uv, isFrontFace),
                    DollPerceptualRoughness(input.uv), outNormalBuffer);
            #endif
            }
            ENDHLSL
        }


        // Shadow caster. Without this pass the geometry is absent from every
        // shadow map, so a doll casts nothing onto the ground or onto itself.
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

            // No bias push on the caster, deliberately. HDRP's own shadow caster
            // does not offset either: it biases receiver-side, at sample time, as
            // normalWS * normalBias * worldTexelSize, so the offset scales with the
            // cascade's texel size and is only as large as that cascade needs. A
            // fixed bias here is the peter-panning recipe, and a 4 mm one was what
            // previously lifted the chin's shadow off the neck.
            float4 vertShadow(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
            }

            void fragShadow() {}
            ENDHLSL
        }

        // Object motion vectors, so the temporal passes know she moved. See
        // DollMotionVectors.hlsl for what the pass is for and what it reads.
        Pass
        {
            Name "MotionVectors"
            // The tag has to read exactly this. The engine keys the previous model
            // matrix and the deformation flags off the pass name.
            Tags { "LightMode" = "MotionVectors" }

            // Tag every pixel this pass covers. HDRP's full-screen camera motion
            // pass runs straight afterwards with Comp NotEqual against this same
            // bit, so an untagged pixel has its object motion overwritten by
            // camera-only motion and the whole pass comes to nothing. 32 is
            // StencilUsage.ObjectMotionVector, which HDRP keeps internal to itself.
            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            // Cull matches the forward pass, as the depth pass does, and for the
            // same reason. Depth is written here because HDRP takes a renderer with
            // motion vectors out of the depth prepass: excludeObjectMotionVectors
            // is set on that renderer list, so the prepass and this pass never both
            // run and this is the only prepass record of her depth.
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vertMotion
            #pragma fragment fragMotion
            #include "DollMotionVectors.hlsl"

            // This pass feeds the normal buffer too, because it is the pass that
            // replaced the prepass for this renderer. Its target index moves with the
            // frame's bindings, which is what these keywords resolve.
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile _ WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH

            // The surface attributes this pass needs, plus the previous-frame position
            // stream. Declared here rather than on the shared Attributes struct
            // because TEXCOORD4 is bound only while this pass is drawing.
            struct MotionAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float3 previousPositionOS : TEXCOORD4;
            };

            struct MotionVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 currentCS : TEXCOORD0;
                float4 previousCS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float2 uv : TEXCOORD4;
            };

            // The motion half comes from the shared vertex rather than a second copy
            // of it, so the two passes cannot drift apart on which projection is
            // jittered or which frame the previous position belongs to.
            MotionVaryings vertMotion(MotionAttributes input)
            {
                DollMotionAttributes motion;
                motion.positionOS = input.positionOS;
                motion.normalOS = input.normalOS;
                motion.previousPositionOS = input.previousPositionOS;
                DollMotionVaryings shared_ = DollMotionVertex(motion);

                MotionVaryings output;
                output.positionCS = shared_.positionCS;
                output.currentCS = shared_.currentCS;
                output.previousCS = shared_.previousCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(
                    TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            void fragMotion(MotionVaryings input, bool isFrontFace : SV_IsFrontFace
            #ifdef WRITE_MSAA_DEPTH
                , out float4 outDepthColour : SV_Target0
            #endif
                , out float4 outMotion : DOLL_SV_TARGET_MOTION
            #ifdef WRITE_NORMAL_BUFFER
                , out float4 outNormalBuffer : DOLL_SV_TARGET_NORMAL_MOTIONPASS
            #endif
            )
            {
                DollMotionVaryings motion;
                motion.positionCS = input.positionCS;
                motion.currentCS = input.currentCS;
                motion.previousCS = input.previousCS;
                outMotion = DollMotionValue(motion);

            #ifdef WRITE_MSAA_DEPTH
                outDepthColour = DollDepthAsColour(input.positionCS);
            #endif
            #ifdef WRITE_NORMAL_BUFFER
                DollWriteNormalBuffer(
                    DollShadingNormal(input.normalWS, input.tangentWS, input.uv, isFrontFace),
                    DollPerceptualRoughness(input.uv), outNormalBuffer);
            #endif
            }
            ENDHLSL
        }

        // Base colour. ForwardOnly is HDRP's pass for a material that does its
        // own shading instead of going through the deferred path.
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            // Off, as the game's main character pass is. The shells are open
            // geometry: a skirt, a sleeve and a hair card have insides that are
            // meant to be seen, and culling them drops that geometry entirely.
            // This is also what makes the SV_IsFrontFace flip below do anything.
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off


            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Shadow receiving, scoped to this pass. HDShadowAlgorithms selects a
            // filter per quality level and errors out if no level is defined for a
            // fragment stage, so these are the keywords HDRP's own shaders declare
            // and the pipeline sets. Declaring them rather than hardcoding a level
            // is what makes the dolls filter their shadows the way the rest of the
            // game does. The depth and caster passes have fragment stages too, so
            // this header must not sit in HLSLINCLUDE where it would reach them
            // without the keywords.
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            // Probe volumes, so IndirectDiffuse can reach them where the pipeline has
            // them. Set per frame by HDRP, hence multi_compile.
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #include "DollToonForward.hlsl"
            ENDHLSL
        }


    }
}
