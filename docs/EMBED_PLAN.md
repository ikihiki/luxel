# 埋め込みブロック プラン (テーブル / 画像 / ライブコードブロック / 汎用 UI)

2026-07-03 起案。status: **全マイルストーン完了 (EX-M0〜M5, 2026-07-03)**。前提: EDITOR_PLAN (ED-M1〜M5) 完了 —
RichDocument (行指向ブロック列)・DocumentEditor (undo/スタイル/型変換)・RichTextEditor
(ブロック毎 TextLayout + 部分更新 + hybrid)・Markdig round-trip が既にある。

## 目的

**到達目標は Strudel REPL 的なライブコードブロック** — 文書内のコードブロックが
「編集できるコード + 実行環境 + ライブな出力 (ビジュアル/オーディオ)」として動く。
MDX 的にコードブロック内へコンポーネント定義をソースコードで書き、それが UI になるケースも
同じ機構に乗せる。そのための段階として:

1. **汎用 UI 埋め込み基盤** — 任意の Widget をブロックとして文書に入れられる拡張点。
   **フェンス記法の解釈はパーサー (アプリ登録のリゾルバ) が判断する** — エディタ側は
   記法を固定しない。
2. **テーブルブロック** — GFM pipe table と round-trip し、セル単位で編集できる。
3. **画像ブロック** — `![alt](src)` と round-trip し、画像を表示する。
   テーブルと画像は「この機構の最初の 2 つの標準実装」にすぎない。

## 設計の骨子

### 0. 文書フォーマット抽象 (IDocumentFormat) — **パーサーが文章をすべて管理する**

エディタは「ブロック列 (RichDocument) の編集と表示」だけを持ち、
**テキスト表現との往復・何が embed か・行の確定 (離脱時再パース)・記法の知識は
すべて文書フォーマット実装の責務**にする。Markdown は一実装に格下げ:

```
public interface IDocumentFormat
{
    RichDocument Parse(string source);
    string Serialize(RichDocument doc);
    string SerializeRange(RichDocument doc, DocPos min, DocPos max);   // コピー用

    // hybrid (アクティブブロックのソース編集) — 行指向フォーマットのみ対応
    bool SupportsHybrid { get; }
    Block ParseLine(string line);
    string SerializeBlock(Block b);

    // 入力オートフォーマット (行頭記法/フェンス確定) — フォーマットが判断。不要なら no-op
    bool TryAutoFormat(DocumentEditor ed, string inserted);
    bool TryBlockCommit(DocumentEditor ed);   // Enter 時のブロック確定 (フェンス開始等)
}
```

- `MarkdownFormat` (Markdig ベース、現行の Markdown クラスを移設) が既定実装。
  フェンス → embed の判定 (IFenceResolver、§2) は **MarkdownFormat の内部機構**。
- **Strudel 用フォーマットは markdown である必要がない**: アプリは `StrudelFormat` の
  ような独自実装を `RichTextEditor.Format` に差し、文書全体を自分の形式
  (例: コード + コメント区切り、あるいは全体が数個のライブコードブロック) で管理できる。
  エディタの機能 (キャレット/選択/undo/部分更新/埋め込み widget) はそのまま使える。
- `SupportsHybrid == false` のフォーマットでは hybrid は無効 (常に整形表示で編集)。
- RichTextEditor の値バインド (signal) も `Format.Serialize/Parse` 経由に一般化する —
  signal の中身は「そのフォーマットのソーステキスト」であって markdown とは限らない。

### 1. モデル: BlockKind.Embed + IBlockPayload (Document 層 — UI 非依存を維持)

```
BlockKind.Embed 追加
public interface IBlockPayload
{
    string TypeId { get; }        // "image" / "table" / アプリ定義 ("chart" 等)
    string Serialize();           // markdown 内表現の本体 (フェンス内テキスト等)
    IBlockPayload Clone();        // undo スナップショット用 (immutable 運用)
}
Block.Payload : IBlockPayload?   // Kind == Embed のとき有効。Clone() は Payload も複製
```

- **Document 層はデータのみ**を持つ (widget を持たない) — テスト可能性と層分離は現状維持。
- 内蔵 payload: `ImagePayload { Src, Alt }`、`TablePayload { string[][] Cells, Align[] }`
  (セルは v1 プレーン文字列。セル内インラインスタイルは将来枠)。
- **汎用 payload の canonical 形 = `FencePayload { string Info; string Body; }`** —
  フェンスの info 文字列とソースコード本文をそのまま保持する。strudel/MDX ケースでは
  **ソースコードそのものが第一級のデータ**であり、widget 側がそれを解釈/実行する。
  Serialize はフェンスを原文どおり書き戻すだけ (**リゾルバが居ない環境でも
  ただのコードブロックとして完全に保全される**)。

### 2. MarkdownFormat 内のフェンス → embed 判定 (IFenceResolver)

markdown を使う場合の embed 判定もパーサー側の機構: FencedCodeBlock (info, body) を
**MarkdownFormat に登録された IFenceResolver チェーン**へ渡し、embed に昇格するか
CodeBlock のままにするかをリゾルバが決める:

```
public interface IFenceResolver
{
    // 例: info == "strudel" → FencePayload / info 先頭 "table" → TablePayload / null = 昇格しない
    IBlockPayload? Resolve(string info, string body);
}
new MarkdownFormat { FenceResolvers = { ... } }   // フォーマットのインスタンス設定
```

- ` ```strudel ` や ` ```csharp live ` のような**普通の info 文字列**をアプリの流儀で解釈できる。
- リゾルバ未登録/不一致のフェンスは従来どおり CodeBlock — 既存文書は何も変わらない。
- 書き出しは常にフェンス原文 (Info + Body) — **round-trip はリゾルバの有無に依存しない**。
- 独自フォーマット (StrudelFormat 等) はこの機構すら不要 — Parse が直接 Embed ブロックを
  作ればよい。IFenceResolver は「markdown の中に埋める」場合の便法にすぎない。

### 3. markdown 表現 (round-trip)

| ブロック | 記法 | 備考 |
|---|---|---|
| 画像 | `![alt](src)` | 段落が画像 1 つだけのとき Image ブロック化 (インライン画像は対象外 v1) |
| テーブル | GFM pipe table | Markdig `UsePipeTables()` をパイプラインへ追加。書き出しは正規形 (整列パディングなし) |
| 汎用/ライブコード | 任意の info のフェンス | **リゾルバが判断** (上記)。他ツールではコードブロック表示 = 安全 |

### 4. 表示: BlockWidgetRegistry (Controls 層) + RichTextEditor の動的実体化

```
public sealed class BlockWidgetRegistry   // エディタ毎のインスタンス (ctor で差し込み)
{
    // TypeId → (payload, 使える幅, Commit, テーマ) から Widget を生成
    public BlockWidgetRegistry Register(string typeId, BlockWidgetFactory factory);
}
```

**表示解釈はパーサー (フォーマット) 固有** — プロセス共有の静的登録はしない。
専用フォーマット (Strudel 等) は「IDocumentFormat + 構成済み BlockWidgetRegistry」を
**対で生成するファクトリ関数**として配布し、解釈を固定する。markdown のような汎用
フォーマットではアプリ/ユーザーが自由に構成できる (どちらも同じ 2 つの ctor 引数に渡すだけ)。

- RichTextEditor.RenderBlock: `Kind == Embed` なら registry から widget を生成し、
  ブロックのコンテナノード配下へ**手動 Realize** (SurfaceView が既にやっている流儀)。
  高さ = widget の measure 結果。ブロック削除/再構築で widget を破棄
  (**IDisposable なら Dispose — ライブブロックは ここで再生停止/リソース解放**)。
- 未登録 TypeId の Embed は「フェンスコードブロック」として描く (現状と同じ見た目)。
- **任意 UI**: ファクトリは何を返してもよい — Sparkline、Button 列、SurfaceView (= 別 UiHost
  丸ごと) すら埋められる。エディタは高さと寿命だけを管理する。

### 5. ライブコードブロック (strudel REPL 型 — 到達目標のかたち)

ライブブロックの widget は典型的に **「コードエディタ + 実行制御 + ライブ出力」の複合**:

```
┌──────────────────────────────┐
│ ▶ / ■   strudel              │   ← 実行制御 (Button) + info 表示
│ ┌──────────────────────────┐ │
│ │ s("bd sd").fast(2)       │ │   ← コード編集 = 既存 TextArea (エディタ内エディタ)
│ └──────────────────────────┘ │
│ ▁▂▄▆█▆▄▂ (ビジュアル出力)     │   ← 任意の widget (Sparkline / SurfaceView / 自前描画)
└──────────────────────────────┘
```

- **コード編集**: 埋め込んだ TextArea が Body を編集し、確定 (フォーカス喪失 or 実行) で
  `onChange(new FencePayload(Info, 新 Body))` → ReplacePayload → 文書へ反映 (undo 可)。
  文書キャレットはブロック内に入らない (原子) — ブロック内のフォーカス/キーは
  埋め込み widget の FocusTarget が普通に受ける (同一 UiHost、タブ順にも乗る)。
- **実行**: 何をどう実行するか (DSL 評価 / Roslyn scripting / オーディオエンジン接続) は
  **完全にアプリ責務**。基盤が保証するのは (a) コード変更の往復、(b) 毎 Tick の駆動
  (ctx.AddAnimation)、(c) 破棄時の Dispose、の 3 点だけ。
- **MDX 型**: Body にコンポーネント定義ソースを書き、ファクトリが評価して UI を返す —
  機構上はライブコードブロックと同一 (出力 = UI そのもの、実行制御なし)。

### 6. 編集モデル: Embed = 原子ブロック

- キャレットは Embed の**前後にのみ**立つ (Divider と同じ扱い: offset 0 のみ)。選択は
  ブロック単位で跨げる。Backspace/Delete/選択削除でブロックごと消える (undo 可)。
- **内部編集は埋め込み widget 自身が行う** (テーブルのセル編集等)。文書への反映は
  `DocumentEditor.ReplacePayload(block, newPayload)` — Change() でジャーナルに乗せ、
  Version を bump (表示側の部分更新キー)。payload は immutable 運用 (Clone で差し替え)。
- hybrid (MarkdownEditor): Embed は CodeBlock と同じく**ソース展開の対象外**
  (フェンスは複数行 = 行指向が崩れる)。コピー (SerializeRange) はフェンス/記法全体。
- 挿入 UI: ツールバー/オートフォーマット (`![` や `|` 始まりは v2)。v1 はツールバー
  ボタン + `InsertEmbed(payload)` API。フェンス入力 (```` ```strudel ```` + Enter) は
  既存のフェンスオートフォーマット → コード入力 → **離脱時にリゾルバ再評価で embed 昇格**
  (hybrid の「離脱 = 確定」と同じ意味論)。

### 7. 画像の解決 (デコードの依存を Controls に持ち込まない)

- `ImagePayload` は src 文字列のみ保持。表示時に **IImageSource** (アプリ登録の抽象) が
  RGBA を解決 → 既存 ImageView (CPU RGBA → bindless) で表示。
- 標準実装 `FileImageSource` (ローカルパス、ImageSharp デコード) は **Luxel.Imaging 新設**
  (or Luxel.Gltf の ImageSharp 依存を移設) に置く。ネットワーク取得はスコープ外 v1。
- 解決失敗/未登録: alt テキスト + 枠のプレースホルダ描画 (データは保持)。

## マイルストーン

- **EX-M0: IDocumentFormat 抽象化** — 静的 Markdown クラスを `MarkdownFormat`
  (IDocumentFormat 実装) へ移設し、RichTextEditor の Parse/Serialize/hybrid/
  オートフォーマット呼び出しを `Format` プロパティ経由に一般化 (既定 = MarkdownFormat、
  既存挙動は不変)。TextArea はプレーンのまま (フォーマット無関係)。
  独自フォーマットの最小実証 (テスト用 PlainFormat: 全行 = 段落)。回帰 = 既存テスト全通過。
- **EX-M1: モデル + round-trip** — BlockKind.Embed / IBlockPayload / FencePayload /
  **IFenceResolver チェーン** (MarkdownFormat の設定)、Markdig UsePipeTables、
  Image/Table payload のパース + 正規形シリアライズ、リゾルバ不在時の保全
  (フェンス原文 round-trip)。単体テスト。
  **完了 (2026-07-03)。実装メモ:**
  - 画像判定は段落単位でなく **EmitInlineBlocks の行単位** (ソフト改行で 1 段落に畳まれるため)。
    「行全体が画像 1 つ」だけ昇格、文中画像は alt テキストに退化 (v1)。リスト/引用内は対象外。
  - テーブルのセルは **ソース原文を Span で切り出し** (ExtractText だとインラインマーカーが
    落ちて round-trip が壊れる)。正規形はパディングなし pipe、`\|` エスケープ往復。
  - 原子ブロック意味論 (DocumentEditor): Embed 上の Backspace/Delete = ブロック削除、
    直後行頭からの Backspace = Embed 削除 (結合しない)、タイプ/Enter = 直後の段落へ逃がす
    (EscapeEmbedForward — なければ作る)、選択削除は端の Embed を丸ごと削除、
    スタイル/型変換は素通し。InsertEmbed / ReplacePayload (undo 1 op)。
  - RichTextEditor は暫定プレースホルダ描画 `[typeid]` (EX-M2 で widget 実体化に置換)。
    hybrid のソース展開は CodeBlock 同様 Embed も対象外。
- **EX-M2: 部分 Realize 基盤 + 埋め込み widget** — 場当たりの除去 API でなく、
  **変更伝播 + 再実体化境界 (Angular change detection / Flutter markNeedsBuild 型)** を入れる:

  - **EX-M2a: RealizeScope (登録の所有権)** — Realize 中の登録 (UiNode サブツリー /
    Hits / Focusables / Scrollables / Animations / **Effect**) を widget 単位の階層スコープに
    紐付ける。UiBuildContext にスコープスタック (Realize で push/pop)、`ctx.Effect(...)` を
    新設して既存の `Reactive.Effect` 呼びを移行 (Effect は IDisposable — 捕捉して破棄可能に)。
    scope.Dispose() = 子スコープ再帰 + Effect 破棄 + 入力登録除去 + ノード除去。
    **SetRoot はルートスコープ破棄になり、既存の Effect リーク (SetRoot が古い Effect を
    破棄せず、全世代がテーマ変更等で走り続ける — 調査で確認済みの実バグ) をここで直す。**
    挙動不変の純基盤 + リーク修正。回帰 = 全テスト + snap。
    **完了 (2026-07-03)。実装メモ:** Widget.Realize は sealed テンプレート (PushScope →
    RealizeCore → 直下ノードを親 Children 差分で捕捉 → PopScope)。全 widget は
    `protected override void RealizeCore` へ機械リネーム (漏れはコンパイルエラー)。
    スコープは Parent ポインタで O(1) pop。Controls の Reactive.Effect は全て ctx.Effect へ
    (Realize 後に走る Refresh/RenderBlock 系は Effect を作らない既存作法のため対象外)。
    UiHost: SetRoot/Dispose で _build.Root.Dispose()。オーバーレイの Effect も build スコープへ。
    実地確認: ストーリー切替 5 連発 (SetRoot 多発) 後の編集/描画健全 + snap 46/46。
  - **EX-M2b: 部分 Realize** — widget ごとに (constraints, 親ノード, worldOrigin, scope) を
    Realize 時に記録し、`UiHost.ReRealize(widget)`: scope 破棄 → 保存 constraints で
    Layout → 同じ位置へ Realize し直す。**再実体化境界 = レイアウトが tight constraints で
    親に影響しない widget** (embed ブロックコンテナが該当)。その上に BlockWidgetRegistry +
    RichTextEditor の Embed 実体化 (payload 変更 → その embed だけ ReRealize、IDisposable 破棄)、
    原子キャレット/選択/削除の表示、離脱時のリゾルバ再評価。サンプル embed (Sparkline) +
    ストーリー + snap。
    **完了 (2026-07-03)。実装メモ:**
    - Widget.LastConstraints (Layout 記録) / RealizedParent / RealizedOrigin (Realize 記録)。
      `UiHost.ReRealize(w)` = scope.Release → 同制約 Layout → 同位置 Realize、フォーカスは
      FocusTarget 参照で可能なら維持。RealizeScope.Release = Dispose + 親から切り離し
      (親の一括 Dispose 再帰中は呼ばない — 列挙破壊)。`ctx.Own(IDisposable)` 新設。
    - RichTextEditor: BlockView.Embed。RenderBlock が登録済み TypeId の Embed を factory →
      Layout (幅 = エディタ内側、高さ自由) → Container 配下へ Realize。差し替え/再構築時は
      DestroyEmbed (scope.Release + IDisposable.Dispose)、SetRoot 越えは ctx.Own で担保。
      Commit は Block **参照**から index を都度解決して ReplacePayload (編集で index が動くため)。
      **エディタの AddHit は Refresh より前に登録** — embed のヒットが後勝ち (前面) になる。
    - **Markdig の罠: pipe table は前後に空行がないと段落の継続に食われる** — 正規形は
      テーブルの前後に空行を出す (空行は再パースで消えるので round-trip 不変)。
    - E2E 済: ```chart フェンス → Sparkline 実体化、embed クリック (原子キャレット) →
      Backspace 削除 → Ctrl+Z で **widget ごと復活 (再実体化)**。未登録 table/image は
      プレースホルダ。テスト 369 / snap 47/47 (vk/dx)。
  - **EX-M2c: dirty 伝播** — `Widget.MarkNeedsRealize()` が親方向へバブリングし、
    「サイズが変わらない変更は自分が境界 / 変わるなら親へ」を自動判定、UiHost の Tick 頭で
    まとめて部分 Realize。ルートまで届いたら SetRoot に縮退。ListView/TextArea/
    RichTextEditor の手動ノード管理をこの機構へ移行する将来リファクタの土台。
    **完了 (2026-07-03)。実装メモ:**
    - **Widget**: `ParentWidget` (Realize テンプレートが RealizeScope.Owner 経由で自動記録。
      イベントから実体化する手動ホストは明示設定 — RichTextEditor が embed に設定)、
      `MarkNeedsRealize()` (= `UiBuildContext.MarkDirty`、未実体化は no-op)、
      `protected internal virtual OnChildNeedsRealize(child)` (吸収点。別アセンブリの
      override は `protected` に落とす — CS0507)。
    - **UiHost.FlushRealize** (Tick 頭 + 直接呼び出し可): dirty snapshot を処理 —
      ①破棄済みスコープ (`RealizeScope.IsDisposed`) はスキップ ②祖先も dirty なら包含スキップ
      ③直近制約で再レイアウトし**サイズ不変 → ReRealize (自分が境界)**
      ④変化 → 祖先の `OnChildNeedsRealize` へ ⑤誰も吸収しなければ SetRoot 縮退 (残りも包含)。
      処理中の再 Mark は次フレームへ (無限ループ防止)。
    - **フォーカス保存 = FocusTarget 再利用**: `ctx.AddFocusable(既存 FocusTarget)`
      オーバーロード新設。widget が FocusTarget を初回 Realize で作りフィールド保持 →
      以後の Realize で同一インスタンスを再登録すると、UiHost の参照保持フォーカスが
      再実体化をまたいで生き残る (TableBlock が採用)。
    - **RichTextEditor.OnChildNeedsRealize**: embed を**同一インスタンスのまま**同じ
      Container へ再ホスト (scope.Release → Layout → Realize)、高さ変化は後続ブロックの
      transform 平行移動で吸収 (エディタ自身は固定サイズ = 内側スクロールなので親へは
      伝播しない)。factory 再呼び出しをしないので widget の内部状態が保持される。
    - **TableBlock 移行**: 行追加/削除を「即 Commit → factory 再構築 (選択喪失)」から
      `MarkNeedsRealize()` へ — **セル選択・キャレット・フォーカスが行増減をまたいで生き残り、
      文書への Commit は blur 時 1 op** (ローカルコピー + blur 確定の設計と一貫)。
    - E2E 済 (実窓): セルクリック → Enter 行追加 → **新行に選択+キャレットが生存** →
      そのままタイプ ("gamma" が新セルへ = フォーカス生存) → blur で 1 op Commit →
      Ctrl+Z で行追加+入力がまとめて復元。テスト 376 / snap 48/48 (vk/dx 不変)。
    - **後続移行も完了 (2026-07-03)**:
      - **ListView**: `SetItems` の手動行再構築 (ノード除去/追加 + Invalidate) → `MarkNeedsRealize()`。
        サイズ固定なので自分が境界、1 フレーム内の多重 SetItems は 1 回に畳まれる (従来は毎回
        即再構築)。スクロール位置はフィールドなので生存。E2E: gallery Log パネル (クリック →
        ctx.Log → SetItems → 行表示)、行クリック選択。
      - **ImageBlock**: ロード完了通知を `BlockWidgetContext.Invalidate` (factory 再構築 +
        ResourceHandle 再取得) → `MarkNeedsRealize()` (同一インスタンス再ホスト) へ。
        `invalidate` ctor 引数を削除。Invalidate API 自体は「状態を捨てて作り直す」用に存置
        (doc に使い分けを記載)。E2E: hybrid で `![alt](src)` をタイプ → 離脱で embed 化 →
        プレースホルダ → ロード完了 → 実寸へ再ホスト (実窓)。
      - MarkdownEditor/Embeds ストーリーに `HybridSource = true` (名前どおりの挙動に。
        golden 1 枚更新 — 見出しがソース表示になるだけ)。
      - **TextArea/RichTextEditor 本体は移行しない (判断)**: キーストローク毎のブロック単位
        差分 (Version キー) はテキスト編集のホットパスで、widget 再実体化より細粒度で速い。
        M2c は「構造変化 (embed/行増減) の状態保存」が適所 — embed 経路は移行済み。

  SurfaceView は「逃げ道」ではなく **描画キャッシュ + 別 canvas 境界** (毎フレーム動く
  重い embed 向けのオプション) に位置づけ直す。
- **EX-M3: 画像ブロック** — IImageSource + FileImageSource (Luxel.Imaging)、
  ImageView ベースの画像 embed (等比縮小、失敗時プレースホルダ)、`![alt](src)` round-trip、
  ストーリー + snap。
  **完了 (2026-07-03)。実装メモ — 独自 IImageSource は作らず Resource システムへ委譲 (ユーザー指示):**
  - **Luxel.Imaging 新設**: `ImageSharpDecoder : IResourceStep<byte[], CpuImage>`
    (png/jpg/bmp/gif/webp/tga)。ImageSharp 依存はここに隔離。アプリが ResourceSystem の
    steps に足すと `Load<CpuImage>("x.png")` が通る (file/http は組込み Source)。
  - **ImageBlock widget (Controls)**: 取得/デコード/キャッシュ/寿命は
    `ResourceSystem.Load<CpuImage>` (URI キャッシュ + RefCount、Dispose で参照返却)。
    widget はハンドル表示のみ — Ready なら CpuImage → HostMapped バッファ → image ノード
    (等比、幅 = min(使える幅, 実寸))。ロード中/失敗は alt 付きプレースホルダ帯。
    完了検知は **Tick ポーリング** (Ready 継続はプールスレッドのため) →
    `BlockWidgetContext.Invalidate` (新設 — undo に乗らない表示だけの再構築) で実寸へ。
    Controls → Luxel.Resources 参照追加 (Resources は Diagnostics のみ依存で循環なし)。
  - 登録: `widgets.Register("image", ImageBlocks.Factory(resources))` — フォーマット構成側の責務。
  - **snap の決定性**: 非同期ロードは 1 フレーム描画に間に合わない — ストーリーは
    Resource を同期 preload してから build する (実アプリでは不要)。
- **EX-M4: テーブルブロック** — pipe table round-trip、テーブル widget: グリッド描画
  (BorderColor 罫線 + ヘッダ地色)、**セルクリックで in-place 編集** (単一セルの TextField 相当、
  Tab/矢印でセル移動、Enter で下のセル)、行/列の追加/削除 (端のホバー UI)、
  ReplacePayload 経由の undo。ストーリー + snap + E2E。
  **完了 (2026-07-03)。実装メモ:**
  - TableBlock (Controls): グリッド描画 (罫線 + ヘッダ帯 SurfaceAlt + 列整列 `:---:` 対応)。
    セル編集は TextEditor ベースの in-place — **ローカルコピー上で編集し、フォーカス喪失時に
    1 op として Commit (ReplacePayload)** — Tab/Enter のセル間移動で undo が刻まれない。
    Tab/Shift+Tab/Enter/↑↓ でセル移動、最下段 Enter/末尾 Tab で行追加、空行の先頭
    Backspace で行削除、Esc で取消。ITextInput 実装 (編集中セル = TSF 文書、日本語入力可)。
    **列の追加/削除 UI は将来枠へ変更** (v1 は行操作のみ — キーで完結)。
  - **UiHost のフォーカスを index → 参照保持へリファクタ** — Focusables は embed 差し替えで
    増減し、index 保持だと除去でズレて誤配送する (Current() が登録済みかを検証)。
    行追加/削除で widget が作り直されると選択状態は失われる (v1 制限 — EX-M2c の
    dirty 伝播でフォーカス/状態保存を扱う)。
  - E2E 済: セルクリック → in-place 編集 (End/Backspace/タイプ) → blur で Commit →
    Ctrl+Z で復元。テスト 371 / snap 47/47 (vk/dx、テーブル widget golden)。
- **EX-M5: ライブコードブロック (strudel 型) の実証** — 「コードエディタ (TextArea) +
  ▶/■ 実行制御 + ライブ出力」の複合 widget サンプルを作り、基盤 3 保証 (コード往復 /
  Tick 駆動 / Dispose) を実証する。出力例は簡易 DSL → Sparkline 波形 (オーディオエンジン
  接続はアプリ側の続きとして残す)。**markdown でない独自フォーマットの実証**:
  StrudelFormat 風の実装 (文書全体 = ライブブロック列 + コメント段落、markdown 記法なし) で
  同じエディタが動くことを確認。エディタ内エディタのフォーカス/undo 干渉の E2E。
  公開 API 手順のドキュメント + EMBED_PLAN 完了記録。
  **完了 (2026-07-03)。実装メモ (Gallery/Stories/LiveCodeStory.cs — アプリが書くものの見本):**
  - **LiveScriptFormat** (非 markdown): `--` 行 = コメント段落 / それ以外の行 = ライブブロック。
    **TryBlockCommit = 「パターン行で Enter → その行を ConvertToEmbed でライブブロック化」**
    (DocumentEditor.ConvertToEmbed 新設 — 段落 → Embed 変換、undo 1 op) — strudel の
    「行を評価」に相当する UX がフォーマットの 1 メソッドで成立。SupportsHybrid=false。
  - **LiveCodeBlock**: 子 widget (TextArea + Button ×2 + Sparkline) を自前 Layout/Realize する
    複合 widget。Run = ローカル再解釈 + Commit (文書往復、Ctrl+Z 可)、Stop/Go = ローカル再生
    トグル、RealizeCore の ctx.AddAnimation が毎 Tick 波形を進める (スコープ破棄で自動停止)、
    IDisposable。snap 決定性: 初期波形は phase=0 で決定的 (アニメは snap では回らない)。
  - E2E 済: パターン「7 7 1 9」をタイプ → Enter → **ライブブロック誕生 + 波形再生** →
    lc1/lc2 のフレーム差分で Tick 駆動確認 → Ctrl+Z で**プレーン段落に復元**。
    エディタ内エディタ (埋め込み TextArea) のフォーカス/キー配送は参照ベースフォーカスで安定。

## 公開 API の使い方 (まとめ)

エディタに独自の文書形式と埋め込み UI を載せる手順:

1. **フォーマット**: `IDocumentFormat` を実装 (または `MarkdownFormat` に `FenceResolvers` を登録)。
   Parse が `BlockKind.Embed` + payload を作れば何でも埋め込みブロックになる。
2. **widget 解釈**: `new BlockWidgetRegistry().Register(typeId, bc => Widget)` —
   `bc.Payload` (データ) / `bc.MaxWidth` (幅) / `bc.Commit` (文書へ確定 = undo 可) /
   `bc.Invalidate` (表示だけ再構築) / `bc.Theme`。IDisposable はエディタが破棄時に呼ぶ。
3. **エディタへ**: `RichTextEditor(source, height, format: fmt, widgets: registry)` —
   専用フォーマットは 1+2 を対で生成するファクトリ関数として配布し、解釈を固定する。

## リスク / 判断メモ

- **Realize 済みツリーへの動的 widget 管理**が本丸: SetRoot 全再構築を避けて widget を
  追加/除去するには、UiBuildContext の Hits/Focusables/Scrollables/Animations から
  「特定 widget が登録した分」を除去する仕組みが要る (現状は追加のみ)。
  案: 登録時にスコープトークンを発行し `ctx.RemoveScope(token)` で一括除去。
  SurfaceView は「子 UiHost に閉じ込める」ことでこれを回避している — 複雑な embed は
  SurfaceView に包んで返せば当面この問題を踏まない (基盤改修を最小化する逃げ道)。
- **エディタ内エディタのヒット/フォーカス干渉**: RichTextEditor の AddHit はエディタ全面を
  覆う (ドラッグ選択) — 埋め込み widget のヒットが勝つには**後から登録された方が前面**
  というヒットテスト順 (Hits を後ろから走査) に依存する。RenderBlock の動的登録は
  ルートより後 = 前面 ✓ だが、再レンダリング順による逆転がないか E2E で確認する。
  埋め込み TextArea へのフォーカス移動はクリックで自然に立つ (同一 UiHost の FocusTarget)。
- **ライブブロックの undo**: コード編集中 (埋め込み TextArea 内) の undo は widget 内の
  undo (TextArea が持つ) が受け、**確定後の ReplacePayload だけが文書 undo に乗る**
  二層構造 — キー (Ctrl+Z) はフォーカスが埋め込み側にある限り widget が消費するので
  自然に成立するはずだが、境界 (確定直後) の体感は E2E で調整。
- **undo の一貫性**: payload 差し替えは必ず ReplacePayload 経由 (widget が Doc を直接
  触らない規約)。連続セル編集の合体はしない (セル確定 = 1 op)。
- **テーブルのキャレット統合はしない** (文書キャレットはテーブル内に入らない)。
  スクリーンリーダ/キーボードだけで完結したい場合の詳細度は将来枠。
- **セル内は v1 プレーン**: GFM はセル内インラインを許すが、round-trip の複雑さに対して
  価値が薄い。パース時はインライン記法をリテラル保持 (書き出しで不変)。
- **画像のサイズ指定**: markdown 標準に無い。v1 は幅 = エディタ幅 (等比)。
  必要になったら `![alt](src "=400x")` 等の方言は不採用、luxel フェンスへ逃がす。
- **他ツール互換**: luxel フェンスは GitHub 等でコードブロック表示になる (壊れない)。
  テーブル/画像は標準記法なのでそのまま通用する。
