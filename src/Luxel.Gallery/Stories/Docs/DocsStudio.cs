using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — Luxel Studio (ゲームエディタ、ADR-0015〜0018 / ToDo 27)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsStudio
{
    [Story("Docs/Studio", Order = 44)]
    public static Widget Studio(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());
        return DocNew(ctx, $$"""
        # Luxel Studio (ゲームエディタ)

        **C# ソリューションを書かずに** — プロジェクト作成 → シーン編集 → csx で挙動 → エディタ内プレイ → フォルダごと出荷 — を完走するためのエディタ群です。決定は [ADR-0015](story:ADR/0015-Game-Project-Scene-Format) (形式) / [ADR-0016](story:ADR/0016-Scene-Editor-Stack) (シーンエディタ) / [ADR-0017](story:ADR/0017-Play-In-Editor) (プレイ) / [ADR-0018](story:ADR/0018-Csx-Behaviour-Model) (csx)。通し実演は [Apps/Studio/CoinGame](story:Apps/Studio/CoinGame) と [Apps/Studio/Mixed3D](story:Apps/Studio/Mixed3D)。

        設計と実装は **2D/3D 両対応**です: シーンは `space` を宣言し、座標はコンポーネント (`transform2d` / `transform3d` は別スキーマ) の関心事。エディタは共有シェル + 空間アダプタ、ランタイムのコンパイラはコア + space 別バックエンドです。プロジェクトは 2D/3D シーンを混在でき、csx から `world.RequestScene("res://scenes/arena.scene.json")` で別 space のシーンへ遷移できます。

        ## プロジェクト形式 (Luxel.SceneEdit)

        プロジェクト = フォルダ: `project.luxel` (名前/開始シーン/ウインドウ) + `scenes/*.scene.json` + `scripts/*.csx` + `atlas/*.atlas.json` + アセット。参照はすべて `res://` (フォルダ相対)。シーンは安定 id のエンティティ + コンポーネント + タイルレイヤで、値は**形ベース** (Bool/Number/Text/Vec2/3/4/Raw) — 型付きの意味 (Int/Enum/AssetRef/Quat/Color) は `IComponentSchema` の解釈なので、スキーマに無いコンポーネントも劣化なく往復します。JSON は決定的 (キー順固定・LF・タイルは行 CSV) で git 差分が読めます。

        ## シーンエディタ ([SceneEditorView](story:Controls/SceneEditorView/Basic))

        テキスト ([ADR-0006](story:ADR/0006-Editor-New-Stack))・ノード ([ADR-0009](story:ADR/0009-Node-Editor-Stack)) に続く**第 3 の Transaction スタック** (不変 SceneDoc + SceneChange + 1 tx = 1 undo)。空間の知識 (変換/ヒット/カメラ/描画) は `ISceneSpaceAdapter` に閉じ、シェルは view-local px と id しか扱いません。選択/移動 (軸分解ハンドル X=赤/Y=緑)/複製 (Ctrl+D)/削除、[タイル描き込み](story:Controls/SceneEditorView/Tiles) (ブラシ = 直線補間/矩形/消しゴム/スポイト、1 ストローク = 1 undo)。

        [SceneInspector](story:Controls/SceneEditorView/Inspector) はスキーマ駆動 (space で出し分け・全フィールド型のエディタ・Quat はオイラー度表示で Quat 保存)。編集は `ApplyEdit` の Transaction 経由なので**インスペクタ編集も undo できます**。[アセット](story:Controls/SceneEditorView/Assets)は AssetBrowser + `*.atlas.json` の PropertyGrid 編集。

        ## 3D シーン ([SceneEditorView/ThreeD](story:Controls/SceneEditorView/ThreeD))

        `space: "3d"` のシーンは `SceneSpace3DAdapter` が担当します。`OrbitCamera` で地面グリッド、`transform3d` の AABB ワイヤ、X/Y/Z 軸ハンドル、marquee、orbit/dolly を描画・操作します。共有シェルは 2D と同じ選択/Transaction/undo 経路を使い、空間固有の数学はアダプタへ閉じています。

        ランタイム側は `SceneCompiler.Compile3D` が `Player3DWorld` を構築します。`mesh3d` は `glb` AssetRef として VFS 上の存在を検証し、`camera3d` は `OrbitCamera` 初期値になります。v1 の Player 3D 描画は deterministic な Scene2D 投影ワイヤで、`Player3DWorld.QueryAabb` / `RayCast` と csx の `self.Pos3D` 更新を提供します。デモは [Apps/Player/ThreeD](story:Apps/Player/ThreeD)。

        ## 挙動 = csx ([ADR-0018](story:ADR/0018-Csx-Behaviour-Model))

        エンティティに `behaviour { script: res://scripts/foo.csx }` を載せ、スクリプトは globals へ Update を代入するだけ:

        ```csharp
        Update = (self, world, dt) =>
        {
            if (world.KeysDown.Contains("Right")) self.Pos.X += 120f * dt;
        };
        ```

        1 スクリプト 1 コンパイルで全エンティティ共有・状態はコンポーネント側。globals の `world` は `IPlayerWorld` なので 2D/3D 共通の `Time` / `KeysDown` / `Find` / `RequestScene` が使え、3D エンティティは `self.Pos3D` を更新できます。失敗契約: コンパイル失敗 = 旧維持 + 診断 / 実行時例外 = 無効化 + 診断 / Reload で復帰。[ScriptEditor](story:Apps/Player/ScriptEditor) が TextEditorView (診断波線 + 補完、`ScriptWorkspace` にランタイムと同じ globals を渡す) と保存 → ホットリロードを実演します。

        ## プレイと出荷

        - **プレイインエディタ** ([ADR-0017](story:ADR/0017-Play-In-Editor) / [デモ](story:Apps/Player/PlayInEditor)): ▶ = SceneCompiler で**別インスタンスを都度構築**、⏹ = 破棄 — 編集データは汚染されません。⏸/ステップは固定 dt (1/60) で決定的
        - **ランタイム** (`Luxel.Player` / [2D](story:Apps/Player/Basic) / [3D](story:Apps/Player/ThreeD)): VFS からプロジェクトを読み一方向構築。exe `Luxel.Player.App <フォルダ> [vk|dx] [--frames N]`、引数省略時は exe 隣の `project/`
        - **出荷** (`PlayerShipper` / `--ship` / リポジトリの `ship-verify.ps1`): `dotnet publish` (self-contained、shaders 同梱) + プロジェクトを `project/` へコピー — フォルダごと配布してダブルクリックで動きます

        ## 現状の制約

        2D タイル/エンティティと 3D メッシュの見た目はプレースホルダ描画です。3D は glb 参照の存在検証とワイヤ表示までで、フル `scene_pbr` / glTF 展開 / Bepu 物理統合は実アセット描画フェーズで接続します。音は未配線。フル DockHost シェル (メニュー/コマンドパレット/Problems/csx DocumentProvider/gizmo オーバーレイ) は独立タスクとして扱います。
        """, toc: true);
    }
}
