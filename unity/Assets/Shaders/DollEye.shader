// GFL2's eyeball, for MENACE.
//
// The first of the three stacked eye materials, under the darkening and highlight
// layers. What makes it its own shader rather than the doll shader with an odd ramp
// is the parallax: the iris is painted flat but has to read as sitting behind a
// cornea, so its lookup slides against the view direction. Nothing else on the doll
// does that.
//
// Transcribed from the decompiled fragment. Two separate offsets, which is easy to
// miss: the iris and the specular map slide by different amounts, _CorneaParallax
// and _SpecularParallax, both 0.3 in the captured material but independent knobs.
//
// The ambient term is the game's NormalizeSH. It divides the probe by its own
// luminance and rescales by the luminance of the probe's DC term, which keeps the
// ambient's hue while throwing away its directionality. On an eyeball that matters:
// a directional ambient across a sphere reads as a second, wrong light source.
Shader "Womenace/DollEye"
{
    Properties
    {
        _BaseMap ("Iris / Eyeball", 2D) = "white" {}
        _BaseTint ("Main Colour", Color) = (1, 1, 1, 1)

        // Black by default, so an eye that ships no specular map contributes no
        // highlight rather than a white one. Makiatto ships none: her glint comes
        // from the additive eyeblend layer instead.
        _SpecularMap ("Specular Map", 2D) = "black" {}
        _SpecularIntensity ("Specular Intensity", Range(0, 3)) = 1.5

        // How far the lookups slide against the view. The iris reads as depth behind
        // the cornea; the specular slides separately so a highlight can sit on the
        // cornea's surface rather than down at the iris.
        _CorneaParallax ("Cornea Parallax", Range(0, 0.5)) = 0.3
        _SpecularParallax ("Specular Parallax", Range(0, 1)) = 0.3

        // A floor under N dot L, so an eye never goes fully dark. At 0.25 a quarter of
        // the lit value survives with the light behind her.
        _ShadowIntensity ("Shadow Intensity", Range(0, 1)) = 0.25
    }

    SubShader
    {
        // Geometry+14, as the game has it: after the face, before the eyeblend
        // layers at +458 and +459 that composite over it.
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+14"
        }

        // Object motion vectors, matching the body's. See DollMotionVectors.hlsl.
        // The parallax slide above does not enter into it: it moves where the iris
        // is sampled from, not where the eyeball is, and a motion vector describes
        // the surface rather than the texture on it.
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

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vertMotion
            #pragma fragment fragMotion
            #include "DollMotionVectors.hlsl"
            #include "DollNormalBuffer.hlsl"

            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile _ WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH

            struct MotionVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 currentCS : TEXCOORD0;
                float4 previousCS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            MotionVaryings vertMotion(DollMotionAttributes input)
            {
                DollMotionVaryings shared_ = DollMotionVertex(input);

                MotionVaryings output;
                output.positionCS = shared_.positionCS;
                output.currentCS = shared_.currentCS;
                output.previousCS = shared_.previousCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            void fragMotion(MotionVaryings input
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
                // The eyeball carries no normal map, so the interpolated vertex
                // normal is the whole of its shading normal. Cull is Back here, so
                // there is no back face to turn around. Roughness 1: an eyeball has
                // no RMO map, and a mirror-smooth sphere would have screen-space
                // reflection chasing a highlight across it.
                DollWriteNormalBuffer(normalize(input.normalWS), 1.0, outNormalBuffer);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_SpecularMap);
            SAMPLER(sampler_SpecularMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseTint;
                float _SpecularIntensity;
                float _CorneaParallax;
                float _SpecularParallax;
                float _ShadowIntensity;
            CBUFFER_END

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
                float3 normalWS : TEXCOORD1;
                float3 positionRWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionRWS = positionRWS;
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            float4 frag(Varyings input) : SV_TARGET
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionRWS);

                // The slide is the view direction in the eye's own frame, whose x and
                // y line up with the iris texture's. Object space carries the rig's
                // half turn about X and the importer's mirrored X, both undone here
                // exactly as the face sweep undoes them, so the iris tracks the camera
                // rather than tracking it backwards.
                float2 slide = -TransformWorldToObjectDir(viewDirWS, false).xy;

                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    input.uv + slide * _CorneaParallax).rgb * _BaseTint.rgb;
                float3 specular = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap,
                    input.uv + slide * _SpecularParallax).rgb * _SpecularIntensity;

                float3 lightWS = float3(0.0, 1.0, 0.0);
                float3 keyColour = 0.0;
                if (_DirectionalLightCount > 0)
                {
                    lightWS = -_DirectionalLightDatas[0].forward;
                    keyColour = _DirectionalLightDatas[0].color;
                }
                float ndotl = saturate(dot(normalWS, lightWS));

                // N dot L with a floor, so the eye keeps a quarter of its value with
                // the key behind her instead of going black.
                float shade = ndotl * (1.0 - _ShadowIntensity) + _ShadowIntensity;
                float3 direct = (albedo * shade + specular * ndotl) * keyColour / PI;

                // NormalizeSH: keep the probe's hue, replace its magnitude with the
                // average. Without this an eyeball picks up the ambient's direction
                // across its sphere and reads as lit by a second source.
                const float3 lumWeights = float3(0.2126, 0.7152, 0.0722);
                float3 sh = max(0.0, EvaluateAmbientProbe(normalWS));
                float3 flat3 = max(0.0, EvaluateAmbientProbeL0());
                float3 ambient = sh / max(1e-4, dot(sh, lumWeights)) * dot(flat3, lumWeights);

                float3 lit = direct + albedo * ambient;
                return float4(lit * GetCurrentExposureMultiplier(), 1.0);
            }
            ENDHLSL
        }
    }
}
