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
            "現在の作業を一時的に中断して、確認や短い入力を求める前面表示です。",
            ["現在の作業を一時停止し、必須の確認や短い入力をモーダルに求める場合。"],
            ["常設の作業面、長時間の編集、作業を遮らない通知に使う場合。"],
            "var open = new Signal<bool>(false);\nDialog(open, Card(content))",
            "開閉は呼び出し側の `Signal<bool>` が所有し、パネル内の入力値も呼び出し側モデルで保持します。",
            "`open` と `panel` の二つが固有パラメーターです。タイトル、本文、操作ボタンは `panel` の Widget 構成として渡します。",
            "パネル内の Widget を操作します。モーダル overlay のため外側クリックと `Escape` は既定で `open` を `false` にし、背面入力を遮断します。",
            "オーバーレイ層に表示されます。幅を抑え、主要操作と取消操作の視覚階層をテーマで示します。",
            "本体は 0 サイズの portal で、表示には `UiHost` の overlay 層が必要です。長大な作業面や常設ナビゲーションは `panel` に載せません。",
            "Dialog",
            alternatives: [new("Popover", "作業を遮らない短い補助内容をアンカー近くへ表示します。")],
            keyboard: [new("`Escape`", "UiHost の既定 dismissal で `open` を `false` にします。")]
        ) with
        {
            Anatomy = "0 サイズの portal 本体と、中央へ配置される一つの `panel` Widget、背景 scrim から構成します。",
            Variants = "固有のタイトル、ボタン、サイズ variant はなく、`panel` の Widget 構成で表現します。",
            FocusActivationDismissal = "開くと modal scope 内の最初の focus target へ移り、背面 focus を遮断します。外側クリックまたは `Escape` で閉じ、閉鎖後は元の focus target を復元します。",
            Accessibility = new("`panel` 内に見えるタイトルと各入力のラベルを用意します。", "modal な入力スコープは提供しますが、専用の dialog semantic role は追加しません。", "`open` 状態と panel 内の検証状態を呼び出し側が管理します。", "scrim、panel、文字、主要操作のコントラストを確認します。", "Dialog 自身は開閉アニメーションを定義しません。", "semantic dialog 名や説明の関連付けは自動ではありません。"),
            Api = new("Dialog", "`open` と `panel` の二つが固有パラメーターです。タイトル、本文、操作ボタンは `panel` の Widget 構成として渡します。", "固有イベントはなく、dismissal は `open` Signal を `false` に書き戻します。"),
        },
        Page(
            "Drawer",
            "Overlay",
            "Drawer",
            "画面端から補助内容を重ねて表示する一時パネルです。詳細、フィルター、狭い画面のナビゲーションに使います。",
            ["右端から一時的な補助パネルをモーダル表示する場合。"],
            ["常設ナビゲーションや、右端以外の辺を選べる汎用パネルが必要な場合。"],
            "var open = new Signal<bool>(false);\nDrawer(open, Card(details))",
            "開閉は呼び出し側の `Signal<bool>` が所有し、パネル内の業務状態も外部モデルで保持します。",
            "`open` と `panel` の二つが固有パラメーターです。表示辺や幅を切り替えるパラメーターはなく、配置は右端です。",
            "パネル内の Widget を操作します。モーダル overlay のため外側クリックと `Escape` は既定で `open` を `false` にし、背面入力を遮断します。",
            "オーバーレイ層とテーマ表面色を使います。小さい画面でも主内容を完全に隠し続けない幅にします。",
            "本体は 0 サイズの portal で、表示には `UiHost` の overlay 層が必要です。左端・下端などを選ぶ汎用 drawer ではありません。",
            "Drawer",
            alternatives: [new("DockHost", "常設パネルを分割・ドッキングします。")],
            keyboard: [new("`Escape`", "UiHost の既定 dismissal で `open` を `false` にします。")]
        ) with
        {
            Anatomy = "0 サイズの portal 本体と、右端へ配置される一つの `panel` Widget、背景 scrim から構成します。",
            Variants = "配置は `RightEdge` 固定です。内容と幅は `panel` 側の Widget 構成で指定します。",
            FocusActivationDismissal = "開くと modal scope 内の最初の focus target へ移り、背面 focus を遮断します。外側クリックまたは `Escape` で閉じ、閉鎖後は元の focus target を復元します。",
            Accessibility = new("`panel` 内に見える見出しと各入力のラベルを用意します。", "modal な入力スコープは提供しますが、専用の drawer semantic role は追加しません。", "`open` と panel 内の状態を呼び出し側が管理します。", "scrim、panel、文字、境界のコントラストを確認します。", "Drawer 自身は開閉アニメーションを定義しません。", "semantic 名や領域説明の関連付けは自動ではありません。"),
            Api = new("Drawer", "`open` と `panel` の二つが固有パラメーターです。表示辺や幅を切り替えるパラメーターはなく、配置は右端です。", "固有イベントはなく、dismissal は `open` Signal を `false` に書き戻します。"),
        },
        Page(
            "Dropdown",
            "Overlay",
            "Dropdown",
            "起点の近くへ一時的な選択肢や操作を表示します。",
            ["ラベル付きトリガーから短い操作項目を選んで即座に実行する場合。"],
            ["任意 Widget の補助内容、値選択、検索可能な多数のコマンドを表示する場合。"],
            "Dropdown(\"操作\", [(\"保存\", Save), (\"閉じる\", Close)])",
            "開閉はコントロール内部の `Opened` Signal が保持します。各操作の業務結果は項目の `Action` が外部モデルへ反映します。",
            "`label` と `(string label, Action onClick)[] items` が固有パラメーターです。任意の anchor Widget や menu Widget、配置パラメーターは受け取りません。",
            "トリガーボタンのポインター操作で開閉し、`MenuRow` のクリックで Action を実行して閉じます。外側クリックと `Escape` は UiHost の既定 dismissal で閉じます。",
            "オーバーレイ層に表示し、起点との視覚的関係を保ちます。",
            "項目はラベルと同期 Action の組に限られます。任意 Widget の内容、多数コマンドの検索、項目仮想化は提供しません。",
            "Dropdown",
            alternatives: [new("Select", "一つの値を候補一覧から選びます。"), new("Popover", "任意 Widget の補助内容を表示します。")],
            keyboard: [new("`Escape`", "開いているメニューを UiHost の既定 dismissal で閉じます。")]
        ) with
        {
            Anatomy = "tonal なトリガー Button と、下側 overlay に並ぶ `MenuRow` の列から構成します。",
            Variants = "項目は `(label, Action)` の組だけです。任意 Widget の menu variant や placement variant はありません。",
            FocusActivationDismissal = "トリガーと MenuRow はポインター hit target です。専用のメニュー focus 移動はなく、外側クリックまたは `Escape` で閉じます。",
            Accessibility = new("`label` と各項目ラベルを具体的な操作名にします。", "専用の menu/menuitem semantic role は公開しません。", "開閉は公開 `Opened` Signal で観測できます。", "トリガー、hover 行、文字のコントラストをテーマで確認します。", "開閉アニメーションは定義しません。", "キーボードだけでトリガーを起動したり項目間を移動したりする契約はありません。"),
            Api = new("Dropdown", "`label` と `(string label, Action onClick)[] items` が固有パラメーターです。任意の anchor Widget や menu Widget、配置パラメーターは受け取りません。", "固有 UiEvent はなく、項目 `Action` を同期実行した直後に内部 `Opened` を `false` にします。"),
        },
        Page(
            "MenuRow",
            "Overlay",
            "MenuRow",
            "メニュー内の一操作を、ラベル、補助表示、状態とともに表します。",
            ["メニュー面の中で一つの短い操作項目を表示する場合。"],
            ["単独の主要操作や、複雑な入力を含む行として使う場合。"],
            "MenuRow(\"保存\", _ => Save())",
            "コマンドの有効状態や実行結果は呼び出し側が所有します。",
            "ラベル、起動処理、アイコン、ショートカット表示、disabled が中心です。",
            "ポインターで起動します。実際のキー割当は Keymap など別の仕組みで管理し、表示と一致させます。",
            "メニュー面の幅、行高、選択色をテーマに合わせます。",
            "メニューの開閉、コマンド履歴、権限判定は管理しません。",
            "MenuRow",
            alternatives: [new("Button", "独立した操作を明示的に起動します。")]
        ),
        Page(
            "Popover",
            "Overlay",
            "Popover",
            "アンカー矩形の近くへ補助内容を重ねる、非モーダルのポータルコントロールです。",
            ["起点との関係を保った短い補助内容や小さなフォームを非モーダル表示する場合。"],
            ["必須確認、長時間作業、単一行だけの補足説明に使う場合。"],
            "Popover(open: open, content: Card(details), anchor: () => triggerRect,\n    placement: new AnchoredPlacement { Side = PopupSide.Below, Align = PopupAlign.Start, Gap = 6, Margin = 4 })",
            "open の Signal、content、anchor の座標計算は呼び出し側が所有します。Popover は UiHost へ OverlayEntry を登録し、外側クリックまたは Escape による閉鎖を open へ反映します。",
            "open、content、anchor、placement が中心です。AnchoredPlacement の Side、Align、Gap、Margin で表示位置を指定し、anchor は表示中も有効な Rect を返します。",
            "Popover 自体は 0 サイズで、起点の操作とフォーカスは呼び出し側が用意します。既定では外側クリックと Escape で閉じます。非モーダルでフォーカストラップは行わないため、内容内のキーボード順と起点へ戻る経路を設計します。",
            "content 側で Surface、BorderColor、Radius、余白、最大幅を指定します。配置は anchor と利用可能領域から決まり、Popover 本体へ width や height を指定しても表示面の寸法にはなりません。",
            "UiHost のオーバーレイ層が必要で、単独の Canvas では表示できません。anchor が古い座標や破棄済み対象を参照しないよう、所有 Widget のライフサイクルに合わせます。確認を強制する処理や長い作業には Dialog または Drawer を使います。",
            "Popover",
            alternatives: [new("Tooltip", "単一行の短い補足だけを表示します。")],
            keyboard: [new("`Escape`", "開いている popover を UiHost の既定 dismissal で閉じます。")]
        ),
        Page(
            "Toast",
            "Overlay",
            "Toast",
            "処理結果を作業を妨げず短時間通知するオーバーレイです。",
            ["処理結果などを作業を遮らず右下へ表示し、表示期間を呼び出し側で管理する場合。"],
            ["必須確認、永続通知、自動消去や通知キューをコントロール自身へ期待する場合。"],
            "var open = new Signal<bool>(true);\nToast(open, Card(Text(\"保存しました\")))",
            "開閉と表示期間は呼び出し側の `Signal<bool>` が所有します。Toast 自身は通知キュー、タイマー、履歴を持ちません。",
            "`open` と `content` の二つが固有パラメーターです。メッセージ、Intent、表示時間、操作の専用パラメーターはありません。",
            "内容側に操作 Widget を含めることはできますが、Toast 自身は外側クリックや `Escape` で閉じません。閉じる場合は呼び出し側または内容内の操作で `open` を更新します。",
            "オーバーレイ層でテーマの Intent 色を使い、他の操作を隠さない位置へ表示します。",
            "本体は 0 サイズの portal で、表示には `UiHost` の overlay 層が必要です。自動消去、読み上げ live region、通知の重なり管理は提供しません。",
            "Toast",
            alternatives: [new("StatusBar", "状態を常設表示します。")]
        ) with
        {
            Anatomy = "0 サイズの portal 本体と、右下へ配置される一つの `content` Widget から構成します。",
            Variants = "専用の Intent、メッセージ、表示時間 variant はなく、見た目と操作は `content` 側で構成します。",
            FocusActivationDismissal = "非モーダルで focus を移動・拘束しません。外側クリックと `Escape` dismissal は無効で、閉じるには呼び出し側が `open` を更新します。",
            Accessibility = new("`content` 内に通知内容を文字で示します。", "専用の status/alert/live-region semantic role は公開しません。", "表示状態は `open` Signal で管理します。", "背景、文字、任意操作のコントラストを `content` 側で確認します。", "Toast 自身は表示時間や開閉アニメーションを管理しません。", "自動読み上げを必要とする重要通知の唯一の経路には使いません。"),
            Api = new("Toast", "`open` と `content` の二つが固有パラメーターです。メッセージ、Intent、表示時間、操作の専用パラメーターはありません。", "固有イベントはなく、表示期間と閉鎖は呼び出し側が `open` Signal で制御します。"),
        },
        Page(
            "Tooltip",
            "Overlay",
            "Tooltip",
            "操作対象の短い補足説明を一時表示します。",
            ["操作対象へ短い補足説明をポインターホバー時だけ追加する場合。"],
            ["操作に必須の情報、長い説明、入力可能な内容を表示する場合。"],
            "Tooltip(target, \"詳しい説明\")",
            "表示対象と説明は呼び出し側、開閉タイミングは Tooltip が管理します。",
            "target、content、配置、遅延が中心です。",
            "ポインターの滞在などで表示されます。Tooltip だけに必須情報を置かず、キーボードや読み上げで到達できない可能性を考慮します。",
            "短い文で幅を抑え、対象を隠しにくい位置とテーマ表面色を使います。",
            "操作、長文、エラー、モバイルの主要説明には不向きです。対象破棄時に表示も閉じます。",
            "Tooltip",
            alternatives: [new("Popover", "複数行や操作可能な補助内容を表示します。")]
        ),
    ];
}
