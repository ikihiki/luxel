// Scene-generic PBR-lite + GPU 頂点スキニング (3D/GltfSkinned 用)。
// scene_pbr_lite と同じ instance/ライティング規約に、頂点スキニングを足したもの。
// SkinnedVertex 56B (pos 12 + normal 12 + uv 8 + joints0 4 + joints1 4 + weights0 16、SceneBuilder.SkinnedVertex)。
// Instance 80B (world 64 + baseColor 16、SceneInstanceData)。skinned mesh の node 変換は glTF 規約で無視
// (= instance world は恒等) — joint 行列が world-bind 空間へ写す。
// Joint buffer: Matrix4x4[] (per-bone、SkinningSystem 出力 = InverseBindMatrix * jointWorld、row-major)。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct RootArgs
{
    float4x4 viewProj;         // 64B
    uint vertexBufIndex;
    uint indexBufIndex;        // 0xFFFFFFFF なら non-indexed
    uint instanceBufIndex;
    uint jointBufIndex;        // Matrix4x4[] を持つバッファの bindless index
    uint instanceStart;
    uint _pad0, _pad1, _pad2;
};
[[vk::push_constant]] RootArgs args;

struct VsOut
{
    float4 svPos : SV_Position;
    float3 normal : NORMAL;
    float3 color : COLOR;
    float2 uv : TEXCOORD0;
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

    // SkinnedVertex 56B: pos(12) + normal(12) + uv(8) + joints0(4) + joints1(4) + weights0(16)
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

    // Skinning matrix: Σ weights[k] * jointMats[joints[k]]。各 joint 行列は既に
    // InverseBindMatrix * jointWorld (row-major、SkinningSystem が計算済み)。
    float4x4 skinMat = weights.x * LoadJoint(j0)
                     + weights.y * LoadJoint(j1)
                     + weights.z * LoadJoint(j2)
                     + weights.w * LoadJoint(j3);

    float4 skinnedPos = mul(float4(vPos, 1.0), skinMat);
    float3 skinnedNrm = mul(vNrm, (float3x3)skinMat);

    // Instance 80B: world(64) + baseColor(16) ← scene_pbr_lite と同じ
    uint iAddr = (args.instanceStart + iid) * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    float4x4 world = float4x4(r0, r1, r2, r3);
    float4 baseColor = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 64));

    float4 worldPos = mul(skinnedPos, world);
    float3 worldN   = mul(skinnedNrm, (float3x3)world);

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
