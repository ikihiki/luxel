namespace Luxel.Graphics.TwoD;

/// <summary>2Dラスタライザが提供する機能。</summary>
[Flags]
public enum Rasterizer2DCapabilities
{
    None = 0,
    GpuCommandRecording = 1 << 0,
    CpuRgbaTarget = 1 << 1,
    BindlessImages = 1 << 2,
    RetainedIncrementalUpdates = 1 << 3,
}

/// <summary>
/// バックエンド非依存の2Dラスタライザ。作成したsessionはrasterizerより先に破棄し、
/// rasterizer/sessionは同時に複数スレッドから使用しない。
/// </summary>
public interface IRasterizer2D : IDisposable
{
    string Name { get; }
    Rasterizer2DCapabilities Capabilities { get; }
    IRasterScene2D CreateScene(Scene2D scene);
    IRasterScene2D CreateScene(RetainedCanvas canvas);
}

/// <summary>ラスタライザが所有するエンコード済みシーン/session。</summary>
public interface IRasterScene2D : IDisposable
{
    IRasterizer2D Rasterizer { get; }
    void Render(Camera2D camera, IRasterTarget2D target, bool transparent = false);
}

/// <summary>ラスタライザ固有の出力先を表す共通契約。</summary>
public interface IRasterTarget2D
{
    uint Width { get; }
    uint Height { get; }
}

internal interface IRetainedCanvasSink
{
    void FullSync(RetainedCanvas canvas);
    void WriteTransform(int index, GpuTransform value);
    void WriteStyle(int index, GpuStyle value);
    void WriteClip(int index, GpuClip value);
    void WriteSegment(int index, GpuSegment value);
    void WritePath(int index, GpuPath value);
    void WriteOrder(uint[] order);
}
