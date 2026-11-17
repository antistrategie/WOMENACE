Shader "Womenace/DollHologram"
{
    // A doll's own model shown as a hologram in a room: her textures' light and dark in one
    // colour with a little of her own colours left in, a glow along her outline, fine lines
    // with the room showing between them and a slow flicker. She is drawn in from the floor up, behind a bright line.
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        [HDR] _Tint ("Tint", Color) = (0.35, 0.9, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Opacity ("Shown (0 hides the part)", Range(0, 1)) = 1
        _AlphaClip ("Cut out by the map's alpha (a picture on a card)", Range(0, 1)) = 0
        _Floor ("Brightness of her darkest parts", Float) = 0.12
        _Contrast ("Brightness of her lightest parts", Float) = 0.9
        _OwnColour ("How much of her own colours shows", Range(0, 1)) = 0.35
        _RimPower ("Rim falloff", Float) = 2.2
        _RimBoost ("Rim brightness", Float) = 0.9
        _ScanDensity ("Lines per metre", Float) = 110
        _ScanGap ("Gap between lines (share of a line)", Range(0, 1)) = 0.4
        _ScanSpeed ("Line drift (metres a second)", Float) = 0.04
        _ScanRows ("Lines by the screen's rows: how many to its height (0 for lines fixed to the room)", Float) = 0
        _ScanRowSpeed ("Their drift (rows a second)", Float) = 12
        _FloorY ("Floor height (world)", Float) = 0
        _Height ("Body height", Float) = 1.8
        _Reveal ("Drawn in (0 to 1 of the height)", Range(0, 1.1)) = 1.1
        // A hologram is a display colour: at full weight MENACE's physical exposure scales it
        // to near nothing.
        _ExposureWeight ("Exposure weight", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _Tint;
            float _Intensity, _Opacity, _AlphaClip, _RimPower, _RimBoost, _ScanDensity, _ScanGap, _ScanSpeed, _Floor, _Contrast, _OwnColour, _FloorY, _Height, _Reveal, _ExposureWeight, _ScanRows, _ScanRowSpeed;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionRWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
        };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.positionRWS = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(output.positionRWS);
            output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
            return output;
        }
        // How far up the body a point is, 0 at the floor and 1 at the body's height.
        float Up(float3 positionRWS)
        {
            return (GetAbsolutePositionWS(positionRWS).y - _FloorY) / max(0.01, _Height);
        }
        // Cuts away what is not shown: what is not drawn in yet, the gaps between her lines and
        // a card's surround. Returns how much of the pixel is the line she is drawn in behind.
        float Cut(Varyings input)
        {
            float up = Up(input.positionRWS);
            clip(min(_Reveal - up, _Opacity - 0.001));

            // Lines fixed to the room, drifting upward. The line she is drawn in behind
            // is whole.
            float y = GetAbsolutePositionWS(input.positionRWS).y;
            float wave = frac((y - _Time.y * _ScanSpeed) * _ScanDensity);
            // Lines by the screen's own rows, for a thing seen from where lines fixed to the
            // room would be finer than a pixel. Such a line is in a pixel on one frame and
            // out of it on the next, as the frame's sample moves, and with a lit window
            // behind it the pixel goes from her colour to many times as bright and back.
            // A whole row is cut or kept, the same on every frame.
            if (_ScanRows > 0.0)
            {
                float period = max(3.0, round(_ScreenSize.y / _ScanRows));
                float gap = max(1.0, round(period * _ScanGap));
                float row = floor(input.positionCS.y) + floor(_Time.y * _ScanRowSpeed);
                wave = fmod(row, period) + 0.5 < gap ? 0.0 : 1.0;
            }
            float front = saturate(1.0 - (_Reveal - up) * _Height / 0.05) * step(_Reveal, 1.0);
            clip(max(wave - _ScanGap, front - 0.5));
            clip(lerp(1.0, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - 0.5, _AlphaClip));
            return front;
        }
        ENDHLSL

        // Her depth, ahead of the frame's colour. The pipeline's effects read the depth of a
        // frame before any colour is drawn, and one that is not in it is drawn over by what
        // stands behind her own front (her back hair, the weapon on her back) on the frames
        // an effect takes the depth as it found it.
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            float4 FragDepth(Varyings input) : SV_Target
            {
                Cut(input);
                return 0;
            }
            ENDHLSL
        }

        // Drawn solid, with depth, so her nearest surface alone shows and the layers of hair,
        // cloth and body behind it never add up into bright patches. The room shows through
        // the gaps between her lines.
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            // Both sides: the dolls' meshes come from the glTF import mirrored, which flips their
            // winding, and a culled back face there is the body's front.
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings input) : SV_Target
            {
                float front = Cut(input);

                float3 n = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionRWS);
                float rim = pow(saturate(1.0 - abs(dot(n, view))), _RimPower);
                float4 map = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float3 own = map.rgb;
                float light = dot(own, float3(0.2126, 0.7152, 0.0722));
                float flicker = 0.95 + 0.05 * sin(_Time.y * 31.0) * sin(_Time.y * 7.3);

                // Her own colours show faintly through the one colour, which tells her hair from
                // her skin from her clothes where their light and dark alone would not.
                float3 shaded = _Tint.rgb * (_Floor + light * light * _Contrast);
                float3 colour = (lerp(shaded, own * (0.4 + 0.6 * _Tint.rgb) * (_Floor + _Contrast), _OwnColour) + _Tint.rgb * rim * _RimBoost) * flicker;
                colour += float3(1.0, 1.0, 1.0) * front * 2.0;
                colour *= _Intensity * lerp(1.0, GetCurrentExposureMultiplier(), _ExposureWeight);
                return float4(colour, 1.0);
            }
            ENDHLSL
        }
    }
}
