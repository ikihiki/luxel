# テキストレイアウト プラン (Luxel.Typography — ボックス内テキストの正しい扱い)

2026-07-03 起案。status: **全マイルストーン (TX-M0〜M5) 完了 (2026-07-03)**。

> TX-M5 実装メモ: ネイティブ ICU は **icu.net 3.0.1 + Icu4c.Win.Full.Bin 59.1.15** で解決 (検証済み: IcuVersion 59.1)。
> `Icu4c.Win.Min` は **ubrk (BreakIterator) を含まない**ため不可。Windows OS 同梱の icu.dll は icu.net が
> 参照しないため利用不可。差し込みは app 起動時に
> `if (IcuSegmenter.IsAvailable) TextSegmenter.Default = new IcuSegmenter();` — snap/golden は
> SimpleSegmenter 固定 (ICU バージョン依存の折返し位置を golden に持ち込まない)。

> 命名メモ: 当初案の `Luxel.Text` は **名前空間 Luxel.Text が Text コントロール/ファクトリの単純名解決を壊す**
> (Luxel 直下に Text という名前空間メンバができ、`Text(...)` 呼び出しが CS0118 になる) ため
> **Luxel.Typography** に改名した。

## 目的

テキスト関連処理を専用プロジェクト **Luxel.Typography** に分離し、「指定されたボックス内での文字の扱い」を
正しくする:

1. **改行と折り返し (wrap)** — `\n` 段落 + 幅超過時の語/文字単位折り返し (現状は単一行のみ、`\n` は豆腐)
2. **合字/クラスタ対応のキャレット** — 現状の「部分文字列を Measure して幅を足す」はシェーピング
   (カーニング/合字) と不整合。クラスタ境界ベースの CaretRect / HitTest へ
3. **表示位置処理** — 水平 (Left/Center/Right)・垂直 (Top/Center/Bottom) 整列、行高
4. **リッチテキスト** — 1 ブロック内でフォント/サイズ/色が混在するスパン列の計測と描画
   (+ グリフ未収載時のフォントフォールバック)

## 現状の課題 (どこが「正しくない」か)

| 課題 | 現状 |
|---|---|
| 複数行 | VectorFont は 1 行専用。`\n` は .notdef (豆腐)。ログ等は「1 行 1 Text」で回避中 |
| キャレット | TextField が `Measure(text[..i]).width` — カーニング境界で描画とズレる。合字 (fi 等) では原理的に破綻 |
| 整列 | Text は左上寄せのみ。中央寄せは Center コンテナで外側から寄せるだけ (行単位の align 不可) |
| 混在スタイル | Text は単一フォント/サイズ/色。色は **UiNode.Color がシーン色を上書き**するため 1 ノード 1 色 |
| フォールバック | ラテンフォントで日本語 → 豆腩。フォント跨ぎの run 分割機構がない |

## 構成 (プロジェクト分離)

```
Luxel.TwoD   … 2D ラスタライザ (Scene2D/RetainedCanvas)。テキストを知らない純粋レイヤに戻す
Luxel.Typography   … 新設。HarfBuzzSharp 依存はここへ移動
  ├ VectorFont      (TwoD から移動。namespace Luxel.Typography。API 不変)
  ├ FontCollection  (優先順フォント列 + グリフ収載判定によるフォールバック)
  ├ TextSource      (プレーン string / RichText = TextSpan[])
  ├ SpanStyle       (Font?/Size/Color/LineHeight — スパン単位の上書き)
  └ TextLayout      (中核: シェーピング済み行の配置結果。計測/描画/ヒット/キャレット)
Luxel.UI     … Luxel.Typography を参照 (LayoutContext.Font の型は移動先を指す)
Luxel.Controls … Text/TextField が TextLayout を使う
Luxel.Typography.Icu … 任意アダプタ (icu-dotnet)。コアは参照しない — 使う app だけが参照して差し込む
```

- 参照方向: `Text → TwoD` (Scene2D へパスを出すため)。UI/Controls は Text 経由でフォントを扱う。
- VectorFont の**既存 API (Measure/Ascent/AppendText) は残す** — Button 等の 1 行ラベルは
  そのままでよく、移行を強制しない (内部は TextLayout の 1 行ケースに委譲)。

## TextLayout の設計 (中核)

```csharp
var layout = new TextLayout(source, new TextLayoutOptions
{
    MaxWidth = 300,            // ∞ = 折り返しなし
    Wrap = TextWrap.Word,      // None / Word / Char (CJK は Word でも文字単位可)
    Align = TextAlign.Center,  // Left / Center / Right / Justify
    LineHeight = 1.2f,         // 行高倍率
});
Size size = layout.Size;                       // 計測 (レイアウト後の実寸)
layout.Draw(scene, x, y);                      // 実描画 (色ごとに呼び分け可能な列挙も提供)
int idx = layout.HitTest(px, py);              // 座標 → テキスト位置 (クラスタ吸着)
Rect caret = layout.CaretRect(idx);            // テキスト位置 → キャレット矩形 (行と x)
Rect[] sel = layout.SelectionRects(a, b);      // 選択範囲の行別矩形
```

パイプライン (レイアウト時に 1 回、結果は保持して再利用):

```
spans → ①段落分割 (\n) → ②run 分割 (スタイル境界 + フォールバック境界 + スクリプト)
      → ③HarfBuzz シェーピング (run 毎; グリフ id/advance/cluster)
      → ④行分割 (break 機会で貪欲詰め; クラスタは分割しない)
      → ⑤行組み (align オフセット, ベースライン = 行内最大 ascent, 行高)
      → TextLayout (行 → GlyphRun[] (位置/スタイル/クラスタマップ))
```

### 分割判定の差し込み点 (ITextSegmenter — icu-dotnet 対応)

行分割・グラフェム・単語境界の判定は **1 インターフェースに隔離**し、実装を差し替え可能にする:

```csharp
public interface ITextSegmenter
{
    /// 段落内の各 char 境界の折り返し可否 (UAX#14 相当: Prohibited / Allowed / Mandatory)
    void GetLineBreaks(ReadOnlySpan<char> paragraph, Span<LineBreak> breaks);
    /// グラフェムクラスタ境界 (UAX#29 — キャレット移動の単位)
    int[] GetGraphemeBoundaries(string text);
    /// index を含む単語の範囲 (UAX#29 word — ダブルクリック選択用)
    (int start, int end) GetWordAt(string text, int index);
}
```

- **標準実装 `SimpleSegmenter`** (Luxel.Typography 内蔵、依存ゼロ): 空白 + CJK 文字境界 + 代表禁則セット、
  グラフェムは .NET の `StringInfo` (.NET 自体が ICU ベースで UAX#29 準拠)。
- **`Luxel.Typography.Icu` アダプタ**: icu-dotnet (icu.net) の `BreakIterator` (LINE/CHARACTER/WORD) で
  **完全 UAX#14/#29** を提供。ネイティブ ICU 依存はこのプロジェクトに閉じ込め、コアには持ち込まない。
- 差し込み方法: `TextLayoutOptions.Segmenter` (レイアウト単位) + プロセス既定
  `TextSegmenter.Default` (app 起動時に 1 回設定 — UI スレッド起動前。島規約と同じ考え方)。
  TextLayout は Segmenter の結果 (break 機会) だけを見るので、実装差し替えでパイプラインは不変。

### 行分割 (wrap) の方針

- break 機会は上記 Segmenter が返す。標準実装 v1: 空白の後 / CJK 文字境界 (漢字・かなは任意点で折る) /
  ハイフン後。**簡易禁則**: 行頭禁則 (。、」』・ゃゅょっ 等の小書き/約物) と行末禁則 (「『 等) の
  代表セット。完全な UAX#14 が要る app は ICU アダプタを差す。
- Word で入り切らない長い語は Char へフォールバック (溢れさせない)。
- クラスタ (合字/結合文字/サロゲートペア) は分割不可の最小単位。

### Justify (両端揃え)

- 行組み (⑤) で行の余り幅を分配する: **ラテンは空白 (SP) へ均等分配**、空白のない
  **CJK 行は文字間 (クラスタ間) へ分配** (和文組版の字間調整と同じ扱い)。混在行は空白優先、
  空白が無ければ字間へ。
- **段落最終行と強制改行 (\n) の行は分配しない** (左寄せのまま — CSS text-align: justify と同じ)。
- 分配はグリフの advance に加算するだけ (シェーピング済み結果への後処理) なので、
  キャレット/HitTest のクラスタ矩形にもそのまま反映される。
- 過大な分配を防ぐ上限 (空白 1 つあたり最大 ~2em 等) を設け、超える場合は分配を諦めて左寄せ。

### キャレット/選択 (合字対応)

- シェーピング結果の cluster 値で **テキスト位置 ↔ グリフ**の対応表を持つ。
- キャレット位置の単位は**グラフェム境界** (StringInfo)。合字 1 グリフに複数グラフェム
  (fi 合字等) が入る場合は**グリフ advance をグラフェム数で按分** (HarfBuzz 推奨の近似)。
- HitTest はクラスタ矩形の中央で左右に吸着。選択は行ごとの矩形列。
- TextField/TextEditor の caret/選択/IME 対象範囲 (GetTextExt) をこの API に置換 —
  「描画とキャレットが必ず一致する」状態にする。

### リッチテキストの描画 (retained の 1 ノード 1 色制約)

- UiNode.Color がシーン色を上書きするため、**色ごとに GlyphRun をグループ化して 1 色 1 ノード**
  で実体化する (色数は実用上少ない)。TextLayout は「色 → パス列」の列挙を提供し、
  widget 側 (RichTextView) がノードを割り当てる。
- テーマ連動色 (Bind) はノード単位の Effect で従来どおり成立する。

### フォールバック

- FontCollection = 優先順の VectorFont 列 (例: [UI フォント, 日本語フォント, 絵文字は将来])。
- run 分割時に `TryGetGlyph` で収載を判定し、未収載文字の連続を次候補フォントの run に切り出す。
- 行内のベースラインは run の最大 ascent に揃える (サイズ混在も同様)。

## マイルストーン

- **TX-M0: プロジェクト分離 (挙動不変)** — Luxel.Typography 新設、VectorFont/HarfBuzzSharp を移動
  (namespace 変更の機械的 sweep)。TwoD からテキスト知識を除去。
  **合格条件: snap 38/38 ピクセル不変 + 266+ テスト。**
- **TX-M1: TextLayout v1 (プレーン)** — 段落 (\n)/Word・Char wrap/簡易禁則/Left・Center・Right・Justify/行高。
  Text widget に `wrap`/`align`/`maxLines` パラメータ (既定 = 現状互換の 1 行)。
  ログ・ダッシュボード等の複数行需要を置換できる状態に。ストーリー + 単体テスト (折返し位置/禁則)。
- **TX-M2: クラスタ/キャレット** — cluster マップ + グラフェム按分の CaretRect/HitTest/SelectionRects。
  TextField の caret/選択/クリック位置/IME 矩形を TextLayout ベースへ移行。
  テスト: 合字 (fi)、結合文字 (é = e+◌́)、サロゲート (𠮷)、日本語混在で「描画とキャレットの一致」。
- **TX-M3: リッチテキスト** — TextSpan/SpanStyle/FontCollection フォールバック、色ごとノード分割。
  新 widget **RichTextView** (spans を受ける; Text は plain 専用のまま)。
  ストーリー: サイズ/色/フォント混在 + 折返し + 整列の組み合わせ。
- **TX-M4: 仕上げ** — VerticalAlign / ellipsis (1 行超過の `…`) / レイアウトキャッシュ
  (テキスト+幅+スタイルのキーで再利用; TextField は編集毎に再レイアウトするため重要)、
  ドキュメント、150% E2E (DevTools/Gallery の複数行化した箇所)。
- **TX-M5: Luxel.Typography.Icu アダプタ (任意)** — icu-dotnet の BreakIterator で ITextSegmenter を実装
  (LINE=UAX#14 完全版, CHARACTER=UAX#29, WORD)。SimpleSegmenter との差分テスト
  (数値+単位「100 km」、URL、絵文字 ZWJ 列、ノーブレークスペース) + 差し込み手順のドキュメント。
  ネイティブ ICU の解決 (Windows 10 1703+ の OS 同梱 icu.dll / NuGet ネイティブパッケージ) を
  ここで検証する。

## リスク / 判断メモ

- **範囲を絞る**: 縦書き・BiDi (RTL) は v1 対象外。完全 UAX#14 は自前実装せず、
  **ITextSegmenter + Luxel.Typography.Icu (icu-dotnet)** で提供する (TX-M5)。
- **icu-dotnet のネイティブ依存**: icu.net はネイティブ ICU (icuuc/icuin) を要する — アダプタ
  プロジェクトに閉じ、コア (Luxel.Typography) は依存ゼロを維持。snap/CI は SimpleSegmenter で走らせ、
  ICU アダプタは差分テストのみ (golden を ICU に依存させない)。
- **性能**: シェーピングは行分割の再試行で複数回走りやすい — run 単位の shape 結果を
  レイアウト内でメモ化。TextField は 1 行なので影響軽微、複数行エディタは将来。
- **snap 影響**: TX-M0 は不変が合格条件。TX-M1 以降は既定値を現状互換 (1 行・左寄せ) にして
  既存 golden を守り、新機能はストーリー追加で golden を増やす。
- **既存 API**: VectorFont.Measure/AppendText は温存 — 全コントロール一斉移行を避け、
  複数行/キャレットが必要な箇所 (Text/TextField/RichTextView) だけ TextLayout を使う。
