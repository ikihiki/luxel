namespace Luxel.Graphics.RenderGraph;

/// <summary>
/// パスのリソース依存を宣言する builder。<see cref="Execute"/> 呼び出しでグラフへ確定登録される。
/// 同じハンドルへの重複 Read/Write は許容する (Compile 相で集約)。
/// </summary>
public sealed class PassBuilder
{
    private readonly RenderGraph _graph;
    internal readonly PassRecord Record;

    internal PassBuilder(RenderGraph graph, PassRecord record)
    {
        _graph = graph;
        Record = record;
    }

    /// <summary>このパスが読むバッファを宣言する。</summary>
    public PassBuilder Read(BufferHandle handle, ResourceUsage usage = ResourceUsage.StorageBufferRead)
    {
        _graph.ValidateBufferHandle(handle, nameof(handle));
        if (usage.IsWrite()) throw new ArgumentException($"{usage} は書き込みです。 Write() を使ってください。", nameof(usage));
        Record.Reads.Add(new ResourceAccess(handle, usage));
        return this;
    }

    /// <summary>このパスが書くバッファを宣言する。</summary>
    public PassBuilder Write(BufferHandle handle, ResourceUsage usage = ResourceUsage.StorageBufferWrite)
    {
        _graph.ValidateBufferHandle(handle, nameof(handle));
        if (!usage.IsWrite()) throw new ArgumentException($"{usage} は読み込みです。 Read() を使ってください。", nameof(usage));
        Record.Writes.Add(new ResourceAccess(handle, usage));
        return this;
    }

    /// <summary>同一パスで読み書き両方する場合 (ReadWrite)。</summary>
    public PassBuilder ReadWrite(BufferHandle handle)
    {
        _graph.ValidateBufferHandle(handle, nameof(handle));
        // 集約は Compile 側に任せる: Reads と Writes 両方に登録する。
        Record.Reads.Add(new ResourceAccess(handle, ResourceUsage.StorageBufferReadWrite));
        Record.Writes.Add(new ResourceAccess(handle, ResourceUsage.StorageBufferReadWrite));
        return this;
    }

    // === Texture (RG-M6) ==========================================================

    /// <summary>このパスが読むテクスチャを宣言する。</summary>
    public PassBuilder Read(TextureHandle handle, TextureUsage usage = TextureUsage.SampledPixel)
    {
        _graph.ValidateTextureHandle(handle, nameof(handle));
        if (usage.IsWrite()) throw new ArgumentException($"{usage} は書き込みです。 Write() を使ってください。", nameof(usage));
        Record.Reads.Add(new ResourceAccess(handle.Id, ResourceUsage.None, usage, IsTexture: true));
        return this;
    }

    /// <summary>このパスが書くテクスチャを宣言する。</summary>
    public PassBuilder Write(TextureHandle handle, TextureUsage usage = TextureUsage.ColorAttachment)
    {
        _graph.ValidateTextureHandle(handle, nameof(handle));
        if (!usage.IsWrite()) throw new ArgumentException($"{usage} は読み込みです。 Read() を使ってください。", nameof(usage));
        Record.Writes.Add(new ResourceAccess(handle.Id, ResourceUsage.None, usage, IsTexture: true));
        return this;
    }

    /// <summary>stable symbolic resource version を読む。</summary>
    public PassBuilder Read(RenderResourceVersionId version, ResourceUsage usage = ResourceUsage.StorageBufferRead)
    {
        _graph.ValidateSymbolicVersion(version, nameof(version));
        if (usage == ResourceUsage.None) throw new ArgumentException("buffer usage に None は指定できません。", nameof(usage));
        if (usage.IsWrite()) throw new ArgumentException($"{usage} は書き込みです。 Write() を使ってください。", nameof(usage));
        Record.SymbolicReads.Add(new SymbolicResourceAccess(version, usage, default));
        return this;
    }

    /// <summary>stable symbolic texture version を読む。</summary>
    public PassBuilder Read(RenderResourceVersionId version, TextureUsage usage)
    {
        _graph.ValidateSymbolicVersion(version, nameof(version));
        if (usage.IsWrite()) throw new ArgumentException($"{usage} は書き込みです。 Write() を使ってください。", nameof(usage));
        Record.SymbolicReads.Add(new SymbolicResourceAccess(version, default, usage));
        return this;
    }

    /// <summary>stable symbolic buffer version を生成し、任意の predecessor version を宣言する。</summary>
    public PassBuilder Write(
        RenderResourceVersionId version,
        RenderResourceVersionId? predecessor = null,
        ResourceUsage usage = ResourceUsage.StorageBufferWrite)
    {
        _graph.ValidateSymbolicVersion(version, nameof(version));
        if (predecessor is { } previous) _graph.ValidateSymbolicVersion(previous, nameof(predecessor));
        if (usage == ResourceUsage.None) throw new ArgumentException("buffer usage に None は指定できません。", nameof(usage));
        if (!usage.IsWrite()) throw new ArgumentException($"{usage} は読み込みです。 Read() を使ってください。", nameof(usage));
        Record.SymbolicWrites.Add(new SymbolicResourceWrite(version, predecessor, usage, default));
        return this;
    }

    /// <summary>stable symbolic texture version を生成し、任意の predecessor version を宣言する。</summary>
    public PassBuilder Write(
        RenderResourceVersionId version,
        TextureUsage usage,
        RenderResourceVersionId? predecessor = null)
    {
        _graph.ValidateSymbolicVersion(version, nameof(version));
        if (predecessor is { } previous) _graph.ValidateSymbolicVersion(previous, nameof(predecessor));
        if (!usage.IsWrite()) throw new ArgumentException($"{usage} は読み込みです。 Read() を使ってください。", nameof(usage));
        Record.SymbolicWrites.Add(new SymbolicResourceWrite(version, predecessor, default, usage));
        return this;
    }

    /// <summary>stable pass key で explicit control dependency を宣言する。</summary>
    public PassBuilder DependsOn(RenderPassKey predecessor)
    {
        _graph.ValidatePassKey(predecessor, nameof(predecessor));
        Record.ControlDependencies.Add(predecessor);
        return this;
    }

    /// <summary>resource output を持たない observable side effect として pass を保持する。</summary>
    public PassBuilder SideEffect()
    {
        Record.HasSideEffect = true;
        return this;
    }

    /// <summary>execute lambda を確定登録する。パスはここで初めて RenderGraph に追加される。</summary>
    public void Execute(Action<PassContext> body)
    {
        Record.Body = body ?? throw new ArgumentNullException(nameof(body));
        _graph.AddPassInternal(Record);
    }
}

/// <summary>リソースアクセス記録。バッファとテクスチャの統一表現。</summary>
internal readonly record struct ResourceAccess(int ResourceId, ResourceUsage BufferUsage, TextureUsage TextureUsage, bool IsTexture)
{
    public ResourceAccess(BufferHandle handle, ResourceUsage usage)
        : this(handle.Id, usage, default, false) { }

    public GpuStage Stage() => IsTexture ? TextureUsage.Stage() : BufferUsage.Stage();
    public bool IsWrite() => IsTexture ? TextureUsage.IsWrite() : BufferUsage.IsWrite();
}
