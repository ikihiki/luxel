# NEXT — 「次へ」で進む実装キュー

ユーザーが**「次へ」**とだけ言ったら、AI はこのファイルの手順に従って次のタスクを 1 つ進める。

## 「次へ」と言われたときの手順

1. このファイルの**実行キュー**から、未チェック (`[ ]`) の最初のエントリを選ぶ。`(着手中: ...)` メモがあればそれを再開する。
2. エントリが指すタスク MD を**全文読む** (背景・実装方針・罠・検証がそこに揃っている)。[README.md](README.md) の共通規約 (ビルド/テスト/golden 運用/UiComponent 規約/決定性) も従うこと。
3. 着手時: エントリ末尾に `(着手中: YYYY-MM-DD)` を書き足してから作業を始める (中断されても次セッションが再開できる)。
4. 実装 → 下の**完了の定義**を全部満たす → キューのチェックを `[x]` にし、`(着手中)` メモを消す。
5. **タスクの全ステージが終わったら**: タスク MD を削除し、README.md の一覧表から行を削除し、仕様は Gallery の Docs ストーリーへ現在形で書く (既存運用)。
6. 作業中に見つかった穴・新タスクは ToDo/ に新しい MD として追加し、このキューの適切な位置に 1 行足す。
7. 1 回の「次へ」で進めるのは**キュー 1 エントリまで**。早く終わっても次のエントリへ勝手に進まない (ユーザーがまた「次へ」と言う)。

## 完了の定義 (全エントリ共通)

- [ ] `dotnet build` / `dotnet test` が通る (新規ロジックには GPU 不要の単体テスト)
- [ ] e2e: `dotnet run --project src/Luxel.Gallery -- vk e2e` が通る。golden 差分は意図分のみ (`--update` 後に `git diff --name-only -- goldens` で意図外を戻す — README 参照)
- [ ] タスク MD 記載のデモストーリー/Docs 追記を実施 (該当があれば)
- [ ] `dotnet format` 相当のスタイルで綺麗 (リポジトリは dotnet/docs の .editorconfig)
- [ ] コミット済み (conventional commits 風: `feat(particles): ...` 等、日本語本文可)

**ユーザーに聞くのは**: タスク MD に「ユーザーに確認」と明記がある箇所、破壊的な選択、スコープの増減だけ。それ以外は MD の記述を正として自走する。

## 実行キュー (上から順)

> **完了済み (2026-07-10 整理)**: M1〜M7 / M9 / M10 の Q01〜Q30b・Q32〜Q44 は全完了につきキューから削除した。capstone 2 本 (`samples/LuxelCavern`・`samples/LuxelRange`)、テキストエディタ新スタック (`Luxel.Document`、ADR-0006/0007)、ノードエディタ (`Luxel.NodeGraph`、ADR-0009)、Workbench (`Luxel.Workbench`、ADR-0010〜0014) まで達成済み。仕様は Gallery の Docs/ADR ストーリー、経緯は git 履歴 (この整理前の NEXT.md に完了ログあり) を参照。

### M11 — ゲームエディタ「Luxel Studio」2D フェーズ (ADR-0015〜0018、[27](27-game-editor.md) の大プログラム。**27 MD は M12 [3D フェーズ] 完了まで残す**)

> 依存順: GE-0 → GE-1 (S1→S2) → GE-2 → GE-3 → GE-4 → GE-5 → GE-6 → GE-7。**設計は 2D/3D 両対応 (27 MD の「設計原則」節が全エントリの縛り)、実装は M11=2D・M12=3D** (GE-7 完了後に M12 = GE-8〜10 を起票)。詳細・罠・検証・ユーザー確認事項は [27](27-game-editor.md) に集約。着手前に MD の「ユーザーに確認」残 2 点を確認する。

- [x] **Q45**: 27 **GE-0** (2026-07-10 完了) — プロジェクト/シーンモデル。新プロジェクト `Luxel.SceneEdit` (依存ゼロ): `SceneValue` (**形ベース**: Bool/Number/Text/Vec2/3/4/Raw — 型付きの意味 [Int/Enum/AssetRef/Quat/Color] はスキーマの解釈、未知保全が構造的に成立。float は最短表現正規化)、`SceneComponent` (フィールド名前順ソート = 決定的 JSON、"type" 予約)/`SceneEntity` (安定 id、型ごと 1 個)/`TileLayer` (行優先 cells)/`SceneDoc` (space ヘッダ + id 索引)、`IComponentSchema`/`SchemaRegistry` (対応 space で出し分け) + `SceneSchemas.Transform2D/3D` 両定義、`SceneJson` (決定的整形: キー順固定/LF/インデント 2/非 ASCII 素通し、serialize∘deserialize=恒等をテストで担保、タイルは行 CSV)、`GameProject`/`GameProjectJson`/`ResPath` (res:// 検証: 脱出/絶対/バックスラッシュ拒否)。ADR-0015 起草 (Accepted、Order 86) + ADR/Overview 一覧追記。単体 `SceneEditCoreTests` 14 本、全 1041 passed、vk e2e 116/116 diff 0 (golden 変更なし — ADR ページは 0002〜0014 同様 play 無し)。**副次発見**: dotnet test の E2E アダプタ乖離 → [28](28-e2e-adapter-drift.md) に起票
- [x] **Q46**: 27 **GE-1 S1** (2026-07-11 完了) — シーンエディタ変更モデル + ビュー骨格。**core** (`Luxel.SceneEdit`、NodeGraph S1 鏡写し): `SceneChange` (AddEntity[挿入 Index 付き — **エンティティ順=描画順なので削除の undo は元位置へ復活**]/RemoveEntity/RenameEntity/SetComponent/RemoveComponent/**SetField** [移動もインスペクタ編集もこれ、形ベース SceneValue で 2D/3D 共通]) + `SceneChangeSet`/`SceneSelection`/`SceneEditState`/`SceneTransaction`/`SceneHistory` (1 tx=1 undo、coalesce)/`SceneCommands` (空間非依存のみ: Add/Delete/Duplicate[offset 注入]/Select 系)。**MD 差**: viewport はコア状態に持たせず**空間アダプタ所有** (2D=pan/zoom、3D=軌道で型が違うため)。**view** (`Luxel.Controls`): `ISceneSpaceAdapter` (変換/ヒット/カメラ/描画/BuildMove/複製オフセットを閉じ込め、シェルは view-local px と id のみ = 原則 3) + `SceneSpace2DAdapter` (Affine2D pan/zoom、transform2d.pos 中心のボックス表示、**軸分解ハンドル X=赤/Y=緑** [画面空間固定サイズ]、グリッド/スナップ 32) + `SceneEditorView` [UiComponent] (クリック/Ctrl+Click トグル/marquee/本体ドラッグ=Free 移動/ハンドル=軸拘束/中ボタン pan/ホイールズーム/Ctrl+Z・Y・A・D・Delete・Esc、ドラッグはプレビュー→drop で 1 undo)。**罠**: PointerEvent の Delta はドラッグ開始からの**累計** (加算すると二重計上 — pan は差分化が要る)。ADR-0016 起草 (Accepted、Order 87)。単体 +14 (Change 往復 10 + 2D アダプタ 4、全 1055)、story `Controls/SceneEditorView/Basic` (play 4: basic/handle/marquee/keys)、**vk/dx golden 各 6 枚** + Reference/Overview.table 更新 ([UiComponent] 追加分)、**full vk e2e 120/120 diff 0 (2 連続)**。次は Q47 (S2 タイル描き込み)
- [x] **Q47**: 27 **GE-1 S2** (2026-07-11 完了) — タイル描き込み。**core**: `TilePaint` + `TileLayer.WithCells` (後勝ち・範囲検証) + `SceneDoc.ReplaceLayer` + **`PaintTiles` change** (1 ストローク = 1 change = 1 undo。座標重複は Apply が検証 — 逆適用の復元順が不定になるため。逆 = 描き込み前の値)。**view**: `SceneTool` (Select/Brush/Rect/Eraser/Picker) + `ISceneTileAdapter` (CellAt [clamp オプション]/CellLocalCenter — **タイルは 2D 機能なので基底アダプタに載せず `is` 判定でツール有効化** = 原則 3 の型で解決)。2D アダプタがタイル描画 (非ゼロセル矩形 + レイヤ境界枠) を追加。シェル: ブラシ = 前回セルから**直線補間** (速いドラッグ/2 点 Drag でも途切れない)、矩形 = 対角範囲塗り潰し、消しゴム = タイル 0、スポイト = セル値→ActiveTile。ドラッグ中は stroke dict → PaintTiles プレビュー、drop で**値の変わるセルだけ** 1 PaintTiles 記録 (全同値なら記録なし)。**MD 差**: タイル表示は**エディタ用プレースホルダ色** (決定的パレット、全セル毎回描き直し) — TileMapLayer/実アトラス流用は SpriteAtlas 実テクスチャ前提のため **GE-2/GE-3 のアセット配線後に差し替え** (チャンク差分描画もその時)。パレットペインは story 内の Button 合成 (専用コントロールは Studio シェル = GE-2 で判断)。単体 +2 (PaintTiles 往復/検証 + CellAt、全 1057)、story `Controls/SceneEditorView/Tiles` (play 3: brush/rect/pick-erase)、**vk/dx golden 各 4 枚**、**full vk e2e 123/123 diff 0 (2 連続)**。次は Q48 (GE-2 インスペクタ + アセット)
- [x] **Q48**: 27 **GE-2** (2026-07-11 完了) — インスペクタ + アセットパイプライン。**`SceneInspector`** [UiComponent] (Luxel.Controls) = **IComponentSchema 駆動のスキーマインスペクタ** — **MD 差**: PropertyGrid はリフレクションベースで SceneComponent (スキーマ駆動バッグ) に合わないため、PropertyGrid の行/エディタ流儀 (Skip1 commit 方式) を写した別コントロール。全 SceneFieldType のエディタ (Bool=Check/Enum=Select/Color=ColorPicker [uint⇄Vec4]/Vec2・3=軸別/Quat=**オイラー度表示・保存は Quat のまま** [`SceneRotation` core ヘルパ + 往復テスト]/Int・Float・String・AssetRef=TextField)。編集は `SceneEditorView.ApplyEdit` の Transaction 経由 = undo 可・スキーマ外コンポーネントは読み取り表示で保全・コンポーネント追加は space で出し分け (Select+追加/× 削除、AddComponent/RemoveComponent 公開)。view 拡張: `Revision` Signal (確定状態変化で bump — **Peek ベース必須**: `Value++` は read-then-write でエフェクト実行中に呼ぶと購読→再入する罠を実地で踏んだ) + `ApplyEdit`。**アセット**: `AtlasDef` + `AtlasDefJson` (決定的、均等グリッド切りの最小形: image/tileWidth/tileHeight — 名前付きスプライト矩形は必要時) を core に追加、story で AssetBrowser (MemoryFileStorage) → *.atlas.json → PropertyGrid 直結編集 → 変更のたび決定的 JSON 保存 + パス入力取り込み (ドロップ API なし)。**MD 差 2**: atlas エディタは ObjectDocument\<T\> でなく PropertyGrid 直結 (ObjectDocument の直列化は非決定的 System.Text.Json のため AtlasDefJson を正とする)。**知見**: コントロール story の snap は幅 480×Height — 写したいペインは左に置く (Inspector/Assets の 2 story に分割した理由)。単体 +2 (Rotation 往復 + AtlasDef、全 1059)、story `Inspector` (play 2: inspect/components) + `Assets` (play 2: atlas/import)、**vk/dx golden 各 7 枚** + Reference/Overview.table 更新、**full vk e2e 127/127 diff 0 (2 連続)**。次は Q49 (GE-3 Luxel.Player)
- [x] **Q49**: 27 **GE-3 S1** (2026-07-11 完了) — `Luxel.Player` ライブラリ新設 (SceneEdit/Resources/TwoD/Typography 参照)。**読込は `IVirtualFileSystem`** (Workbench の IFileStorage でなく — ランタイムは読み取り専用、書くのはエディタだけ、という層の分離)。`PlayerLoader` (project.luxel → res:// 開始シーン → `PlayerGame`)、`SceneCompiler` (コア = space 分岐のみ + **2D バックエンド**、3D は NotSupported を明示 = M12/GE-9)、`Player2DWorld`/`PlayerEntity` (**transform2d は第一級フィールドに展開しデータ袋から除く** = 二重の真実を避ける、他コンポーネントは形ベース SceneValue のまま Field/SetField [ランタイム状態のみ・保存しない = ADR-0017 の使い捨て契約]、固定 dt Update + Scene2D Render [背景/タイル/tint 反映の箱+名前])。`TilePalette` を Luxel.SceneEdit に切り出しエディタ (2D アダプタ) とランタイムで**同じ見た目**を共有。story `Apps/Player/Basic` = fixture プロジェクト (MemoryFileSystem にエディタ形式 JSON) → LoadStart → `Canvas2D(animate:)` ホスト (Tick 累積 = 決定的)、play run (30 固定 step で移動 + タイル素通し assert)。単体 `PlayerCoreTests` 4 本 (全 1063)、vk/dx golden 各 2 枚、**full vk e2e 128/128 diff 0 (2 連続)**。次は Q49b (S2 csx ビヘイビア)
- [ ] **Q49b**: 27 **GE-3 S2** — csx ビヘイビア (ADR-0018 起草): behaviour スキーマ + ScriptHost 配線 (コンパイル失敗で旧維持 + 診断)、globals = 空間非依存共通部。story play (csx がエンティティを動かす golden)
- [ ] **Q49c**: 27 **GE-3 S3** — `Luxel.Player.App` exe (引数 = プロジェクトフォルダ、PhysicalFileStorage + 実窓 + キー入力) + 実窓スモーク (exit 0)。音は res:// wav (HeadlessAudio 判定に乗せる)
- [ ] **Q50**: 27 **GE-4** — プレイインエディタ (▶/⏸/ステップ/⏹、プレイ world 別インスタンス・停止で破棄、gizmo/DevStats オーバーレイ)。ADR-0017 起草。story play + golden
- [ ] **Q51**: 27 **GE-5** — スクリプト編集統合 (csx DocumentProvider = TextEditorView + ScriptHost 診断、保存→ホットリロード、Problems ペイン)。story + golden
- [ ] **Q52**: 27 **GE-6** — 出荷コマンド (dotnet publish Player + コンテンツコピー → リポジトリ外起動 vk/dx exit 0 の自動検証)。capstone チェックリスト踏襲
- [ ] **Q53**: 27 **GE-7** — dogfood: ミニゲーム 1 本をエディタ操作だけで作って出荷 (通し play + golden) + `Docs/Studio` 執筆 → **M11 クローズ。M12 (3D: GE-8〜10) を 27 MD の見取り図からキューに起票** (27 MD は M12 完了まで残す)

### メンテナンス (発見順。M11 の合間に片付けて良い)

- [ ] **Q54**: [28 dotnet test E2E アダプタ乖離](28-e2e-adapter-drift.md) — `dotnet test` (E2ePlayTests) だけ Reference/Overview.table (決定的)・Docs/Strudel (走行順依存) の golden が落ちる (Gallery ランナーは全緑)。描画環境の一致 + stale golden `Demos_Strudel_Repl.playing.vk.png` の後始末。**golden を安易に --update しない** (MD の罠参照)

### M8 — 排他モード IME (必要になったら。ADR-0008 は Proposed)

- [ ] **Q31**: [24 カスタム IME 候補ウインドウ](24-custom-ime-candidates.md) — TSF `ITfUIElementSink` で OS 候補を抑制 + `ITfCandidateListUIElement` 読み取り + Popup 描画 (排他フルスクリーン対応)。**排他モードが必要になった時点で着手**、実機手動検証 (golden 不可)。ADR-0008

## 運用メモ

- 分割ステージを持つタスクの MD は**全ステージ完了まで残す** (消すタイミングに注意)。
- git worktree で作業する場合は tools/ junction を忘れない (README/メモリ参照)。
- 検証 GPU が無い環境では e2e は Skip される — その場合は「単体テスト + ビルド」までで完了とし、キューに `(e2e 未実施)` を残して次のセッションで実機確認する。
