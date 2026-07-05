using Luxel.Document;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>埋め込みブロック widget の生成文脈。</summary>
public sealed class BlockWidgetContext
{
    public required IBlockPayload Payload { get; init; }
    /// <summary>使える幅 (エディタ内側の論理 px)。widget はこの幅でレイアウトされる。</summary>
    public required float MaxWidth { get; init; }
    /// <summary>内部編集の確定 — 新しい payload を文書へ反映する (ReplacePayload 経由 = undo 可)。
    /// 埋め込み widget は文書を直接触らず必ずこれを呼ぶこと。</summary>
    public required Action<IBlockPayload> Commit { get; init; }
    /// <summary>ホストのテーマ signal (widget が Effect で読む用)。</summary>
    public required Signal<Theme> Theme { get; init; }
    /// <summary>表示だけの再構築要求 (undo に乗らない) — エディタがこのブロックを**作り直す**
    /// (ファクトリ再呼び出し = widget の内部状態は捨てられる)。UI スレッドから呼ぶこと。
    /// 自然サイズ変化や内容更新で**状態を保ったまま**再表示したいときは、こちらでなく
    /// <see cref="Luxel.UI.Widget.MarkNeedsRealize"/> を使う (同一インスタンスのまま再ホストされる)。</summary>
    public required Action Invalidate { get; init; }
}

public delegate Widget BlockWidgetFactory(BlockWidgetContext ctx);

/// <summary>
/// TypeId → 埋め込み widget ファクトリのレジストリ。**表示解釈はパーサー (フォーマット) 固有** —
/// エディタ毎にインスタンスを持ち (ctor で差し込み可)、プロセス共有の静的登録はしない。
/// 専用フォーマット (Strudel 等) は「IDocumentFormat + 構成済み BlockWidgetRegistry」を対で
/// 生成するファクトリ関数として配布し、解釈を固定する。markdown のような汎用フォーマットでは
/// アプリが自由に登録する。
/// ファクトリは任意の Widget を返してよい (Sparkline/Button 列/SurfaceView 等)。
/// IDisposable を返した場合、エディタが破棄時 (ブロック削除/差し替え/ツリー破棄) に Dispose する —
/// ライブブロックはここで再生停止/リソース解放を行う。未登録 TypeId はプレースホルダ表示。
/// </summary>
public sealed class BlockWidgetRegistry
{
    private readonly Dictionary<string, BlockWidgetFactory> _map = new();

    public BlockWidgetRegistry Register(string typeId, BlockWidgetFactory factory)
    {
        _map[typeId] = factory;
        return this;   // 連鎖登録用
    }

    public BlockWidgetFactory? Find(string typeId) => _map.GetValueOrDefault(typeId);
}
