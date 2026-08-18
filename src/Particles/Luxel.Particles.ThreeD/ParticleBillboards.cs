using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.AssetsGpu;

namespace Luxel.Particles.ThreeD;

/// <summary>
/// <see cref="ParticleSystem"/> をカメラ向きビルボード (インスタンス quad) で 3D 描画する。生存パーティクルを
/// <see cref="RenderBuffer{T}"/> の instance 配列に詰め、<c>billboard.slang</c> が SV_InstanceID から
/// 各パーティクルを 6 頂点の quad にカメラの right/up 軸で展開する。深度テストあり・書き込み無し + アルファブレンド。
/// 描画順は発生順 (ソートしない — 半透明の割り切りを Docs に明記)。
/// </summary>
public sealed class ParticleBillboards : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Instance   // 32B — billboard.slang の BillboardInstance と一致
    {
        public float PosX, PosY, PosZ, Size;
        public float R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Args       // 96B — billboard.slang の Args と一致
    {
        public Matrix4x4 ViewProj;
        public float RightX, RightY, RightZ;
        public uint InstIndex;
        public float UpX, UpY, UpZ;
        public uint Pad;
    }

    private readonly ParticleSystem _system;
    private readonly RenderBuffer<Instance> _instances;
    private readonly GpuPipeline _pipeline;
    private bool _disposed;

    public ParticleBillboards(GpuDevice device, ParticleSystem system, GpuFormat colorFormat = GpuFormat.Rgba8Unorm)
    {
        _system = system;
        _instances = new RenderBuffer<Instance>(device, system.Capacity, "particleBillboards");
        var pipelineDesc = new GpuGraphicsPipelineDesc(
            new GpuAttachmentLayout(colorFormat, GpuFormat.D32Float));
        _pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("billboard"), pipelineDesc);
    }

    /// <summary>直近の <see cref="Sync"/> 時点の生存インスタンス数。</summary>
    public int InstanceCount { get; private set; }

    /// <summary><see cref="ParticleSystem.Update"/> 後に生存パーティクルを instance バッファへ詰め GPU へ反映する。</summary>
    public void Sync()
    {
        ParticleBuffer b = _system.Buffer;
        ParticleConfig cfg = _system.Config;
        bool animSize = cfg.Size.IsAnimated;
        Span<Instance> data = _instances.Data;

        for (int i = 0; i < b.Count; i++)
        {
            float t01 = b.Age[i] / MathF.Max(1e-6f, b.LifeMax[i]);
            float size = animSize ? cfg.Size.Eval(t01) : b.Size[i];
            uint c = ParticleColor.Multiply(cfg.Color.Eval(t01), b.Tint[i]);
            data[i] = new Instance
            {
                PosX = b.PosX[i],
                PosY = b.PosY[i],
                PosZ = b.PosZ[i],
                Size = size,
                R = (c & 0xFF) / 255f,
                G = ((c >> 8) & 0xFF) / 255f,
                B = ((c >> 16) & 0xFF) / 255f,
                A = ((c >> 24) & 0xFF) / 255f,
            };
        }
        InstanceCount = b.Count;
        _instances.MarkDirty();
        _instances.FlushImmediate();
    }

    /// <summary><c>BeginRendering</c> と <c>EndRendering</c> の間で呼ぶ。<paramref name="camRight"/>/<paramref name="camUp"/>
    /// はカメラのスクリーン軸 (ワールド空間) — <see cref="CameraAxes"/> で得る。</summary>
    public void Draw(GpuCommandBuffer cmd, Matrix4x4 viewProj, Vector3 camRight, Vector3 camUp)
    {
        if (InstanceCount == 0) return;
        var args = new Args
        {
            ViewProj = Matrix4x4.Transpose(viewProj),   // row-major 行ベクトル規約 (シェーダで mul(v, M))
            RightX = camRight.X, RightY = camRight.Y, RightZ = camRight.Z,
            InstIndex = _instances.Buffer.BindlessIndex,
            UpX = camUp.X, UpY = camUp.Y, UpZ = camUp.Z,
        };
        cmd.SetGraphicsPipeline(_pipeline)
            .SetRasterizerState(GpuRasterizerState.Default)
            .SetDepthStencilState(GpuDepthStencilState.Default with { DepthTest = true })
            .SetBlendState(GpuBlendState.AlphaBlend)
            .SetRootArguments(args).Draw(6, (uint)InstanceCount);
    }

    /// <summary>視点 (eye) と注視点 (target) からビルボード展開用のスクリーン軸 (right/up) を計算する
    /// (<see cref="System.Numerics.Matrix4x4.CreateLookAt"/> と同じ右手系基底)。</summary>
    public static (Vector3 Right, Vector3 Up) CameraAxes(Vector3 eye, Vector3 target)
    {
        Vector3 z = Vector3.Normalize(eye - target);              // 後方 (RH)
        Vector3 x = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z));   // right
        Vector3 y = Vector3.Cross(z, x);                         // up
        return (x, y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeline.Dispose();
        _instances.Dispose();
    }
}
