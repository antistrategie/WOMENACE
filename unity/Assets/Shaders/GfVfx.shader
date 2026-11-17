// GFL2's effect shaders (gf_shader/vfx) for the strategy diorama's effects, rebuilt from GFL2's own
// particle systems and materials. GFL2 renders with URP, so its compiled shaders cannot load here.
// This carries each family's maths as its D3D11 programs compute it, over GFL2's own property names
// and values (Womenace.EditorTools.BuildRoomVfx copies a material's values across unchanged and
// picks the family). The families:
//
//   0 UV_Effect_UVAll_Fast, 1 UV_Effect_UVAll_Fast_2, 2 Particle_Base_Bland_Fast,
//   3 UV_Effect_Base_Fast, 4 UV_Effect_Base_Mask_Fast, 5 add, 6 gf_sfx_holographic3,
//   7 gf_vfx_rgb_Glitch_Noise_Fast, 8 gf_vfx_pixelization, 9 gf_shader/scene/Neonlight/Billboard
//   (an animated LED board in the room). GFL2's env_unlit scenery cards on effect meshes are the
//   room bake's to draw.
//
// Family 8 is the one exception to the programs: it is written from what its materials set and
// what their particles feed it, which is a tinted texture, a mask, and a dissolve by a noise
// texture that the particle's custom data drives. Its pixel grid and outline are left out, as
// the materials here have both switched off.
//
// GFL2 scales most families by a global, _GlobalSfxIntensityScale, which each scene's GameScene sets
// (the ops room 1, the refitting room 0.25) and StrategyDiorama sets per room. GFL2 has no exposure
// step (the room camera exposes at 1).
// The UVAll families take per-particle custom data (Custom1 on TEXCOORD1: uv offset, dissolve,
// distortion. Custom2 on TEXCOORD2: uv scale, edge width, edge multiplier) when _ShaderMode is 0.
Shader "Womenace/GfVfx"
{
    Properties
    {
        [HideInInspector] _Family ("Family", Float) = 0
        // Set by BuildRoomVfx on the materials of particles drawn as meshes.
        [HideInInspector] _CardFade ("Fades edge-on and close to the camera", Float) = 0
        // Render state, set per family by BuildRoomVfx from GFL2's own pass state.
        [HideInInspector] _WmSrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _WmDstBlend ("Dst Blend", Float) = 10
        [HideInInspector] _WmCull ("Cull", Float) = 0
        [HideInInspector] _WmZWrite ("Z Write", Float) = 0

        _MainTex ("Main", 2D) = "white" {}
        _Logo ("Logo (Billboard)", 2D) = "black" {}
        _LED ("LED (Billboard)", 2D) = "white" {}
        _GlitchLEDRGALogoB ("Glitch (Billboard)", 2D) = "white" {}
        _Distortions ("Distortions (Billboard)", 2D) = "white" {}
        [HDR] _LogoColor ("Logo Colour", Color) = (1, 1, 1, 0)
        [HDR] _GlitchR ("Glitch R", Color) = (0.559, 0.081, 0, 0)
        [HDR] _GlitchG ("Glitch G", Color) = (0.52, 0.431, 0.993, 0)
        _LEDIntensity ("LED Intensity", Float) = 0
        _GlitchRSpeed ("Glitch R Speed", Float) = 0.6
        _GlitchIntensityMaskA ("Glitch Mask", Float) = 0
        _PannerX ("Logo Panner X", Float) = 0
        _PannerY ("Logo Panner Y", Float) = 0
        _MaskTex ("Mask", 2D) = "white" {}
        _TurbulenceTex ("Turbulence", 2D) = "white" {}
        _Mask ("Second Mask (Base)", 2D) = "white" {}
        _DissolveTex ("Dissolve (UVAll 2)", 2D) = "white" {}
        _HolographicTex ("Holographic", 2D) = "white" {}
        _HolographicMask ("Holographic Mask", 2D) = "white" {}
        _VertexAnimationTex ("Vertex Animation", 2D) = "black" {}

        [HDR] _MainColor ("Main Colour", Color) = (1, 1, 1, 1)
        [HDR] _BackColor ("Back Colour", Color) = (1, 1, 1, 1)
        [HDR] _FixColor ("Fix Colour (Particle Base)", Color) = (1, 1, 1, 1)
        [HDR] _Color ("Colour (Glitch)", Color) = (1, 1, 1, 1)
        [HDR] _WidthColor ("Dissolve Edge Colour", Color) = (1, 1, 1, 1)
        [HDR] _DissolveEdgeColor ("Dissolve Edge Colour (UVAll 2)", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Float) = 1
        _Contrast ("Contrast", Float) = 1
        _Alpha ("Alpha", Float) = 1
        _AdditionalAlpha ("Additional Alpha", Float) = 1
        // The mod's own dimmer, the same for every family. A family's brightness is not one:
        // an effect many times brighter than the screen shows looks no different at a tenth.
        _WmShown ("Shown (0 to 1)", Range(0, 1)) = 1
        _ShaderMode ("Shader Mode (0 custom data, 1 material)", Float) = 1
        _MainTexClamp ("Main Clamp", Float) = 0
        _MainPannerX ("Main Panner X", Float) = 0
        _MainPannerY ("Main Panner Y", Float) = 0
        _PowerU ("Turbulence Panner U", Float) = 0
        _PowerV ("Turbulence Panner V", Float) = 0
        _DistortPower ("Distortion", Float) = 0
        _Dissolve ("Dissolve", Float) = 0
        _Hardness ("Dissolve Hardness", Float) = 0
        _EdgeWidth ("Dissolve Edge Width", Float) = 0
        _DepthMode ("Depth Mode", Float) = 0
        _CUTOUT ("Cutout", Float) = 0.5
        _MaskPannerX ("Mask Panner X (UVAll 2)", Float) = 0
        _MaskPannerY ("Mask Panner Y (UVAll 2)", Float) = 0
        _TurbulencePannerX ("Turbulence Panner X (UVAll 2)", Float) = 0
        _TurbulencePannerY ("Turbulence Panner Y (UVAll 2)", Float) = 0
        _TurbulencePower ("Turbulence Power (UVAll 2)", Float) = 0
        _DissolvePannerX ("Dissolve Panner X", Float) = 0
        _DissolvePannerY ("Dissolve Panner Y", Float) = 0
        _DissolveHardness ("Dissolve Hardness (UVAll 2)", Float) = 0
        _DissolveEdgeWidth ("Dissolve Edge Width (UVAll 2)", Float) = 0
        _MainTexPannerX ("Main Panner X (Particle, Base)", Float) = 0
        _MainTexPannerY ("Main Panner Y (Particle, Base)", Float) = 0
        _MaskTexPannerX ("Mask Panner X (Base)", Float) = 0
        _MaskTexPannerY ("Mask Panner Y (Base)", Float) = 0
        _NormalTexPannerX ("Turbulence Panner X (Base)", Float) = 0
        _NormalTexPannerY ("Turbulence Panner Y (Base)", Float) = 0
        _UVEffectPower ("Distortion (Base)", Float) = 0
        _MainTexBrightness ("Brightness (Base)", Float) = 1
        _MainTexContrast ("Contrast (Base)", Float) = 1
        _Gradient ("Gradient Wipe (Base)", Float) = 0
        _Dis ("Dissolve Threshold (Base Mask)", Float) = 1
        _globeDayNightUse ("Day Night", Float) = 0
        _SoftParticleNear ("Soft Particle Near", Float) = 0
        _SoftParticleFar ("Soft Particle Far", Float) = 2
        _BottomTopRange ("Height Range (Holographic)", Vector) = (0, 1, 0, 0)
        [HDR] _MainColor1 ("Main Colour Bottom", Color) = (1, 1, 1, 1)
        [HDR] _MainColor2 ("Main Colour Top", Color) = (1, 1, 1, 1)
        [HDR] _HolographicColor1 ("Holographic Colour Bottom", Color) = (1, 1, 1, 1)
        [HDR] _HolographicColor2 ("Holographic Colour Top", Color) = (1, 1, 1, 1)
        [HDR] _FresnelColor1 ("Fresnel Colour Bottom", Color) = (1, 1, 1, 1)
        [HDR] _FresnelColor2 ("Fresnel Colour Top", Color) = (1, 1, 1, 1)
        _FresnelRangeX ("Fresnel Range X", Float) = 0
        _FresnelRangeY ("Fresnel Range Y", Float) = 1
        _HolographicFlow ("Holographic Flow", Vector) = (0, 0, 0, 0)
        _HolographicMaskFlow ("Holographic Mask Flow", Vector) = (0, 0, 0, 0)
        _MaskFlow ("Mask Flow", Vector) = (0, 0, 0, 0)
        _MainTexFlow ("Main Flow (Pixelization)", Vector) = (0, 0, 0, 0)
        _DissolveTexFlow ("Dissolve Flow (Pixelization)", Vector) = (0, 0, 0, 0)
        _ClipThreshold ("Clip Threshold (Pixelization)", Float) = 0
        _HolographicMode ("Holographic Mode", Float) = 0
        _VertexAnimationFlow ("Vertex Animation Flow", Vector) = (0, 0, 0, 0)
        _VertexAnimationIntensity ("Vertex Animation Intensity", Float) = 0
        _Speed ("Glitch Speed", Float) = 1
        _GlitchNumbers ("Glitch Slices", Float) = 0
        _GlitchOffset ("Glitch Offset", Float) = 0
        _RGBOffset ("Glitch RGB Offset", Float) = 0
        // GFL2 keywords, as switches (BuildRoomVfx sets them from the material's keywords).
        _AlphaR ("Alpha From Red", Float) = 0
        _UseMask ("Use Mask", Float) = 0
        _UseTurbulence ("Use Turbulence", Float) = 0
        _UseClip ("Use Dissolve", Float) = 0
        _UseBackColor ("Use Back Colour", Float) = 0
        _SoftParticles ("Soft Particles", Float) = 0
        _Horizontal ("Glitch Horizontal", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend [_WmSrcBlend] [_WmDstBlend]
            ZWrite [_WmZWrite]
            ZTest LEqual
            Cull [_WmCull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Logo); SAMPLER(sampler_Logo);
            TEXTURE2D(_LED); SAMPLER(sampler_LED);
            TEXTURE2D(_GlitchLEDRGALogoB); SAMPLER(sampler_GlitchLEDRGALogoB);
            TEXTURE2D(_Distortions); SAMPLER(sampler_Distortions);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            TEXTURE2D(_TurbulenceTex); SAMPLER(sampler_TurbulenceTex);
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            TEXTURE2D(_DissolveTex); SAMPLER(sampler_DissolveTex);
            TEXTURE2D(_HolographicTex); SAMPLER(sampler_HolographicTex);
            TEXTURE2D(_HolographicMask); SAMPLER(sampler_HolographicMask);
            TEXTURE2D(_VertexAnimationTex); SAMPLER(sampler_VertexAnimationTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Logo_ST, _LED_ST, _LogoColor, _GlitchR, _GlitchG;
                float _LEDIntensity, _GlitchRSpeed, _GlitchIntensityMaskA, _PannerX, _PannerY;
                float4 _MainTex_ST, _MaskTex_ST, _TurbulenceTex_ST, _Mask_ST, _DissolveTex_ST;
                float4 _HolographicTex_ST, _HolographicMask_ST, _VertexAnimationTex_ST;
                float4 _MainColor, _BackColor, _FixColor, _Color, _WidthColor, _DissolveEdgeColor;
                float4 _BottomTopRange, _MainColor1, _MainColor2, _HolographicColor1, _HolographicColor2;
                float4 _FresnelColor1, _FresnelColor2, _HolographicFlow, _HolographicMaskFlow, _MaskFlow, _VertexAnimationFlow;
                float4 _MainTexFlow, _DissolveTexFlow;
                float _ClipThreshold;
                float _Family, _CardFade, _WmSrcBlend, _WmDstBlend, _WmCull, _WmZWrite;
                float _Brightness, _Contrast, _Alpha, _AdditionalAlpha, _ShaderMode, _MainTexClamp, _WmShown;
                float _MainPannerX, _MainPannerY, _PowerU, _PowerV, _DistortPower, _Dissolve, _Hardness, _EdgeWidth;
                float _DepthMode, _CUTOUT, _MaskPannerX, _MaskPannerY, _TurbulencePannerX, _TurbulencePannerY, _TurbulencePower;
                float _DissolvePannerX, _DissolvePannerY, _DissolveHardness, _DissolveEdgeWidth;
                float _MainTexPannerX, _MainTexPannerY, _MaskTexPannerX, _MaskTexPannerY, _NormalTexPannerX, _NormalTexPannerY;
                float _UVEffectPower, _MainTexBrightness, _MainTexContrast, _Gradient, _Dis, _globeDayNightUse;
                float _SoftParticleNear, _SoftParticleFar, _FresnelRangeX, _FresnelRangeY, _HolographicMode, _VertexAnimationIntensity;
                float _Speed, _GlitchNumbers, _GlitchOffset, _RGBOffset;
                float _AlphaR, _UseMask, _UseTurbulence, _UseClip, _UseBackColor, _SoftParticles, _Horizontal;
            CBUFFER_END

            float _GlobalSfxIntensityScale;
            #define GLOBAL_SFX_INTENSITY _GlobalSfxIntensityScale

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 colour : COLOR;
                float4 uv : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                float4 custom2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                float4 custom2 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
                float4 clip : TEXCOORD5;
            };

            float2 Uv(float2 uv, float4 st) { return uv * st.xy + st.zw; }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionOS = input.positionOS;
                if (_Family > 5.5 && _Family < 6.5)
                {
                    // Holographic: pushed out along the horizontal of its normal by a scrolling map.
                    float3 n = SafeNormalize(input.normalOS);
                    float3 horizontal = SafeNormalize(float3(dot(n.xz, UNITY_MATRIX_M._m00_m02), dot(n.xz, UNITY_MATRIX_M._m10_m12), dot(n.xz, UNITY_MATRIX_M._m20_m22)));
                    float2 uvAnim = Uv(input.uv.xy, _VertexAnimationTex_ST) + _Time.y * _VertexAnimationFlow.xy * 0.1;
                    positionOS += horizontal * SAMPLE_TEXTURE2D_LOD(_VertexAnimationTex, sampler_VertexAnimationTex, uvAnim, 0).r * _VertexAnimationIntensity;
                }
                float3 positionRWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.clip = output.positionCS;
                output.positionWS = GetAbsolutePositionWS(positionRWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.colour = input.colour;
                output.uv = input.uv.xy;
                output.custom1 = input.custom1;
                output.custom2 = input.custom2;
                return output;
            }

            // Soft particles: fade where the particle meets what lies behind it.
            float SoftFade(float4 positionCS)
            {
                if (_SoftParticles < 0.5)
                    return 1.0;
                float scene = LinearEyeDepth(LoadCameraDepth(uint2(positionCS.xy)), _ZBufferParams);
                return saturate((scene - _SoftParticleNear - positionCS.w) * _SoftParticleFar);
            }

            // Dave Hoskins' hash, as the glitch shader computes it.
            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float4 UvAll(Varyings input, bool front, bool second)
            {
                float t = _Time.y;
                // Material values, or the particle's custom data.
                float4 c1 = lerp(input.custom1, float4(float2(_MainPannerX, _MainPannerY) * t, _Dissolve, second ? _TurbulencePower : _DistortPower), _ShaderMode);
                float4 c2 = lerp(input.custom2 + float4(0, 0, 0, 1), float4(1, 1, second ? _DissolveEdgeWidth : _EdgeWidth, 1), _ShaderMode);
                float2 uvMain = input.uv * (c2.xy - 1.0) + (c1.xy + Uv(input.uv, _MainTex_ST)) - 0.5 * (c2.xy - 1.0);
                float noise = 0.5;
                if (_UseTurbulence > 0.5)
                {
                    float2 pan = second ? float2(_TurbulencePannerX, _TurbulencePannerY) : float2(_PowerU, _PowerV);
                    noise = SAMPLE_TEXTURE2D(_TurbulenceTex, sampler_TurbulenceTex, Uv(input.uv, _TurbulenceTex_ST) + pan * t).r;
                    uvMain += (noise - 0.5) * c1.w;
                }
                if (_MainTexClamp > 0.5)
                    uvMain = saturate(uvMain);
                float4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain);
                float3 vertexColour = input.colour.rgb * _Brightness * GLOBAL_SFX_INTENSITY;
                float vertexAlpha = input.colour.a * _MainColor.a * _Alpha * _AdditionalAlpha;
                float alpha = (_AlphaR > 0.5 ? main.r : main.a) * vertexAlpha;
                float edgeMask = 1.0;
                if (_UseClip > 0.5)
                {
                    float d = c1.z, edge = c2.z;
                    float hardness = second ? _DissolveHardness : _Hardness;
                    float n = second
                        ? SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, Uv(input.uv, _DissolveTex_ST) + float2(_DissolvePannerX, _DissolvePannerY) * t).r
                        : noise;
                    float tnoise = n + 1.0;
                    float k = 2.0 - hardness;
                    float soft = max(1.0 - hardness, 1e-4);
                    alpha *= saturate((tnoise - (d * (edge + 1.0) - edge) * k - hardness) / soft);
                    edgeMask = saturate((tnoise - d * (edge + 1.0) * k - hardness) / soft);
                }
                if (_UseMask > 0.5)
                {
                    float2 pan = second ? float2(_MaskPannerX, _MaskPannerY) * t : 0.0;
                    alpha *= SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, Uv(input.uv, _MaskTex_ST) + pan).r;
                }
                alpha = min(alpha, 1.0);
                clip(alpha - min(_DepthMode, _CUTOUT));
                float3 tint = (_UseBackColor > 0.5 && !front) ? _BackColor.rgb : _MainColor.rgb;
                float3 colour = pow(max(main.rgb, 0.0), max(_Contrast, 0.01)) * tint * vertexColour;
                float3 edgeColour = colour * (second ? _DissolveEdgeColor.rgb : _WidthColor.rgb) * c2.w;
                colour = lerp(edgeColour, colour, edgeMask);
                return lerp(float4(colour, alpha), float4(colour, 1.0), _DepthMode);
            }

            float4 ParticleBase(Varyings input)
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, Uv(input.uv, _MainTex_ST) + float2(_MainTexPannerX, _MainTexPannerY) * _Time.y);
                float3 colour = tex.rgb * input.colour.rgb * _FixColor.rgb * _Brightness * GLOBAL_SFX_INTENSITY * (1.0 - _globeDayNightUse);
                float alpha = tex.a * input.colour.a * _FixColor.a * _AdditionalAlpha * SoftFade(input.positionCS);
                // GFL2's window light shafts are flat cards fixed in the room, made for its own
                // few camera angles. Here the room is seen from many more, so a card fades out
                // as it turns edge-on to the camera, where it would show as a line or a slab, and
                // close to the camera, where the view would pass through a hard sheet. Only a
                // card's material has the fade on. The card's own facing is taken from its
                // surface, since a particle scaled flat carries no usable normal.
                float3 facing = normalize(cross(ddx(input.positionWS), ddy(input.positionWS)));
                float3 toCamera = GetWorldSpaceNormalizeViewDir(GetCameraRelativePositionWS(input.positionWS));
                alpha *= lerp(1.0, smoothstep(0.06, 0.4, abs(dot(facing, toCamera))) * saturate((input.positionCS.w - 0.3) / 1.2), _CardFade);
                return float4(colour, alpha);
            }

            float4 Base(Varyings input, bool withMask)
            {
                float t = _Time.y;
                float noise = SAMPLE_TEXTURE2D(_TurbulenceTex, sampler_TurbulenceTex, (input.uv + float2(_NormalTexPannerX, _NormalTexPannerY) * t) * _TurbulenceTex_ST.xy + _TurbulenceTex_ST.zw).r;
                float2 uvMain = (input.uv + float2(_MainTexPannerX, _MainTexPannerY) * t + (withMask ? 0.0 : noise * _UVEffectPower)) * _MainTex_ST.xy + _MainTex_ST.zw;
                float4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain);
                float mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, (input.uv + float2(_MaskTexPannerX, _MaskTexPannerY) * t) * _MaskTex_ST.xy + _MaskTex_ST.zw).r;
                float contrast = withMask ? max(_MainTexContrast, 0.01) : _MainTexContrast;
                float3 colour = pow(max(main.rgb * _MainTexBrightness * GLOBAL_SFX_INTENSITY, 0.0), contrast) * input.colour.rgb * _MainColor.rgb;
                float alpha;
                if (withMask)
                {
                    clip(noise + _Dis - 0.5);
                    alpha = mask * main.a * input.colour.a * _MainColor.a * _AdditionalAlpha;
                }
                else
                {
                    clip((input.uv.x - frac(_Gradient)) * (_Gradient - 1.0));
                    alpha = saturate(pow(max(main.r * mask, 0.0), _MainTexContrast) * input.colour.a * _MainColor.a * _AdditionalAlpha)
                        * SAMPLE_TEXTURE2D(_Mask, sampler_Mask, Uv(input.uv, _Mask_ST)).r;
                }
                return float4(colour, alpha);
            }

            float4 Add(Varyings input)
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, Uv(input.uv, _MainTex_ST) + float2(_MainPannerX, _MainPannerY) * _Time.y);
                float3 colour = pow(max(tex.rgb, 0.0), _Contrast) * tex.a * _MainColor.rgb * _MainColor.a * _Brightness * GLOBAL_SFX_INTENSITY
                    * input.colour.rgb * input.colour.a;
                return float4(colour, 1.0);
            }

            float4 Holographic(Varyings input)
            {
                float t = _Time.y;
                // HDRP renders camera-relative: the camera is at the origin of relative space.
                float3 V = SafeNormalize(GetAbsolutePositionWS(0.0) - input.positionWS);
                float fresnel = smoothstep(0.0, 1.0, saturate((abs(dot(V, SafeNormalize(input.normalWS))) - _FresnelRangeX) / (_FresnelRangeY - _FresnelRangeX)));
                fresnel *= SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, Uv(input.uv, _MaskTex_ST) + _MaskFlow.xy * t * 0.1).r;
                // The dot matrix and its noise, in screen space about the object's pivot and scaled
                // by its distance, so they hold their size on screen as the hologram moves.
                float3 pivotRWS = UNITY_MATRIX_M._m03_m13_m23;
                float4 pivotClip = TransformWorldToHClip(pivotRWS);
                float2 offset = (input.clip.xy / input.clip.w - pivotClip.xy / pivotClip.w) * 0.5 * pivotClip.w;
                float holoMask = SAMPLE_TEXTURE2D(_HolographicMask, sampler_HolographicMask, Uv(offset, _HolographicMask_ST) + _HolographicMaskFlow.xy * t * 0.1).r;
                float holo = holoMask * SAMPLE_TEXTURE2D(_HolographicTex, sampler_HolographicTex, Uv(offset, _HolographicTex_ST) + _HolographicFlow.xy * t * 0.1).r;
                float pivotY = UNITY_MATRIX_M._m13 + GetAbsolutePositionWS(0.0).y;
                float h = saturate((input.positionWS.y - (pivotY + _BottomTopRange.x)) / (_BottomTopRange.y - _BottomTopRange.x));
                float3 holoColour = lerp(_HolographicColor1.rgb * _HolographicColor1.a, _HolographicColor2.rgb * _HolographicColor2.a, h) * holo;
                float4 main = lerp(_MainColor1, _MainColor2, h) * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, Uv(input.uv, _MainTex_ST));
                float3 fresnelColour = lerp(_FresnelColor1.rgb * _FresnelColor1.a, _FresnelColor2.rgb * _FresnelColor2.a, h);
                float3 colour = (holoColour + main.rgb * main.a + fresnelColour * fresnel) * _AdditionalAlpha;
                float holoAlpha = saturate(lerp(_HolographicColor1.a, _HolographicColor2.a, h) * holo + main.a);
                float alpha = (_HolographicMode * (holoAlpha - main.a) + main.a) * _AdditionalAlpha;
                return float4(colour, alpha);
            }

            float4 Glitch(Varyings input)
            {
                float2 uv = Uv(input.uv, _MainTex_ST);
                float2 dx = ddx(uv), dy = ddy(uv);
                // Slices across (or along, horizontal) the texture, each shifted by a hashed amount
                // that changes _Speed times a second.
                float across = _Horizontal > 0.5 ? input.uv.y : input.uv.x;
                float along = _Horizontal > 0.5 ? uv.x : uv.y;
                float step = floor(_Time.y * _Speed);
                for (float i = 0; i < min(_GlitchNumbers, 20.0); i++)
                {
                    float start = Hash12(float2(step, i + 2345.0));
                    float end = min(start + Hash12(float2(step, i + 9035.0)) * 0.125, 1.0);
                    if (across >= start && across < end)
                        along += Hash12(float2(step, i + 9625.0)) * _GlitchOffset - _GlitchOffset * 0.5;
                }
                float2 shifted = _Horizontal > 0.5 ? float2(along, uv.y) : float2(uv.x, along);
                float4 tint = input.colour * _Color * _Color;
                tint.a *= _AdditionalAlpha;
                float4 colour = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, shifted, dx, dy) * tint;
                // One channel split off by a hashed offset.
                float pick = Hash12(float2(step, 9545.0));
                float2 split = float2(pick, Hash12(float2(step, 7205.0))) * _RGBOffset * 0.3 - _RGBOffset * 0.15;
                float4 moved = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, shifted + split, dx, dy) * tint;
                if (pick < 0.33)
                    colour.r = moved.r;
                else if (pick < 0.66)
                    colour.g = moved.g;
                else
                    colour.b = moved.b;
                return colour;
            }

            float4 Pixelization(Varyings input)
            {
                float t = _Time.y;
                // The particle's custom data: its texture's offset, and how far it has dissolved.
                float4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, Uv(input.uv, _MainTex_ST) + input.custom1.xy + _MainTexFlow.xy * t);
                float alpha = main.a * input.colour.a * _MainColor.a * _AdditionalAlpha;
                if (_UseMask > 0.5)
                    alpha *= SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, Uv(input.uv, _MaskTex_ST) + float2(_MaskTexPannerX, _MaskTexPannerY) * t).r;
                if (_UseClip > 0.5)
                {
                    float noise = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, Uv(input.uv, _DissolveTex_ST) + _DissolveTexFlow.xy * t).r;
                    clip(noise - (_Dissolve + input.custom1.z) * 1.01);
                }
                clip(alpha - max(_ClipThreshold, 0.001));
                return float4(main.rgb * _MainColor.rgb * input.colour.rgb * GLOBAL_SFX_INTENSITY, alpha);
            }

            float4 Billboard(Varyings input)
            {
                float t = _Time.y;
                float s = sin(t);
                float flicker = saturate((s * s * s * 3.25 + 2.0) * 0.1 + 0.7);
                float3 colour = flicker * SAMPLE_TEXTURE2D(_LED, sampler_LED, Uv(input.uv, _LED_ST)).rgb * _LEDIntensity;
                float2 uv = Uv(input.uv, _Logo_ST);
                colour += _GlitchR.rgb * SAMPLE_TEXTURE2D(_GlitchLEDRGALogoB, sampler_GlitchLEDRGALogoB, uv + float2(0.0, t * _GlitchRSpeed)).r;
                colour += _GlitchG.rgb * SAMPLE_TEXTURE2D(_GlitchLEDRGALogoB, sampler_GlitchLEDRGALogoB, uv + t * float2(2.0, 3.0)).g;
                float logoMask = SAMPLE_TEXTURE2D(_GlitchLEDRGALogoB, sampler_GlitchLEDRGALogoB, uv + t * float2(-5.0, -2.3)).b;
                colour *= saturate(SAMPLE_TEXTURE2D(_GlitchLEDRGALogoB, sampler_GlitchLEDRGALogoB, uv + t * float2(-6.0, 5.0)).a + _GlitchIntensityMaskA);
                float distortion = (SAMPLE_TEXTURE2D(_Distortions, sampler_Distortions, uv + t * float2(1.0, 2.0)).r
                    + SAMPLE_TEXTURE2D(_Distortions, sampler_Distortions, uv + t * float2(-1.8, 0.3)).g
                    + SAMPLE_TEXTURE2D(_Distortions, sampler_Distortions, uv + t * float2(0.8, -1.5)).b
                    + SAMPLE_TEXTURE2D(_Distortions, sampler_Distortions, uv + t * float2(-0.8, -1.5)).a) * _DistortPower;
                float2 uvLogo = uv + t * ((float2(_PannerX, _PannerY) + 1.0) * 1.5 - 1.5) + distortion * 0.5;
                colour += _LogoColor.rgb * logoMask * SAMPLE_TEXTURE2D(_Logo, sampler_Logo, uvLogo).rgb;
                return float4(colour, distortion);
            }

            float4 frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                float4 result;
                if (_Family < 0.5)
                    result = UvAll(input, front, false);
                else if (_Family < 1.5)
                    result = UvAll(input, front, true);
                else if (_Family < 2.5)
                    result = ParticleBase(input);
                else if (_Family < 3.5)
                    result = Base(input, false);
                else if (_Family < 4.5)
                    result = Base(input, true);
                else if (_Family < 5.5)
                    result = Add(input);
                else if (_Family < 6.5)
                    result = Holographic(input);
                else if (_Family < 7.5)
                    result = Glitch(input);
                else if (_Family < 8.5)
                    result = Pixelization(input);
                else
                    result = Billboard(input);
                float3 colour = max(0.0, result.rgb) * GetCurrentExposureMultiplier();
                colour = (colour == colour) ? colour : 0.0;
                // Less of it shows by its alpha, or by its colour where the blend adds the
                // colour whole.
                return float4(colour * (_WmSrcBlend < 1.5 ? _WmShown : 1.0), saturate(result.a) * _WmShown);
            }
            ENDHLSL
        }
    }
}
