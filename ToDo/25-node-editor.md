# 25 — 汎用ノードエディタ (Transaction ベース新スタック)

## 概要

ノード + ポート + 接続線を空間上で編集する**汎用ノードグラフ制御**を新規に作る。特定ドメイン (アニメ/シェーダ/オーディオ/レンダーグラフ) に縛らない TouchDesigner / Blueprint 流の土台で、テキスト新スタック ([ADR-0006](../src/Luxel.Gallery/Stories/Docs/DocsAdr.cs)) と**同じ骨格** (不変状態 + Transaction + 純射影ジオメトリ + 薄い view) を鏡写しにする。決定は **ADR-0009** (`ADR/0009-Node-Editor-Stack`) が正 — 着手前に読むこと。既存 `Luxel.Diagram` / `Luxel.Animation/Graph` は触らず並置する (Diagram の配置アルゴリズムのみ再利用)。

## 背景と現状 (調査結果)

既存資産はどれもエディタの土台にならない (詳細は ADR-0009 の Alternatives):

- **`Luxel.Diagram`** ([src/Luxel.Diagram/DiagramLayout.cs](../src/Luxel.Diagram/DiagramLayout.cs)): `DiagramLayout.Arrange(spec, measure)` = measure 注入の階層ランク配置 (longest-path ランキング + 循環安全 + 辺端をノード箱にクリップ)。一方向レンダラでポート/ドラッグ/編集モデル無し・辺は直線。**配置アルゴリズムは S7 の自動整列に再利用**。描画パターン (`DiagramBlock.RealizeCore` [DiagramBlock.cs:44-127]、色グループごとに `Scene2D` を作り Z 別の子 `UiNode` にする + Effect で recolor) はノード描画の手本
- **`Luxel.Animation/Graph`**: 実行時評価 DAG。座標/ポート/明示接続リスト無し → エディタモデルではない
- **`Luxel.Editor`** ([src/Luxel.Editor/](../src/Luxel.Editor/)): アーキの手本。`EditorState`/`Transaction`/`TransactionSpec` (不変スナップショット + 変更束ね)、`ChangeSet` (ここはノードでは不要 = 安定 id)、`EditorGeometry` (純射影・TextLayout・選択非保持)、`History` (1 tx=1 undo)、`Decoration`+`DecorationTable`+`StateEffect`+`IDecorationProvider` (第一級の装飾状態)、`WidgetResolver`/`WidgetSlot` (インライン widget ホスト)

描画・入力の素材 (調査で確認):

- **接続線のベジェ**: `Scene2D.CubicTo(c0,c1,x,y)` [src/Luxel.TwoD/Scene2D.cs:104] + `BeginStroke(color,width)` :74 (幅は screen px)。曲線は追加時に de Casteljau で自動フラット化。UiComponent の custom 描画は `CreateRoot` → `node.ContentColors=true` → `node.Content = scene` (`Canvas2D` [src/Luxel.Controls/Canvas2D.cs:47] が最小手本)。ドラッグ中の再エンコードは `UiNode.ReserveContent` [src/Luxel.TwoD/Retained/UiNode.cs:94] で in-place 更新に載せる
- **ドラッグ入力**: controls は `OnPointerDown` を override せず、`RealizeCore` で `ctx.AddHit(node, rect, onDragStart/onDrag/onDragEnd/onClick/onContext/onHover, cursor)` [src/Luxel.UI/Widget.cs:30-77,216] を登録。`onDragStart`+`onDrag` があると `Draggable` 扱いでポインタキャプチャ。`PointerEvent` [src/Luxel.UI/PointerEvent.cs] は座標 (X/Y ローカル・ScreenX/Y・StartX/Y・DeltaX/Y) のみ。ゴースト追従は `Splitter` [src/Luxel.Controls/Splitter.cs:46-65] が手本 (`e.DeltaX` を使う)。選択ドラッグは `RichTextEditor` [RichTextEditor.cs:368-370]
- **⚠️ PointerEvent に修飾キーが無い** (確認済み) — Shift/Ctrl/Alt は `KeyEvent`/`KeyGesture` のみ [src/Luxel.Input/Input.cs:6,11]。マルチカーソルの Alt+Click 延期 (ToDo/22 S7) と同じ穴。**追加クリック選択は v1 では延期**し、範囲選択 + キーで押下中修飾を追う形で代替
- **pan/zoom**: `Camera2D` [src/Luxel.TwoD/Primitives.cs:90-107] は `RetainedCanvas.Render` 時に効き **UI ヒット経路に乗らない**。→ pan/zoom は**コンテナ `UiNode` の `Affine2D` 変換** [src/Luxel.TwoD/Affine2D.cs、`TryInvert` :35] で実装するとヒットテストが自動追従 (`ScrollBars` [ScrollModel.cs:144] が `bar.Transform = Affine2D.Translate(...)` を使う先例)。`RectF` [src/Luxel.TwoD/RectF.cs] を world 矩形/ヒットに
- **浮遊 UI**: `PopupPlacer.Solve` [src/Luxel.UI] + `OverlayEntry.Anchored` (ADR-0007) をノード追加パレット/コンテキストメニューに消費

## 設計

### コア: `Luxel.NodeGraph` (canvas 非依存、純データ + 純射影)

テキストスタックとの対応:

| テキスト (`Luxel.Editor`) | ノード (`Luxel.NodeGraph`) |
| --- | --- |
| `TextDoc` (行索引・不変) | `NodeGraphDoc` (ノード/ポート/辺、id 索引・不変) |
| `EditorSelection` (複数レンジ) | `GraphSelection` (選択ノード id 集合 + 辺 id 集合 + main) |
| `EditorState` (Doc+Sel+装飾) | `NodeGraphState` (Doc+Sel+`GraphViewport`+装飾) |
| `ChangeSet`+`MapPos` | **不要** (安定 id) — `GraphChange` の列だけ |
| `Transaction`/`TransactionSpec` | `GraphTransaction`/`GraphTransactionSpec` |
| `History` (1 tx=1 undo, Invert) | 同型 (反転変更で undo) |
| `EditorGeometry` (純射影) | `GraphGeometry` (純射影・measure 注入) |
| `Decoration`/`StateEffect`/`Provider` | 同型 (ノード/ポート/辺の装飾) |
| `WidgetResolver`/`WidgetSlot` | 同型 (ノード内インライン UI) |

**データモデル** (不変):

```csharp
public readonly record struct PortId(int Node, int Port);          // 安定 id
public enum PortDir { In, Out }

public sealed record NodePort(int Id, PortDir Dir, string TypeKey, string Label, bool Multi = false);
public sealed record GraphNode(int Id, string Kind, string Title, Vec2 Pos,
                               IReadOnlyList<NodePort> Ports, object? Data = null, bool Collapsed = false);
public sealed record GraphEdge(int Id, PortId From, PortId To);     // From=Out 側, To=In 側

public sealed class NodeGraphDoc            // 不変・id 索引 (TextDoc 相当)
{
    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }
    public GraphNode Node(int id);          // 索引引き
    public NodeGraphDoc Apply(GraphChangeSet changes);   // 変更適用 → 新 Doc
}
```

**変更 (Transaction)**: `GraphChange` は差分の union — `AddNode(GraphNode)` / `RemoveNode(int)` / `MoveNode(int, Vec2 delta)` / `SetNodeData(int, object)` / `SetCollapsed(int,bool)` / `Connect(GraphEdge)` / `Disconnect(int edgeId)`。`GraphChangeSet` = その列。`GraphTransactionSpec { Changes, Selection?, Viewport?, Effects? }`。`GraphTransaction.State` が適用後 `NodeGraphState` (遅延・キャッシュ)。**各 change は `Invert(doc)` を持ち** (MoveNode↔逆 delta、AddNode↔RemoveNode、Connect↔Disconnect)、History が反転 tx を作る = **1 Transaction = 1 undo** (複数ノード移動やパレットからの追加も 1 打鍵 1 undo)。安定 id なので座標写像は無し。

**装飾** (第一級・S2): `GraphDecoration` = ノードバッジ (エラーリング/警告)・辺ハイライト・ポート有効性 (接続先候補の光り)・進行中ワイヤ。`AffectsLayout` 分類 (ノードサイズを変える折り畳み/ポート増減 = 要再射影 vs バッジ/ハイライト = オーバーレイのみ)。`DecorationTable` (owner 別・不変) + `StateEffect`/`SetDecorations` + `IGraphDecorationProvider` (同期 pull)。テキスト S2 と同型。

**ジオメトリ** (`GraphGeometry`、純射影・canvas 非依存):

```csharp
public delegate Size NodeMeasure(GraphNode node);   // view が注入 (ラベル幅/ポート数→サイズ)

public sealed class GraphGeometry
{
    public GraphGeometry(NodeGraphDoc doc, NodeMeasure measure, GraphViewport viewport, GraphConfig cfg);
    public RectF NodeRect(int nodeId);                 // world
    public Vec2 PortAnchor(PortId port);               // world (辺の端点)
    public GraphWire Wire(int edgeId);                 // CubicTo 制御点 4 つ (out→in、水平接線)
    public GraphHit HitTest(Vec2 world);               // Node/Port/Edge/Empty + id
    public IReadOnlyList<WidgetSlot> WidgetSlots();    // ノード内インライン UI 枠
    public Vec2 WorldToScreen(Vec2 w);                 // viewport (pan/zoom Affine2D)
    public Vec2 ScreenToWorld(Vec2 s);
    public RectF ContentBounds();                      // fit-to-view 用
}
```

選択状態を持たない純関数。ポートのノード内配置は measure + ポートリストから決定的。**行 (ノード) キャッシュはノード + サイズ依存装飾を鍵に** — バッジ/ハイライト変更では再射影せず (テキスト S3 の「オーバーレイは行キャッシュ非依存」と同じ根拠、進行中ワイヤ 60fps)。`Assert.Same/NotSame` でキャッシュを実証。

### view: `NodeGraphView` [UiComponent] (Luxel.Controls、Luxel.NodeGraph 参照)

`NodeGraphState`/`History`/`GraphGeometry` を保持し、入力を `GraphTransaction` 化し、ジオメトリの矩形/ワイヤを塗る薄いビュー:

- **描画**: グリッド背景 → 辺 (`BeginStroke`+`CubicTo`、型別色) → ノード本体 (`FillRoundedRect` タイトルバー + ポートドット + ラベル `ctx.Font.AppendText`) → 選択ハイライト → 範囲選択矩形 → 進行中ワイヤ。`ContentColors=true`、`ReserveContent` でドラッグ中の in-place 更新
- **pan/zoom**: コンテナ `UiNode.Transform = Affine2D`。中ボタン/Space ドラッグで pan、ホイールでカーソル中心ズーム。ヒットは自動追従
- **入力** (`AddHit` フック): ノード本体ドラッグ → `MoveNodes` (ゴースト追従、drop で 1 undo) / ポートからドラッグ → 進行中ワイヤ、互換ポートで drop → `Connect` (空白 drop → パレット) / 空白ドラッグ → 範囲選択 / クリック → 選択 (追加選択は範囲 + キー修飾で代替、Ctrl+Click 追加は延期) / 右クリック → `PopupPlacer` のパレット/削除メニュー
- **ノード内インライン UI**: `WidgetResolver` (キー→Widget) + `WidgetSlots` の `Realize`/`OnChildNeedsRealize` (テキスト S5 流用)。ホストドメインがスライダ/フィールドをノード本体に差す
- **`INodeCatalog`** (ホスト供給): 種別一覧 + 各種別のポート定義 + パレット表示名。ドメイン非依存の要 (Controls は Roslyn/audio 等に非依存)

## ステージ

1. **S1** — コアデータ。`NodeGraphDoc`/`GraphNode`/`NodePort`/`GraphEdge` (不変・id 索引) + `GraphSelection` + `GraphViewport` + `NodeGraphState`/`GraphTransaction`/`GraphTransactionSpec` + `GraphChange` (各 `Apply`/`Invert`) + `History` (1 tx=1 undo, coalesce は移動連続のみ)。単体テスト (Apply/Invert 往復・複数変更 1 undo・接続/切断・孤立辺の掃除・id 安定)。UI 非接続 = golden 影響なし
2. **S2** — 装飾を第一級状態に。`GraphDecoration` 種別 + `AffectsLayout` 分類 + `Map` (削除ノード/辺に紐づく装飾を drop) + `DecorationTable` + `StateEffect`/`SetDecorations` + `IGraphDecorationProvider` + `NodeGraphState.Decorations` 統合 (編集で写像・undo 追従)。単体テスト。golden 影響なし
3. **S3** — `GraphGeometry` (純射影、`NodeMeasure` 注入)。`Luxel.NodeGraph` に Luxel.Typography 参照は**持たせない** (measure は view が注入 = DiagramLayout 流、コアは依存ゼロ)。ノード矩形/ポートアンカー/ワイヤベジェ/`HitTest`/`WidgetSlots`/world↔screen/`ContentBounds`。キャッシュ (Assert.Same/NotSame)。単体テスト (座標往復・ポートアンカー・ワイヤ端点・ヒット判定各種・ズーム変換)。golden 影響なし
4. **S4** — canvas 接続。`GraphCommands` (純関数 `(state)→transaction`: AddNode/DeleteSelection/MoveNodes/BoxSelect/SelectAll)。`NodeGraphView` [UiComponent]: グリッド + ノード + 辺描画、pan/zoom コンテナ変換、ノードドラッグ (移動=1 undo)、クリック選択、範囲選択。story `Controls/NodeGraphView/Basic` + vk/dx golden。**新 [UiComponent] → Reference/Overview golden 更新**
5. **S5** — 配線操作。ポートからドラッグ → 進行中ワイヤ → 互換ポートで `Connect`/`Disconnect`、型互換 (`TypeKey`) チェックと不可視覚化、辺選択 → 削除。story `Controls/NodeGraphView/Wiring` + golden
6. **S6** — ノード内インライン widget (`WidgetResolver`/slots 流用) + `PopupPlacer` のノード追加パレット (右クリック/ダブルクリック空白)。`INodeCatalog`。story `Controls/NodeGraphView/Widgets` (ノード内スライダ + パレット追加) + golden
7. **S7** — 自動整列 (`DiagramLayout.Arrange` を包む `GraphCommands.AutoLayout`) + fit-to-view + グリッドスナップ + **具体ドメインのデモ 1 つ** (小さな式グラフ or オーディオパッチ) でドッグフード = 汎用性の実証。Docs/NodeEditor 節 + デモストーリー。**全ステージ完了で本 MD を削除**

## 罠・注意

- **PointerEvent に修飾キーが無い** — Ctrl+Click 追加選択は延期 (ADR-0009)。範囲選択で代替。フレームワークが pointer 修飾を通したら追加 (マルチカーソルと共有の穴)
- **pan/zoom は Camera2D ではなくコンテナ `Affine2D`** — Camera2D は Render 時のみで UI ヒット経路に乗らない (ポートヒットが破綻する)
- **ドラッグ中の再エンコードは `ReserveContent`** に載せる (フル再構築を避ける、テキストのオーバーレイ 60fps と同じ規律)
- **コアは依存ゼロ** — サイズ測定は `NodeMeasure` を view が注入 (Typography 参照を持たない)。DiagramLayout の measure 注入と同型
- **既存 Diagram / Animation.Graph は無改変** — 概念が近いので Docs で使い分けを書く (Diagram=表示専用の図、NodeGraph=編集キャンバス、Animation.Graph=実行時評価)
- 決定性: ドラッグ/ズームは固定 dt、golden は play のみ生成 (README 規約)

## スコープ外 (v2 送り)

- ミニマップ・ノードグループ/フレーム・コメント付箋
- Ctrl+Click 追加クリック選択 (pointer 修飾のフレームワーク拡張が前提)
- 入れ子グラフ (サブグラフ) / `SurfaceView` 化した独立ズーム
- 交差最小化のある高度な自動整列 (S7 は DiagramLayout の宣言順ランクのみ)
- 具体ドメインの本格エディタ (シェーダグラフ等) — 本タスクは**汎用土台**まで。各ドメインは `INodeCatalog` を実装する別タスク
- コピー/ペースト・整列コマンド群 (揃える/分布) — 基盤が固まってから
