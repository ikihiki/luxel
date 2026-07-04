// 元画像 + intensity × blur を加算合成 (ポストプロセス bloom の最終段)。
// RG-M5 のサンプル 26 で使う。bindless バッファ (RGBA8) 入力 2 本 + 出力 1 本。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct BloomArgs
{
    uint baseIndex;     // 元画像 (3D 出力)
    uint blurIndex;     // ぼかし結果
    uint dstIndex;      // 出力
    uint width;
    uint height;
    float intensity;    // blur の足し込み係数 (0.0..1.5 程度)
    uint _pad0;
    uint _pad1;
};
[[vk::push_constant]] BloomArgs args;

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

    float4 baseColor = LoadPixel(args.baseIndex, x, y, w);
    float4 blurColor = LoadPixel(args.blurIndex, x, y, w);
    // additive bloom: 元 + 強度 × blur (アルファは元の値を維持)
    float3 lit = baseColor.rgb + args.intensity * blurColor.rgb;
    StorePixel(args.dstIndex, x, y, w, float4(lit, baseColor.a));
}
