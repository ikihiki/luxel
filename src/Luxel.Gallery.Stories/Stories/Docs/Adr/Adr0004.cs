using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0004-Compute-Rasterizer-Retained-2D", Order = 75, Toc = true)]
    public static StoryResult Adr0004(StoryContext ctx) => $$"""
        # ADR-0004 — 2D はコンピュートラスタライザ + 保持型キャンバス

        - **Status**: Accepted
        - **Date**: 2026-07-08 (記録日 — 決定自体は 2D 層の着手時)
        - **Deciders**: ikihiki

        ## Context

        UI ([ADR-0003](story:Internals/ADR/0003-Declarative-Signal-Ui))・docs・2D ゲームの土台として、GPU 抽象 ([ADR-0002](story:Internals/ADR/0002-Thin-Bindless-Gpu-Abstraction)) の上に 2D ベクター描画層が必要でした。要件と力学:

        - **ベクター品質** — テキスト (TTF 輪郭、日本語) と図形をズームしてもエッジが崩れないこと。docs のダイアグラム・数式・エディタまでこの層で描く
        - **部分更新が主戦場** — UI の定常フレームは「動いたものだけ」を書きたい (移動 = 変換だけ、色変更 = スタイルだけ)。bench 回帰ゲート (タイプ連打で再構築 ~3% 等) の土台になる
        - **バックエンド中立** — vk / dx ピクセル一致の規律に乗ること。バックエンド固有の 2D API (Direct2D 等) には依存できない
        - 参照実装として Vello (Rust) が「三角形分割しない compute ラスタライズ」の成立を実証している

        ## Decision

        **GPU コンピュートラスタライザ (Vello 風) + 保持型キャンバス (RetainedCanvas)** を自作します (`Luxel.Graphics.TwoD`)。核心は次の 4 点:

        - **三角形分割しない** — パスを線分のまま GPU に常駐させ、compute シェーダが画素ごとに巻き数/距離で被覆を計算して塗る (NonZero/EvenOdd、距離ベースの画面一定幅ストローク)。framebuffer は bindless バッファなのでバックエンド変更ゼロ
        - **Encode 1 回、ズームはカメラだけ** — ワールド座標で `Encode` したら `Camera2D` を変えるだけで連続拡縮できる (再エンコードも再分割もない)
        - **保持型ツリー + SoA** — `RetainedCanvas` がフレーム間で保持するノードツリーを提供し、データを Transform / Style / Clip / Order / Segment に分離して持つ。シェーダが per-path 変換を適用するため**移動 = 変換だけ書込、色変更 = スタイルだけ書込** (ジオメトリ不変)
        - **増分更新は「slot 据え置き、レンジは容量付き」** — Content 差し替えは容量内なら in-place、伸びたら末尾追記、空きが閾値を超えたときだけコンパクション。定常フレームのコストは O(変わったノード)

        現在の姿は [Reference/Guides/TwoD](story:Reference/Guides/TwoD) へ。部分更新量は `LastTransformWrites` 等で観測でき、bench が回帰を監視します。

        ## Alternatives

        - **Skia を製品描画経路に採用** — 実績は最大だが、レンダラが自前 GPU 抽象の外にあり bindless 経路・vk/dx ピクセル一致・部分更新の観測に載らない → 製品経路としては却下。ただし **CPU リファレンスバックエンド (Luxel.Graphics.TwoD.Skia) として検証用に併用**する
        - **三角形分割 (テッセレーション) 方式 (NanoVG / ImDrawList 系)** — ズームや形状変更のたびに再分割が走り、「Encode 1 回でスムーズズーム」が成立しない。曲線の分割粒度と AA 品質のトレードオフも抱える → 却下
        - **Direct2D / バックエンド固有 2D API** — vk 側に対応物がなく、バックエンド中立の規律と矛盾 → 却下
        - **SDF ベース (テキスト/図形を距離場テクスチャ化)** — 小さい字の品質とアトラス管理のコストが重く、任意パス (穴あき・複数パス合成) への一般化が難しい → 却下
        - **即時モード (毎フレーム頂点生成)** — 部分更新の要件と正反対。定常フレームで O(シーン全体) を払い続ける → 却下

        ## Consequences

        - ✅ ベクターのままズームしてもエッジが崩れず、テキストも同じパイプラインで描ける (日本語含む)
        - ✅ UI の定常フレームが「変わったものだけの書込」に落ち、`LastTransformWrites` 等の観測 + bench で回帰を機械的に検出できる
        - ✅ compute + bindless バッファだけで完結するため、バックエンド追加コストが GPU 抽象側に閉じる
        - ⚠️ **現状はブルートフォース** — 画素×線分 (+ bbox 早期スキップ) で、タイル binning は今後の課題。AA も 4x4 スーパーサンプルで解析的 AA ではない
        - ⚠️ compute ラスタライザは非標準経路 — RenderDoc 等での三角形パイプラインのデバッグ手法がそのまま効かず、問題調査は自前の観測カウンタと golden 比較が頼り
        - ⚠️ 容量付き slot の増分更新は複雑さの置き場 — コンパクション条件やノード増減の増分化 (保留中) など、チューニング項目を自前で抱える
        """;
}
