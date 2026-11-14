// The depth and normal prepass program shared by Womenace/DollToon and Womenace/DollToonFace.
// Each shader's pass declares its own render state and keywords and includes this.
#ifndef DOLL_DEPTH_PASS_INCLUDED
#define DOLL_DEPTH_PASS_INCLUDED
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
#endif
