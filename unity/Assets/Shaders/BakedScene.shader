// GFL2's scenery shading, for the strategy diorama's rooms: gf_shader/scene/pbr_env_standard and
// env_unlit as their compiled D3D11 programs compute it (recovered from the GFL2 client).
//
// pbr_env_standard, per pixel:
//   _SMO: R smoothness, G metallic, B occlusion, A emission mask (each scaled by its material value).
//   diffuse colour 0.96 * (1 - metallic) * albedo, specular colour f0 = lerp(0.04, albedo, metallic).
//   baked:  lightmap * occlusion * diffuse colour, darkened where a live caster shadows the sun.
//           A renderer GFL2 does not lightmap reads its light probes' SH instead.
//           (GFL2's own "player shadow" does this for its characters.)
//   sun:    (diffuse colour + f0 * GGX) * sun colour * NdotL * min(live shadow, shadowmask red).
//           No occlusion on direct light. Live lights (GFL2's Realtime and Mixed spots and points)
//           shade the same way with URP's attenuation.
//   probe:  the renderer's reflection probe at mip perceptualRoughness * 6, times Karis's
//           environment BRDF approximation, times occlusion, dimmed where the surface's baked
//           light is dark. Both BRDFs take a roughness widened by the normal's screen-space
//           change (geometric specular anti-aliasing).
//   emission: _SMO alpha * _EmissionColor * albedo.
//   fog:    GFL2's linear fog (see ApplyFog).
// env_unlit: _BaseMap * _BaseColor * _GlowIntensity, fogged the same way.
//
// MENACE's build ships neither lightmap variants nor HDRP/Lit's emissive-map variant, so the mod
// carries this shader with every variant it needs.
Shader "Womenace/BakedScene"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        [HDR] _BaseColor ("Albedo Tint", Color) = (1, 1, 1, 1)
        [Toggle] _Unlit ("Unlit (env_unlit)", Float) = 0
        _GlowIntensity ("Unlit Glow Intensity", Float) = 1
        [NoScaleOffset] _Lightmap ("Baked Lighting (UV1, linear HDR)", 2D) = "white" {}
        // A prop with no lightmap (the Dinergate) stands in a flat level of the room's light.
        _LightmapScale ("Baked Lighting Scale", Float) = 1
        // Where the sun reaches past the room's own geometry (UV1, red).
        [NoScaleOffset] _ShadowMask ("Shadowmask (UV1)", 2D) = "white" {}
        [NoScaleOffset] _SMO ("SMO (R smoothness, G metallic, B occlusion, A emission)", 2D) = "white" {}
        _Smoothness ("Smoothness", Float) = 1
        _Metallic ("Metallic", Float) = 0
        _OcclusionStrength ("Occlusion Strength", Float) = 1
        // GFL2's two-channel normal map, read raw (x = R * A, y = G). 0 is a flat surface.
        [NoScaleOffset] _NormalMap ("Normal Map (AG)", 2D) = "grey" {}
        _BumpScale ("Normal Scale", Float) = 0
        [HDR] _EmissionColor ("Emission Colour", Color) = (0, 0, 0, 0)
        // A prop's own glow map, added on top (the Dinergate's eye). Not a GFL2 scenery term.
        [NoScaleOffset] _GlowMap ("Glow Map", 2D) = "black" {}
        [HDR] _GlowColor ("Glow Colour", Color) = (0, 0, 0, 0)
        // A renderer GFL2 does not lightmap takes its light probes' SH (Unity's unity_SHAr..SHC
        // constants) and sun occlusion in place of the lightmap and shadowmask.
        [Toggle] _UseProbes ("Light Probes Instead Of Lightmap", Float) = 0
        _ProbeSH0 ("SHAr", Vector) = (0, 0, 0, 0)
        _ProbeSH1 ("SHAg", Vector) = (0, 0, 0, 0)
        _ProbeSH2 ("SHAb", Vector) = (0, 0, 0, 0)
        _ProbeSH3 ("SHBr", Vector) = (0, 0, 0, 0)
        _ProbeSH4 ("SHBg", Vector) = (0, 0, 0, 0)
        _ProbeSH5 ("SHBb", Vector) = (0, 0, 0, 0)
        _ProbeSH6 ("SHC", Vector) = (0, 0, 0, 0)
        _ProbeOcclusion ("Probe Sun Occlusion", Float) = 1
        [NoScaleOffset] _ReflectionCube ("Reflection Probe", Cube) = "black" {}
        _ReflectionIntensity ("Reflection Probe Intensity", Float) = 1
        [HideInInspector] _CullMode ("Cull", Float) = 2
        // Opaque by default. GFL2's alpha-blended materials (glass) blend SrcAlpha/OneMinusSrcAlpha
        // with no depth write, in the transparent queue.
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("Z Write", Float) = 1
        // Alpha-tested materials (floor decals) cut out below this, in every pass. 0 keeps every pixel.
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        #include "DollNormalBuffer.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_SMO); SAMPLER(sampler_SMO);
        TEXTURECUBE(_ReflectionCube); SAMPLER(sampler_ReflectionCube);
        TEXTURE2D(_Lightmap); SAMPLER(sampler_Lightmap);
        TEXTURE2D(_ShadowMask); SAMPLER(sampler_ShadowMask);
        TEXTURE2D(_GlowMap); SAMPLER(sampler_GlowMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Unlit;
            float _GlowIntensity;
            float _LightmapScale;
            float _Smoothness;
            float _Metallic;
            float _OcclusionStrength;
            float _BumpScale;
            float4 _EmissionColor;
            float4 _GlowColor;
            float _ReflectionIntensity;
            float _UseProbes;
            float4 _ProbeSH0, _ProbeSH1, _ProbeSH2, _ProbeSH3, _ProbeSH4, _ProbeSH5, _ProbeSH6;
            float _ProbeOcclusion;
            float _CullMode;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            float _Cutoff;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv0 : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
        };

        float2 BaseUv(float2 uv0) { return uv0 * _BaseMap_ST.xy + _BaseMap_ST.zw; }

        void ClipAlpha(float2 uv)
        {
            if (_Cutoff > 0.0)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a - _Cutoff);
        }

        // GFL2's perceptual smoothness for the depth pass's normal buffer, as the forward pass reads it.
        float Smoothness(float2 uv)
        {
            return _Unlit > 0.5 ? 0.0 : saturate(SAMPLE_TEXTURE2D(_SMO, sampler_SMO, uv).r * _Smoothness);
        }

        float3 MappedNormal(float3 n, float4 tangentWS, float2 uv)
        {
            // Meshes without tangents carry zeros, and normalising zero is NaN, which HDRP's bloom
            // then smears across the whole frame. Those keep the vertex normal.
            float3 tRaw = tangentWS.xyz - n * dot(n, tangentWS.xyz);
            if (dot(tRaw, tRaw) <= 1e-8 || _BumpScale == 0.0)
                return n;
            float3 t = tRaw * rsqrt(dot(tRaw, tRaw));
            float3 b = cross(n, t) * (tangentWS.w < 0.0 ? -1.0 : 1.0) * GetOddNegativeScale();
            float4 packed = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv);
            float2 xy = (float2(packed.r * packed.a, packed.g) * 2.0 - 1.0) * _BumpScale;
            float z = sqrt(saturate(1.0 - dot(xy, xy)));
            return SafeNormalize(t * xy.x + b * xy.y + n * z);
        }
        ENDHLSL

        // Shadow caster: the room's own geometry into HDRP's shadow maps, for GFL2's live lights
        // (the refitting room's sun, the ops room's spots). Each renderer casts as GFL2 sets it.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_CullMode]
            ZWrite On
            ZClip [_ZClip]
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
            };

            ShadowVaryings vertShadow(Attributes input)
            {
                ShadowVaryings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv0 = BaseUv(input.uv0);
                return output;
            }

            void fragShadow(ShadowVaryings input)
            {
                ClipAlpha(input.uv0);
            }
            ENDHLSL
        }

        // Depth and normal prepass. Without it the depth buffer has no record of the room and
        // every screen space effect composites as though nothing were there.
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull [_CullMode]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 tangentWS : TEXCOORD1;
                float2 uv0 : TEXCOORD2;
            };

            DepthVaryings vertDepth(Attributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.uv0 = BaseUv(input.uv0);
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
                ClipAlpha(input.uv0);
            #ifdef WRITE_MSAA_DEPTH
                outDepthColour = DollDepthAsColour(input.positionCS);
            #endif
            #ifdef WRITE_NORMAL_BUFFER
                float3 n = normalize(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                DollWriteNormalBuffer(MappedNormal(n, input.tangentWS, input.uv0), 1.0 - Smoothness(input.uv0), outNormalBuffer);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull [_CullMode]
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH

            // The shadows are read directly (the sun's and each live light's), so no tile or cluster
            // light lists, and the punctual and area filters HDShadow.hlsl requires are fixed.
            #define PUNCTUAL_SHADOW_MEDIUM
            #define AREA_SHADOW_MEDIUM
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT
            #define LIGHTLOOP_DISABLE_TILE_AND_CLUSTER
            #define SHADOW_DATA_NOT_GUARANTEED_SCALAR
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"

            // Set by the diorama (StrategyDiorama.Shadows.cs, StrategyDiorama.Room.cs).
            // The room's sun: direction toward it (xyz), linear colour times intensity. Zero is none.
            float4 _WmSunDirection;
            float4 _WmSunColor;
            // How far a live caster's shadow darkens the baked light (GFL2's indoor player shadow).
            float _WmShadowStrength;
            // GFL2's live lights, up to four: position (xyz) and 1 / range^2 (w), spot direction
            // toward the light (xyz), linear colour times intensity (rgb, zero for no light), and the
            // spot cone's scale and offset (a point light is 0, 1) and whether the shadowmask holds
            // the room's shadows for it (a Mixed light, z). Set one by one (the game's
            // interop cannot hand Unity a vector array).
            float4 _WmLight0Position, _WmLight0Direction, _WmLight0Color, _WmLight0Spot;
            float4 _WmLight1Position, _WmLight1Direction, _WmLight1Color, _WmLight1Spot;
            float4 _WmLight2Position, _WmLight2Direction, _WmLight2Color, _WmLight2Spot;
            float4 _WmLight3Position, _WmLight3Direction, _WmLight3Color, _WmLight3Spot;
            float4 _WmLight4Position, _WmLight4Direction, _WmLight4Color, _WmLight4Spot;
            float4 _WmLight5Position, _WmLight5Direction, _WmLight5Color, _WmLight5Spot;
            float4 _WmLight6Position, _WmLight6Direction, _WmLight6Color, _WmLight6Spot;
            float4 _WmLight7Position, _WmLight7Direction, _WmLight7Color, _WmLight7Spot;
            // GFL2's linear fog: colour (rgb), and its start and end distances (zw).
            float4 _WmFogColor;
            float4 _WmFogLinear;
            // The planar floor reflection (StrategyDiorama.Reflection.cs): a mirror render of the
            // room, (plane world height, strength, blur mip, unused), and its tint.
            TEXTURE2D(_WmPlanarReflection); SAMPLER(sampler_WmPlanarReflection);
            float4 _WmPlanarParams;
            float4 _WmPlanarTint;
            // How far a surface's reflection of the room is dimmed where its baked light is dark
            // (x, zero for none), and the baked brightness at and over which it is left whole (y).
            float4 _WmReflectionShade;
            // GFL2's screen space ambient occlusion for the room on screen: its strength (x, zero
            // for a room without it) and its radius in metres (y).
            float4 _WmAo;
            // Dev: 0 normal, or one term alone: 1 the ambient occlusion, 2 the live shadow, 3 the shadowmask, 4 the probe
            // reflection, 5 the live lights, 6 the sun, 7 the baked light, 8 what the fog adds,
            // or the full shading with 9 flat normals, 10 no probe reflection.
            float _WmDebugTerm;
            float _WmRoomShade;

            // How open the surface is to the room around it, from the depth the prepass has laid
            // down: what stands within the radius and above the surface's own plane closes it.
            // GFL2 lights its furniture and whatever is put in a room without baking their shadows
            // into the lightmap, and this is what seats them on the floor.
            float RoomAo(float2 positionSS, float3 positionRWS, float3 normalWS, float viewDepth)
            {
                if (_WmAo.x <= 0.0)
                    return 1.0;
                const int taps = 16;
                float radiusPixels = clamp(_WmAo.y * _ScreenSize.y * 0.5 * abs(UNITY_MATRIX_P[1][1]) / max(viewDepth, 0.05), 3.0, 120.0);
                float turn = frac(52.9829189 * frac(dot(positionSS, float2(0.06711056, 0.00583715)))) * 6.2831853;
                float closed = 0.0;
                for (int i = 0; i < taps; i++)
                {
                    // A golden-angle spiral, turned differently at every pixel.
                    float angle = turn + i * 2.3999632;
                    float2 pixel = positionSS + float2(cos(angle), sin(angle)) * radiusPixels * sqrt((i + 0.5) / taps);
                    if (any(pixel < 0.0) || any(pixel >= _ScreenSize.xy))
                        continue;
                    float depth = LoadCameraDepth(uint2(pixel));
                    float3 there = ComputeWorldSpacePosition((floor(pixel) + 0.5) * _ScreenSize.zw, depth, UNITY_MATRIX_I_VP) - positionRWS;
                    float distanceSqr = max(dot(there, there), 1e-6);
                    // Weighted by how near it stands, so what touches the surface closes it most.
                    closed += max(dot(normalWS, there) - 0.02, 0.0) / (distanceSqr + 1e-3) * saturate(1.0 - distanceSqr / (_WmAo.y * _WmAo.y));
                }
                return max(1.0 - 4.0 * _WmAo.x * _WmAo.y * closed / taps, 0.15);
            }

            // The directional shadow map's attenuation here (1 lit).
            float LiveShadow(float2 positionSS, float3 positionRWS, float3 normalWS)
            {
                if (_DirectionalShadowIndex < 0)
                    return 1.0;
                DirectionalLightData light = _DirectionalLightDatas[_DirectionalShadowIndex];
                if (light.shadowDimmer <= 0.0)
                    return 1.0;
                HDShadowContext context = InitShadowContext();
                return lerp(1.0, GetDirectionalShadowAttenuation(context, positionSS, positionRWS, normalWS, light.shadowIndex, -light.forward), light.shadowDimmer);
            }

            // URP's direct BRDF: Lambert without 1/pi, plus its GGX specular.
            float3 DirectLight(float3 diffuseColour, float3 f0, float roughness, float3 N, float3 V, float3 L, float3 radiance)
            {
                float NdotL = saturate(dot(N, L));
                float3 H = SafeNormalize(L + V);
                float NdotH = saturate(dot(N, H));
                float LdotH = saturate(dot(L, H));
                float r2 = roughness * roughness;
                float d = NdotH * NdotH * (r2 - 1.0) + 1.00001;
                float specular = r2 / (d * d * max(0.1, LdotH * LdotH) * (roughness * 4.0 + 2.0));
                return (diffuseColour + f0 * specular) * radiance * NdotL;
            }

            // A live light's shadow, read from the shadow map HDRP renders for the same light (the
            // room carries it as an HDRP light too, found here by its position). 1 where it has none.
            float LiveLightShadow(float3 lightPositionWS, float2 positionSS, float3 positionRWS, float3 normalWS, float3 L, float distance)
            {
                HDShadowContext context = InitShadowContext();
                for (uint i = 0; i < _PunctualLightCount; i++)
                {
                    LightData light = _LightDatas[i];
                    float3 offset = GetAbsolutePositionWS(light.positionRWS) - lightPositionWS;
                    if (dot(offset, offset) > 1e-3)
                        continue;
                    if (light.shadowIndex < 0 || light.shadowDimmer <= 0.0)
                        return 1.0;
                    return lerp(1.0, GetPunctualShadowAttenuation(context, positionSS, positionRWS, normalWS, light.shadowIndex, L, distance,
                        light.lightType == GPULIGHTTYPE_POINT, true), light.shadowDimmer);
                }
                return 1.0;
            }

            // One of GFL2's live lights with URP's attenuation (a smooth window to its range over the
            // inverse square, and a spot's cone) and its shadow.
            float3 LiveLight(float4 position, float4 direction, float4 colour, float4 spotParams, float3 positionWS,
                float2 positionSS, float3 positionRWS, float3 n, float mask, float3 diffuseColour, float3 f0, float roughness, float3 N, float3 V)
            {
                if (dot(colour.rgb, colour.rgb) <= 0.0)
                    return 0.0;
                float3 toLight = position.xyz - positionWS;
                float distanceSqr = max(dot(toLight, toLight), 1e-4);
                float3 L = toLight * rsqrt(distanceSqr);
                float factor = distanceSqr * position.w;
                float smooth = saturate(1.0 - factor * factor);
                float spot = saturate(dot(direction.xyz, L) * spotParams.x + spotParams.y);
                float attenuation = smooth * smooth / distanceSqr * spot * spot;
                if (attenuation <= 0.0)
                    return 0.0;
                attenuation *= LiveLightShadow(position.xyz, positionSS, positionRWS, n, L, sqrt(distanceSqr)) * lerp(1.0, mask, spotParams.z);
                return DirectLight(diffuseColour, f0, roughness, N, V, L, colour.rgb * attenuation);
            }

            // Unity's SH evaluation of the probe constants (ShadeSH9).
            float3 ProbeLight(float3 N)
            {
                float4 n = float4(N, 1.0);
                float3 l1 = float3(dot(_ProbeSH0, n), dot(_ProbeSH1, n), dot(_ProbeSH2, n));
                float4 b = N.xyzz * N.yzzx;
                float3 l2 = float3(dot(_ProbeSH3, b), dot(_ProbeSH4, b), dot(_ProbeSH5, b));
                return max(0.0, l1 + l2 + _ProbeSH6.rgb * (N.x * N.x - N.y * N.y));
            }

            // Karis's environment BRDF approximation, as GFL2 applies it.
            float3 EnvironmentBrdf(float3 f0, float roughness, float NdotV)
            {
                const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
                const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
                float4 r = roughness * c0 + c1;
                float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
                float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
                return f0 * ab.x + ab.y;
            }

            // GFL2's linear fog over a colour at this view depth. GFL2 adds UE4's exponential height
            // fog on top, whose parameters its own code packs from the volume, and the packing is
            // not in the scene's data: read as UE4 packs them, the room hazes over far past a GFL2
            // capture of the same view, so the height fog is left out.
            float3 ApplyFog(float3 colour, float viewDepth)
            {
                return lerp(colour, _WmFogColor.rgb, saturate((viewDepth - _WmFogLinear.z) / max(_WmFogLinear.w - _WmFogLinear.z, 1e-4)));
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 positionRWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float4 tangentWS : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.positionRWS = positionRWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.uv0 = BaseUv(input.uv0);
                output.uv1 = input.uv1;
                return output;
            }

            float4 frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv0);
                float alpha = baseSample.a * _BaseColor.a;
                if (_Cutoff > 0.0)
                    clip(alpha - _Cutoff);
                float3 positionWS = GetAbsolutePositionWS(input.positionRWS);
                float viewDepth = input.positionCS.w;

                float3 colour;
                float3 n = normalize(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                float3 N = n;
                float NdotV = 1.0;
                float occlusion = 1.0;
                if (_Unlit > 0.5)
                {
                    colour = baseSample.rgb * _BaseColor.rgb * _GlowIntensity;
                }
                else
                {
                    float3 albedo = baseSample.rgb * _BaseColor.rgb;
                    float4 smo = SAMPLE_TEXTURE2D(_SMO, sampler_SMO, input.uv0);
                    float smoothness = saturate(smo.r * _Smoothness);
                    float metallic = saturate(smo.g * _Metallic);
                    occlusion = lerp(1.0, smo.b, _OcclusionStrength);
                    N = _WmDebugTerm > 8.5 && _WmDebugTerm < 9.5 ? n : MappedNormal(n, input.tangentWS, input.uv0);

                    // Camera-relative rendering: the camera sits at the origin of positionRWS.
                    float3 V = -SafeNormalize(input.positionRWS);
                    NdotV = saturate(dot(N, V));
                    float perceptualRoughness = 1.0 - smoothness;
                    // GFL2's geometric specular anti-aliasing: the roughness both BRDFs use widens with
                    // how fast the normal turns across the pixel (thin rails, tight bevels), while the
                    // probe's mip keeps the material's own roughness.
                    float3 normalChange = abs(ddx_coarse(N)) + abs(ddy_coarse(N));
                    float roughness = sqrt(pow(perceptualRoughness, 4.0) + 0.25 * dot(normalChange, normalChange));
                    float3 diffuseColour = albedo * 0.96 * (1.0 - metallic);
                    float3 f0 = lerp(0.04, albedo, metallic);

                    float live = LiveShadow(input.positionCS.xy, input.positionRWS, n);
                    float mask = _UseProbes > 0.5 ? _ProbeOcclusion : SAMPLE_TEXTURE2D(_ShadowMask, sampler_ShadowMask, input.uv1).r;
                    float3 bakedLight = _UseProbes > 0.5 ? ProbeLight(N) : SAMPLE_TEXTURE2D(_Lightmap, sampler_Lightmap, input.uv1).rgb;
                    float ao = RoomAo(input.positionCS.xy, input.positionRWS, n, viewDepth);
                    float3 baked = bakedLight * _LightmapScale
                        * occlusion * ao * diffuseColour * lerp(1.0, live, _WmShadowStrength);
                    float3 sun = DirectLight(diffuseColour, f0, roughness, N, V, _WmSunDirection.xyz, _WmSunColor.rgb * min(live, mask));
                    float3 lights = LiveLight(_WmLight0Position, _WmLight0Direction, _WmLight0Color, _WmLight0Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V)
                        + LiveLight(_WmLight1Position, _WmLight1Direction, _WmLight1Color, _WmLight1Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V)
                        + LiveLight(_WmLight2Position, _WmLight2Direction, _WmLight2Color, _WmLight2Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V)
                        + LiveLight(_WmLight3Position, _WmLight3Direction, _WmLight3Color, _WmLight3Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V)
                        + LiveLight(_WmLight4Position, _WmLight4Direction, _WmLight4Color, _WmLight4Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V)
                        + LiveLight(_WmLight5Position, _WmLight5Direction, _WmLight5Color, _WmLight5Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V)
                        + LiveLight(_WmLight6Position, _WmLight6Direction, _WmLight6Color, _WmLight6Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V)
                        + LiveLight(_WmLight7Position, _WmLight7Direction, _WmLight7Color, _WmLight7Spot, positionWS, input.positionCS.xy, input.positionRWS, n, mask, diffuseColour, f0, roughness, N, V);
                    float3 R = reflect(-V, N);
                    float3 environment = SAMPLE_TEXTURECUBE_LOD(_ReflectionCube, sampler_ReflectionCube, R, perceptualRoughness * 6.0).rgb
                        * _ReflectionIntensity * EnvironmentBrdf(f0, roughness, NdotV) * occlusion * ao;
                    // The probe is one picture of the whole room, bright windows included, and a
                    // wall in shadow would shine with it. Its baked light says how much of the room
                    // reaches it.
                    float bakedBrightness = dot(bakedLight * _LightmapScale, float3(0.2126, 0.7152, 0.0722));
                    environment *= lerp(1.0, saturate(bakedBrightness / max(_WmReflectionShade.y, 0.0001)), _WmReflectionShade.x);
                    colour = baked + sun + lights + (_WmDebugTerm > 9.5 ? 0.0 : environment) + smo.a * _EmissionColor.rgb * albedo;

                    if (_WmDebugTerm > 0.5 && _WmDebugTerm < 8.5)
                    {
                        float3 term = _WmDebugTerm < 1.5 ? ao.xxx : _WmDebugTerm < 2.5 ? live.xxx : _WmDebugTerm < 3.5 ? mask.xxx : _WmDebugTerm < 4.5 ? environment
                            : _WmDebugTerm < 5.5 ? lights : _WmDebugTerm < 6.5 ? sun : _WmDebugTerm < 7.5 ? baked
                            : ApplyFog(0.0, viewDepth);
                        return float4(term * (_WmDebugTerm > 3.5 && _WmDebugTerm < 7.5 ? GetCurrentExposureMultiplier() : 1.0), 1.0);
                    }
                }
                colour += SAMPLE_TEXTURE2D(_GlowMap, sampler_GlowMap, input.uv0).rgb * _GlowColor.rgb;
                // The room is darkened as a whole under the holotable's map (0 where nothing sets it).
                colour = ApplyFog(colour, viewDepth) * GetCurrentExposureMultiplier() * (1.0 - _WmRoomShade);

                // Planar reflection on surfaces lying in the floor plane, added after exposure (the
                // mirror render is already exposed). GFL2's overlay: tint, fresnel to the power 0.85
                // clamped at 0.75, a little bump from the normal map.
                if (_WmPlanarParams.y > 0.0 && _Unlit < 0.5)
                {
                    float onPlane = saturate(1.0 - abs(positionWS.y - _WmPlanarParams.x) / 0.1) * step(0.9, n.y);
                    if (onPlane > 0.0)
                    {
                        // The mirror camera keeps the reflection's right and turns its up over, so
                        // its image is the reflection upside down.
                        float2 screenUv = input.positionCS.xy * _ScreenSize.zw;
                        screenUv.y = 1.0 - screenUv.y;
                        screenUv += (N.xz - n.xz) * 0.03;
                        float3 mirrored = SAMPLE_TEXTURE2D_LOD(_WmPlanarReflection, sampler_WmPlanarReflection, screenUv, _WmPlanarParams.z).rgb;
                        float planarFresnel = min(pow(1.0 - NdotV, 0.85), 0.75);
                        colour += mirrored * _WmPlanarTint.rgb * planarFresnel * onPlane * _WmPlanarParams.y * occlusion;
                    }
                }
                // Last line of defence against NaN and negative values reaching post-processing.
                colour = max(0.0, colour);
                colour = (colour == colour) ? colour : 0.0;
                return float4(colour, _DstBlend > 0.5 ? saturate(alpha) : 1.0);
            }
            ENDHLSL
        }
    }
}
