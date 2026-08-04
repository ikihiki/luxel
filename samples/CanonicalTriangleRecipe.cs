using System.Runtime.InteropServices;

/// <summary>The canonical first-triangle data and identity shared by native and browser samples.</summary>
public static class CanonicalTriangleRecipe
{
    public const string Story = "Examples/3D/Triangle";
    public const string Recipe = "canonical-triangle-v1";

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
