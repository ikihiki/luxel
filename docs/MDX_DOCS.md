# MDX 風 docs ページ (2026-07-04 完了)

Storybook の MDX 相当 — **補完文字列に markdown を書き、hole にライブ UI を置く** docs ページ。

## 書き方

```csharp
[Story("Docs/Button", Width = 800, Height = 480)]
public static Widget ButtonDocs(StoryContext ctx) => Docs($"""
    # Button

    ボタンは **Variant × Intent** で配色が決まります。バージョン: {version}

    {Button(_ => ctx.Log("clicked"), "触ってみて")}

    ## バリエーション

    {StoryRef(ctx, "Button/Variants")}
    """);
```

- リテラル部分 = markdown (見出し/強調/リスト/引用/コードブロック/テーブル)
- `Widget` の hole → その場に**ブロックレベル**のライブ UI (キャンバス枠付き、クリック可・状態も生きる)
- `StoryRef(ctx, path)` (Gallery 側ヘルパ) → 他ストーリーの埋め込み (キャプション付き。Log/knob は
  docs ページに合流)。パス不明は Alert カード
- `Signal<T>` / その他の値の hole → テキスト補完 (**構築時評価、非リアクティブ**)
- サイズは**与えられた領域いっぱい** (HAlign/VAlign = Stretch)。内容が溢れたら内部スクロール

## 実装 (既存 embed 基盤にそのまま乗せる)

- **`DocString`** ([InterpolatedStringHandler] class, Luxel.Controls/Docs.cs):
  リテラルを連結し、Widget hole を `` ```luxel-ui `` フェンス (hole 連番) に置き換え + holes リスト保持。
  overload 解決 (具体型 > ジェネリック) で `AppendFormatted(Widget)` / `(Signal<T>)` / `<T>(T)` を振り分け
  — BindableString と同じ手法。
- **`Kit.Docs(DocString)`**: 専用 MarkdownFormat (UiFenceResolver — `luxel-ui` フェンスだけ embed 昇格) +
  BlockWidgetRegistry ("luxel-ui" → holes[N] を SurfaceAlt のキャンバス枠 Border で包む) を組んだ
  **ReadOnly RichTextEditor** を返す。ドキュメントモデルへの直接挿入 API は作らない —
  LiveCodeBlock と同じ IFenceResolver + BlockWidgetRegistry 経路。
- **`RichTextEditor.ReadOnly`** (新規): FocusRing/FocusTarget/AddHit (選択/編集/コンテキストメニュー)/
  キャレット点滅を登録しない。スクロールと埋め込み widget の操作だけ残る — RenderBlock の描画資産
  (見出し/リスト/コード/テーブル/embed) を表示専用でそのまま使う。
- **RichTextEditor の可変高**: `VAlign == Stretch && MaxH 有限` なら領域いっぱい (実効高 `H`)、
  それ以外は従来の ctor `height` (既存呼び出し不変)。

## 直したバグ: Center が無限制約で子を潰す

埋め込み (縦積み文脈) は高さ ∞ の制約でレイアウトされるが、`Center` は「∞ なら 0」に潰していた →
子ボタンが高さ 0 に押し込まれ、**文字 (Measure 由来の Scene) は見えるのに背景矩形 (Size 由来) が消える**
という紛らわしい見た目になる。修正: 無限の軸は**子の自然サイズにフォールバック** (有限側は従来どおり
領域いっぱい)。`Frame(...)` を使う全ストーリーが docs に埋め込めるようになった。

## 制約 (v1)

- hole は**ブロックレベルのみ** (embed がブロック単位のため)。文中インライン UI は非対応
- テキスト hole は非リアクティブ (構築時の値が焼き込まれる)
- 埋め込みストーリーの knob 名が docs 側と衝突したら後勝ち
- snap (offscreen) は日本語フォールバックフォントなしのため golden の日本語は豆腐 (決定的なので回帰は有効)

## ゲート

- テスト 407 (+DocsTests 3: ブロック分割/テキスト焼き込み/hole 解決と順序)
- snap 54/54 (vk/dx、Docs/GettingStarted + Docs/Button 追加 — 両バックエンドで --update 済み)
- 実窓 E2E: docs 内ライブカウンタのクリック → Log 反映 + 値更新、StoryRef 埋め込み (Variants/Intents)
  の完全描画、ページが 800×480 の領域いっぱいに表示

## 後続 (MD-NL, 2026-07-04): 改行を改行として扱う (空行の保存)

- 従来: 段落内の単一改行はブロック分割で表示に出るが、**空行 (段落区切り) は Markdig の AST から
  消える**ため表示に反映されず、エディタの再ロードでも空行が消えていた。
- Markdig パイプラインに **UseSoftlineBreakAsHardlineBreak** を適用 (段落内の単一改行をハード改行として扱う公式拡張 — 写像は LineBreakInline で分割するため表示は同じだが、AST の意味が正しくなる)。
- **Markdown.Parse がトップレベルブロック間の空行を空段落として復元** (Markdig ブロックの
  Line/Span からソース行の隙間を計算)。1 ソース行 = 1 表示行 (空行含む) の行指向モデルが完成し、
  空行を含む文書の round-trip が安定した。**末尾の改行 1 つだけは行終端** (空行にしない)。
- シリアライザ: テーブル前後の強制空行は**隣が空段落なら重ねない** (増殖防止)。
- DocString: フェンスの前後に空行を足すのをやめ**行境界のみ保証** — docs の要素間隔は
  書き手のソースがそのまま決める。
- 対象外: リスト/引用の**内側**の空行 (コンテナ 1 個の span に畳まれる — 従来どおり正規形で消える)。
- ゲート: テスト 409 (+空行保存 2、テーブル round-trip 期待値更新)、snap 54/54
  (Docs ×2 + MarkdownEditor/Embeds の golden 更新 — 空行が表示に出る期待どおりの差分)。

## 後続 (MDX2+CE, 2026-07-04): Emoji & SmartyPants / カラー絵文字 / リンク / TOC

### Markdig 拡張 (M1a)
- `UseEmojiAndSmiley` (:smile:/:+1: → 絵文字) + `UseSmartyPants` ("..." → “…”、--/--- → –/—。
  省略記号 ... は Markdig 非対応)。SmartyPant inline は写像に case 追加 (無いと黙って消える)。
- round-trip は正規化 1 回で収束 (変換後の文字が書き戻される)。

### カラー絵文字 (CE)
- **COLR v0 + CPAL の自前パーサ** (VectorFont.ColorGlyphs — base glyph 二分探索 + パレット 0)。
  `TryGetColorLayers(glyphId)` → レイヤ列 (通常輪郭グリフ + パレット色。0xFFFF = テキスト色)。
- **Scene2D の Shape.AbsoluteColor** — 保持型キャンバスの「1 ノード 1 色」をシェイプ単位で opt-out。
  混色ノード (ContentColors or AbsoluteColor 含み) は形状別スタイルレンジを持ち、
  非対象パスはノード色のまま (テーマ recolor は白描きテキストにだけ効き、絵文字色は不変)。
- テキスト出力: カラーグリフはレイヤ毎に色付き AbsoluteColor パスとして展開 (グリフ線分キャッシュ共用)。
- フォールバック連鎖 (JpFallback) に seguiemj.ttf。COLR v1 (グラデーション) は対象外。

### リンク (M2)
- ReadOnly docs のみ: Link run の矩形 (SelectionRects) をブロック Container ローカルでヒット登録
  (スクロール追従)、cursor = Hand。`#アンカー` は内部で `ScrollToAnchor` (見出しスラグ照合 —
  `Slug()` = 小文字 + 空白→ハイフン)、それ以外は `[UiEvent] OnLink` (sender-first)。
- `StoryContext.Navigate(path)` (ホスト設備窓口): GalleryHost = story.select コマンドへキュー、
  GalleryApp = _pendingNav → Update で SelectByPath — **入力ディスパッチ中の即時 TearDown をしない**。
- `Docs(ctx, ...)` 糖衣が `story:` → Navigate / 未知スキーム → Log を配線。http(s) は v1 無効。

### TOC (M3)
- `Docs(..., toc: true)` — H2/H3 を**アンカーリンク付き markdown リスト**として最初の H1 直後に挿入。
  embed でなくただの markdown なので、エディタのフォント (日本語/絵文字) とリンク機構がそのまま効く
  (最初 Button 製 TOC にしたら ctx.Font で日本語が豆腐 → この方式に変更)。

### 直したバグ: value-sync effect の正規化差再発火
- エディタの外部変更検知が `_value != SerializeForValue()` 比較だったため、絵文字ショートコード等の
  **正規化差で恒常不一致** → effect が Refresh 中に読んだ signal (_scroll 等) に購読し、
  **スクロールするたびに巻き戻す**バグ (アンカースクロールで顕在化)。
  「最後に取り込んだ/書き出した値 (_synced)」比較へ変更 — Sync() も _synced を先に更新する。

### ゲート
- テスト 418 (+SmartyPants/round-trip 2、カラー絵文字 4、Slug/OnLink 配線/TOC 3)
- snap 58/58 (vk/dx — Docs ×2 の golden 更新: 絵文字/引用符/リンク/TOC)
- 実窓 E2E: docs 内 `story:` リンククリック → ストーリー遷移 (タイトル変化)、`#` アンカー →
  スクロール (scroll=156 反映をフレーム差分で確認)、カラー絵文字 😄🚀👍 のフルカラー描画

## 後続 (SH, 2026-07-04): コードブロックのシンタックスハイライト

- **ライブラリ採用 (自前トークナイザではない)**: TextMateSharp + TextMateSharp.Grammars (MIT、
  VS Code と同じ .tmLanguage 文法 + native onig)。依存は **Luxel.Highlight.TextMate** アセンブリに
  閉じ込め、`RichTextEditor.Highlighter` (ISyntaxHighlighter — Luxel.Document の純契約) へ注入。
  scope 名 (`keyword`/`string`/`comment`/`constant.numeric`/`entity.name.type`) → 粗い TokenKind へ
  写像し、**色は Luxel の Theme が決める** (TokKeyword/TokType/TokString/TokNumber/TokComment を
  Light/Dark に追加 — VS Code テーマは使わない)。
- **解析は別スレッド**: HighlightQueue (プロセス共有の単一ワーカー)。描画は即時単色 → ワーカー完了 →
  結果キュー → 毎フレームのドレイン (AddAnimation) → キャッシュ格納 → **該当ブロックだけ**
  作り直し (KeyOf に |H を含め、既存のブロック差分機構に乗せる)。文法初回ロードもワーカー上。
- キャッシュは (lang, text) キー — タイプで編集中ブロックだけ再依頼、同一コードの再表示は同期ヒット。
- **snap の決定性**: Snapshot はストーリー選択後 `HighlightQueue.WaitIdle()` → **dt=0 で 2 Step**
  (ドレイン+再描画のみ — アニメ時間を進めるとアニメ系 golden が全部ズレる。実際に踏んだ)。
- 対応言語: TextMate 文法のエイリアス表 (cs/csharp, json, js/ts, py, rust, cpp, hlsl, md, yaml, ...)。
  未対応言語・言語なしフェンスは従来どおり単色。
- 適用: docs (Docs/Button の ```csharp)、RichTextEditor/Basic (```cs)、MarkdownEditor/Embeds。
- **SH2 (細分化)**: TokenKind を VS Code (Dark+/Light+) が実際に色分けする粒度の **15 種**へ拡張
  (Comment/String/Escape/Regexp/Number/Constant/Keyword/KeywordControl/Operator/Function/Type/
  Variable/Tag/Attribute + Text)。scope 写像は VS Code のテーマ規則と同一対応 (keyword.control=紫、
  entity.name.function=黄 等)、Theme のトークン 14 色は Light+/Dark+ の実値。VS Code で同色のもの
  (parameter/property = variable) は統合。
- ゲート: テスト 424、snap 58/58 (vk/dx — RichTextEditor/Basic golden: var=青/変数=紺/数値=緑/
  演算子=灰)、bench タイプ 120f (full rebuild 10%, 3.2ms/rebuild)。
- 注意: Docs/Button のコードブロックは表示域の下なので golden 不変 (デバッグで追った結果、機構は
  全て正常だった — 「pass = 動いていない」ではない)。

## 後続 (TV, 2026-07-04): TreeView コントロール + サイドバーのツリー化

- **汎用 TreeView** (`Luxel.Controls/TreeView.cs`): `TreeNode(Key, Label, Children, Tag)` record +
  `TreeView : CompositeControl`。行 = インデント + シェブロン (子持ちのみ) + ラベル Button。
  **子持ちノードのクリック = 開閉**、葉のクリック = `OnSelect` (sender-first)。
  選択 (`Selected` Key 一致) は子持ち/葉とも Tonal ハイライト。
- **展開状態は呼び出し側が所有**: ctor の `ISet<string> expanded` に保持 — chrome 再構築で
  TreeView が作り直されても展開状態が生き残る (GalleryApp は `_treeExpanded` HashSet フィールド)。
  開閉トグルは内部 `_version` signal → TrackBuild で TreeView だけ再構築。
- **サイドバー 3 階層**: Component (`g:` プレフィックスキー) → Story (Key=Path, Tag=StoryInfo) →
  見出し (選択中ストーリーのみ、Tag=ScrollTo クロージャ)。onSelect は Tag でディスパッチ。
  初回のみ全 Component を展開 (`_treeInit`)、ストーリー選択時はその Path を展開して見出しを見せる。
- ゲート: テスト 428 (+Flatten 4 — 折りたたみ/深さ/祖先未展開/葉キー無害)、snap 58/58 (vk/dx 不変 —
  chrome のみの変更)、実窓 E2E (ラベルクリック開閉・ストーリー選択・見出しクリックでスクロール)。
- 注意 (E2E): `/winframe` は **arm→次リクエストで取得**のため直近操作の反映は 1 リクエスト遅れる。
  「クリックが効かない」ように見えたら取得を 2 回重ねてから判断する (実際に誤診しかけた)。
- Luxel.Controls に `InternalsVisibleTo("Luxel.Tests")` を追加 (Flatten を internal のままテスト)。

### TV 追補 (LinkText 化, 2026-07-04)
- ユーザーフィードバック「子要素が右に寄りすぎ・スペースがありすぎ。ボタンでなく LinkText に」
  → **LinkText コントロール新設** (`Luxel.Controls/LinkText.cs`): 背景・余白なしの左寄せクリック
  テキスト。通常 TextMuted → hover で Text へ 80ms フェード、`active:` で Primary (選択中)、
  Hand カーソル。既定フォント = FontSm。
- TreeView の行を Button → LinkText へ: 1 段 = 12px インデント、葉はシェブロン列 12px 分を足して
  親ラベルと左端を揃える。VStack(3) の詰まった行間。Button の中央寄せ+padding が
  「右に寄りすぎ・スペースありすぎ」の正体だった (hAlign: Stretch で伸びた幅の中央にラベルが出る)。
- ゲート: テスト 428、snap 58/58 (vk/dx 不変)、実窓 E2E (選択 Primary 表示/開閉/見出しスクロール)。

## 後続 (SR, 2026-07-04): Docs 全文検索 (検索バー + ツリー絞り込み + 本文ハイライト)

- **RichTextEditor 検索 API**: `SetSearchHighlight(query)` (大小無視の部分一致、null/空で解除) /
  `SearchNext()/SearchPrev()` (折り返し + 現在マッチへスクロール) / `SearchMatchCount/SearchCurrent/Realized`。
  マッチ矩形は選択と同じ `Layout.SelectionRects` 方式の専用ノード 2 枚 (全マッチ = Warning 薄、
  現在マッチ = 濃)。**Z=3 でブロックの前面に置く** — コードブロックは自前の地色矩形を持つため
  背面 (選択と同じ Z=1) だと隠れる。半透明なので文字は透けて読める (蛍光マーカー)。
  Refresh 毎に引き直し (実体化前に設定されたクエリも初回 Refresh で反映 + 保留スクロール)。
- **DocsIndex** (Gallery): 起動時に全ストーリーを使い捨て StoryContext で **Build だけ** (実体化
  しない = GPU/Effect が走らず安価、7 ページ 203ms) → `FindDocEditor` で本文 + 見出し (TOC) を
  回収して `path → DocsPage(Text, Headings)` 辞書に。docs を持たないストーリーはスキップ。
- **ツリー統合**: 全ページの TOC を TreeView の子項目に常設 (Tag = (StoryInfo, ブロック index) —
  未選択ページの見出しクリックは Select + `_pendingScroll` で実体化を待って ScrollTo)。
  `TreeNode.SearchText` に docs 本文を添付し、`TreeView.Filter` (BindableString) が
  Label/SearchText の部分一致で**ヒットしたページ + 祖先だけを全展開表示** (FilterTree、
  開閉セットは触らない = クリアで復帰)。
- **選択できる子持ちノード**: ページに TOC が付いたので「子持ちクリック = 開閉」を変更 —
  **Tag 付き子持ちはラベル = 選択 + 展開、開閉はシェブロン** (Icon に `OnClick` [UiEvent] を追加、
  ハンドラがあるときだけヒット登録)。Tag なし (Component 見出し) は従来どおりラベルで開閉。
- **検索バー** (サイドバー上部): TextField + ‹ (前へ) / n/m / › (次へ)。クエリ signal は TextField と
  TreeView.Filter が**共有** — タイプ毎に TreeView だけが再構築され (TrackBuild)、chrome 再構築なし
  = 検索欄のフォーカスが落ちない。docs への適用は GalleryApp.Update の SyncSearch (クエリ/ルート参照の
  変化検知 — 同一パス再選択でもルートは変わるため参照比較)。
- ゲート: テスト 436 (+FindMatches 4 / FilterTree 4)、snap 58/58 (vk/dx 不変)、実窓 E2E
  ("variant" 入力 → ツリーが 2 ページに絞れる → ページを開くと蛍光ハイライト + 1/5 →
  次へ×3 でコードブロック内 4/5 へスクロール → クリアで全復帰 → 未選択ページの見出しクリックで
  ページ遷移 + 該当節へスクロール)。

## 後続 (KT, 2026-07-04): Knobs の autodoc 風テーブル + StoryRef の knobs 指定

- **StoryKnob 拡張**: `ctx.Signal(name, initial, description)` — 説明が `StoryKnob.Description` に
  載る。`SetText(string)` は型別に JSON へ寄せて書き込み (int/float/bool は TryParse、
  失敗は FormatException — Pump 側が無視)。
- **編集の effect 安全化を StoryContext へ一般化**: `QueueKnobEdit(knob, value)` +
  `PumpKnobEdits()` (ホストのフレームループが毎フレーム呼ぶ — GalleryApp.Update / GalleryHost.Step)。
  エディタの commit は Reactive.Effect 内で走るため、signal 直書きせずキュー経由にする既存規約を
  ホスト非依存にした (GalleryApp ローカルの knob キューは廃止)。
- **KnobsTable コントロール** (Luxel.Controls): 名前 | 型 | 説明 | 操作 の 4 列 (Storybook の
  ArgsTable 相当)。操作列は型別エディタ (bool=Check / color=ColorPicker / int,float=規制付き
  TextField / string=TextField)、編集は `OnEdit` → 受け手が QueueKnobEdit。幅は ctor 引数
  (説明列が残り、最小 50)。右パネルは `_rightW` 既定を 224→360 に拡大 (4 列が収まる幅)。
- **StoryRef(ctx, path, knobs: true)**: 埋め込みストーリーの下に Divider + KnobsTable。
  **その Build が登録した knob だけ**を `ctx.Knobs.Count` の前後差分で切り出す (docs ページ全体の
  knob と混ざらない)。同じ knob は右パネルにも出る (同一 ctx) — 両方から編集でき、同期する。
- デモ: GettingStarted に「埋め込み + Knobs」節 (2D/Orbit を knobs: true で埋め込み) —
  Docs/GettingStarted golden 更新 (vk/dx、TOC 1 行 + 新節)。
- ゲート: テスト 440 (+Knobs 4 — Description/SetText 型寄せ/キュー適用タイミング/不正値無視)、
  snap 58/58、実窓 E2E (右パネル speed=0 → 軌道静止を 1.5 秒差 2 フレームで確認、
  埋め込みテーブル speed=0 → 埋め込み側も静止 + 右パネルと値同期)。

### KT2 追補 (enum / Length knob, 2026-07-04)
- `ctx.Signal("align", Align.Start, ...)` (任意の enum) → 型ヒント **"enum:A|B|C"** (DebugProps と
  同形式)、書き込みは名前の大小無視 TryParse (不正名は無視)。`ctx.Signal("width", new Length(320,
  LengthUnit.Px), ...)` → 型ヒント **"length"**、CSS 風文字列 ("120px" "50%" "1.5em") で往復。
  どちらも Coerce<T> を通らない専用 get/set (Coerce は enum/Length 非対応)。
- KnobsTable の操作列: enum = **Select** (候補は型ヒントから)、length = **LengthField**
  (数値 + 単位)。型列は "enum:..." の候補列挙を出さず "enum" と短縮 (候補は Select で見える)。
- **Knobs/Kinds ストーリー新設** (golden +1 = 59): 全 7 型 (bool/int/float/string/color/enum/length)
  を 1 画面で。値はサマリテキスト (getter = リアクティブ) とチップの色/アルファに直結。
  右パネルの Knobs 欄は 120→260px (テーブル 7 行分)。
- ゲート: テスト 442 (+enum/length の型ヒント・SetText・不正値無視 2)、snap 59/59 (vk/dx、
  Knobs/Kinds 追加)、実窓 E2E (Select で align=Center → サマリ反映、LengthField で width=200 → 反映)。

## 後続 (DW, 2026-07-04): エンジン解説の執筆に向けた不足解消 (①画像 ②順序 ③外部リンク)

- **①docs 画像**: `Kit.Docs(ctx, ...)` が `ctx.ResourcesOrNull` (新設 — nullable 版) を見て
  `ImageBlocks.Factory` を配線 — markdown の `![alt](src)` が Resource システム経由で表示される。
  あわせて `TableBlocks.Factory` も基本 overload で配線 (markdown テーブルの embed 表示)。
  snap の決定性はストーリー側で画像を同期 preload (Embeds ストーリーと同じ手口)。
- **②ページ順序**: `[Story(Order = n)]` (既定 1000)。StoryGenerator が焼き込み、StoryRegistry.All が
  「コンポーネント = 所属ストーリーの最小 Order → 名前 / 内部 = Order → Path」で整列。
  Docs 章を先頭に (GettingStarted=0, Button=1) — サイドバーが「説明書が最初」の並びになった。
- **③外部リンク**: docs の http(s) リンクを既定ブラウザで開く (ShellExecute、失敗は Log)。
  `story:`/`#` は従来どおり。GettingStarted に Aaltonen ブログへの参考リンクを追加。
- ゲート: テスト 443 (+Order 整列 1)、snap 59/59 (GettingStarted golden 更新 — 外部リンク行 +
  画像ブロック)、実窓 E2E (画像表示 / ツリー先頭が Docs / 外部リンククリック → Log "open:" +
  ブラウザ起動)。

## 後続 (DW4, 2026-07-04): コントロール API テーブル (autodocs の ArgTypes 相当)

- **ControlApiRegistry** (Luxel.UI): `ControlApi(Name, Summary, ApiMember[])`、ApiMember =
  名前/型 (短縮表示)/種別 (ctor|event|param)/説明//// 由来/Stateable/Inherited。
- **ジェネレーター焼き込み**: WidgetDebugGenerator が [UiComponent] 毎に module initializer で
  Register を生成。フィールド/イベント/ctor 引数の /// summary・&lt;param&gt; を CleanDoc
  (see cref → 名前、タグ剥がし) して埋め込む。**CLI ビルドは DocumentationMode.None で
  GetDocumentationCommentXml が空になる** → Luxel.UI / Luxel.Controls に
  GenerateDocumentationFile=true (+NoWarn CS1591) を追加して解決。
- **ApiTable コントロール**: `{ApiTable("Button")}` で コンストラクタ引数/イベント/パラメータ の
  3 節テーブル (名前|型|説明、Stateable は「(状態対応)」表示)。既定は自身のメンバーのみ、
  `inherited: true` で Widget 共通も。未登録名は Alert。
- **DocString の罠を修正**: hole の静的型がコントロール型 (ApiTable ファクトリ戻り値等) だと
  ジェネリック `AppendFormatted<T>(T)` が Widget オーバーロードに勝ち (恒等変換 > 基底変換)、
  **ToString のテキスト補完になる** — 実行時に `value is Widget` を拾って UI hole へ振り直した
  (これまでの hole は全て Widget 型変数だったため潜在していた)。
- ゲート: テスト 445 (+ControlApi 2 — Button の説明/種別/Stateable/Inherited、TreeView の ctor/短縮型)、
  snap 59/59 (Docs/Button golden 更新 — TOC に API 行)、実窓 E2E (API 節にクラス summary +
  OnClick/全パラメータが /// 説明付きで表示)。

## 後続 (DW5, 2026-07-04): 中優先の執筆機能 — Markdig 拡張の棚卸しから

- **Markdig 1.3.2 の拡張調査**: 採用 = UseAlertBlocks (コールアウト) / UseCjkFriendlyEmphasis
  (日本語文中の **強調** が効く)。不採用 = UseDiagrams (mermaid フェンスの認識のみで描画しない —
  図は自前フェンス resolver の領域) / UseMathematics (AST 化のみ、低優先のまま)。
- **コールアウト**: `> [!NOTE]` (NOTE/TIP/IMPORTANT/WARNING/CAUTION) → Block.Callout +
  CalloutMarker (ラベル行)。表示は意味色バー (4px) + 色付き太字ラベル (NOTE=Info/TIP=Success/
  IMPORTANT=Primary/WARNING=Warning/CAUTION=Danger、色キー CInfo..CDanger を追加)。
  シリアライズはマーカーが `> [!KIND]` へ戻り round-trip 安定。
- **storysource**: StoryGenerator が [Story] メソッドのソース (Dedent 済み) を `StoryInfo.Source` に
  焼き込み。docs では **`{StorySource("path")}` (DocMarkdown = 生 markdown hole)** がコードフェンス
  として差し込む — ページ本体のハイライトがそのまま効く。
  **教訓: 埋め込み (embed) の中に RichTextEditor を入れ子にすると空白描画になる** (原因未特定の
  既知制限) — 生 markdown 注入方式へ切替えて回避。DocString.AppendFormatted(DocMarkdown) 新設。
- **リンク検証**: `LinkCheck.FindBroken(blocks, storyExists)` (Luxel.Document 純ロジック —
  #アンカー は同一文書見出し、story: は解決関数、http は対象外)。DocsIndex.Build が全ページへかけ
  `[gallery] dead link in 'page': url` を警告 + 集計をログ。
- **全画面 (zen)**: ツールバー「全画面/元に戻す」— 右パネル + Log を隠し、プレビュー内容を
  メイン全面サイズで再実体化。**SurfaceView の論理サイズはサーフェス (ctor 固定 framebuffer) が
  上限** — サーフェスを最初から全画面サイズ (1092×760) で確保し、通常時は論理サイズを
  ストーリー宣言値に (余白は透過なので見た目不変)。
- ゲート: テスト 451 (+コールアウト 2 / CJK 1 / LinkCheck 3)、snap 59/59 (Docs/Button golden 更新 —
  TIP コールアウト + storysource)、実窓 E2E (コールアウト配色 / storysource ハイライト表示 /
  全画面トグル往復 / dead link 0 ログ)。

## 後続 (DG, 2026-07-04): Luxel.Diagram — mermaid サブセットのダイアグラム

- **独立プロジェクト Luxel.Diagram** (UI/TwoD/Document のみ参照、Controls 非依存):
  MermaidParser (flowchart|graph LR/RL/TB/TD/BT、`id[矩形]`/`id(角丸)`/`id{ひし形}`、
  `A --> B` / `A -->|ラベル| B`、%% コメント、**対応外の行は無視** — 全記法は追わない) +
  DiagramLayout (最長パスのランク法、ランク内は宣言順、循環安全、計測は関数注入 = 純ロジック) +
  DiagramBlock widget (Scene2D 描画、色 5 グループで recolor、幅超過は等比縮小)。
- **docs 配線**: Kit.Docs に `fences:` 引数 (追加 IFenceResolver)。Gallery は
  DocsFences = [MermaidFenceResolver] + `doc.Widgets.Register("mermaid", ...)` (WithDocFonts)。
  FencePayload なので round-trip は ```mermaid フェンスのまま。
- **罠**: `$"""` 補間文字列内で `{{ }}` は書けない (CS9006 — 生文字列に波かっこエスケープはない)
  → docs デモのひし形は `( )` に変更、ひし形は Diagram/Basic ストーリー (非補間 """) で回帰。
- ゲート: テスト 456 (+パース/形/後勝ちラベル/ランク進行/循環/空 5)、snap 60/60
  (Diagram/Basic golden 新規 — vk/dx)、実窓 E2E (GettingStarted 内 ```mermaid 描画)。

## 後続 (MA, 2026-07-04): Luxel.MathText — 数式 ($ インライン / $$ ブロック)

- **方針: インラインとブロックで割り切りを変える**。インライン `$...$` は **Unicode 正規化**
  (Document/TexText — ギリシャ/演算子コマンド + 単一トークンの ^/_ を上付き下付き文字へ、
  変換不能は原文のまま)。**パース時に一度だけ焼き込む** (emoji/SmartyPants と同じ —
  表示/検索/シリアライズのオフセットが常に一致、round-trip は 1 回で収束)。
  InlineStyle.Math フラグ + シリアライズは `$正規化済み$`。
- **ブロック `$$...$$` = 独立プロジェクト Luxel.MathText の本格組版**: TexParser (サブセット —
  ^ _ {} \frac \sqrt \vec \begin{matrix|pmatrix|bmatrix} & \、置換表は TexText と共有、
  未知コマンドは \cmd のまま) → MathLayoutEngine (ボックス = W/H/Baseline、script 0.7 倍、
  数式軸 0.30px、**計測/描画プリミティブは関数注入 = 純ロジックでテスト可**) →
  MathBlockView widget (グリフ + ストローク描画: 分数線/根号/括弧、テーマ Text recolor、幅超過は等比縮小)。
  Document 側は MathPayload (embed "math"、シリアライズは `$$` 記法へ)。
- **罠 (再発)**: TeX の `{ }` は `$"""` の補間と衝突 — docs のデモは DocMarkdown hole
  (生 markdown) で差し込む。
- ゲート: テスト 461 (+TexText/インライン round-trip/ブロック round-trip/TexParser/組版 5)、
  snap 61/61 (Math/Basic 新規 + GettingStarted 更新 — vk/dx)、実窓 E2E (インライン E=mc² と
  $$ 行列+分数+根号が docs 内で描画)。

## 後続 (IN, 2026-07-04): インライン widget 差し込み ({widget:inline})

- **TextLayout インラインボックス** (Typography): `SpanStyle.BoxW/BoxH` — > 0 のスパンは
  グリフを描かず占位 (疑似グリフ 1 つ、advance = BoxW、Px/Ascent = BoxH で行高へ寄与、
  下端 = ベースライン)。テキストは占位 1 文字 (U+FFFC)。`LineAscentAt(index)` 追加 (縦位置決め用)。
- **RichTextEditor.InlineWidgetResolver** (`Func<string, Widget?>`): Link run の URL を解決して
  widget を返すとリンクではなく**行内 widget** に — BuildLayout で計測して占位スパン化し、
  RenderBlock 後に `SelectionRects(off, off+1)` の矩形へ実体化 (embed と同じ再ホスト方式)。
  **インスタンスは hole 所有者のもの — Dispose せず Scope 解放のみ** (再レンダーで同一インスタンス
  が実体化し直され状態が生きる)。RegisterLinkHits は resolver が解決する URL をスキップ。
- **DocString `{widget:inline}`**: C# 補間の書式指定 (`AppendFormatted(Widget, string format)`) —
  内部はリンク記法 `[￼](luxel-ui:N)` (既存のリンク run 経路に乗るため Document 層は無変更)。
  Kit.Docs が resolver を配線。
- 制約 (v1): IME 合成中のブロック/hybrid ソース展開中は占位が消える (単一プレーンスパン化のため)。
  docs (ReadOnly) では影響なし。
- 執筆メモ: `$"""` 内の `{ }` は **`$$"""` にすれば literal** (hole は `{{ }}`) — DocMarkdown 回避策
  より先にこちらを検討する (ユーザー指摘)。
- ゲート: テスト 464 (+ボックス占位/inline 記法/ブロック hole 不変 3)、snap 61/61 (vk/dx 不変 —
  デモ文はビューポート下)、実窓 E2E (行内の Badge/Button 表示 + ベースライン揃え + 折り返し +
  インラインボタンのクリック → Log "inline click")。

## 後続 (LX, 2026-07-04): 製品名を Luxel へ (NoGfx から一括リネーム)

- 由来: **luxel = ライトマップの 1 画素** (lux + el、実在のレンダリング用語)。NuGet `luxel` は空き (0 件)。
- 変更範囲: ディレクトリ/csproj/slnx (Luxel.slnx)/namespace/ジェネレーターの文字列リテラル
  ("Luxel.UI.StoryAttribute" 等の**文字列照合**があるため機械置換必須)/DiagnosticListener 名 ("Luxel")/
  fence TypeId・インラインスキーム (`luxel-ui`)/InternalsVisibleTo/shaders targets/docs 全文。
- 罠: **Visual Studio が開いていると** ディレクトリリネームがロックされ、design-time build が
  旧パスへ obj だけの殻ディレクトリを再生成する — `dotnet build-server shutdown` + 殻削除 +
  (ロック継続時は) 子ファイルの Move で回避。
- ゲート: フルソリューションビルド 0 エラー、テスト 464 (Luxel.Tests)、snap **61/61 vk/dx
  ピクセル完全一致** (描画は不変の証明)、実窓スモーク (タイトル "Luxel Gallery"、docs/検索/knobs 動作)。
