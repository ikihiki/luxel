using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>CPU/Slang ABI used by the staged LuxelTriangle tutorials.</summary>
public static class TutorialAbi
{
    public const int VertexSize = 32;
    public const int DrawArgsSize = 4;
    public const int Vertex3DSize = 32;
    public const int DrawArgs3DSize = 176;
    public const int PostProcessArgsSize = 20;

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

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex3D
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Uv;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DrawArgs3D
    {
        public uint VertexBufferIndex;
        public uint IndexBufferIndex;
        public uint TextureIndex;
        public uint SamplerIndex;
        public Matrix4x4 Model;
        public Matrix4x4 ViewProjection;
        public Vector4 LightDirection;
        public uint Stage;
        private uint _pad0, _pad1, _pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PostProcessArgs
    {
        public uint SourceBufferIndex;
        public uint DestinationBufferIndex;
        public uint Width;
        public uint Height;
        public uint StridePixels;
    }

    public static float VisibleAspect(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return (float)width / height;
    }
}

public enum TutorialStage
{
    Triangle,
    Texture,
    Transform,
    Lighting,
    Graph,
    PostProcess,
}
