// Normal buffer writes for the doll shaders.
//
// HDRP's ambient occlusion is not a depth-only effect: GTAO decodes a world normal
// per pixel out of _NormalBufferTexture and integrates visibility around it. A
// surface that writes no normal therefore has its occlusion computed from whatever
// happens to be in that buffer at its pixels.
//
// That matters here rather than being academic, because DollToon reads the result
// back and multiplies its ambient term by it. Writing nothing does not opt out of
// the effect, it opts into being shaded by another surface's normal.
//
// Both the depth prepass and the motion vector pass bind the normal buffer, and
// which of the two actually runs depends on whether the renderer was dropped from
// the prepass for having motion vectors. So every surface that writes normals writes
// them from both, exactly as HDRP's own materials do.
#ifndef DOLL_NORMAL_BUFFER_INCLUDED
#define DOLL_NORMAL_BUFFER_INCLUDED

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"

// The render target the normal buffer lands on, which is not a fixed index: the
// pipeline binds a different set of targets per pass and per frame settings, and the
// keywords below are how a shader is told which arrangement it got. They are global
// keywords set on the command buffer each frame, so they have to be multi_compile
// rather than shader_feature or the variant will not exist when the pipeline asks
// for it.
//
// The prepass binds, in order: the MSAA depth-as-colour target if MSAA is on, then
// the normal buffer, then the rendering layer buffer. So the normal is first unless
// MSAA pushed it along.
#if defined(WRITE_MSAA_DEPTH)
#define DOLL_SV_TARGET_NORMAL_DEPTHPASS SV_Target1
#else
#define DOLL_SV_TARGET_NORMAL_DEPTHPASS SV_Target0
#endif

// The motion vector pass binds the same targets in a different order: MSAA depth,
// then motion vectors, then the rendering layer buffer, and the normal buffer last.
#if defined(WRITE_DECAL_BUFFER_AND_RENDERING_LAYER) && defined(WRITE_MSAA_DEPTH)
#define DOLL_SV_TARGET_NORMAL_MOTIONPASS SV_Target3
#elif defined(WRITE_DECAL_BUFFER_AND_RENDERING_LAYER) || defined(WRITE_MSAA_DEPTH)
#define DOLL_SV_TARGET_NORMAL_MOTIONPASS SV_Target2
#else
#define DOLL_SV_TARGET_NORMAL_MOTIONPASS SV_Target1
#endif

// Motion vectors sit at target 0 unless MSAA put the depth-as-colour target there.
#if defined(WRITE_MSAA_DEPTH)
#define DOLL_SV_TARGET_MOTION SV_Target1
#else
#define DOLL_SV_TARGET_MOTION SV_Target0
#endif

// The octahedral encoding HDRP's own materials use, so what GTAO decodes is what a
// neighbouring MENACE surface would have given it.
//
// The roughness travelling alongside the normal is read by screen-space reflection,
// not by occlusion, which takes the normal alone. A material shipping no RMO map has
// no roughness to report and takes 1, which is the value that stops SSR trying to
// mirror off it.
void DollWriteNormalBuffer(float3 normalWS, float perceptualRoughness, out float4 outNormalBuffer)
{
    NormalData normalData;
    normalData.normalWS = normalWS;
    normalData.perceptualRoughness = perceptualRoughness;
    EncodeIntoNormalBuffer(normalData, outNormalBuffer);
}

// Under MSAA the pipeline reads depth back from a colour target rather than paying
// for a multisampled depth fetch, so a pass that writes depth has to write it there
// too. Alpha carries coverage for alpha-to-mask, and these surfaces do not alpha
// clip, so it is fully covered.
float4 DollDepthAsColour(float4 positionCS)
{
    return float4(positionCS.z, 0.0, 0.0, 1.0);
}

#endif
