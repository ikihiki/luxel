namespace Luxel.Resources;

/// <summary>
/// <see cref="ResourceSystem"/> のコンストラクタに渡す組込み Source/Step の defaults を提供するヘルパ。
///
/// <para><b>設計</b>: 組込みも DI 経由 (ctor 配列渡し)。ResourceSystem 本体は Source/Step を自動生成しない。
/// 呼び出し側が「何を入れるか」を明示的に組み立てる。</para>
///
/// <code>
/// var res = new ResourceSystem(
///     sources: ResourceSystemDefaults.BuiltinSources(assetRoot: "./assets"),
///     steps:   ResourceSystemDefaults.BuiltinSteps());
///
/// // または追加 Source/Step とスプレッド:
/// var res = new ResourceSystem(
///     sources: [..ResourceSystemDefaults.BuiltinSources(vfs: myVfs), new MyCustomSource()],
///     steps:   [..ResourceSystemDefaults.BuiltinSteps(), new MyStep()]);
/// </code>
/// </summary>
public static class ResourceSystemDefaults
{
    /// <summary>組込み Source (<see cref="FileSource"/> + <see cref="HttpSource"/>) の配列を返す。
    /// VFS 未指定なら <see cref="PhysicalFileSystem"/> を <paramref name="assetRoot"/> ルートで生成。</summary>
    public static IResourceSource[] BuiltinSources(string? assetRoot = null, IVirtualFileSystem? vfs = null, HttpClient? http = null)
    {
        vfs ??= new PhysicalFileSystem(assetRoot ?? AppContext.BaseDirectory);
        http ??= new HttpClient();
        return new IResourceSource[]
        {
            new FileSource(vfs),
            new HttpSource(http),
        };
    }

    /// <summary>組込み Step (<see cref="TexDecoder"/>) の配列を返す。</summary>
    public static IResourceStep[] BuiltinSteps() => new IResourceStep[]
    {
        new TexDecoder(),
    };
}
