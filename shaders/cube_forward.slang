// 3D forward シェーダ (RG-M4 サンプル 25 用)。
// 頂点バッファ (Vertex: pos+normal, 24 バイト/頂点) と instance バッファ (mat4 world + vec4 color, 80 バイト/インスタンス)
// を bindless で参照し、SV_VertexID / SV_InstanceID で per-vertex / per-instance データをプル。
// 簡易拡散ライティング (固定方向光 + 環境光)。

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
};

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    // Vertex (24B): pos(12B) + normal(12B)
    uint vAddr = vid * 24;
    float3 vPos    = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 0));
    float3 vNormal = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 12));

    // Instance (80B): world(64B) + color(16B)
    uint iAddr = iid * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    // System.Numerics.Matrix4x4 は row-major (M11..M14 が 1 行目)。GPU 側でも row-major で読み取り、
    // mul(matrix, vector) は row-major × column-vector で扱う (HLSL 既定)。
    float4x4 world = float4x4(r0, r1, r2, r3);
    float4 color   = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 64));

    float4 worldPos = mul(float4(vPos, 1.0), world);     // row-major: 行ベクトル × 行列
    float3 worldN   = mul(vNormal, (float3x3)world);

    VsOut o;
    o.svPos = mul(worldPos, args.viewProj);
    o.normal = normalize(worldN);
    o.color = color.rgb;
    return o;
}

[shader("pixel")]
float4 psMain(VsOut i) : SV_Target
{
    float3 L = normalize(float3(0.6, 0.85, 0.4));
    float ndotl = saturate(dot(normalize(i.normal), L));
    float3 lit = i.color * (0.22 + 0.78 * ndotl);
    return float4(lit, 1.0);
}
