using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("ADR/0016-Scene-Editor-Stack", Order = 87)]
    public static Widget Adr0016(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0016 — シーンエディタは第 3 の Transaction スタック + 空間アダプタで作る

        - **Status**: Accepted
        - **Date**: 2026-07-11
        - **Deciders**: ikihiki

        ## Context

        ゲームエディタ (Luxel Studio、[ADR-0015](story:ADR/0015-Game-Project-Scene-Format)) の中心はシーン編集 — エンティティの選択/移動/複製/削除、タイル描き込み、undo/redo です。リポジトリには実証済みの編集アーキテクチャが 2 本あります: テキスト ([ADR-0006](story:ADR/0006-Editor-New-Stack)) とノードグラフ ([ADR-0009](story:ADR/0009-Node-Editor-Stack))。どちらも「不変スナップショット + Transaction + 1 tx = 1 undo + 安定 id」の同じ骨格です。

        加えて 2D/3D 両対応の制約 (2026-07-10 決定) がある: 実装は 2D 先行 (M11) だが、3D (M12) をシェルの作り直しなしに足せる形にする必要があります。2D と 3D ではカメラの型 (pan/zoom vs 軌道)、ヒットテスト (矩形 vs レイピック)、座標型 (Vector2 vs Vector3) が違います。

        ## Decision

        `Luxel.SceneEdit` に**第 3 の Transaction スタック**を作り、ビューは**共有シェル + 空間アダプタ**に分けます。

        - **変更モデル** (NodeGraph S1 の鏡写し): `SceneChange` (AddEntity/RemoveEntity/Rename/SetComponent/RemoveComponent/**SetField**) + `SceneChangeSet` + `SceneEditState`/`SceneTransaction` + `SceneHistory` (1 tx = 1 undo、coalesce)。移動もインスペクタ編集も **SetField が形ベースの `SceneValue` で運ぶ**ため、2D/3D どちらの Transform にも同じ変更型が効く
        - **エンティティ順 = 描画順**: `RemoveEntity` の逆は**元の位置への復活** (AddEntity が挿入 Index を持つ) — undo で重なり順が変わらない
        - **カメラはコアの状態に持たせない**: NodeGraph は viewport を状態に含めたが、シーンでは空間ごとに型が違う (pan/zoom vs 軌道) ため**空間アダプタの所有**とする。undo 対象外なのは同じ
        - **`ISceneSpaceAdapter`** (Luxel.Controls): スクリーン↔ワールド変換・ヒットテスト・カメラ操作・world 描画・移動の変更列組み立て (`BuildMove`)・複製オフセットをすべて閉じる。**シェル (`SceneEditorView`) は view-local px と id しか扱わない** — 「ワールド = 平面」の前提をシェルに書いたら原則違反
        - **移動ギズモは軸分解** (X=赤/Y=緑、画面空間固定サイズ)。2D は「2 軸の特殊形」で、3D アダプタは Z を足すだけ
        - ドラッグはプレビュー状態で描き drop で 1 変更を記録 (NodeGraphView の手筋)。プレビューと確定が同じ `BuildMove` を通るので拘束/スナップの実装が一元化される

        ## Alternatives

        - **NodeGraphView を流用** — ポート/辺の意味論が不要で、エンティティ=コンポーネントの編集 (SetField) とタイルレイヤが乗らない → 却下 (骨格だけ鏡写し)
        - **2D と 3D で別エディタ** — 選択/undo/ツール切替/キーバインドが二重実装になり、混在プロジェクト (原則 6) で UX が割れる → 却下 (共有シェル + アダプタ)
        - **カメラをコア状態に統一型で持つ** — 2D と 3D のカメラを 1 つの型に押し込むと結局 union になり、コアが空間を知ってしまう → 却下 (アダプタ所有)

        ## Consequences

        - ✅ 実証済み骨格の 3 度目の適用 — 設計リスクが低く、テストの書き方も既知
        - ✅ 3D 対応 (M12) は「アダプタ 1 実装を足す」に局所化される (シェル無改修が検収条件)
        - ✅ SetField 1 本で移動/インスペクタ/スクリプト設定が同じ undo 経路に乗る
        - ⚠️ アダプタ境界の維持には規律が要る — シェルに空間ロジックを書くと 3D で崩壊する (レビュー観点)
        - ⚠️ プレースホルダ表示 (定型ボックス) は GE-2/GE-3 でスプライト/実表示に置き換えるまでの仮
        """, toc: true);
}
