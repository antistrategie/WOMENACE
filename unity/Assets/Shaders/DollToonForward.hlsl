// The ForwardOnly stage shared by Womenace/DollToon and Womenace/DollToonTrans:
// the pass-scoped shadow and GI includes, the indirect diffuse chain, the
// shadow attenuation, and the vertex and fragment programs. The transparent
// variant defines DOLL_FORWARD_TRANSPARENT, which carries the albedo alpha
// through to the blend instead of writing opaque. The multi_compile keywords
// these includes need are declared by each shader pass, because a #pragma
// inside an include is invisible to Unity.
#ifndef DOLL_TOON_FORWARD_INCLUDED
#define DOLL_TOON_FORWARD_INCLUDED
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowContext.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowAlgorithms.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"

// Indirect diffuse, scoped to this pass. BuiltinGIUtilities reaches the
// legacy probe-volume sampler in EntityLighting, and neither belongs in
// HLSLINCLUDE: the depth, shadow and motion vector passes have vertex
// stages too, and this header does not compile without a light-probe
// context they have no reason to carry.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinGIUtilities.hlsl"

// ambientDiffuse, the composite's second term.
//
// The spec names this term but never defines it, because in the game's own
// engine it is simply "the indirect diffuse the pipeline supplies". Reading
// the sky's ambient probe was too narrow a reading of that: HDRP has three
// sources and picks between them, and its own materials go through this same
// priority. A doll that reads only the last of the three is lit by a fraction
// of what every MENACE character standing next to her receives.
//
//   screen-space GI   when _IndirectDiffuseMode is on, HDRP replaces the
//                     baked value outright with this buffer. It is the one
//                     lighting input in this shader that arrives already
//                     pre-exposed, so it is divided back out to reach the
//                     scene-referred space the composite works in.
//   probe volumes     the baked bounce lighting around the character, which
//                     SampleBakedGI resolves when the volumes exist.
//   ambient probe     the sky's own SH, which is all there is in a scene
//                     carrying neither of the above.
float3 IndirectDiffuse(float3 positionRWS, float3 normalWS, uint2 positionSS)
{
    // The sources in order, each tried only where the one before it came
    // back empty.
    //
    // The fall-through is at runtime, not in the preprocessor, and that
    // distinction is the whole point. Probe volumes are a compile-time
    // keyword but a runtime dataset: the pipeline can have them enabled
    // while the scene bakes none, and then the call below returns nothing
    // and a #else would never be reached. Measured on this doll, the
    // per-renderer probe is the source that actually carries data.
    float3 indirect = float3(0.0, 0.0, 0.0);

#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    // needToIncludeAPV, because without it HDRP's own call sites leave the
    // volumes to the light loop, and this shader has no light loop to leave
    // them to.
    indirect = SampleBakedGI(positionRWS, normalWS, positionSS,
        float2(0.0, 0.0), float2(0.0, 0.0), true);
#endif

    // The per-renderer light probe. Unity fills unity_SHAr..unity_SHC per
    // renderer from the scene's baked probes, and our renderers ask for that
    // (m_LightProbeUsage BlendProbes) exactly as the game's own characters
    // do. EvaluateAmbientProbe cannot reach it: with AMBIENT_PROBE_BUFFER
    // defined, and the atmospheric header does define it, that function
    // reads the sky's probe out of _AmbientProbeData and nothing else.
    if (dot(indirect, float3(1.0, 1.0, 1.0)) <= 0.0)
        indirect = EvaluateLightProbe(normalWS);

    // The sky probe last, for a scene that bakes no probes at all. All three
    // describe the same indirect light rather than three lights, so this
    // picks one instead of adding them.
    if (dot(indirect, float3(1.0, 1.0, 1.0)) <= 0.0)
        indirect = EvaluateAmbientProbe(normalWS);

    // Screen-space GI last, and only where it has something. HDRP replaces
    // the baked value outright here and can afford to, because its own
    // objects are in that buffer by construction. Ours are not: this shader
    // renders in a forward pass of its own, and an empty read then throws
    // away a probe that does carry light.
    //
    // Same rule as the two above, for the same reason: a source being
    // enabled is not a source having data, and every one of these three has
    // to earn its turn by returning something.
#if !defined(SCREEN_SPACE_INDIRECT_DIFFUSE_DISABLED)
    if (_IndirectDiffuseMode != INDIRECTDIFFUSEMODE_OFF)
    {
        float3 ssgi = LOAD_TEXTURE2D_X(_IndirectDiffuseTexture, positionSS).xyz
            * GetInverseCurrentExposureMultiplier();
        if (dot(ssgi, float3(1.0, 1.0, 1.0)) > 0.0)
            indirect = ssgi;
    }
#endif

    // The indirect lighting volume's own control, which HDRP applies to
    // bakeDiffuseLighting for every one of its materials and this shader was
    // not applying at all.
    //
    // This is the scale, not another source. Measured on this doll the probe
    // returns 1 to 10 while the key term lands near a thousand, because
    // MENACE's sun is 6700 lux and the composite divides by PI. A hundredfold
    // gap is not a term missing, it is a term unscaled, and a game lifting its
    // shadows through this volume setting is exactly what produces one.
    return max(0.0, indirect * GetIndirectDiffuseMultiplier(GetMeshRenderingLayerMask()));
}

// The cascaded shadow map's attenuation for the scene's directional
// light, or 1 where there is no shadowed directional light to read.
float KeyShadowAttenuation(float2 positionSS, float3 positionRWS, float3 normalWS, float3 L)
{
    if (_DirectionalLightCount == 0)
        return 1.0;
    DirectionalLightData light = _DirectionalLightDatas[0];
    if (light.shadowIndex < 0)
        return 1.0;
    HDShadowContext shadowContext = InitShadowContext();
    return GetDirectionalShadowAttenuation(
        shadowContext, positionSS, positionRWS, normalWS, light.shadowIndex, L);
}

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;   // w carries the handedness sign
    float2 uv1 : TEXCOORD3;
    float3 positionRWS : TEXCOORD4;
    float2 uv2 : TEXCOORD5;
};

Varyings vert(Attributes input)
{
    Varyings output;
    float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionRWS = positionRWS;
    output.positionCS = TransformWorldToHClip(positionRWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv1 = input.uv1;
    output.uv2 = input.uv2;
    output.tangentWS = float4(
        TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    return output;
}

float4 frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_TARGET
{
    // The same normal the prepass and the motion vector pass wrote into
    // the normal buffer, so the occlusion read back below was integrated
    // around the normal this surface actually shades with.
    float3 normalWS = DollShadingNormal(
        input.normalWS, input.tangentWS, input.uv, isFrontFace);

    float4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    float3 albedo = base.rgb;
    float3 tinted = albedo * _BaseTint.rgb;

    // Occlusion darkens the ambient term only. Applying it to the key
    // as well would double-count what the shading already resolves,
    // and the flat ambient is the part with no idea a crevice exists.
    float3 rmo = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv).rgb;
    bool hasMask = rmo.b > 0.001;
    float occlusion = hasMask ? lerp(1.0, rmo.b, _OcclusionStrength) : 1.0;
    float roughness = _MaskRoughnessInverted > 0.5 ? 1.0 - rmo.r : rmo.r;

    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionRWS);

    float3 lightWS, keyColour;
    bool hasKey = GetKeyLight(lightWS, keyColour);
    float shadow = hasKey
        ? KeyShadowAttenuation(input.positionCS.xy, input.positionRWS, normalWS, lightWS)
        : 1.0;
    float ndotl = dot(normalWS, lightWS);

    // Specular sits inside the parentheses of the composite, added to
    // albedo and then multiplied by the diffuse ramp, so a surface the
    // key does not reach carries no highlight.
    //
    // GGX and the hair path are mutually exclusive, not additive: the
    // game's _ANISOTROPIC_SPECULAR variant contains no GGX and no
    // tangent-frame terms at all. There is no keyword here, so a
    // non-zero _MatCapIntensity is what marks a material as taking
    // the hair path.
    //
    // Hair reaches the right answer today by a different route, and
    // it is worth naming because it crosses a repo boundary: the bake
    // binds a 1x1 placeholder into _MaskMap for any material whose
    // source ships no mask, and that placeholder is packed for HDRP's
    // convention rather than GFL2's. Read as RMO it is roughness 0,
    // metallic 1, occlusion 0 — a chrome mirror — and the only thing
    // standing between that and the screen is the zero in B tripping
    // the sentinel above. So the sentinel is load-bearing, not a
    // convenience, and a placeholder with a non-zero B would turn
    // every unmasked material into chrome.
    bool anisotropicHair = _MatCapIntensity > 0.0;
    float3 specular = (hasMask && !anisotropicHair && hasKey)
        ? GgxSpecular(albedo, roughness, rmo.g, normalWS, lightWS, viewDirWS)
        : 0.0;

    // The shadow attenuates N dot L rather than the ramp's output.
    // Scaling the output drives a shadowed surface toward black and
    // throws away the warm tint the ramp's dark end exists to supply,
    // where attenuating the lookup walks it down the same curve the
    // terminator uses, so a cast shadow and a self-shadow read alike.
    // The hair term composes the two the same way, as NdotL * shadow,
    // which is the closest the spec comes to stating the placement.
    //
    // The body's U departs from the spec here, which scales this
    // sample by min(1, NoL / max(eps, NoL)): zero for any surface
    // facing away from the key, leaving that whole side to
    // ambientDiffuse. That division of labour assumes the game's
    // authored ambient, a healthy fraction of the key. This scene's
    // probe measures near one percent of the key, so under that
    // scale the away side reads black while the face, whose sweep
    // takes no such scale, lands on the ramp's warm dark end. The
    // body indexes the ramp the same way the face does: terminator,
    // cast shadow and away side all land on the dark end, the one
    // shadow colour the ramp exists to supply, and the probe's
    // ambient rides on top of all of it equally.
    //
    // keyColour is HDRP's directional illuminance in lux, and the
    // divide by PI is the Lambert normalisation the ramp stands in
    // for. A scene with no directional light contributes nothing here
    // and is carried by the extra lights and the ambient probe.
    // The face substitutes the SDF sweep for N dot L entirely, which
    // is the whole point of that path: a smooth face shaded by how
    // much it faces the light gets a nose terminator the game never
    // draws. The shadow map still attenuates it, as the spec has it
    // (directIntensity = faceSDF * mainShadow * yFactor), and the
    // result indexes the same ramp band the body uses.
    bool useSdf = _UseBlendTex > 0.5;
    float3 diffuse = RampDiffuse(
        (useSdf ? FaceSdf(input.uv2, lightWS) : ndotl) * shadow);

    float3 key = hasKey
        ? (tinted + specular) * diffuse * keyColour / PI
        : 0.0;

    // Hair sits on top of the ramp diffuse rather than inside it, and
    // carries its own shadow floor.
    float3 hair = hasKey
        ? HairSpecular(input.uv1, normalWS, lightWS, viewDirWS, shadow) * keyColour
        : 0.0;

    // The extra lights, which are the whole of the lighting in a
    // scene with no directional light.
    float3 extra = tinted * AdditionalLightsDiffuse(input.positionRWS, normalWS);

    // ambientDiffuse * occlusion, the composite's second term. This is
    // the scene's own probe, so it carries no factor of the key: a
    // dim key under a bright sky and a bright key under a dark one
    // are different scenes, and a fill expressed as a fraction of the
    // key cannot tell them apart.
    //
    // Directional, not flattened. The game's GI-flatten keyword is off
    // in all 84 shipped character materials, and its NormalizeSH, which
    // strips SH directionality, belongs to the eyeball rather than here.
    //
    // One occlusion term, the RMO map's, as the composite has it. HDRP's
    // screen-space occlusion is deliberately not read: the composite has
    // no screen-space term, and multiplying by both stacks two answers to
    // the same question. That stacking is invisible on the lit side, where
    // the key dominates, and is the entire brightness of the side facing
    // away from the key, where ambient is all there is. The RMO map is
    // gentle by design, measuring 234-239 of 255, where screen-space
    // occlusion on a body reading its own bulk as an occluder is not.
    float3 ambient = tinted * IndirectDiffuse(
        input.positionRWS, normalWS, (uint2)input.positionCS.xy) * occlusion;

    // Gated on the same mask sentinel GGX uses, and for a sharper reason.
    // A material shipping no RMO map takes the 1x1 placeholder, which read
    // as RMO is roughness 0 and metallic 1: without the gate that is a
    // mirror-smooth chrome sphere reflecting the sky at mip 0, which is
    // very far from nearly invisible.
    //
    // Hair takes the body's term here rather than its own. The spec's hair
    // IBL is a fixed mip 6 scaled by a ramp band that clamps to a constant,
    // measuring about 2% of the env colour on this doll, and nothing in the
    // material data marks a hair material while the matcap stays off.
    // Not gated on the key. This term is the sky, not the sun: a scene with
    // no directional light still has one, and a scene with no sky supplies
    // black here on its own.
    float3 ibl = hasMask
        ? IblSpecular(albedo, roughness, rmo.g, normalWS, viewDirWS)
        : 0.0;

    // Pre-exposition is on, so the buffer expects a pre-exposed
    // value. This multiply puts the result at the same scale as
    // everything else HDRP renders, and the frame's own tonemap and
    // grade finish it from there.
    float3 lit = (key + hair + extra + ambient + ibl) * GetCurrentExposureMultiplier();
    #ifdef DOLL_FORWARD_TRANSPARENT
    // The blend weight. The _da albedo carries the coverage, and the tint's
    // alpha scales it so a material can fade as a whole.
    return float4(lit, base.a * _BaseTint.a);
#else
    return float4(lit, 1.0);
#endif
}
#endif
