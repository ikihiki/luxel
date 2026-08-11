using LuxelCavern.Core;
using Luxel.Typography;

namespace Luxel.Tests;

/// <summary>
/// 同梱フォントの読み込み <see cref="CavernAssets"/>: Core.dll 埋め込みの .ttf を <c>res://</c> (ResourceSystem)
/// 経由でロードできる (single-file publish で loose ファイルに頼らないための埋め込み) / 欠損はエラー。
/// </summary>
public class CavernAssetsTests
{
    [Fact]
    public void LoadBodyFont_FromEmbeddedResource_ViaResourceSystem()
    {
        using var res = CavernResources.CreateEmbedded();

        byte[] bytes = CavernAssets.LoadBytes(res, CavernAssets.BodyFontUri);
        Assert.True(bytes.Length > 100_000, "埋め込み TTF のバイトが取れている");

        using VectorFont font = CavernAssets.LoadBodyFont(res);
        Assert.NotNull(font);
    }

    [Fact]
    public void LoadBytes_MissingAsset_Throws()
    {
        using var res = CavernResources.CreateEmbedded();
        Assert.ThrowsAny<Exception>(() => CavernAssets.LoadBytes(res, "res://fonts/does-not-exist.ttf"));
    }
}
