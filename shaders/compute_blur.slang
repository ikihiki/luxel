// 分離可能 9-tap ガウシアンブラー (compute)。
//
// RenderGraph (RG-M1) のサンプル22 で使う。bindless バッファ間で読み書きし、
// dirX/dirY を切り替えて 1 shader 2 dispatch で水平→垂直の 2 段ブラーを行う。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct BlurArgs
{
    uint srcIndex;
    uint dstIndex;
    uint width;
    uint height;
    int  dirX;     // 1=水平, 0=垂直
    int  dirY;     // 0=水平, 1=垂直
    uint _pad0;
    uint _pad1;
};
[[vk::push_constant]] BlurArgs args;

float4 LoadPixel(uint bufIdx, int x, int y, int w, int h)
{
    x = clamp(x, 0, w - 1);
    y = clamp(y, 0, h - 1);
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

    // σ≈2.5 程度の 9 タップガウシアン (正規化済み)
    const float k0 = 0.020;
    const float k1 = 0.060;
    const float k2 = 0.120;
    const float k3 = 0.180;
    const float k4 = 0.240;

    float4 sum = float4(0, 0, 0, 0);
    sum += LoadPixel(args.srcIndex, x + args.dirX * -4, y + args.dirY * -4, w, h) * k0;
    sum += LoadPixel(args.srcIndex, x + args.dirX * -3, y + args.dirY * -3, w, h) * k1;
    sum += LoadPixel(args.srcIndex, x + args.dirX * -2, y + args.dirY * -2, w, h) * k2;
    sum += LoadPixel(args.srcIndex, x + args.dirX * -1, y + args.dirY * -1, w, h) * k3;
    sum += LoadPixel(args.srcIndex, x,                  y,                  w, h) * k4;
    sum += LoadPixel(args.srcIndex, x + args.dirX *  1, y + args.dirY *  1, w, h) * k3;
    sum += LoadPixel(args.srcIndex, x + args.dirX *  2, y + args.dirY *  2, w, h) * k2;
    sum += LoadPixel(args.srcIndex, x + args.dirX *  3, y + args.dirY *  3, w, h) * k1;
    sum += LoadPixel(args.srcIndex, x + args.dirX *  4, y + args.dirY *  4, w, h) * k0;

    StorePixel(args.dstIndex, x, y, w, sum);
}
