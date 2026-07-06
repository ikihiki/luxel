// 2D ラスタライザ (bounds): order エントリ毎に、パス bbox を画面空間 AABB へ前計算して
// bounds バッファ (float4 = lo.xy, hi.xy) に書く。fine の per-pixel 早期スキップと同一の式
// (4 隅変換 + strokeHalfWidth + 1.5 マージン + クリップ AABB での clamp)。
// bin パスがこれをタイル毎に読むことで、Path/Tf/Cl の重い再読込をタイル数分やらずに済む。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct RasterArgs
{
    float a, b, c, d, e, f;
    uint width, height;
    uint segIndex, pathIndex, transformIndex, styleIndex, clipIndex, orderIndex;
    uint orderCount, fbIndex;
    uint bgMode;
    uint tilesX, tileCap;
    uint boundsIndex, tileListIndex, tileCountIndex;
};
[[vk::push_constant]] RasterArgs args;

struct Path { uint segStart, segCount, transformSlot, styleSlot;
              uint fillRule, kind, clipSlot, srcIndex;
              float bminx, bminy, bmaxx, bmaxy;
              float strokeHalfWidth; uint srcStride, srcW, srcH, srcX, srcY; };   // 72B
struct Tf { float a, b, c, d, e, f; uint p0, p1; };
struct Cl { float minx, miny, maxx, maxy; };

float2 affine(float a, float b, float c, float d, float e, float f, float2 p)
{
    return float2(a * p.x + c * p.y + e, b * p.x + d * p.y + f);
}

float2 toScreen(float2 w, Tf tf)
{
    float2 wp = affine(tf.a, tf.b, tf.c, tf.d, tf.e, tf.f, w);
    return affine(args.a, args.b, args.c, args.d, args.e, args.f, wp);
}

[shader("compute")]
[numthreads(64, 1, 1)]
void main(uint3 tid : SV_DispatchThreadID)
{
    uint oi = tid.x;
    if (oi >= args.orderCount) return;

    uint pidx = g_buffers[args.orderIndex].Load<uint>(oi * 4);
    Path path = g_buffers[args.pathIndex].Load<Path>(pidx * 72);
    Tf tf = g_buffers[args.transformIndex].Load<Tf>(path.transformSlot * 32);

    float2 clo = float2(-1e30, -1e30), chi = float2(1e30, 1e30);
    if (path.clipSlot != 0xFFFFFFFFu)
    {
        Cl cl = g_buffers[args.clipIndex].Load<Cl>(path.clipSlot * 16);
        float2 k0 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.minx, cl.miny));
        float2 k1 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.maxx, cl.miny));
        float2 k2 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.minx, cl.maxy));
        float2 k3 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.maxx, cl.maxy));
        clo = min(min(k0, k1), min(k2, k3)); chi = max(max(k0, k1), max(k2, k3));
    }

    float2 q0 = toScreen(float2(path.bminx, path.bminy), tf);
    float2 q1 = toScreen(float2(path.bmaxx, path.bminy), tf);
    float2 q2 = toScreen(float2(path.bminx, path.bmaxy), tf);
    float2 q3 = toScreen(float2(path.bmaxx, path.bmaxy), tf);
    float2 lo = max(min(min(q0, q1), min(q2, q3)) - (path.strokeHalfWidth + 1.5), clo);
    float2 hi = min(max(max(q0, q1), max(q2, q3)) + (path.strokeHalfWidth + 1.5), chi);

    g_buffers[args.boundsIndex].Store<float4>(oi * 16, float4(lo.x, lo.y, hi.x, hi.y));
}
