// Scene-generic PBR-lite + material buffer (Sample 83+ 用)。
// Vertex 32B (pos 12 + normal 12 + uv 8、SceneBuilder.Vertex)。
// Instance 80B (world 64 + materialIndex 4 + pad 12)。
// Material 32B (baseColor 16 + texIdx 4 + sampIdx 4 + flags 4 + pad 4)。

[[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];
[[vk::binding(1, 0)]] Texture2D g_textures[];
[[vk::binding(2, 0)]] SamplerState g_samplers[];

struct RootArgs
{
    float4x4 viewProj;         // 64B
    uint vertexBufIndex;
    uint indexBufIndex;        // 0xFFFFFFFF なら non-indexed
    uint instanceBufIndex;
    uint materialBufIndex;
    uint _pad0, _pad1, _pad2, _pad3;
};
[[vk::push_constant]] RootArgs args;

struct VsOut
{
    float4 svPos : SV_Position;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    nointerpolation uint matIdx : TEXCOORD1;
};

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    uint actualVid = (args.indexBufIndex == 0xFFFFFFFFu)
        ? vid
        : g_buffers[args.indexBufIndex].Load<uint>(vid * 4);
    uint vAddr = actualVid * 32;
    float3 vPos    = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 0));
    float3 vNormal = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 12));
    float2 vUv     = asfloat(g_buffers[args.vertexBufIndex].Load2(vAddr + 24));

    // Instance 80B: world(64) + materialIndex(4) + pad(12)
    uint iAddr = iid * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    float4x4 world = float4x4(r0, r1, r2, r3);
    uint matIdx = g_buffers[args.instanceBufIndex].Load<uint>(iAddr + 64);

    float4 worldPos = mul(float4(vPos, 1.0), world);
    float3 worldN   = mul(vNormal, (float3x3)world);

    VsOut o;
    o.svPos = mul(worldPos, args.viewProj);
    o.normal = normalize(worldN);
    o.uv = vUv;
    o.matIdx = matIdx;
    return o;
}

[shader("pixel")]
float4 psMain(VsOut i) : SV_Target
{
    // Material 32B lookup
    uint mAddr = i.matIdx * 32;
    float4 baseColor = asfloat(g_buffers[args.materialBufIndex].Load4(mAddr + 0));
    uint texIdx  = g_buffers[args.materialBufIndex].Load<uint>(mAddr + 16);
    uint sampIdx = g_buffers[args.materialBufIndex].Load<uint>(mAddr + 20);
    uint flags   = g_buffers[args.materialBufIndex].Load<uint>(mAddr + 24);

    float3 albedo = baseColor.rgb;
    if ((flags & 1u) != 0u)
    {
        float4 sampled = g_textures[texIdx].Sample(g_samplers[sampIdx], i.uv);
        albedo *= sampled.rgb;
    }
    float3 L = normalize(float3(0.6, 0.85, 0.4));
    float ndotl = saturate(dot(normalize(i.normal), L));
    float3 lit = albedo * (0.30 + 0.75 * ndotl);
    return float4(lit, 1.0);
}
