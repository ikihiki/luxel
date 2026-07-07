using System.Numerics;

namespace LuxelRange.Core;

/// <summary>
/// アリーナの起伏地形メッシュ (三角形スープ) を生成する。**同じ頂点が物理コライダー (AddStaticMesh) と
/// 描画の両方に使われる** — 絵と当たりのズレが即バレる生きた検証 (タスク 05)。決定的な高さ関数。
/// </summary>
public static class RangeTerrain
{
    /// <summary>アリーナ半径 (±HalfSize m の正方形)。</summary>
    public const float HalfSize = 15f;
    /// <summary>グリッド分割数 (頂点は (N+1)²)。</summary>
    public const int N = 20;

    /// <summary>地形の高さ (決定的なゆるい起伏)。</summary>
    public static float Height(float x, float z)
        => 0.55f * MathF.Sin(x * 0.33f) * MathF.Cos(z * 0.38f)
         + 0.30f * MathF.Sin((x + z) * 0.5f);

    /// <summary>地形メッシュ (位置 + 法線 + 三角形インデックス) を生成する。法線は解析的 (上向き)。</summary>
    public static (Vector3[] Positions, Vector3[] Normals, int[] Indices) Build()
    {
        int side = N + 1;
        var pos = new Vector3[side * side];
        var nrm = new Vector3[side * side];
        for (int z = 0; z <= N; z++)
            for (int x = 0; x <= N; x++)
            {
                float wx = x / (float)N * (2 * HalfSize) - HalfSize;
                float wz = z / (float)N * (2 * HalfSize) - HalfSize;
                int i = z * side + x;
                pos[i] = new Vector3(wx, Height(wx, wz), wz);
                // 高さ関数の勾配から解析的な法線 (-dh/dx, 1, -dh/dz)
                float dhx = 0.55f * 0.33f * MathF.Cos(wx * 0.33f) * MathF.Cos(wz * 0.38f)
                          + 0.30f * 0.5f * MathF.Cos((wx + wz) * 0.5f);
                float dhz = -0.55f * 0.38f * MathF.Sin(wx * 0.33f) * MathF.Sin(wz * 0.38f)
                          + 0.30f * 0.5f * MathF.Cos((wx + wz) * 0.5f);
                nrm[i] = Vector3.Normalize(new Vector3(-dhx, 1, -dhz));
            }

        var idx = new List<int>(N * N * 6);
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
                idx.AddRange([a, b, c, b, d, c]);   // Bepu の上面法線 winding (Q16)
            }
        return (pos, nrm, idx.ToArray());
    }
}
