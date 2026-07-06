using Luxel.Scripting;

namespace Luxel.Tests;

/// <summary>
/// ScriptHostRegistry (役割ごとの ScriptHost 登録簿) のテスト: 役割名でキャッシュ / 別プロファイルは別 host /
/// 同名再登録は無視 / 未登録は例外 / Contains・Names。ScriptHost ctor は Roslyn コンパイルを走らせない
/// (初回 Run まで遅延) ので高速。
/// </summary>
public class ScriptHostRegistryTests
{
    private static ScriptProfile Profile(string name)
        => new(name, [typeof(object).Assembly], ["System"], typeof(object));

    [Fact]
    public void GetOrAdd_CachesHostPerProfile()
    {
        var reg = new ScriptHostRegistry();
        ScriptHost h1 = reg.GetOrAdd(Profile("a"));
        ScriptHost h2 = reg.GetOrAdd(Profile("a"));
        Assert.Same(h1, h2);
        Assert.Same(h1, reg.Host("a"));   // 役割名で同じ実体
    }

    [Fact]
    public void DifferentProfiles_DifferentHosts()
    {
        var reg = new ScriptHostRegistry();
        Assert.NotSame(reg.GetOrAdd(Profile("a")), reg.GetOrAdd(Profile("b")));
    }

    [Fact]
    public void Register_Idempotent_KeepsFirst()
    {
        var reg = new ScriptHostRegistry();
        reg.Register(Profile("a"));
        ScriptHost first = reg.Host("a");
        reg.Register(Profile("a"));   // 同名再登録は無視 (暖まった host を保つ)
        Assert.Same(first, reg.Host("a"));
    }

    [Fact]
    public void Host_Unknown_Throws()
        => Assert.Throws<KeyNotFoundException>(() => new ScriptHostRegistry().Host("nope"));

    [Fact]
    public void Workspace_ResolvesPerProfile()
    {
        var reg = new ScriptHostRegistry();
        reg.Register(Profile("a"));
        Assert.NotNull(reg.Workspace("a"));
        Assert.Same(reg.Workspace("a"), reg.Workspace("a"));   // キャッシュ
    }

    [Fact]
    public void ContainsAndNames()
    {
        var reg = new ScriptHostRegistry();
        Assert.False(reg.Contains("a"));
        reg.Register(Profile("a"));
        Assert.True(reg.Contains("a"));
        Assert.Contains("a", reg.Names);
    }
}
