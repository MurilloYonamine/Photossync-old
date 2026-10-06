Shader "PSX/CRT_Composite"
{
    Properties
    {
        [HideInInspector] _BlitTexture ("Source Texture", 2D) = "white" {}

        [Header(Pixelation)]
        _EnablePixelate ("Enable Pixelation", Float) = 1.0
        _PixelResolutionX ("Resolution Width", Float) = 320.0
        _PixelResolutionY ("Resolution Height", Float) = 240.0

        [Header(CRT Barrel Distortion)]
        _EnableBarrel ("Enable Barrel Distortion", Float) = 1.0
        _BarrelStrength ("Distortion Strength", Range(-1.0, 1.0)) = 0.12
        _BarrelTightness ("Tightness", Range(0.1, 10.0)) = 3.0
        _BarrelZoom ("Zoom", Range(0.5, 2.0)) = 0.98
        _Vignette ("Corner Vignette", Range(0.0, 1.0)) = 0.4

        [Header(Bayer Dithering)]
        _EnableDither ("Enable Dithering", Float) = 1.0
        _DitherSpread ("Quantization Levels", Range(2.0, 64.0)) = 16.0
        _DitherStrength ("Dither Intensity", Range(0.0, 1.0)) = 0.7

        [Header(Chroma Bleed Analogo RCA)]
        _EnableChromaBleed ("Enable Chroma Bleed", Float) = 1.0
        _BleedAmount ("Bleed Spread", Range(0.0, 0.02)) = 0.005

        [Header(Scanlines and Rolling Bands)]
        _EnableScanlines ("Enable Scanlines", Float) = 1.0
        _EnableRollingBands ("Enable Rolling Bands", Float) = 0.0
        _ScanlineCount ("Scanline Count", Range(50.0, 1200.0)) = 240.0
        _ScanlineIntensity ("Scanline Intensity", Range(0.0, 1.0)) = 0.3
        _RollingBandSpeed ("Band Speed", Range(-10.0, 10.0)) = 1.0
        _RollingBandIntensity ("Band Intensity", Range(0.0, 1.0)) = 0.1

        [Header(Glitch and VHS Tape Noise)]
        _EnableGlitch ("Enable VHS / Glitch", Float) = 1.0
        _GlitchAmount ("Jitter Amount", Range(0.0, 0.1)) = 0.01
        _VhsGrain ("Tape Grain Noise", Range(0.0, 1.0)) = 0.1
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        LOD 100
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "PSXCRTCompositePass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _EnablePixelate;
                float _PixelResolutionX;
                float _PixelResolutionY;

                float _EnableBarrel;
                float _BarrelStrength;
                float _BarrelTightness;
                float _BarrelZoom;
                float _Vignette;

                float _EnableDither;
                float _DitherSpread;
                float _DitherStrength;

                float _EnableChromaBleed;
                float _BleedAmount;

                float _EnableScanlines;
                float _EnableRollingBands;
                float _ScanlineCount;
                float _ScanlineIntensity;
                float _RollingBandSpeed;
                float _RollingBandIntensity;

                float _EnableGlitch;
                float _GlitchAmount;
                float _VhsGrain;
            CBUFFER_END

            static const float4x4 BAYER_4X4 = float4x4(
                0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
               12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
               15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            );

            float Hash12(float2 p)
            {
                p = frac(p * float2(5.3983, 5.4427));
                p += dot(p.yx, p.xy + float2(21.5351, 14.3137));
                return frac(p.x * p.y);
            }

            // Conversão RGB para YIQ (Espaço de cor NTSC)
            float3 RGB2YIQ(float3 c)
            {
                return float3(
                    0.299 * c.r + 0.587 * c.g + 0.114 * c.b,
                    0.596 * c.r - 0.274 * c.g - 0.322 * c.b,
                    0.211 * c.r - 0.523 * c.g + 0.312 * c.b
                );
            }

            float3 YIQ2RGB(float3 yiq)
            {
                return float3(
                    yiq.x + 0.956 * yiq.y + 0.621 * yiq.z,
                    yiq.x - 0.272 * yiq.y - 0.647 * yiq.z,
                    yiq.x - 1.106 * yiq.y + 1.703 * yiq.z
                );
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // 1. Barrel Distortion CRT
                float edgeMask = 1.0;
                if (_EnableBarrel > 0.5)
                {
                    float2 centeredUV = uv - 0.5;
                    float r2 = dot(centeredUV, centeredUV);
                    float distortion = 1.0 + _BarrelStrength * pow(abs(r2), _BarrelTightness * 0.5);
                    uv = (centeredUV * distortion) * _BarrelZoom + 0.5;

                    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    {
                        return half4(0, 0, 0, 1);
                    }

                    float edgeX = smoothstep(0.0, 0.04, uv.x) * smoothstep(1.0, 0.96, uv.x);
                    float edgeY = smoothstep(0.0, 0.04, uv.y) * smoothstep(1.0, 0.96, uv.y);
                    edgeMask = lerp(1.0, edgeX * edgeY, _Vignette);
                }

                // 2. Glitch / VHS Jitter
                if (_EnableGlitch > 0.5)
                {
                    float time = _Time.y * 12.0;
                    float lineNoise = Hash12(float2(floor(uv.y * 100.0), floor(time)));
                    if (lineNoise > 0.88)
                    {
                        uv.x += (Hash12(float2(time, uv.y)) - 0.5) * _GlitchAmount;
                    }
                }

                // 3. Pixelation
                if (_EnablePixelate > 0.5)
                {
                    float2 targetRes = float2(_PixelResolutionX, _PixelResolutionY);
                    uv = floor(uv * targetRes) / targetRes + (0.5 / targetRes);
                }

                // 4. Chroma Bleed (Sangramento Analógico RCA)
                half3 color;
                if (_EnableChromaBleed > 0.5)
                {
                    float3 centerCol = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
                    float3 leftCol   = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(_BleedAmount, 0)).rgb;
                    float3 rightCol  = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(_BleedAmount, 0)).rgb;

                    float3 yiqC = RGB2YIQ(centerCol);
                    float3 yiqL = RGB2YIQ(leftCol);
                    float3 yiqR = RGB2YIQ(rightCol);

                    // Borra o sinal I e Q (Crominância) horizontalmente mantendo o Y (Luminância) nítido
                    float3 finalYIQ = float3(yiqC.x, (yiqL.y + yiqC.y + yiqR.y) / 3.0, (yiqL.z + yiqC.z + yiqR.z) / 3.0);
                    color = YIQ2RGB(finalYIQ);
                }
                else
                {
                    color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
                }

                // 5. Bayer Dithering
                if (_EnableDither > 0.5)
                {
                    uint2 pixelPos = (uint2)(uv * _ScreenParams.xy);
                    float dither = BAYER_4X4[pixelPos.x % 4][pixelPos.y % 4] - 0.5;
                    float3 dithered = color + (dither * _DitherStrength / _DitherSpread);
                    color = floor(dithered * _DitherSpread) / _DitherSpread;
                }

                // 6. Scanlines & Rolling Bands
                if (_EnableScanlines > 0.5)
                {
                    float scanline = sin(uv.y * _ScanlineCount * 3.14159) * 0.5 + 0.5;
                    color *= lerp(1.0 - _ScanlineIntensity, 1.0, scanline);
                }

                if (_EnableRollingBands > 0.5)
                {
                    float roll = sin(uv.y * 10.0 - _Time.y * _RollingBandSpeed) * 0.5 + 0.5;
                    color = lerp(color, color * 0.7, roll * _RollingBandIntensity);
                }

                // 7. VHS Tape Noise
                if (_EnableGlitch > 0.5 && _VhsGrain > 0.0)
                {
                    float grain = (Hash12(uv + _Time.y) - 0.5) * _VhsGrain;
                    color += grain;
                }

                return half4(color * edgeMask, 1.0);
            }
            ENDHLSL
        }
    }
}
