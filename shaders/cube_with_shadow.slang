// 3D forward + shadow map サンプリング (RG-M5c)。
// シャドウマップ (R32Float, ライト視点 NDC z を bindless buffer に書き戻したもの) を比較し、
// 影に入っているピクセルは拡散項を弱める。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct MainArgs
{
    float4x4 viewProj;     // メインカメラ (転置済)
    float4x4 lightVP;      // ライト視点 (転置済)
    uint vertexBufIndex;
    uint instanceBufIndex;
    uint shadowBufIndex;   // R32F の bindless buffer
    uint shadowW;
    uint shadowH;
    uint _pad0;
    uint _pad1;
    uint _pad2;
};
[[vk::push_constant]] MainArgs args;

struct VsOut
{
    float4 svPos : SV_Position;
    float3 worldPos : POSITION0;
    float3 normal : NORMAL;
    float3 color : COLOR;
};

[shader("vertex")]
VsOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    uint vAddr = vid * 24;
    float3 vPos    = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 0));
    float3 vNormal = asfloat(g_buffers[args.vertexBufIndex].Load3(vAddr + 12));

    uint iAddr = iid * 80;
    float4 r0 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr +  0));
    float4 r1 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 16));
    float4 r2 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 32));
    float4 r3 = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 48));
    float4x4 world = float4x4(r0, r1, r2, r3);
    float4 color   = asfloat(g_buffers[args.instanceBufIndex].Load4(iAddr + 64));

    float4 worldPos = mul(float4(vPos, 1.0), world);
    float3 worldN   = mul(vNormal, (float3x3)world);

    VsOut o;
    o.svPos = mul(worldPos, args.viewProj);
    o.worldPos = worldPos.xyz;
    o.normal = normalize(worldN);
    o.color = color.rgb;
    return o;
}

// ピクセルの world position をライト視点 NDC へ → シャドウマップ参照 → 比較。
float SampleShadow(float3 worldPos)
{
    float4 lp = mul(float4(worldPos, 1.0), args.lightVP);
    if (lp.w <= 0.0) return 1.0;   // ライトの背面
    float2 ndc = lp.xy / lp.w;
    float ndcZ = lp.z / lp.w;
    // ライトビューの外なら影なし扱い
    if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0 || ndcZ < 0.0 || ndcZ > 1.0)
        return 1.0;

    // NDC [-1,1] → [0,1] → ピクセル座標
    // Vulkan/D3D 共通で、Y は描画時のビューポート設定で吸収 (Luxel は vk でネガティブ高さ
    // ビューポートを使うため両方で Y 軸方向が一致)。シャドウバッファは「Y は上から下」で書かれる。
    float u = ndc.x * 0.5 + 0.5;
    float v = ndc.y * 0.5 + 0.5;
    int x = clamp((int)(u * args.shadowW), 0, (int)args.shadowW - 1);
    int y = clamp((int)(v * args.shadowH), 0, (int)args.shadowH - 1);
    uint addr = (y * args.shadowW + x) * 4;
    float occluderZ = asfloat(g_buffers[args.shadowBufIndex].Load(addr));

    float bias = 0.003;
    return (ndcZ - bias > occluderZ) ? 0.35 : 1.0;
}

[shader("pixel")]
float4 psMain(VsOut i) : SV_Target
{
    float3 L = normalize(float3(0.5, 0.95, 0.25));
    float ndotl = saturate(dot(normalize(i.normal), L));
    float shadow = SampleShadow(i.worldPos);
    float3 lit = i.color * (0.20 + 0.80 * ndotl * shadow);
    return float4(lit, 1.0);
}
