using System.Numerics;
using System.Runtime.InteropServices;

namespace Luxel.Assets;

/// <summary>RG-M4 用の固定キューブメッシュ。中心 (0,0,0)、辺 1 (-0.5..+0.5)、面ごとに正規化された法線。</summary>
public static class CubeMesh
{
    /// <summary>キューブメッシュの頂点レイアウト (position + normal、24B)。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex
    {
        /// <summary>頂点位置。</summary>
        public Vector3 Position;
        /// <summary>面法線。</summary>
        public Vector3 Normal;
        /// <summary>位置と法線を指定して作成。</summary>
        public Vertex(Vector3 p, Vector3 n) { Position = p; Normal = n; }
    }

    /// <summary>頂点数 (6 面 × 2 三角 × 3 頂点 = 36)。</summary>
    public const int VertexCount = 36;
    /// <summary>頂点 1 個のバイト数 (Vector3 × 2 = 24B)。</summary>
    public const int VertexStride = 24;

    private static readonly Vertex[] _vertices = Build();

    /// <summary>non-indexed の頂点列 (triangle list)。</summary>
    public static ReadOnlySpan<Vertex> Vertices => _vertices;

    private static Vertex[] Build()
    {
        // 6 面 × 2 三角 × 3 頂点 = 36 頂点。各面のローカル UV 軸ごとに四隅を取り、(0,1,2)+(2,1,3) で 2 三角形。
        var vs = new List<Vertex>();
        void Face(Vector3 origin, Vector3 ax, Vector3 ay, Vector3 n)
        {
            Vector3 p00 = origin;
            Vector3 p10 = origin + ax;
            Vector3 p01 = origin + ay;
            Vector3 p11 = origin + ax + ay;
            vs.Add(new Vertex(p00, n));
            vs.Add(new Vertex(p10, n));
            vs.Add(new Vertex(p01, n));
            vs.Add(new Vertex(p10, n));
            vs.Add(new Vertex(p11, n));
            vs.Add(new Vertex(p01, n));
        }
        const float s = 0.5f;
        // +X / -X
        Face(new(+s, -s, -s), new(0, +1, 0), new(0, 0, +1), new(+1, 0, 0));
        Face(new(-s, -s, +s), new(0, +1, 0), new(0, 0, -1), new(-1, 0, 0));
        // +Y / -Y
        Face(new(-s, +s, -s), new(0, 0, +1), new(+1, 0, 0), new(0, +1, 0));
        Face(new(-s, -s, +s), new(0, 0, -1), new(+1, 0, 0), new(0, -1, 0));
        // +Z / -Z
        Face(new(-s, -s, +s), new(+1, 0, 0), new(0, +1, 0), new(0, 0, +1));
        Face(new(+s, -s, -s), new(-1, 0, 0), new(0, +1, 0), new(0, 0, -1));
        return vs.ToArray();
    }
}
