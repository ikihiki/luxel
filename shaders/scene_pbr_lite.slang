// Scene-generic PBR-lite shader (Sample 76/77 用)。
// Vertex 32B (pos 12 + normal 12 + uv 8、SceneBuilder.Vertex)。
// Instance 80B (world 64 + baseColor 16)。
// 簡易拡散ライティング (固定方向光 + 環境光) + base color。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct RootArgs
{
    float4x4 viewProj;     // 64B
    uint vertexBufIndex;
    uint instanceBufIndex;
    uint _pad0;
    uint _pad1;
};
[[vk::push_constant]] RootArgs args;

struct VsOut
{
    float4 svPos : SV_Position;
    float3 normal : NORMAL;
    float3 color : COLOR;
    float2 uv : TEXCOORD0;
};

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    // SceneBuilder.Vertex (32B): pos(12) + normal(12) + uv(8)
    uint vAddr = vid * 32;
    float3 vPos    = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 0));
    float3 vNormal = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 12));
    float2 vUv     = asfloat(g_buffers[args.vertexBufIndex].Load2(vAddr + 24));

    // Instance (80B): world(64) + color(16) ← cube_forward と同じ
    uint iAddr = iid * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    float4x4 world = float4x4(r0, r1, r2, r3);
    float4 baseColor = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 64));

    float4 worldPos = mul(float4(vPos, 1.0), world);
    float3 worldN   = mul(vNormal, (float3x3)world);

    VsOut o;
    o.svPos = mul(worldPos, args.viewProj);
    o.normal = normalize(worldN);
    o.color = baseColor.rgb;
    o.uv = vUv;
    return o;
}

[shader("pixel")]
float4 psMain(VsOut i) : SV_Target
{
    float3 L = normalize(float3(0.6, 0.85, 0.4));
    float ndotl = saturate(dot(normalize(i.normal), L));
    float3 lit = i.color * (0.25 + 0.75 * ndotl);
    return float4(lit, 1.0);
}
