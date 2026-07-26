// Learn/Rendering R3: indexed vertex pulling, texture sampling, MVP, depth/culling and Lambert light.

[[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];
[[vk::binding(1, 0)]] Texture2D g_textures[];
[[vk::binding(2, 0)]] SamplerState g_samplers[];

struct DrawArgs
{
    uint vertexBufferIndex;
    uint indexBufferIndex;
    uint textureIndex;
    uint samplerIndex;
    float4x4 model;
    float4x4 viewProjection;
    float4 lightDirection;
    uint stage;
    uint _pad0;
    uint _pad1;
    uint _pad2;
};
[[vk::push_constant]] DrawArgs g_args;

struct VSOut
{
    float4 position : SV_Position;
    float3 normal : NORMAL0;
    float2 uv : TEXCOORD0;
};

float3 SrgbToLinear(float3 value)
{
    return pow(max(value, 0.0), 2.2);
}

float3 LinearToSrgb(float3 value)
{
    return pow(saturate(value), 1.0 / 2.2);
}

[shader("vertex")]
VSOut vsMain(uint vertexId : SV_VertexID)
{
    uint index = g_buffers[g_args.indexBufferIndex].Load<uint>(vertexId * 4);
    uint address = index * 32;
    float3 position = asfloat(g_buffers[g_args.vertexBufferIndex].Load3(address + 0));
    float3 normal = asfloat(g_buffers[g_args.vertexBufferIndex].Load3(address + 12));
    float2 uv = asfloat(g_buffers[g_args.vertexBufferIndex].Load2(address + 24));

    VSOut output;
    if (g_args.stage == 0)
    {
        output.position = float4(position, 1.0);
        output.normal = normal;
    }
    else
    {
        float4 worldPosition = mul(g_args.model, float4(position, 1.0));
        output.position = mul(g_args.viewProjection, worldPosition);
        // The tutorial model uses rotation only. For non-uniform scale use transpose(inverse((float3x3)model)).
        output.normal = normalize(mul((float3x3)g_args.model, normal));
    }
    output.uv = uv;
    return output;
}

[shader("pixel")]
float4 psMain(VSOut input) : SV_Target
{
    float4 sampled = g_textures[g_args.textureIndex].Sample(g_samplers[g_args.samplerIndex], input.uv);
    float3 linear = SrgbToLinear(sampled.rgb);
    if (g_args.stage == 2)
    {
        float ndotl = saturate(dot(normalize(input.normal), normalize(-g_args.lightDirection.xyz)));
        linear *= 0.18 + 0.82 * ndotl;
    }
    return float4(LinearToSrgb(linear), sampled.a);
}
