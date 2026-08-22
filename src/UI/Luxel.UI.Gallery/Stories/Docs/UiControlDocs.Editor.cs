using Luxel.Gallery;
using Luxel.Gallery.UI;

namespace Luxel.UI.Gallery;

internal static partial class UiControlDocs
{
    private static readonly ControlDocsPage[] EditorPages =
    [
        Page(
            "NodeGraphView",
            "Editor",
            "NodeGraphView",
            "不変なグラフ文書を、選択、ノード移動、配線、pan / zoom、undo / redo とともに編集・閲覧する canvas view です。",
            ["ノード、型付き port、edge、任意の node parameter UI を空間配置で編集する場合。", "依存グラフや render graph を pan / zoom 付きの読み取り専用 view として検査する場合。"],
            ["線形なフォームや表で十分なデータ、完全なキーボード操作や支援技術によるグラフ編集が必須の場合。"],
            "var document = new GraphDocument(source);\nNodeGraphView(document: document, viewWidth: 640, viewHeight: 400)",
            "`GraphDocument` を渡すと document が graph state と履歴を所有し、複数 view で共有できます。`source` だけなら view が内部 document を作ります。選択と viewport は state に含まれますが文書変更ではなく、drag preview は drop まで履歴へ積みません。",
            "`source` / `document`、`viewWidth` / `viewHeight`、`Config`、`ReadOnly`、`SnapToGrid`、`NodeCatalog`、`WidgetResolver`、`OnEdit` が中心です。",
            "ノードクリックで単独選択、`Ctrl`+click で追加・解除、空白 drag で marquee、ノード drag で選択集合を移動します。port drag は互換性を検証して edge を作り、edge click で切断対象を選択します。wheel は cursor 下を固定して zoom、中ボタン drag は pan、右クリックは catalog palette です。",
            "背景、grid、wire、node surface、header、port、選択線は `UiTheme` token に追従します。既定 view は 480 × 360 px、最小は 160 × 120 px で、内容は view 内に clip されます。",
            "全 node / edge を refresh ごとに描画し、viewport culling や仮想化はありません。巨大 graph では geometry、hit test、inline widget の再実体化コストを測定します。完全な keyboard navigation、semantic graph、cycle policy、domain validation、永続化は自動ではありません。",
            "NodeGraphView",
            alternatives: [
                new("DataGrid", "同じ列構造の項目をキーボードと semantic grid で比較・選択します。"),
                new("PropertyGrid", "一つの対象の名前付き scalar property を縦フォームで編集します。")
            ],
            keyboard: [
                new("`Ctrl+Z` / `Ctrl+Y`", "`GraphDocument` の直前 transaction を undo / redo します。"),
                new("`Ctrl+A`", "編集可能時に全 node を選択します。"),
                new("`Delete` / `Backspace`", "選択 node と接続 edge、または選択 edge を一つの transaction で削除します。"),
                new("`Escape`", "選択を解除します。読み取り専用時もこの操作だけは消費します。"),
                new("`Tab` / `Shift+Tab`", "`UiHost` が graph の外側を含む次または前の focus target へ移動します。node / port 間の keyboard navigation はありません。")
            ],
            related: [
                new("Controls/Editor/NodeGraphView/Wiring", "配線", "port drag、接続可否の色、edge 選択と切断を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/NodeGraphView/Widgets", "node 内 UI", "document-backed Signal の Slider と右クリック node palette を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/NodeGraphView/AutoLayoutStory", "自動レイアウト", "依存 rank に沿う左から右の配置、grid snap、FitToView を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/NodeGraphView/RenderGraph", "読み取り専用 render graph", "drag=pan、click=検査選択、wheel=zoom の read-only 動作を確認します。", StoryKind.Unspecified),
                new("Examples/Workbench/Material", "material workbench", "NodeGraph document、保存、palette、status を組み合わせた editor lifecycle を確認します。", StoryKind.Unspecified)
            ]
        ) with
        {
            Anatomy = "clip された root の下に world-space の grid / wire / node / port / title layer、screen-space の marquee と inline-widget layer、一つの `FocusTarget` を持ちます。pan / zoom は world container の affine transform です。",
            Variants = "`source` を内部 document で扱う形と、共有 `GraphDocument` を注入する形があります。`ReadOnly`、40 px grid への `SnapToGrid`、`NodeCatalog` palette、`NodeInlineDecoration` + `WidgetResolver`、`WidgetClip` を組み合わせられます。",
            FocusActivationDismissal = "canvas の pointer down で focus し、focus ring を表示します。`Tab` は host traversal、`Escape` は選択解除です。palette は右クリック時だけ開き、catalog がない場合と read-only では開きません。",
            Accessibility = new(
                "画面上の見出しや別の一覧で graph の目的を示し、node title と port label は短く具体的にします。",
                "`ISemanticProvider` を実装せず、graph、node、port、edge、selected state の semantic role / relation を公開しません。",
                "選択、接続可否、read-only、viewport、node parameter は視覚または内部 state だけで、支援技術向け state ではありません。",
                "node、wire、selected stroke、進行中 wire の緑 / 赤を背景上で確認し、可否を色だけに依存させる重要フローでは別の診断も表示します。",
                "標準の transition animation はなく、drag preview、pan、zoom が pointer 入力へ追従します。",
                "node / port への keyboard focus、関係読み上げ、代替 list editor は提供しません。完全な keyboard / screen-reader 経路が必要なら別 view を併設します。"),
            ThemeLayout = new(
                "`Background`、`Surface`、`SurfaceAlt`、`BorderColor`、`TextMuted`、`Text`、`Primary` で canvas を描き、node title は `FontSm` を使います。",
                "world content は view rect で clip し、inline widget は screen-space へ変換して node slot に配置します。wheel zoom は 0.25–4 倍、`FitToView` は 0.25–2 倍へ clamp します。",
                "`viewWidth` は最小 160、`viewHeight` は最小 120 です。node size は title、port label、inline slot と `GraphConfig` から測定します。"),
            Constraints = new(
                "接続は自己接続、同方向、型不一致、重複を拒否します。単一 input への新接続は既存 edge を同じ transaction で置換し、`Multi` input は複数を許します。cycle や domain 固有規則は別層です。",
                "注入した `GraphDocument` の変更購読と node parameter binding は Widget scope で解除し、削除した inline widget / context は破棄します。`Load` は選択と履歴を reset します。",
                "pointer drag、wheel、focus、context menu を持つ `UiHost` が前提です。platform semantic adapter はなく、GPU 固有機能は使いません。"),
            Api = new(
                "NodeGraphView",
                "`source` / `document`、`viewWidth` / `viewHeight` に加え、`Graph`、`Viewport`、件数・選択 query、`Load`、`PanBy`、`ResetView`、`FitToView`、`AutoLayout`、`Undo` / `Redo`、`ApplyEdit`、`SetDecorations` を公開します。",
                "文書が変わる edit / undo / redo で `OnEdit(NodeGraphView)` を通知します。選択と viewport だけの変更は dirty edit として通知せず、永続化は document owner が行います。"),
        },
        Page(
            "TextEditorView",
            "Editor",
            "TextEditorView",
            "複数行 text / code / Markdown を transaction、複数 selection、IME、scroll、装飾 provider、inline widget とともに編集・表示する canvas editor です。",
            ["複数行の text や code に caret、selection、undo / redo、検索、syntax / diagnostics、IME が必要な場合。", "read-only Markdown、live preview、block / inline widget を同じ text geometry 上へ構成する場合。"],
            ["短い一行入力、単純な非選択ラベル、巨大文書の viewport virtualization、native textbox semantics が必須の場合。"],
            "TextEditorView(value, editorWidth: 640, editorHeight: 360)",
            "`Signal<string>` が外部の text 正本で、各 document transaction 後に即時書き戻します。view は `EditorState`、複数 selection、scroll、search、composition と内部 history を持ちます。外部 Signal から異なる全文が入ると state を作り直し、history と scroll を reset します。保存 / dirty / conflict は呼び出し側が所有します。",
            "`value`、`editorWidth` / `editorHeight`、`fontSize` と、`ReadOnly`、`Fill`、`WrapText`、font / appearance、providers、language service、block / selection contributions、widget resolver、`OnEdit` が中心です。",
            "click で caret、`Shift`+click と drag で selection、`Alt`+click で追加 caret を作ります。wheel は縦 scroll、右クリックは selection / clipboard / insert context menu、block rail は追加 menu と drag reorder を提供します。",
            "背景、focus、selection、caret、current line、diagnostics、syntax、search mark はテーマと provider から描画します。既定は 360 × 200 px、最小 120 × 40 px、通常 line-height 1.5 で、`Fill` は親の有限制約を使います。",
            "refresh は provider を再実行し、全 line / color run の scene、selection、overlay を再構築します。viewport text virtualization はありません。large document、provider cost、decoration 数、hosted widget 資源を測定し、保存、merge、file encoding、完全な IDE service は外側で実装します。",
            "TextEditorView",
            alternatives: [
                new("TextField", "一行の短い値を軽量に入力し、Signal へ即時反映します。"),
                new("Text", "選択や編集のない一様な文字列を表示します。"),
                new("RichTextView", "少量の styled spans を選択なしで表示します。")
            ],
            keyboard: [
                new("矢印、`Home` / `End`", "caret を grapheme / visual line 上で移動し、`Shift` 併用で主 selection を拡張します。"),
                new("`Enter`", "popup がなければ改行を一つの transaction として挿入し、completion / slash menu 中は選択候補を確定します。"),
                new("`Tab` / `Shift+Tab`", "標準 `UiHost` は editor へ渡す前に focus traversal として消費します。view handler に 4 spaces 挿入と popup 確定はありますが、標準 host では到達しません。"),
                new("`Backspace` / `Delete`", "各 selection または前後の grapheme を transaction で削除します。"),
                new("`Ctrl+A` / `Ctrl+C` / `Ctrl+X` / `Ctrl+V`", "全選択と platform clipboard の copy / cut / paste を行います。read-only では copy と selection は可能ですが文書変更は無視します。"),
                new("`Ctrl+Z` / `Ctrl+Y`", "内部 history を undo / redo し、Signal と `OnEdit` を更新します。"),
                new("`Ctrl+D`", "現在語または selection と同じ次の occurrence を複数 selection に追加します。"),
                new("`Ctrl+Alt+Up` / `Ctrl+Alt+Down`", "主 caret と同じ visual x の上下行へ caret を追加します。"),
                new("`Alt+Up` / `Alt+Down`、`Shift+Alt+Down`", "行を移動し、または現在行を複製します。"),
                new("`Ctrl+/`", "現在行または selection 行の line comment を切り替えます。"),
                new("`Ctrl+Space`、popup の `Up` / `Down` / `Enter` / `Escape`", "language completion を開き、候補移動、確定、閉鎖を行います。"),
                new("`Escape`", "completion / slash menu を閉じ、popup がなければ secondary cursors を解除します。")
            ],
            related: [
                new("Controls/Editor/TextEditorView/Code", "code 表示", "line number、syntax、diagnostics、current-line provider を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/Edit", "行操作と検索置換", "行移動・複製・comment、match navigation、ReplaceAll を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/MultiCursor", "複数 caret", "`Ctrl+D`、縦列 caret、`Alt`+click と一括編集を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/Completion", "completion と hover", "language service の popup、filter、確定、dwell tooltip を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/Strudel", "再生 mark と Slider", "高頻度 decoration と文中 Slider を同じ editor に載せます。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/RichText", "rich decoration", "font variant、scale、foreground を同じ text model に適用します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/Markdown", "read-only Markdown", "Markdown marker を隠した折り返し文書と click offset を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/LivePreviewStory", "Markdown live preview", "caret 行だけ marker を見せる editable preview を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/MilkdownStyleEditor", "block editor", "selection toolbar、block menu、table / image embed、appearance をまとめて確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/MarkdownDocStory", "Markdown document", "read-only + wrap の document factory と日本語 fallback を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/MarkdownFillStory", "Fill document", "親 constraint に合わせた再折り返しを確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/DocBridge", "DocString bridge", "既存 DocString と live widget を新 text stack へ移行する例です。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/DocEmbeds", "diagram / math embed", "自動高さ block widget で diagram と数式を埋め込みます。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/Embed", "live UI embed", "embed fence を `WidgetResolver` で実 Widget へ解決します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/BlockWidgetAuto", "自動高さ block widget", "自然高さの測定と次 frame での geometry 収束を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/BlockWidget", "固定高さ block widget", "複数 source 行を一つの全幅 widget へ置換します。", StoryKind.Unspecified),
                new("Controls/Editor/TextEditorView/Widgets", "inline widget", "行頭 decoration と文中 Switch の focus / state を確認します。", StoryKind.Unspecified),
                new("Examples/Workbench/Files", "file workbench", "TextEditor document、tab、save / reload、status を組み合わせた lifecycle を確認します。", StoryKind.Unspecified)
            ]
        ) with
        {
            Anatomy = "clip された editor root に background / focus、任意の line-number gutter、scrolling text / selection / overlay / caret layer、popup / selection toolbar、block rail、hosted widget layer、一つの `FocusTarget` / `ITextInput` を持ちます。",
            Variants = "plain text / code、`ShowLineNumbers`、`WrapText`、`ReadOnly`、`Fill`、font face と `TextEditorAppearance`、decoration providers、search、language completion、Markdown `BlockProvider`、selection / insert action、inline / block widget を段階的に追加できます。",
            FocusActivationDismissal = "pointer 操作または `Tab` traversal で focus し、focus ring と blinking caret を表示します。completion / slash menu は `Escape` で閉じ、read-only でも selection と scroll は残ります。editor 自身を閉じる操作や保存 commit はありません。",
            Accessibility = new(
                "外側に editor / document の見える名前、language、read-only 状態、error summary を用意します。",
                "`ISemanticDocument` は Gallery の `DocSource` / `DocEmbeds` を static index / export へ渡す契約であり、textbox / document accessibility role ではありません。`ISemanticProvider` は実装しません。",
                "caret、複数 selection、read-only、diagnostic、completion、dirty state を platform semantic state として公開しません。dirty と保存結果は shell で別表示します。",
                "本文、selection、caret、gutter、syntax、diagnostic、search、popup の各色を Light / Dark と custom provider で確認します。diagnostic は色だけでなく一覧や文言も併設します。",
                "caret は約 0.53 秒で点滅し、dwell hover は 30 frame 後に開きます。scroll / popup は入力へ即時追従し、標準 transition はありません。",
                "screen reader の text range、line / column、diagnostic relation、completion semantics はありません。重要な編集では platform adapter または代替フォームが必要です。"),
            ThemeLayout = new(
                "`SurfaceAlt`、`Surface`、`Text`、`TextMuted`、`Primary` と provider の decoration 色を使い、font fallback と bold / italic / mono face は呼び出し側が供給します。",
                "`Fill=false` は `editorWidth` / `editorHeight`、`Fill=true` は親の有限 constraint を使います。wrap は `TextWrap.Word`、非 wrap は無限 content width で、縦 scroll bar を付けます。",
                "幅は最小 120、高さは最小 40、既定 360 × 200 です。`Appearance.LineHeight` の既定は 1.5、wrap line height は別指定できます。"),
            Constraints = new(
                "標準 host では `Tab` が focus traversal へ予約されます。viewport virtualization はなく、provider と全行 scene を refresh するため、巨大 file や高頻度 Signal では計測と throttling が必要です。",
                "外部 value の全文置換は history / scroll を reset します。hosted widget は位置だけ変わる場合は再利用し、幅変更や child realize 要求で組み直し、除去時に scope と `IDisposable` を解放します。",
                "IME は `ITextInput` bridge に依存し、composition 中は secondary cursors を解除して主 caret だけを使い、確定時に一つの transaction として Signal へ反映します。clipboard、font、language service、resource host は platform / caller 依存です。"),
            Api = new(
                "TextEditorView",
                "`value`、表示寸法、font size に加え、font / `Fonts` / `Appearance`、`WrapText`、`ReadOnly`、`Fill`、`ShowLineNumbers`、`ShowBlockControls`、`Providers`、`LanguageService`、`WidgetResolver`、block / selection contribution、search / replace、undo / redo API を公開します。",
                "document transaction、undo、redo の後に value Signal を更新し、文書変更時だけ `OnEdit(TextEditorView)` を呼びます。`OnClickOffset` は click の source offset を通知し、`OnKeyIntercept` は host から配送された既定処理前の key を横取りできます。"),
        },
        Page(
            "PropertyGrid",
            "Editor",
            "PropertyGrid",
            "対象 object の対応する public 読み書き member を宣言順に発見し、型別 editor と group 見出しへ投影する inspector です。",
            ["config、ECS component、boxed struct などの scalar property を、型に応じた標準 editor で即時編集する場合。", "複数 view と undo / redo を共有する reflected property document を inspector として表示する場合。"],
            ["任意の nested object / collection、custom row template、read-only property、複数選択の mixed value、AOT で任意 reflection を避ける必要がある場合。"],
            "PropertyGrid(target, width: 330, onChanged: (_, name, value) => Save(name, value))",
            "target と最終的な domain state は呼び出し側が所有します。`ReflectedPropertyController` は accepted value、draft diagnostic、property 単位の history を所有し、成功した editor 変更を target へ即時適用します。parse 失敗や setter 例外では accepted value と history を変えません。",
            "`target`、`width`、`OnChanged`、`Controller`、`UseController`、`Refresh`、`Discover` と、`PropertyRange` / `PropertyGroup` / `PropertyIgnore` が中心です。",
            "各 row は組み込まれた `Check`、`Select`、`ColorPicker`、`Slider`、`TextField`、`LengthField` の pointer 操作に従います。grid 自身の row selection、drag、context menu はありません。",
            "名前列、8 px gap、150 px editor 列を横並びにし、group 間へ divider を置きます。全幅の既定は 300 px、名前列は少なくとも 60 px です。色と文字は子 control と `UiTheme` に従います。",
            "対応型は bool、int、float、string、uint、enum、Vector2 / Vector3、Length です。nested object、nullable、collection、custom editor、virtualization はありません。reflection API は trimming / Native AOT で注記対象です。",
            "PropertyGrid",
            alternatives: [
                new("Grid", "label relation、validation message、任意の field 構成を明示的に設計します。"),
                new("DataGrid", "同じ schema の多数 record を仮想化して比較・選択します。")
            ],
            keyboard: [
                new("`Tab` / `Shift+Tab`", "`UiHost` が focusable な型別 editor 間を次 / 前へ移動します。PropertyGrid 自身には focus target がありません。"),
                new("TextField の編集キー", "string、int、range なし float、Vector 軸では caret、selection、delete、clipboard を各 TextField が処理します。"),
                new("Select の `Enter` / `Space` / `Up` / `Down`", "enum menu を開閉し、選択 index を変更して即時 commit します。"),
                new("Slider の `Left` / `Right`", "`PropertyRange` 付き float を step 単位で変更し、連続する値を即時 commit します。"),
                new("ColorPicker の `Enter` / `Space`", "uint color editor の picker overlay を開閉します。"),
                new("Undo / redo", "grid 固有 shortcut はありません。共有 `Controller.Undo()` / `Redo()` または shell command から実行します。")
            ],
            related: [
                new("Examples/Workbench/Inspector", "inspector workbench", "ObjectDocument、PropertyGrid、save、dirty、undo / redo を一つの editor lifecycle で確認します。", StoryKind.Unspecified),
                new("Controls/Input/Slider/Examples/Interactive", "Slider の対話調整", "range 付き float row が利用する Slider の連続値更新を確認します。", StoryKind.Example)
            ]
        ) with
        {
            Anatomy = "任意の group 見出しと divider、member 名の `Text`、対応型から選んだ一つの editor を `VStack` / `HStack` で組み立てる `CompositeControl` です。target が null または対応 member がない場合は空状態文を表示します。",
            Variants = "bool=Check、enum=Select、uint=ColorPicker、`PropertyRange` 付き float=Slider、int / string / range なし float=TextField、Vector2 / Vector3=軸別 TextField、Length=LengthField です。`PropertyGroup` と `PropertyIgnore` で投影を調整します。",
            FocusActivationDismissal = "PropertyGrid 自身は focus target ではなく、子 editor が個別に focus します。`Tab` は host traversal です。overlay を持つ Select / ColorPicker の閉じ方は各 control の契約に従います。",
            Accessibility = new(
                "各 member 名を見える label として表示し、group 名は人が理解できる文言にします。",
                "grid / form / group semantic role と、label から editor への relation を公開しません。子 editor の semantic 対応も各実装の範囲です。",
                "accepted value、draft、parse diagnostic、setter rejection、dirty / undo state を支援技術へ公開しません。diagnostic は `Controller.DraftOf` で取得できますが grid は自動表示しません。",
                "label、group、divider、focus、子 editor の value / error 色をテーマごとに確認し、invalid を色だけで示さず説明文を追加します。",
                "grid 自身の animation はなく、Slider や overlay など子 control の動作だけが発生します。",
                "完全な form semantics、error relation、read-only / required state はありません。業務フォームには明示構成した control を使います。"),
            ThemeLayout = new(
                "member / group text は `Text` / `TextMuted`、divider と子 editor はそれぞれの `UiTheme` token を使います。",
                "全幅から 150 px editor 列と 8 px gap を引き、名前列を最低 60 px にします。Vector field は 2 または 3 軸へ等分します。",
                "既定幅は 300 px です。狭すぎる幅では固定 editor 列により overflow し得るため、inspector pane に十分な幅を確保します。"),
            Constraints = new(
                "数値 pattern は `-` や `1.` の一時 draft を許し、parse 失敗中は target と history を変えません。setter 例外も diagnostic に変換して rollback します。validation message の描画は呼び出し側です。",
                "target reference が変わると内部 controller を作り直します。外部変更後は `Refresh()`、複数 view / history 共有には `UseController()` を使います。同値 refresh は history を保持し、異なる外部値は history を reset します。",
                "任意 runtime member の発見は `RequiresUnreferencedCode` です。Native AOT / trimming では generated / static descriptor を使う別経路を検討します。"),
            Api = new(
                "PropertyGrid",
                "`target`、`width`、`OnChanged` に加え、`Controller`、`UseController`、`Refresh`、`EditorOf`、static `Discover`、`PropertyRangeAttribute`、`PropertyGroupAttribute`、`PropertyIgnoreAttribute` を公開します。",
                "target への setter が成功した後だけ `OnChanged(PropertyGrid, memberName, value)` を通知します。parse / adapter rejection では通知せず、詳細は controller の result / draft diagnostic から取得します。"),
        },
        Page(
            "StatusBar",
            "Editor",
            "StatusBar",
            "26 px の画面端 surface に、leading / center / trailing の状態 contribution を配置し、幅不足時に低 priority 項目を畳む status 表示です。",
            ["active document、cursor、encoding、build / connection 状態など、短く継続的な補助情報を editor 端へ置く場合。", "安定 key と priority を持つ contribution を幅に応じて省略する場合。"],
            ["確認が必要な error、履歴、長文、必ず操作できる overflow menu、画面読み上げが必須の live notification を置く場合。"],
            "StatusBar(items: [new StatusBarItem(\"cursor\", Text(\"行 12, 桁 4\"), StatusBarRegion.Trailing, Priority: 100)])",
            "各 `StatusBarItem.Content` と表示値は呼び出し側が所有します。StatusBar は layout ごとに visible / collapsed key を派生し、選択、commit、validation state は持ちません。legacy `left` / `center` / `right` 配列も表示できます。",
            "`left`、`center`、`right`、structured `items` と、`CollapsedKeys`、`VisibleKeys`、`HasOverflow`、`SeparatorCount`、static `Collapse` が中心です。",
            "bar 自身は pointer hit target を持ちません。配置した child Widget の操作だけが各 child の契約で動きます。幅不足時の `+N` は表示だけで、click / menu / tooltip はありません。",
            "`SurfaceAlt` の 26 px surface、上端 hairline、任意 separator を使います。leading は左から、trailing は右から、center は左右 contribution と重ならない範囲で中央へ置きます。有限な親幅がなければ 480 px を使います。",
            "structured item は `PreferredWidth` と priority で collapse しますが、legacy 配列は常に残り、overflow し得ます。大量項目の仮想化、wrap、scroll、interactive overflow はありません。重要な情報を collapsed count の背後だけに置きません。",
            "StatusBar",
            alternatives: [
                new("Toast", "一時的な処理結果を独立した通知面へ表示します。"),
                new("Alert", "重要な error / warning を本文中の明示領域で説明します。")
            ],
            keyboard: [
                new("`Tab` / `Shift+Tab`", "StatusBar 自身は focus せず、focus target を持つ child Widget があれば `UiHost` traversal に参加します。"),
                new("その他", "bar、separator、`+N` overflow indicator に固有の key binding はありません。child の操作は child control に従います。")
            ],
            related: [
                new("Examples/Workbench/Files", "file workbench", "active path、external change、storage 状態を live StatusBar へ投影します。", StoryKind.Unspecified),
                new("Examples/Workbench/Inspector", "inspector workbench", "active object document と path を editor shell の status surface に表示します。", StoryKind.Unspecified),
                new("Examples/Workbench/Material", "material workbench", "active graph / shader kind と保存 workflow の状態を表示します。", StoryKind.Unspecified)
            ]
        ) with
        {
            Anatomy = "一つの背景 surface と上端 hairline、0 個以上の separator、leading / center / trailing に layout した child Widget、必要時の右端 `+N` 描画から構成します。",
            Variants = "互換用 `left` / `center` / `right` Widget 配列と、`Key`、`Region`、`Priority`、`Visible`、`Separator`、`PreferredWidth` を持つ structured `StatusBarItem` を併用できます。",
            FocusActivationDismissal = "bar 自身と overflow indicator は focus / activation / dismissal を持ちません。child が focusable なら child だけが focus し、status surface は常設です。",
            Accessibility = new(
                "各 structured item の `Key` は semantic label にも使われるため、安定性に加えて人が理解できる短い識別子を選び、visible content にも状態文を置きます。",
                "root と structured item ごとに `SemanticRole.Status` を公開します。legacy 配列の item と child content の詳細 semantics はこの snapshot に含めません。",
                "`Visible`、collapsed、priority、overflow count を semantic state として公開せず、item label は content text ではなく `Key` です。",
                "surface、hairline、separator、muted text、badge のコントラストを確認し、warning / error は label と intent を併用します。",
                "アニメーションはありません。幅変更時に visible contribution が即時切り替わります。",
                "`Status` role が platform live-region announcement になる保証はありません。緊急 error や操作必須通知の唯一の経路にはしません。"),
            ThemeLayout = new(
                "bar は `SurfaceAlt`、hairline / separator は `BorderColor`、overflow count は `TextMuted` を使い、child は各自の theme に従います。",
                "左右 padding は 10 px、item gap は 8 px、separator space は 9 px、overflow reservation は 38 px です。center は leading / trailing の間へ clamp されます。",
                "高さは常に `BarH = 26` px です。幅は Widget 共通の `width`、有限な親幅、既定 480 px の順で決まり、item content は `PreferredWidth` 以上の実測幅を使います。"),
            Constraints = new(
                "collapse は `Visible=true` の structured item から低 `Priority`、同値なら `Key` 順に除外します。`+N` は hidden item を開く affordance ではありません。duplicate / 空 key は layout 時に例外です。",
                "StatusBar は child Widget を所有 tree に組み込みます。表示 state の Signal / Bindable と child resource の寿命は呼び出し側および Widget scope に従います。",
                "framework semantic snapshot は利用できますが、native live announcement と child semantics の統合は platform adapter 次第です。pointer / keyboard input は child が必要な場合だけ使います。"),
            Api = new(
                "StatusBar",
                "`left` / `center` / `right`、`items`、`StatusBarItem` の `Key` / `Content` / `Region` / `Priority` / `Visible` / `Separator` / `PreferredWidth` と、collapse 結果の query API が中心です。",
                "固有イベントはありません。状態更新は item content の Signal / Bindable または `items` の差し替えで行い、overflow click や item activation は child 側で実装します。"),
        },
    ];
}
