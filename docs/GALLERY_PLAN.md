# Luxel.Gallery プラン — Storybook 風コントロールカタログ

## ゴール

ブラウザで Luxel.Controls の全コントロールを **閲覧・操作・検証** できるカタログ。

- **Story** = コントロールの特定状態の実例 (`Button/Primary`, `Button/States`, `CheckBox/Checked` …)
- **Knobs** = `[UiParam]` プロパティをブラウザから実行時編集 → 部分更新で即反映
- **States** = Hover/Pressed/Focused/Disabled/Checked をトグルで強制
- **Docs** = XML summary + パラメータ表 (ジェネレーターのメタデータから自動)
- **Snapshot 回帰** = 全ストーリーを offscreen PNG 化して golden 比較 (vk/dx、Chromatic 相当)

## 既存資産との対応 (ほぼ揃っている)

| Storybook の概念 | Luxel の既存資産 | 追加で必要なもの |
|---|---|---|
| Story registry (CSF) | ソースジェネレーター基盤 (Luxel.UI.Generators) | `[Story]` 属性 + 収集・列挙の生成 |
| Controls (knobs) panel | `DebugProps`/`SetDebugProp` 焼き込み + `ui.set` + index.html の prop editor (color/number/bool/enum ドロップダウン) | ルート widget を対象にした流用のみ |
| Canvas プレビュー | DevTools の frame 配信 (`DiagUiFrame`/`/uiframe`, Sample94 の offscreen render→readback) | ストーリー単位の GalleryHost |
| クリック/キー/IME | index.html の透明 `#kbd` + `POST /cmd` 転送 | 流用 |
| pseudo-states 強制 | `Widget.Hovered/Pressed/Focused` は public Signal | signal を直接セットするコマンド 1 つ |
| テーマ切替 | `UiTheme.Current` signal (recolor 部分更新) | トグル UI のみ |
| Docs (argTypes) | ジェネレーターが ctor 引数 / [UiParam] / XML summary を既に解析 | メタ JSON の出力を追加 |
| Chromatic (VRT) | offscreen PNG + vk/dx ピクセル一致検証 (プロジェクトの流儀) | golden 管理と verify コマンド |

## ストーリーの書き方 (authoring API)

```csharp
using static Luxel.Controls.Kit;

public static class ButtonStories
{
    [Story("Button/Primary")]
    public static Widget Primary() => Button("OK", () => { });

    [Story("Button/Variants", Width = 480, Height = 120)]
    public static Widget Variants() => HStack(8)[
        Button("Filled", () => { }),
        Button("Tonal",  () => { }).WithVariant(Variant.Tonal),
        Button("Ghost",  () => { }).WithVariant(Variant.Ghost)];

    // signal が要るコントロールは StoryContext から。ctx.Signal(...) は自動で knob になる
    [Story("CheckBox/Basic")]
    public static Widget Check(StoryContext ctx)
        => Check(ctx.Signal("checked", false), "Subscribe");
}
```

- `StoryAttribute(string path)` — `"コンポーネント/ストーリー名"` の 2 階層。任意で `Width`/`Height`(既定 480×320)、`Theme`("light"/"dark")。
- 署名は `static Widget M()` または `static Widget M(StoryContext ctx)`。
- **`StoryContext`**: `Signal<T> Signal(string name, T initial)` — 生成した signal を knob として登録 (bool→チェックボックス, int/float→number, string→text)。Storybook の args に相当。ストーリー再選択で作り直し。
- **収集はソースジェネレーター** (reflection なし、プロジェクトの流儀): `[Story]` 付きメソッドを走査し、アセンブリごとに `StoryRegistry.Register(path, size, theme, invoker)` を `[ModuleInitializer]` で焼き込む。

### 自動ストーリー (M4)

ジェネレーターは全 `[UiComponent]` の ctor 引数型を知っているので、既定値を合成できるものは `"<Component>/Auto"` を自動生成する:

- `string` → `"Sample"` / `BindableString` → `"Sample"` / `Action` → `() => { }`
- `Signal<bool>`/`Signal<int>`/`Signal<float>`/`Signal<string>` → StoryContext 経由で knob 化
- `string[]` → `["Alpha","Beta","Gamma"]` / `Widget` → `Text("content")` / `Widget[]` → 同上
- 合成できない ctor 引数があるコントロールはスキップ (手書きストーリーで補う)

→ 手書きゼロでも 27 コントロールの大半がカタログに並ぶ。

## ランタイム構成

```
src/Luxel.Gallery/            (net9.0 console, ref: Controls/UI/TwoD/Luxel/Vulkan/D3D12/DevTools)
  Program.cs                  -- dotnet run --project src/Luxel.Gallery -- vk [port]
  StoryAttribute.cs           -- [Story] / StoryContext / StoryRegistry (実行時側)
  GalleryHost.cs              -- 選択中ストーリーの UiHost+RetainedCanvas を構築・描画
  GalleryServer.cs            -- HTTP 配信 (DebugServer と同じ HttpListener/loopback 流儀)
  wwwroot/gallery.html        -- フロント (index.html の canvas/kbd/props パターンを流用)
  Stories/*.cs                -- 手書きストーリー (ButtonStories, OverlayStories, ...)
```

- **GalleryHost**: Sample94 の DevLoop 相当。選択中ストーリー **1 つだけ** を実体化 (UiHost + RetainedCanvas + host-mapped fb)。ループ = `cmds.Drain()` → Tick(アニメ) → 変更あれば Render → frame 配信。knob/テーマ/状態変更は signal 経由なので部分更新、構造変更 (story 切替/リサイズ) だけ作り直し。
- **GalleryServer** エンドポイント:
  - `GET /stories` — `{ component, stories: [{id, title, w, h}] }[]` のツリー
  - `POST /cmd` — `story.select {id}` / `story.state {state, on}` (root の Hovered/Pressed/Focused signal を直接セット) / `story.theme {dark}` / `story.resize {w,h}` / 既存流儀の `click`/`key`/`char`/`compose`/`commit` / `ui.set` (knobs)
  - `GET /frame?rev=` — 8B ヘッダ + RGBA (DebugServer と同形式, 304 対応)
  - `GET /props` — ルート widget の `DebugProps` ツリー + StoryContext knobs
  - `GET /docs?id=` — ジェネレーター出力のメタ (summary / ctor 引数 / [UiParam] 一覧)
- **状態強制**: `Hovered/Pressed/Focused` は基底の public Signal なのでコマンドでセットするだけ (エンジン変更ゼロ)。`Enabled` は `story.state` で bool を書く。`Checked` はコントロール内部 signal なので StoryContext knob で扱う。

## フロントエンド (gallery.html)

```
┌─────────────┬──────────────────────────┬───────────────┐
│ 検索         │  [Light|Dark] [480×320▾]  │ Knobs         │
│ ▾ Button    │                          │  background ■ │
│    Primary  │      ┌────────────┐      │  rounded  [6] │
│    Variants │      │   canvas   │      │  width  [...] │
│ ▾ CheckBox  │      │ (frame poll│      │ States        │
│    Basic    │      │  + #kbd)   │      │  □Hover □Prs │
│ ▸ Select    │      └────────────┘      │ Docs          │
│ ...         │                          │  summary+表   │
└─────────────┴──────────────────────────┴───────────────┘
```

- サイドバー: `/stories` からツリー構築 + テキスト検索
- canvas: index.html と同じ putImageData ポーリング + 透明 `#kbd` で click/hover/キー/IME 転送 (実装流用)
- Knobs: index.html の `renderUiPropRow` (color picker / number / checkbox / enum select) をルート widget (path="0") に適用 + StoryContext knobs
- States: トグル群 → `story.state`
- Docs: `/docs` のメタ表示

## スナップショット回帰 (M5)

- `dotnet run --project src/Luxel.Gallery -- vk snap [--update]`
  - 全ストーリーを既定サイズ・Light テーマで offscreen render → `goldens/<component>/<story>.png`
  - `--update` なしは比較モード: 不一致でストーリー名 + diff 画像を出力、exit 1 (CI 用)
  - vk/dx 両方で撮って相互一致も検証 (プロジェクトの既存流儀)
- アニメ系 (Spinner/Accordion) は `Tick` を固定ステップで N 回進めて決定的に

## マイルストーン

| M | 内容 | 検証 |
|---|---|---|
| **GB-M1** | `[Story]` 属性 + ジェネレーター収集 (StoryGenerator を Luxel.UI.Generators に追加) + StoryRegistry + GalleryHost + 最小サーバ (`/stories`, `story.select`, `/frame`) | 単体テスト (registry 生成) + Invoke-WebRequest で stories/frame 取得 |
| **GB-M2** | gallery.html: サイドバー/canvas/入力転送/テーマ切替/サイズ変更 | 実 Chrome で Button/CheckBox ストーリーを操作 (クリック/hover) |
| **GB-M3** | Knobs (`/props` + `ui.set` 流用) + StoryContext signal knobs + States 強制 | knob で色変更→部分更新 (styleWrites>=1)、Hover 強制→状態レイヤ発火を HTTP 検証 |
| **GB-M4** | 自動ストーリー (ctor 既定値合成) + Docs パネル (メタ JSON) | 27 コントロール中の自動生成数を確認、手書きで穴埋め |
| **GB-M5** | スナップショット回帰 (`snap`/`--update`, vk/dx) + 検索 + README | goldens 生成 → 再実行で全一致、意図的変更で検出 |

コアは M1–M3 (これだけで「動く Storybook」)。M4–M5 は拡充。

## リスク・設計判断

- **ctor 引数はライブ編集不可** (構造的): knobs は `[UiParam]` と StoryContext signal のみ。ctor バリエーションはストーリーを分けて表現 (Storybook も同じ割り切り)。
- **1 ストーリーのみ実体化**: 全ストーリー同時レンダは GPU/メモリ浪費。サムネイルが欲しくなったら「選択時に撮った PNG をキャッシュ」で代替。
- **preview MCP はこのマシンでポート bind 不可** (既知) → 検証は Invoke-WebRequest + 実 Chrome。
- **DevTools との関係**: サーバ/フロントのパターンは流用するが **プロジェクトは分離** (Gallery はカタログ、DevTools は実行中アプリの診断)。共有したくなった部品 (LatestSlot/frame 配信) は必要になった時点で Luxel.DevTools から公開する。
- **ジェネレーターの置き場**: [Story] 収集は既存 Luxel.UI.Generators に同居 (シンボル走査基盤を共有)。Gallery プロジェクトにアナライザ参照を追加。
