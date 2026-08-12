// The GFL2 shading model shared by Womenace/DollToon and its transparent
// variant Womenace/DollToonTrans: the material inputs, the ramp, GGX, the
// FaceSDF sweep, the hair path, the extra-lights loop and the key light
// fetch. Pass-specific stages stay in the shaders; #pragma directives are
// invisible inside an include, so those stay in the shaders too.
#ifndef DOLL_TOON_LIGHTING_INCLUDED
#define DOLL_TOON_LIGHTING_INCLUDED
// ShaderVariables brings HDRP's globals, the camera-relative space
// transforms, and GetCurrentExposureMultiplier.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
// ShaderVariables already brings the light lists, the screen-space
// lighting textures and the atmospheric block, and several of those
// headers carry no include guard: including them again redefines their
// buffers and the shader fails to compile. So the scene's lights and
// shadow atlases are available here for free, read directly rather than
// pushed in from managed code, and only genuinely additional headers
// belong below.
//
// The composite's ambientDiffuse term. The atmospheric block defines
// AMBIENT_PROBE_BUFFER, so this reads the sky's own ambient probe out of
// _AmbientProbeData rather than a per-renderer light probe: one globally
// bound source, identical for every doll in the frame. A scene with no
// sky contributes nothing here, which is why the armoury has to be
// carried by the extra lights rather than by a fill.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
// The extra-lights loop: GetPunctualLightVectors resolves a light's
// direction and distances, PunctualLightAttenuation turns those into a
// falloff. Both are guarded, unlike most of what ShaderVariables pulls in.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/PunctualLightCommon.hlsl"
// The normal buffer the prepass and the motion vector pass both feed, which
// is what HDRP's ambient occlusion reads to know which way this surface
// points. Guarded, so the passes that do not write it pay nothing.
#include "DollNormalBuffer.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_RampMap);
SAMPLER(sampler_RampMap);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);
TEXTURE2D(_MaskMap);
SAMPLER(sampler_MaskMap);
TEXTURE2D(_SpecularMap);
SAMPLER(sampler_SpecularMap);
TEXTURE2D(_SdfMap);
SAMPLER(sampler_SdfMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseTint;
    float _NormalScale;
    float _OcclusionStrength;
    float _MaskRoughnessInverted;
    float _MatCapIntensity;
    float _MatCapUVOffset;
    float _UseBlendTex;
    float _BlendSmoothness;
    float _IblIntensity;
CBUFFER_END

#define RAMP_V_MAIN_DIFFUSE 0.125
#define RAMP_V_SPECULAR 0.375
#define RAMP_V_ADDITIONAL 0.875

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    // The face SDF's lookup set, baked from rest positions by
    // scripts/bake_face_sdf_uv.py: the game's own second UV set,
    // transferred per vertex from a capture of its face draw. Not an
    // unwrap, and not something a PMX source can carry.
    float2 uv2 : TEXCOORD2;
};

// The shading normal: the vertex normal turned to face the viewer, then
// perturbed by the tangent-space normal map.
//
// Shared by every pass that needs it rather than written out three times,
// because the depth prepass, the motion vector pass and the forward pass have
// to agree on it exactly. The buffer HDRP's occlusion reads and the normal
// this surface shades with are then the same normal by construction.
//
// The facing flip is what makes cull-off geometry work: a skirt, a sleeve and
// a hair card all have insides that are meant to be seen, and without the flip
// the inside of a shell shades as though lit from behind.
float3 DollShadingNormal(float3 normalWS, float4 tangentWS, float2 uv, bool isFrontFace)
{
    float facing = isFrontFace ? 1.0 : -1.0;
    float3 vertexNormalWS = normalize(normalWS) * facing;
    float3 tangent = normalize(tangentWS.xyz) * facing;
    float3 bitangent = cross(vertexNormalWS, tangent) * tangentWS.w;
    float3x3 tangentToWorld = float3x3(tangent, bitangent, vertexNormalWS);

    float3 normalTS = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalScale);
    return normalize(mul(normalTS, tangentToWorld));
}

// The roughness that travels alongside the normal in that buffer. Same
// sentinel the forward pass uses: an all-black mask means the material ships
// no RMO map, and a material with no roughness to report takes 1.
float DollPerceptualRoughness(float2 uv)
{
    float3 rmo = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv).rgb;
    if (rmo.b <= 0.001)
        return 1.0;
    return _MaskRoughnessInverted > 0.5 ? 1.0 - rmo.r : rmo.r;
}

// The scene's key light: HDRP's first directional light, which is the one
// it treats as the main light. Returns false where a scene has none, and
// the caller then contributes no key rather than inventing an illuminance
// for it. MENACE's armoury is such a scene, lit entirely by punctual and
// area lights.
bool GetKeyLight(out float3 L, out float3 colour)
{
    L = float3(0.0, 1.0, 0.0);
    colour = 0.0;
    if (_DirectionalLightCount == 0)
        return false;
    DirectionalLightData light = _DirectionalLightDatas[0];
    // forward points the way the rays travel, so the vector towards the
    // light is its negation.
    L = -light.forward;
    colour = light.color;
    return true;
}

// Band 0 is a transfer curve indexed by N dot L, not a hard step. The
// shadow end of a GFL2 ramp is a warm desaturated tint rather than a
// darkened albedo, which is most of why the result reads as stylised
// over a physically based base.
// The ramp's main-light band. A negative U clamps to the dark end, so
// a signed N dot L indexes it directly.
//
// The dark end mixes halfway toward grey of the same luminance. The
// skin and hair ramps floor on strongly red tints, authored for a
// scene whose ambient dilutes them toward neutral. This scene carries
// no such ambient, the key is nearly the whole term, and the undiluted
// floor lands skin's shadow on pink where the game reads cream. Half
// grey, tuned in game, lands it on the game's cream. The mix is
// weighted by 1 - U, so the dark end takes the full amount, the
// terminator part of it, and the lit end keeps the gradient's warmth.
// Same-luminance grey moves the shadow's hue and not its brightness.
float3 RampDiffuse(float u)
{
    float x = saturate(max(1e-4, u));
    float3 band = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_RampMap,
        float2(x, RAMP_V_MAIN_DIFFUSE), 0).rgb;
    float grey = dot(band, float3(0.2126, 0.7152, 0.0722));
    return lerp(band, grey.xxx, 0.5 * (1.0 - x));
}

// Height-correlated Smith visibility, already carrying 1/(4 NoL NoV) so
// nothing divides by that again. Symmetric in its two arguments, which is
// why the peak below can pass the same value twice.
float SmithVisibility(float x, float y, float a2, float oneMinusA2)
{
    float v = x * (y * oneMinusA2 + a2) + y * (x * oneMinusA2 + a2);
    return min(1.0, 0.5 / max(1e-4, v));
}

// GGX as the game writes it, transcribed register by register from the
// decompiled fragment rather than from a summary. Its quirks are
// deliberate: D is squared and is not divided by PI, the visibility term
// already carries 1/(4 NoL NoV) so there is no separate division, and F
// collapses to a constant 1 for any F0 at or above 0.02, which defeats
// Fresnel by design. Transcribe, do not correct.
//
// With the diffuse ramp on, the raw lobe is not used. The lobe is divided
// by the peak lobe, the ratio indexes the specular band, and the band
// value is scaled back up by that same peak. The peak is D at NoH = 1
// paired with the visibility evaluated at NoL = NoV = LoH: in the
// decompile the ramp branch overwrites the register holding one of the
// outer dot products with max(0, L dot H) and rebuilds V from it, so the
// peak's visibility is a different quantity from the outer one and the two
// do not cancel. Using the outer V here, or dropping V from the ratio,
// both overstate the highlight.
float3 GgxSpecular(float3 albedo, float roughness, float metallic,
                   float3 N, float3 L, float3 viewDirWS)
{
    float3 F0 = albedo * metallic + 0.04 * (1.0 - metallic);
    float a2 = max(1e-4, roughness * roughness);

    float3 H = normalize(L + viewDirWS);
    float NoH = saturate(dot(N, H));
    float NoL = saturate(dot(N, L));
    float NoV = saturate(dot(N, viewDirWS));
    float LoH = max(0.0, dot(L, H));

    float oneMinusA2 = 1.0 - a2;
    float d = NoH * NoH * (a2 * a2 - 1.0) + 1.0;
    float D = min(2048.0, (a2 / d) * (a2 / d));
    float vis = SmithVisibility(NoL, NoV, a2, oneMinusA2);

    float oneMinusLoH = 1.0 - LoH;
    float pow5 = oneMinusLoH * oneMinusLoH;
    pow5 = pow5 * pow5 * oneMinusLoH;
    float F = (saturate(F0.g * 50.0) - 1.0) * pow5 + 1.0;

    float peak = 1.0 / a2;
    float Dpeak = min(2048.0, peak * peak);
    float visPeak = SmithVisibility(LoH, LoH, a2, oneMinusA2);

    float ratio = saturate((D * vis) / max(1e-4, Dpeak * visPeak));
    float3 band = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_RampMap,
        float2(ratio, RAMP_V_SPECULAR), 0).rgb;

    return band * Dpeak * visPeak * F * F0;
}

// iblColor, the composite's third term and the last of it to be built.
//
// The sky cubemap sampled along the reflection vector at a mip chosen by
// roughness, times the analytic env-BRDF. It sits beside the ambient rather
// than inside it, and carries no occlusion factor, as the composite has it.
//
// This is the term that lights a surface the key never reaches and the
// ambient probe barely does: the sky is bright from almost every direction a
// reflection vector can point, so it lifts the side facing away from the key
// where nothing else contributes.
//
// mip = 6 * roughness, kept literal from the spec rather than routed through
// HDRP's PerceptualRoughnessToMipmapLevel, whose curve is not linear.
//
// No HDR decode. The game's cube is an encoded format that needs one, and
// HDRP's sky cube is already linear HDR, so decoding again would darken it by
// the factor the encoding exists to undo. HDRP does not pre-expose this
// texture either, so the exposure multiply at the end of the fragment applies
// to it exactly as it does to the lights and the probe.
float3 IblSpecular(float3 albedo, float roughness, float metallic,
                   float3 N, float3 viewDirWS)
{
    float3 F0 = albedo * metallic + 0.04 * (1.0 - metallic);
    float NoV = saturate(dot(N, viewDirWS));
    float3 R = reflect(-viewDirWS, N);

    // Clamped as HDRP clamps its own sky reads, because an unbounded sky
    // texel reaching a float16 target arrives as an infinity.
    float3 env = ClampToFloat16Max(SampleSkyTexture(R, 6.0 * roughness, 0).rgb);

    // Karis's mobile env-BRDF: two fitted polynomials standing in for the
    // split-sum scale and bias, which is the approximation the spec names.
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;

    return env * (F0 * ab.x + ab.y) * _IblIntensity;
}

float WrapMod2(float x) { return 2.0 * frac(x * 0.5 + 2.0); }

// FaceSDF, transcribed from the decompile's _USE_BLEND_TEX variant.
//
// The face is not shaded by how much it faces the light, it is shaded by
// where the light is standing. The map stores, per texel, the compass
// angle at which that part of the face flips into shadow, and the sweep
// compares the light's angle against it. That is why a nose wedge and a
// side-of-face sweep come out clean where an N dot L terminator on a
// smooth face would not.
//
// Three things that matter, each of which the spec had to correct once:
//
//  - Object space, and the character root rather than the head bone. On a
//    skinned renderer the inverse model matrix is the root, so the shadow
//    tracks the body's facing and a head turn does not drag it around.
//  - normalize() is applied to the 3D vector and xz taken from it, so the
//    pair is not unit length and shrinks as the light climbs. yFactor is
//    built from that shrunken value. The sweep is not: it renormalises the
//    2D pair before building baseU.
//  - Both thresholds are remapped before any comparison. Listings derived
//    from the third-party writeup omit this.
float FaceSdf(float2 sdfUV, float3 lightWS)
{
    // Two independent frame corrections, which happen to compose into a
    // single negation. Applying either alone leaves the other visible, and
    // both were diagnosed that way: correcting only X left the sweep's
    // forward axis pointing out the back of her head, and correcting only
    // Y and Z left a light on her left shadowing her left.
    //
    //   X   the lookup coordinates are baked from the source mesh, whose X
    //       the importer mirrors on the way in (measured: the imported
    //       bounds are the source's X negated, Y and Z untouched), so the
    //       baked u axis runs opposite to object-space x.
    //
    //   Y,Z the rig this doll is retargeted onto puts its root bone at
    //       Euler (0, 180, 180). Unity composes Euler as Ry*Rx*Rz, so that
    //       is Ry(180)*Rz(180), mapping (x, y, z) to (x, -y, -z): a half
    //       turn about X. The skinned result still renders upright because
    //       the bind poses account for it, but the frame a light direction
    //       resolves into is turned with it.
    //
    // Corrected here rather than in the bake because the rig half of it
    // belongs to the skeleton she is retargeted onto, not to her mesh.
    float3 localDir = -TransformWorldToObjectDir(lightWS, false);
    float2 shrunk = normalize(localDir).xz;
    float yFactor = (1.0 - abs(shrunk.y)) * 0.5 + 0.5;
    float2 faceLightDir2D = normalize(shrunk);

    float4 sdf1 = SAMPLE_TEXTURE2D(_SdfMap, sampler_SdfMap, sdfUV);
    float4 sdf2 = SAMPLE_TEXTURE2D(_SdfMap, sampler_SdfMap, float2(1.0 - sdfUV.x, sdfUV.y));

    float penumbra = max(1e-4, _BlendSmoothness);

    // Alpha selects which half of the [0,2) circle baseU lands in.
    bool isSideFace = sdf1.a < 0.5;
    float baseU  = isSideFace ? (faceLightDir2D.y * 0.5 + 0.5)
                              : (faceLightDir2D.y * 0.5 + 1.5);
    float mainTh = isSideFace ? sdf2.r : sdf1.r;
    float backTh = isSideFace ? sdf1.r : sdf2.r;

    float remap = 1.0 - penumbra * 0.5;
    mainTh = (mainTh - penumbra * 0.5) / remap;
    backTh = (backTh - penumbra * 0.5) / remap;

    float u = (faceLightDir2D.x < 0.0) ? (2.0 - baseU) : baseU;
    float wr1 = WrapMod2(u - penumbra * 0.5);
    float wr2 = WrapMod2(u + penumbra * 0.5);
    float valA = saturate((mainTh - wr1) / penumbra);
    float valB = 1.0 - saturate((2.0 - wr1 - backTh) / penumbra);
    float valC = 1.0 - saturate((wr2 - mainTh) / penumbra);
    float result1 = (wr1 < 1.0) ? valA : valB;
    float faceSDF = (wr2 < 1.0) ? min(result1, valC) : max(result1, valB);

    return faceSDF * yFactor;
}

// Hair specular, which is not GGX and reads no tangent frame. The spc
// map is re-sampled at a view-shifted V and sits on top of the ramp
// diffuse. The 1/PI keeps the streak below the map's own colour so it can
// never reach white, and the 0.1 floor keeps a tenth of it in full shadow.
float3 HairSpecular(float2 uv1, float3 N, float3 L, float3 viewDirWS, float shadow)
{
    float2 matcapUV = float2(uv1.x, uv1.y - viewDirWS.y * _MatCapUVOffset);
    float3 samp = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, matcapUV).rgb;
    float NdotV = saturate(dot(N, viewDirWS));
    float NdotL = saturate(dot(N, L));
    float3 matcapSpec = NdotV * samp * _MatCapIntensity;
    return matcapSpec * (0.1 + 0.9 * (NdotL * shadow)) / PI;
}

// Illuminance arriving from one punctual light, carrying its inverse
// square falloff and its cone shape.
float3 PunctualIlluminance(LightData light, float3 positionRWS, out float3 L)
{
    float4 distances;
    GetPunctualLightVectors(positionRWS, light, L, distances);
    float atten = PunctualLightAttenuation(distances,
        light.rangeAttenuationScale, light.rangeAttenuationBias,
        light.angleScale, light.angleOffset) * light.lightDimmer;
    return light.color * max(0.0, atten);
}

// The extra-lights loop. Ramp band 3 exists only for this: the spec has
// V = 0.875 as the additional-light diffuse and notes it is sampled
// nowhere else, so nothing lit by the key belongs on this curve.
//
// Two readings, since the spec gives the row's role but not its U. The
// curve is indexed by N dot L to keep it parallel with the main light,
// and the light's attenuation is applied as illuminance rather than
// folded into U, because a 0..1 transfer curve cannot carry an inverse
// square. Folding it in as well would square the falloff.
//
// Punctual only. HDRP keeps its area lights past _PunctualLightCount in
// the same buffer and they need a different integral, so the armoury's
// five rectangles are skipped and its twenty-three point, spot and
// pyramid lights are not.
float3 AdditionalLightsDiffuse(float3 positionRWS, float3 N)
{
    float3 sum = 0.0;
    // [loop], not unrolled: the count is a runtime value and a scene can
    // carry dozens of lights, so asking the compiler to unroll it either
    // fails outright or produces an enormous program.
    [loop]
    for (uint i = 0; i < _PunctualLightCount; i++)
    {
        LightData light = _LightDatas[i];
        float3 L;
        float3 illuminance = PunctualIlluminance(light, positionRWS, L);
        if (all(illuminance <= 0.0))
            continue;
        float u = saturate(dot(N, L));
        float3 band = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_RampMap,
            float2(max(1e-4, u), RAMP_V_ADDITIONAL), 0).rgb;
        sum += band * illuminance;
    }
    return sum / PI;
}
#endif
