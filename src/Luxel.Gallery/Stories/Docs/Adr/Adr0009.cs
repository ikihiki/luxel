using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("ADR/0009-Node-Editor-Stack", Order = 80)]
    public static Widget Adr0009(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0009 — ノードエディタは汎用の Transaction ベース新スタックとして作る

        - **Status**: Accepted
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        ノードグラフ (ノード + ポート + 接続線を空間上で編集する UI) が欲しくなりました。将来の用途はアニメーションブレンド・シェーダ/マテリアル・オーディオパッチ・レンダーグラフ可視化など複数あり、**特定ドメインに縛らない汎用のノード編集キャンバス** (TouchDesigner / Blueprint 流の土台) を最初に据えたい、という力学です。

        既存資産には近いものが 3 つありますが、いずれもそのままエディタの土台にはなりません:

        - **`Luxel.Diagram`** — measure → layout → 描画の**一方向レンダラ**。編集可能なモデル・ポート・ドラッグが無く、辺は直線。ただし階層ランク配置 (`DiagramLayout.Arrange`) は自動整列に**再利用**できる
        - **`Luxel.Animation/Graph`** — 実行時評価 DAG (`GraphNode.Evaluate`)。接続は blend ノード内の親子参照で暗黙、座標もポートも無く、空間エディタのモデルではない
        - **`Luxel.Editor` (テキスト新スタック、[ADR-0006](story:ADR/0006-Editor-New-Stack))** — こちらは**アーキテクチャの手本**になる: 不変状態 + Transaction + 純射影ジオメトリ + 薄い view が、canvas 非依存で headless テスト可能という Luxel の強みをそのまま出せている

        また描画・入力の素材は既に揃っています — `Scene2D.CubicTo` (接続線のベジェ)・`AddHit` のドラッグフック・`Affine2D` コンテナ変換 (pan/zoom)・`PopupPlacer` ([ADR-0007](story:ADR/0007-Floating-Ui-Placement) のノード追加パレット)・`DiagramLayout` (自動整列)。

        ## Decision

        テキスト新スタック ([ADR-0006](story:ADR/0006-Editor-New-Stack)) と**同じ骨格**で、canvas 非依存の新プロジェクト **`Luxel.NodeGraph`** を新規に作り、その上に薄い view `NodeGraphView` ([UiComponent]) を載せます。既存の Diagram / Animation.Graph は触らず並置します (Diagram の配置アルゴリズムだけ参照)。

        - **不変状態 + Transaction** — `NodeGraphDoc` (ノード/ポート/辺、不変・id 索引) + `GraphSelection` + `GraphViewport` (pan/zoom) を束ねた不変スナップショット `NodeGraphState`。編集は `GraphTransaction` が変更列 (`GraphChange`: ノード追加/削除/移動・辺接続/切断・ノードデータ更新) を 1 つに束ね、**1 Transaction = 1 undo**。反転変更で undo
        - **id は安定 → 位置写像は不要** — テキストのオフセットと違いノード/ポートは安定 id を持つので、`ChangeSet.MapPos` に相当する座標写像が要らない (テキストスタックからの**意図的な簡約**)。選択・装飾は id 参照で編集を生き延びる
        - **純射影ジオメトリ** — `GraphGeometry` が Doc + view 注入の `NodeMeasure` (種別→サイズ) + viewport から、ノード矩形・ポートアンカー点・接続線のベジェ制御点・`HitTest(world)` (ノード本体/ポート/辺/空白) を計算する。選択状態を持たない純関数で、canvas 無しで単体テスト可能 (`DiagramLayout.Arrange(spec, measure)` と同じ measure 注入の型)
        - **ドメイン非依存** — ノードの中身は不透明ペイロード + ホストが供給する `INodeCatalog` (種別とそのポート) で型付け。ノード本体内のインライン UI (スライダ/フィールド) はテキストスタックの widget ホスト (`WidgetResolver`/`WidgetSlots`) を**流用**。これでアニメ/シェーダ/オーディオが後から同じ view に載る
        - **既存素材を消費** — 接続線 = `Scene2D.BeginStroke` + `CubicTo`、ノードドラッグ = `AddHit` の onDrag (Splitter 流ゴースト追従、drop で 1 undo)、pan/zoom = **コンテナ `UiNode` の `Affine2D` 変換** (ヒットテストが自動追従)、パレット/コンテキストメニュー = `PopupPlacer`、自動整列 = `DiagramLayout`

        実装計画は ToDo/25 (S1〜S7)。

        ## Alternatives

        - **`Luxel.Diagram` を拡張してエディタ化** — 一方向レンダラで編集モデル・ポート・ドラッグ・ベジェ辺が無い。編集可能な不変モデルを後付けすると Diagram の描画専用の性質と衝突する → 土台としては却下 (配置アルゴリズムのみ再利用)
        - **`Luxel.Animation/Graph` を可視化してエディタ化** — 実行時評価ツリーで座標もポートも明示接続リストも無い。汎用要件 (任意ドメイン) も満たさない → 却下
        - **pan/zoom を `SurfaceView` + `Camera2D` の子キャンバスで実装** — `Camera2D` は `RetainedCanvas.Render` 時に効き、UI のヒットテスト経路 (`UiNode` の変換連鎖) に**乗らない**。ポート単位のヒットが要るノードエディタでは入力が破綻する → v1 は却下 (コンテナ `Affine2D` 変換を採用。将来 SurfaceView が要る規模になったら再検討)
        - **PointerEvent に修飾キーを今すぐ通す (Ctrl+Click の追加選択のため)** — フレームワーク横断の変更で、`UiHost.PointerDown/Move/Up` → `PointerEvent` → `HitTarget` を貫く必要がある。マルチカーソルの Alt+Click 延期 (ToDo/22 S7) と同じ穴 → v1 は延期し、範囲選択 + キーボード追従の修飾で代替 (穴が埋まったら追加クリック選択を足す)

        ## Consequences

        - ✅ 実証済みのテキストスタックのアーキ (不変 + Transaction + 純射影 + 薄い view) を再利用でき、ジオメトリが canvas 非依存で headless テスト可能
        - ✅ 描画・入力・配置・浮遊 UI の既存素材 (Scene2D ベジェ / AddHit / Affine2D / PopupPlacer / DiagramLayout) にそのまま載り、view の新規コードが薄い
        - ✅ ドメイン非依存 → アニメブレンド・シェーダ・オーディオパッチ・レンダーグラフ可視化が後から同じ control に載る (1 つ作れば横展開)
        - ✅ 安定 id によりテキストの座標写像を持たずに済み、コアがテキストスタックより単純
        - ⚠️ 新プロジェクト + view の表面積が増える (既存 Diagram / Animation.Graph と概念が近く、使い分けの説明が要る)
        - ⚠️ PointerEvent に修飾キーが無い制約で、追加クリック選択の操作感はフレームワーク拡張まで範囲選択で代替 (マルチカーソル / [ADR-0008](story:ADR/0008-Custom-Ime-Candidates) と共有の穴)
        - ⚠️ pan/zoom がコンテナ変換 1 枚の共有ワールドなので、入れ子で独立ズームが要る規模になったら `SurfaceView` 化の再検討が要る
        """, toc: true);
}
