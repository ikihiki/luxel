using System.Runtime.InteropServices;

/// <summary>The canonical first-triangle data and identity shared by native and browser samples.</summary>
public static class CanonicalTriangleRecipe
{
    public const string Story = "Examples/3D/Triangle";
    public const string Shader = "tutorial_triangle";
    public const string Recipe = "canonical-triangle-v1";
    public const string ShaderSha256 = "4c3a36aa594306d963f00f1c0e6c5d7c62b1543748bfc882d72d0de8cf9a2cdd";
    public const int Width = 320;
    public const int Height = 240;
    public const int VertexSize = 32;
    public const int DrawArgsSize = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public float Px, Py, Pz, Pw;
        public float R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DrawArgs
    {
        public uint VertexBufferIndex;
    }

    public static Vertex[] CreateVertices() =>
    [
        new() { Px = 0, Py = -0.72f, Pz = 0, Pw = 1, R = 1, G = 0.18f, B = 0.18f, A = 1 },
        new() { Px = 0.72f, Py = 0.62f, Pz = 0, Pw = 1, R = 0.18f, G = 1, B = 0.28f, A = 1 },
        new() { Px = -0.72f, Py = 0.62f, Pz = 0, Pw = 1, R = 0.2f, G = 0.42f, B = 1, A = 1 },
    ];
}
