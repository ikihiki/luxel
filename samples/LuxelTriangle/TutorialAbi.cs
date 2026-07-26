using System.Runtime.InteropServices;

/// <summary>CPU/Slang ABI used by the LuxelTriangle tutorial shader.</summary>
public static class TutorialAbi
{
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
}
