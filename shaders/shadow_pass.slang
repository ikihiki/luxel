// Shadow map 生成パス (RG-M5c)。ライト視点で 3D シーンを描画し、
// R32Float RT に NDC z (∈ [0,1]) を書き出す。pixel shader は depth を単 float として返す。
// Vertex は cube_forward と同じく bindless 頂点 + instance バッファから pull。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct ShadowArgs
{
    float4x4 lightVP;       // CPU 側で転置済 (light view * light proj)
    uint vertexBufIndex;
    uint instanceBufIndex;
    uint _pad0;
    uint _pad1;
};
[[vk::push_constant]] ShadowArgs args;

struct VsOut
{
    float4 svPos : SV_Position;
};

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    uint vAddr = vid * 24;
    float3 vPos = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 0));
    uint iAddr = iid * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    float4x4 world = float4x4(r0, r1, r2, r3);
    float4 worldPos = mul(float4(vPos, 1.0), world);
    VsOut o;
    o.svPos = mul(worldPos, args.lightVP);
    return o;
}

[shader("pixel")]
float psMain(VsOut i) : SV_Target
{
    // NDC z = svPos.z / svPos.w で深度値 ∈ [0,1] (D3D 慣習、Vulkan 含む)。
    // SV_Position は perspective divide 済で .z = NDC z, .w = 1/w_clip (HLSL spec) なので
    // SV_Position.z をそのまま返せば NDC z になる。
    return i.svPos.z;
}
