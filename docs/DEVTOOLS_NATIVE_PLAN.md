# ネイティブ DevTools プラン (別ウィンドウ + 別スレッド、UI システム自身で表示)

**status: 全マイルストーン + v2 + v3 実装完了 (2026-07-03)** — Sample94 `vk 94 [port] [sec] app` で起動。
検証済み: vk 2 デバイス/2 窓/2 スレッド共存、Frame パネル (◀▶ で main/UiFrame をソース切替 —
HUD→Cockpit gauges 等、UiFrame フォールバック付き)、Trees、Log、Stat、
**ECS (ライブ component 値)/Res (依存グラフ)/Surf/Input/Audio/GPU/Graph** (汎用 JSON フラット化 JsonPanel)、
pause/resume/step → Enqueue → メイン Drain のクロススレッド操作。E2E は port+1 の第二 DebugServer。
ブラウザ版 index.html は併存。

**v3 (カード + グラフ、2026-07-03)**: html 版のようにパネルをカードで整理。
- 新基本コントロール **WrapPanel** (折返しフローコンテナ) と **Sparkline** (折れ線/棒の小型グラフ、
  `SetValues` で実体化後も差替え — ListView と同じ Content 差替え + Invalidate 流儀、色は単一 Effect)。
  ギャラリーストーリー WrapPanel/Basic・Sparkline/Basic 追加 (snap 36/36 vk/dx)。
- DevToolsUi: 島テーマの Card 合成ヘルパ (Border+タイトル — Kit.Card は既定テーマ島専用なので不使用) で
  全タブをカード化。Stat タブは WrapPanel ダッシュボード: Perf カード (fps 折れ線 + frame ms 棒、
  履歴 120 点 ≈20 秒)、Runtime カード (ヒープ MB 折れ線 + GC/threads)、Engine/Flush、Phases/Systems。
- 副産物の修正: **Tabs.PerformLayout が Width/Height バインダブルを無視していた** —
  親が無限高さを渡すと 160px にフォールバックし、コンテンツが MaxH=120 に締め付けられていた
  (ListView は layout を無視して描くため露見せず、カード背景が初めて可視化)。Width.Or/Height.Or で尊重。
  Tabs ストーリーの width/height 指定が効くようになり golden 2 件は意図的更新。

2026-07-03。ブラウザ (index.html) の DevTools を、Luxel の UI システムで**別ウィンドウ**に表示する。
処理負荷軽減のため DevTools の描画/UI 処理は**専用スレッド**へ分離する。
エンジンとの結合は従来どおり「DiagnosticListener (読み) + EngineCommands (書き)」のみ —
EngineCommands.Enqueue は ConcurrentQueue でスレッドセーフ (確認済)、DevToolsListener の
LatestSlot/LogRing も読み手スレッドセーフなので、**既存の疎結合がそのままスレッド境界になる**。

## スレッド/所有モデル

```
[メイン (app) スレッド]                     [DevTools スレッド (STA)]
  エンジンループ                              DevToolsApp ループ
  EngineDiagnostics.Emit ──▶ DevToolsListener ──▶ (LatestSlot/LogRing を rev ポーリング)
  EngineCommands.Drain  ◀── Enqueue ◀──────────  ボタン/入力 (pause/step/ui.set…)
  メイン GpuDevice/窓                          自前 GpuDevice + WindowSystem + WindowManager(窓1枚)
```

- **DevTools スレッドは自前の GpuDevice を作る** (メインと共有しない)。GpuQueue の submit は
  外部同期が必要で、共有するとロック競合が「負荷軽減」の目的に反する。デバイス分離なら
  ラスタライザ/コマンドプールも独立し、メインのフレームレートに一切影響しない。
- フレームパネルは DiagFrame (CPU byte[]) 経由なのでデバイスをまたげる
  (devtools デバイスのバッファへアップロード → image プリミティブで表示)。
- HTTP は不要になる (in-process ポーリング)。ただし **E2E 検証用に devtools 窓側にも
  DebugServer を任意ポートで付けられる** (自分の /windows /winframe?format=png / 入力 op)。

## 必要なスレッド安全化 (前提修正)

**方針: [ThreadStatic] は使わない。** スレッドローカルは「今のスレッド構成」を静的に固定してしまい、
将来ゲームシステム自体がマルチスレッド化 (ジョブ/ワーカー) した際に、プールスレッド再利用での
古い値の残留・所有権の不可視化・島の移動不能を招く。代わりに**明示的な所有権 (インスタンス)** へ寄せる。
唯一の例外は OS/COM が「スレッド固有」と定義する資源 (STA の TsfThread) のみ。

| 箇所 | 問題 | 対処 (所有権ベース) |
|---|---|---|
| `Win32Window.Map` (static Dictionary) | 2 スレッドが窓を持つと WndProc 配送辞書を共有 | **静的マップ自体を廃止** — CreateWindowEx の lpParam で instance (GCHandle) を渡し、WM_NCCREATE で **GWLP_USERDATA** に格納。WndProc は USERDATA から instance を引く (Win32 の正攻法、グローバル状態ゼロ) |
| `Win32Window._classRegistered` | 2 スレッド同時初期化の race | lock で 1 回だけ登録 (クラス登録はプロセス全域の Win32 資源なのでプロセス 1 回で正しい) |
| **`UiTheme.Current` (static Signal)** | 2 つの UI スレッドの Effect が同一 Signal の subscriber HashSet へ同時アクセス + Notify が他スレッドの effect を発行側スレッドで実行 | **テーマを UiHost 単位の signal に (明示所有)**: `UiHost` が `Signal<Theme>` を所有し、`UiBuildContext.Theme` / `LayoutContext.Theme` で配る。コントロールは Realize/PerformLayout で受けた参照を閉包にキャプチャ (`UiTheme.T` の静的読みを廃止)。静的 `UiTheme.Current` は**既定テーマ**として残す (UiHost ctor の既定引数) — 単一スレッドの既存アプリ/テスト/snap は挙動不変。DevTools 島は自前 signal を渡すため**スレッド間で signal を共有しない**。副産物: ギャラリーの「プレビューだけテーマ切替」が可能になる (保留していた per-UiHost テーマがこれ) |
| Signal/Effect 全般 | スレッド内前提 (HashSet、ロック無し) | 設計規約: **signal は所有する島 (スレッド) のみが触る**。スレッド間は Listener (volatile/lock 済) と EngineCommands (ConcurrentQueue) のみ。ロックは足さない (将来の MT 化でも「島 + キュー」が単位) |
| ui.set (Luxel.UiSetRequest) | 全 UiHost が購読するので他スレッドの host のハンドラが発行側スレッドで走る | "ui" 名不一致は即 return で実害なし。**対象名なしの ui.set は複数スレッド構成で禁止** (規約)。将来は購読を UiRegistry 経由にして島内配送へ |

テーマ配管の変更規模: コントロールの `UiTheme.T` 読みは Realize (Effect 閉包) と PerformLayout に
集中しており、両方とも ctx を既に受けている — 機械的な置換で済む (Styles.Resolve は既に Theme を
引数に取る形)。アプリコード (Gallery 等) の `Bind.From(() => UiTheme.T.X)` は既定テーマのままで動く。

## コントロール方針 (基本は増やさない)

新規**基本**コントロールは 1 つだけ:
- **ImageView** — CPU の RGBA (w, h, byte[]) を表示する。内部に bindless バッファを持ち、
  `SetPixels(w, h, span)` でアップロード + image ノード dirty (rev 更新はバッファ書換のみ、
  構造変更なし)。IMG-M1 の image プリミティブの CPU ソース版で、フレームパネルの土台。
  (SurfaceView は GPU ソースなので不可。将来は画像ファイル表示にも流用可)

あとは**合成**で賄う (DevTools.App 内のヘルパ、Controls には足さない):
- ツリーパネル = **ListView + インデント文字列** (`"  ".Repeat(depth) + type + detail`)
- Log = ListView / Stat・Perf・Runtime・ECS = key-value 行の ListView (または VStack+Text)
- パネル切替 = 既存 **Tabs** / 操作 = **Button** (engine.pause/resume/step を Enqueue)
- リサイズ = 既存 **Splitter** / スクロール = ListView 内蔵
- **slot**: ListView の行テンプレート slot 化は「文字列で足りなくなったら」着手
  (Slider の SliderSlot と同じ ISlotted 方式)。v1 は文字列行で足りる見込み

## 構成

- 新プロジェクト **Luxel.DevTools.App** (net10.0-windows) — Luxel.DevTools は headless 純度を
  保つため参照を増やさない。参照: DevTools + Controls + Platform (+UI/TwoD/core)。
- API: `DevToolsApp.Launch(Func<GpuDevice> createDevice, DevToolsListener listener, EngineCommands commands, int e2ePort = 0) : IDisposable`
  — STA スレッドを起動し、Dispose で窓を閉じて join。ホスト側は 2 行で装着できる。
- ループ: Pump → listener の各 rev を比較 → 変化したパネルだけ signal/SetItems/SetPixels →
  WindowManager.RunFrame。ポーリングはフレーム毎 (rev 比較は volatile 読みだけで安価)。

## v1 パネル (index.html との対応)

| パネル | データ | 表示 |
|---|---|---|
| Frame | DiagFrame (LatestSlot) | ImageView (rev 変化時のみ SetPixels) |
| Trees | DebugTreeSet (30f 毎 emit) | ListView (UI 毎に見出し + インデント行) |
| Log | LogRing (入力イベント) | ListView (新しい順、Since カーソル) |
| Stat/Perf/Runtime | 各 LatestSlot JSON | key-value ListView |
| 操作 | — | Button: pause / resume / step (Framework の engine.* op) |

ECS/Resources/Primitives/GPU/RenderGraph/Audio/Surfaces はデータ経路が同じなので
v2 で同型に追加 (v1 は上記コアのみ)。

## マイルストーン

- **DT-M1a**: Win32Window の静的マップ廃止 (GWLP_USERDATA) + クラス登録 lock。回帰 (95/Gallery)。
- **DT-M1b**: テーマの UiHost 所有化 — UiHost が Signal&lt;Theme&gt; を所有 (既定 = UiTheme.Current)、
  UiBuildContext/LayoutContext で配布、全コントロールの UiTheme.T 読みを ctx 経由に置換。
  **snap 34/34 不変が合格条件** (既定テーマ経路が同一であることの証明)。
- **DT-M1c**: Luxel.DevTools.App 骨格 (Launch/Dispose、別スレッドで空ウィンドウ + 自前デバイス、
  2 デバイス共存の検証)。
- **DT-M2**: ImageView 基本コントロール + Frame パネル (メインアプリの画面が devtools 窓に映る)。
- **DT-M3**: Trees/Log/Stat/Perf/Runtime パネル (合成) + Tabs + pause/step ボタン + Splitter。
- **DT-M4**: Sample94 に `app` モード (`vk 94 app` = 実エンジン + ネイティブ DevTools) +
  E2E (第二 DebugServer: /windows /winframe?format=png、pause→メインループ停止確認) +
  ドキュメント/メモリ。ブラウザ版 index.html は当面併存 (削除しない)。

## リスク

- 同一プロセス 2 GpuDevice (vk×2): インスタンス/デバイス複製は問題ないはずだが DT-M1c で最初に検証
- テーマ所有化はコントロール全域に触る (機械的だが広い) — snap 34/34 不変をゲートに小さく刻む
- DevTools 窓での ui.set 編集はメインの UiHost に "ui" 名ルーティングで届く (UiRegistry 名が必要)
- 将来のエンジン MT 化との整合: 「UI 島 (スレッド所有) + スレッドセーフキュー」を唯一の境界とし、
  スレッドローカル/グローバル可変状態を増やさない — 本プランの修正はその第一歩
