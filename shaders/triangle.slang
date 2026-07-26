// サンプル02: 三角形 (vertex pulling)。
// 頂点バッファは bindless 配列 g_buffers[] から SV_VertexID で raw load する
// (頂点入力ステート無し = ブログの vertex pulling)。Vulkan (SPIR-V) / D3D12 (DXIL) 共通。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct DrawArgs { uint vertexBufferIndex; };
[[vk::push_constant]] DrawArgs g_args;

struct Vertex
{
    float4 position;  // クリップ空間 (offset 0)
    float4 color;     // (offset 16)  ※合計 32 バイト
};

struct VSOut
{
    float4 position : SV_Position;
    float4 color : COLOR0;
};

[shader("vertex")]
VSOut vsMain(uint vid : SV_VertexID)
{
    Vertex v = g_buffers[g_args.vertexBufferIndex].Load<Vertex>(vid * 32);
    VSOut o;
    o.position = v.position;
    o.color = v.color;
    return o;
}

[shader("pixel")]
float4 psMain(VSOut i) : SV_Target
{
    return i.color;
}
