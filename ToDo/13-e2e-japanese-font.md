# 13 — e2e の日本語表示: 同梱フォントの導入

## 概要

e2e (golden 生成・比較) で日本語が表示されない問題を直す。再配布可能な日本語フォントをダウンロードしてリポジトリに配置し、e2e がそれを使うようにする。副次効果として **golden がマシンのインストール済みフォントに依存しなくなる** (別マシン/CI/新しい Windows でも同じピクセルが出る)。

**注意: 全 golden が再生成になるタスク** — 他タスクで golden を増やす前、早い段階で片付けるほど差分が小さく済む。

## 背景と現状 (原因の特定済み)

フォントの供給経路が実窓と e2e で食い違っている:

| 経路 | ホストフォント | 日本語 |
|---|---|---|
| 実窓 Gallery ([src/Luxel.Gallery/Program.cs:72](../src/Luxel.Gallery/Program.cs)) | `VectorFont.LoadSystemJapanese()` | 出る |
| **e2e ランナー ([src/Luxel.Gallery/Program.cs:31](../src/Luxel.Gallery/Program.cs))** | **`VectorFont.LoadSystem()` (ラテンのみ)** | **出ない** |
| **dotnet test フィクスチャ ([tests/Luxel.E2e.Tests/E2ePlayTests.cs:77](../tests/Luxel.E2e.Tests/E2ePlayTests.cs))** | **`VectorFont.LoadSystem()` (ラテンのみ)** | **出ない** |

- `VectorFont.LoadSystem(params string[])` ([src/Luxel.Typography/VectorFont.cs:57](../src/Luxel.Typography/VectorFont.cs)) は Windows フォントフォルダから候補名を探す。既定候補は arial/segoeui/consola/… (ラテン)。`LoadSystemJapanese()` は yumin.ttf / YuGothM.ttc / meiryo.ttc / msgothic.ttc / BIZ-UDGothicR.ttc。
- ホストフォント (GalleryHost ctor の `VectorFont`) が StoryContext 経由でストーリーの基本フォントになる — **基本フォントで描く日本語 (Button ラベル、CodeEditor 内の日本語コメント等) が e2e で欠落する**。
- 日本語を明示的に使うストーリーは `StoryKit.JpFallback` ([src/Luxel.Gallery/Stories/StoryKit.cs](../src/Luxel.Gallery/Stories/StoryKit.cs)) = `FontCollection(LoadSystem(), LoadSystemJapanese(), [seguiemj])` を `WithDocFonts` / `.Fonts =` で挿している — これは**システムフォント依存** (マシンが変わると golden が割れる。JP フォントが無い環境では throw)。
- `StoryKit.EditorFaces` (太字/斜体/等幅) も segoeui*/consola のシステム依存。

## 方針

### 1. フォントの選定とダウンロード

**推奨: Noto Sans JP (SIL OFL 1.1 — 再配布可、ライセンスファイル同梱が条件)**

- 取得元: https://github.com/notofonts/noto-cjk (Sans/OTF/Japanese) または Google Fonts。static の `NotoSansJP-Regular.otf` と `NotoSansJP-Bold.otf` (各 ~5MB)。variable TTF 1 本でも可だが、VectorFont (HarfBuzz + glyf/CFF) が variable に対応しているか未確認なので **static を選ぶのが安全**。
- 等幅 (CodeEditor 用): 同リポジトリの `NotoSansMonoCJKjp-Regular.otf` (OFL)。ラテン等幅 + 日本語を 1 フォントでカバー。
- 代替 (容量を絞るなら): BIZ UDGothic Regular/Bold (OFL、https://github.com/googlefonts/morisawa-biz-ud-gothic 、各 ~3MB)。ただし斜体なし。
- **OFL.txt (ライセンス全文) を必ず同じフォルダに置く**。README.md (リポジトリルート) にもフォントのライセンス表記を 1 行追記。

### 2. 配置場所: `assets/fonts/` (リポジトリにコミット)

- `assets/` は既にストーリー用画像 fixture (assets/sample-sparkline.png) で使われており、e2e は cwd = リポジトリルートで走る (RepoRoot.Ensure 済み) ので相対パスで解決できる。
- **tools/ には置かない**: tools/ は gitignore 済みで worktree に junction が要る罠がある。フォントは数 MB でコミット可能 (100MB 制限に遠い) なので、リポジトリに入れて「clone すれば e2e が通る」状態にする方が良い。
- 構成例:
  ```
  assets/fonts/NotoSansJP-Regular.otf
  assets/fonts/NotoSansJP-Bold.otf
  assets/fonts/NotoSansMonoCJKjp-Regular.otf
  assets/fonts/OFL.txt
  ```

### 3. コードの結線

- `VectorFont` に同梱フォントローダを足す (または Gallery 側ヘルパ `GalleryFonts`):
  ```csharp
  // 探索: cwd/assets/fonts → AppContext.BaseDirectory 相対 (RepoRoot.Ensure 後なら cwd で足りる)
  public static VectorFont LoadBundled(string fileName);
  ```
- **Noto Sans JP はラテンもカバーする** → ホストフォント 1 本で日本語まで出る (実窓の LoadSystemJapanese 1 本方式と同型)。差し替え箇所:
  1. Program.cs の e2e 分岐 (L31): `LoadSystem()` → `LoadBundled("NotoSansJP-Regular.otf")`
  2. E2ePlayTests.cs の GpuGalleryFixture (L77): 同上 (RepoRoot.Ensure 後なので相対パス可)
  3. `StoryKit.JpFallback`: `FontCollection(LoadBundled(Regular), [システム絵文字 seguiemj])` — システム JP フォントへの依存を除去 (ラテンも Noto がカバーするので LoadSystem() 先頭も不要になる。見た目の変化は golden 更新で吸収)
  4. `StoryKit.EditorFaces`: Bold = LoadBundled(Bold)、Mono = LoadBundled(Mono)。Italic は Noto Sans JP に無い — 当面システム (segoeuii) 維持か null (合成斜体は無いので「斜体は golden 上ラテンのみ」の現状維持で可、Docs に注記)
  5. **実窓 Gallery (Program.cs:72) も同じ同梱フォントへ** — 実窓と golden の見た目を一致させる (推奨。実窓だけ游明朝のままだと「実窓で確認 → golden が別の字形」のずれが残る)
- 単体テスト (tests/Luxel.Tests) の `LoadSystem()`/`LoadSystemJapanese()` は golden 比較をしないので**当面そのまま**で良い。マシン非依存にしたければ TextLayoutTests の `Jp()` だけ LoadBundled へ (任意)。

### 4. 日本語表示の e2e テストを追加

導入の目的はここ: 日本語がちゃんと描けていることを golden で担保する。

- 既存の日本語入りストーリー (TextControl 系、Strudel/Repl、Docs ページ群) は差し替えだけで日本語が写るようになる。
- 加えて**専用ストーリー** (例: Controls/Text/Japanese) を 1 本足す: 基本フォント直 (Button ラベル「ボタン」等) + FontCollection フォールバック + CodeEditor 内の日本語コメント + IME 経由入力 (play の `d.Type("日本語")` — ITextInput 実装の InsertAtCaret 経路) を 1 画面に。play: Snap → Type → Snap "typed" → Expect (Text に日本語が入った)。
- 「豆腐でも golden は通ってしまう」対策: golden の絵に日本語が写っていることを **--update 時に目視確認**する (PNG を開く)。以後は golden 比較が回帰を防ぐ。

### 5. golden の全再生成

フォントが変わる = 文字メトリクスが変わる = **テキストを含む全 golden (実質ほぼ全部) が変わる**。

1. `dotnet run --project src/Luxel.Gallery -- vk e2e --update` (全更新で良い — 意図的な全変更)
2. 代表ページ (Docs 数枚 + 日本語ストーリー + CodeEditor) を目視: 日本語が出ている / 豆腐がない / レイアウト崩れ (フォント高さ変化で折り返し位置が変わる) がない
3. 2 回実行してハッシュ一致 (決定性)
4. dx も同様 (`-- dx e2e --update`) — [12](12-maintenance-docs-golden.md) の dx golden 未更新分もこのタイミングで一緒に解消するのが効率的
5. `dotnet test` 全緑

## 作業ステップ (まとめ)

1. フォント DL → `assets/fonts/` + OFL.txt 配置、ルート README にライセンス表記追記
2. `LoadBundled` 実装 + 差し替え 5 箇所 (e2e ランナー / テストフィクスチャ / JpFallback / EditorFaces / 実窓)
3. 日本語表示の専用ストーリー + play
4. golden 全再生成 (vk → dx)、目視確認、決定性確認
5. Docs (Docs/Gallery or Docs/Contributing の e2e 節) に「フォントは assets/fonts/ の同梱 Noto を使う。システムフォントに依存しない」を追記

## 罠・注意

- **OTF (CFF アウトライン) 対応の確認が先**: VectorFont のアウトライン抽出は `GlyfOutlines` (glyf テーブル) — **CFF ベースの OTF だと glyf が無く読めない可能性が高い**。最初に `LoadBundled` で Noto の .otf を読んで描けるか確認し、ダメなら **TTF (glyf) 版を選ぶ**: Google Fonts 配布の NotoSansJP は TTF (variable)、static TTF は notofonts のリリースやサブセット版で入手可。BIZ UDGothic は TTF なので確実な代替。**ここが本タスク最大の分岐点なので手順 1 の前に必ず検証。**
- variable font の場合の既定インスタンス挙動も未検証 — static を優先する理由。
- FontCollection のフォールバック順は「先勝ち」— Noto を先頭にすると英数字も Noto の字形になる (意図どおり。golden 全更新で吸収)。
- 絵文字 (seguiemj) はシステム依存のまま — 絵文字ストーリーの golden は引き続きこのマシン専用。Noto Emoji の同梱は別判断 (COLR 対応形式の確認が要る) としてスコープ外。
- LoadSystem のフォールバック候補に BIZ-UDGothicR.ttc 等が既にある — LoadSystemJapanese 自体は残す (Platform/実アプリ向け API として有効)。Gallery/e2e だけ同梱に寄せる。
- golden 全更新のコミットは巨大 (200 枚級) — フォント追加・コード変更・golden 更新を 1 コミットにまとめ、メッセージに「全 golden 再生成 (同梱フォント化)」と明記。
- `snap --update` 系の既知の罠 (全 PNG 再エンコード) は今回は全更新なので気にしなくて良いが、**未コミットの他の意図分があると混ざる** — 作業前に working tree をクリーンに。

## スコープ外

- 絵文字フォントの同梱、単体テスト (Luxel.Tests) のフォント差し替え、variable font 対応、フォントのサブセット化 (容量最適化)。
