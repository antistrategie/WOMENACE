// The motion vector program shared by Womenace/DollToon and Womenace/DollToonFace. Each
// shader's pass declares its own render state and keywords and includes this.
#ifndef DOLL_MOTION_PASS_INCLUDED
#define DOLL_MOTION_PASS_INCLUDED
#include "DollMotionVectors.hlsl"
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
#endif
