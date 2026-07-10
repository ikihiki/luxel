# 27 — ゲームエディタ「Luxel Studio」: エディタだけでゲームを一通り作る

## 概要

Workbench (ADR-0010〜0014) の上に、**ゲームを最初から最後まで — プロジェクト作成 → アセット取り込み → レベル/シーン編集 → 挙動スクリプト → プレイテスト → 出荷 — を C# ソリューションを書かずに完走できる**エディタを作る。GE-0〜GE-7 の 8 ワークストリーム。完成の定義は capstone 方式: **ミニゲーム 1 本をエディタ操作だけで作って publish する** (GE-7 dogfood)。

**解釈 (ユーザーに確認済みの前提として進める。違ったら指摘を)**: 「一通り作れる」= Unity 的な完結環境 (ゲーム = プロジェクトデータ + csx スクリプト、エンジン改造なしで 1 本出せる)。既存 capstone のような「C# コア + データ」方式のゲームにも部品 (シーンエディタ/インスペクタ) が使える設計にするが、北極星は前者。

**2D/3D の扱い (2026-07-10 ユーザー決定)**: 実装フェーズは分けて良い (M11 = 2D、M12 = 3D) が、**設計は最初から両対応** — モデル/スキーマ/エディタ/コンパイラ/プレイヤーのどの層にも「2D 前提」を焼き込まない。具体規則は下の「2D/3D 両対応の設計原則」節。

## ゴール / 非ゴール

- **M11 (このキュー) で実装するのは 2D** (LuxelCavern 相当の 2D ゲームが作れる)。**3D は M12** として GE-7 完了後に起票 (見取り図は本 MD 末尾) — 資産: OrbitCamera / PhysicsGizmos / glTF / scene_pbr 系は揃っている。
- **非ゴール (M11)**: 3D 編集の実装 (設計対応のみ)、アニメーションタイムライン UI、ビジュアルスクリプティング (NodeGraph 資産はあるが挙動は csx で)、アセットストア的な配布、マルチユーザー編集、Windows 以外。
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

## 2D/3D 両対応の設計原則 (全 WS 共通の縛り。M11 は 2D 実装だが、この形を崩さない)

1. **SceneDoc は空間非依存** — エンティティ + コンポーネントデータの袋。シーンヘッダに `space: "2d" | "3d"` を持ち、viewport/コンパイラのパイプライン選択だけに使う。座標はコンポーネント側の関心事: `Transform2D` (Vector2 + 回転 rad + スケール) と `Transform3D` (Vector3 + クォータニオン + スケール) は**別スキーマ**として最初から両方定義する (Unity 式の「常に 3D Transform」は採らない — Luxel のランタイムが 2D/3D 別スタックのため。混在や自動変換はしない)。
2. **IComponentSchema のフィールド型は初日から全部切る** — Bool/Int/Float/String/Enum/**Vec2/Vec3/Quat (エディタ表示はオイラー角)**/Color/AssetRef。2D だけなら Vec3/Quat は不要だが、後から足すとスキーマ + JSON + PropertyGrid の 3 箇所に同時に手が入るため先に揃える。各スキーマは**対応 space** (2d/3d/両方) を宣言し、インスペクタの「コンポーネント追加」やパレットが自動で出し分ける。
3. **SceneEditorView = 共有シェル + `ISceneSpaceAdapter`** — 選択モデル/Transaction 配線/ツール切替/オーバーレイ/キーバインドは空間非依存の共有シェル。**スクリーン↔ワールド変換・ヒットテスト・カメラ操作・エンティティ/ハンドル描画はすべてアダプタ経由** (シェルに「ワールド = 平面」の前提を書かない。書きたくなったらアダプタへ)。M11 は 2D アダプタ (Affine2D pan/zoom + TileMapLayer + 矩形ヒット) のみ実装、M12 で 3D アダプタ (OrbitCamera + レイピック + ワイヤ描画) を追加。
4. **移動ギズモは v1 から軸分解の形** — ハンドル = 軸 (X/Y、3D で Z が増える) + 平面ドラッグ。2D を「2 軸の特殊形」として作れば 3D で作り直しにならない。回転/スケールハンドルは両フェーズともスコープ外 (数値はインスペクタで)。
5. **SceneCompiler もコア + space 別バックエンド** — エンティティ/コンポーネント→ECS のコアは空間非依存、描画パス (Rasterizer2D/TileMapLayer vs RenderGraph + scene_pbr 系)・カメラ (CameraRig2D vs OrbitCamera 等)・衝突 (QueryAabb/Sweep vs Luxel.Physics) の構築だけ space 別モジュール。
6. **プロジェクトは 2D/3D シーン混在可** — space はシーン単位 (例: 3D ゲームの 2D タイトル画面)。Player はシーンの space を見て描画パスを選ぶ。csx ビヘイビアの globals も空間非依存の共通部 + space 別の拡張に分ける。

## アーキテクチャ決定 (着手時に ADR を起こす。次番号 0015〜)

- **ADR-0015 (GE-0 で起草): プロジェクト/シーン形式とデータ駆動ランタイム。** ゲームプロジェクト = フォルダ (`project.luxel` [JSON: 名前/開始シーン/ウインドウ設定] + `scenes/*.scene.json` + `assets/**` + `scripts/*.csx` + `atlas/*.json` 等)。シーンはエディタ専用モデル `SceneDoc` (安定 id のエンティティ列 + コンポーネントデータ + タイルレイヤ参照 + **space ヘッダ**) を JSON 往復し、ランタイムは `SceneCompiler` が ECS world + TileMap + リソースへ**一方向に構築** (Friflo `WorldSave` を編集形式に直接使わない — エディタは安定 id/未知コンポーネント保全/差分 undo が要るため。WorldSave はゲーム内セーブ用のまま)。アセット参照は `res://` 相対パスで統一 (png/wav/tmj/**glb** を同列に)。両対応原則 1・2・5・6 をここで固定する。
- **ADR-0016 (GE-1 で起草): シーンエディタ = 第 3 の Transaction スタック + 空間アダプタ。** `Luxel.SceneEdit` (依存最小・canvas 非依存): `SceneDoc` (不変) + `SceneChange` (AddEntity/RemoveEntity/MoveEntity/SetComponent/PaintTiles…、各 Apply/Invert。移動は Vec2/Vec3 を持てる中立表現) + `SceneSelection` + `SceneTransaction`/`History` — NodeGraph S1 の設計をほぼ写経。ビューは `SceneEditorView` [UiComponent] = 共有シェル + `ISceneSpaceAdapter` (原則 3・4)。2D アダプタは pan/zoom = Affine2D コンテナ、TileMapLayer/スプライト描画流用、PointerEvent の Button/Modifiers (ADR-0011) を初めて本格消費。
- **ADR-0017 (GE-4 で起草): プレイインエディタ実行モデル。** 編集 world とプレイ world は**別インスタンス** (SceneCompiler で都度構築 → 停止で破棄 = 状態リークなし、Unity の「プレイ中の変更は消える」と同じ契約)。エディタ内 viewport はストーリー内ゲーム描画の既存手法 (Apps/Game 系) を踏襲、固定 dt 駆動でエディタ golden も決定的に。ポーズ/ステップ/timescale は DevTools の既存機構を接続。
- **ADR-0018 (GE-3 で起草): csx ビヘイビアモデル。** エンティティのコンポーネントに `Behaviour { Script = "scripts/enemy.csx" }` を持たせ、`Luxel.Player` が `ScriptSystem` で Attach。スクリプトの globals はプレイヤー提供の `StudioGlobals` (world/entity/入力/時間/音)。コンパイル失敗は旧維持 + 診断をエディタの Problems へ (既存 ScriptSystem の契約そのまま)。

## ワークストリーム (依存順。各 WS は「次へ」1〜2 回想定、大きいものはステージ分割)

### GE-0 — プロジェクト/シーンモデル (純ロジック)

`Luxel.SceneEdit` プロジェクト新設: `GameProject` (project.luxel 往復 + 相対パス解決)、`SceneDoc` (space ヘッダ + エンティティ/コンポーネント/タイルレイヤ、JSON 往復 + **未知コンポーネントの素通し保全**)、コンポーネントスキーマ登録 (`IComponentSchema`: 型名 → フィールド定義 [全型: 原則 2] → 既定値 → 対応 space。PropertyGrid/インスペクタとランタイム構築の共通語彙)。`Transform2D`/`Transform3D` 両スキーマをここで定義 (3D はスキーマとテストのみ — エディタ/ランタイム実装は M12)。ADR-0015 起草。検証 = 単体テスト (往復/未知保全/パス解決/Vec3・Quat 往復)。golden 影響なし。
**罠**: シーン JSON の決定的整形 (キー順固定・改行固定) — golden とテキスト diff の要。float の往復は R フォーマット固定。Quat のオイラー表示変換はエディタ側の表示問題であり保存形式は Quat (往復劣化を避ける)。

### GE-1 — シーンエディタ (2 ステージ)

- **S1 変更モデル + ビュー骨格**: `SceneChange`/`History` (NodeGraph S1 写経、移動は空間中立表現) + `SceneEditorView` = **共有シェル + `ISceneSpaceAdapter`** (原則 3) の 2D アダプタ実装 (グリッド/pan/zoom、エンティティ = スプライトかプレースホルダ矩形で表示、クリック選択/矩形選択/ドラッグ移動 [スナップ、軸分解ハンドル = 原則 4]、Delete/複製、undo/redo)。story + golden。ADR-0016 起草。
- **S2 タイル描き込み**: TileSet パレットペイン + ブラシ/矩形/消しゴム/スポイト、`PaintTiles` change (ストローク 1 回 = 1 undo に coalesce)、`TileMapLayer` 描画流用。story + golden。
**罠**: ドラッグ中はプレビュー状態で描き drop で 1 change 記録 (NodeGraphView の MoveNodes と同じ)。タイル座標系と world 座標の変換は geometry 層に閉じる。**シェルにスクリーン↔ワールドの直計算を書かない** (必ずアダプタ経由 — 3D アダプタ追加時の唯一の保険)。

### GE-2 — インスペクタ + アセットパイプライン

選択エンティティ → `IComponentSchema` 経由で PropertyGrid に表示・編集 (`SetComponent` change = undo 可)。コンポーネント追加/削除メニュー。AssetBrowser を `IFileStorage.List` + プロジェクトフォルダに配線し、png → SpriteAtlas 定義エディタ (`ObjectDocument<T>` ベースで最小)、.tmj/wav はコピー取り込み。story + golden。
**罠**: PropertyGrid の編集を Transaction 経由にする (直接 mutate すると undo が壊れる)。ファイルドロップ API は無い → v1 は「取り込み」ダイアログ (パス入力) で可。

### GE-3 — Luxel.Player (データ駆動ランタイム)

`Luxel.Player` プロジェクト新設: `GameProject` を読み `SceneCompiler` で ECS world/TileMap/アトラス/カメラを構築、`LuxelHostBuilder` + `GameScene` で駆動。**コンパイラはコア + space 別バックエンドに分割 (原則 5)、M11 は 2D バックエンドのみ実装** (3D は space="3d" で NotSupported を明示、M12 で追加)。入力 = InputAction (project.luxel でバインド宣言)、衝突 = QueryAabb/Sweep、音 = res:// の wav。**csx ビヘイビア** (ADR-0018): Behaviour コンポーネント → ScriptSystem Attach、globals は空間非依存の共通部 + space 別拡張 (原則 6)。exe `Luxel.Player.App` (引数 = プロジェクトフォルダ)。検証 = 単体 (コンパイラ/往復) + fixture プロジェクトを Gallery story で実体化 (golden) + 実窓スモーク。
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

## M12 (3D フェーズ) の見取り図 (GE-7 完了後に起票。M11 の設計原則が効いていれば「追加」だけで済むはず)

- **GE-8 — 3D 空間アダプタ**: `ISceneSpaceAdapter` の 3D 実装 (OrbitCamera 操作 + レイピックのヒットテスト + エンティティのワイヤ/AABB 表示 [PhysicsGizmos 流] + 3 軸移動ハンドル + グリッド平面)。シーンエディタ共有シェルは無改修が目標 (改修が要る = 原則 3 違反の検出)。
- **GE-9 — 3D コンパイラバックエンド + Player 拡張**: SceneCompiler の 3D バックエンド (scene_pbr 系描画 + glTF 参照 [AssetRef で glb] + Luxel.Physics 衝突 + OrbitCamera/追従カメラ)。csx globals の 3D 拡張。
- **GE-10 — dogfood ミニ 3D + Docs 追記**: Range 風の 1 シーンものをエディタ操作だけで (2D タイトル画面 + 3D プレイ画面の**混在プロジェクト**で原則 6 を実証) → 出荷。
- タイムライン/スキンアニメの編集 UI・回転/スケールハンドルは M12 でもスコープ外 (必要になったら個別タスク)。

## ユーザーに確認

1. ~~v1 = 2D のみで良いか~~ → **決定 (2026-07-10)**: フェーズは 2D (M11) → 3D (M12) で分割、ただし**設計は最初から両対応** (上の設計原則節)。
2. アプリ名 **「Luxel Studio」** (`src/Luxel.Studio` + `Luxel.Studio.App` / `Luxel.Player` + `Luxel.Player.App`) で良いか
3. Strudel を BGM 作成ペインとして v1 に含めるか (含めない想定。資産はあるが scope を絞る)

## スコープ外 (M11。3D 実装は M12 = 上の見取り図)

アニメーションタイムライン / ビジュアルスクリプティング / 回転・スケールギズモ / プレハブ・ネストシーン / アセットのサムネイル生成 / エディタの多言語化 / プレイ中ライブ編集の書き戻し
