using System.Numerics;
using System.Runtime.InteropServices;

namespace Luxel.Assets;

/// <summary>
/// RG-M5b 用の UI quad メッシュ。ローカル座標 (-0.5..0.5)×(-0.5..0.5) の平面、+Z 面 (法線 +Z) を持つ。
/// UI buffer のサンプリング用に UV (0..1) を頂点に持つ。Vertex stride = 24B (pos 12B + uv 8B + pad 4B)。
/// </summary>
public static class UiQuadMesh
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector2 Uv;
        public float Pad;
        public Vertex(Vector3 p, Vector2 uv) { Position = p; Uv = uv; Pad = 0; }
    }

    public const int VertexCount = 6;
    public const int VertexStride = 24;

    private static readonly Vertex[] _verts = Build();
    public static ReadOnlySpan<Vertex> Vertices => _verts;

    private static Vertex[] Build()
    {
        // 2 三角形で四角形。+Z 面、上方向=+Y、右方向=+X。
        // UV は (0,0)=左下、(1,1)=右上。Luxel の Y 軸は下方向に増加 (2D ラスタライザの慣習)、
        // よって UV.v の対応は: テクスチャ画像上の y を「上から下」へ並べたとき、quad の上端=v.0、下端=v.1 になる。
        var lb = new Vertex(new Vector3(-0.5f, -0.5f, 0f), new Vector2(0f, 1f));
        var rb = new Vertex(new Vector3(+0.5f, -0.5f, 0f), new Vector2(1f, 1f));
        var lt = new Vertex(new Vector3(-0.5f, +0.5f, 0f), new Vector2(0f, 0f));
        var rt = new Vertex(new Vector3(+0.5f, +0.5f, 0f), new Vector2(1f, 0f));
        return new[] { lb, rb, lt, rb, rt, lt };
    }
}
