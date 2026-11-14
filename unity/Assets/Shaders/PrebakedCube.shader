// GFL2's gf_shader/scene/prebaked_cubemap_scene, from its compiled programs: a box drawn from the
// inside (front faces culled) that shows a cube captured from the room, looked up by the
// normalised object-space position with y negated. The ops room's floor mirror renders this box and
// nothing else, so its reflection is a frozen image of the room (StrategyDiorama.Reflection.cs).
Shader "Womenace/PrebakedCube"
{
    Properties
    {
        [NoScaleOffset] _Cubemap ("Cubemap", Cube) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURECUBE(_Cubemap); SAMPLER(sampler_Cubemap);

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.direction = input.positionOS;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float3 colour = SAMPLE_TEXTURECUBE(_Cubemap, sampler_Cubemap, float3(d.x, -d.y, d.z)).rgb;
                return float4(colour * GetCurrentExposureMultiplier(), 1.0);
            }
            ENDHLSL
        }
    }
}
