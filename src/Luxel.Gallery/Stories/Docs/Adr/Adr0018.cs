using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0018-Csx-Behaviour-Model", Order = 89)]
    public static Widget Adr0018(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0018 — ゲームの挙動は csx ビヘイビア (状態レス Update) で書く

        - **Status**: Accepted
        - **Date**: 2026-07-11
        - **Deciders**: ikihiki

        ## Context

        Luxel Studio ([ADR-0015](story:Internals/ADR/0015-Game-Project-Scene-Format)) の北極星は「C# ソリューション無しでゲームを 1 本出す」— エンティティの挙動をプロジェクトデータの一部として書ける仕組みが要ります。エンジンには `ScriptHost` (Roslyn csx、診断付き) と、capstone で実証した「コンパイル失敗で旧ロジック維持 + 診断公開」の運用があります。制約は: 決定性 (固定 dt・wall-clock 禁止 — golden/リプレイ)、ホットリロード (GE-5)、同じスクリプトを複数エンティティで共有できること。

        ## Decision

        エンティティに `behaviour { script: res://…/*.csx }` コンポーネント (組み込みスキーマ) を持たせ、`Luxel.Player` の `PlayerBehaviours` が ScriptHost でコンパイルして毎ステップ呼びます。

        - **スクリプト = 状態レスな Update 関数**: csx は globals の `Update = (self, world, dt) => { … }` を設定するだけ。**1 スクリプト 1 コンパイルで全エンティティが共有**し、エンティティ状態はコンポーネント (`self.Pos` / `Field`/`SetField`) に置く — 状態の単一の真実がシーンデータ側に残る
        - **失敗契約** (capstone の ScriptSystem と同じ): コンパイル失敗 = **旧 Update を維持**して診断公開 / 実行時例外 = そのスクリプトを**無効化**して診断公開 (毎フレームのスパム防止) / `Reload` で復帰。エディタの Problems ペイン (GE-5) はこの診断を表示する
        - **決定性**: スクリプトに渡るのは固定 dt と `world.Time` (固定 dt の累積) のみ。wall-clock/乱数はスコープ外 (固定シード乱数はゲーム API が必要になったら globals に足す)
        - globals は**空間非依存の共通部** (`PlayerEntity`/`Player2DWorld`)。3D の拡張 (M12) は space 別の globals 拡張として足す (2D/3D 両対応原則 6)

        ## Alternatives

        - **per-entity スクリプトインスタンス (スクリプトが状態を持つ)** — セーブ/リプレイ/プレイインエディタの破棄契約 ([ADR-0017 予定](story:Internals/ADR/Overview)) と衝突し、状態の在り処が二重になる → 却下 (状態はコンポーネント)
        - **ScriptSystem (Luxel.Scripting.Framework) の流用** — ECS のシステム単位で per-entity ビヘイビアの形でない → 却下 (ScriptHost 直結、失敗契約だけ踏襲)
        - **ビジュアルスクリプティング (NodeGraph)** — 資産はあるが v1 スコープ外 (ToDo/27) → 見送り
        - **C# クラス (コンパイル済みアセンブリ) のプラグイン** — 「エディタだけで完走」に反する → 却下

        ## Consequences

        - ✅ 挙動がプロジェクトデータ (.csx) になり、エディタ内編集 + ホットリロード (GE-5) にそのまま乗る
        - ✅ スクリプト共有 + 状態レスで、同じ敵を 100 体置いてもコンパイルは 1 回
        - ✅ 失敗してもゲーム/エディタが落ちない (旧維持 or 無効化 + 診断)
        - ⚠️ スクリプト間の直接呼び出しは無い — 連携はコンポーネント経由 (複雑な連携が要るゲームは C# コア方式が引き続き有効)
        - ⚠️ Roslyn コンパイルは初回が重い — 起動時 LoadAll でまとめて払う (ゲーム中のスパイクにしない)
        """, toc: true);
}
