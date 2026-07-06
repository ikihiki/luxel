// パーティクルのビルボード描画 (タスク 16 .ThreeD)。
// 頂点バッファ無し: 1 インスタンス = 6 頂点 (2 三角形) の quad を SV_VertexID から生成し、
// カメラの right/up 軸 (push constant) でワールド空間に展開 → 常にカメラを向く。
// インスタンスバッファ (BillboardInstance, 32B: pos(12) + size(4) + rgba(16)) を bindless で読む。
// アルファブレンド (straight alpha)、深度テストあり・書き込み無し。vk/dx ピクセル一致。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct Args
{
    float4x4 viewProj;              // 64B (CPU 側で転置して渡す = row-major 行ベクトル規約)
    float3 right; uint instIndex;   // 16B: カメラ right 軸 + インスタンスバッファ index
    float3 up;    uint _pad;        // 16B: カメラ up 軸
};
[[vk::push_constant]] Args args;

struct VsOut
{
    float4 svPos : SV_Position;
    float4 color : COLOR0;
    float2 uv : TEXCOORD0;          // [0,1] quad 座標 (ソフト円のため)
};

static const float2 CORNERS[6] = {
    float2(-0.5, -0.5), float2(0.5, -0.5), float2(0.5, 0.5),
    float2(-0.5, -0.5), float2(0.5, 0.5), float2(-0.5, 0.5),
};

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    uint base = iid * 32;
    float3 center = asfloat(g_buffers[args.instIndex].Load3(base + 0));
    float size = asfloat(g_buffers[args.instIndex].Load(base + 12));
    float4 color = asfloat(g_buffers[args.instIndex].Load4(base + 16));

    float2 c = CORNERS[vid];
    float3 world = center + (c.x * size) * args.right + (c.y * size) * args.up;

    VsOut o;
    o.svPos = mul(float4(world, 1.0), args.viewProj);
    o.color = color;
    o.uv = c + 0.5;
    return o;
}

[shader("pixel")]
float4 psMain(VsOut i) : SV_Target
{
    // ソフト円: 中心からの距離で縁をなめらかにフェード
    float d = length(i.uv - 0.5);
    float a = i.color.a * smoothstep(0.5, 0.42, d);
    return float4(i.color.rgb, a);   // straight alpha (blend = src.a*src + (1-src.a)*dst)
}
