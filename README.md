# Luxel — 「No Graphics API」C# 実装

[Sebastian Aaltonen の *No Graphics API*](https://www.sebastianaaltonen.com/blog/no-graphics-api)
の設計を C# で提供する薄いグラフィックライブラリ。最新のバインドレス GPU が備える機能
(64bit ポインタ / bindless / dynamic rendering / stage バリア) の上に、ディスクリプタセットや
PSO 爆発のない薄い API を構築する。

- **バックエンド:** Vulkan 1.3 (一次) + DirectX 12 (二次)。`IGpuBackend` 抽象で切替。
- **シェーダ:** Slang で記述し、SPIR-V (Vulkan) と DXIL (D3D12) に併存コンパイル。
- **核心:** 全パイプライン共通の固定レイアウト = 8B push 定数 (ルート引数の GPU アドレス)
  + bindless heap。シェーダはルート引数構造体への単一ポインタを受け取る。

## 必要環境

- .NET 9 SDK
- Vulkan 対応 GPU/ドライバ (`vulkan-1.dll`)
- `slangc` (本リポジトリは `tools/slang/` に standalone Slang を配置して使用)

## ビルドと実行

```powershell
dotnet build Luxel.slnx -c Debug
# サンプル01: compute hello-world
dotnet run --project src/Luxel.Samples -c Debug -- vk 01   # Vulkan
dotnet run --project src/Luxel.Samples -c Debug -- dx 01   # DirectX 12
# サンプル02: 三角形 (オフスクリーン描画→PNG 出力)
dotnet run --project src/Luxel.Samples -c Debug -- vk 02   # Vulkan
dotnet run --project src/Luxel.Samples -c Debug -- dx 02   # DirectX 12
# サンプル03: bindless テクスチャ + サンプラ
dotnet run --project src/Luxel.Samples -c Debug -- vk 03   # Vulkan
dotnet run --project src/Luxel.Samples -c Debug -- dx 03   # DirectX 12
# サンプル04: 深度テスト / サンプル05: アルファブレンド (vk / dx)
dotnet run --project src/Luxel.Samples -c Debug -- vk 04
dotnet run --project src/Luxel.Samples -c Debug -- vk 05
# 2D ベクター (Luxel.TwoD): 06=基本図形+ズーム / 07=地図風(日本語) / 08=GUI風(日本語)
dotnet run --project src/Luxel.Samples -c Debug -- vk 06
dotnet run --project src/Luxel.Samples -c Debug -- vk 07
dotnet run --project src/Luxel.Samples -c Debug -- vk 08
# 09=保持型(retained) UI ツリー + 部分更新
dotnet run --project src/Luxel.Samples -c Debug -- vk 09
# 10=宣言的 UI ライブラリ (Luxel.UI): Grid + signal束縛 + Click/hover (factory 引数 + indexer 統一 DSL)
dotnet run --project src/Luxel.Samples -c Debug -- vk 10
dotnet run --project src/Luxel.Samples -c Debug -- dx 10
# (旧 Sample 11-19, 20 = Luxel.Controls 系/DevTools サンプルは indexer/引数化 refactor 時に削除済み)
# 22=RenderGraph (RG-M1): UI → 分離ガウシアン × 2 → 合成 を多段で組む
dotnet run --project src/Luxel.Samples -c Debug -- vk 22
dotnet run --project src/Luxel.Samples -c Debug -- dx 22
# 23=RenderGraph (RG-M2): 反復ブラー (4 段) で transient aliasing + デッドパスカリングを観測
dotnet run --project src/Luxel.Samples -c Debug -- vk 23
dotnet run --project src/Luxel.Samples -c Debug -- dx 23
# 24=RenderGraph (RG-M3): DevTools 経由でレンダーグラフ DAG を HTTP で観測
dotnet run --project src/Luxel.Samples -c Debug -- vk 24
dotnet run --project src/Luxel.Samples -c Debug -- dx 24
# 25=RG-M4: 最小 ECS (World/Entity/Component/System) + 3D forward 描画 + RenderGraph 経由
dotnet run --project src/Luxel.Samples -c Debug -- vk 25
dotnet run --project src/Luxel.Samples -c Debug -- dx 25
# 26=RG-M5: 3D forward + post-process bloom 連鎖 (4 段: 3D→CopyToBuf→BlurH→BlurV→BloomCombine)
dotnet run --project src/Luxel.Samples -c Debug -- vk 26
dotnet run --project src/Luxel.Samples -c Debug -- dx 26
# 27=RG-M5b: world-space UI (2D ベクター UI を 3D 内の傾いた板にサンプリング)
dotnet run --project src/Luxel.Samples -c Debug -- vk 27
dotnet run --project src/Luxel.Samples -c Debug -- dx 27
# 28=RG-M5c: shadow map (ライト視点 R32F → bindless buffer → 比較)
dotnet run --project src/Luxel.Samples -c Debug -- vk 28
dotnet run --project src/Luxel.Samples -c Debug -- dx 28
# 29=RG-M6: texture transient aliasing (同形 RT/Depth を寿命非重複で alias、物理リソース削減)
dotnet run --project src/Luxel.Samples -c Debug -- vk 29
dotnet run --project src/Luxel.Samples -c Debug -- dx 29
# 30=RG-M6: 動的解像度 (フレームごとに RT 寸法を変更)
dotnet run --project src/Luxel.Samples -c Debug -- vk 30
dotnet run --project src/Luxel.Samples -c Debug -- dx 30
# 31=AN-M1: アニメーションコア (Curve + Tween + Animatable + AnimationPlayer + Signal target)
dotnet run --project src/Luxel.Samples -c Debug -- vk 31
dotnet run --project src/Luxel.Samples -c Debug -- dx 31
# 32=AN-M2: コード DSL (Animate.Tween / Sequence / Parallel + 再生制御 Pause/Resume/Seek)
dotnet run --project src/Luxel.Samples -c Debug -- vk 32
dotnet run --project src/Luxel.Samples -c Debug -- dx 32
# 33=AN-M3: AnimationClip + Track + EcsAnimationTarget (ECS でキューブを Clip 再生)
dotnet run --project src/Luxel.Samples -c Debug -- vk 33
dotnet run --project src/Luxel.Samples -c Debug -- dx 33
# 34=AN-M4: RetainedCanvas (2D 保持型ツリー) に AnimationClip を適用、部分更新を維持
dotnet run --project src/Luxel.Samples -c Debug -- vk 34
dotnet run --project src/Luxel.Samples -c Debug -- dx 34
# 35=AN-M5: AnimationGraph DAG (BlendNode で 2 つの Clip を weight で混合)
dotnet run --project src/Luxel.Samples -c Debug -- vk 35
dotnet run --project src/Luxel.Samples -c Debug -- dx 35
# 36=AN-M6a: CSS @keyframes 文字列をパース → AnimationClip → RetainedCanvas で再生
dotnet run --project src/Luxel.Samples -c Debug -- vk 36
dotnet run --project src/Luxel.Samples -c Debug -- dx 36
# 37=AN-M6b: StateMachine (idle/jump 遷移 + crossfade)
dotnet run --project src/Luxel.Samples -c Debug -- vk 37
dotnet run --project src/Luxel.Samples -c Debug -- dx 37
# 38=TR-M1: CSS transition 風 implicit な値補間 (Signal の値変化を自動で Tween)
dotnet run --project src/Luxel.Samples -c Debug -- vk 38
dotnet run --project src/Luxel.Samples -c Debug -- dx 38
# 39=TR-M2: P.Transition.* 添付プロパティ (Grid.Column 流) で宣言的に Transition 指定
dotnet run --project src/Luxel.Samples -c Debug -- vk 39
dotnet run --project src/Luxel.Samples -c Debug -- dx 39
# 40=TW-M1: Button が StateStyle 引数を受け取り、状態別スタイルを宣言的に指定 (Tailwind/CSS 流)
dotnet run --project src/Luxel.Samples -c Debug -- vk 40
dotnet run --project src/Luxel.Samples -c Debug -- dx 40
# 41=TW-M2: StateStyle + TransitionSet 引数で状態切替が自動補間 (CSS transition モデル)
dotnet run --project src/Luxel.Samples -c Debug -- vk 41
dotnet run --project src/Luxel.Samples -c Debug -- dx 41
# 42=TW-M3: ユーザー定義 AppTheme record + Signal<AppTheme> で Light/Dark 切替
dotnet run --project src/Luxel.Samples -c Debug -- vk 42
dotnet run --project src/Luxel.Samples -c Debug -- dx 42
# 43=TW-M5: Luxel.UI.Tailwind 別アセンブリで utility (S.Bg/Fg/Rounded/...) + token (Tw.Blue500/...) + On(Hover, ...)
dotnet run --project src/Luxel.Samples -c Debug -- vk 43
dotnet run --project src/Luxel.Samples -c Debug -- dx 43

# 44=TW-M4: Text にも StateStyle 引数 + utility (Fg/FontSize/Opacity) が波及
dotnet run --project src/Luxel.Samples -c Debug -- vk 44
dotnet run --project src/Luxel.Samples -c Debug -- dx 44# 45=TW-M4b: CheckBox の checked: 状態 (Tailwind の checked: prefix と等価)
dotnet run --project src/Luxel.Samples -c Debug -- vk 45
dotnet run --project src/Luxel.Samples -c Debug -- dx 45
# 46=TW-M4c: Switch の on/off 切替 (utility + On(Checked, Bg(Green500)))
dotnet run --project src/Luxel.Samples -c Debug -- vk 46
dotnet run --project src/Luxel.Samples -c Debug -- dx 46
# 47=TW-M4c 全 Widget: Slider/TextField/Select/Segmented/Radio/Tabs に utility 適用
dotnet run --project src/Luxel.Samples -c Debug -- vk 47
dotnet run --project src/Luxel.Samples -c Debug -- dx 47
dotnet test
```

## 2D ベクターレンダリング (拡張: Luxel.TwoD)

GPU **コンピュートラスタライザ** (Vello 風) による 2D ベクター描画。パスを三角形分割せず、
線分のまま GPU に常駐させ、compute が画素ごとに巻き数/距離で被覆を計算して塗る。
バックエンド変更ゼロ (framebuffer を bindless バッファに書き→読み戻し)。Vulkan/D3D12 一致。

- **塗り (穴対応)** NonZero/EvenOdd、**複数パス合成**、**ストローク** (距離ベース・画面一定幅)、
  **ベクターテキスト** (TTF 輪郭→パス→塗り、**日本語対応** TTC/Yu Mincho)、**SDF的角丸** (パス)。
- **スムーズズーム**: ワールド座標で 1 回 `Encode` → `Camera2D` を変えるだけで連続拡縮 (再エンコード/
  再三角形分割なし)。毎フレーム CPU 負荷はシーン複雑度に非依存。
- 入口: `Scene2D` (パス構築)、`VectorFont` (TTF)、`Rasterizer2D.Encode/Render`、`Camera2D`。

```csharp
var scene = new Scene2D();
scene.FillRoundedRect(Color2D.Blue, 40, 40, 120, 80, 12);
using (var jp = VectorFont.LoadSystemJapanese())
    jp.AppendText(scene, "こんにちは", 50, 120, 28, Color2D.Black);
using var raster = new Rasterizer2D(device);
using var encoded = raster.Encode(scene);                 // GPU へ 1 回
using var fb = device.Malloc(w*h*4, GpuMemoryKind.HostMapped);
using var cmd = device.MainQueue.StartCommandRecording();
raster.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);   // ズームは Camera2D.Create(...) に差し替え
cmd.Finish(); device.MainQueue.SubmitAndWait(cmd);
```

### 保持型 (retained) UI ツリー + 部分更新 (Luxel.TwoD/Retained)

UI ライブラリのバックエンド向けに、フレーム間で保持するノードツリーと**部分更新**を提供。
データを SoA (Transform/Style/Clip/Order と Segment を分離) にし、シェーダが per-path 変換を適用するため、
**移動=変換だけ書込、色変更=スタイルだけ書込 (ジオメトリ不変)**。

```csharp
using var raster = new Rasterizer2D(device);
using var canvas = new RetainedCanvas(raster);
UiNode panel = canvas.AddChild(canvas.Root);
panel.Transform = Affine2D.Translate(40, 40);
panel.Color = Color2D.White;
panel.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 400, 240, 16);
UiNode button = canvas.AddChild(panel);
button.Transform = Affine2D.Translate(250, 190);
button.Color = accent;
button.Content = new Scene2D().FillRoundedRect(accent, 0, 0, 90, 32, 8);

canvas.Render(cmd, Camera2D.Pixels, w, h, fb);   // 初回はフル構築
button.Color = red;                               // 部分更新: スタイルのみ
button.Transform = Affine2D.Translate(250, 150);  // 部分更新: 変換のみ
canvas.Render(cmd, Camera2D.Pixels, w, h, fb);    // Segment 書込 0 (canvas.LastSegmentBytesWritten==0)
```

- `UiNode`: ローカル変換/色/不透明/クリップ(矩形)/Z/子/コンテンツ。setter が dirty を伝播。
- `RetainedCanvas`: ツリー所有 + dirty フラッシュ + 描画。`LastTransformWrites`/`LastStyleWrites`/
  `LastSegmentBytesWritten` で部分更新量を確認可能。
- **クリッピング(AABB)**: ノードの矩形クリップを祖先と交差して適用 (スクロール/パネル)。
- **描画順**: ツリー pre-order + 兄弟内 Z で `Order[]` を構築 (奥→手前 alpha 合成)。
- 構造変更(追加/削除): 現状はフル再構築で対応 (正しいが非増分)。増分 free-list アロケータは将来最適化。
- レイアウト(flex/制約)・入力/ヒットテストは上位 UI ライブラリ (`Luxel.UI`) の責務。

## 宣言的 UI ライブラリ (拡張: Luxel.UI)

保持型 2D 層の上に、**宣言的 C# DSL** + **signals (細粒度リアクティブ)** + **単一パスレイアウト**
(Flutter 風) + **入力 (Click/hover)** を提供する UI ライブラリ。

```csharp
using static Luxel.UI.UI;            // 構築ファクトリ (Grid/Button/Text/Border ...)
using static Luxel.UI.Decl;          // 添付プロパティ用の P
var count = new Signal<int>(0);
var host = new UiHost(canvas, font, w, h);
host.SetRoot(
    Border(
        Grid(columns: [1, 2], rows: [GridLength.Px(70), GridLength.Star(1)])[
            Text(() => $"Count: {count.Value}", 30, P.Grid.Row(0), P.Grid.ColumnSpan(2)),
            Button("- 1", () => count.Value--, P.Grid.Column(0), P.Grid.Row(1)),
            Button("+ 1", () => count.Value++, P.Grid.Column(1), P.Grid.Row(1))
        ]
    ).WithBackground(panel).WithPadding(new Thickness(16)));

host.PointerMove(190, 105);   // hover → ボタン背景色を部分更新 (スタイルのみ)
host.Click(190, 105);          // ヒットテスト → onClick 発火 → count.Value++ → テキスト更新
canvas.Render(cmd, Camera2D.Pixels, w, h, fb);
```

- **宣言的 DSL**: 構築は `using static Luxel.Controls.Kit;` の**ベアファクトリ** `Grid(...)`/`Button(...)`/`Text(...)`/
  `VStack(...)`/`Border(...)` (ソースジェネレーターが `[UiComponent]`/`[UiParam]` から生成)、子は get-only
  インデクサ `[...]` (`Grid(columns:[1,2])[ … ]`)、列定義 `[1,2]` は `int→GridLength` 暗黙変換で **star 比率**。
  すべての見た目プロパティは `[UiParam]` の `Bindable<T>` 引数 (`background:`/`variant:`/`rounded:` 等、
  fluent メソッドは廃止)。状態別は Tailwind utility の `On(WidgetState.Hover, Bg(...))` を `parts` に渡す。
  **添付プロパティだけ `P.Grid.Column(1)`/`.Row`/`.ColumnSpan`** (C# 14 拡張メンバー: `P` の型への拡張
  プロパティ `Grid` が返すファサード) を `params INodePart[]` で渡す。`<LangVersion>14</LangVersion>` 必須。
  (補足: `Foo(...)` 呼び出しと `Foo.Bar` メンバアクセスを同名で両立できない C# 制約のため、構築は
  ベア関数、添付は `P.Grid.*` に分離している。)
- **signals**: `Signal<T>`/`Computed<T>`/`Reactive.Effect`。プロパティを `() => signal.Value` で束縛すると、
  signal 変化でその束縛ノードだけ再評価。**色/位置は保持型の部分更新**(スタイル/変換のみ)。
  テキスト内容変更は当面フル再構築 (増分は将来)。
- **レイアウト (Flutter 風 単一パス)**: `Layout(Constraints, parentUsesSize) → Size` を 1 回呼び、
  同じ呼び出し内で子の Offset を書く (Measure/Arrange 2 パス不要)。Grid は Fixed/Star/Auto
  (Auto 列のみ局所的に intrinsic 幅を測る)、StackPanel/Border は完全 1 パス。
- **入力**: `UiHost.Click(x,y)` / `PointerMove(x,y)` が前面優先でヒットテストし onClick/hover を発火。
- ウィジェット: `Grid` / `StackPanel` / `Border` / `Button` / `Text`。
- 将来: keyed reconciler (構造差分)、テキスト内容の増分更新、bubbling/capture・キーボード・スクロール、
  Auto 行・整列(Center/Stretch)。

#### 別プロジェクトでコントロールを追加する (拡張)

`Widget` を継承すれば**別アセンブリ**で独自コントロールを追加できる (公開 API のみで完結)。
`PerformLayout` でサイズ決定、`Realize` で `UiNode` 生成 + signal を `Reactive.Effect` 束縛、
`ctx.AddHit(rect, onClick, onHover)` で入力登録。配置の添付プロパティは組込の `P.Grid.Column(1)` を
そのまま使える (独自の添付キーは `Widget.SetAttached`/`GetAttached<T>(key)` + C# 14 拡張プロパティで `P` に追加可)。

```csharp
// 消費側プロジェクトで:
public sealed class CheckBox(Signal<bool> on, string label) : Widget
{
    protected override void PerformLayout(Constraints c, LayoutContext ctx) { /* Size を決める */ }
    public override void Realize(UiBuildContext ctx, UiNode parent, Point origin)
    {
        var node = ctx.Canvas.AddChild(parent);
        node.Transform = Affine2D.Translate(Offset.X, Offset.Y);
        var check = ctx.Canvas.AddChild(node); /* ... 描画 ... */
        Reactive.Effect(() => check.Opacity = on.Value ? 1f : 0f);   // signal→部分更新
        ctx.AddHit(new Rect(origin.X+Offset.X, origin.Y+Offset.Y, Size.Width, Size.Height),
                   onClick: () => on.Value = !on.Value);
    }
}
// ベアファクトリ (using static で呼ぶ):
public static class MyControls {
    public static CheckBox CheckBox(Signal<bool> on, string label, params INodePart[] parts)
    { var c = new CheckBox(on, label); c.ApplyParts(parts); return c; }
}
// 使用: VStack( CheckBox(opt, "Shadows", P.Grid.Column(0)) )   // 組込と混在可
```
サンプル11 (`-- vk 11` / `-- dx 11`) が Luxel.Samples (別アセンブリ) で `CheckBox` を実装した実例。

## コントロールライブラリ (拡張: Luxel.Controls)

`Luxel.UI` の上に、React 系 UI フレームワーク (MUI/Ant/Chakra) 相当のコントロール群を整備
(Phase 1+2 実装済み)。横断基盤は `Luxel.UI` 側: **テーマ** (トークン+Light/Dark, signal 切替で recolor)、
**フォーカス/キーボード** (`UiHost.FocusNext/KeyDown/Char/Compose/Commit`)、**オーバーレイ** (最前面+アンカー
配置+scrim+Esc/外側ディスミス)、**スクロール** (`ScrollViewer`)、**アニメ** (`UiHost.Tick`)、**動的リスト**
(`UI.Each/When`)、**テキスト編集** (`TextEditor`: caret/選択/IME)、**フォーム** (`Field<T>` + バリデーション)。

```csharp
using static Luxel.UI.UI;
using static Luxel.Controls.Kit;
var host = new UiHost(canvas, font, w, h);
host.SetRoot(
    Card(VStack(
        Heading("Settings", 2),
        FormField("Name", TextField(name.Value, "Enter name"), name.Error),   // 入力+バリデーション
        Segmented(["Day","Week","Month"], view),                              // 単一選択 (←→)
        new Switch(enabled),                                                   // トグル (Space)
        Slider(volume),                                                        // ドラッグ/←→
        HStack(Menu("Actions", ("Rename", Rename), ("Delete", Delete)),       // ドロップダウン
               Button("Save", Save)).WithSpacing(8)
    ).WithSpacing(14)));
host.FocusNext(); host.Char("Hi"); host.KeyDown(Key.Tab);   // programmatic 入力 (ウィンドウ無し)
```

カタログ (S=表示, M=対話, L=重い):
- **入力/フォーム**: Button(M)・CheckBox(M)・Switch(M)・RadioGroup(M)・SegmentedControl(M)・Slider(M)・
  **TextField**(L, caret/選択/IME)・**Select**(L, オーバーレイ)・Field/FormField(バリデーション)。
- **表示**: Text/Heading/Label/Muted(S)・Icon(S)・Avatar(S)・Badge(S)・Chip(S)・Card(S)・Divider(S)・
  Alert(S)・ProgressBar(M)・Spinner(アニメ)・Skeleton(S)・Accordion(M, アニメ)。
- **レイアウト**: Stack/Grid/Border/Center/Spacer・ScrollViewer。
- **ナビ**: Tabs(M)・Menu/Dropdown(L)・Breadcrumb(S)・Pagination(M)。
- **オーバーレイ**: Tooltip(L)・Dialog(L, モーダル)・Drawer(L)・Toast(L)。

サンプル 12 (テーマ/表示)・13 (フォーカス/キーボード/Scroll)・14 (オーバーレイ/アニメ)・15 (テキスト編集/Form)
を vk/dx で検証。**Phase 3 ロードマップ (未実装)**: 仮想化・DataGrid・Autocomplete・DatePicker・ColorPicker 等。

## 実ウィンドウ + スワップチェーン + IME (拡張: Luxel.Platform, Windows 専用)

オフスクリーン(PNG)に加え、**実 OS ウィンドウ表示**と**実 IME(日本語入力)**を提供 (CsWin32 で Win32/TSF を生成)。

```csharp
using var device = new GpuDevice(VulkanBackend.Create());   // dx も可
using var font = VectorFont.LoadSystemJapanese();
using var app = new AppWindow(device, font, 720, 520, "Luxel");
app.SetRoot( Card(VStack( Heading("…",1), TextField(name,"…"), Button("OK", ()=>{}) )) );
app.Run();   // PeekMessage ループ: 入力→UiHost, 毎フレーム描画→swapchain present
```

- **ウィンドウ/入力**: `Win32Window`(CreateWindowEx + WndProc + PeekMessage)。マウス/ホイール/キー/WM_CHAR/
  リサイズを `UiHost` へ配線。`AppWindow.Run()`(対話) / `RunFrames(n)`(スモーク)。
- **スワップチェーン提示**: compute が書いた RGBA8 framebuffer を **swapchain image へコピー**して present
  (Vulkan=`vkCmdCopyBufferToImage`, D3D12=`CopyTextureRegion`)。framebuffer 幅を 64 倍数にパディングし
  D3D12 の 256B 行整列を充足、可視領域のみ提示。両バックエンド R8G8B8A8 で swizzle 不要。リサイズ再生成。
- **IME (TSF, 自前 preedit)**: `TsfTextStore : ITextStoreACP` をフォーカス中の `TextField`(`ITextInput`)へ
  橋渡し。`GetTextExt` が caret 矩形を返し候補ウィンドウを caret 位置へ。**preedit 下線・変換対象節ハイライト・
  caret は自前描画**(`ImeComposition` モデル)。IME 各状態の描画はオフスクリーンで自動検証 (サンプル16)。
  実 IME 変換は実ウィンドウで手動確認 (サンプル17 `-- vk 17 live`)。

### 2D の状況・今後
- 実装済み: 塗り/穴/合成/ストローク/ベクターテキスト(日本語)/角丸/スムーズズーム/bbox 早期スキップ
  (簡易カリング)。サンプル 06–08 を vk/dx で検証 (ピクセル一致)。
- 今後 (性能/品質): タイル binning (現状は画素×線分のブルートフォース+bbox早期スキップ)、
  解析的 AA (font-rs 面積法, 現状 4x4 スーパサンプル)、ウィンドウ/スワップチェーン表示。

## レンダーグラフ (拡張: Luxel.RenderGraph)

UI のレンダリング結果を別パスでテクスチャとして参照したり、シェーダのアルゴリズムを
**複数段階 (compute/graphics 混在)** で組み立てるための薄い管理層。設計と意思決定は
[docs/RENDER_GRAPH_PLAN.md](docs/RENDER_GRAPH_PLAN.md) に記載。要点:

- **scene-agnostic**: 入力は GPU ハンドル (`BufferHandle`) のみ。シーン側 (RetainedCanvas / UI / 将来の ECS) を一切知らない。
- **Setup / Compile / Execute の三相** (Frostbite FrameGraph / UE RDG / Unity URP / Granite と同型)。
- 自動バリア: pass の Read/Write 宣言から `GpuStage` の遷移を計算し `GpuCommandBuffer.Barrier` を挿入。
- Transient プール: 同形 `BufferDesc` のバッファを 1 フレーム内で再利用 (RG-M2 で aliasing 予定)。
- バックエンド変更ゼロ: bindless 統一レイアウト (`g_buffers[index]` + 8B push 定数) をそのまま使う。

```csharp
using var rg = new Luxel.RenderGraph.RenderGraph(device);
BufferHandle hUi    = rg.ImportBuffer(ui,    "ui");        // External
BufferHandle hTmp   = rg.CreateBuffer(new BufferDesc(fbBytes), "blurH");  // Transient
BufferHandle hFinal = rg.ImportBuffer(final, "final");

rg.AddPass("BlurH", PassQueue.Compute)
  .Read(hUi).Write(hTmp)
  .Execute(ctx => ctx.Cmd.SetComputePipeline(blur).SetRootArguments(new {
      Src = ctx.BindlessIndex(hUi), Dst = ctx.BindlessIndex(hTmp), /*…*/
  }).Dispatch((w+7)/8, (h+7)/8));
// …BlurV, Composite も同様…
rg.Execute(cmd);   // Compile + Execute (寿命解析 + 自動バリア + lambda 駆動)
```

サンプル22 (`-- vk/dx 22`) で **UI → BlurH → BlurV → Composite** を vk/dx でピクセル一致確認。

### 進捗
**RG-M1 完了**:
- Setup / Compile / Execute 三相、bindless ハンドル解決、Read/Write 宣言。
- バッファ寿命解析 (first-write / last-read)。
- パス境界の自動 stage バリア。
- サンプル22 (UI → blur → composite) vk/dx 一致。

**RG-M2 完了**:
- **Transient aliasing** — 同形 (Size, Kind) で寿命非重複の transient を interval scheduling で物理バッファ共有。
- **デッドパスカリング** — External 出力に到達しない pass を Compile 相で除外 (backward reachability)。
- **物理単位のバリア追跡** — aliasing 境界 (異なる論理 → 同じ物理) でもハザード検出。
- 公開検査 API: `PhysicalTransientBufferCount` / `GetPhysicalSlot` / `IsAliased` / `IsPassCulled` / `LastExecutedPassCount`。
- サンプル23 (反復ブラー×4 + DeadPass) vk/dx 一致: **論理 5 transient → 物理 2 個** (3 個 alias で削減 + 1 個未使用)、DeadPass culled。

**RG-M3 完了**:
- `IRenderExtractor` + `ExtractContext` 抽象を導入 (シーン側を GPU バッファへ抽出する規約。RG-M4 の 3D + ECS で本格利用)。
- `Luxel.Diagnostics` に `DiagRenderGraph`/`DiagRenderGraphPass`/`DiagRenderGraphResource` を追加、`RenderGraph.Execute` 末尾で `EngineDiagnostics.Emit("Luxel.RenderGraph", ...)` 発行。
- `DevToolsListener` が購読し `LatestSlot` で coalesce、`DebugServer` に `GET /rendergraph` エンドポイント追加。
- `wwwroot/index.html` に **レンダーグラフ パネル** + ボタン (`getRenderGraph()`) 追加: パス=列ノード、リソース寿命=帯、culled=灰色、aliased=橙縁取り、External=青縁取り を SVG で描画。
- サンプル24: `DebugServer` を loopback で起動 → RG 構築/Execute → `HttpClient` で `/rendergraph` を取得し JSON を 12 項目検証 (passes/resources/culling/aliasing/slot 整合)。vk/dx で 12/12 OK。

**RG-M4 完了**:
- 新規プロジェクト `src/Luxel.ThreeD` (net9.0, Luxel + Luxel.RenderGraph のみ参照)。
- **最小 ECS** (`World`/`Entity`/`Set/Get/Query` ジェネリック) を内蔵 (約 100 LoC, 依存ゼロ)。本格運用では Arch/Friflo 等への置換を想定。
- コンポーネント: `LocalTransform` / `GlobalTransform` / `Parent` / `MeshRef` / `Color3D` / `Visible`。
- `TransformPropagateSystem` — Parent をたどって GlobalTransform を更新 (深い階層は反復で安定)。
- `Render3DExtractor : IRenderExtractor` — ECS をクエリ → `InstanceData[]` (80B = mat4 + vec4) を bindless buffer に書く。
- 固定 `CubeMesh` (36 頂点, position + normal, 24B/頂点)。
- Slang シェーダ `cube_forward.slang` — 頂点プル + bindless instance、ピクセルで N·L 簡易拡散ライティング。両 backend に SPIR-V/DXIL 併出力。**System.Numerics と HLSL の行列レイアウト差は CPU 側 `Matrix4x4.Transpose` で吸収**。
- サンプル25: ECS で 5×5=25 個のキューブを生成 → TransformPropagate → Render3DExtractor → RenderGraph (graphics pass, 深度有効) → PNG。**vk/dx で非背景ピクセル 8726/65536 完全一致**。
- 単体テスト: World/CRUD/Query、TransformPropagate (root/親子/深い階層)、CubeMesh 検証 = 10 件追加。

**RG-M5 完了**:
- **3D + post-process 連鎖** を 1 つの RenderGraph で組めるようにした。サンプル26 = 4 パス (Render3D + BlurH + BlurV + BloomCombine)。
- 新シェーダ `compute_bloom_combine.slang` — 元画像 + intensity × blur を加算合成 (additive bloom)。
- **D3D12 backend の重要な修正** — `CopyTextureToBuffer` 後に dst バッファを `CopyDest → Common` で明示遷移。implicit promotion で次の compute UAV/SRV 読みに正しく繋がり、Copy→Compute のハザードが解消。Vulkan は synchronization2 のメモリバリアで元から正しく動作していたが、D3D12 のレガシーバリアは UAV barrier のみだったため不足していた。**サンプル 26 でこの修正により vk/dx 完全一致 (65536/65536 bloom 適用)**。
- 既存サンプル (02/03/04/05/22/23/24/25) すべて vk/dx で回帰なし。
- **CopyDest 等の正しい usage を `Write(handle, ResourceUsage.CopyDest)` で宣言**することで RG の auto-barrier が `Copy→Compute` のステージ遷移を発行できる (デフォルト `StorageBufferWrite` だと ComputeShader stage 扱いになり実際の Copy 経由を覆えない)。

**RG-M5b 完了 (world-space UI)**:
- `Luxel.ThreeD.UiQuadMesh` — 4 隅 + UV を持つ quad (6 頂点 2 三角形, stride 24B)。
- `shaders/ui_quad_3d.slang` — 頂点プル + pixel で UI bindless buffer を sampling (`Load`)、`mvp` を push constants で。
- サンプル27 — 1 つの RenderGraph で 2D UI ラスタライズ済み buffer を import → 3D 空間の傾けた板に貼り、キューブ群と一緒に同じ RT へ描画 (1 graphics pass 内で pipeline 切替+2 draw)。`Read(hUi, ResourceUsage.SampledInPixelShader)` で auto-barrier が Compute→PixelShader 遷移を発行。vk/dx で 21072/65536 完全一致。
- このパターンにより「2D UI を 3D の壁/HUD として配置」「VR/AR スタイルの world-space HUD」「UI を 3D 内で歪曲して配置」が同じ RenderGraph 構成で表現可能。

**RG-M5c 完了 (shadow map)**:
- `shaders/shadow_pass.slang` — ライト視点の頂点変換 → R32Float RT に NDC z (= `SV_Position.z`) を pixel 書込。
- `shaders/cube_with_shadow.slang` — メイン視点 forward + shadow buffer から `Load` で z 比較。bias 0.003、ライト視野外は影なし扱い。
- backend 不変: 既存の `CreateRenderTarget(R32Float)` + `CopyTextureToBuffer` で shadow RT を bindless buffer 化、その buffer を pixel から `Load(addr)` で読む流れ。「shadow map = R32F カラー RT + bindless buffer 経由 sampling」という Aaltonen 流の compute-first 哲学に従う実装。
- **Push constants サイズを 128B → 192B に拡張** (Vulkan/D3D12 両方)。mat4×2 (viewProj + lightVP) + 数 uint を渡すため。Vulkan の spec min は 128B だが最新 GPU は 256B 対応 (RTX 4080 SUPER 等で確認済)、D3D12 root signature は 64 DWord = 256B 上限なので余裕あり。
- サンプル28 — 床キューブ + 浮遊キューブ 5 個。RG で 2 graphics pass (ShadowPass→MainPass) を組み、各 lambda 内で `BeginRendering`/`Draw`/`EndRendering`/`Barrier(ColorOutput,Copy)`/`CopyTextureToBuffer` を行う。`Write(shadowBuf, ResourceUsage.CopyDest)` で次パスへの barrier を auto 発行。vk/dx で灰色域 42213 ピクセル完全一致、床に影が落ちる絵に。

**RG-M6 完了 (Texture aliasing + 動的解像度)**:
- `TextureHandle` / `TextureDesc(Width, Height, Format, Kind)` / `TextureKind{Color, Depth}` / `TextureUsage{ColorAttachment, DepthAttachment, SampledPixel, CopySource, CopyDest}` を `Luxel.RenderGraph` に追加。
- `RenderGraph.ImportTexture` / `CreateTexture` を追加。`PassBuilder.Read(TextureHandle, TextureUsage)` / `Write(TextureHandle, TextureUsage)` オーバーロード。
- **Texture transient aliasing**: `(Width, Height, Format, Kind)` でグループ化し、寿命非重複ならグループ内で物理 GpuTexture を共有 (Buffer aliasing と同じ interval scheduling)。`Color` と `Depth` は別グループ。Compile 相で `_device.CreateRenderTarget`/`CreateDepthTarget` を呼び `_ownedTransientTextures` に保持、Dispose で一括解放。
- 公開検査 API: `PhysicalTransientTextureCount` / `GetPhysicalSlot(TextureHandle)` / `IsAliased(TextureHandle)`。`ResourceAccess` を buffer/texture 統一表現に refactor、auto-barrier が `object` 単位の `ReferenceEqualityComparer` でリソース追跡 (Buffer/Texture 両対応)。
- サンプル29 — 同じ ECS シーンを 2 つの視点 (front / side) で順次描画。各視点用に同形 `Transient` RT + Depth を宣言 → 物理は 1 color + 1 depth (4 論理 → 2 物理 alias)、最後に合成。vk/dx で非背景 10927/65536 完全一致。
- **動的解像度** (サンプル30) — 同じ ECS シーンを 3 解像度 (256/192/128) で順次レンダ。各フレームで `RenderGraph` を新規構築 → `CreateTexture(TextureDesc(size, size, ...))` で transient RT/Depth を確保 → `Dispose` で解放。フレームごとの RT 寸法変更が同じ API でそのまま表現できることを実証。vk/dx 完全一致。
- 既存サンプル (01-28) 全て vk/dx で回帰なし、テスト 91→**97 件** (texture aliasing 6 件追加) 全合格。

今後: partial update / RetainedCanvas dirty 伝播との連携 (機能としては既存)、その他の発展は [docs/RENDER_GRAPH_PLAN.md](docs/RENDER_GRAPH_PLAN.md) 参照。

## アニメーション (拡張: Luxel.Animation / Luxel.Animation.UI)

様々な形式 (glTF / CSS / Lottie subset / コード DSL) を統一的に扱う**アニメーションシステム**。
設計プラン: [docs/ANIMATION_PLAN.md](docs/ANIMATION_PLAN.md) (Open Questions 全 6 件解決済)。

設計の核心: **「3 層 IR は共通、ターゲット書込みは 3 アダプタに分離」** (deep-research の業界 5 実装が同型に収束)。

```
時間 t → Curve.Eval(t01) → progress → Tween.Lerp(p) → 値 T → Setter
        (LinearCurve / CubicBezierCurve / StepsCurve / SpringCurve)
                                       (FloatTween / Vector2/3 / RgbaTween / QuaternionTween-slerp)
                                                                        ↓
                                                  Signal<T>.Value / UiNode 部分更新 / ECS Set<T>
```

### AN-M1 完了 (コアと UI Adapter, 絶対時刻 Clock)
- `Animatable<T>` = `ICurve` + `ITween<T>` + `Duration` の 2 段分解 (Flutter Animatable 流)
- **`IClock` (FixedFrameClock / WallClock / ManualClock) + `AnimationPlayer.Update(IClock)` で絶対時刻モデル** ─ frame ベースで累積誤差ゼロ (60 frame ピッタリで Done 保証)
- `SignalAnimationTarget.For(signal)` で `Signal<T>` を `Action<T>` として受け取り → reactive 機構が自動起動
- サンプル 31 で vk/dx 完全一致 (4 フレーム t=0/0.33/0.66/1.00、frame=0 で `pos=(20.0)` ピッタリ、frame=60 で `pos=(180.0)` 確実完了)

### AN-M2 完了 (DSL fluent + Sequence/Parallel + 再生制御)
- **`Animate.Tween(setter, from, to, dur).WithCurve(...).WithDelay(...).OnComplete(...).Play(player, clock)`** fluent DSL
- **`Animate.Sequence(a, b, c)`** / **`Animate.Parallel(a, b, c)`** で合成。Sequence は子の TotalDuration を時間オフセットで連鎖、Parallel は同時開始
- `IAnimationCommand` 抽象 (TweenCommand / SequenceCommand / ParallelCommand)
- **再生制御**: `TrackEntry.Pause(clock)` / `Resume(clock)` (AccumulatedPausedTime で停止時間を track time から除外) / `Seek(localTime, clock)` (StartTime 再計算)
- サンプル 32 で 2 カードの Sequence(Parallel + Parallel) を vk/dx 完全一致で実行

### AN-M3 完了 (AnimationClip + Track + ECS Adapter)
- **L1 IR**: `Keyframe<T>` / `Track<T>` (target path + keyframes + Step/Linear/CubicSpline 補間) / `AnimationClip` (複数 Track の束)
- `Tracks.Float/Vector2/3/4/Quaternion(slerp)/Color` ファクトリで型ごとに lerp 関数を埋め込み
- **`IAnimationTarget` 抽象** (path + value の薄い抽象、scene-agnostic 設計の核)
- **`EcsAnimationTarget`** (Luxel.Animation.ThreeD): "{entity}/translation|rotation|scale|color" を解釈、`Matrix4x4.Decompose` で TRS 分解 → 該当成分を上書き → 再合成して `LocalTransform` に書き戻し。カスタム property 用 `RegisterPropertyHandler` 拡張点も
- **`Animate.Clip(clip, target).WithLoop().OnComplete(...).Play(player, clock)`** で Clip 全体を再生 (各 Track が独立した TrackEntry になる)
- サンプル 33 で `AnimationClip` (translation Vector3 + rotation Quaternion の 2 Track) をコード構築し、ECS の 1 キューブで再生 → TransformPropagateSystem → Render3DExtractor → 描画。vk/dx 完全一致 (4 フレーム t=0/0.5/1.0/1.5 で y 値・回転が同値)
- 単体テスト累計 141 件 (Animation 44 件: 基本 35 件 + Track 5 + EcsAnimationTarget 3 + ClipPlayback 1)

### AN-M4 完了 (RetainedCanvas 連携)
- 新規 `src/Luxel.Animation.TwoD` (Luxel + Animation + TwoD 参照)
- **`RetainedCanvasAnimationTarget`** が `"{nodeName}/{property}"` パスを解釈、サポート property: `transform`/`translation`/`translationX/Y`/`scale`/`scaleX/Y`/`rotation`/`color`/`opacity`、`RegisterPropertyHandler` で拡張可
- 各 setter は `UiNode` の既存 dirty 伝播 (transform slot / style slot のみ書込み) を起動 → **segment データは触らず部分更新**
- サンプル 34: 2 カードに slide-in + fade-in + 色変化 (Clip 全 4 Track) を適用、4 フレームで vk/dx 完全一致
- **部分更新を観測値で検証**: 初回 Flush 以外は `LastWasFullRebuild=false` / `LastTransformWrites=2` / `LastStyleWrites=2` / `LastSegmentBytesWritten=0` (ジオメトリ不変)
- 単体テスト累計 143 件

### AN-M5 完了 (AnimationGraph DAG)
- **`GraphNode` 抽象** + 3 種実装: `ClipNode` / `BlendNode` (lerp 混合) / `AddNode` (additive)
- **`GraphEvaluator`** が path → (値, Track 型情報) を蓄積、型ごとの正しい `Lerp` / `Add` (Quaternion は slerp)
- **`AnimationGraph.Tick(clock)`** で Root を毎フレーム評価 → `Target` へ書込み
- サンプル 35: 2 Clip (上下振動 + 左右振動) を weight=0/0.5/1 でミックス → vk/dx 完全一致

### AN-M6 完了 (CSS @keyframes + StateMachine)
- **`CssKeyframesImporter.Parse(css, prefix, duration)`** — CSS `@keyframes` 文字列を解析、`opacity`/`transform: translateX/Y, scale, rotate(deg/rad)`/`background-color: rgba(...)/#hex` を Track 群へ変換、未対応プロパティは warnings に追加
- サンプル 36: CSS テキストから 3 Track (opacity + translationX + color) を抽出し RetainedCanvas で再生、vk/dx 完全一致
- **`StateMachine`** + **`State`** + **`Transition`** — Trigger 名で遷移、`CrossfadeSec` で BlendNode 経由の動的混合
- サンプル 37: idle/jump 2 状態、`Trigger("press")`/`Trigger("done")` で切替、遷移中は中間色を観測 → vk/dx 完全一致
- 単体テスト累計 155 件 (CSS 4 + StateMachine 2)

## UI Transitions (拡張: CSS transition 風)

設計プラン: [docs/UI_TRANSITION_PLAN.md](docs/UI_TRANSITION_PLAN.md) (Open Questions 全 8 件解決済)。

CSS `transition` のように **「値変化を検知して自動補間」** を、scene-agnostic な setter ラッパーで実現する:

```csharp
// setter をラップ → 値変化時に自動補間
var animatedColor = Transition.Animate<uint>(
    v => node.Color = v, player, clock,
    duration: 0.25f, curve: CubicBezierCurve.EaseInOut);
animatedColor(Color2D.Red);    // 初回は即時 Apply
animatedColor(Color2D.Blue);   // 0.25s で Red→Blue を補間

// Signal と組み合わせ
var hovered = new Signal<bool>(false);
using var sub = SignalTransition.Watch(hovered,
    h => animatedColor(h ? Red : Blue));
hovered.Value = true;   // 自動補間が起動
```

### TR-M1 完了 (Setter ラッパー + Signal 連携)
- **`Transition.Animate<T>(setter, player, clock, duration, curve, delay)`** ─ scene-agnostic 核心。Luxel.Animation 本体に配置
- **型ごとの自動 Tween 選択** ─ float/Vector2/3/4/Quaternion(slerp)/uint(RgbaTween)/不明型は Step
- **Smooth interrupt** ─ 進行中の値を凍結 (`Stop`)、現在値からフル duration で新値へ
- **delay 対応** ─ CSS `transition-delay` 互換、staggered animation 可
- **デフォルト curve = EaseInOut** ─ Framer Motion / SwiftUI のモダンデフォルト
- `Transition.Watch(Signal, animatedSetter)` (Luxel.Animation.UI) ─ Signal 値変化を ReactiveEffect で購読
- サンプル 38: hover 切替えで色 + scale が補間 (color 0.25s + scale 0.15s を別 duration で並列)、vk/dx 完全一致

### TR-M2 完了 (P.Transition.* 添付プロパティ)
- **`TransitionSpec`** (Duration/Curve/Delay) + **`TransitionKeys`** 定数 (Transition.Color/Opacity/Translation(X/Y)/Scale(X/Y)/Rotation)
- **`P.Transition.Color(dur, curve, delay)`** 等のファクトリ ─ Grid.Column と同じパターン (`extension(PRoot)` で UI 本体無変更で拡張)
- **`TransitionAttachment : IConfigPart`** ─ Luxel.UI の `AttachedPart` (value が外部公開されていない) を回避する自前型、`Spec` を public で公開
- **`WidgetTransitions.Wrap<T>(parts, key, rawSetter, player, clock)`** ─ 添付パーツから spec を抽出 → ある場合 `Transition.Animate` でラップ、無ければ raw setter をそのまま返す
- サンプル 39: 2 カードに各々違う transition 群 (`P.Transition.Color`/`Scale`/`TranslationY`) を宣言、hover 状態切替えで宣言的に補間、vk/dx 完全一致

### 残スコープ
- **TR-M3 (任意)**: CSS `transition:` 構文パーサ

### 残スコープ (Animation 全体)
- **AN-M3b**: glTF JSON Importer (別プラン化済み)
- **Lottie subset Importer**: `IAnimationImporter` 拡張点でサードパーティ実装可
- **MESH_PLAN.md** (将来): skinned mesh + morph target
- **PARTICLES_PLAN.md** (将来): GPU 駆動パーティクル (Animation の `IAnimationTarget` を上から使う)

## プロジェクト構成

| プロジェクト | 役割 |
|---|---|
| `src/Luxel` | バックエンド非依存の公開 API (`GpuDevice`, `GpuBuffer`, `GpuPipeline`, ...) と `IGpuBackend` 抽象 |
| `src/Luxel.Vulkan` | Vulkan バックエンド (Silk.NET) |
| `src/Luxel.D3D12` | DirectX 12 バックエンド (Vortice) |
| `src/Luxel.TwoD` | 2D ベクター (compute ラスタライザ) + 保持型ツリー |
| `src/Luxel.UI` | 宣言的 UI フレームワーク基盤 (DSL + signals + レイアウト + 入力 + テーマ/フォーカス/オーバーレイ/スクロール/アニメ/テキスト編集) |
| `src/Luxel.Controls` | コントロールカタログ (React 系相当: 入力/表示/レイアウト/ナビ/オーバーレイ) |
| `src/Luxel.Platform` | 実ウィンドウ (Win32) + スワップチェーン提示 + TSF IME (Windows 専用, CsWin32) |
| `src/Luxel.RenderGraph` | レンダーグラフ (scene-agnostic, Setup/Compile/Execute, 自動バリア + transient プール) |
| `src/Luxel.ThreeD` | 3D + 最小 ECS (World/Components/Systems) + Render3DExtractor + 固定キューブメッシュ |
| `src/Luxel.Animation` | アニメーションコア (scene-agnostic): Curve/Tween/Animatable/AnimationPlayer/TrackEntry + AnimationClip/Track/Keyframe + Animate DSL |
| `src/Luxel.Animation.UI` | Animation の UI Adapter (`Signal<T>` ターゲット, `UiHost.Tick` 結線) |
| `src/Luxel.Animation.ThreeD` | Animation の ECS Adapter (`EcsAnimationTarget`、LocalTransform の TRS 分解再合成) |
| `src/Luxel.Animation.TwoD` | Animation の RetainedCanvas Adapter (`RetainedCanvasAnimationTarget`、UiNode の Transform/Color/Opacity を部分更新で書込み) |
| `src/Luxel.Samples` | 実行可能デモ |
| `tests/Luxel.Tests` | CPU ロジックの単体テスト |
| `shaders/` | `.slang` ソースと MSBuild コンパイルターゲット |

## 実装状況

今回のスコープは完了。全機能が **Vulkan / DirectX 12 の両バックエンドで動作・検証済み**
(全10サンプル実行 = 5機能 × 2backend、+ 単体テスト 3/3)。

- [x] **M1** 抽象 + ソリューション基盤
- [x] **M2** Vulkan 基盤 (instance/device、BDA・descriptor-indexing・dynamic rendering・sync2、固定レイアウト)
- [x] **M3** `gpuMalloc` (3 メモリ種別) + compute + サンプル01 (`out[i] == in[i]*2`)
- [x] **M4** graphics + dynamic rendering + vertex pulling + サンプル02 (三角形)
- [x] **M5** D3D12 バックエンド (Vortice, GPU_UPLOAD, bindless table, fence) — サンプル01/02
- [x] **M6** bindless テクスチャ + サンプラ — サンプル03 (Y フリップ吸収でピクセル一致)
- [x] **M8(深度/ブレンド)** 深度テスト + アルファブレンド — サンプル04/05

### 今回スコープ外 (将来拡張)
- ウィンドウ / スワップチェーンによる実画面表示 (現状はオフスクリーン描画→読み戻しで検証)
- M7 メッシュシェーダ
- M8 残り: 特殊化定数 (specialization constants)
- メモリは現状バッファ 1 個ごとに `DeviceMemory` を確保 (サブアロケータは未実装)

### 統一シェーダ (サンプル01)
生 64bit ポインタは DXIL 非対応のため、**明示的な bindless 配列 + inline ルート引数**で
1 ソースに統一した (`compute01.slang`):
- バッファは `g_buffers[index]` で参照 → Vulkan は set0/binding0 の storage buffer 配列、
  D3D12 は u0/space1 の unbounded UAV テーブルに lower。indexは `GpuBuffer.BindlessIndex`。
- ルート引数は push 定数 (Vulkan) / root 32bit 定数 (D3D12) で inline 渡し。
- DXIL は `-validator-version 1.7` で出力 (DXC 既定版は OS ランタイムに拒否される)。
- C# のサンプルコードもバックエンド分岐なしの 1 本。

詳細プランは `~/.claude/plans/` の該当ファイルを参照。

## API スケッチ (サンプル01)

```csharp
using var device = new GpuDevice(VulkanBackend.Create());
using var input  = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);
using var output = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);
using var root   = device.Malloc((ulong)sizeof(RootArgs), GpuMemoryKind.HostMapped);

input.Span<float>(n)[i] = ...;                       // CPU マップに直接書き込み
root.Span<RootArgs>(1)[0] = new RootArgs {           // ルート引数 = GPU アドレス
    Input = input.DeviceAddress, Output = output.DeviceAddress, Count = n };

using var pipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute01"));
using var cmd = device.MainQueue.StartCommandRecording();
cmd.SetComputePipeline(pipeline).SetRootArguments(root)
   .Dispatch((n + 63) / 64)
   .Barrier(GpuStage.ComputeShader, GpuStage.All);
cmd.Finish();
device.MainQueue.SubmitAndWait(cmd);
// output.Span<float>(n) を読み戻して検証
```

## セットアップ: tools/ (リポジトリ非含)

シェーダビルドに standalone Slang + DXC が必要です (`slang-llvm.dll` が GitHub の 100MB 制限を超えるため
リポジトリには含めていません)。`tools/slang/` に Slang リリースを展開し、DXIL 出力用に
`dxcompiler.dll` / `dxil.dll` を `tools/slang/bin/` へコピーしてください。
