namespace Luxel.RenderGraph;

/// <summary>パスの確定後の内部表現。順序付きリストに保持され、Compile/Execute が走査する。</summary>
internal sealed class PassRecord
{
    public required string Name;
    public required PassQueue Queue;
    public required int Index;
    public List<ResourceAccess> Reads { get; } = new();
    public List<ResourceAccess> Writes { get; } = new();
    public Action<PassContext>? Body;
    public bool Culled = false;  // Compile 相でデッドパス除去 (RG-M2 で実装)
}
