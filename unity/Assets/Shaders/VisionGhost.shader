Shader "Womenace/VisionGhost"
{
    // Nemesis's foresight afterimage: a still copy of an enemy drawn as purple smoke where
    // it is about to stand. A rim glow carries the silhouette, world-space noise rising
    // through the body reads as smoke, and the upper body frays away into wisps.
    Properties
    {
        _NoiseTex ("Smoke noise", 2D) = "grey" {}
        [HDR] _RimColor ("Rim colour", Color) = (1.3, 0.55, 2.4, 1)
        [HDR] _CoreColor ("Core colour", Color) = (0.45, 0.12, 0.85, 1)
        _RimPower ("Rim falloff", Float) = 2.5
        _CoreAlpha ("Core opacity", Range(0, 1)) = 0.18
        _NoiseScale ("Noise scale", Float) = 1.6
        _RiseSpeed ("Rise speed", Float) = 0.35
        _Height ("Body height", Float) = 2
        _FrayStart ("Fray start (fraction of height)", Range(0, 1)) = 0.55
        _Inflate ("Shell offset (metres along the normal)", Float) = 0.035
        _Alpha ("Opacity", Range(0, 1)) = 1
        // A vision is a display colour, like GFL2's decorative glows: at full weight MENACE's
        // physical exposure scales it to near zero and the ghost reads as a black silhouette.
        _ExposureWeight ("Exposure weight", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            // Both sides: the dolls' meshes come from the glTF import mirrored, which flips their
            // winding, and a culled back face there is the body's front.
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST, _RimColor, _CoreColor;
                float _RimPower, _CoreAlpha, _NoiseScale, _RiseSpeed, _Height, _FrayStart, _Inflate, _Alpha, _ExposureWeight;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Pushed out along the normal in world units, whatever the part's import
                // scale: an enemy that stays put has its ghost exactly on its own body, where
                // the real surface's depth would hide it, so the ghost is a shell around it.
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionRWS = TransformObjectToWorld(input.positionOS) + normalize(output.normalWS) * _Inflate;
                output.positionCS = TransformWorldToHClip(output.positionRWS);
                return output;
            }
            float Smoke(float3 p, float3 n, float t)
            {
                // Two planar projections blended by the normal, both scrolling upward, so
                // the noise is fixed to the world and the smoke rises however the camera moves.
                float2 rise = float2(0, -t * _RiseSpeed);
                float a = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xy * _NoiseScale + rise).r;
                float b = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.zy * _NoiseScale + rise).r;
                float fine = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, (p.xz + p.zy) * _NoiseScale * 2.3 + rise * 1.7).r;
                return saturate(lerp(a, b, abs(n.x)) * 0.75 + fine * 0.35);
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionRWS);
                float rim = pow(saturate(1.0 - abs(dot(n, view))), _RimPower);

                // Camera-relative rendering: noise and height read absolute world space.
                float3 p = GetAbsolutePositionWS(input.positionRWS);
                float originY = GetAbsolutePositionWS(TransformObjectToWorld(float3(0, 0, 0))).y;
                float height = saturate((p.y - originY) / max(0.01, _Height));
                float smoke = Smoke(p, n, _Time.y);

                // The body thins toward the head: the threshold climbs with height, so the
                // smoke breaks into gaps and the top dissolves.
                float threshold = smoothstep(_FrayStart, 1.05, height) * 0.9;
                float keep = saturate((smoke - threshold) / 0.18);

                float pulse = 0.88 + 0.12 * sin(_Time.y * 2.1);
                float opacity = saturate((_CoreAlpha + rim) * (0.55 + smoke * 0.9)) * keep * _Alpha * pulse;
                float3 colour = lerp(_CoreColor.rgb, _RimColor.rgb, saturate(rim * 1.4)) * (0.65 + smoke * 0.7);
                colour *= lerp(1.0, GetCurrentExposureMultiplier(), _ExposureWeight);
                return float4(colour * opacity, opacity);
            }
            ENDHLSL
        }
    }
}
