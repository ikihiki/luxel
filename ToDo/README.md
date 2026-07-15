# ToDo — 未着手タスクの詳細仕様

Luxel エンジンの未完了・保留タスクを、AI が単独セッションで着手できる粒度で記述したもの。
各ファイルが 1 タスク。着手時はそのファイルだけ読めば背景・現状・実装方針・検証手順が分かる状態を目指している。

**進め方: ユーザーが「次へ」と言ったら [NEXT.md](NEXT.md) の実行キューに従う** (依存順・ステージ分割・完了の定義はそちらに集約)。

## タスク一覧 (推奨順)

| # | ファイル | タスク | 規模感 | リスク |
|---|---|---|---|---|
| 24 | [24-custom-ime-candidates.md](24-custom-ime-candidates.md) | カスタム IME 候補ウインドウ (TSF ITfUIElementSink で OS 抑制 + 自前 Popup 描画、排他モード対応。ADR-0008 Proposed) | 中 | **高** (実 IME 依存) |

完了したタスクの MD は削除済み (規約通り)。仕様は Gallery の Docs/ADR ストーリー、経緯は git 履歴を参照。直近では 13 (e2e 日本語フォント)・22 (エディタ新スタック)・23 (浮遊 UI placement)・25 (ノードエディタ)・26 (Workbench) が完了 (2026-07-10 整理)。

## 将来タスクの候補 (2026-07-06 ギャップ分析の残り)

ゲームエンジン完成の定義だった capstone 2 本 (`samples/LuxelCavern`・`samples/LuxelRange`) は 2026-07-07 に完成済み。ギャップ分析の Tier 1 タスクはすべて消化した。残る候補 (未タスク化、必要になったら MD を起こす):

- **Tier 2**: 音のバス/グループ音量とフェード、シーン間の型安全なパラメータ受け渡し、ゲームパッド振動、実行時キーリバインド UI、固定シード Random / timeScale の DI サービス、normal map / IBL、GPU タイムスタンプ プロファイリング、i18n 文字列テーブル。
- **Tier 3** (ゲームの種類が決まってから): ネットワーク、デファード/SSAO/TAA、アセット暗号化、Windows 以外のプラットフォーム。

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
