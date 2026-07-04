// 左半分=UI, 右半分=ブラー結果 の合成 (compute)。
// RenderGraph (RG-M1) のサンプル22 のフレーム最終出力を作る。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct CompositeArgs
{
    uint uiIndex;
    uint blurIndex;
    uint dstIndex;
    uint width;
    uint height;
    uint splitX;     // 左右分割の x 座標 (このピクセルから右は blur)
    uint _pad0;
    uint _pad1;
};
[[vk::push_constant]] CompositeArgs args;

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

    float4 src = (x < (int)args.splitX)
        ? LoadPixel(args.uiIndex,   x, y, w)
        : LoadPixel(args.blurIndex, x, y, w);

    // 分割線を可視化 (2 ピクセル)
    int dx = x - (int)args.splitX;
    if (dx == 0 || dx == 1) src = float4(0.85, 0.85, 0.85, 1.0);

    StorePixel(args.dstIndex, x, y, w, src);
}
