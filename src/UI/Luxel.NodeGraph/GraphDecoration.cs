using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>ノードバッジの種別 (エラーリング/警告/情報)。色は装飾側が持つ。</summary>
public enum GraphBadge { Error, Warning, Info }

/// <summary>
/// グラフへの**装飾** — ノード/辺/ポートに紐づく描画メタデータ ([[Luxel.Document.Decoration]] 相当)。テキストと違い
/// 対象は**安定 id** なので、編集追従は座標写像ではなく <see cref="Map"/> による「対象が消えたら落とす」存在フィルタ。
/// <see cref="AffectsLayout"/> は「ノードの再射影 (サイズ変化) を要するか」の分類 — バッジ/ハイライト/進行中ワイヤ=不要
/// (オーバーレイのみ、進行中ワイヤ 60fps の根拠) / ノード内インライン枠=要 (S3/S6 のノード幾何に効く)。
/// </summary>
public abstract record GraphDecoration
{
    /// <summary>並び替え用のキー (決定的な描画順のため。通常は対象ノード/辺 id)。</summary>
    public abstract int SortKey { get; }
    /// <summary>ノードの再射影 (サイズ変化) を要するか。false = 矩形オーバーレイのみ。</summary>
    public abstract bool AffectsLayout { get; }
    /// <summary>対象が <paramref name="doc"/> に存在すれば自身を、削除されていれば null を返す (編集追従)。</summary>
    public abstract GraphDecoration? Map(NodeGraphDoc doc);
}

/// <summary>ノードの隅に付くバッジ — エラーリング/警告など。<see cref="Text"/> は任意の短い注記。</summary>
public sealed record NodeBadgeDecoration(int NodeId, GraphBadge Kind, uint Color, string? Text = null) : GraphDecoration
{
    /// <inheritdoc/>
    public override int SortKey => NodeId;
    /// <summary>オーバーレイのみ。</summary>
    public override bool AffectsLayout => false;
    /// <inheritdoc/>
    public override GraphDecoration? Map(NodeGraphDoc doc) => doc.HasNode(NodeId) ? this : null;
}

/// <summary>辺のハイライト — 選択/経路強調/エラー配線など。</summary>
public sealed record EdgeHighlightDecoration(int EdgeId, uint Color, float Width = 2f) : GraphDecoration
{
    /// <inheritdoc/>
    public override int SortKey => EdgeId;
    /// <summary>オーバーレイのみ。</summary>
    public override bool AffectsLayout => false;
    /// <inheritdoc/>
    public override GraphDecoration? Map(NodeGraphDoc doc) => doc.HasEdge(EdgeId) ? this : null;
}

/// <summary>ポートのヒント光り — 配線中に接続可能なポートを光らせる等。</summary>
public sealed record PortHintDecoration(PortId Port, uint Color) : GraphDecoration
{
    /// <inheritdoc/>
    public override int SortKey => Port.Node;
    /// <summary>オーバーレイのみ。</summary>
    public override bool AffectsLayout => false;
    /// <inheritdoc/>
    public override GraphDecoration? Map(NodeGraphDoc doc) => doc.Port(Port) is not null ? this : null;
}

/// <summary>進行中ワイヤ — 配線ドラッグ中に出発ポート <see cref="From"/> からポインタ world 位置 <see cref="To"/> へ引く
/// 一時的な線 (まだ辺ではない)。view が毎フレーム push する想定 (レイアウト非依存で 60fps)。</summary>
public sealed record PendingWireDecoration(PortId From, Vector2 To, uint Color) : GraphDecoration
{
    /// <inheritdoc/>
    public override int SortKey => From.Node;
    /// <summary>オーバーレイのみ。</summary>
    public override bool AffectsLayout => false;
    /// <inheritdoc/>
    public override GraphDecoration? Map(NodeGraphDoc doc) => doc.Port(From) is not null ? this : null;
}

/// <summary>ノード本体に差すインライン枠 — <see cref="Width"/>×<see cref="Height"/> を占有し view が <see cref="Key"/> から
/// 実 Widget を解決する ([[Luxel.Document.WidgetDecoration]] 相当、コアは Widget 型を知らない)。ノードのサイズに効くので
/// <see cref="AffectsLayout"/>=true。実際の widget ホストは S6 で view 側に足す。</summary>
public sealed record NodeInlineDecoration(int NodeId, float Width, float Height, object Key) : GraphDecoration
{
    /// <inheritdoc/>
    public override int SortKey => NodeId;
    /// <summary>ノードの空間を占有するのでレイアウトに効く。</summary>
    public override bool AffectsLayout => true;
    /// <inheritdoc/>
    public override GraphDecoration? Map(NodeGraphDoc doc) => doc.HasNode(NodeId) ? this : null;
}
