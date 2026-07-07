using System.Reflection;
using Luxel.Resources;

namespace LuxelCavern.Core;

/// <summary>
/// ゲーム同梱アセット (Core.dll 埋め込み: レベル .tmj / フォント .ttf) を読む <see cref="ResourceSystem"/> を組む。
/// スキーム <c>res://</c> の <see cref="EmbeddedResourceSource"/> を 1 つ載せるだけ — 以降 <c>res://…</c> URI で
/// キャッシュ/型付きノードとして扱える。single-file publish でも loose ファイルに依存しない。
/// </summary>
public static class CavernResources
{
    /// <summary>埋め込みアセットを読む ResourceSystem を新規に組む (呼び出し側が Dispose する)。</summary>
    public static ResourceSystem CreateEmbedded()
        => new(sources: [new EmbeddedResourceSource(typeof(CavernResources).Assembly)]);

    /// <summary>Core が埋め込みアセットを持つアセンブリ。</summary>
    public static Assembly AssetAssembly => typeof(CavernResources).Assembly;
}
