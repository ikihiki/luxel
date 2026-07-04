using Luxel.Ecs;
using Luxel.Assets;
using Friflo.Engine.ECS.Systems;
using Luxel.RenderGraph;

namespace Luxel.AssetRuntime;

/// <summary>
/// Friflo の <see cref="SystemGroup"/> 風に <see cref="IRenderExtractor"/> を Schedule に組み込む薄い wrapper。
///
/// <para><b>使い方</b>:</para>
/// <code>
/// var root = ScheduleRoot.Create(world);
/// var rg = new Luxel.RenderGraph.RenderGraph(device);
/// var extractor = new Render3DExtractSystem(world, device);
///
/// // PreRender phase に Extractor を走らせる system を登録
/// root.GetChild(Phase.PreRender)?.Add(new ExtractorSystem(extractor, device, frame: 0));
///
/// // Render phase で RenderGraph を実行する system を登録 (ユーザ側で BeginPass 等を組む)
/// </code>
/// </summary>
public sealed class ExtractorSystem : BaseSystem
{
    private readonly IRenderExtractor _extractor;
    private readonly GpuDevice _device;
    private ulong _frame;

    public ExtractorSystem(IRenderExtractor extractor, GpuDevice device, ulong frame = 0)
    {
        _extractor = extractor;
        _device = device;
        _frame = frame;
    }

    protected override void OnUpdateGroup()
    {
        _extractor.Extract(new ExtractContext(_device, frameIndex: _frame));
        _frame++;
    }
}

public static class SystemGroupExtensions
{
    /// <summary>名前で子 SystemGroup を取得 (Luxel の Phase 規約と組み合わせて使う)。</summary>
    public static SystemGroup? GetChild(this SystemGroup parent, string name)
    {
        foreach (var child in parent.ChildSystems)
            if (child is SystemGroup g && g.Name == name) return g;
        return null;
    }
}
