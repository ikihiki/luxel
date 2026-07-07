using Luxel.Resources;
using Luxel.Typography;

namespace LuxelCavern.Core;

/// <summary>
/// ゲーム同梱アセットの解決口。フォントは **Core.dll に埋め込み**、<see cref="ResourceSystem"/> 経由で
/// <c>res://</c> スキーム (<see cref="EmbeddedResourceSource"/>) から読む — cwd/loose ファイルに非依存なので
/// **single-file publish でも起動できる** (旧: <see cref="AppContext.BaseDirectory"/> 隣の Content を読んでいたため
/// 単一ファイルではフォントが見つからず失敗していた)。
/// </summary>
public static class CavernAssets
{
    /// <summary>本文/UI フォント (BIZ UDGothic — 日本語かな漢字 + ラテン) の埋め込み URI。</summary>
    public const string BodyFontUri = "res://fonts/BIZUDGothic-Regular.ttf";

    /// <summary>本文フォントを <see cref="ResourceSystem"/> 経由でロードして <see cref="VectorFont"/> を作る。</summary>
    public static VectorFont LoadBodyFont(ResourceSystem resources)
        => new(LoadBytes(resources, BodyFontUri));

    /// <summary>埋め込みアセットのバイト列を ResourceSystem 経由で取得する (ロード完了を待つ)。</summary>
    public static byte[] LoadBytes(ResourceSystem resources, string uri)
    {
        using ResourceHandle<byte[]> h = resources.Load<byte[]>(uri);
        h.Ready.GetAwaiter().GetResult();
        if (h.Error is not null)
            throw new InvalidOperationException($"埋め込みアセットの読み込みに失敗: {uri}", h.Error);
        return h.Value;
    }
}
