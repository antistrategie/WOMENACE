// Object motion vectors for the doll shaders.
//
// HDRP reprojects the previous frame to build temporal antialiasing, and to carry
// the history that screen-space occlusion and reflection accumulate over. To do
// that it needs, per pixel, where that surface sat last frame. A material with no
// MotionVectors pass supplies none, and HDRP's full-screen camera motion pass then
// fills those pixels in by reprojecting them as though they belonged to the static
// scene. For a skinned character that is the one answer that cannot be right: the
// camera can be still while she moves, and the history is then fetched from
// wherever she was not.
//
// Shared by every doll surface that owns its pixels, which is the body, the eyeball
// and the outline hull. The eye shadow and highlight layers do not: they are
// ZWrite Off layers composited over the eyeball, and the eyeball beneath them
// already carries the depth and the motion for those pixels.
//
// Transcribed from HDRP's own ShaderPassMotionVectors.hlsl and
// MotionVectorVertexShaderCommon.hlsl rather than from a summary, and it reads the
// same three unity_MotionVectorsParams channels the pipeline sets.
#ifndef DOLL_MOTION_VECTORS_INCLUDED
#define DOLL_MOTION_VECTORS_INCLUDED

// The transforms, the previous model matrix, unity_MotionVectorsParams and
// _ScreenSize. Both of these carry an include guard, so a shader that already has
// them in an HLSLINCLUDE block pays nothing, and one that keeps its includes inside
// a pass still gets them here.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

// CalculateMotionVector compiles to a constant zero unless SHADERPASS says this is
// the motion vector pass, so the define is load-bearing rather than descriptive.
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
#define SHADERPASS SHADERPASS_MOTION_VECTORS

// EncodeMotionVector, and the micro-movement threshold CalculateMotionVector
// clamps against. Neither this header nor ShaderPass.cs.hlsl carries an include
// guard, and neither is reachable from the chain ShaderVariables.hlsl pulls in, so
// each belongs here exactly once and nowhere else.
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Builtin/BuiltinData.hlsl"
// The motion vector functions alone. The rest of this header is the baked-GI layer,
// which wants a material context this pass does not build.
#define INCLUDE_ONLY_MV_FUNCTIONS
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"

struct DollMotionAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    // Last frame's object-space positions, which the engine binds as an extra
    // vertex stream on a skinned renderer with skinnedMotionVectors on. TEXCOORD4
    // is where it looks for them, and the surface passes' own coordinates stop at
    // TEXCOORD2, so the two never collide.
    float3 previousPositionOS : TEXCOORD4;
};

struct DollMotionVaryings
{
    float4 positionCS : SV_POSITION;
    float4 currentCS : TEXCOORD0;
    float4 previousCS : TEXCOORD1;
};

// No depth bias here, and unity_MotionVectorsParams.z goes unread.
//
// HDRP has a MotionVectorPositionZBias that would subtract it, but that function
// takes its argument by value, which in HLSL means it biases a local copy and
// throws it away, so the pipeline ships no bias. URP reads only .x and .y and has
// no equivalent at all.
//
// Applying one is not a harmless difference. A renderer with motion vectors is
// dropped from the depth prepass, so this pass writes the depth that the forward
// pass then tests against with ZTest LEqual. Bias it nearer and every forward
// fragment fails that test, and the doll renders completely transparent.

// Where this vertex was last frame, in camera-relative world space.
//
// x is non-zero while the engine is deforming the mesh, which is the only time the
// previous-position stream holds anything. A rigid mesh leaves it unbound, and all
// of its motion is in the previous model matrix instead, so the current position
// goes through that matrix rather than through a stream of noise.
float3 DollPreviousPositionRWS(DollMotionAttributes input)
{
    bool hasDeformation = unity_MotionVectorsParams.x > 0.0;
    float3 previousPositionOS = hasDeformation ? input.previousPositionOS : input.positionOS.xyz;
    return TransformPreviousObjectToWorld(previousPositionOS);
}

// y is zero when the renderer has asked for no motion at all. HDRP's convention is
// to write a vector longer than any real one can be rather than to write zero,
// because zero means "held still" and the temporal passes have to tell those apart.
bool DollMotionSuppressed()
{
    return unity_MotionVectorsParams.y == 0.0;
}

// SV_POSITION keeps the jitter, because the raster has to land on the same samples
// as every other pass. The vectors are built from the unjittered projection
// instead: the jitter pattern moves every frame by design, and measuring against it
// would report that motion as the surface's own and leave TAA chasing its own
// dither.
DollMotionVaryings DollMotionVertex(DollMotionAttributes input)
{
    DollMotionVaryings output;
    float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
    // The same expression the depth and forward passes use, so the depth this pass
    // writes is bit-identical to the depth the forward pass tests against.
    output.positionCS = TransformWorldToHClip(positionRWS);

    output.currentCS = mul(UNITY_MATRIX_UNJITTERED_VP, float4(positionRWS, 1.0));
    output.previousCS = DollMotionSuppressed()
        ? float4(0.0, 0.0, 0.0, 1.0)
        : mul(UNITY_MATRIX_PREV_VP, float4(DollPreviousPositionRWS(input), 1.0));
    return output;
}

// The encoded vector, without a target semantic, so a pass that also writes the
// normal buffer can call it and place the result on its own output.
float4 DollMotionValue(DollMotionVaryings input)
{
    if (DollMotionSuppressed())
        return float4(2.0, 0.0, 0.0, 0.0);

    float4 encoded;
    EncodeMotionVector(CalculateMotionVector(input.currentCS, input.previousCS) * 0.5, encoded);
    return encoded;
}

// For a pass whose only output is the vector.
float4 DollMotionFragment(DollMotionVaryings input) : SV_TARGET
{
    return DollMotionValue(input);
}

#endif
