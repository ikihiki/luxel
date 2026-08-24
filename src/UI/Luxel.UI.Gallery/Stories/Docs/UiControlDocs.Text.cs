using Luxel.Gallery;
using Luxel.Gallery.UI;

namespace Luxel.UI.Gallery;

internal static partial class UiControlDocs
{
    private static readonly ControlDocsPage[] TextPages =
    [
        Page(
            "LinkText",
            "Text",
            "LinkText",
            "背景や余白を持たない短い文字列をポインターで起動する、ナビゲーション向けの軽量リンクです。",
            ["サイドバー、ツリー、パンくずなどで、短い文字ラベルから別の場所や文書へ移動する場合。"],
            ["現在画面のコマンド実行、二値状態の切替、キーボードだけで到達できる経路が必須の場合。"],
            "LinkText(\"詳細を見る\", _ => Navigate())",
            "遷移先、履歴、外部ブラウザー起動などの状態と副作用は呼び出し側が所有します。`active` は現在地を示す見た目だけを変え、hover は Widget 内の一時状態です。確定値や検証モデルはありません。",
            "`text`、`fontSize`、`active`、状態指定可能な `color` / `hoverColor`、`OnClick` が中心です。",
            "文字列全体がポインター hit target で、クリック時に `OnClick(LinkText)` を発火します。選択、ドラッグ、URI 解決は行いません。",
            "既定の文字サイズは `UiTheme.FontSm`、通常色は `TextMuted`、hover は `Text`、active は `Primary` です。背景と内側余白はありません。幅と高さは文字の自然寸法です。",
            "一行表示だけで、折り返し、ellipsis、文字選択はありません。URL の安全性、履歴、失敗表示は呼び出し側で扱います。専用 semantic role とキーボード focus は提供しません。",
            "LinkText",
            alternatives: [
                new("Button", "現在画面でコマンドを実行し、ボタンとして明確な操作面を示します。"),
                new("MenuRow", "背景と行高を持つ一覧項目としてポインター操作させます。")
            ]
        ) with
        {
            Anatomy = "一つのテキスト描画ノードと、同じ自然寸法を持つ一つのポインター hit region から構成します。背景、padding、子 slot は持ちません。",
            Variants = "`active` で通常色と hover 色の両方をアクセントへ切り替えます。`fontSize`、`color`、`hoverColor` と Widget 共通のサイズ・配置指定を組み合わせられます。",
            FocusActivationDismissal = "focus target を登録しないため `Tab` では到達せず、`Enter` / `Space` 起動もありません。ポインタークリックだけが activation で、dismissal の概念はありません。",
            Accessibility = new(
                "見える `text` 自体を遷移先が分かる具体的な文言にします。",
                "`SemanticRole` を公開せず、link / button としての意味や URL を支援技術へ渡しません。",
                "`active` と hover は描画色だけの状態で、現在地や visited 状態として公開されません。",
                "既定では通常、hover、active の色差だけで状態を示すため、背景とのコントラストを確認し、重要な経路では周囲の見出しやアイコンも併用します。",
                "hover 色は 0.08 秒で補間し、`active` の変化は即時です。",
                "ポインター専用です。キーボード操作、リンク role、読み上げ名、visited 状態が必要な主要導線の唯一の経路にはしません。"),
            ThemeLayout = new(
                "未指定色は `UiTheme` の `TextMuted` / `Text` / `Primary` に追従し、文字サイズは `FontSm` を使います。",
                "左寄せの自然寸法要素として配置し、必要な行高やクリック余白は親コンテナで確保します。",
                "明示した `width` / `height` 以外は文字計測値です。長文の wrap や省略は行わないため、短いラベルに限定します。"),
            Constraints = new(
                "文字列描画とポインター hit だけを提供し、URI、ルーター、外部アプリ起動、失敗処理を知りません。",
                "hover animation と hit registration は Widget scope に従って破棄されます。遷移先の状態や購読は呼び出し側が管理します。",
                "手形 cursor とポインター入力を持つホストで利用できますが、プラットフォーム固有のネイティブ link semantics はありません。"),
            Api = new(
                "LinkText",
                "`text`、`fontSize`、`active`、状態指定可能な `color` / `hoverColor` が固有パラメーターです。",
                "`OnClick` は発火元の `LinkText` を渡します。遷移の成否や選択状態は通知せず、呼び出し側が必要な Signal を更新します。"),
        },
        Page(
            "RichTextView",
            "Text",
            "RichTextView",
            "フォント、文字サイズ、色をスパン単位で混在させる、読み取り専用のリッチテキスト表示です。",
            ["短い説明や装飾付き本文を、`TextSpan[]` のスタイルを保って読み取り専用で表示する場合。"],
            ["利用者が選択・編集する文書、Markdown / HTML の意味構造、最大行数による省略が必要な場合。"],
            "RichTextView([new TextSpan(\"重要\", new SpanStyle { Size = 20, Color = 0xFF2563EB })], wrap: TextWrap.Word)",
            "`TextSpan[]` と利用する `FontCollection` は呼び出し側が所有します。ビューはレイアウト結果だけを保持し、選択、キャレット、commit、validation state は持ちません。",
            "`spans`、`fontSize`、`wrap`、`textAlign`、`lineHeight` と、任意の `Fonts` が中心です。",
            "ポインター操作、文字選択、リンク起動はありません。スパン中の文字は一つの連続レイアウトとして描画されます。",
            "各 `SpanStyle` のフォント、サイズ、色を使い、未指定フォントは `Fonts` またはホストの `ctx.Font` へフォールバックします。幅は明示値または親の有限幅を使います。高さはレイアウトの自然高です。",
            "HTML / CSS、Markdown 解釈、リンク、選択、編集、`maxLines`、縦位置揃えは提供しません。色 run ごとに描画ノードを作るため、色数と長文の再レイアウトコストに注意します。フォント資源は呼び出し側が破棄します。",
            "RichTextView",
            alternatives: [
                new("Text", "全体が同じスタイルの短い文字列を軽量に表示します。"),
                new("TextEditorView", "選択、編集、Markdown 装飾、長いスクロール文書を扱います。")
            ],
            related: [
                new("Controls/Editor/TextEditorView/RichText", "TextEditorView のリッチ装飾", "編集モデル上の太字、文字尺度、色の装飾を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/MarkdownDocStory", "Markdown 文書表示", "折り返し、選択可能なコード、Markdown 装飾を持つ読み取り専用文書を確認します。", StoryKind.Unspecified)
            ]
        ) with
        {
            Anatomy = "`TextSpan[]` を一つの `TextLayout` にまとめ、行内の最大 ascent へベースラインを揃えます。保持描画の色制約に合わせ、使用色ごとに子描画ノードを分けます。",
            Variants = "スパンごとに `Font`、`Size`、`Color`、インライン占位用 `BoxW` / `BoxH` を指定できます。全体では `TextWrap.None` / `Word` / `Char`、左・中央・右・Justify、`lineHeight` を選びます。",
            FocusActivationDismissal = "focus target と hit target を登録しません。キーボード activation、selection、dismissal はありません。",
            Accessibility = new(
                "ビュー自身は名前を持たないため、周囲の見出しや説明で内容の目的を示します。",
                "専用の text / document semantic role、見出し階層、リンク、スパン強調の意味を公開しません。",
                "読み取り専用で、選択、focus、現在行、検証状態はありません。",
                "`SpanStyle.Color` は literal 色で既定値も黒です。Light / Dark の両テーマで背景とのコントラストを確認し、必要ならテーマ変更時に spans を再構築します。",
                "アニメーションはありません。",
                "描画された文字とスタイルを支援技術が構造化文書として読む契約はありません。重要情報は別の semantic な本文でも提供します。"),
            ThemeLayout = new(
                "フォントと色は `TextSpan` / `Fonts` から決まり、テーマ色へ自動変換されません。グリフがない場合は `FontCollection` の次候補を使います。",
                "明示 `width`、親の有限幅、無限幅の順で折り返し幅を決め、スパン境界を越えて禁則・整列・Justify を適用します。",
                "高さは行内最大文字サイズと `lineHeight` から求めます。`maxLines`、ellipsis、ボックス内の縦揃えはありません。"),
            Constraints = new(
                "長い文書の仮想化、選択、リンク hit test、編集履歴はありません。多数の色 run は子ノード数を増やします。",
                "`Fonts` は所有せず、渡した `VectorFont` / `FontCollection` の寿命と破棄は呼び出し側が管理します。",
                "表示品質と利用可能グリフは描画ホストと供給フォントに依存します。IME やクリップボードは使用しません。"),
            Api = new(
                "RichTextView",
                "`spans`、`fontSize`、`wrap`、`textAlign`、`lineHeight` と、公開プロパティ `Fonts` が中心です。",
                "固有イベントと commit はありません。内容更新が必要な場合は入力となる spans を更新し、必要な再レイアウトをホスト側で発生させます。"),
        },
        Page(
            "SearchField",
            "Text",
            "SearchField",
            "一行の検索語と、ローカル候補の部分一致リスト、検索・クリア icon を組み合わせた typeahead 入力です。",
            ["小さな固定候補集合を、入力と同時に部分一致で絞り込み、ポインターで一件確定する場合。"],
            ["非同期検索、大量候補の仮想化、候補のキーボード移動、combobox semantics、検索実行の明示 commit が必要な場合。"],
            "SearchField(query, candidates, maxSuggestions: 5)",
            "検索語の `Signal<string>` と候補一覧は呼び出し側が所有します。候補は現在 query から毎回派生し、入力、クリア、候補クリックはいずれも query へ即時書き戻します。別の commit、validation、debounce state はありません。",
            "`value`、`candidates`、`maxSuggestions` が中心です。内部には幅 220 px の `TextField` と leading / trailing icon、候補 `MenuRow` を持ちます。",
            "入力欄をクリックして編集し、閉じる icon で空文字へ戻し、候補行クリックでその文字列を確定します。候補は overlay ではなく入力欄の直下へインライン表示されます。",
            "内部 `TextField` はテーマの入力外観、候補行は `MenuRow` の hover 面を使います。候補の増減で CompositeControl の高さが変わります。入力幅は実装上 220 px です。",
            "各 query 変更で候補全体を大文字小文字を無視した `Contains` で走査し、行ツリーを再構築します。空 query と完全一致候補は表示しません。非同期検索、結果件数、loading、error、キャッシュは呼び出し側の責任です。",
            "SearchField",
            alternatives: [
                new("TextField", "候補一覧を持たない一般の一行文字列を入力します。"),
                new("TextEditorView", "複数行の検索式や置換 UI を構成します。")
            ],
            keyboard: [
                new("`Left` / `Right`、`Home` / `End`", "入力欄のキャレットを移動し、`Shift` 併用で選択を拡張します。"),
                new("`Backspace` / `Delete`", "入力欄の選択または前後の grapheme を削除し、query を即時更新します。"),
                new("`Ctrl+A` / `Ctrl+C` / `Ctrl+X` / `Ctrl+V`", "全選択と、利用可能な platform clipboard によるコピー・切り取り・貼り付けを行います。"),
                new("`Tab` / `Shift+Tab`", "`UiHost` が次または前の focus target へ移動します。"),
                new("`Up` / `Down` / `Enter`", "候補選択には割り当てられていません。候補確定はポインター操作です。")
            ],
            related: [
                new("Controls/Text/TextField/Examples/Interactive", "TextField の対話入力", "共有 Signal へ一行入力を書き戻す基本動作を確認します。", StoryKind.Example),
                new("Controls/Text/TextField/Examples/Slots", "TextField の前後 slot", "検索 icon と shortcut 表示を入力領域へ組み込む構造を確認します。", StoryKind.Example)
            ]
        ) with
        {
            Anatomy = "状態を保持する一つの `TextField`、leading の検索 icon、trailing のクリア icon、その下へ 0 件以上並ぶ `MenuRow` から構成します。コンテナだけを rebuild し、TextField インスタンスは再利用します。",
            Variants = "`maxSuggestions` で表示上限を指定しますが、実装は 1 未満を 1 として扱います。候補表示は空 query で 0 件、case-insensitive な完全一致も除外します。",
            FocusActivationDismissal = "focus target は内部 `TextField` だけです。候補行とクリア icon はポインター hit target ですが focus target ではありません。overlay ではないため `Escape` dismissal はありません。",
            Accessibility = new(
                "見えるラベルを外側に置き、placeholder の `Search...` だけを名前として頼らないようにします。",
                "searchbox / combobox / listbox / option semantic role を公開しません。内部 TextField と候補 MenuRow にも専用 role はありません。",
                "query と候補件数、候補 highlight、確定状態を支援技術へ公開しません。",
                "入力面、placeholder、icon、候補 hover のコントラストをテーマごとに確認します。",
                "候補リストの開閉アニメーションはなく、MenuRow の hover だけが 0.08 秒で変化します。",
                "候補をキーボードや読み上げだけで走査・確定する契約はありません。アクセシブルな候補選択が必須なら別実装を用意します。"),
            ThemeLayout = new(
                "TextField と MenuRow の `UiTheme` token を使い、検索・クリア icon は `TextMuted` へ追従します。",
                "候補は overlay ではなく通常フローで縦に増えるため、後続内容の押し下げを許すレイアウトへ置きます。",
                "内部入力は幅 220 px、候補間隔は 2 px です。大量候補や狭い親幅への自動適応は行いません。"),
            Constraints = new(
                "候補走査は query 変更ごとに全件評価し、表示行を rebuild します。大量・遠隔候補には debounce と非同期結果モデルを外側で用意します。",
                "内部 TextField は rebuild を跨いで focus、caret、selection を保持し、CompositeControl の追跡購読は scope 破棄時に解除されます。",
                "IME と clipboard は内部 TextField の `ITextInput` および利用ホストに依存します。候補 UI の platform accessibility adapter はありません。"),
            Api = new(
                "SearchField",
                "`value: Signal<string>`、`candidates: IReadOnlyList<string>`、`maxSuggestions` が固有パラメーターです。独自 placeholder、filter predicate、loading state は公開しません。",
                "固有イベントはなく、入力・クリア・候補クリックの結果は同じ query Signal への即時書き戻しで観測します。"),
        },
        Page(
            "Text",
            "Text",
            "Text",
            "一様なスタイルの文字列を描画する基本コントロールで、一行ラベルから折り返し本文、最大行数付きの補足文まで扱います。",
            ["ラベル、見出し、補足文、状態文など、編集しない一様なスタイルの文字列を表示する場合。"],
            ["利用者の文字選択・編集、スパンごとの色や font、文書 semantics が必要な場合。"],
            "Text(message, 14, wrap: TextWrap.Word, maxLines: 2)",
            "値または `BindableString` / `Func<string>` が表示内容の正本です。選択、キャレット、commit、validation state はありません。",
            "`content`、`fontSize`、状態指定可能な `color`、`opacity`、`wrap`、`textAlign`、`lineHeight`、`maxLines`、`verticalAlign` が中心です。",
            "ポインター、キーボード、文字選択は扱いません。文字列の内容とレイアウト結果だけを描画します。",
            "文字色と opacity は状態・テーマへ bind できます。折り返し幅は明示 `width`、親の有限幅、無限幅の順で決まり、明示 `height` は縦位置揃えの基準になります。",
            "`TextWrap.None` の一行表示は自動 ellipsis や clipping を追加しません。`maxLines` を指定した複数行レイアウトだけが超過を `…` で省略します。内容の reactive 更新は再描画しますが、v1 では周囲を含むサイズ変更の自動再レイアウトを保証しません。",
            "Text",
            alternatives: [
                new("RichTextView", "スパンごとに font、size、color を変える読み取り専用表示です。"),
                new("TextField", "一行文字列を選択・編集します。"),
                new("TextEditorView", "複数行の選択、編集、スクロール、装飾を扱います。")
            ],
            related: [
                new("Controls/Text/Text/Examples/Styles", "文字スタイル", "文字尺度、テーマ色、opacity の組み合わせを比較します。", StoryKind.Example),
                new("Controls/Text/Text/Examples/Multiline", "複数行と禁則", "改行、word wrap、日本語禁則、中央揃え、Justify を確認します。", StoryKind.Example),
                new("Controls/Text/Text/Examples/EllipsisVerticalAlignment", "省略と縦位置揃え", "`maxLines` の ellipsis と `TextVAlign` を確認します。", StoryKind.Example),
                new("Controls/Text/Text/Examples/Japanese", "日本語表示", "同梱 fallback font による日本語本文と editor 表示を確認します。", StoryKind.Example)
            ]
        ) with
        {
            Anatomy = "高速な一行互換 path、または `TextLayout` を使う複数行 path のどちらかで、一つの描画ノードへ glyph scene を生成します。",
            Variants = "既定は `TextWrap.None`、左揃え、最大行数なし、上揃えです。改行、wrap、中央・右・Justify、`maxLines`、明示高さでの中央・下揃えを組み合わせられます。",
            FocusActivationDismissal = "focus target と hit target を登録しません。activation、selection、dismissal はありません。",
            Accessibility = new(
                "見える文字列を明確にし、入力の label として使う場合は周囲の構造で対象入力との関係を示します。",
                "専用の text、heading、label semantic role や heading level を公開しません。",
                "focus、selected、expanded、validation state はありません。色の Widget state override は見た目だけです。",
                "未指定色は固定の濃灰であり、テーマ追従には `Bind.From(() => UiTheme.T.Text)` などを明示します。Light / Dark で文字と背景のコントラストを確認します。",
                "アニメーションはありません。状態付き `color` / `opacity` に transition を設定した場合だけ補間されます。",
                "描画文字を読み上げ用の名前、見出し、live region として公開する契約はありません。重要な状態通知は semantic 対応コントロールも併用します。"),
            ThemeLayout = new(
                "`color` と `opacity` は Bindable でテーマや Widget state に追従できます。使用 font はホストの `LayoutContext.Font` です。",
                "一行 path は自然寸法、複数行 path は width と親制約を使います。日本語禁則、word / char wrap、段落末行を除く Justify を `TextLayout` が処理します。",
                "`lineHeight` の既定は 1.2、`maxLines` の既定 0 は無制限です。`verticalAlign` は明示 `height` がある場合だけ意味を持ちます。"),
            Constraints = new(
                "選択、コピー、編集、スパン style、リンク hit test はありません。頻繁に長さが変わる文字列では固定幅・高さを確保し、周囲レイアウトの再計算を別途行います。",
                "複数行 layout は同じ font、文字列、option のキャッシュを利用し、content 変更時は scene を再生成します。外部 Signal の寿命は呼び出し側が管理します。",
                "表示 glyph と日本語 fallback はホストが供給する font に依存します。IME、clipboard、platform semantic adapter は使用しません。"),
            Api = new(
                "Text",
                "`content`、`fontSize`、`color`、`opacity`、`wrap`、`textAlign`、`lineHeight`、`maxLines`、`verticalAlign` と Widget 共通の `width` / `height` が中心です。",
                "固有イベントはありません。内容は Bindable の更新で再描画されますが、値変更の通知や commit は入力側が所有します。"),
        },
        Page(
            "TextField",
            "Text",
            "TextField",
            "caret、選択、clipboard、IME composition を持つ一行テキスト入力です。編集結果を共有 `Signal<string>` へ即時反映します。",
            ["名前、検索語、短い識別子など、一行の自由文字列または軽い正規表現制約付き文字列を入力する場合。"],
            ["複数行本文、保存時だけの commit、secret masking、完全な validation UI、自動横スクロールが必要な場合。"],
            "TextField(value, placeholder: \"名前\", width: 240)[TextFieldSlot.Leading(() => Icon(IconKind.Search))]",
            "`Signal<string>` が確定値の正本で、通常入力、削除、貼り付け、IME 確定ごとに即時更新されます。Widget は caret、anchor、selection、composition と caret blink を保持します。別の commit event はありません。`Pattern` は入力拒否だけで error state を生成しません。",
            "`value`、`placeholder`、`fontSize`、`background`、leading / trailing slot、公開 `Pattern` と `ExtraKeys` が中心です。",
            "クリックで grapheme 境界へ caret を置き、ドラッグで選択します。ダブルクリックは単語、トリプルクリックは全文を選択し、右クリックは editor context menu を開きます。",
            "既定幅は 240 px、高さは `UiTheme.ControlH`、左右 padding は `PadIn` です。slot の自然幅と 6 px gap を差し引いた一行領域を clip し、背景、focus ring、選択、IME target、caret をテーマ色で描きます。",
            "折り返し、ellipsis、自動横スクロール、undo / redo、password masking、validation message はありません。長い値は入力領域で見切れます。platform clipboard がない場合も shortcut は消費されます。`Pattern` は業務検証や安全性の境界に使いません。",
            "TextField",
            alternatives: [
                new("SearchField", "ローカル候補の絞り込みとクリア操作を組み込みます。"),
                new("TextEditorView", "複数行、undo / redo、検索、装飾、scroll を扱います。"),
                new("LengthField", "単位付き `Length` を型付きで編集します。")
            ],
            keyboard: [
                new("`Left` / `Right`", "grapheme 単位で caret を移動し、`Shift` 併用で選択を拡張します。"),
                new("`Home` / `End`", "一行の先頭または末尾へ移動し、`Shift` 併用で選択します。"),
                new("`Backspace` / `Delete`", "選択範囲、または前後の grapheme を削除して Signal を即時更新します。削除は `Pattern` による拒否対象になりません。"),
                new("`Ctrl+A`", "全文を選択します。"),
                new("`Ctrl+C` / `Ctrl+X` / `Ctrl+V`", "利用可能な platform clipboard でコピー、切り取り、貼り付けを行います。貼り付けの CR は除去し、LF は空白へ変換します。"),
                new("`Tab` / `Shift+Tab`", "`UiHost` が次または前の focus target へ移動します。`Enter` の固有 commit はありません。")
            ],
            related: [
                new("Controls/Text/TextField/Examples/Interactive", "対話入力", "クリック、文字入力、共有 Signal への即時反映を確認します。", StoryKind.Example),
                new("Controls/Text/TextField/Examples/Slots", "前後 slot", "leading / trailing widget と入力領域の配置を確認します。", StoryKind.Example),
                new("Controls/Text/TextField/States/Focused", "focus 状態", "focus ring と caret を持つ表示状態を確認します。", StoryKind.State),
                new("Controls/Text/TextField/States/Invalid", "制約付き入力", "`Pattern` と呼び出し側が描く error 補助文の組み合わせを確認します。", StoryKind.State)
            ]
        ) with
        {
            Anatomy = "テーマ背景と focus ring、選択面、IME target 面、preedit underline、文字、caret の描画層に、任意の leading / trailing slot と一つの `FocusTarget` / `ITextInput` を組み合わせます。",
            Variants = "slot なし、leading、trailing、両方の構造を選べます。`Pattern` に正規表現を設定すると、通常入力・貼り付け・`ITextInput.Replace` 後の全文を検査して不一致操作を拒否します。全文制約には `^...$` を明示します。",
            FocusActivationDismissal = "ポインタークリックまたは `Tab` traversal で focus します。focus 取得時は caret を末尾へ置き、再実体化では同じ `FocusTarget` を再登録して focus を保持します。外側へ `Tab` 移動しても自動 commit / validation event はありません。",
            Accessibility = new(
                "常設の見える label を外側に置き、placeholder を唯一の名前にしないようにします。",
                "`ITextInput` により IME 文書と caret rect は提供しますが、framework の textbox semantic role、label relation、required / invalid state は公開しません。",
                "focus、selection、composition は視覚表示されます。`Pattern` の拒否や validation error は semantic state として通知されません。",
                "背景、placeholder、本文、選択面、focus ring、IME target と caret のコントラストを Light / Dark で確認します。",
                "caret は約 0.53 秒ごとに点滅し、その他の標準 animation はありません。",
                "読み上げ名、textbox role、error relation を必要とするフォームでは platform adapter または代替 UI が必要です。"),
            ThemeLayout = new(
                "`SurfaceAlt`、`TextMuted`、`Text`、`Primary` と focus ring を使用し、`fontSize` 未指定時は `UiTheme.Font` です。",
                "`Align.Stretch` では親の有限幅を使い、それ以外は既定 240 px または明示幅です。slot は同じ owner theme で layout し、置換時に旧 subtree を破棄します。",
                "高さは `ControlH`、slot gap は 6 px です。本文は一行・clip のみで、横 scroll と ellipsis はありません。"),
            Constraints = new(
                "`Pattern` は不正 draft の診断を保持せず、外部から Signal へ設定された値も検証しません。IME の `OnCommit` 経路は `Pattern` を再検査しないため、最終的な業務 validation は別に行います。",
                "Value Signal と slot 内の外部状態は呼び出し側が所有します。slot 交換時は旧 scope を破棄し、field の FocusTarget は Widget インスタンスの生存中保持します。",
                "IME は `ITextInput` と対象ホストの TSF / text-input bridge、copy / cut / paste は `PlatformClipboard` に依存します。"),
            Api = new(
                "TextField",
                "`value: Signal<string>`、`placeholder`、`fontSize`、`background`、Widget 共通サイズ、`SetSlot` / `TextFieldSlot.Leading` / `Trailing`、`Pattern`、`ExtraKeys` が中心です。",
                "固有 `UiEvent` や commit event はありません。変更結果は value Signal で観測し、`ExtraKeys` は既定処理に該当しないキーだけを受け取って `true` で消費できます。"),
        },
    ];
}
