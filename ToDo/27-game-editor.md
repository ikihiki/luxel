# 27 — ゲームエディタ「Luxel Studio」: エディタだけでゲームを一通り作る

## 概要

Workbench (ADR-0010〜0014) の上に、**ゲームを最初から最後まで — プロジェクト作成 → アセット取り込み → レベル/シーン編集 → 挙動スクリプト → プレイテスト → 出荷 — を C# ソリューションを書かずに完走できる**エディタを作る。GE-0〜GE-7 の 8 ワークストリーム。完成の定義は capstone 方式: **ミニゲーム 1 本をエディタ操作だけで作って publish する** (GE-7 dogfood)。

**解釈 (ユーザーに確認済みの前提として進める。違ったら指摘を)**: 「一通り作れる」= Unity 的な完結環境 (ゲーム = プロジェクトデータ + csx スクリプト、エンジン改造なしで 1 本出せる)。既存 capstone のような「C# コア + データ」方式のゲームにも部品 (シーンエディタ/インスペクタ) が使える設計にするが、北極星は前者。

## ゴール / 非ゴール

- **v1 は 2D のみ** (LuxelCavern 相当の 2D ゲームが作れる)。3D シーン編集は v2 (資産: OrbitCamera / PhysicsGizmos / glTF は揃っているので拡張点だけ確保)。
- **非ゴール (v1)**: 3D 編集、アニメーションタイムライン UI、ビジュアルスクリプティング (NodeGraph 資産はあるが挙動は csx で)、アセットストア的な配布、マルチユーザー編集、Windows 以外。
- README の Tier 2 候補 (音バス UI、i18n 等) はエディタが動いてから個別タスク化。

## 北極星シナリオ (GE-7 で実演するユーザー体験)

1. Studio を起動 → 「新規プロジェクト」→ フォルダに `project.luxel` + 既定構成が生える
2. AssetBrowser に png/wav をドロップ相当の操作で取り込み → SpriteAtlas/TileSet 定義
3. シーンエディタでタイルを描き、エンティティ (プレイヤー/敵/コイン) を配置、インスペクタでパラメータ調整
4. エンティティに `player.csx` を割り当て、エディタ内でコード編集 (診断付き) → プレイモードでホットリロード
5. ▶ でエディタ内プレイ (固定 dt、ポーズ/ステップ、gizmo オーバーレイ) → 停止で編集状態に戻る
6. 「出荷」→ 実行可能フォルダ (player + コンテンツ) が出て、リポジトリ外で起動する

## 現状資産 (すべて実装済み・流用する)

| 領域 | 資産 |
|---|---|
| シェル | `Luxel.Workbench` (IEditorDocument/IDocumentProvider/Workspace/DockTree/CommandRegistry) + `DockHost`/`DocumentTabs`/`StatusBar`/`MenuBar`/`CommandPalette` (Luxel.Controls)。Gallery chrome が実運用例 |
| 永続化 | `IFileStorage` (Memory/Physical + watch)/`DocumentStore` (open/save/外部変更検知) — src/Luxel.Workbench/DocumentStore.cs |
| インスペクタ | `PropertyGrid` + `ObjectDocument<T>` (Luxel.Controls/EditorDocuments.cs) |
| アセット一覧 | `AssetBrowser` (Luxel.Controls) |
| 編集スタック雛形 | Transaction+ChangeSet+History ×2 (`Luxel.Document` ADR-0006、`Luxel.NodeGraph` ADR-0009) — シーンエディタは **3 本目の鏡写し** |
| 2D ランタイム | `TileMap`/`TileSet` (.tmj import)/`TileMapLayer` (保持型・可視チャンク)/`SpriteAtlas`+`SpriteAtlasStep`/`CameraRig2D`/衝突 `QueryAabb`/`Sweep`/パーティクル (`ParticleConfigJson`+`ParticleConfigStep`)/`Gizmos2D` |
| ECS | Friflo + `WorldSave.Serialize/Deserialize` (version ラッパ) |
| ゲームホスト | `LuxelHostBuilder`/`GameScene`/`GameLoop`/FixedUpdate+補間/`SettingsStore`/Audio (Wav/ループ/ミキサ/バス)/DevTools (`WithDevTools`/DebugServer) |
| スクリプト | `ScriptHost` (Roslyn csx、filePath で実デバッガ可)/`ScriptSystem` (安定ラッパ Attach + Reload、失敗時旧維持) |
| リソース | `ResourceSystem` + `res://` `EmbeddedResourceSource` + `IVirtualFileSystem` |
| 出荷知見 | capstone の publish チェックリスト (shaders/フォント/glTF 同梱、self-contained、single-file、リポジトリ外起動検証) |

## アーキテクチャ決定 (着手時に ADR を起こす。次番号 0015〜)

- **ADR-0015 (GE-0 で起草): プロジェクト/シーン形式とデータ駆動ランタイム。** ゲームプロジェクト = フォルダ (`project.luxel` [JSON: 名前/開始シーン/ウインドウ設定] + `scenes/*.scene.json` + `assets/**` + `scripts/*.csx` + `atlas/*.json` 等)。シーンはエディタ専用モデル `SceneDoc` (安定 id のエンティティ列 + コンポーネントデータ + タイルレイヤ参照) を JSON 往復し、ランタイムは `SceneCompiler` が ECS world + TileMap + リソースへ**一方向に構築** (Friflo `WorldSave` を編集形式に直接使わない — エディタは安定 id/未知コンポーネント保全/差分 undo が要るため。WorldSave はゲーム内セーブ用のまま)。アセット参照は `res://` 相対パスで統一。
- **ADR-0016 (GE-1 で起草): シーンエディタ = 第 3 の Transaction スタック。** `Luxel.SceneEdit` (依存最小・canvas 非依存): `SceneDoc` (不変) + `SceneChange` (AddEntity/RemoveEntity/MoveEntity/SetComponent/PaintTiles…、各 Apply/Invert) + `SceneSelection` + `SceneTransaction`/`History` — NodeGraph S1 の設計をほぼ写経。ビューは `SceneEditorView` [UiComponent] (pan/zoom = Affine2D コンテナ、TileMapLayer/スプライト描画を流用、PointerEvent の Button/Modifiers [ADR-0011] を初めて本格消費)。
- **ADR-0017 (GE-4 で起草): プレイインエディタ実行モデル。** 編集 world とプレイ world は**別インスタンス** (SceneCompiler で都度構築 → 停止で破棄 = 状態リークなし、Unity の「プレイ中の変更は消える」と同じ契約)。エディタ内 viewport はストーリー内ゲーム描画の既存手法 (Apps/Game 系) を踏襲、固定 dt 駆動でエディタ golden も決定的に。ポーズ/ステップ/timescale は DevTools の既存機構を接続。
- **ADR-0018 (GE-3 で起草): csx ビヘイビアモデル。** エンティティのコンポーネントに `Behaviour { Script = "scripts/enemy.csx" }` を持たせ、`Luxel.Player` が `ScriptSystem` で Attach。スクリプトの globals はプレイヤー提供の `StudioGlobals` (world/entity/入力/時間/音)。コンパイル失敗は旧維持 + 診断をエディタの Problems へ (既存 ScriptSystem の契約そのまま)。

## ワークストリーム (依存順。各 WS は「次へ」1〜2 回想定、大きいものはステージ分割)

### GE-0 — プロジェクト/シーンモデル (純ロジック)

`Luxel.SceneEdit` プロジェクト新設: `GameProject` (project.luxel 往復 + 相対パス解決)、`SceneDoc` (エンティティ/コンポーネント/タイルレイヤ、JSON 往復 + **未知コンポーネントの素通し保全**)、コンポーネントスキーマ登録 (`IComponentSchema`: 型名 → フィールド定義 → 既定値。PropertyGrid/インスペクタとランタイム構築の共通語彙)。ADR-0015 起草。検証 = 単体テスト (往復/未知保全/パス解決)。golden 影響なし。
**罠**: シーン JSON の決定的整形 (キー順固定・改行固定) — golden とテキスト diff の要。float の往復は R フォーマット固定。

### GE-1 — シーンエディタ (2 ステージ)

- **S1 変更モデル + ビュー骨格**: `SceneChange`/`History` (NodeGraph S1 写経) + `SceneEditorView` (グリッド/pan/zoom、エンティティ = スプライトかプレースホルダ矩形で表示、クリック選択/矩形選択/ドラッグ移動 [スナップ]、Delete/複製、undo/redo)。story + golden。ADR-0016 起草。
- **S2 タイル描き込み**: TileSet パレットペイン + ブラシ/矩形/消しゴム/スポイト、`PaintTiles` change (ストローク 1 回 = 1 undo に coalesce)、`TileMapLayer` 描画流用。story + golden。
**罠**: ドラッグ中はプレビュー状態で描き drop で 1 change 記録 (NodeGraphView の MoveNodes と同じ)。タイル座標系と world 座標の変換は geometry 層に閉じる。

### GE-2 — インスペクタ + アセットパイプライン

選択エンティティ → `IComponentSchema` 経由で PropertyGrid に表示・編集 (`SetComponent` change = undo 可)。コンポーネント追加/削除メニュー。AssetBrowser を `IFileStorage.List` + プロジェクトフォルダに配線し、png → SpriteAtlas 定義エディタ (`ObjectDocument<T>` ベースで最小)、.tmj/wav はコピー取り込み。story + golden。
**罠**: PropertyGrid の編集を Transaction 経由にする (直接 mutate すると undo が壊れる)。ファイルドロップ API は無い → v1 は「取り込み」ダイアログ (パス入力) で可。

### GE-3 — Luxel.Player (データ駆動ランタイム)

`Luxel.Player` プロジェクト新設: `GameProject` を読み `SceneCompiler` で ECS world/TileMap/アトラス/カメラを構築、`LuxelHostBuilder` + `GameScene` で駆動。入力 = InputAction (project.luxel でバインド宣言)、衝突 = QueryAabb/Sweep、音 = res:// の wav。**csx ビヘイビア** (ADR-0018): Behaviour コンポーネント → ScriptSystem Attach。exe `Luxel.Player.App` (引数 = プロジェクトフォルダ)。検証 = 単体 (コンパイラ/往復) + fixture プロジェクトを Gallery story で実体化 (golden) + 実窓スモーク。
**罠**: e2e に音を出させない — `HeadlessAudio` 判定に乗せる ([[luxel-e2e-headless-audio]] 方式)。スクリプトは固定 dt の Update フックのみ (wall-clock 禁止) で決定性を守る。

### GE-4 — プレイインエディタ

Studio に ▶/⏸/ステップ/⏹: プレイ用 world を SceneCompiler で構築しエディタ内 viewport タブで固定 dt 駆動 (別インスタンス、停止で破棄)。Gizmos2D/DevStats オーバーレイトグル。プレイ中のインスペクタは読み取り表示 (書き込みは v2)。ADR-0017 起草。story play (▶ → 数ステップ → snap → ⏹ → 編集状態が不変) + golden。
**罠**: エディタ UI とゲーム viewport の入力ルーティング (viewport フォーカス時のみゲームへ)。プレイ中のシーン保存は禁止 (編集 doc は触っていないので本来安全だが、UI 上グレーアウト)。

### GE-5 — スクリプト編集統合

scripts/*.csx を TextEditorView + Roslyn プロバイダ (`ScriptHost` の診断 → DiagnosticsProvider 波線、既存 csx プレイグラウンドの流用) で開く DocumentProvider。保存 → プレイ中なら ScriptSystem Reload (ホットリロード)。Problems ペイン (診断一覧 → クリックでジャンプ)。story + golden。
**罠**: 補完はプレイグラウンド同等の範囲で十分 (LSP 級は [[luxel-scripting]] の別フェーズ)。Reload の失敗は旧維持 + Problems 表示で「エディタが落ちない」ことを play で実証。

### GE-6 — 出荷 (エディタから publish)

「出荷」コマンド: `dotnet publish Luxel.Player.App` (self-contained) + プロジェクトフォルダのコンテンツコピー → 出力フォルダ。capstone チェックリスト (shaders/フォント同梱、リポジトリ外起動 vk/dx exit 0) を自動検証するスクリプト/テストに落とす。**選択肢** (着手時に判断、ADR-0015 に追記): (a) dotnet SDK 前提で publish 実行 / (b) 事前ビルド済み player の同梱コピー。v1 は (a) で可 (開発機前提)。
**罠**: single-file は capstone の知見どおりフォント埋め込み経路に注意。出荷検証はリポジトリ外パスから。

### GE-7 — dogfood + Docs (完了で 27 MD 削除)

北極星シナリオを通しで実演: **ミニゲーム 1 本 (コイン集め 1 画面もの) をエディタ操作だけで作る** — 手順は play スクリプト化して golden に (新規プロジェクト → タイル描き → 配置 → csx → プレイ → 出荷までの主要 snap)。`Docs/Studio` ページ執筆 (使い方 + アーキテクチャ + 4 ADR へのリンク)。README/メモリ更新、27 MD 削除。

## 検証方針 (全 WS 共通)

- エディタ UI はすべて Gallery story + play + golden (Studio シェルはライブラリ `Luxel.Studio` に置き、Gallery から実体化できる構成に。exe `Luxel.Studio.App` は薄い殻)
- golden 決定性: `MemoryFileStorage` を story で使う (実 FS watch を golden に持ち込まない)。プレイは固定 dt。シーン JSON は決定的整形
- 純ロジック (SceneDoc/Change/Compiler/GameProject) は GPU 不要の単体テスト
- 実窓/実 FS/出荷はスモーク (exit 0) + 手動確認。音は HeadlessAudio

## リスク

- **スキーマとランタイムの二重定義** (エディタのコンポーネント定義 ⇔ ECS 実型) — `IComponentSchema` を単一の真実にし、Player 側の構築もスキーマ経由にすることで乖離を防ぐ。リフレクション自動生成は v1 では欲張らない (登録は手書きで十分小さい)
- **シーンエディタの操作性沼** (スナップ/ハンドル/多選択の細部) — v1 は「移動 + スナップ + 矩形選択」まで。回転/スケールハンドルは v2
- **csx の表現力不足が dogfood で露見** — StudioGlobals の API はミニゲームを先に紙上で書いてから決める (GE-3 冒頭)
- **エディタ golden の脆さ** (シェル全体 snap はレイアウト変化に敏感) — snap は各ペイン/各機能単位のストーリーに分け、シェル全体 snap は GE-7 の通しのみ

## ユーザーに確認

1. **v1 = 2D のみ**で良いか (3D は v2)
2. アプリ名 **「Luxel Studio」** (`src/Luxel.Studio` + `Luxel.Studio.App` / `Luxel.Player` + `Luxel.Player.App`) で良いか
3. Strudel を BGM 作成ペインとして v1 に含めるか (含めない想定。資産はあるが scope を絞る)

## スコープ外 (v1)

3D 編集 / アニメーションタイムライン / ビジュアルスクリプティング / 回転・スケールギズモ / プレハブ・ネストシーン / アセットのサムネイル生成 / エディタの多言語化 / プレイ中ライブ編集の書き戻し
