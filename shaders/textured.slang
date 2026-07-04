// サンプル03: bindless テクスチャ + サンプラ。
// フルスクリーン三角形を描き、bindless テクスチャヒープからサンプリングする。
//   g_textures[i] : sampled image 配列 (set0/binding1)
//   g_samplers[i] : sampler 配列     (set0/binding2)
// ブログの「textureHeap[i] = view」+ インデックスでの参照に相当。

[[vk::binding(1, 0)]] Texture2D g_textures[];
[[vk::binding(2, 0)]] SamplerState g_samplers[];

struct DrawArgs { uint textureIndex; uint samplerIndex; };
[[vk::push_constant]] DrawArgs g_args;

struct VSOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

[shader("vertex")]
VSOut vsMain(uint vid : SV_VertexID)
{
    // 1 枚の三角形で画面全体を覆い、uv 0..1 を張る。
    float2 uv = float2((vid << 1) & 2, vid & 2);
    VSOut o;
    o.position = float4(uv * 2.0 - 1.0, 0, 1);
    o.uv = uv;
    return o;
}

[shader("pixel")]
float4 psMain(VSOut i) : SV_Target
{
    return g_textures[g_args.textureIndex].Sample(g_samplers[g_args.samplerIndex], i.uv);
}
