namespace Luxel.Graphics.RenderGraph;

/// <summary>パスの確定後の内部表現。順序付きリストに保持され、Compile/Execute が走査する。</summary>
internal sealed class PassRecord
{
    public required string Name;
    public required PassQueue Queue;
    public required int Index;
    public required RenderPassKey Key;
    public List<ResourceAccess> Reads { get; } = new();
    public List<ResourceAccess> Writes { get; } = new();
    public List<SymbolicResourceAccess> SymbolicReads { get; } = new();
    public List<SymbolicResourceWrite> SymbolicWrites { get; } = new();
    public List<RenderPassKey> ControlDependencies { get; } = new();
    public Action<PassContext>? Body;
    public bool HasSideEffect;
    public bool Culled = false;
}

internal readonly record struct SymbolicResourceAccess(
    RenderResourceVersionId Version,
    ResourceUsage BufferUsage,
    TextureUsage TextureUsage);

internal readonly record struct SymbolicResourceWrite(
    RenderResourceVersionId Version,
    RenderResourceVersionId? Predecessor,
    ResourceUsage BufferUsage,
    TextureUsage TextureUsage);
