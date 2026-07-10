using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("ADR/0015-Game-Project-Scene-Format", Order = 86)]
    public static Widget Adr0015(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0015 — ゲームプロジェクト/シーン形式はエディタ専用モデル + 一方向コンパイルにする

        - **Status**: Accepted
        - **Date**: 2026-07-10
        - **Deciders**: ikihiki

        ## Context

        ゲームエディタ (Luxel Studio、ToDo/27) は「プロジェクト作成 → シーン編集 → csx → プレイ → 出荷」を C# ソリューション無しで完走させるため、**ゲームをデータとして表す形式**が要ります。制約は 4 つ:

        1. **エディタの要件**: 安定 id (選択/undo が編集を生き延びる)・スキーマに無いコンポーネントの保全 (手書き/将来バージョンのファイルを開いても壊さない)・差分の読める決定的な直列化 (git 管理)
        2. **ランタイムの要件**: Friflo ECS + 既存の 2D/3D スタックへ迷いなく構築できること
        3. **2D/3D 両対応** (ユーザー決定 2026-07-10): 実装フェーズは 2D 先行だが、形式・スキーマ・コンパイラのどの層にも 2D 前提を焼き込まない
        4. 既存の `WorldSave` (Friflo EntitySerializer) は**ゲーム内セーブ用**であり、未知保全・安定 id・差分 undo の観点ではエディタ形式に向かない

        ## Decision

        プロジェクトとシーンを**エディタ専用の不変モデル** (`Luxel.SceneEdit`、依存ゼロ) で表し、ランタイムへは **SceneCompiler が一方向に構築**します。

        - **プロジェクト = フォルダ**: `project.luxel` (名前/開始シーン/ウインドウ) + 規約配置 (`scenes/` `assets/` `scripts/`)。アセット参照は **`res://` (プロジェクトフォルダ相対) に統一** — png/wav/tmj/glb を同列に扱う
        - **シーン = `SceneDoc`**: `space: "2d" | "3d"` ヘッダ + 安定 id のエンティティ列 + タイルレイヤ。座標はコンポーネント側の関心事 (`transform2d` と `transform3d` は**別スキーマ**、混在や自動変換はしない)
        - **値は形ベース**: フィールド値は JSON の形そのもの (Bool/Number/Text/Vec2/Vec3/Vec4/Raw) で持ち、**型付きの意味 (Int/Enum/AssetRef/Quat/Color…) は `IComponentSchema` の解釈**とする。スキーマに無いコンポーネント/フィールドは形のまま素通しで往復 (未知保全が構造的に成立)
        - **`IComponentSchema` が単一の真実**: フィールド定義 (全型を初日から: Vec3/Quat/Color/AssetRef 含む) + 対応 space。インスペクタの UI 選択・パレットの出し分け・SceneCompiler の構築が全部これを参照する
        - **決定的 JSON**: キー順固定 (フィールドは名前順ソート)・インデント 2・LF・非 ASCII 素通し・数値は最短往復表現・タイルセルは行 CSV。`serialize(deserialize(s)) == s` を回帰で担保
        - `WorldSave` は従来どおりゲーム内セーブ専用に残す

        ## Alternatives

        - **WorldSave (Friflo EntitySerializer) をシーン形式に流用** — 実行時型に密結合で未知保全と安定 id が難しく、エディタの undo 差分も切りにくい → 却下 (エディタモデルを分離)
        - **Unity 式「常に 3D Transform」** — Luxel のランタイムは 2D/3D 別スタックで、2D に使わない z/quat を持たせると両者の橋渡しに暗黙変換が生まれる → 却下 (space 宣言 + 別スキーマ)
        - **値を型タグ付きで保存** (`{"t":"vec3","v":[…]}`) — 未知保全は楽だが冗長で手書きに向かない → 却下 (形ベース + スキーマ解釈)
        - **シーンを C# コードとして生成** — 「エディタだけで完走」の北極星に反し、ホットリロードも重い → 却下

        ## Consequences

        - ✅ 未知コンポーネント保全が値モデルの構造で成立し、バージョン間・手書きファイルに強い
        - ✅ スキーマ 1 箇所にフィールドを足せばインスペクタとランタイムの両方が追従する
        - ✅ 決定的 JSON で golden/git diff が安定する
        - ⚠️ Int/Float、Quat/Color の区別は保存形に現れない — スキーマ無しで開いた場合の表示は形どおり (Number/Vec4) に落ちる
        - ⚠️ エディタモデルと ECS 実型の二重性は残る — SceneCompiler (GE-3) がスキーマ経由で構築することで乖離を防ぐ
        """, toc: true);
}
