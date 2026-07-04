// 2D UI (premultiplied alpha) を 3D シーン上に alpha over 合成。
// UI 側は Rasterizer2D.Render(transparent:true) で未描画部分が alpha=0 で埋められている前提。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct OverlayArgs
{
    uint baseIdx;   // 3D scene のピクセルバッファ (RGB, alpha は無視)
    uint uiIdx;     // 2D UI のピクセルバッファ (premultiplied RGBA)
    uint dstIdx;    // 合成先バッファ
    uint width;
    uint height;
};
[[vk::push_constant]] OverlayArgs args;

float4 LoadPixel(uint bufIdx, int x, int y, int w)
{
    uint addr = (y * w + x) * 4;
    uint packed = g_buffers[bufIdx].Load(addr);
    return float4(((packed >>  0) & 0xffu) / 255.0,
                  ((packed >>  8) & 0xffu) / 255.0,
                  ((packed >> 16) & 0xffu) / 255.0,
                  ((packed >> 24) & 0xffu) / 255.0);
}

void StorePixel(uint bufIdx, int x, int y, int w, float4 c)
{
    uint addr = (y * w + x) * 4;
    uint pr = (uint)(saturate(c.r) * 255.0 + 0.5);
    uint pg = (uint)(saturate(c.g) * 255.0 + 0.5);
    uint pb = (uint)(saturate(c.b) * 255.0 + 0.5);
    uint pa = (uint)(saturate(c.a) * 255.0 + 0.5);
    uint packed = pr | (pg << 8) | (pb << 16) | (pa << 24);
    g_buffers[bufIdx].Store(addr, packed);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 tid : SV_DispatchThreadID)
{
    int w = (int)args.width;
    int h = (int)args.height;
    int x = (int)tid.x;
    int y = (int)tid.y;
    if (x >= w || y >= h) return;

    float4 baseCol = LoadPixel(args.baseIdx, x, y, w);
    float4 uiCol   = LoadPixel(args.uiIdx, x, y, w);   // premultiplied

    // alpha over: out.rgb = ui.rgb + base.rgb * (1 - ui.a)
    float3 outRgb = uiCol.rgb + baseCol.rgb * (1.0 - uiCol.a);
    StorePixel(args.dstIdx, x, y, w, float4(outRgb, 1.0));
}
