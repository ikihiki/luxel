using Luxel.Gallery;
using Luxel.Gallery.UI;

namespace Luxel.UI.Gallery;

internal static partial class UiControlDocs
{
    private static readonly ControlDocsPage[] OverlayPages =
    [
        Page(
            "Dialog",
            "Overlay",
            "Dialog",
            "現在の作業を一時的に中断し、確認または短い入力を完了するまで背面操作を止める modal overlay です。`Dialog` 自身はタイトル、本文、操作列を持たず、一つの `panel` Widget を `UiHost` の中央へ portal 表示します。",
            [
                "破壊的・不可逆な操作を確定する前に、明示的な確認を必須にする場合。",
                "短い入力や判断を完了するまで、背面のポインター、キーボード、スクロール、ドラッグ＆ドロップを止める場合。",
            ],
            [
                "作業を遮らない補助内容や任意の操作を起点の近くへ出す場合。`Popover` を使います。",
                "長時間の編集、複数段階の作業、常設の作業面を一つの modal panel に詰め込む場合。",
                "処理結果を通知するだけの場合。`Toast` または常設の `StatusBar` を使います。",
            ],
            "var open = new Signal<bool>(false);\nDialog(open, Card(content))",
            "開閉は呼び出し側の `Signal<bool>` が所有し、パネル内の入力値も呼び出し側モデルで保持します。外側クリックと `Escape` は同じ `open` へ `false` を書き戻します。閉じたこと自体は確定・取消の意味を持たないため、業務結果は各操作 handler で別に記録します。",
            "`open` と `panel` の二つが固有パラメーターです。タイトル、本文、操作ボタンは `panel` の Widget 構成として渡します。",
            "パネル内の Widget を操作します。モーダル overlay のため外側クリックと `Escape` は既定で `open` を `false` にし、背面入力を遮断します。外側クリックは閉鎖に消費され、同じクリックで背面操作は起動しません。",
            "Dialog が加える視覚要素は黒 45% の scrim だけです。panel の Surface、境界、角丸、余白、文字、操作の Intent は呼び出し側で構成します。",
            "本体は 0 サイズの portal で、表示には `UiHost` の overlay 層が必要です。長大な作業面や常設ナビゲーションは `panel` に載せず、長い内容には panel 内のスクロール手段を用意します。",
            "Dialog",
            alternatives:
            [
                new("Popover", "背面を操作可能なまま、短い補助内容をアンカー近くへ表示します。"),
                new("Drawer", "右端の全高 panel へ詳細やフィルターをまとめます。現実装の Drawer も modal です。"),
                new("Toast", "確認を要求せず、処理結果を右下へ通知します。"),
            ],
            keyboard:
            [
                new("`Escape`", "現在の focus target が消費しなければ、最後に登録された dismissible overlay として `open` を `false` にします。"),
                new("`Tab` / `Shift+Tab`", "panel runtime scope 内に登録された focus target だけを循環します。"),
            ],
            related:
            [
                new("Tutorials/UIApp/DialogSample", "UIApp の確認例", "起点、取消、確定を一つの `open` Signal へ接続する構成を確認します。", StoryKind.Unspecified),
            ]
        ) with
        {
            Anatomy = "通常レイアウトでは 0 × 0 の portal 本体です。開いている間だけ、canvas root 直下の overlay layer、画面全体の scrim、中央の holder、その中の一つの `panel` Widget を実体化します。起点ボタンや閉じる操作は Dialog の構造に含まれません。",
            Variants = "配置は `OverlayPlacement.Center`、`Modal = true` 固定です。専用の title、description、action、size、Intent、animation variant はありません。確認、入力、警告の違いは `panel` の Widget tree と呼び出し側の状態で表します。",
            PointerInteraction = "panel 内では content が登録した hit target を通常どおり操作します。panel 外の左 click / pointer down は `open` を false にしてその入力を消費します。modal 中は背面の click、context click、wheel、drag/drop を scope gating で遮断し、同じ入力を背面へ再配送しません。",
            FocusActivationDismissal = "開く直前の登録済み focus target を保存し、panel runtime scope 内で最初に登録された focus target へ移動します。`Tab` はその scope 内に trap されます。focusable content がない場合は背景 focus を外したまま focus なしになり、panel や scrim 自体へは移りません。閉じると保存先がまだ登録済みの場合だけ復元します。起点が現実装の `Button` のように focus target を登録しない場合、復元先はありません。`Escape` は focus 中コントロールへ先に配送され、そこで消費されなかった場合だけ overlay を閉じます。",
            Accessibility = new(
                "`panel` 内に見えるタイトルを置き、各入力と主要・取消操作を曖昧でない文字列で示します。Dialog は title/description を受け取らないため、関係付けも panel 側で設計します。",
                "`Dialog` は `ISemanticProvider` を実装せず、現行 `SemanticRole` に dialog role もありません。modal な入力 scope と semantic dialog は別物です。",
                "`open`、検証エラー、確定可否は支援技術向け状態として自動公開されません。色だけでなく見える文字でも状態を示します。",
                "scrim 上の panel 境界、本文、主要操作、取消操作、focus 表示を light/dark の両テーマで確認します。",
                "Dialog 自身は開閉アニメーションを定義せず、Signal 変更時に runtime を開閉します。重要な変化を motion だけで伝えることはありません。",
                "semantic name、description、dialog role、既定操作、読み上げ順は host へ自動接続されません。キーボード利用を要件にする場合は、panel 内に実際に focus を登録するコントロールがあることを検証します。"),
            ThemeLayout = new(
                "scrim は固定の黒、opacity 0.45 です。panel は自動で Card 化されないため、`Card`、`Border`、theme bind などを明示します。",
                "visual layer は canvas root 直下の `Z = 1000`、content の既定 hit layer は 2000 です。同じ Z の overlay は登録順で積まれ、dismissal と top-modal 判定は登録リストの末尾から行われます。",
                "中央配置では既定 `Margin = 16` により panel の layout 上限は viewport の幅・高さから各 32 px を引いた値です。自動スクロールはないため、有限幅を与え、長い本文や入力列は panel 内でスクロールさせます。"),
            Constraints = new(
                "同時に複数の peer Dialog を開かないよう一つの owner が順序を管理します。nested modal は最後に登録されたものが focus trap と dismissal の対象になり、親 Dialog 内の操作は子が閉じるまで受け付けません。",
                "Dialog を実体化した `RealizeScope` が `OverlayEntry` を所有します。`open = true` で content 用 runtime scope を作り、閉鎖時に node、hit、focus、effect、子 overlay を破棄します。所有 Widget が再実体化または破棄されると overlay も自動解除されます。panel 内で登録した nested overlay はこの runtime の子になり、親と一緒に閉じます。",
                "この契約は retained `UiHost` の overlay、focus、input routing に基づきます。Dialog-level semantics を native/browser host へ変換する契約は現時点でありません。"),
            Api = new(
                "Dialog",
                "`open` と `panel` の二つが固有パラメーターです。タイトル、本文、操作ボタンは `panel` の Widget 構成として渡します。共通 Widget パラメーターを Dialog 本体へ指定しても、portal content の寸法や見た目にはなりません。",
                "固有イベントはなく、dismissal は `open` Signal を `false` に書き戻します。確定、取消、検証、保存は panel 内イベントが所有し、必要なら処理後に同じ `open` を更新します。"),
        },
        Page(
            "Drawer",
            "Overlay",
            "Drawer",
            "画面右端へ全高の補助 panel を重ね、閉じるまで背面操作を止める modal portal です。詳細、フィルター、狭い画面の一時ナビゲーションを主画面から分離します。",
            [
                "現在の文脈を残したまま、詳細、プロパティ、フィルターを右端の一時 panel で編集する場合。",
                "狭い viewport でナビゲーションを一時表示し、選択または取消まで背面入力を止める場合。",
            ],
            [
                "常に表示するナビゲーションやツール面が必要な場合。`DockHost` など通常レイアウトを使います。",
                "背面を操作可能な非 modal inspector が必要な場合。現実装の Drawer は必ず modal です。",
                "左、下、任意 anchor など右端以外へ配置する場合。Drawer には side パラメーターがありません。",
            ],
            "var open = new Signal<bool>(false);\nDrawer(open, Card(details))",
            "開閉は呼び出し側の `Signal<bool>` が所有し、パネル内の業務状態も外部モデルで保持します。外側クリック、`Escape`、panel 内の完了・取消操作を同じ `open` へ集約します。",
            "`open` と `panel` の二つが固有パラメーターです。表示辺や幅を切り替えるパラメーターはなく、配置は右端です。",
            "パネル内の Widget を操作します。モーダル overlay のため外側クリックと `Escape` は既定で `open` を `false` にし、背面入力を遮断します。外側クリックは閉鎖だけに使われ、背面へ再配送されません。",
            "Drawer 自身は scrim 以外の panel surface を描きません。panel の背景、境界、影、余白と視覚階層を theme へ接続します。",
            "本体は 0 サイズの portal で、表示には `UiHost` の overlay 層が必要です。左端・下端などを選ぶ汎用 drawer ではなく、内容のスクロールや幅の上限も panel 側の責務です。",
            "Drawer",
            alternatives:
            [
                new("DockHost", "常設 panel を分割・ドッキングし、主レイアウトの一部として保持します。"),
                new("Dialog", "中央の短い確認または入力へ利用者の注意を集中させます。"),
                new("Popover", "背面を操作可能なまま、anchor に結び付いた小さい補助面を出します。"),
            ],
            keyboard:
            [
                new("`Escape`", "focus 中コントロールが消費しなければ、UiHost の dismissal で `open` を `false` にします。"),
                new("`Tab` / `Shift+Tab`", "Drawer panel runtime scope 内の focus target だけを循環します。"),
            ]
        ) with
        {
            Anatomy = "通常レイアウトでは 0 × 0 の portal 本体です。開いている間だけ、canvas root 直下の overlay layer、全画面 scrim、右端 holder、その中の一つの `panel` Widget を実体化します。起点と close affordance は panel 外または panel 内に呼び出し側が用意します。",
            Variants = "`OverlayPlacement.RightEdge` と `Modal = true` 固定です。side、width、title、actions、animation の専用 variant はありません。幅と内容構造は `panel` Widget、状態は外部 Signal とモデルで表します。",
            PointerInteraction = "panel 内では子 Widget の pointer 操作を使います。panel 外の左 click / pointer down は Drawer を閉じるために消費されます。modal scope 外の click、context click、wheel、drag/drop は背面へ届かず、右端 panel を閉じずに背面だけ操作する mode はありません。",
            FocusActivationDismissal = "開くと直前の登録済み focus target を保存し、Drawer runtime scope 内の最初の focus target へ移動して Tab を trap します。focusable content がなければ focus は空になり、panel 自体へは移りません。閉鎖後は保存先がまだ有効な場合だけ復元します。`Escape` は focus 中コントロールが先に消費できます。外側クリックは閉鎖に消費されます。",
            Accessibility = new(
                "`panel` の先頭に見える見出しを置き、閉じる操作、入力、適用・取消を具体的な文字列で示します。",
                "`Drawer` は `ISemanticProvider` を実装せず、専用の drawer/dialog role、name、description 関係を公開しません。modal input scope だけが host で成立します。",
                "`open`、適用済み、未保存、検証エラーなどは自動公開されないため、panel 内の文字と各コントロールの状態で示します。",
                "scrim と panel の境界、本文、操作、focus 表示が theme 上で識別できることを確認します。",
                "Drawer 自身は slide/fade animation を持たず、開閉時に runtime を即時作成・破棄します。",
                "semantic region としての到達、見出しとの関連付け、閉じる既定操作は host へ自動接続されません。キーボード要件がある panel には実際の focus target を含めます。"),
            ThemeLayout = new(
                "scrim は固定の黒 45% です。panel の Surface、角丸、境界、影、余白は呼び出し側が theme bind します。",
                "visual layer は `Z = 1000`、content hit layer は既定 2000 です。右端配置は x を `viewport width - panel width`、y を 0 とし、panel 高さへ viewport 高さの tight constraint を渡します。",
                "高さは常に viewport 全高です。幅は panel の layout 結果で 0 から viewport 幅までになり、専用の既定幅はありません。小さい画面では panel に有限幅または responsive な幅を指定し、長い内容は panel 内でスクロールさせます。"),
            Constraints = new(
                "右端以外の edge、non-modal mode、resizable rail は提供しません。複数 Drawer や Dialog と同時に開く場合、最後の modal だけが top-modal となるため、呼び出し側で一つずつ開く方針を持ちます。",
                "所有 `RealizeScope` が `OverlayEntry` を登録し、`open` 中だけ panel runtime を所有します。閉じると panel 配下の hit、focus、effect、nested overlay を破棄し、owner が破棄されても自動解除します。panel 内の Popover などは同じ scope 階層へ置くと親 Drawer と一緒に片付きます。",
                "retained `UiHost` の overlay と modal input gating が必要です。Drawer-level accessibility semantics を platform adapter へ公開する契約はありません。"),
            Api = new(
                "Drawer",
                "`open` と `panel` の二つが固有パラメーターです。表示辺や幅を切り替えるパラメーターはなく、配置は右端です。幅、surface、close affordance は `panel` 側で指定します。",
                "固有イベントはなく、dismissal は `open` Signal を `false` に書き戻します。適用・取消の意味は panel 内 handler が所有し、Drawer の閉鎖だけを業務処理の成功として扱いません。"),
        },
        Page(
            "Dropdown",
            "Overlay",
            "Dropdown",
            "組み込みの tonal trigger と短い action list を一体で提供する、pointer 中心の non-modal menu control です。任意 anchor や任意 menu content を受け取る汎用 popup ではありません。",
            [
                "一つのラベル付き trigger から、少数の即時 action を文字列一覧で実行する場合。",
                "各項目が同期 `Action` で完結し、icon、checked state、submenu、検索を必要としない場合。",
            ],
            [
                "一つの値を候補から選び、現在値や disabled item を表す場合。`Select` を使います。",
                "任意 Widget、小さいフォーム、説明を anchor 近くへ出す場合。`Popover` を使います。",
                "矢印キー移動、type-ahead、menu/menuitem semantics が必須の command menu として使う場合。現実装は pointer 契約だけです。",
            ],
            "Dropdown(\"操作\", [(\"保存\", Save), (\"閉じる\", Close)])",
            "開閉はコントロール内部の `Opened` Signal が保持します。`open` constructor parameter はありませんが、取得した `Dropdown` インスタンスの `Opened.Value` は観測・更新できます。各 Action の業務結果は呼び出し側モデルが所有します。",
            "`label` と `(string label, Action onClick)[] items` が固有パラメーターです。任意の anchor Widget や menu Widget、配置パラメーターは受け取りません。",
            "トリガーボタンのポインター操作で開閉し、`MenuRow` のクリックで Action を実行して閉じます。外側クリックと `Escape` は UiHost の既定 dismissal で閉じます。外側クリックは背面 action へ通りません。",
            "trigger は `Variant.Tonal` / `Intent.Neutral`、menu surface は theme `Surface` と `Radius`、行 hover は `SurfaceAlt` を使います。",
            "項目はラベルと同期 Action の組に限られ、全件を一度に `MenuRow` へ展開します。任意 Widget、検索、virtualization、scroll container は提供しません。",
            "Dropdown",
            alternatives:
            [
                new("Select", "現在値を持つ単一選択とキーボード選択を扱います。"),
                new("Popover", "外部 trigger/anchor と任意 Widget content を組み合わせます。"),
                new("MenuRow", "独自の menu surface を組むときの一行だけを提供します。"),
            ],
            keyboard:
            [
                new("`Escape`", "開いている menu を閉じます。focus 中コントロールが先に Escape を消費した場合は閉じません。"),
            ]
        ) with
        {
            Anatomy = "通常レイアウトに置かれる一つの tonal `Button` と、開いている間だけ overlay layer に実体化する `Border` + `VStack` + `MenuRow[]` から構成します。anchor は Dropdown 自身の `WorldPos` と `Size` です。",
            Variants = "trigger variant は tonal/neutral、placement は `OverlayPlacement.Below` 固定です。items は `(label, Action)` だけで、icon、shortcut、separator、checked、disabled、submenu、任意 row Widget、placement の variant はありません。",
            PointerInteraction = "trigger 全体の click で内部 `Opened` を toggle し、menu row 全体の click で対応する `Action` を呼びます。content 外の左 click / pointer down は menu を閉じるために消費されます。hover は各 MenuRow だけが持ち、pointer context menu や wheel に固有処理はありません。",
            FocusActivationDismissal = "現行 `Button` と `MenuRow` は focus target を登録しないため、trigger への keyboard focus、Enter/Space 起動、矢印キー移動、項目 focus はありません。開いても focus を移動・trap・復元しない non-modal overlay です。外側クリックまたは `Escape` で閉じ、light-dismiss に使ったクリックは消費します。親 modal の panel 内で実体化した Dropdown はその modal scope に属し、親が閉じると menu も破棄されます。",
            Accessibility = new(
                "`label` と各 item label を、文脈なしでも判別できる具体的な action 名にします。同じ動詞だけが並ぶ場合は対象も含めます。",
                "`Dropdown`、内部 `Button`、`MenuRow` は menu/menuitem/expanded semantics を公開せず、`ISemanticProvider` も実装しません。",
                "`Opened`、現在項目、disabled、checked state は semantic state として公開されません。items contract 自体に disabled/checked はありません。",
                "tonal trigger、menu Surface、hover SurfaceAlt、文字が light/dark theme で識別できることを確認します。hover だけを選択状態の意味に使いません。",
                "MenuRow の hover は 80 ms で補間されますが、Dropdown の開閉 animation はありません。",
                "keyboard-only 起動・移動、accessible menu role、expanded relationship、type-ahead は未提供です。これらが要件なら Dropdown を採用しません。"),
            ThemeLayout = new(
                "trigger は theme の tonal neutral style、menu は `Surface`、theme `Radius`、6 px padding、行間 2 px です。MenuRow は通常 `Surface`、hover `SurfaceAlt`、文字 `Text` を使います。",
                "menu は canvas root の overlay visual layer `Z = 1000`、hit layer 2000 へ出ます。`Below` は gap 6、margin 0 の anchored placement へ変換され、下に入らなければ上へ flip し、横方向は viewport 内へ shift します。anchor は open 時に評価され、表示中の移動へ連続追従しません。",
                "menu 幅は trigger 幅へ固定されません。現実装は各 `MenuRow` を `Align.Stretch` で組み、overlay から有限の viewport 幅を受けるため、menu は利用可能な viewport 幅まで広がります。長い label は wrap/ellipsis されず row がその制約を超えることもあるため、短い action 名にし、compact menu 幅が必要なら別構成を使います。"),
            Constraints = new(
                "pointer 専用の簡易 action list です。label の動的更新は初回に作られた内部 trigger へ反映されない現実装のため、label を変える場合は Dropdown インスタンスを再構築します。Action 数が多い、非同期進行を menu 内に残す、項目状態を持つ用途には向きません。",
                "Dropdown の realize scope が overlay entry を所有し、開いている間だけ menu row runtime を作ります。Dropdown が再実体化・破棄されると menu も解除されます。親 modal 外で所有された menu は top modal の input gating 対象外になり操作できないため、modal 内で使う Dropdown は panel subtree 内に置きます。",
                "pointer、Escape、overlay routing は `UiHost` 契約です。menu semantics や native menu adapter はありません。"),
            Api = new(
                "Dropdown",
                "`label` と `(string label, Action onClick)[] items` が固有パラメーターです。任意の anchor Widget や menu Widget、配置パラメーターは受け取りません。公開 `Opened` は内部開閉 Signal への参照です。",
                "固有 `UiEvent` はなく、項目 `Action` を同期実行した直後に内部 `Opened` を `false` にします。Action が例外を投げると UiHost が報告しますが、後続の close 代入は実行されないため menu は開いたままです。"),
        },
        Page(
            "MenuRow",
            "Overlay",
            "MenuRow",
            "menu surface 内の一つの文字 action を描画する、pointer-only の行プリミティブです。menu の open state、配置、focus navigation、項目モデルは持ちません。",
            [
                "独自の menu surface 内で、短い文字 label を持つ一つの即時 action を表示する場合。",
                "Dropdown と同じ Surface / hover 表現を使いながら、行の click handler を個別に構成する場合。",
            ],
            [
                "単独の主要操作として常に表示する場合。`Button` を使います。",
                "icon、shortcut、checked、disabled、submenu、複数行説明を構造化して表示する場合。",
                "keyboard focus や menuitem semantics を必要とする場合。MenuRow は focus target を登録しません。",
            ],
            "MenuRow(\"保存\", _ => Save())",
            "hover は Widget の内部 `Hovered` state、背景・前景・文字サイズは bindable parameter、業務 action と有効可否は呼び出し側が所有します。MenuRow 自身は selected、checked、open、disabled state を持ちません。",
            "`label`、`OnClick`、`fontSize`、stateable な `background` / `foreground` が固有 API です。icon、shortcut、disabled の専用パラメーターはありません。",
            "行全体を pointer hit target として hover と click を受けます。click は `OnClick.Invoke(this)` を同期実行するだけで、周囲の menu や overlay は自動で閉じません。",
            "未指定時は theme `Surface` から `SurfaceAlt` へ hover 補間し、文字は theme `Text`、角丸 5 px、左右 padding 12 px を使います。",
            "label は一行描画で wrap、ellipsis、icon slot、shortcut column を持ちません。長い文字列は自然幅を広げ、親 overlay の viewport clip で切れる可能性があります。",
            "MenuRow",
            alternatives:
            [
                new("Button", "独立した action と明確な button surface を表示します。"),
                new("Dropdown", "trigger、open state、anchored overlay、複数 MenuRow を一体で提供します。"),
            ]
        ) with
        {
            Anatomy = "一つの rounded rectangle scene、中央寄せした一つの text node、行全体の pointer hit target から構成します。overlay、trigger、icon、補助列は構造に含みません。",
            Variants = "`fontSize`、`background`、`foreground` と Widget 共通の width/alignment を変更できます。`background` と `foreground` は stateable ですが、専用の selected、danger、disabled、separator、submenu variant はありません。",
            PointerInteraction = "行矩形全体が hover/click hit target です。pointer enter/leave で `Hovered` を更新し、click で `OnClick` を一度発火します。pointer button の種類や座標は event contract に渡らず、context click、drag、wheel の固有処理もありません。",
            FocusActivationDismissal = "`MenuRow` は `AddFocusable` を呼ばず、Tab 順、Enter/Space 起動、矢印キー移動を提供しません。overlay 内に置いても focus entry や dismissal は追加されません。親 menu を閉じる場合は `OnClick` handler が親の open Signal を更新します。親 overlay scope が閉じると MenuRow の hit/effect も破棄されます。",
            Accessibility = new(
                "見える `label` に action と対象を含めます。icon や shortcut だけで意味を補う API はないため、文字だけで理解できる名称にします。",
                "`MenuRow` は `ISemanticProvider` を実装せず、menuitem/button role を公開しません。",
                "selected、checked、expanded、disabled state はありません。共通 `Enabled` を false にしても click handler 内で確認していないため、無効化が必要なら行を出さないか handler で guard します。",
                "通常/hover 背景と文字のコントラストを確認し、hover 色だけで destructive/selected など別の意味を表しません。",
                "hover state は 80 ms の色補間です。action 実行や親 overlay の開閉 animation は管理しません。",
                "keyboard と semantic menuitem の経路がなく、screen reader 向け label/state も自動公開されません。完全な command menu の唯一の項目実装には使いません。"),
            ThemeLayout = new(
                "背景は未指定時 `Surface`、hover 時 `SurfaceAlt`、文字は `Text` です。rounded radius は固定 5 px で、hover transition は 0.08 秒です。",
                "text は x = 12 px、垂直中央へ配置します。row は親から有限幅を受けるとその幅以上へ広がり、Dropdown の `VStack` では stretch されます。",
                "自然幅は text 幅 + 24 px、高さは text 高 + 12 px です。wrap/ellipsis/max-lines はなく、長い label では親側の幅制限と clip を確認します。"),
            Constraints = new(
                "MenuRow 単体には menu container、open/close、focus、keyboard、scroll、virtualization がありません。多数行を直接並べる場合は viewport と input requirements を別途設計します。",
                "実体化 scope が scene、hover effect、hit target を所有します。親 overlay runtime が閉じれば一緒に破棄されますが、`OnClick` 自体は overlay ownership を変更しません。",
                "pointer hit testing と theme transition を持つ retained `UiHost` が前提です。native menuitem semantics は提供しません。"),
            Api = new(
                "MenuRow",
                "`label`、`OnClick` (`UiEvent<MenuRow>`)、`fontSize`、stateable な `background` / `foreground` が中心です。icon、shortcut、disabled、checked、submenu の専用 API はありません。",
                "pointer click ごとに `OnClick` へ発火元の `MenuRow` を一度同期通知します。close、command routing、async completion、enabled guard は handler 側の契約です。"),
        },
        Page(
            "Popover",
            "Overlay",
            "Popover",
            "外部で定義した anchor rectangle の近くへ任意 Widget を重ねる、controlled かつ non-modal な portal です。trigger、surface、focus policy は呼び出し側と content が所有します。",
            [
                "起点との位置関係を保つ短い補助内容、詳細、少数入力を、背面を操作可能なまま表示する場合。",
                "Dropdown の固定 action list では表せない任意 Widget を、明示した `AnchoredPlacement` で表示する場合。",
            ],
            [
                "確認や入力を完了するまで背面を止める場合。`Dialog` または現行の modal `Drawer` を使います。",
                "pointer hover だけの短い一行補足の場合。要件と現行制約を確認して `Tooltip` を検討します。",
                "長時間の編集面や常設 panel として使う場合。通常レイアウトへ置きます。",
            ],
            "Popover(open: open, content: Card(details), anchor: () => triggerRect,\n    placement: new AnchoredPlacement { Side = PopupSide.Below, Align = PopupAlign.Start, Gap = 6, Margin = 4 })",
            "`open` Signal、`content` の業務状態、`anchor` が返す world-space `Rect` は呼び出し側が所有します。Popover は UiHost へ `OverlayEntry` を登録し、外側クリックまたは `Escape` による閉鎖を `open` へ反映します。",
            "`open`、`content`、`anchor`、`placement` が中心です。`AnchoredPlacement` の `Side`、`Align`、`Flip`、`Shift`、`Gap`、`Margin`、`MaxWidth`、`MaxHeight` で open 時の配置を指定します。",
            "Popover 自体には trigger hit target がありません。外部 trigger が `open` を更新し、content 内の hit target が操作を受けます。既定では外側クリックと `Escape` で閉じ、light-dismiss click は背面へ通しません。",
            "Popover は surface を描かないため、content 側で theme `Surface`、境界、角丸、padding、影を構成します。",
            "`UiHost` の overlay layer が必要です。anchor は open 時に一度評価され、表示中の移動や scroll へ連続追従しません。長い content には最大寸法と内部 scroll を用意します。",
            "Popover",
            alternatives:
            [
                new("Tooltip", "操作不能な短い補足を hover で表示します。ただし現行 hover routing の制約があります。"),
                new("Dropdown", "組み込み trigger と文字 Action 一覧だけで足りる場合に使います。"),
                new("Dialog", "背面入力を止め、focus を modal scope に trap します。"),
            ],
            keyboard:
            [
                new("`Escape`", "focus 中コントロールが消費しなければ、最後に登録された dismissible overlay として `open` を `false` にします。"),
            ]
        ) with
        {
            Anatomy = "通常レイアウトでは 0 × 0 の portal 本体です。開いている間だけ canvas root 直下に一つの content Widget を実体化します。trigger、anchor の可視要素、scrim、arrow、surface、close button は含みません。",
            Variants = "既定 `AnchoredPlacement` は `Side = Below`、`Align = Start`、`Flip = true`、`Shift = true`、`Gap = 6`、`Margin = 4` です。`Side` は Below/Above/Right/Left、`Align` は Start/Center/End を選べます。Popover 自体の modal variant はありません。",
            PointerInteraction = "Popover 自身は trigger hit target を持たず、content 内の hit target だけが操作を受けます。content 外の左 click / pointer down は `open` を false にし、light-dismiss に使ったクリックは背面へ通しません。non-modal なので、閉じた後の次の入力から背面を再び操作できます。",
            FocusActivationDismissal = "開いても focus を content へ移動せず、trap も復元も行いません。content 内の focus target は開いている間だけ host の global focusables へ追加されるため、Tab 到達順は trigger 直後とは限りません。focusable content がなくても表示と dismissal は動作します。外部 trigger に keyboard 起動と `open` state の伝達を実装します。外側クリックは閉じるために消費され、`Escape` は focus 中 control が先に処理できます。",
            Accessibility = new(
                "trigger に見える名前を用意し、content 内にも見出し、説明、各入力 label を置きます。Popover は trigger と content の accessible relationship を自動構築しません。",
                "`Popover` は `ISemanticProvider` を実装せず、popover/dialog role、expanded relationship、description association を公開しません。",
                "`open` と placement side は semantic state として公開されません。trigger の expanded 表示や content 内の検証状態は呼び出し側が可視化します。",
                "content の Surface、境界、文字、focus 表示を theme 上で確認し、背面と十分に区別します。",
                "Popover 自身は open/close animation を持ちません。anchor side の変更も animation されません。",
                "automatic focus entry、focus trap、focus restoration、semantic relationship がありません。keyboard-only 利用が必要なら trigger と content の実際の Tab 順を host 上で検証します。"),
            ThemeLayout = new(
                "Popover 自身は色、角丸、影、padding を設定しません。`Card` や `Border` を content root に使い、利用 theme へ bind します。",
                "visual layer は `Z = 1000`、content hit layer は既定 2000 です。open 時に `PopupPlacer.Solve` が anchor、layout 済み content size、viewport から side と position を決めます。希望側に入らなければ `Flip`、交差軸では `Shift` し、overlay layer 全体を viewport で clip します。",
                "最初の content layout 上限は viewport から `Margin` を両側分引き、さらに正値の `MaxWidth` / `MaxHeight` で cap します。side ごとの残り空間に合わせた再 layout や自動 scroll は行わないため、長い content は root clip で切れる前に content 自身で wrap、最大寸法、scroll を構成します。"),
            Constraints = new(
                "`anchor` は world-space `Rect` を返し、open の瞬間に有効である必要があります。表示中に anchor が移動しても自動再配置されないため、scroll/resize/transform 後は owner を再実体化するか閉じて開き直します。同じ content Widget インスタンスを複数 overlay へ同時共有しません。",
                "Popover の owner scope が entry を登録し、`open` 中だけ content runtime を作ります。owner の再実体化・破棄で自動解除されます。modal panel 内に置いた nested Popover は modal runtime の子 scope となり、親を閉じると一緒に破棄されます。parent content 外の sibling Popover はこの所有関係を持ちません。",
                "retained `UiHost`、world coordinates、overlay input routing が前提です。framework-level popover semantics を platform host へ公開する契約はありません。"),
            Api = new(
                "Popover",
                "`open: Signal<bool>`、`content: Widget`、`anchor: Func<Rect>`、`placement: AnchoredPlacement` が固有パラメーターです。placement の既定値は Below/Start/Gap 6/Margin 4 です。",
                "固有イベントはありません。trigger は呼び出し側が `open` を true/false にし、outside/Escape dismissal は UiHost が同じ Signal へ false を書き戻します。anchor callback は open 時の placement 計算で同期呼び出しされます。"),
        },
        Page(
            "Toast",
            "Overlay",
            "Toast",
            "任意 content を viewport 右下へ non-modal 表示する controlled portal です。通知 message、Intent、duration、queue、close action は組み込まず、表示期間と内容を呼び出し側へ委ねます。",
            [
                "保存完了などの処理結果を、作業と focus を中断せず右下へ一時表示する場合。",
                "timer、再表示、閉じる action、通知履歴を外部 notification owner が一貫して管理する場合。",
            ],
            [
                "必ず確認してもらう警告や入力を表示する場合。`Dialog` を使います。",
                "常に参照すべき状態や進捗を表示する場合。`StatusBar` など通常レイアウトを使います。",
                "自動消去、stack/queue、live-region 読み上げを Toast 自身へ期待する場合。これらは提供しません。",
            ],
            "var open = new Signal<bool>(true);\nToast(open, Card(Text(\"保存しました\")))",
            "開閉と表示期間は呼び出し側の `Signal<bool>` が所有します。Toast 自身は通知 queue、timer、履歴を持ちません。content の action や timer callback が同じ `open` を更新します。",
            "`open` と `content` の二つが固有パラメーターです。メッセージ、Intent、表示時間、操作の専用パラメーターはありません。",
            "内容側に操作 Widget を含めることはできますが、Toast 自身は外側クリックや `Escape` で閉じません。閉じる場合は呼び出し側または内容内の操作で `open` を更新します。背面 input は遮断しません。",
            "Toast 自身は surface や Intent 色を描きません。content を `Card`、`Alert`、`Border` などで構成し、通知の意味に合う theme token を選びます。",
            "本体は 0 サイズの portal で、表示には `UiHost` の overlay 層が必要です。自動消去、読み上げ live region、通知の重なり管理は提供しません。",
            "Toast",
            alternatives:
            [
                new("StatusBar", "状態を常設表示し、framework-level Status semantics を持つ領域を構成します。"),
                new("Dialog", "利用者の確認または入力が完了するまで背面操作を止めます。"),
            ]
        ) with
        {
            Anatomy = "通常レイアウトでは 0 × 0 の portal 本体です。開いている間だけ canvas root 直下の overlay layer と、右下へ配置される一つの `content` Widget を実体化します。message、icon、close button、progress bar、stack container は含みません。",
            Variants = "`OverlayPlacement.CornerBottomRight`、non-modal、`DismissOnOutside = false`、`DismissOnEscape = false` 固定です。専用の Intent、duration、position、animation、action variant はなく、見た目と操作は `content` 側で構成します。",
            PointerInteraction = "Toast 自身は outside click を監視せず、背面 pointer input を遮断しません。content に Button などを含めた場合だけその hit target が操作を受けます。複数 Toast が重なると同じ corner の後から登録された hit が前面になり、stack の間隔や pointer priority は owner が構成します。",
            FocusActivationDismissal = "開いても focus を移動・trap・復元しません。content に focus target を含めると開いている間だけ global Tab 順へ追加されますが、通知へ自動移動はしません。focusable content がなくても表示できます。outside/Escape dismissal は無効で、閉じるには caller が `open` を更新します。Toast が Escape を無視しても、登録順で下にある dismissible overlay まで Escape 探索が続く場合があります。",
            Accessibility = new(
                "`content` 内に通知内容を文字で示し、操作を含める場合は結果と期限が分かる label を付けます。",
                "`Toast` は `ISemanticProvider` を実装せず、status/alert/live-region role を公開しません。content と発生元の semantic relationship も自動構築しません。",
                "表示状態は `open` Signal が保持しますが、支援技術へ自動通知されません。重要状態は常設領域や別の accessible 経路にも反映します。",
                "content の背景、文字、icon、任意 action と背面 UI のコントラストを light/dark theme で確認します。",
                "Toast 自身は表示時間や open/close animation を管理しません。timer や progress motion を追加する場合も、結果を motion だけに依存させません。",
                "自動読み上げ、通知優先度、pause-on-hover、focus transfer はありません。緊急・失敗通知の唯一の経路には使いません。"),
            ThemeLayout = new(
                "Toast 自身は theme 色を選びません。`content` root が Surface、Intent、角丸、影、padding を所有します。",
                "visual layer は `Z = 1000`、content hit layer は既定 2000 です。既定 `Margin = 16` で右端・下端から 16 px 内側へ置きます。複数 Toast は同じ corner と Z に独立配置され、stack されず重なります。",
                "content の layout 上限は viewport の幅・高さから各 32 px を引いた値です。長文の wrap、最大幅、close action の確保は content 側で行い、複数通知は外部 owner が一つの stack Widget または queue にまとめます。"),
            Constraints = new(
                "duration、queue、deduplication、stacking、swipe、outside/Escape dismissal はありません。content 内に長い操作フォームを置かず、通知は短く保ちます。",
                "Toast の owner scope が overlay entry を登録し、`open` 中だけ content runtime を所有します。閉鎖または owner 破棄で content の hit、focus、effect、nested overlay を破棄します。top modal 外で所有された Toast は視覚表示されても modal input gating により操作できないため、modal 中の action Toast は modal subtree へ置くか閉鎖後に表示します。",
                "retained `UiHost` の overlay layer が必要です。native notification、OS toast、browser live region へ転送する API ではありません。"),
            Api = new(
                "Toast",
                "`open` と `content` の二つが固有パラメーターです。メッセージ、Intent、表示時間、操作の専用パラメーターはありません。共通 Widget size は portal 本体ではなく content 側へ指定します。",
                "固有イベントはなく、表示期間と閉鎖は呼び出し側が `open` Signal で制御します。`DismissOnOutside` と `DismissOnEscape` は内部で false のため、UiHost は Toast 自身を light-dismiss しません。"),
        },
        Page(
            "Tooltip",
            "Overlay",
            "Tooltip",
            "一つの `child` を通常レイアウトで包み、wrapper が pointer hover を受けている間だけ短い文字 bubble を上側へ portal 表示します。keyboard focus、delay、公開 open state はありません。",
            [
                "pointer 利用者向けの省略可能な短い補足を一行で追加し、同じ情報が見える label や文脈からも理解できる場合。",
                "child が独自の深い hit target を持たず、現行 UiHost で Tooltip wrapper が hover target になることを確認できる場合。",
            ],
            [
                "操作に必須の説明、validation error、長文、入力可能 content を表示する場合。",
                "keyboard focus、touch、screen reader でも同じ説明を必ず提示する場合。Tooltip はそれらで開きません。",
                "`Button` など独自 hit target を持つ interactive child へ無条件に適用する場合。現行の最深 hit 優先 routing では wrapper が hover を受けないことがあります。",
            ],
            "Tooltip(target, \"詳しい説明\")",
            "`child` と `text` は呼び出し側、hover 中の内部 `_open` Signal は Tooltip が所有します。公開 `Opened` や controlled `open` parameter はなく、hover enter/leave で即時に true/false を切り替えます。",
            "固有パラメーターは `child` と `text` だけです。placement、delay、duration、max width、rich content、controlled open state のパラメーターはありません。",
            "wrapper の pointer hover で表示し、hover を失うと閉じます。outside click と `Escape` は Tooltip を閉じず、bubble 自体に hit target や操作はありません。",
            "bubble は theme `Text` 背景、`OnAccent` 文字、角丸 5 px、padding 8 × 5 px、font size 13 です。",
            "短い一行 text 専用です。keyboard/touch 起動、delay、semantic description、interactive content はなく、interactive child の hover routing に host 制約があります。",
            "Tooltip",
            alternatives:
            [
                new("Popover", "外部 trigger、controlled open、任意 Widget content、placement を必要とする補助面に使います。"),
                new("Text", "必須説明を常に見える通常レイアウトへ置きます。"),
            ]
        ) with
        {
            Anatomy = "通常レイアウトでは `child` と同じ size を持つ wrapper です。child をそのまま実体化し、wrapper 全体へ hover hit を登録し、open 中だけ一つの text bubble を overlay layer に実体化します。arrow、surface slot、close action はありません。",
            Variants = "希望 placement は `OverlayPlacement.Above` 固定で、legacy anchored mapping により `Align = Start`、`Gap = 6`、`Margin = 0`、`Flip = true`、`Shift = true` として解かれます。上に入らなければ下へ flip しますが、placement や delay を呼び出し側から変更できません。",
            PointerInteraction = "wrapper 自身が登録した hover hit に pointer が入ると表示し、離れると閉じます。bubble は hit target を持たず pointer を受けません。UiHost は最も深い hit target だけへ hover を配送するため、child が独自 hit を登録すると wrapper の hover handler は選ばれないことがあります。",
            FocusActivationDismissal = "Tooltip は focus target を追加せず、child が keyboard focus を得ても表示しません。bubble へ focus を移動・trap・復元せず、focusable content も受け取りません。hover leave だけが Tooltip 自身の close 経路で、`Escape` と outside click は無効です。modal panel 内に置いた Tooltip はその scope の input gating と lifecycle に従います。",
            Accessibility = new(
                "`text` は短い補足として書き、対象の目的や必須手順は child の見える label または周囲の本文にも置きます。",
                "`Tooltip` は `ISemanticProvider` を実装せず、tooltip role を公開しません。`text` は child の `SemanticNode.Description` へ関連付けられません。",
                "open/hover state は支援技術へ公開されません。keyboard focus や touch では同じ state になりません。",
                "`Text` 背景と `OnAccent` 文字の組み合わせ、bubble と背面の識別を利用 theme で確認します。",
                "hover enter/leave で即時表示・破棄し、delay や fade animation はありません。",
                "現行 UiHost は一点につき最も深い hit target の hover handler だけを呼ぶため、独自 hit target を持つ child では wrapper の hover が発火しない場合があります。screen reader、keyboard、touch 向けの代替説明を必ず用意します。"),
            ThemeLayout = new(
                "bubble style は固定で theme `Text` / `OnAccent` を参照し、radius 5 px、padding horizontal 8 px / vertical 5 px、font size 13 です。child の theme/style は変更しません。",
                "bubble は visual layer `Z = 1000`、hit layer は既定 2000 ですが、自身の hit target はありません。anchor は Tooltip realization 時に得た child world rect を open 時に使い、上/下 flip と横 shift を行います。表示中の child 移動へ連続追従しません。",
                "text は一つの `Kit.Text` として自然サイズを使い、専用 max width、wrap、ellipsis はありません。viewport constraint と root clip は適用されますが、長文を収める control ではないため短い一行にします。"),
            Constraints = new(
                "公開 delay、placement、controlled open、rich content、interactive bubble はありません。interactive child で hover が必要なら対象 host で動作確認し、未対応なら visible text または明示 trigger の `Popover` へ切り替えます。",
                "Tooltip の owner scope が overlay entry と内部 hover state の購読を所有します。hover 中だけ bubble runtime を作り、child/owner の再実体化・破棄で解除します。modal 外で所有された Tooltip は top modal 中に hover input を受けません。",
                "pointer hover と retained overlay を持つ `UiHost` が前提です。touch-only host、keyboard-only 操作、platform tooltip/accessibility bridge の契約はありません。"),
            Api = new(
                "Tooltip",
                "固有パラメーターは `child: Widget` と `text: string` です。placement、delay、duration、open Signal、任意 content は公開しません。",
                "固有イベントはなく、wrapper の hover callback が内部 open Signal を更新します。outside/Escape dismissal は false で、呼び出し側から open/close を制御する契約はありません。"),
        },
    ];
}
