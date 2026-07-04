// Scene-generic PBR-lite + skinning (Sample 85 用)。
// SkinnedVertex 56B (pos 12 + normal 12 + uv 8 + joints0 4 + joints1 4 + weights0 16)。
// Instance 80B (world 64 + matIdx 4 + pad 12)。
// Joint buffer: Matrix4x4[] (per-bone、SkinningSystem 出力 = InverseBindMatrix * jointWorld)。
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
    uint jointBufIndex;        // Matrix4x4[] を持つバッファの bindless index
    uint _pad0, _pad1, _pad2;
};
[[vk::push_constant]] RootArgs args;

struct VsOut
{
    float4 svPos : SV_Position;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    nointerpolation uint matIdx : TEXCOORD1;
};

float4x4 LoadJoint(uint idx)
{
    uint addr = idx * 64;
    float4 r0 = asfloat(g_buffers[args.jointBufIndex].Load4(addr +  0));
    float4 r1 = asfloat(g_buffers[args.jointBufIndex].Load4(addr + 16));
    float4 r2 = asfloat(g_buffers[args.jointBufIndex].Load4(addr + 32));
    float4 r3 = asfloat(g_buffers[args.jointBufIndex].Load4(addr + 48));
    return float4x4(r0, r1, r2, r3);
}

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    uint actualVid = (args.indexBufIndex == 0xFFFFFFFFu)
        ? vid
        : g_buffers[args.indexBufIndex].Load<uint>(vid * 4);
    uint vAddr = actualVid * 56;
    float3 vPos = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 0));
    float3 vNrm = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 12));
    float2 vUv  = asfloat(g_buffers[args.vertexBufIndex].Load2(vAddr + 24));
    uint j01 = g_buffers[args.vertexBufIndex].Load<uint>(vAddr + 32);
    uint j23 = g_buffers[args.vertexBufIndex].Load<uint>(vAddr + 36);
    float4 weights = asfloat(g_buffers[args.vertexBufIndex].Load4(vAddr + 40));

    uint j0 = j01 & 0xffffu;
    uint j1 = (j01 >> 16) & 0xffffu;
    uint j2 = j23 & 0xffffu;
    uint j3 = (j23 >> 16) & 0xffffu;

    // Skinning matrix: Σ weights[k] * jointMats[joints[k]]
    // 各 joint 行列は既に InverseBindMatrix * jointWorld (SkinningSystem が計算済み)
    float4x4 m0 = LoadJoint(j0);
    float4x4 m1 = LoadJoint(j1);
    float4x4 m2 = LoadJoint(j2);
    float4x4 m3 = LoadJoint(j3);
    float4x4 skinMat = weights.x * m0 + weights.y * m1 + weights.z * m2 + weights.w * m3;

    float4 skinnedPos = mul(float4(vPos, 1.0), skinMat);
    float3 skinnedNrm = mul(vNrm, (float3x3)skinMat);

    // Instance 80B: world(64) + matIdx(4) + pad(12)
    uint iAddr = iid * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    float4x4 world = float4x4(r0, r1, r2, r3);
    uint matIdx = g_buffers[args.instanceBufIndex].Load<uint>(iAddr + 64);

    float4 worldPos = mul(skinnedPos, world);
    float3 worldN   = mul(skinnedNrm, (float3x3)world);

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
