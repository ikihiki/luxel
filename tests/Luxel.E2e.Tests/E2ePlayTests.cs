using System.Runtime.CompilerServices;
using Luxel;
using Luxel.Gallery;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Gallery.E2eTests;

/// <summary>リポジトリルート解決 — dotnet test の cwd は bin 配下なので、ストーリーの相対パス資産
/// (assets/goldens) が解決できるよう cwd をリポジトリルートへ移す。</summary>
internal static class RepoRoot
{
    private static bool _done;

    public static void Ensure()
    {
        if (_done) return;
        _done = true;
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (string? d = start; d is not null; d = Path.GetDirectoryName(d))
                if (File.Exists(Path.Combine(d, "Luxel.slnx")))
                {
                    Environment.CurrentDirectory = d;
                    return;
                }
    }
}

/// <summary>play カタログ — テスト検出時に全ストーリーを **GPU なしで** 一度構築し、
/// 登録された play を列挙する (構築が GPU フリーなのは IGpuScene 規約と DocsIndex で実証済み)。</summary>
internal static class E2eCatalog
{
    private static readonly Lazy<IReadOnlyList<(string Path, int Index, string Name)>> Lazy = new(Discover);

    public static IReadOnlyList<(string Path, int Index, string Name)> All => Lazy.Value;

    private static IReadOnlyList<(string, int, string)> Discover()
    {
        RepoRoot.Ensure();
        // ストーリー登録 (module initializer) を確実に走らせる
        RuntimeHelpers.RunModuleConstructor(typeof(GalleryHost).Module.ModuleHandle);
        var resources = new Luxel.Resources.ResourceSystem(
            sources: Luxel.Resources.ResourceSystemDefaults.BuiltinSources(assetRoot: Environment.CurrentDirectory),
            steps: [.. Luxel.Resources.ResourceSystemDefaults.BuiltinSteps(), new Luxel.Imaging.ImageSharpDecoder()]);

        var list = new List<(string, int, string)>();
        foreach (StoryInfo s in StoryRegistry.All)
        {
            if (s.RealWindowOnly) continue;
            var ctx = new StoryContext(resources);
            ctx.SetNavigator(static _ => { });
            try { _ = s.Build(ctx); }
            catch { continue; }   // 構築に GPU/実窓が要るストーリーは対象外
            for (int i = 0; i < ctx.Plays.Count; i++)
                list.Add((s.Path, i, ctx.Plays[i].Name));
        }
        return list;
    }
}

/// <summary>GPU + GalleryHost をテストアセンブリで 1 個共有する (GPU は直列 — コレクションで直列化)。
/// GPU が無い環境ではテストを Skip にする (silent pass にしない)。</summary>
public sealed class GpuGalleryFixture : IDisposable
{
    public GalleryHost? Host { get; }
    public string SkipReason { get; } = "";
    private readonly GpuDevice? _device;
    private readonly VectorFont? _font;

    public GpuGalleryFixture()
    {
        RepoRoot.Ensure();
        try
        {
            _device = new GpuDevice(Luxel.Vulkan.VulkanBackend.Create());
            _font = VectorFont.LoadSystem();
            Host = new GalleryHost(_device, _font);
        }
        catch (Exception e)
        {
            SkipReason = $"GPU デバイスを作成できません — E2E を SKIP: {e.Message}";
        }
    }

    public void Dispose()
    {
        Host?.Dispose();
        _font?.Dispose();
        _device?.Dispose();
    }
}

[CollectionDefinition("e2e-gpu", DisableParallelization = true)]
public sealed class E2eGpuCollection : ICollectionFixture<GpuGalleryFixture>;

/// <summary>1 play = 1 テスト。表示名 = (パス, play 番号, play 名)。
/// `dotnet test --filter` で絞れる (例: DisplayName~Button)。golden は検証のみ —
/// 更新は Gallery の `-- vk e2e --update` で行う (テストから golden を書かない)。</summary>
[Collection("e2e-gpu")]
public sealed class E2ePlayTests(GpuGalleryFixture fx)
{
    public static TheoryData<string, int, string> Plays
    {
        get
        {
            var data = new TheoryData<string, int, string>();
            foreach ((string path, int index, string name) in E2eCatalog.All)
                data.Add(path, index, name.Length > 0 ? name : "(default)");
            return data;
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Plays))]
    public void Play(string story, int index, string name)
    {
        Skip.If(fx.Host is null, fx.SkipReason);
        _ = name;   // 表示名用 (テストエクスプローラ/--filter で識別する)
        E2e.RunPlay(fx.Host!, story, index, "vk");
    }
}
