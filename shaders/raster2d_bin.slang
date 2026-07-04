// 2D ラスタライザ (bin): 1 スレッド = 1 タイル (16px)。order を先頭から順に走査し、
// 前計算 AABB (bounds) がタイルに重なる order index を自タイルのリストへ詰める。
// **スレッドがタイルを専有するので atomic 不要で、リストは自然に描画順を保つ** (painter's algorithm)。
// 容量 (tileCap) を超えたタイルは件数だけ数え続け、fine が全走査へフォールバックする (正しさ優先)。

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

[shader("compute")]
[numthreads(64, 1, 1)]
void main(uint3 tid : SV_DispatchThreadID)
{
    uint tilesY = (args.height + 15) / 16;
    uint tileCount = args.tilesX * tilesY;
    uint tile = tid.x;
    if (tile >= tileCount) return;

    uint tx = tile % args.tilesX;
    uint ty = tile / args.tilesX;
    float2 tlo = float2(tx * 16, ty * 16);
    float2 thi = tlo + float2(16, 16);

    uint count = 0;
    uint listBase = tile * args.tileCap;
    for (uint oi = 0; oi < args.orderCount; oi++)
    {
        float4 b = g_buffers[args.boundsIndex].Load<float4>(oi * 16);   // lo.xy, hi.xy
        if (thi.x < b.x || tlo.x > b.z || thi.y < b.y || tlo.y > b.w) continue;
        if (count < args.tileCap)
            g_buffers[args.tileListIndex].Store((listBase + count) * 4, oi);
        count++;   // 容量超過後も数え続ける — fine が count > tileCap で全走査へ切り替える
    }
    g_buffers[args.tileCountIndex].Store(tile * 4, count);
}
