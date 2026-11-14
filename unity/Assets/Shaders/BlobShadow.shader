// A soft contact shadow on the floor under a doll standing in a baked room. The room's lighting is
// baked and ignores live lights, so a doll casts nothing; GFL2 bakes a shadow patch under each of
// its staged dolls instead. This draws an elliptical falloff on a floor quad that darkens what is
// already there (multiply), densest under the feet.
Shader "Womenace/BlobShadow"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 0.8
        _Softness ("Softness", Range(0.05, 1)) = 0.65
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-100" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend Zero SrcColor
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float _Softness;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float r = length(input.uv * 2.0 - 1.0);
                float shade = _Strength * (1.0 - smoothstep(1.0 - _Softness, 1.0, r));
                return float4((1.0 - shade).xxx, 1.0);
            }
            ENDHLSL
        }
    }
}
