using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — 汎用ノードエディタ (Luxel.NodeGraph / NodeGraphView、ADR-0009)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsNodeEditor
{
    [Story("Docs/NodeEditor", Order = 42)]
    public static Widget NodeEditor(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ノードエディタ (Luxel.NodeGraph)

        ノード + ポート + 接続線を空間上で編集する**汎用ノードグラフ制御**です。特定ドメインに縛られない土台 (TouchDesigner / Blueprint 流) で、アニメブレンド・シェーダ・オーディオパッチ・レンダーグラフ可視化などが同じ control に載ります。決定は [ADR-0009](story:ADR/0009-Node-Editor-Stack)。テキスト新スタック ([ADR-0006](story:ADR/0006-Editor-New-Stack)) と**同じ骨格**で、canvas 非依存のコアと薄いビューに分かれます。

        ```mermaid
        flowchart TB
        core[Luxel.NodeGraph - Doc/装飾/幾何/コマンド] --> view[NodeGraphView - 描画+入力の薄いビュー]
        ```

        ## コア (Luxel.NodeGraph、依存ゼロ)

        - **不変状態 + Transaction**: `NodeGraphDoc` (ノード/ポート/辺、id 索引) + `GraphSelection` + `GraphViewport` を束ねた不変 `NodeGraphState`。編集は `GraphTransaction` が変更列 (`GraphChange`: 追加/削除/移動・接続/切断・データ更新) を 1 つに束ね、**1 Transaction = 1 undo**。
        - **安定 id → 座標写像が不要**: テキストのオフセットと違いノード/ポート/辺は安定 id を持つので、`ChangeSet.MapPos` に相当する写像が要りません。選択・装飾は id 参照で編集を生き延びます (テキストスタックからの意図的な簡約)。
        - **装飾は第一級**: ノードバッジ・辺ハイライト・ポートヒント・進行中ワイヤ・ノード内インライン枠 (`GraphDecoration`)。`AffectsLayout` 分類でオーバーレイ (再射影不要) とサイズ変化を分けます。
        - **純射影ジオメトリ**: `GraphGeometry` が Doc + view 注入の `NodeMeasure` から、ノード矩形・ポートアンカー・接続線のベジェ・`HitTest`・world↔screen 変換を計算します。選択状態を持たない純関数で、サイズ測定を外注するので **canvas なしで単体テスト**できます (`Luxel.Typography` 非依存)。

        ## ビュー (NodeGraphView — [UiComponent])

        状態を投影して塗るだけの薄いビューです。**pan/zoom は world コンテナノードの `Affine2D` 変換**なのでヒットテストが自動追従します。

        - **ノードドラッグで移動** (複数選択はまとめて 1 undo)、**クリック選択 / 空白ドラッグで範囲選択 (marquee)**、**ホイールでカーソル中心ズーム**
        - **配線**: ポートからドラッグ → 進行中ワイヤ (互換ポートなら緑・不可なら赤) → ドロップで接続。型キー一致・向き・重複を `GraphConnect` が検証。**辺クリック選択 → Delete で切断**
        - **ノード内インライン UI**: `NodeInlineDecoration` + `WidgetResolver` でノード本体に実 Widget (スライダ等) をホスト
        - **ノード追加パレット**: 右クリックで `INodeCatalog` の種別を [PopupPlacer](story:ADR/0007-Floating-Ui-Placement) のメニューに並べ、クリック位置に追加
        - **自動整列**: 辺の依存に沿って左→右にランク配置 (`AutoLayout`) + `FitToView` + グリッドスナップ

        実物: [Controls/NodeGraphView/Basic](story:Controls/NodeGraphView/Basic) (ノード/選択/移動) / [Wiring](story:Controls/NodeGraphView/Wiring) (配線) / [Widgets](story:Controls/NodeGraphView/Widgets) (ノード内 UI + パレット) / [AutoLayout](story:Controls/NodeGraphView/AutoLayout) (式グラフの自動整列)。

        > [!NOTE]
        > ドメインを載せるには `INodeCatalog` (種別と工場) を実装し、必要ならノード内の値編集を `WidgetResolver` で供給します。既存の [Luxel.Diagram](story:Docs/Controls) は**表示専用の図**、`Luxel.Animation` の Graph は**実行時評価ツリー**で、本エディタ (編集キャンバス) とは別物です (配置アルゴリズムのみ Diagram と同型)。

        > [!NOTE]
        > **制約**: `PointerEvent` にボタン/修飾キーが無いため、空白ドラッグを範囲選択に割り当てた結果、**対話的な pan ドラッグは延期**しています (pan は `PanBy`/`FitToView` の API、ズームはホイールで対話可)。フレームワークがポインタ修飾を通せば追加選択・pan ドラッグを足せます (マルチカーソルの Alt+Click 延期と同じ穴)。
        """, toc: true, fences: DocsFences));
}
