#pragma once

namespace dfl::chapter_two
{
    // HLSL adaptation of Libretro's NTSC-Adaptive (GPL-3.0-only, based on
    // Themaister's NTSC shader) and Timothy Lottes' public-domain CRT shader.
    // See ntsc-upstream/PROVENANCE.md for the included sources and license.
    inline constexpr char NtscShaderSource[] = R"HLSL(
Texture2D<float4> Input : register(t0);
cbuffer Settings : register(b0) { int4 Preset; };

static const int Width = 640;
static const int Height = 480;
static const int SignalWidth = Width * 4;
static const float Pi = 3.14159265;

float4 VertexMain(uint id : SV_VertexID) : SV_Position
{
    const float2 corner = float2((id << 1) & 2, id & 2);
    return float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 EncodeMain(float4 position : SV_Position) : SV_Target
{
    const int2 pixel = int2(position.xy);
    const float3 rgb = Input.Load(int3(pixel.x / 4, pixel.y, 0)).rgb;
    const float y = dot(rgb, float3(0.2989, 0.5870, 0.1140));
    const float i = dot(rgb, float3(0.5959, -0.2744, -0.3216));
    const float q = dot(rgb, float3(0.2115, -0.5229, 0.3114));

    // At 640 pixels NTSC-Adaptive selects its two-phase carrier. The web
    // preview numbers texture rows bottom-up and starts at field zero.
    const float row = Height - position.y;
    const float phase = Pi * fmod(row, 2.0) + position.x * (4.0 * Pi / 15.0);
    float sine, cosine;
    sincos(phase, sine, cosine);
    const float modulatedI = i * cosine;
    const float modulatedQ = q * sine;
    const float crosstalk = Preset.x;
    return float4(y + crosstalk * (modulatedI + modulatedQ),
        (y * crosstalk + 2.0 * modulatedI) * cosine,
        (y * crosstalk + 2.0 * modulatedQ) * sine, 1.0);
}

// Symmetric 65-tap luma and chroma kernels from NTSC-Adaptive.
static const float2 Kernel[33] = {
    float2(-0.000174844, 0.001384762), float2(-0.000205844, 0.001678312),
    float2(-0.000149453, 0.002021715), float2(-0.000051693, 0.002420562),
    float2( 0.000000000, 0.002880460), float2(-0.000066171, 0.003406879),
    float2(-0.000245058, 0.004004985), float2(-0.000432928, 0.004679445),
    float2(-0.000472644, 0.005434218), float2(-0.000252236, 0.006272332),
    float2( 0.000198929, 0.007195654), float2( 0.000687058, 0.008204665),
    float2( 0.000944112, 0.009298238), float2( 0.000803467, 0.010473450),
    float2( 0.000363199, 0.011725413), float2( 0.000013422, 0.013047155),
    float2( 0.000253402, 0.014429548), float2( 0.001339461, 0.015861306),
    float2( 0.002932972, 0.017329037), float2( 0.003983485, 0.018817382),
    float2( 0.003026683, 0.020309220), float2(-0.001102056, 0.021785952),
    float2(-0.008373026, 0.023227857), float2(-0.016897700, 0.024614500),
    float2(-0.022914480, 0.025925203), float2(-0.021642347, 0.027139546),
    float2(-0.008863273, 0.028237893), float2( 0.017271957, 0.029201910),
    float2( 0.054921920, 0.030015081), float2( 0.098342579, 0.030663170),
    float2( 0.139044281, 0.031134640), float2( 0.168055832, 0.031420995),
    float2( 0.178571429, 0.031517031)
};

float3 SignalAt(int x, int y)
{
    // The longest tap reaches 32 signal samples beyond either edge.
    x = max(x, -x - 1);
    x = min(x, 2 * SignalWidth - x - 1);
    return Input.Load(int3(x, y, 0)).rgb;
}

float4 DecodeMain(float4 position : SV_Position) : SV_Target
{
    const int2 pixel = int2(position.xy);
    const int center = pixel.x * 2;
    float3 yiq = 0.0;
    [unroll] for (int tap = 0; tap < 32; ++tap)
    {
        const int offset = 32 - tap;
        const float3 samples = SignalAt(center - offset, pixel.y) + SignalAt(center + offset, pixel.y);
        yiq += samples * Kernel[tap].xyy;
    }
    yiq += SignalAt(center, pixel.y) * Kernel[32].xyy;
    return float4(dot(yiq, float3(1.0, 0.956, 0.6210)),
        dot(yiq, float3(1.0, -0.2720, -0.6474)),
        dot(yiq, float3(1.0, -1.1060, 1.7046)), 1.0);
}

float LinearChannel(float value)
{
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
}

float SrgbChannel(float value)
{
    return value < 0.0031308 ? value * 12.92 : 1.055 * pow(value, 0.41666) - 0.055;
}

float3 LinearPixel(int2 pixel, int inputWidth)
{
    const float3 rgb = Input.Load(int3(clamp(pixel, int2(0, 0), int2(inputWidth - 1, Height - 1)), 0)).rgb;
    return float3(LinearChannel(rgb.r), LinearChannel(rgb.g), LinearChannel(rgb.b));
}

float Gaussian(float distance, float hardness)
{
    return exp2(hardness * distance * distance);
}

float4 CrtMain(float4 position : SV_Position) : SV_Target
{
    const int inputWidth = Preset.x == 3 ? Width : Width * 2;
    const float2 beamPosition = float2(position.x * (float(inputWidth) / Width), position.y);
    const int2 center = int2(beamPosition);
    const float2 distance = 0.5 - frac(beamPosition);

    // Lottes' 3/5/3 beam and 5/7/7/7/5 bloom in linear light. Reuse
    // each linearized fetch across the two kernels on overlapping rows.
    float3 beam = 0.0;
    float3 bloom = 0.0;
    [unroll] for (int row = -2; row <= 2; ++row)
    {
        float3 beamLine = 0.0;
        float3 bloomLine = 0.0;
        float beamWeight = 0.0;
        float bloomWeight = 0.0;
        const int beamRadius = row == 0 ? 2 : 1;
        const int bloomRadius = abs(row) == 2 ? 2 : 3;
        [unroll] for (int column = -3; column <= 3; ++column)
        {
            if (abs(column) <= bloomRadius)
            {
                const float3 light = LinearPixel(center + int2(column, row), inputWidth);
                const float wide = Gaussian(distance.x + column, -1.5);
                bloomLine += light * wide;
                bloomWeight += wide;
                if (abs(row) <= 1 && abs(column) <= beamRadius)
                {
                    const float narrow = Gaussian(distance.x + column, -3.0);
                    beamLine += light * narrow;
                    beamWeight += narrow;
                }
            }
        }
        bloom += bloomLine * (Gaussian(distance.y + row, -2.0) / bloomWeight);
        if (abs(row) <= 1)
            beam += beamLine * (Gaussian(distance.y + row, -8.0) / beamWeight);
    }

    // The reference's flat screen and 15% aperture-grille setting. The
    // mask is evaluated at 640x480 along with the rest of the effect.
    const int phosphor = int(position.x) % 3;
    const float3 mask = 0.925 + 0.15 * float3(phosphor == 0, phosphor == 1, phosphor == 2);
    const float3 light = (beam + 0.15 * bloom) * mask;
    return float4(SrgbChannel(light.r), SrgbChannel(light.g), SrgbChannel(light.b), 1.0);
}
)HLSL";
}
