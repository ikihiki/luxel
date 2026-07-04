// 3D 内に貼る UI quad (world-space UI)。
// 頂点プル (pos+uv from bindless vertex buf) + ピクセル側で UI bindless buffer をサンプリング。
// RG-M5b サンプル27 で使用。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct UiQuadArgs
{
    float4x4 mvp;            // CPU 側で viewProj * world を計算済み (転置済)
    uint vertexBufIndex;
    uint uiBufferIndex;
    uint uiWidth;
    uint uiHeight;
};
[[vk::push_constant]] UiQuadArgs args;

struct VsOut
{
    float4 svPos : SV_Position;
    float2 uv : TEXCOORD0;
};

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID)
{
    // Vertex: pos(12B) + uv(8B) + pad(4B) = 24B
    uint addr = vid * 24;
    float3 pos = asfloat(g_buffers[args.vertexBufIndex].Load3(addr + 0));
    float2 uv  = asfloat(g_buffers[args.vertexBufIndex].Load2(addr + 12));
    VsOut o;
    o.svPos = mul(float4(pos, 1.0), args.mvp);
    o.uv = uv;
    return o;
}

[shader("pixel")]
float4 psMain(VsOut i) : SV_Target
{
    // UV を UI ピクセル空間へ。bounds は clamp。
    int w = (int)args.uiWidth;
    int h = (int)args.uiHeight;
    int x = clamp((int)(i.uv.x * w), 0, w - 1);
    int y = clamp((int)(i.uv.y * h), 0, h - 1);
    uint addr = (y * w + x) * 4;
    uint packed = g_buffers[args.uiBufferIndex].Load(addr);
    float r = ((packed >>  0) & 0xffu) / 255.0;
    float g = ((packed >>  8) & 0xffu) / 255.0;
    float b = ((packed >> 16) & 0xffu) / 255.0;
    float a = ((packed >> 24) & 0xffu) / 255.0;
    return float4(r, g, b, a);
}
