# ToDo — 未着手タスクの詳細仕様

Luxel エンジンの未完了・保留タスクを、AI が単独セッションで着手できる粒度で記述したもの。
各ファイルが 1 タスク。着手時はそのファイルだけ読めば背景・現状・実装方針・検証手順が分かる状態を目指している。

**進め方: ユーザーが「次へ」と言ったら [NEXT.md](NEXT.md) の実行キューに従う** (依存順・ステージ分割・完了の定義はそちらに集約)。

## タスク一覧 (推奨順)

| # | ファイル | タスク | 規模感 | リスク |
|---|---|---|---|---|
| 01 | [01-scripting-scriptsystem-hot-reload.md](01-scripting-scriptsystem-hot-reload.md) | Framework ScriptSystem + .csx hot reload | 中〜大 | 中 |
| 02 | [02-strudel-codeeditor.md](02-strudel-codeeditor.md) | Strudel REPL の CodeEditor 化 + Ctrl+Enter 評価 | 小〜中 | 低 |
| 03 | [03-physics-ccd.md](03-physics-ccd.md) | Physics: CCD (連続衝突検出) の ECS 公開 | 小 | 低 |
| 04 | [04-physics-contact-events.md](04-physics-contact-events.md) | Physics: 接触イベント + トリガーボリューム | 中 | 中 |
| 05 | [05-physics-mesh-colliders.md](05-physics-mesh-colliders.md) | Physics: メッシュ/凸包コライダー | 中 | 中 |
| 06 | [06-codeeditor-completion-polish.md](06-codeeditor-completion-polish.md) | CodeEditor: 補完ポップアップの磨き込み (P2.5 残) | 小〜中 | 低 |
| 07 | [07-codeeditor-multicursor.md](07-codeeditor-multicursor.md) | CodeEditor: マルチカーソル (E3.5) | 大 | **高** |
| 08 | [08-strudel-music-features.md](08-strudel-music-features.md) | Strudel: 音楽機能拡張 (scale/chord・filter/delay・記法・MIDI) | 中 (分割可) | 低 |
| 09 | [09-gltf-skin-morph.md](09-gltf-skin-morph.md) | glTF skin/morph アニメーション | 大 | 中 |
| 11 | [11-scripting-debug-tools.md](11-scripting-debug-tools.md) | Scripting: DevTools Console タブ + リプレイ + 外部デバッガ | 中 (分割可) | 中 |
| 19 | [19-standalone-game-shipping.md](19-standalone-game-shipping.md) | capstone ①: 2D プラットフォーマー「Luxel Cavern」+ publish 検証 | 大 | 中 |
| 20 | [20-game2-3d-shooting-range.md](20-game2-3d-shooting-range.md) | capstone ②: 3D 射的「Luxel Range」(03/04/05/09 の検証場) | 中〜大 | 中 |
| 21 | [21-devtools-game-scale.md](21-devtools-game-scale.md) | DevTools のゲーム規模対応 (ECS スケール/gizmo/ゲーム統計/timescale) | 中 | 低 |

## ゲームエンジン完成に向けた文脈 (2026-07-06 ギャップ分析)

14〜19 は「ゲームを 1 本作って出荷する」ためのギャップ分析 (Tier 1 = 必須) から起こしたタスク。既存タスクでは 04 (接触イベント)・09 (skin/morph)・10 (Audio ストリーミング) が同じ Tier 1〜2 に属する。**完成の定義は capstone ゲーム 2 本** (2026-07-06 決定、当初の Breakout 案を置換): [19](19-standalone-game-shipping.md) = 2D プラットフォーマー「Luxel Cavern」(13/14/15/16-2D/17/18 + 01/10 の検証場 + publish 基盤) → [20](20-game2-3d-shooting-range.md) = 3D 射的「Luxel Range」(03/04/05/09 + 16-.ThreeD/17-3D + glTF/3D シェーダ publish の検証場)。この 2 本でゲーム検証可能な全タスクをカバーする (02/06/07/08/11/12 はエディタ/ツール系のため各自のデモ/テストで担保)。19 は最小構成 (タイトル画面のみ) で publish を早めに 1 回通し、見つかった穴をタスク化するのが効率的。[21](21-devtools-game-scale.md) (DevTools のゲーム規模対応) は capstone の開発を支える支援タスク — A (ECS スケール)/C (ゲーム統計)/E (スタンドアロン統合) は 19 のゲーム組み上げ前、B (物理 gizmo) は 20 着手前までに済ませると効率が良い。Tier 2 (未タスク化、必要になったら起こす): 音のバス/グループ音量とフェード、シーン間の型安全なパラメータ受け渡し、ゲームパッド振動、実行時キーリバインド UI、固定シード Random / timeScale の DI サービス、normal map / IBL、GPU タイムスタンプ プロファイリング、i18n 文字列テーブル。Tier 3 (ゲームの種類が決まってから): ネットワーク、デファード/SSAO/TAA、アセット暗号化、Windows 以外のプラットフォーム。

## 全タスク共通の規約・検証手順

### ビルドとテスト

```powershell
dotnet build
dotnet test                                              # ユニット + E2E play アダプタ (GPU なければ E2E は Skip)
dotnet run --project src/Luxel.Gallery -- vk             # Gallery 実窓 (dx も可)
dotnet run --project src/Luxel.Gallery -- vk e2e         # E2E play 実行 + golden 比較
dotnet run --project src/Luxel.Gallery -- vk e2e --update "部分一致フィルタ"   # golden 更新
```

- シェーダビルドには `tools/slang/` (standalone Slang + `tools/slang/bin/` に dxcompiler.dll/dxil.dll) が必要。リポジトリ非含。git worktree で作業する場合は本体の tools/ へ junction を張る。
- 検証 GPU は RTX 4080 SUPER (Vulkan 一次、D3D12 二次)。

### golden (スナップショット) 運用

- **golden は play だけが生む**: ストーリーに `ctx.Play(async d => {...})` を書く。`d.Snap()`/`d.Click`/`d.Type`/`d.Key`/`d.Step`/`d.Expect`。全操作固定 dt・wall-clock 禁止 (`Task.Delay` 不可)。
- golden 名は `{Story}[.{Play}][.{Snap名|連番}].{backend}.png`。snap は 800×480 固定。
- `--update` は全 golden を再エンコードする。意図した差分だけ残す手順: update 後に `git diff --name-only -- goldens` を見て、意図分**以外**を `git checkout --` で戻す (過去の未コミット意図分も巻き戻さないよう除外リストに含めること)。

### UI コンポーネント規約 ([UiComponent])

- instance ctor は書かない (ジェネレーターが生成、自前 ctor は NGUI002)。パラメータは `[UiParam] private readonly Bindable<T> _x = 既定値;` をクラス先頭にファクトリ引数順で宣言。読みは `X.Get()`。
- live データは `Bindable<Signal<T>>`。イベントは `[UiEvent]` public フィールド。
- 新しい [UiComponent] を足すと Reference/Overview の自動生成 API ページの golden が変わる → e2e --update 対象に含める。

### ドキュメント規約

- 計画 MD をリポジトリに置かない方針 (docs/ は削除済み)。**この ToDo/ フォルダはユーザー指示による例外**。実装が終わったタスクファイルは削除し、仕様は Gallery の Docs ストーリーへ現在形で書く。
- Docs ページは `src/Luxel.Gallery/Stories/Docs/Docs*.cs`。段落は 1 ソース行 = 1 表示行 (手動改行しない)。hole は `$$` 既定。
- 機能追加時は該当 Docs ページに節を足し、デモストーリー (play + golden 付き) を添える。

### 決定性

テスト・golden はすべて決定的であること: wall-clock/Task.Delay/未固定シード乱数を使わない。時間は固定 dt、乱数は固定シード xorshift (StrudelKit 方式)。file IO を挟む機能はテスト用に抽象を切る (01 参照)。
