// 2D ベクターラスタライザ (fine)。SoA + per-path 間接化。
// Order[] の順に Path を処理し、Path ごとの Transform(world)・Style(色)・Clip(角丸矩形の連鎖) を適用。
// world 変換 → camera 変換で線分を画面へ写し、巻き数(塗り)/距離(ストローク)で被覆を計算、
// クリップを4x4サンプルで抜き、奥→手前 alpha 合成して framebuffer(RGBA8) へ書く。
// ジオメトリ(Segment)は不変のまま、Transform/Style/Order の小さな書換だけで移動/色変更/並べ替え可能。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct RasterArgs
{
    float a, b, c, d, e, f;   // カメラ (ワールド→画面 affine 2x3)
    uint width, height;
    uint segIndex, pathIndex, transformIndex, styleIndex, clipIndex, orderIndex;
    uint orderCount, fbIndex;
    uint bgMode;              // 0 = 白背景, 1 = 透過 (premultiplied RGBA を保持)
    uint tilesX, tileCap;     // タイルビニング (bounds → bin が構築、fine は自タイルのリストのみ走査)
    uint boundsIndex, tileListIndex, tileCountIndex;
};
[[vk::push_constant]] RasterArgs args;

struct Path { uint segStart, segCount, transformSlot, styleSlot;
              uint fillRule, kind, clipSlot, srcIndex;           // kind: 0=fill 1=stroke 2=image 3=R8 mask
              float bminx, bminy, bmaxx, bmaxy;                  // image/mask は bbox が UV 矩形を兼ねる
              float strokeHalfWidth; uint srcStride, srcW, srcH, srcX, srcY; };   // 72B (mask の srcStride は byte)
struct Tf { float a, b, c, d, e, f; uint p0, p1; };             // 32B
struct St { uint colorRgba; float opacity; uint p0, p1; };      // 16B
struct Cl { float minx, miny, maxx, maxy;
            float radiusX, radiusY; uint corners, parentSlot; }; // 32B
struct Seg { float x0, y0, x1, y1; uint pathId, p0, p1, p2; };  // 32B

float2 affine(float a, float b, float c, float d, float e, float f, float2 p)
{
    return float2(a * p.x + c * p.y + e, b * p.x + d * p.y + f);
}

// world(tf) → camera(args) で画面座標へ。
float2 toScreen(float2 w, Tf tf)
{
    float2 wp = affine(tf.a, tf.b, tf.c, tf.d, tf.e, tf.f, w);
    return affine(args.a, args.b, args.c, args.d, args.e, args.f, wp);
}

float distSeg(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float t = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
    return length(pa - ba * t);
}

float4 unpack(uint c)
{
    return float4((c & 0xffu) / 255.0, ((c >> 8) & 0xffu) / 255.0,
                  ((c >> 16) & 0xffu) / 255.0, ((c >> 24) & 0xffu) / 255.0);
}
uint pack(float3 rgb)
{
    uint r = (uint)(saturate(rgb.r) * 255.0 + 0.5);
    uint g = (uint)(saturate(rgb.g) * 255.0 + 0.5);
    uint b = (uint)(saturate(rgb.b) * 255.0 + 0.5);
    return r | (g << 8) | (b << 16) | (0xffu << 24);
}
uint packRgba(float4 c)
{
    uint r = (uint)(saturate(c.r) * 255.0 + 0.5);
    uint g = (uint)(saturate(c.g) * 255.0 + 0.5);
    uint b = (uint)(saturate(c.b) * 255.0 + 0.5);
    uint a = (uint)(saturate(c.a) * 255.0 + 0.5);
    return r | (g << 8) | (b << 16) | (a << 24);
}

// R8 は 4 texel/word で ByteAddressBuffer に格納する。各行を 4 byte 境界へ揃えることで、
// Vulkan/DX12/WebGPU の全 backend で aligned uint Load だけを使用できる。
float loadMask(Path path, int2 texel)
{
    uint byteOffset = (uint(texel.y) + path.srcY) * path.srcStride + uint(texel.x) + path.srcX;
    uint word = g_buffers[path.srcIndex].Load<uint>(byteOffset & ~3u);
    uint shift = (byteOffset & 3u) * 8u;
    return ((word >> shift) & 0xffu) / 255.0;
}

bool insideRoundedClip(float2 p, Cl cl)
{
    float2 k0 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.minx, cl.miny));
    float2 k1 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.maxx, cl.miny));
    float2 k2 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.minx, cl.maxy));
    float2 k3 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.maxx, cl.maxy));
    float2 lo = min(min(k0, k1), min(k2, k3));
    float2 hi = max(max(k0, k1), max(k2, k3));
    if (p.x < lo.x || p.x > hi.x || p.y < lo.y || p.y > hi.y) return false;

    float2 radius = min(float2(cl.radiusX * length(float2(args.a, args.b)),
                               cl.radiusY * length(float2(args.c, args.d))),
                        (hi - lo) * 0.5);
    if (cl.corners == 0 || radius.x <= 0.0 || radius.y <= 0.0) return true;

    float2 q;
    if ((cl.corners & 1u) != 0 && p.x < lo.x + radius.x && p.y < lo.y + radius.y)
        q = (p - (lo + radius)) / radius;
    else if ((cl.corners & 2u) != 0 && p.x > hi.x - radius.x && p.y < lo.y + radius.y)
        q = (p - float2(hi.x - radius.x, lo.y + radius.y)) / radius;
    else if ((cl.corners & 4u) != 0 && p.x > hi.x - radius.x && p.y > hi.y - radius.y)
        q = (p - (hi - radius)) / radius;
    else if ((cl.corners & 8u) != 0 && p.x < lo.x + radius.x && p.y > hi.y - radius.y)
        q = (p - float2(lo.x + radius.x, hi.y - radius.y)) / radius;
    else return true;
    return dot(q, q) <= 1.0;
}

bool insideAllClips(float2 p, uint clipSlot)
{
    for (uint depth = 0; clipSlot != 0xFFFFFFFFu && depth < 64; depth++)
    {
        Cl cl = g_buffers[args.clipIndex].Load<Cl>(clipSlot * 32);
        if (!insideRoundedClip(p, cl)) return false;
        clipSlot = cl.parentSlot;
    }
    return true;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= args.width || tid.y >= args.height) return;
    float2 pix = float2(tid.x + 0.5, tid.y + 0.5);
    float4 acc = float4(0, 0, 0, 0);   // premultiplied

    // 自タイルのパスリスト (bin が描画順で構築)。容量超過タイルは全 order 走査へフォールバック。
    uint tile = (tid.x / 16) + (tid.y / 16) * args.tilesX;
    uint tcount = g_buffers[args.tileCountIndex].Load<uint>(tile * 4);
    bool binned = tcount <= args.tileCap;
    uint n = binned ? tcount : args.orderCount;

    for (uint i = 0; i < n; i++)
    {
        uint oi = binned ? g_buffers[args.tileListIndex].Load<uint>((tile * args.tileCap + i) * 4) : i;
        uint pidx = g_buffers[args.orderIndex].Load<uint>(oi * 4);
        Path path = g_buffers[args.pathIndex].Load<Path>(pidx * 72);
        Tf tf = g_buffers[args.transformIndex].Load<Tf>(path.transformSlot * 32);

        // 祖先を含む角丸クリップをサンプルマスク化。塗りではパスのサンプルと同じ位置を使う。
        float2 clo = float2(-1e30, -1e30), chi = float2(1e30, 1e30);
        uint clipMask = 0xFFFFu;
        if (path.clipSlot != 0xFFFFFFFFu)
        {
            bool hasRounded = false;
            uint clipSlot = path.clipSlot;
            for (uint depth = 0; clipSlot != 0xFFFFFFFFu && depth < 64; depth++)
            {
                Cl cl = g_buffers[args.clipIndex].Load<Cl>(clipSlot * 32);
                float2 k0 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.minx, cl.miny));
                float2 k1 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.maxx, cl.miny));
                float2 k2 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.minx, cl.maxy));
                float2 k3 = affine(args.a, args.b, args.c, args.d, args.e, args.f, float2(cl.maxx, cl.maxy));
                clo = max(clo, min(min(k0, k1), min(k2, k3)));
                chi = min(chi, max(max(k0, k1), max(k2, k3)));
                hasRounded = hasRounded || (cl.corners != 0 && cl.radiusX > 0.0 && cl.radiusY > 0.0);
                clipSlot = cl.parentSlot;
            }

            float2 pixelLo = float2(tid.xy), pixelHi = pixelLo + 1.0;
            if (pixelHi.x <= clo.x || pixelLo.x >= chi.x || pixelHi.y <= clo.y || pixelLo.y >= chi.y) continue;
            // 通常の矩形クリップ内にピクセル全体が収まる大半のケースは16サンプルを省く。
            if (hasRounded || pixelLo.x < clo.x || pixelHi.x > chi.x || pixelLo.y < clo.y || pixelHi.y > chi.y)
            {
                clipMask = 0u;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        uint bit = uint(sy * 4 + sx);
                        float2 sp = float2(tid.x + (sx + 0.5) * 0.25, tid.y + (sy + 0.5) * 0.25);
                        if (insideAllClips(sp, path.clipSlot)) clipMask |= 1u << bit;
                    }
                if (clipMask == 0u) continue;
            }
        }

        // パス bbox を画面空間へ (4 隅変換) し、早期スキップ。
        float2 q0 = toScreen(float2(path.bminx, path.bminy), tf);
        float2 q1 = toScreen(float2(path.bmaxx, path.bminy), tf);
        float2 q2 = toScreen(float2(path.bminx, path.bmaxy), tf);
        float2 q3 = toScreen(float2(path.bmaxx, path.bmaxy), tf);
        float2 lo = max(min(min(q0, q1), min(q2, q3)) - (path.strokeHalfWidth + 1.5), clo);
        float2 hi = min(max(max(q0, q1), max(q2, q3)) + (path.strokeHalfWidth + 1.5), chi);
        if (pix.x < lo.x || pix.x > hi.x || pix.y < lo.y || pix.y > hi.y) continue;

        float cov;
        if (path.kind == 1)
        {
            float md = 1e9;
            for (uint s = 0; s < path.segCount; s++)
            {
                Seg seg = g_buffers[args.segIndex].Load<Seg>((path.segStart + s) * 32);
                md = min(md, distSeg(pix, toScreen(float2(seg.x0, seg.y0), tf),
                                          toScreen(float2(seg.x1, seg.y1), tf)));
            }
            cov = 1.0 - smoothstep(path.strokeHalfWidth - 0.75, path.strokeHalfWidth + 0.75, md);
            int clipSamples = 0;
            for (uint bit = 0; bit < 16; bit++) if ((clipMask & (1u << bit)) != 0) clipSamples++;
            cov *= clipSamples / 16.0;
        }
        else
        {
            int inside = 0;
            for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    uint bit = uint(sy * 4 + sx);
                    if ((clipMask & (1u << bit)) == 0) continue;
                    float2 sp = float2(tid.x + (sx + 0.5) * 0.25, tid.y + (sy + 0.5) * 0.25);
                    int wind = 0;
                    for (uint s = 0; s < path.segCount; s++)
                    {
                        Seg seg = g_buffers[args.segIndex].Load<Seg>((path.segStart + s) * 32);
                        float2 A = toScreen(float2(seg.x0, seg.y0), tf);
                        float2 B = toScreen(float2(seg.x1, seg.y1), tf);
                        if ((A.y <= sp.y) != (B.y <= sp.y))
                        {
                            float t = (sp.y - A.y) / (B.y - A.y);
                            if (A.x + t * (B.x - A.x) > sp.x) wind += (B.y > A.y) ? 1 : -1;
                        }
                    }
                    if ((path.fillRule == 0) ? (wind != 0) : ((wind & 1) != 0)) inside++;
                }
            cov = inside / 16.0;
        }

        if (cov > 0.0)
        {
            St st = g_buffers[args.styleIndex].Load<St>(path.styleSlot * 16);
            float4 src;
            if (path.kind == 2)
            {
                // 画像: 画面座標 → ローカル (world→camera 合成 affine の逆) → bbox 基準の UV → nearest 取得。
                // ソースは premultiplied RGBA (透過キャンバス出力) 前提で、opacity と被覆をそのまま乗算。
                float ca = args.a * tf.a + args.c * tf.b;
                float cb = args.b * tf.a + args.d * tf.b;
                float cc = args.a * tf.c + args.c * tf.d;
                float cd = args.b * tf.c + args.d * tf.d;
                float ce = args.a * tf.e + args.c * tf.f + args.e;
                float cf = args.b * tf.e + args.d * tf.f + args.f;
                float det = ca * cd - cc * cb;
                float id = abs(det) < 1e-12 ? 0.0 : 1.0 / det;
                float2 rel = pix - float2(ce, cf);
                float2 local = float2((cd * rel.x - cc * rel.y) * id, (-cb * rel.x + ca * rel.y) * id);
                float2 ext = max(float2(path.bmaxx - path.bminx, path.bmaxy - path.bminy), 1e-6);
                float2 uv = (local - float2(path.bminx, path.bminy)) / ext;
                // bilinear (premultiplied なので線形補間で正しい)。1:1 表示では frac=0 になり nearest と同一
                float2 tex = uv * float2(path.srcW, path.srcH) - 0.5;
                float2 tf = floor(tex);
                float2 fr = tex - tf;
                int2 tmax = int2(int(path.srcW) - 1, int(path.srcH) - 1);
                int2 t0 = clamp(int2(tf), int2(0, 0), tmax);
                int2 t1 = min(t0 + int2(1, 1), tmax);
                // アトラス原点 (srcX,srcY) を texel 座標へ加算 (clamp 後に加算 → sub-rect 内に閉じ隣接スプライトへ漏れない)
                uint ox = path.srcX, oy = path.srcY;
                float4 c00 = unpack(g_buffers[path.srcIndex].Load<uint>(((uint(t0.y) + oy) * path.srcStride + uint(t0.x) + ox) * 4));
                float4 c10 = unpack(g_buffers[path.srcIndex].Load<uint>(((uint(t0.y) + oy) * path.srcStride + uint(t1.x) + ox) * 4));
                float4 c01 = unpack(g_buffers[path.srcIndex].Load<uint>(((uint(t1.y) + oy) * path.srcStride + uint(t0.x) + ox) * 4));
                float4 c11 = unpack(g_buffers[path.srcIndex].Load<uint>(((uint(t1.y) + oy) * path.srcStride + uint(t1.x) + ox) * 4));
                float4 ic = lerp(lerp(c00, c10, fr.x), lerp(c01, c11, fr.x), fr.y);
                float f = st.opacity * cov;
                src = float4(ic.rgb * f, ic.a * f);
            }
            else if (path.kind == 3)
            {
                // R8 マスク: 画面座標をローカルへ戻して atlas sub-rect を読む。
                // fillRule は mask だけ sampling (0=nearest, 1=linear) として使用する。
                float ca = args.a * tf.a + args.c * tf.b;
                float cb = args.b * tf.a + args.d * tf.b;
                float cc = args.a * tf.c + args.c * tf.d;
                float cd = args.b * tf.c + args.d * tf.d;
                float ce = args.a * tf.e + args.c * tf.f + args.e;
                float cf = args.b * tf.e + args.d * tf.f + args.f;
                float det = ca * cd - cc * cb;
                float id = abs(det) < 1e-12 ? 0.0 : 1.0 / det;
                float2 rel = pix - float2(ce, cf);
                float2 local = float2((cd * rel.x - cc * rel.y) * id, (-cb * rel.x + ca * rel.y) * id);
                float2 ext = max(float2(path.bmaxx - path.bminx, path.bmaxy - path.bminy), 1e-6);
                float2 uv = (local - float2(path.bminx, path.bminy)) / ext;
                float2 tex = uv * float2(path.srcW, path.srcH) - 0.5;
                int2 tmax = int2(int(path.srcW) - 1, int(path.srcH) - 1);
                float mask;
                if (path.fillRule == 0u)
                {
                    int2 tn = clamp(int2(floor(tex + 0.5)), int2(0, 0), tmax);
                    mask = loadMask(path, tn);
                }
                else
                {
                    float2 tb = floor(tex);
                    float2 fr = tex - tb;
                    int2 t0 = clamp(int2(tb), int2(0, 0), tmax);
                    int2 t1 = min(t0 + int2(1, 1), tmax);
                    float m00 = loadMask(path, t0);
                    float m10 = loadMask(path, int2(t1.x, t0.y));
                    float m01 = loadMask(path, int2(t0.x, t1.y));
                    float m11 = loadMask(path, t1);
                    mask = lerp(lerp(m00, m10, fr.x), lerp(m01, m11, fr.x), fr.y);
                }
                float4 col = unpack(st.colorRgba);
                float a = col.a * st.opacity * cov * mask;
                src = float4(col.rgb * a, a);
            }
            else
            {
                float4 col = unpack(st.colorRgba);
                float a = col.a * st.opacity * cov;
                src = float4(col.rgb * a, a);
            }
            acc = src + acc * (1.0 - src.a);
        }
    }

    uint outPacked;
    if (args.bgMode == 0)
    {
        float3 outc = acc.rgb + float3(1, 1, 1) * (1.0 - acc.a);   // 白背景合成
        outPacked = pack(outc);
    }
    else
    {
        outPacked = packRgba(acc);                                  // premultiplied RGBA を保持
    }
    g_buffers[args.fbIndex].Store((tid.y * args.width + tid.x) * 4, outPacked);
}
