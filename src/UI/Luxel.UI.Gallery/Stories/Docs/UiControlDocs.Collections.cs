using Luxel.Gallery;
using Luxel.Gallery.UI;

namespace Luxel.UI.Gallery;

internal static partial class UiControlDocs
{
    private static readonly ControlDocsPage[] CollectionsPages =
    [
        Page(
            "Accordion",
            "Collections",
            "Accordion",
            "一つの補足領域を見出しから段階的に開示する、単一セクションの折りたたみ表示です。",
            [
                "詳細設定、補足説明、診断情報など、既定では隠しても主操作を妨げない一つの領域を開示する場合。",
                "開閉状態を外部の `Signal<bool>` と同期し、別の操作からも展開・折りたたみを制御する場合。",
                "展開内容が後続要素と重ならない末尾領域、オーバーレイ、または専用の重なり管理下に配置できる場合。",
            ],
            [
                "複数の見出しを一つの項目コレクションとして管理する場合。`Accordion` は一つの title/content だけを受け取るため、複数インスタンスまたは `TreeView` を使います。",
                "展開時に親レイアウトへ高さを返して後続要素を押し下げる必要がある場合。通常の `Stack` と条件付き構築を使います。",
                "キーボードだけの開閉、expanded semantic state、フォーカス可能な disclosure button が必須の場合。現実装にはその契約がありません。",
            ],
            "var expanded = new Signal<bool>(false);\nAccordion(\"詳細\", details, expanded: expanded, width: 320)",
            "`content` のデータは呼び出し側が所有します。`expanded` を渡すとその `Signal<bool>` が正本になり、ヘッダークリックが値を書き換えます。未指定時は初期値 false の内部 Signal が同じ Widget インスタンスの生存中だけ保持されます。",
            "`title`、`content`、任意の `expanded`、`fontSize`、状態別指定可能な `background` と `foreground` が中心です。コレクション、複数展開規則、開閉イベントは持ちません。",
            "40 px のヘッダー全体をクリックすると開閉を反転します。hover 時は既定背景が `Surface` から `SurfaceAlt` へ変わります。内容領域自体の入力は、展開後に子 Widget が処理します。",
            "ヘッダーは `Surface` / `SurfaceAlt`、文字は `Text` に追従し、個別色で上書きできます。親から有限幅を与えると、その幅で content を無限高まで測定します。",
            "レイアウト上の `Size.Height` は常にヘッダーの 40 px だけです。内容はヘッダー下 4 px からクリップ付きでオーバーフローし、0.25 秒でスライドします。親の再配置、複数項目、遅延生成は行いません。",
            "Accordion",
            alternatives:
            [
                new("Stack", "内容を常時表示するか、条件付きで構築して親レイアウトへ実寸を返します。"),
                new("TreeView", "複数の階層項目を展開・選択し、項目キーで状態を管理します。"),
            ]
        ) with
        {
            Anatomy = "一つの 40 px ヘッダー、ヘッダー直下 4 px に置く固定クリップ領域、その中を上下移動する一つの `content` Widget から構成します。展開アイコンや複数パネルのコンテナーはありません。",
            Variants = "外部 `expanded` Signal を使う制御形と、未指定時の内部状態形があります。`fontSize`、`background`、`foreground` は変更できますが、orientation、複数展開、disabled、遅延 content のバリエーションはありません。",
            FocusActivationDismissal = "ヘッダーは pointer hit target ですが focus target を登録しません。専用の起動キー、フォーカス移動、dismissal はなく、閉じるには再度ヘッダーをクリックするか外部 `expanded` を false にします。",
            Accessibility = new(
                "見える `title` を、その直後に現れる内容を具体的に表す短い名前にします。",
                "`Accordion` 自身は button、group、expanded などの semantic role を公開しません。",
                "開閉状態は支援技術へ通知されません。重要な情報や操作は折りたたみ内部だけに置かず、別経路も用意します。",
                "`background` と `foreground` を上書きする場合も、通常・hover の両方でタイトルのコントラストを確認します。",
                "content は 0.25 秒でスライドします。動きを無効化するパラメーターはありません。",
                "キーボードとスクリーンリーダーだけで開閉する契約がないため、アクセシブルな disclosure が必須の画面には使用しません。"),
            ThemeLayout = new(
                "既定のヘッダー色は `UiTheme.Surface` / `SurfaceAlt`、文字色は `UiTheme.Text` です。",
                "有限幅では親の最大幅を使い、無限幅では 300 px を採用します。content は同じ幅、無限高で測定されます。",
                "公称高さは 40 px のままで、展開内容の高さを親へ返しません。周囲の要素との重なりを配置側で防ぎます。"),
            Constraints = new(
                "一つの title/content に限定され、親の再レイアウト、複数パネルの排他展開、仮想化は提供しません。",
                "内部展開状態は同じ Widget の再実体化では保持されますが、インスタンスを作り直すと false に戻ります。永続化や共有には外部 Signal を使います。",
                "通常の pointer/render host で動作し、overlay や drag のサービスは不要です。ただし focus/keyboard/semantic 対応はプラットフォーム差ではなく未実装です。"),
            Api = new(
                "Accordion",
                "`title`、`content`、`expanded`、`fontSize`、`background`、`foreground` と Widget 共通のサイズ・配置指定を使います。",
                "固有の `UiEvent` はありません。開閉通知が必要なら、渡した `expanded` Signal の変更を呼び出し側で観測します。"),
        },
        Page(
            "DataGrid",
            "Collections",
            "DataGrid",
            "固定列の文字列レコードを縦方向に仮想化し、行選択と同一グリッド内の並べ替え要求を扱います。",
            [
                "同じ列定義を持つ多数のレコードを、列見出しに沿って比較しながら行単位で選択する場合。",
                "固定行高の読み取り専用データを数十〜大量件表示し、可視行だけを描画してスクロール負荷を抑える場合。",
                "安定した行 Key を使い、ポインターとキーボードで単一・追加・範囲選択を同じモデル上で扱う場合。",
            ],
            [
                "セル編集、列ソート、フィルター、列リサイズ、可変行高が必要な場合。これらは `DataGrid` の責務外です。",
                "カード、画像、任意の子 Widget を項目ごとに表示する場合。文字列セルだけなので `GridView` または専用ビューを使います。",
                "列幅の合計が表示幅を超え、横スクロールが必要な場合。現実装は横スクロールせず右側をクリップします。",
            ],
            "var rows = new Signal<IReadOnlyList<DataGridRow>>([new(\"a\", [\"設定\", \"有効\"])]);\nDataGrid(items: rows, columns: [new DataGridColumn(\"name\", \"名前\", 180)],\n    height: 320, rowHeight: 26, onSelect: (_, row) => Select(row.Key))",
            "`items` Signal、行データ、並び順は呼び出し側が所有します。選択 Key、anchor、focused Key、縦スクロール位置は DataGrid 内部にあり、外部 `selected` 入力はありません。items が別の一覧参照へ変わると存在しない選択と focus を除去し、スクロールを新しい長さへクランプします。",
            "`items`、`columns`、`height`、`rowHeight`、`OnSelect`、`OnReorder` が中心です。`AllowReorder` は生成ファクトリ引数ではなく、構築後のプロパティで有効化します。行 Key と列 Key は安定した一意値にします。",
            "行クリックは通常選択、Ctrl は追加・解除、Shift は anchor からの範囲選択で、disabled 行の選択は無視します。`AllowReorder` 時は 4 px を超えるドラッグで同じ DataGrid の行だけを受け入れますが、drag source では disabled を除外しないため、移動不可行が必要なら受信側でも拒否します。ホイールと縦スクロールバーで本文だけをスクロールし、見出しは固定されます。",
            "見出しと通常文字は `TextMuted` / `Text`、選択面は `SurfaceAlt` に追従します。列幅は各 `DataGridColumn.Width`、表示高は `height`、行高は `rowHeight` で決まり、親から有限幅を与えます。",
            "セルは文字列の読み取り専用表示です。描画ノードは可視行数 + 1 に仮想化されますが、semantic snapshot は全行・全セルを列挙します。items の動的更新では同じ一覧を in-place 変更せず、新しい一覧参照を Signal へ設定します。",
            "DataGrid",
            alternatives:
            [
                new("GridView", "列比較ではなく、同じ大きさのラベル項目を複数列で走査します。"),
                new("ListView", "列見出しが不要な一列の文字列一覧を、より単純な API で仮想化します。"),
                new("TreeView", "親子関係の展開・折りたたみが主目的のデータを表示します。"),
            ],
            keyboard:
            [
                new("`Up` / `Down`", "前後の有効行へ移動し、その行を選択します。Shift 併用で anchor から範囲選択します。"),
                new("`Home` / `End`", "先頭または末尾 index へ移動します。対象の端行が disabled の場合は、現在の実装では移動せず現在行に留まる場合があります。"),
                new("`PageUp` / `PageDown`", "本文ビューポートのおよそ 1 ページ分を移動します。Shift 併用で範囲を延長します。"),
                new("`Space`", "focused 行を通常選択し、Ctrl 併用時は選択を追加・解除します。"),
            ]
        ) with
        {
            Anatomy = "固定された列見出し行、縦スクロールする固定高の本文ビューポート、可視行プール、行選択面、右端の縦スクロールバーから構成します。各 `DataGridRow.Cells` は列順に文字列として描画されます。",
            Variants = "通常の選択専用形と、`AllowReorder = true` の同一グリッド内 drag/reorder 形があります。`DataGridRow.Disabled` は click 選択と keyboard 移動から除外されますが、reorder の drag source からは除外されません。選択は単一・Ctrl toggle・Shift range を常時サポートします。",
            FocusActivationDismissal = "本文のクリックまたは drag hit が一つの DataGrid focus target を取得します。focus 中は行移動キーを処理しますが、専用の focus ring は描画しません。選択を閉じる・解除する dismissal キーはありません。",
            Accessibility = new(
                "周囲の見出しで表の目的を示し、列 Header とセル文字列を簡潔にします。",
                "root は `SemanticRole.Grid`、各行は `Row`、各セルは `GridCell` です。列見出し自体は semantic child として公開されません。",
                "行の selected / disabled 状態は Row semantic に反映されます。focused Key と並べ替え位置は semantic state へ公開されません。",
                "`Text`、`TextMuted`、`SurfaceAlt` の組み合わせを利用テーマで確認し、無効行を透明度だけで識別させない補足を検討します。",
                "選択とスクロールに固有アニメーションはなく、ドラッグ時だけ ghost と挿入位置を表示します。",
                "描画は仮想化されますが semantics は全件生成します。大規模データでは支援技術側のコストと、列見出し semantic がない制約を評価します。"),
            ThemeLayout = new(
                "文字、区切り、選択面は `UiTheme.Text`、`TextMuted`、`SurfaceAlt` を使い、個別色パラメーターはありません。",
                "有限幅では親の最大幅を使い、無限幅では列幅合計または 160 px 以上を採用します。列幅合計が viewport を超えても横スクロールしません。",
                "列幅は最小 24 px、本文行高は最小 18 px、見出し高は最小 22 px、表示高は最小 1 px です。"),
            Constraints = new(
                "文字列セル、固定行高、縦スクロール、行単位選択に限定されます。編集、sort/filter、column resize、横スクロールは呼び出し側または別コントロールで実装します。",
                "同じ Widget では選択、focus、scroll を保持します。items の一覧参照を交換すると、消えた Key を選択から除去します。Key の再利用や重複は選択対応を曖昧にするため避けます。",
                "通常操作は pointer/keyboard/scroll 対応 host で動作します。drag/reorder の開始には `UiHost` が必要で、他の DataGrid や外部ドロップ先は受け入れません。"),
            Api = new(
                "DataGrid",
                "`Signal<IReadOnlyList<DataGridRow>> items`、`IReadOnlyList<DataGridColumn> columns`、`height`、`rowHeight`、`AllowReorder`、読み取り用の `SelectedKeys` / `FocusedKey` / `ScrollOffset` が中心です。",
                "`OnSelect(DataGrid, DataGridRow)` は pointer と keyboard の選択経路から発火します。`OnReorder(DataGrid, from, to)` は現在の一覧に対する移動要求だけを返すため、呼び出し側が source を除去し、挿入 index を補正して新しい一覧を items Signal へ設定します。"),
        },
        Page(
            "DocumentTabs",
            "Collections",
            "DocumentTabs",
            "文書タブの active、dirty、閉じる要求、同一または共有 channel 上の drag/drop を扱う軽量なタブ帯です。",
            [
                "エディターや DockHost で、文書 ID、タイトル、dirty Signal を既存の文書モデルから投影する場合。",
                "閉じる前の保存確認や並べ替え規則をシェル側に残し、タブ帯は要求通知だけを担当させる場合。",
                "同じ `dragChannel` を共有する複数の帯へ文書を移動する、または一つの帯内で順序を変更する場合。",
            ],
            [
                "キーボード移動、tab semantics、disabled、Badge、Tooltip、明示的な overflow 操作が必要な場合。より完全な `TabStrip` を使います。",
                "閉じる・dirty・drag が不要な少数の固定ビューを切り替える場合。content も管理する `Tabs` の方が単純です。",
                "狭い幅で多数のタブを確実に到達可能にする場合。最小幅を超えた分はクリップされ、スクロールや overflow menu はありません。",
            ],
            "DocumentTabs(tabs, active: activeId,\n    onActivate: (_, id) => Activate(id),\n    onClose: (_, id) => RequestClose(id),\n    onDropTab: (_, id, index) => Move(id, index))",
            "文書一覧、active ID、文書の存続、並び順は呼び出し側が所有します。各 `DocTab.Dirty` Signal だけは glyph にライブ反映されます。`OnActivate`、`OnClose`、`OnDropTab` は要求を返すだけなので、受信側がモデルを更新し親を再構築します。",
            "`items`、`active`、`dragChannel`、`showClose`、`stripHeight`、`activeBackground` と `OnActivate`、`OnClose`、`OnDropTab` が中心です。`DocTab` は `Id`、`Title`、任意の `Dirty` Signal を持ちます。",
            "タブ本体の短いクリックで activate、右端 glyph のクリックで close 要求を通知します。4 px を超えるドラッグは `UiHost` の drag へ昇格し、同じ channel の帯へ drop できます。dirty の ● も close glyph と同じ hit target です。",
            "選択面は `SurfaceAlt`、下線は `Primary`、通常・非 active 文字は `TextMuted` に追従します。既定高は 32 px で、active background を消して下線だけの view switcher にできます。",
            "各タブは自然幅を最大 176 px に抑えます。幅不足時は比例縮小しつつ 56 px を下限にし、それでも収まらない部分は帯でクリップします。keyboard、semantic provider、scroll/overflow button はありません。",
            "DocumentTabs",
            alternatives:
            [
                new("TabStrip", "キーボード、Tab semantics、disabled、Badge、Tooltip、overflow 操作を含む文書タブを使います。"),
                new("Tabs", "閉じる・dirty・drag を持たない少数の content を同じ Widget 内で切り替えます。"),
            ]
        ) with
        {
            Anatomy = "下辺の hairline、横一列のタブ面、active 下線、タイトル、任意の dirty/close glyph、drop 挿入インジケーターから構成します。content 領域は持たず、文書内容は外側が配置します。",
            Variants = "`showClose = false` の view switcher、`activeBackground = false` の下線のみ、任意の `stripHeight`、外部 `Dirty` Signal、既定の自己 channel または共有 `dragChannel` があります。disabled、Badge、Tooltip、vertical orientation はありません。",
            FocusActivationDismissal = "タブ、close glyph、drop surface は focus target を登録しません。起動は pointer の短い click だけで、close や drag 後の focus 復元も行いません。dismissal は `OnClose` を受けた所有者が実装します。",
            Accessibility = new(
                "各 `Title` を文書を識別できる名前にし、dirty や close の意味を周囲にも表示します。",
                "`DocumentTabs` は `TabList` / `Tab`、button、close button の semantic role を公開しません。",
                "active、dirty、closable、drop position は支援技術へ公開されません。dirty は見た目の ● だけです。",
                "active 背景を無効にする場合も `Primary` の下線と文字色がテーマ上で判別できるか確認します。",
                "固有の切替アニメーションはありません。drag 時は ghost と挿入線を表示します。",
                "pointer-only のため、キーボードとスクリーンリーダー対応が必要な文書 UI では `TabStrip` または別経路を選びます。"),
            ThemeLayout = new(
                "`SurfaceAlt`、`Primary`、`Text`、`TextMuted`、`BorderColor` を使い、個別の色パラメーターはありません。",
                "自然幅はタイトルと close glyph から計算し、有限幅ではその幅に収まるよう比例縮小します。残りは clip されます。",
                "既定高は 32 px、最大タブ幅は 176 px、overflow 縮小時の最小幅は 56 px、close/dirty glyph 列は 18 px です。"),
            Constraints = new(
                "タブ内容、保存確認、削除、active 更新、並べ替えは管理しません。到達不能な clipped tab を救済する scroll/menu もありません。",
                "items と active の変更は所有者の再構築または bindable 更新で反映し、`DocTab.Dirty` は同じ Signal の値変更へ追従します。Id は一意で安定させますが、重複を実装が検証する契約はありません。",
                "pointer 表示は通常 host で動作します。drag/drop の開始と帯間移動には `UiHost` と同一 `dragChannel` が必要です。"),
            Api = new(
                "DocumentTabs",
                "`IReadOnlyList<DocTab> items`、`active`、`dragChannel`、`showClose`、`stripHeight`、`activeBackground`、座標確認用の `TabCenterOf` / `CloseCenterOf` が中心です。",
                "`OnActivate(DocumentTabs, id)`、`OnClose(DocumentTabs, id)`、`OnDropTab(DocumentTabs, id, index)` はモデル変更前の要求です。同一帯で source を除去してから挿入する場合は、source が index より前なら index を 1 減らして正規化します。"),
        },
        Page(
            "GridView",
            "Collections",
            "GridView",
            "固定サイズのラベル項目を row-major の複数列へ仮想化し、選択と同一グリッド内の並べ替え要求を扱います。",
            [
                "同じ視覚的重要度と固定サイズを持つ多数の項目を、利用幅に応じた複数列で走査する場合。",
                "安定した Key を持つ項目に単一・追加・範囲選択と方向キー移動を提供する場合。",
                "可視セルだけを実体化しつつ、ラベル中心のカードまたはタイル一覧を軽量に表示する場合。",
            ],
            [
                "列ごとに異なる意味を比較する表形式データの場合。`DataGrid` の列見出しと行 semantics を使います。",
                "画像、複数行テキスト、任意の子 Widget、可変サイズのカードが必要な場合。`GridViewItem` は一つの Label だけを描画します。",
                "階層、遅延ロード、親子の展開が必要な場合。平坦な row-major 配置なので `TreeView` を使います。",
            ],
            "var items = new Signal<IReadOnlyList<GridViewItem>>([new(\"a\", \"プロジェクト A\")]);\nGridView(items: items, height: 240, itemWidth: 144, itemHeight: 80,\n    onSelect: (_, item) => Open(item.Key))",
            "`items` Signal と項目データ、並び順は呼び出し側が所有します。選択 Key、anchor、focused Key、計算済み列数、縦スクロール位置は GridView が保持します。items が別の一覧参照へ変わると、消えた Key を選択と focus から除去します。",
            "`items`、`height`、`itemWidth`、`itemHeight`、`OnSelect`、`OnReorder` が中心です。`AllowReorder` は構築後のプロパティです。`GridViewItem` は Key、Label、Tag、Disabled を持ちます。",
            "セルクリックは通常選択、Ctrl は追加・解除、Shift は一覧順の範囲選択で、disabled 項目の選択は無視します。`AllowReorder` 時は 4 px を超えるドラッグで同じ GridView の drop だけを受け入れますが、drag source では disabled を除外しないため、移動不可項目が必要なら受信側でも拒否します。ホイールと縦スクロールバーで行単位に移動します。",
            "Label は `Text`、disabled は低 alpha の `TextMuted`、選択面は `SurfaceAlt` に追従します。利用可能幅を `itemWidth` で割って 1 以上の列数を決めます。",
            "表示セルだけを可視行数 + 1 のプールへ実体化しますが、semantics は全項目を列挙します。画像、任意テンプレート、横スクロール、可変セルサイズはなく、items 更新は新しい一覧参照で行います。",
            "GridView",
            alternatives:
            [
                new("DataGrid", "列の意味を揃えてレコードを比較し、行単位で選択します。"),
                new("ListView", "一列の読み順と高密度な文字列一覧を優先します。"),
                new("TreeView", "項目間の親子関係を展開・折りたたみします。"),
            ],
            keyboard:
            [
                new("`Left` / `Right`", "row-major 順で前後の有効項目へ移動し、その項目を選択します。"),
                new("`Up` / `Down`", "計算済み列数ぶん前後の有効項目へ移動します。Shift 併用で範囲選択します。"),
                new("`Home` / `End`", "先頭または末尾の有効項目へ移動し、表示範囲へスクロールします。"),
                new("`Space`", "focused 項目を通常選択し、Ctrl 併用時は追加・解除します。"),
            ]
        ) with
        {
            Anatomy = "縦スクロールする固定高 viewport、幅から算出する row-major 列、可視セルプール、各セルの Label、選択面、右端の縦スクロールバーから構成します。",
            Variants = "通常形と `AllowReorder = true` の同一グリッド drag/reorder 形があります。`GridViewItem.Disabled` は click 選択と keyboard 移動から除外されますが、reorder の drag source からは除外されません。選択は単一・Ctrl toggle・Shift range を常時サポートします。",
            FocusActivationDismissal = "セル領域の click/drag が一つの GridView focus target を取得します。focus 中は方向キーを処理しますが専用 focus ring は描画しません。選択をすべて解除する dismissal キーはありません。",
            Accessibility = new(
                "各 `Label` を項目を単独で識別できる名前にし、グリッド全体の目的は周囲の見出しで示します。",
                "root は `SemanticRole.Grid`、各項目は direct child の `GridCell` として公開されます。",
                "selected と disabled は各 GridCell に反映されます。列数、行位置、focused Key、drop position は semantic state に含まれません。",
                "通常、disabled、selected の文字と面が利用テーマ上で判別できるか確認します。",
                "選択とスクロールに固有アニメーションはなく、drag 中だけ ghost を表示します。",
                "視覚上は仮想化されますが semantics は全件生成します。画像の代替テキストやカード内部の構造を表す API はありません。"),
            ThemeLayout = new(
                "`UiTheme.Text`、`TextMuted`、`SurfaceAlt` を使用し、個別色パラメーターはありません。",
                "有限幅を列計算に使い、無限幅では 3 セル分を既定幅にします。余り幅は末尾側に残り、横スクロールは行いません。",
                "`itemWidth` は最小 48 px、`itemHeight` は最小 24 px、`height` は最小 1 px です。列数は `floor(width / itemWidth)` の 1 以上です。"),
            Constraints = new(
                "一つの Label を持つ固定サイズ項目に限定され、画像、複数 slot、可変 span、横スクロールは提供しません。",
                "同じ Widget では選択、focus、scroll を保持します。items の一覧参照交換で消えた Key を除去するため、更新ごとに安定した一意 Key を維持します。",
                "通常操作は pointer/keyboard/scroll 対応 host で動作します。drag/reorder には `UiHost` が必要で、drop は同じ GridView からの payload だけを受け入れます。"),
            Api = new(
                "GridView",
                "`Signal<IReadOnlyList<GridViewItem>> items`、`height`、`itemWidth`、`itemHeight`、`AllowReorder`、読み取り用の `SelectedKeys` / `FocusedKey` / `RealizedCellCount` / `ScrollOffset` が中心です。",
                "`OnSelect(GridView, GridViewItem)` は pointer と keyboard 選択から発火します。`OnReorder(GridView, from, to)` は要求だけなので、呼び出し側が source を除去し、挿入 index を補正して新しい一覧を Signal へ設定します。"),
        },
        Page(
            "ListView",
            "Collections",
            "ListView",
            "固定高の文字列行を縦方向に仮想化し、複数選択、キーボード移動、同一リスト内の並べ替え要求を扱います。",
            [
                "一列の同種文字列を大量に表示し、可視行数 + 1 だけを実体化して滑らかにスクロールする場合。",
                "index ベースの単一・Ctrl toggle・Shift range 選択を pointer と keyboard の両方から扱う場合。",
                "並び順の正本を外部 `Signal<IReadOnlyList<string>>` に保ち、drag/drop の要求を受けてモデルを更新する場合。",
            ],
            [
                "各行に画像、複数列、任意の子 Widget、disabled 状態が必要な場合。`ListView` は文字列だけで行 template を持ちません。",
                "項目の選択を並べ替えや追加後も ID で保持したい場合。選択は Key ではなく index なので、別の keyed control を使います。",
                "階層表示や列比較が必要な場合。それぞれ `TreeView` または `DataGrid` を使います。",
            ],
            "var items = new Signal<IReadOnlyList<string>>([\"Alpha\", \"Bravo\", \"Charlie\"]);\nListView list = ListView(height: 240, rowHeight: 24, items: items,\n    onSelect: (_, index) => Select(index),\n    onReorder: (_, from, to) => Reorder(items, from, to));\nlist.AllowReorder = true;",
            "項目列と順序は呼び出し側の `items` Signal が所有します。selected index、複数選択集合、anchor、scroll offset、keyboard focus は ListView 内部です。items が別の一覧参照へ変わるたび選択を全解除し、scroll は新しい内容長へクランプします。",
            "`height`、`rowHeight`、`items`、`textColor`、`selectedColor`、`fontSize`、`OnSelect`、`OnReorder` が中心です。`AllowReorder` は構築後に設定し、`SelectedIndex` / `SelectedIndices` / `ScrollOffset` は読み取り専用です。",
            "行クリックは通常選択、Ctrl は追加・解除、Shift は anchor からの範囲選択です。ホイールは 0.12 秒の平滑スクロール、thumb drag は直接追従します。reorder 有効時は 4 px を超える drag を同じ ListView だけで受け、挿入線を表示します。",
            "文字は `TextMuted`、選択面は `SurfaceAlt` に追従し、`textColor`、`selectedColor`、`fontSize` で上書きできます。幅未指定時は 240 px、表示高は `height` です。",
            "10 万行でも描画ノード数は可視行数 + 1 に保たれます。固定行高・文字列表示に限定され、semantics は全行を列挙します。items の同一一覧を in-place 変更せず、新しい一覧参照を Signal に設定します。",
            "ListView",
            alternatives:
            [
                new("DataGrid", "複数列の値を見出し付きで比較します。"),
                new("GridView", "同じ大きさの項目を複数列へ配置します。"),
                new("TreeView", "親子関係を展開・折りたたみし、Key ベースで選択します。"),
            ],
            keyboard:
            [
                new("`Up` / `Down`", "前後の行へ移動して選択します。Shift 併用で anchor から範囲選択します。"),
                new("`Home` / `End`", "先頭または末尾行へ移動し、自動的に表示範囲へスクロールします。"),
                new("`PageUp` / `PageDown`", "表示行数 - 1 を単位に移動します。Shift 併用で範囲を延長します。"),
                new("`Space`", "現在行を通常選択し、Ctrl 併用時は追加・解除します。"),
                new("`Ctrl+A`", "全行を内部選択集合へ追加します。この操作だけでは `OnSelect` を発火しません。"),
            ],
            related:
            [
                new("Controls/Collections/ListView/Examples/Reorder", "並べ替え", "drag/drop 要求を受け、外部 items Signal の順序を更新する契約を確認します。", StoryKind.Example),
                new("Controls/Collections/ListView/Examples/Interactive", "対話パラメーター", "項目、表示高、行高、文字色、選択色を変更して密度と選択表示を確認します。", StoryKind.Example),
                new("Controls/Collections/ListView/Test/Huge", "10 万行テスト", "大量項目でも可視行プールだけを実体化する仮想化 fixture です。", StoryKind.TestFixture),
            ]
        ) with
        {
            Anatomy = "固定高 viewport、選択面、縦方向へ移動する content node、可視行数 + 1 の文字ノード/選択面プール、右端の scroll thumb、reorder 時の挿入線から構成します。",
            Variants = "通常形と `AllowReorder = true` の同一リスト drag/reorder 形があります。選択は単一・Ctrl toggle・Shift range・Ctrl+A を持ちます。行は常に文字列で、disabled、item template、horizontal orientation はありません。",
            FocusActivationDismissal = "行領域の click/drag が一つの ListView focus target を取得します。focus 中は一覧キーを処理しますが専用 focus ring は描画しません。選択を全解除する dismissal キーはありません。",
            Accessibility = new(
                "各文字列が単独で意味のある項目名になるようにし、一覧全体の目的は周囲の見出しで示します。",
                "root は `SemanticRole.List`、各行は `ListItem` として全件公開されます。",
                "各 ListItem の selected 状態は内部 index 集合から反映されます。focused index、anchor、drop position は公開されません。",
                "`textColor` と `selectedColor` を上書きする場合は、通常文字、選択面、背景の組み合わせを確認します。",
                "wheel scroll と単一選択 highlight は 0.12 秒で補間され、thumb drag は即時です。motion を無効化する固有 API はありません。",
                "描画は仮想化されますが semantics は全行を生成し、項目 ID や disabled state は持ちません。大規模一覧では支援技術コストを評価します。"),
            ThemeLayout = new(
                "既定文字は `UiTheme.TextMuted`、選択面は `SurfaceAlt`、drop indicator は `Primary` です。",
                "幅は Widget 共通指定を解決し、未指定では 240 px です。内容は固定 viewport 内で縦にスクロールします。",
                "`height` は最小 1 px、`rowHeight` は最小 8 px です。pool size は `ceil(height / rowHeight) + 1` です。"),
            Constraints = new(
                "固定行高の文字列だけを扱い、項目ごとの Widget、可変高、disabled、Key ベース選択は提供しません。",
                "scroll は同じ Widget の再実体化や resize をまたいで保持されます。items の一覧参照交換では選択を全解除するため、必要な永続選択は外部モデルから再適用します。",
                "通常操作は pointer/keyboard/scroll 対応 host で動作します。drag/reorder の開始には `UiHost` が必要で、同じ ListView 以外からの drop は拒否します。"),
            Api = new(
                "ListView",
                "`height`、`rowHeight`、`Signal<IReadOnlyList<string>> items`、`textColor`、`selectedColor`、`fontSize`、`AllowReorder`、`SelectIndex` / `MoveSelection` と読み取り用状態が中心です。",
                "`OnSelect(ListView, index)` は選択経路から発火します。`OnReorder(ListView, from, to)` の to は source 除去前の挿入位置なので、`to > from` なら通常 1 を減らしてから外部一覧へ挿入します。"),
        },
        Page(
            "NavigationView",
            "Collections",
            "NavigationView",
            "固定幅の左ナビゲーション pane と一つの content child を組み合わせ、共有 `Navigation` の path と履歴を操作します。",
            [
                "アプリケーションの主要 destination が少数で安定し、絶対 path と履歴を一つの `Navigation` に集約する場合。",
                "同じ navigation state を `NavigationHost` と共有し、pane 選択と content 再構築を同期する場合。",
                "back row、disabled destination、選択色を持つ固定デスクトップ向け二列 shell を構成する場合。",
            ],
            [
                "狭い幅で pane を自動折りたたみ、hamburger、overlay、top navigation に変形する必要がある場合。adaptive behavior はありません。",
                "同じ文脈内の少数 content を切り替えるだけの場合。履歴を持たない `Tabs` を使います。",
                "多数・階層化された destination、pane 内スクロール、仮想化が必要な場合。`TreeView` や専用 shell を使います。",
            ],
            "var navigation = new Navigation(\"/\", CanNavigate);\nNavigationView(navigation, items, showBackButton: true)[\n    new NavigationHost(navigation, BuildPage)\n]",
            "current path と back stack は呼び出し側が生成した `Navigation` が所有します。items と一つの content child も呼び出し側所有です。NavigationView は path/history Signal を追跡して pane を再構築し、`SelectedItem` を現在 path から導出します。",
            "`navigation`、`items`、`showBackButton`、`paneWidth`、`itemHeight` と pane/item/selected の色が中心です。content は生成ファクトリ引数ではなく `NavigationView(...)[content]` の indexer で設定・交換します。",
            "back row のクリックは `Navigation.Back()`、有効な destination row のクリックは正規化済み path で `Navigation.Navigate()` を呼びます。現在 path と同じ destination は no-op、disabled row は遷移しません。",
            "pane は `Surface`、通常文字は `TextMuted`、selected 背景は `SurfaceAlt`、selected 文字は `Primary` に追従し、個別色で上書きできます。pane と content は固定幅 + Star の二列です。",
            "pane は折りたたみ、overlay、scroll、virtualization を行いません。path は `/` で始まる絶対形式へ正規化され、大文字小文字を区別します。route resolution と content の生成は `NavigationHost` または呼び出し側の責務です。",
            "NavigationView",
            alternatives:
            [
                new("Tabs", "履歴を持たず、同じ文脈内の少数 content を切り替えます。"),
                new("TreeView", "多数または階層化された destination を展開・絞り込みします。"),
                new("NavigationHost", "pane を持たず、共有 Navigation の current path から content だけを再構築します。"),
            ],
            related:
            [
                new("Controls/Collections/Navigation/Examples/History", "Navigation 履歴", "Navigate、Replace、Back と case-sensitive path の状態遷移を確認します。", StoryKind.Example),
            ]
        ) with
        {
            Anatomy = "左の固定幅 pane（8 px padding、任意の back Button、destination Button の縦列）と、右の一つの content child を持つ二列 Grid から構成します。content 未指定時は Spacer を使います。",
            Variants = "back row の表示/非表示、destination の `IsEnabled`、`paneWidth`、`itemHeight`、4 種の pane/selection 色を変更できます。pane position は左固定で、compact、overlay、top、hierarchical variant はありません。",
            FocusActivationDismissal = "NavigationView 自身は focus target を登録せず、現行の pane Button も pointer click だけを実装します。content 内の focusable controls は独立して参加します。destination の閉じる/dismissal 概念はありません。",
            Accessibility = new(
                "各 `NavigationViewItem.Label` を destination 名として明確にし、back row の表示文字も目的を示します。",
                "NavigationView は navigation/list/current-item semantics を公開せず、内部 Button も専用 semantic provider を持ちません。",
                "selected と disabled は見た目と pointer behavior に反映されますが、支援技術へ current/disabled state を通知する契約はありません。",
                "pane、通常文字、selected 背景/文字を上書きする場合は通常・selected・disabled の各状態で確認します。",
                "固有アニメーションはなく、path 変更で CompositeControl の内容を再構築します。",
                "keyboard-only navigation と current destination semantics が必要な shell では、代替操作または対応コントロールを用意します。"),
            ThemeLayout = new(
                "既定色は `UiTheme.Surface`、`TextMuted`、`SurfaceAlt`、`Primary` です。",
                "`HAlign` / `VAlign` は既定で Stretch になり、左を固定 `paneWidth`、右を残り幅の Star column として配置します。",
                "`paneWidth` は 0 以上、`itemHeight` は 1 px 以上です。pane 内は 8 px padding、row 間は 4 px で、項目数に応じた overflow 処理はありません。"),
            Constraints = new(
                "固定左 pane と一つの content に限定され、responsive collapse、pane scroll、nested groups、route registry は提供しません。",
                "同じ `Navigation` インスタンスを保持すれば current path と history が再構築をまたいで残ります。content indexer の交換は NavigationView を Rebuild します。",
                "通常の pointer/render host で動作し、overlay/drag サービスは不要です。現行 pane は keyboard/focus/semantic 非対応です。"),
            Api = new(
                "NavigationView",
                "`Navigation navigation`、`IReadOnlyList<NavigationViewItem> items`、`showBackButton`、`paneWidth`、`itemHeight`、色指定、content indexer、読み取り用 `SelectedItem` が中心です。",
                "固有 `UiEvent` はありません。destination は `Navigation.Navigate`、back row は `Navigation.Back` を直接呼びます。`canNavigate` が destination を拒否すると Navigate は `InvalidOperationException` を送出するため、items と route predicate を一致させます。"),
        },
        Page(
            "ScrollViewer",
            "Collections",
            "ScrollViewer",
            "一つの子 Widget を viewport 幅・無限高で測定し、縦方向の clip、wheel、scroll thumb を提供します。",
            [
                "一つの非仮想化 content が利用可能な高さを超え、縦方向に任意位置まで閲覧する必要がある場合。",
                "スクロールされた子の pointer hit test を transform に追従させ、内部の Button などをそのまま操作する場合。",
                "同じ ScrollViewer インスタンスを保ったまま `SetViewportHeight` で resize し、scroll offset を保持する場合。",
            ],
            [
                "大量の同種項目を表示する場合。全子ノードを実体化するため、`ListView`、`GridView`、`DataGrid` の仮想化を使います。",
                "横方向または両方向の scroll、keyboard scroll、focus item の自動 reveal が必要な場合。現実装は縦 pointer scroll だけです。",
                "複数の独立した子を直接渡す場合。一つの Stack/Grid にまとめてから単一 child として設定します。",
            ],
            "Scroll(320, width: 480)[\n    VStack(8)[rows]\n]",
            "content model は呼び出し側が所有し、scroll offset は ScrollViewer 内部の `ScrollModel` が保持します。同じ Widget の再実体化と viewport resize では位置を保ち、内容長が短くなった場合だけ有効範囲へクランプします。",
            "公開ファクトリ名は `Scroll` です。`viewportHeight`、indexer の単一 child、`thumbColor`、Widget 共通の width/height、動的 resize 用 `SetViewportHeight` が中心です。",
            "viewport 上の wheel で目標 offset を更新し、右端 thumb の drag は直接 offset を変更します。スクロール後も child の pointer hit は表示位置へ追従します。背景 drag、pan、horizontal scroll はありません。",
            "thumb は `BorderColor` に追従し、`thumbColor` で上書きできます。親から有限幅または明示 width を与え、`viewportHeight` で見える高さを決めます。",
            "子は viewport 幅・無限高で一度に layout/realize され、virtualization は行いません。wheel 表示は 0.12 秒で補間され、thumb drag は即時です。専用 keyboard、focus、semantic scroll region はありません。",
            "ScrollViewer",
            alternatives:
            [
                new("ListView", "大量の一列文字列を固定行高で仮想化します。"),
                new("GridView", "大量の固定サイズ項目を複数列で仮想化します。"),
                new("DataGrid", "列見出しを持つ多数のレコードを行単位で仮想化します。"),
            ],
            related:
            [
                new("Controls/Collections/ScrollViewer/Examples/Clickable", "スクロール後のクリック", "scroll transform 後も子 Button の hit test と thumb drag が一致することを確認します。", StoryKind.Example),
            ]
        ) with
        {
            Anatomy = "viewport clip を持つ root、offset transform を適用する一つの content node、任意の単一 child、右端の縦 scroll thumb から構成します。",
            Variants = "`viewportHeight` と width、`thumbColor` を変更できます。子なしも許容されますが、horizontal/both orientation、常時表示設定、keyboard scroll、複数 child variant はありません。",
            FocusActivationDismissal = "ScrollViewer 自身は focus target を登録しません。child の focusable control は独立して focus を取得できますが、focus 変化で自動 scroll しません。起動・dismissal の概念はありません。",
            Accessibility = new(
                "周囲の見出しで scroll 領域の内容を説明し、子 Widget 自身のラベルを維持します。",
                "ScrollViewer は scroll region role、offset、範囲を semantic node として公開しません。",
                "scroll 可能状態、現在位置、上端/下端は支援技術へ通知されません。",
                "thumbColor を変更する場合は背景とのコントラストと、細い thumb の視認性を確認します。",
                "wheel は 0.12 秒で補間し、thumb drag は直接追従します。固有の reduced-motion 切替はありません。",
                "keyboard scroll と focus reveal がないため、child へ別経路で到達できる構成を用意し、長文の唯一の閲覧経路にしないよう評価します。"),
            ThemeLayout = new(
                "scroll thumb の既定色は `UiTheme.BorderColor` で、content のテーマは child が決めます。",
                "幅指定を優先し、未指定かつ有限制約なら親幅を使います。child はその幅と無限高で測定され、viewport で clip されます。",
                "表示高は `height` 指定または `viewportHeight` から解決します。`SetViewportHeight` は同じインスタンスの override を更新し、offset を保持します。"),
            Constraints = new(
                "縦方向・単一 child・非仮想化に限定されます。nested scroll はイベント競合と到達性を個別に検証します。",
                "scroll model は同じ Widget インスタンスの再実体化/resize をまたいで残ります。インスタンスを作り直すと位置は初期化されます。",
                "wheel と pointer drag を配信できる host で動作します。drag/drop や overlay サービスは不要ですが、keyboard/semantic 対応はありません。"),
            Api = new(
                "ScrollViewer",
                "公開 factory `Scroll(viewportHeight)`、単一 child indexer、`viewportHeight`、`thumbColor`、`SetViewportHeight(float)` と Widget 共通の width/height が中心です。",
                "固有イベントはありません。scroll offset は内部状態で、公開 getter/setter や scroll-changed callback は提供しません。"),
        },
        Page(
            "TabStrip",
            "Collections",
            "TabStrip",
            "文書や作業対象を横一列で切り替え、Marker、Badge、Tooltip、閉じる要求、keyboard、drag/drop、overflow を扱うタブ帯です。",
            [
                "複数文書や編集対象を Key で切り替え、active、dirty marker、Badge、close availability を項目ごとに表す場合。",
                "disabled を飛ばす keyboard navigation と `TabList` / `Tab` semantics を必要とする場合。",
                "同一帯または共有 `dragChannel` の複数帯で、source/target identity を含む drop 要求をモデルへ返す場合。",
            ],
            [
                "タブ内容も同じ control に保持する少数の固定ビューの場合。`Tabs` の方が構成が単純です。",
                "アプリ全体の階層 destination や back history を扱う場合。`NavigationView` を使います。",
                "overflow 項目を一覧 menu から直接選ぶ必要がある場合。現実装の `+N` は viewport を前方へ送るだけです。",
            ],
            "TabStrip(items: tabs, selectedKey: activeKey,\n    onSelect: (_, key) => Select(key),\n    onCloseRequest: (_, key) => RequestClose(key),\n    onDropRequest: (_, request) => ApplyDrop(request))",
            "`items`、`selectedKey`、文書の存続、並び順は呼び出し側が正本です。TabStrip は `FocusedKey`、横 `ScrollOffset`、`OverflowCount` を保持し、Marker Signal へライブ追従します。すべてのイベントは要求であり、選択・削除・並べ替えを自動反映しません。",
            "`items`、`selectedKey`、`dragChannel`、`OnSelect`、`OnCloseRequest`、`OnDropRequest` が中心です。`TabStripItem` は Key、Title、Marker、Badge、Tooltip、Closable、Disabled を持ちます。Key は空白不可・重複不可です。",
            "タブ本体の click で選択、close glyph で close 要求、4 px を超える drag で同一 channel の帯へ drop します。wheel は両方向に横移動し、`+N` click は viewport の 75% ずつ前方へ進めます。Tooltip は hover 中だけ上側 overlay に表示します。",
            "高さ 32 px、各タブ 64〜190 px、overflow 領域 28 px を基準にします。選択面/下線は `SurfaceAlt` / `Primary`、通常・disabled 文字は `TextMuted` に追従し、親から有限幅を与えます。",
            "Title/Badge は最大幅で clip されます。overflow は menu ではなく横 viewport です。drag/drop には `UiHost` と一致 channel が必要で、close/drop 後のモデル変更と focus の再配置は呼び出し側が行います。",
            "TabStrip",
            alternatives:
            [
                new("Tabs", "閉じる・Marker・drag を持たない少数 content を同じ Widget 内で切り替えます。"),
                new("DocumentTabs", "keyboard/semantics/overflow が不要な既存 DockHost 向けの軽量タブ帯を使います。"),
                new("NavigationView", "文書ではなくアプリの主要 destination と back history を扱います。"),
            ],
            keyboard:
            [
                new("`Left` / `Right`", "disabled を除くタブ間を循環し、focus と selection 要求を同時に移動します。"),
                new("`Home` / `End`", "先頭または末尾の有効タブへ移動し、隠れていれば自動 reveal します。"),
                new("`Enter` / `Space`", "focused タブが有効なら `OnSelect` を通知します。"),
                new("`Delete`", "focused タブが closable かつ有効なら `OnCloseRequest` を通知します。"),
            ]
        ) with
        {
            Anatomy = "下辺 hairline、横 scroll viewport、各タブの選択面/下線、Title と任意 Badge、Marker、close glyph、hover Tooltip overlay、右端 `+N` overflow control、drop 挿入線から構成します。content 領域は持ちません。",
            Variants = "項目ごとの `Marker` Signal、Badge、Tooltip、Closable、Disabled、外部 selected Key、既定の自己 channel または共有 `dragChannel` があります。全体は常に horizontal・32 px 高で、vertical/stacked variant はありません。",
            FocusActivationDismissal = "タブ本体、close glyph、overflow control は同じ TabStrip focus target を取得します。focus は disabled を除外し、選択/focus 項目を viewport へ reveal します。close は request だけなので、削除後にどの Key を選ぶかは所有者が決めます。",
            Accessibility = new(
                "各 `Title` を文書名として明確にし、必要な補足は `Tooltip` に設定します。",
                "root は `SemanticRole.TabList`、全項目は `Tab` として Title、Key、Tooltip description を公開します。",
                "selected と disabled は Tab semantic に反映されます。Marker、Badge、Closable、close button、overflow count、drop position は独立した semantic state/control として公開されません。",
                "選択面、下線、通常/disabled 文字が利用テーマ上で判別できるか確認し、Marker だけに重要状態を依存させません。",
                "選択切替に固有アニメーションはなく、drag ghost と Tooltip overlay を必要時だけ表示します。",
                "close glyph と overflow control は別 semantic child ではありません。Marker/Badge の意味が重要なら Title、Tooltip、周囲の状態表示でも伝えます。"),
            ThemeLayout = new(
                "`SurfaceAlt`、`Primary`、`Text`、`TextMuted`、`BorderColor` を使用し、個別色パラメーターはありません。Tooltip は theme の Text/OnAccent を使います。",
                "有限幅から overflow control の有無と viewport 幅を決めます。selected/focused Key は layout 時に reveal され、画面外タブは描画ノードを作りません。",
                "固定高 32 px、タブ幅 64〜190 px、close 列 20 px、overflow 列 28 px です。Badge は Title に連結して同じ幅計算と clip を受けます。"),
            Constraints = new(
                "content、保存確認、selection の正本、削除、並べ替えは管理しません。overflow は前進 button と wheel だけで、一覧 menu や後退 button はありません。",
                "layout ごとに Key の空白/重複を検証します。items から focused Key が消えるか disabled になると、selected Key または先頭の有効項目へ補正します。Marker は同じ Signal の更新へ追従します。",
                "通常操作は pointer/keyboard/overlay 対応 host で動作します。drag/drop には `UiHost` が必要で、default channel は自己帯、共有 channel は帯間移動を許可します。"),
            Api = new(
                "TabStrip",
                "`IReadOnlyList<TabStripItem> items`、`selectedKey`、`dragChannel`、読み取り用 `FocusedKey` / `OverflowCount` / `ScrollOffset`、`ValidateItems` と座標 helper が中心です。",
                "`OnSelect(TabStrip, key)`、`OnCloseRequest(TabStrip, key)`、`OnDropRequest(TabStrip, TabDropRequest)` は要求だけを通知します。drop request は Key、現在の target 配列に対する Index、SourceStrip、TargetStrip を含み、同一帯の source 除去後は index を正規化します。"),
        },
        Page(
            "Tabs",
            "Collections",
            "Tabs",
            "同じ文脈内の少数 content を横タブで切り替え、すべての content を保持したまま選択中だけ表示します。",
            [
                "2〜数個程度の固定ビューを同じ文脈内で切り替え、閉じる・disabled・並べ替えを必要としない場合。",
                "非選択 content の Widget 状態を保持し、再生成せず `Visible` だけを切り替えたい場合。",
                "選択 index を外部 `Signal<int>` の正本として、別 UI と双方向に同期する場合。",
            ],
            [
                "文書の close、dirty Marker、Badge、Tooltip、drag、overflow が必要な場合。`TabStrip` を使います。",
                "アプリの主要 destination、path、back history を扱う場合。`NavigationView` を使います。",
                "多数のタブ、狭い幅、dynamic add/remove、アクセシブルな Tab semantics が必要な場合。overflow と semantic provider はありません。",
            ],
            "var selected = new Signal<int>(0);\nTabs(\n    [\"概要\", \"設定\"],\n    [overview, settings],\n    selected, width: 420, height: 240)",
            "選択 index は必須の外部 `Signal<int>` が所有し、click と Left/Right が直接値を書き換えます。content Widget は呼び出し側が所有し、全件が layout/realize されたまま holder の `Visible` だけが切り替わるため、同じインスタンスの内部状態を保持します。",
            "`labels`、`contents`、`selected`、`fontSize`、下線用 `foreground`、Widget 共通の width/height が中心です。固有 event、disabled、close、orientation、overflow parameter はありません。",
            "40 px のタブ見出しをクリックすると selected index を更新します。非選択 content は `Visible = false` になり、描画順と hit test から除外されます。pointer hit はタブごとの focus target を持ちません。",
            "選択文字は `Primary`、非選択文字は `TextMuted`、下線は `foreground` または `Primary` に追従します。content は strip 下の同じ矩形へ重ねて配置されます。",
            "labels は 1 件以上、contents と同数、selected は有効範囲に保ちます。タブ帯は clip/scroll/折返しを行わず、全 content を実体化するため多数・重量級 content には不向きです。",
            "Tabs",
            alternatives:
            [
                new("TabStrip", "close、Marker、Badge、Tooltip、keyboard semantics、drag、overflow を持つタブ帯を使います。"),
                new("NavigationView", "path と履歴を持つアプリケーション destination を切り替えます。"),
            ],
            keyboard:
            [
                new("`Left`", "selected index を 1 減らし、先頭で停止します。"),
                new("`Right`", "selected index を 1 増やし、末尾で停止します。"),
            ],
            related:
            [
                new("Controls/Collections/Tabs/Examples/Interactive", "対話パラメーター", "labels、selected、下線色を変更し、見出しと content の対応を確認します。", StoryKind.Example),
            ]
        ) with
        {
            Anatomy = "40 px の横タブ見出し、選択位置と幅へ移動する 3 px 下線、同じ content 矩形に重なる複数 holder から構成します。選択中 holder だけが Visible です。",
            Variants = "`fontSize`、下線 `foreground`、width/height を変更できます。選択は外部 Signal の一つだけで、disabled、close、Badge、Tooltip、scroll、vertical orientation はありません。",
            FocusActivationDismissal = "Tabs は strip 全体に一つの focus target と focus ring を登録します。focus traversal で取得した後は Left/Right が選択を直接変更します。各タブは独立 focus target ではなく、Enter/Space activation や dismissal はありません。",
            Accessibility = new(
                "各 label を content の目的が分かる短い名前にし、見出しと content の対応を同じ順序で保ちます。",
                "`Tabs` は `TabList` / `Tab` / `TabPanel` semantic role を公開しません。",
                "selected index は見た目と content visibility に反映されますが、支援技術へ selected state を通知しません。",
                "selected と非 selected の文字色、下線、content 境界が利用テーマ上で十分に判別できるか確認します。",
                "下線は 0.16 秒で位置と幅を補間します。固有の reduced-motion 切替はありません。",
                "semantic tabs が必須の UI には使用せず、`TabStrip` などの対応 control または別のラベル付き操作を選びます。"),
            ThemeLayout = new(
                "selected 文字は `UiTheme.Primary`、通常文字は `TextMuted`、下線は `foreground` または `Primary` です。",
                "有限制約では親幅/高さを使い、無限制約では tab 自然幅と高さ 160 px を fallback にします。全 content は strip 下へ同じ制約で layout されます。",
                "strip 高は固定 40 px です。タブ幅は label 測定幅 + 左右 16 px で、利用幅を超えても clip、scroll、wrap しません。"),
            Constraints = new(
                "labels/contents の件数整合を検証する例外はありませんが、1 件以上・同数を呼び出し側の契約とします。範囲外 selected は下線だけ clamp され、content が非表示になり得ます。",
                "同じ content Widget は非選択中も保持・実体化されます。labels/contents の構造変更は親の再構築で行い、selected Signal だけを動的状態として更新します。",
                "通常の pointer/keyboard/render host で動作し、overlay/drag サービスは不要です。ただし semantic provider はありません。"),
            Api = new(
                "Tabs",
                "`string[] labels`、`Widget[] contents`、`Signal<int> selected`、`fontSize`、下線用 `foreground`、width/height が中心です。",
                "固有 `UiEvent` はありません。click と Left/Right は渡された selected Signal を直接書き換えるため、変更通知は Signal の購読側で処理します。"),
        },
        Page(
            "TreeView",
            "Collections",
            "TreeView",
            "Key 付き階層データを平坦な可視行へ展開し、開閉、複数選択、絞り込み、外観調整を扱います。",
            [
                "ファイル、設定カテゴリ、ドキュメント目次など、親子関係そのものが情報構造である場合。",
                "stable Key と外部 expanded set を使い、再構築をまたいで開閉状態を共有・保持する場合。",
                "Label/SearchText の部分一致で一致ノードと祖先だけを全展開表示し、元の開閉 set を壊さず絞り込む場合。",
            ],
            [
                "平坦な大量一覧や列比較が主目的の場合。仮想化する `ListView` / `DataGrid` を使います。",
                "巨大階層、遅延 child load、virtualization、循環検出が必要な場合。TreeView は全可視行を Widget として構築します。",
                "標準的な Left で親へ移動、Right で子へ移動、expanded semantic state が必須の場合。現行 keyboard/semantics はその完全な tree pattern ではありません。",
            ],
            "var selected = new Signal<string>(\"docs\");\nvar expanded = new HashSet<string> { \"docs\" };\nTreeView(nodes, expanded: expanded, selected: selected, filter: query,\n    onSelect: (_, node) => selected.Value = node.Key)",
            "roots と永続 selected Key は呼び出し側が所有します。`expanded` set を渡すと TreeView が同じ set を直接 mutate し、省略時は内部 HashSet を Widget 寿命中保持します。pointer/keyboard の複数選択は内部 `SelectedKeys` にあり、外部 `selected` へは自動書き戻さないため `OnSelect` で同期します。",
            "`roots`、`expanded`、`selected`、`filter`、`appearance`、`OnSelect` が中心です。`TreeNode` は Key、Label、Children、Tag、SearchText を持ち、`Expand(key)`、`FocusedKey`、`SelectedKeys` も公開します。",
            "leaf row は選択、Tag 付き parent の label は選択 + 展開、Tag なし parent の通常 click は開閉です。chevron は parent 種別に関係なく開閉します。Ctrl click は toggle selection、Shift click は可視行順の range selection です。",
            "既定または `TreeViewAppearance` で row height/spacing、indent、padding、radius、folder/leaf font、各状態色を制御します。深さは indent、parent は chevron、selected/hover は面と文字色で示します。",
            "scroll と virtualization は内蔵せず、可視行をすべて再構築します。filter は大小無視の部分一致で一致ノードと祖先を全展開表示しますが expanded set は変更しません。Key 重複、循環、遅延 load は検証しません。",
            "TreeView",
            alternatives:
            [
                new("ListView", "平坦な一列項目を固定行高で仮想化します。"),
                new("DataGrid", "階層ではなく複数列のレコードを比較します。"),
            ],
            keyboard:
            [
                new("`Up` / `Down`", "前後の可視行へ移動して選択します。Shift 併用で可視行順の範囲選択を延長します。"),
                new("`Home` / `End`", "最初または最後の可視行へ移動して選択します。"),
                new("`Right`", "focused parent を展開して同じ行に留まり、その行を選択します。子へは自動移動しません。"),
                new("`Left`", "focused parent が展開中なら折りたたんで同じ行に留まります。親行へは自動移動しません。"),
                new("`Space`", "focused 行を通常選択し、Ctrl 併用時は内部選択集合を追加・解除します。"),
            ],
            related:
            [
                new("Controls/Collections/TreeView/Examples/Interactive", "選択と絞り込み", "selected と filter を変更し、展開 set を保った絞り込みを確認します。", StoryKind.Example),
                new("Controls/Collections/TreeView/Examples/Utilities", "外観 utilities", "row height、spacing、indent、radius、selected background の utility 適用を確認します。", StoryKind.Example),
                new("Controls/Collections/TreeView/States/Selection", "選択状態", "外部 selected Key と expanded set による選択ハイライトを確認します。", StoryKind.State),
                new("Controls/Collections/TreeView/States/Expanded", "展開状態", "複数 branch を展開した階層密度と indent を確認します。", StoryKind.State),
            ]
        ) with
        {
            Anatomy = "可視ノードを深さ付きで平坦化した縦 row 列、parent の chevron、Label、hover/selected 背景、空結果時の `(該当なし)` 行から構成します。scroll container は含みません。",
            Variants = "外部または内部 expanded set、外部 selected Key と内部 multi-selection、filter Signal、Tag なし group / Tag 付き selectable parent / leaf、`TreeViewAppearance` と control-specific utilities があります。disabled node、lazy child、checkbox、drag/reorder はありません。",
            FocusActivationDismissal = "各 styled row の hit は TreeView 共通の一つの focus target を取得します。focus 中は階層キーを処理しますが専用 focus ring は描画しません。選択解除や tree dismissal のキーはありません。",
            Accessibility = new(
                "各 `Label` をノード名にし、検索専用語は非表示の `SearchText` に分離します。",
                "root は `SemanticRole.Tree`、可視ノードはすべて direct child の `TreeItem` として Label/Key を公開します。semantic tree は親子の入れ子を再現しません。",
                "selected は内部 SelectedKeys または外部 selected Key から反映されます。expanded/collapsed、depth、parent/leaf、focused state は公開されません。",
                "folder/leaf/hover/selected/chevron 色を上書きする場合、文字と背景の各組み合わせを確認します。",
                "固有アニメーションはなく、開閉・filter・選択で CompositeControl の行構造を再構築します。",
                "semantic hierarchy と expanded state がないため、完全な tree accessibility pattern が必要な画面では補助の検索・平坦一覧または別実装を用意します。"),
            ThemeLayout = new(
                "未指定色は `TextMuted`、`Text`、`Primary`、`SurfaceAlt` へ解決され、`TreeViewAppearance` または utilities で個別に上書きできます。",
                "可視行は `VStack` に並び、各 row は有限幅なら親幅へ伸びます。depth ごとに `Indent` を加え、TreeView 自身は clip/scroll しません。",
                "標準 appearance は row 31 px、spacing 1 px、indent 16 px です。未指定 appearance の compact fallback は row 22 px、spacing 3 px、indent 12 px です。"),
            Constraints = new(
                "全可視行を構築するため巨大 tree の virtualization はありません。cycles、duplicate Key、async children、drop reorder はモデル側で防ぎます。",
                "外部 expanded set は TreeView が直接 mutate します。外部コードだけで set を変更した場合は親 Rebuild または `Expand` 等で再評価を発生させます。内部 set と内部 selection は Widget を作り直すと失われます。",
                "通常の pointer/keyboard/render host で動作し、overlay/drag サービスは不要です。scroll が必要なら外側の ScrollViewer を使いますが、大規模 tree の全実体化は変わりません。"),
            Api = new(
                "TreeView",
                "`IReadOnlyList<TreeNode> roots`、`ISet<string> expanded`、`selected`、`filter`、`TreeViewAppearance`、`Expand`、読み取り用 `FocusedKey` / `SelectedKeys` と utilities が中心です。",
                "`OnSelect(TreeView, TreeNode)` は leaf、selectable parent、pointer/keyboard selection から発火します。Tag なし parent の通常 click/chevron toggle は発火せず、selected Signal も自動更新しないため、永続選択は handler で書き戻します。"),
        },
    ];
}
