// サンプル01: compute hello-world (Vulkan/D3D12 統一シェーダ)。
//
// バッファは明示的な bindless 配列 g_buffers[] で参照する。これは
//   Vulkan(SPIR-V): set 0 / binding 0 の storage buffer 配列 (descriptor-indexing)
//   D3D12(DXIL)   : u0, space1 の unbounded UAV テーブル
// に lower され、両バックエンドで同一の「インデックスでバッファを引く」モデルになる。
// ルート引数は push 定数 (Vulkan) / root 32bit 定数 (D3D12) で inline に渡す。

[[vk::binding(0, 0)]]
RWByteAddressBuffer g_buffers[];

struct RootArgs
{
    uint inputIndex;   // 入力バッファの bindless index
    uint outputIndex;  // 出力バッファの bindless index
    uint count;        // 要素数
};
[[vk::push_constant]] RootArgs g_args;

[shader("compute")]
[numthreads(64, 1, 1)]
void main(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= g_args.count) return;
    float v = asfloat(g_buffers[g_args.inputIndex].Load(tid.x * 4));
    g_buffers[g_args.outputIndex].Store(tid.x * 4, asuint(v * 2.0f));
}
