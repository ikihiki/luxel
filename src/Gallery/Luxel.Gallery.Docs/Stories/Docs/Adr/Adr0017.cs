using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story]
    public static StoryResult Adr0017(StoryContext ctx) => $$"""
        # ADR-0017 — プレイインエディタは「都度コンパイル + 停止で破棄」の別インスタンスで動かす

        {{Toc()}}

        - **Status**: Accepted
        - **Date**: 2026-07-11
        - **Deciders**: ikihiki

        ## Context

        Luxel Studio でシーンを編集しながら ▶ で即プレイできる必要があります。編集状態 (SceneDoc、[ADR-0016](story:Internals/ADR/Adr0016)) は不変 + Transaction、ランタイム (Player2DWorld、[ADR-0018](story:Internals/ADR/Adr0018)) は可変 — この 2 つの間で「プレイ中の変更が編集データを汚染する」「停止後に状態が残る」事故を構造的に防ぎたい。また golden 回帰のためプレイは決定的 (固定 dt) であること。

        ## Decision

        - **▶ = SceneCompiler で編集中の SceneDoc から新しい Player2DWorld を都度構築**する (csx ビヘイビアもこの時点でロード)。編集状態への参照は渡さない — コンパイルは一方向 ([ADR-0015](story:Internals/ADR/Adr0015))
        - **⏹ = プレイ world を捨てる**。プレイ中に起きた変更 (位置・コンポーネント値) は保存されない — Unity の「プレイ中の変更は消える」と同じ契約。次の ▶ は編集中の最新 SceneDoc から作り直す
        - **⏸ / ⏭ (1 ステップ)**: プレイは固定 dt (1/60) でのみ進む。ステップ実行で決定的にフレームを刻める (golden/デバッグ)
        - プレイ中の編集は**編集側にだけ**効く (次の ▶ から反映)。プレイ中のインスペクタ書き戻しは v1 スコープ外
        - エディタ内の表示はストーリー/シェルが Canvas2D 等で world.Render をホストする (exe と同じ描画コード — 見た目が実行時と一致)

        ## Alternatives

        - **編集 world をそのまま動かして undo で巻き戻す** — 物理/スクリプトの毎フレーム変更が undo 履歴を汚染し、csx の可変状態は Transaction モデルに乗らない → 却下
        - **プレイ状態を編集へ書き戻すモード (Unity の Play Mode Edit)** — 事故源。必要になったら「プレイ状態をシーンに焼く」明示コマンドとして別途 → 見送り
        - **プレイを別プロセス (Player.App) で起動** — 分離は最強だがステップ実行/オーバーレイ/ホットリロードの統合が重い。exe 起動は「実行」メニューとして残す → 却下 (in-process 別インスタンス)

        ## Consequences

        - ✅ 編集データの汚染と状態残留が構造的に起きない (参照を渡さない + 捨てる)
        - ✅ プレイ表示が exe と同じ Render を通る — 「エディタでは動くのに実行すると違う」を防ぐ
        - ✅ 固定 dt + ステップで golden/デバッグが決定的
        - ⚠️ ▶ のたびにコンパイル (シーン構築 + csx) が走る — 現規模では軽い。重くなったら csx キャッシュ (同一ソース再利用) を足す
        - ⚠️ プレイ中に「この配置いいな」を保存する導線が無い (明示コマンドとして将来)
        """;
}
