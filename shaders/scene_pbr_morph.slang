// Scene-generic PBR-lite + morph target (ブレンドシェイプ) (3D/GltfMorph 用)。
// scene_pbr_lite と同じ instance/ライティング規約に、頂点で morph デルタを重み付き加算したもの。
// Vertex 32B (pos 12 + normal 12 + uv 8、SceneBuilder.Vertex)。
// Instance 80B (world 64 + baseColor 16、SceneInstanceData)。
// Morph buffer: MorphDelta 24B (dPos 12 + dNrm 12) の [target][vertex] = t*vertexCount+v 配列。
// Weight buffer: float[targetCount] (per-frame、MorphWeights component 由来)。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct RootArgs
{
    float4x4 viewProj;         // 64B
    uint vertexBufIndex;
    uint indexBufIndex;        // 0xFFFFFFFF なら non-indexed
    uint instanceBufIndex;
    uint instanceStart;
    uint morphBufIndex;
    uint weightBufIndex;
    uint targetCount;
    uint vertexCount;
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
    uint actualVid = (args.indexBufIndex == 0xFFFFFFFFu)
        ? vid
        : g_buffers[args.indexBufIndex].Load<uint>(vid * 4);

    // Vertex 32B: pos(12) + normal(12) + uv(8)
    uint vAddr = actualVid * 32;
    float3 vPos = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 0));
    float3 vNrm = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 12));
    float2 vUv  = asfloat(g_buffers[args.vertexBufIndex].Load2(vAddr + 24));

    // morph: pos += Σ w_t * dPos_t、normal += Σ w_t * dNrm_t
    for (uint t = 0; t < args.targetCount; t++)
    {
        float w = asfloat(g_buffers[args.weightBufIndex].Load(t * 4));
        uint mAddr = (t * args.vertexCount + actualVid) * 24;
        float3 dPos = asfloat(g_buffers[args.morphBufIndex].Load3(mAddr + 0));
        float3 dNrm = asfloat(g_buffers[args.morphBufIndex].Load3(mAddr + 12));
        vPos += w * dPos;
        vNrm += w * dNrm;
    }

    // Instance 80B: world(64) + baseColor(16)
    uint iAddr = (args.instanceStart + iid) * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    float4x4 world = float4x4(r0, r1, r2, r3);
    float4 baseColor = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 64));

    float4 worldPos = mul(float4(vPos, 1.0), world);
    float3 worldN   = mul(vNrm, (float3x3)world);

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
