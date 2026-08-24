using Luxel.Gallery.UI;

namespace Luxel.Editor.Gallery;

internal static partial class EditorControlDocs
{
    private static readonly ControlDocsPage[] Pages =
    [
        DetailedPage(
            type: "AssetBrowser",
            category: "Collections",
            summary: "プロジェクトの論理アセットパスをフォルダーツリーと現在フォルダーの一覧／グリッドで参照し、開く・作成・名前変更・移動・複製・削除・取り込み・OS での表示要求へ接続する Editor pane です。OS の汎用ファイルマネージャーではなく、`IFileStorage` と `IAssetOperations` を Editor の document lifecycle に結び付けるための UI です。",
            useWhen:
            [
                "EditorSession と同じ `IFileStorage`／`IAssetOperations` を使い、アセット mutation の結果を開いている document binding と調整する場合。",
                "フォルダー単位の検索、単一／複数選択、List／Grid 表示、import/reveal capability の可否を一つの pane にまとめる場合。",
                "Browser・Native・Gallery fixture で同じ論理パス UI を使い、ホスト固有の picker や file manager 起動だけを event で外へ返す場合。",
            ],
            avoidWhen:
            [
                "任意の OS パス、権限、シンボリックリンク、ドライブ、ネットワーク共有を扱う汎用ファイルマネージャーが必要な場合。",
                "数万件以上を仮想化・ページング・非同期検索する必要がある場合。現在フォルダー面はスクロールも virtualization も持ちません。",
                "一覧セルまで完全なキーボード操作や tree/grid の完全な支援技術表現が必須な場合。",
            ],
            alternatives:
            [
                new("TreeView", "読み取り中心の階層ナビゲーションだけが必要で、作成・mutation・現在フォルダー一覧が不要な場合。"),
                new("GridView", "呼び出し側が用意した平坦な項目をカード表示し、アセット service や folder model を持たせない場合。"),
            ],
            usage: """
                AssetBrowser browser = EditorKit.AssetBrowser(
                    storage: files,
                    operations: session.Assets,
                    expanded: expandedFolders,
                    onOpen: (_, path) => session.OpenAsset(path),
                    onImportRequest: view => ImportInto(view),
                    onRevealRequest: (_, path) => host.Reveal(path),
                    onMutation: (_, result) => Log(result));
                """,
            anatomy: "上から、現在フォルダーだけを絞る `TextField`、`Assets`／現在パス／`List`／`Grid`／`Refresh` の toolbar、幅 190 px の folder-only `TreeView`、現在フォルダーを描く `AssetItemsView`、作成・rename・duplicate・move・delete・import・reveal の action 群、`LastError` の文字表示で構成します。`AssetItemsView` は List では 27 px 行、Grid では 112×70 px cell を描き、全体を最大 280 px 高に収めます。",
            variants: "`operations` を渡す構成が本番向けです。省略時は `storage` から fallback の `AssetOperations` を作ります。表示は `AssetBrowserViewMode.List`／`Grid`、host capability に応じた Import／Reveal の有効・理由表示、外部所有の `expanded` set、初期 `filter` で変わります。`filter` は最初の build で内部 draft へ取り込まれ、その後の入力は control instance 内に保持されます。",
            state: "アセット内容、path validation、mutation、capability は `IAssetOperations` とその `Storage` が正本です。`AssetBrowserModel` は列挙済み path、現在フォルダー、filter、selection、error、version を保持し、`Refresh()` で storage から再同期します。List／Grid mode、入力 draft、`LastError` は `AssetBrowser` が所有し、`expanded` set だけは呼び出し側が所有して再構築をまたいで共有できます。現在フォルダー、selection、view mode、filter draft の永続化は組み込まれていないため、pane 再生成後も必要なら Editor session 側へ退避します。",
            pointer: "folder tree の行でフォルダーを開き、現在フォルダーの folder cell はその folder へ移動します。file cell の通常クリックは単一選択して直ちに `OnOpen(path)`、`Ctrl` クリックは toggle、`Shift` クリックは追加選択ですが range selection ではなく、追加選択時は `OnOpen` を発火しません。右クリックは Open／Duplicate／Rename／Move／Delete と、capability があれば Reveal を開きます。surface への drop は `AssetImportPayload` だけを受け、現在フォルダーへ import します。",
            keyboard:
            [
                new("↑ / ↓ / Home / End", "folder tree の focus 行を移動してその行を選択します。AssetBrowser では選択 callback が現在フォルダーを開きます。"),
                new("← / →", "folder tree の現在行を折りたたみ／展開し、同じ行を選択します。"),
                new("Space / Ctrl+Space", "folder tree の focus 行を選択／内部選択を toggle します。現在フォルダー一覧の file cell には対応しません。"),
                new("Shift+方向キー", "TreeView 内部の可視行選択を拡張しますが、file の range selection にはなりません。"),
                new("← / → / Home / End / Backspace / Delete / Ctrl+A/C/X/V", "filter、path、name、target folder の各 `TextField` が通常の 1 行編集として処理します。"),
            ],
            focus: "folder tree の行をクリックすると `TreeView` の focus target が有効になります。各 `TextField` は独立して focus を受けますが、現在フォルダーの List／Grid cell、toolbar と action の `Button`、context-menu row は focus target を登録していません。context menu は項目実行、外側クリック、`Escape` で閉じます。フォルダーを開くと file selection は消えます。",
            accessibility: new ControlDocsAccessibility(
                "pane 見出しと action label に加え、周囲でプロジェクト名または『アセット』であることを示します。",
                "folder tree は `Tree`／`TreeItem` semantics を公開します。現在フォルダーの custom List／Grid cell と context action は semantic collection/item role を公開しません。",
                "folder tree の選択、List／Grid の選択色、capability 理由、`LastError` を見える文字でも示します。複数 file selection の件数は専用に読み上げません。",
                "`Surface`、`SurfaceAlt`、`Primary`、`Text`、`TextMuted` を使うため、選択色と file 名、error text のコントラストを利用テーマで確認します。",
                "hover 以外の必須 animation はありません。drag/drop の highlight だけに import 可否を依存させず、Import action も用意します。",
                "current-folder cell は pointer-only で、完全な list/grid semantics、range selection、scrolling、rename validation のインライン説明はありません。キーボード利用者には検索、tree、明示コマンドなど別経路を用意します。"),
            theme: new ControlDocsThemeLayout(
                "surface、selected、muted、error の各色は `UiTheme` に追従し、folder tree と item surface の境界を隣接 pane と揃えます。",
                "実装は filter と toolbar の下に tree と current-folder pane を横並びにします。tree 幅は 190 px、filter 幅は 260 px、current item surface は少なくとも 220 px を要求します。",
                "item surface は最大 280 px 高で、List は 27 px 行、Grid は 112×70 px cell です。狭い dock では action 行が収まらないため、Asset pane には概ね 430 px 以上の幅を確保します。"),
            constraints: new ControlDocsConstraints(
                "`List()`、mutation、`Refresh()` は同期実行で全 path を列挙・検証・sort します。current item surface は scrolling／virtualization を持たず、280 px を越える項目は描画されないため、大規模フォルダーには別の仮想化 UI が必要です。drop payload は `AssetImportPayload` 限定で、`filter` は現在フォルダー名への部分一致です。",
                "storage と operations は呼び出し側／EditorSession が所有します。operations の instance が変わると model を作り直します。外部変更を自動監視しないため host watcher から `Refresh()` を呼びます。mutation 成功後は model を refresh し、作成 path を再選択して `OnMutation` を通知します。",
                "Memory／Browser／Native の `IFileStorage` で利用できますが、import、reveal、永続性、file watching は `IHostCapabilities` と `IEditorAssetHost` に依存します。UI thread を塞ぐ高遅延 storage を直接接続しないでください。"),
            apiHighlights: "`storage`、`operations`、`filter`、`expanded`。公開状態は `Model`、`ViewMode`、`CurrentFolder`、`SelectedPaths`、`CurrentItems`、`FolderTree`、`LastError`、capability。操作 API は `OpenFolder`、`SelectAsset`、`SetViewMode`、`CreateAsset`、`RenameSelected`、`MoveSelected`、`DuplicateSelected`、`DeleteSelected`、`Import`、`HandleDrop`、`Refresh` です。",
            eventContracts: "`OnOpen(path)` は file の起動要求、`OnImportRequest` と `OnRevealRequest(path)` は host UI への要求です。`OnMutation(result)` は operation 実行後の `AssetMutationResult` を返し、部分成功／失敗も含みます。document の rename・move・delete 調整や永続化は `EditorSession`／`IAssetOperations` 側で行います。",
            related:
            [
                new("Controls/Editor/SceneEditorView/Assets", "シーン用アセットと atlas 定義", "AssetBrowser から atlas 定義を開き、PropertyGrid と組み合わせる構成です。", StoryKind.Unspecified),
                new("Examples/Workbench/Files", "Workbench: files", "AssetBrowser → DocumentStore → DockHost → save／external change の通し例です。", StoryKind.Example),
                new("Examples/Workbench/Inspector", "Workbench: inspector document", "JSON 設定を AssetBrowser から開き、document として編集・保存します。", StoryKind.Example),
                new("Examples/Workbench/Material", "Workbench: material", "material graph と Slang file を同じ asset pane から開く構成です。", StoryKind.Example),
            ]),

        DetailedPage(
            type: "DockHost",
            category: "Editor",
            summary: "不変 `DockTree` を、tab group、splitter、drop zone、host 内 floating pane として描画し、tab activation・並べ替え・group 間移動・edge docking・split ratio・float position/size を同じ tree signal へ書き戻す Editor work area です。",
            useWhen:
            [
                "document と tool pane を一つの `Signal<DockTree>` で管理し、layout を保存・復元・reset する Editor shell を作る場合。",
                "tab の stateful view を layout rebuild や tab 切替の間キャッシュし、同じ instance を再利用したい場合。",
                "同一 host 内で split、group 間 tab move、中央への tab 追加、edge docking、floating pane の移動／resize を提供する場合。",
            ],
            avoidWhen:
            [
                "二つの固定領域の比率変更だけで足りる場合。`Splitter` の方が model と操作が単純です。",
                "OS の独立 window、複数 monitor、native title bar、window manager への tear-off が必要な場合。DockHost の float は host 内 overlay です。",
                "tab、splitter、dock target を完全にキーボード操作し、支援技術へ window/tab semantics を公開する必要がある場合。",
            ],
            alternatives:
            [
                new("Splitter", "固定された隣接二領域の境界だけを pointer で変更します。"),
                new("DocumentTabs", "一つの tab strip の activation／close／reorder だけを扱い、split や float を持たせない場合。"),
                new("Drawer", "作業領域を組み替えず、一時的な補助 pane を重ねる場合。"),
            ],
            usage: """
                DockHost host = EditorKit.DockHost(
                    session.Layout,
                    session.ResolveDockItem,
                    closeRemoves: false,
                    onCloseTab: (_, id) => session.CloseTab(id));
                """,
            anatomy: "`DockTree.Root` を再帰的に `DockSplitPanel` または group へ変換します。各 group は `DocumentTabs`、active view、全面の `DockDropZone` から成り、split 間には `Splitter` を置きます。`DockTree.Floats` は `DockFillPanel` の最前面に `DockFloatPanel` として重ね、14 px の grab bar と右下 resize corner を持ちます。",
            variants: "`closeRemoves` は close event 後に tree から自動除去するか、shell の dirty-confirmation に委ねるかを選びます。`hideSingleTabStrip` は単一 tab group の strip を隠し、`showTabClose`、`tabStripHeight`、`tabActiveBackground` で chrome を調整します。root は horizontal／vertical split、group は任意 tab 数、float は複数持てます。",
            state: "layout、active index、tab 順、split fraction、float rectangle の正本は呼び出し側所有の `Signal<DockTree>` です。DockHost は各 id の `DockItem.CreateView()` を初回だけ呼んで `_views` に cache し、`ResolveItem` は build ごとに title／dirty signal を再取得します。永続化は DockHost にはなく、`EditorSession.LayoutService` は検証済み tree を `editor.layout.v1` として settings store へ保存し、壊れた layout は default へ戻します。",
            pointer: "tab 本体は release まで drag が 4 px 以下なら activate、4 px を超えると drag payload になります。tab strip へ drop すると insertion index へ移動し、content 中央は対象 group へ追加、左右上下の edge zone（各辺 25%、最大 96 px）は split を作ります。splitter は drag end で隣接 fraction を更新し各 pane を最低 5% に保ちます。float は grab bar で移動し、右下 corner で最低 120×80 px まで resize します。",
            keyboard:
            [
                new("—", "DockHost、DocumentTabs、splitter、drop zone、floating chrome は専用の focus target／keyboard docking binding を登録しません。"),
                new("登録済み Editor command", "layout reset、pane visibility、focus mode などは DockHost ではなく EditorSession の menu／keymap から実行します。"),
            ],
            focus: "tab click、drag、splitter、float grab／resize は keyboard focus を移しません。active view 内の control が自身の focus を所有します。close glyph は `OnCloseTab(id)` を通知し、`closeRemoves:false` では dialog／dirty confirmation と実際の tree 更新を shell が完了するまで view を残します。DockHost 自体に dismissal はありません。",
            accessibility: new ControlDocsAccessibility(
                "各 `DockItem.Title` を一意で安定した pane／document 名にし、同名 tab が必要なら追加文脈を付けます。",
                "tab strip、splitter、dock preview、floating chrome は custom-drawn で、tablist／separator／window semantics を公開しません。active child view の semantics だけが利用できます。",
                "active tab は `Primary` の underline／任意の背景、dirty は ●、drop target は半透明 highlight で示します。色だけでなく title、dirty confirmation、menu command でも状態を確認できるようにします。",
                "tab text、dirty／close glyph、splitter 境界、drop highlight、float frame が editor theme 上で識別できるか確認します。",
                "drag 中の ghost／highlight は補助表示です。layout reset と pane menu を非ドラッグ経路として提供します。",
                "完全な keyboard docking、focus order、tablist/window semantics、screen-reader 向け drop target 説明、native window accessibility は未実装です。"),
            theme: new ControlDocsThemeLayout(
                "`Surface`、`SurfaceAlt`、`BorderColor`、`Primary`、`Text`、`TextMuted` で tab、frame、drop zone、resize ghost を描きます。child view の theme は同じ `UiBuildContext` から継承します。",
                "親制約いっぱいに root を広げ、float を高 Z／hit layer で dock content の前へ重ねます。float は host bounds 内へ表示 clamp され、OS window にはなりません。",
                "有限制約がない軸は 640×400 px を既定にします。float は host より大きければ表示時に縮み、drag resize は 120×80 px を下限にします。実用 Editor では Star 行など明示的な有限 work-area を与えます。"),
            constraints: new ControlDocsConstraints(
                "tree の正当性、重複 id、保存 schema migration、未知 pane id、最小 pane policy は呼び出し側の責任です。大量 pane では tree 変更ごとに構造を rebuild し、`resolve` を各 tab について再実行します。float は host 内だけで、multi-monitor、native maximize/minimize、front activation、tear-off を持ちません。",
                "tree に残る view は cache され、tree から id が消えると `IDisposable` なら dispose して cache から除去します。view factory は同じ id に安定した種類を返し、resource-heavy view は自身の disposal を実装します。DockHost を EditorShell から使う場合、session が layout service と close coordination を所有します。",
                "Retained UI の pointer／drag/drop host で動作します。Browser と Native で同じ in-window docking を使えますが、OS window integration や platform drag payload との相互運用は提供しません。"),
            apiHighlights: "`tree`、`resolve`、`closeRemoves`、`hideSingleTabStrip`、`showTabClose`、`tabStripHeight`、`tabActiveBackground`。検査 API は `ViewOf(id)` と `TabCenter(id)` です。`DockItem` は `Title`、一度だけ呼ばれる `CreateView`、任意の `Dirty` signal を持ちます。",
            eventContracts: "`OnCloseTab(id)` は close 要求です。`closeRemoves:true` では通知後に `DockTree.RemoveTab`、`false` では caller が dirty confirmation 等を終えて tree を更新します。activation、reorder、dock、split resize、float move/resize は event ではなく `tree.Value` の新しい `DockTree` として反映されます。",
            related:
            [
                new("Controls/Editor/DockHost/Examples/Floating", "floating と再 docking", "host 内 float の移動、group への tab drop、dock への戻しを実行します。", StoryKind.Example),
                new("Examples/Workbench/Shell", "portable EditorShell", "EditorSession の document を production shell と DockHost に載せる通し例です。", StoryKind.Example),
                new("Examples/Workbench/Files", "Workbench: files", "AssetBrowser、DocumentStore、Toolbar、DockHost の保存フローです。", StoryKind.Example),
                new("Examples/Workbench/Inspector", "Workbench: inspector document", "PropertyGrid document の dirty／save／undo を DockHost で扱います。", StoryKind.Example),
                new("Examples/Workbench/Material", "Workbench: material", "異なる document kind を同じ DockHost へ構成だけで追加します。", StoryKind.Example),
            ]),

        DetailedPage(
            type: "EditorShell",
            category: "Editor",
            summary: "一つの `EditorSession` を MenuBar、Toolbar、global DocumentTabs、DockHost、dialogs、StatusBar に投影し、command shortcut、autosave pump、theme／UI scale 同期までを同じ realization scope へ結線する portable Editor chrome です。単なる見た目ではなく、session が既に構成済みであることを前提とする application shell です。",
            useWhen:
            [
                "Browser、Native、Gallery／test fixture で同じ `EditorSession` と production pane を共通 chrome に載せる場合。",
                "command registry、workspace active document、dock layout、settings、autosave、close coordination を session に集約し、shell を薄い投影に保つ場合。",
                "保存・undo/redo・pane visibility・focus mode・dialog を core command と production service の実動作で確認する場合。",
            ],
            avoidWhen:
            [
                "単一の pane／document view だけを埋め込みたい場合。必要な control を直接使う方が session と service の負担が小さくなります。",
                "独自の document model、command router、window manager を持ち、`EditorSession` の ownership contract を採用しない application。",
                "shell 自身が session を自動生成・自動 dispose すると期待する場合。直接生成した EditorShell は渡された session を所有しません。",
            ],
            alternatives:
            [
                new("DockHost", "既にある `DockTree` と pane だけを表示し、menu、toolbar、autosave、dialogs が不要な場合。"),
                new("MenuBar", "command hierarchy だけを独立した application chrome に載せる場合。"),
                new("Toolbar", "少数の高頻度 command だけを常時表示する場合。"),
            ],
            usage: """
                var session = new EditorSession(files, documents, initialLayout);
                EditorShell shell = EditorKit.EditorShell(session,
                    productName: "Luxel Editor");
                shell.HAlign.SetBase(Align.Stretch);
                shell.VAlign.SetBase(Align.Stretch);
                // The application composition root must dispose session.
                """,
            anatomy: "5 行 Grid で、30 px の `MenuBar`、34 px の toolbar chrome、`DocumentTabs.StripH` の `EditorDocumentTabs`、Star の `DockHost` と `EditorDialogs`、`StatusBar.BarH` の `EditorStatusBar` を重ねずに配置します。`DocumentsHost` は `session.Layout` と `session.ResolveDockItem` を使い、close は `session.CloseTab` へ返します。公開 `DocumentTabs`／`DocumentsHost` は Gallery play と integration test から実体を検査するための参照です。",
            variants: "表示内容は session に登録した document／standard pane、active document の `CommandContribution`、host capability、settings、dock layout で変わります。Gallery／test では `EditorTestFixture` が session を `UiBuildContext` に own して同じ shell を起動できます。`productName` は現在 public parameter として受け取りますが、shell chrome にはまだ描画されません。",
            state: "`EditorSession` が `Workspace`、`DocumentStore`、`CommandRegistry`、`Signal<DockTree>`、selection／diagnostics／output service、settings／keymap、asset operations、layout persistence、close coordinator、autosave scheduler、document dirty／undo を所有します。EditorShell は toolbar 参照、最初の system theme、生成した child surface だけを保持し、状態の正本を複製しません。session の layout service は settings store から restore し、focus mode 中の一時 layout は永続化しません。",
            pointer: "MenuBar root／menu item、Toolbar button、DocumentTabs、DockHost、各 pane、dialog、status 内 control の pointer 契約を合成します。tab close は session の close coordinator へ進み、dirty document を即時破棄しません。shell 自身は全画面 hit target を追加せず、各 child surface が操作を所有します。",
            keyboard:
            [
                new("Ctrl+S / Ctrl+Shift+S", "既定の Save／Save As command。keymap override があれば実効 gesture に置き換わります。"),
                new("Ctrl+W", "active document の close 要求を `EditorCloseCoordinator` へ送ります。"),
                new("Ctrl+Z / Ctrl+Y", "`Workspace` の active document へ Undo／Redo を委譲します。"),
                new("Ctrl+Shift+F", "active pane の focus mode を toggle し、解除時に以前の DockTree を復元します。"),
                new("その他の registry gesture", "`session.Commands` に登録された実効 gesture は shell realization 中だけ `UiHost` global shortcut として binding されます。"),
            ],
            focus: "EditorShell 自身は focus target を持ちません。focus は active editor、field、select、dialog など child が所有し、global shortcut は focus 中 control が key を消費しなかった場合だけ実行されます。MenuBar／Toolbar／DocumentTabs／DockHost chrome の多くは pointer-only です。modal dialog は overlay scope の focus／`Escape` dismissal を使います。active document の contribution は MenuBar／Toolbar へ合成されますが、contribution-only gesture は `BindShortcuts(host)` の対象外なので、必要なら registry にも登録します。",
            accessibility: new ControlDocsAccessibility(
                "product 名だけに依存せず、document title、pane title、command title、dialog heading、status text を具体的に設定します。",
                "shell 自身の application／menubar／tablist semantics はありません。各 child が公開する semantics の集合として振る舞い、custom MenuBar／Toolbar／DocumentTabs／DockHost chrome には既知の穴があります。",
                "dirty、active document、disabled command、diagnostic、autosave／storage status は tab glyph、muted text、status／dialog に反映します。重要な状態を色や pane 配置だけで伝えないでください。",
                "session theme と UI scale を host theme へ適用するため、全 surface で text、focus ring、selection、dialog scrim、status のコントラストを確認します。",
                "autosave pump は視覚 animation を起こしません。drag docking や editor animation には menu command、reset、停止など別経路を用意します。",
                "shell 全体の landmark、keyboard-only menu/tab/docking、focus restoration の一貫性は未完成です。実製品では command palette／key binding view、明示的な pane menu、host-level accessibility 検証を追加します。"),
            theme: new ControlDocsThemeLayout(
                "realize 時の host theme を system theme として保持し、`EditorSettings.ResolveTheme` で Light／Dark／System と UI scale を解決して `ctx.Theme` へ反映します。settings 変更時は shell を再実体化します。",
                "menu、toolbar、global document tabs、document/dock/dialog row、status の固定 5 行です。work area は `DockHost` が fill するため、shell 自身を親で `Stretch` させます。floating pane はこの work area 内に留まります。",
                "menu 30 px、toolbar 34 px、document strip 32 px、status は `StatusBar.BarH`、中央だけ Star です。小さい window では pane 最小幅や toolbar overflow を自動解決しないため、host 側に実用最小 window size を設定します。"),
            constraints: new ControlDocsConstraints(
                "有効な `EditorSession` と、その document/provider/storage/settings/host service が必要です。`productName` は現在表示に使われません。toolbar は wrap／overflow せず、MenuBar と DockHost は完全な keyboard navigation を持ちません。host が無い realization では command shortcut を binding できません。",
                "direct な EditorShell は session を dispose しません。application composition root が `EditorSession.Dispose()` を呼び、Gallery／test は `EditorTestFixture` で `ctx.Own(session)` できます。shell scope は shortcut binding、reactive effect、autosave animation を own し、autosave enabled 中は毎 UI tick `PumpAutosave(max(0, dt))` を呼びます。document view の resource は session／DockHost の close と disposal contract に従います。",
                "Browser／Native／Gallery で同じ shell を使えますが、persistent storage、project picker、native dialog、file watching、process build、reveal/import は `IHostCapabilities` と host service の実装次第です。host 内 floating は native multi-window ではありません。"),
            apiHighlights: "固有 parameter は `session` と `productName`。公開検査点は `DocumentTabs` と `DocumentsHost` です。production composition では `EditorSession` の `Commands`、`Layout`、`Workspace`、`Settings`、`Documents`、`Assets`、`CloseCoordinator`、service signal を利用します。",
            eventContracts: "EditorShell 固有の `UiEvent` はありません。command は `session.Commands`、document/pane close は `session.CloseTab`／`CloseCoordinator`、application close は `CloseProjectRequested`／`ExitRequested`、document change は各 `IEditorDocument` と `Workspace`、status/error は session service を通じて通知・更新します。",
            related:
            [
                new("Examples/Workbench/Shell", "portable EditorShell の実動作", "4 種の document、tab 切替、dirty、Ctrl+S、NodeGraph を production shell で実行します。", StoryKind.Example),
            ]),

        DetailedPage(
            type: "MenuBar",
            category: "Overlay",
            summary: "`CommandRegistry` の menu path と active document の contribution を root menu と dropdown row に投影する application-level command surface です。command の定義・enablement・gesture・実行を複製せず、registry を単一の真実として表示します。",
            useWhen:
            [
                "File／Edit／View／Window のような安定した command hierarchy を window 上端へ常時表示する場合。",
                "active document 固有の menu chapter を `CommandContribution.MenuPath` で追加・削除したい場合。",
                "command title、disabled state、登録時 gesture の表示を一つの registry から生成する場合。",
            ],
            avoidWhen:
            [
                "少数の高頻度 action だけを常時見せる場合。Toolbar の方が短い操作になります。",
                "検索中心の command discovery、完全な keyboard menu navigation、入れ子 fly-out が必要な場合。CommandPalette または専用 menu 実装を使います。",
                "command の非同期進捗、error presentation、permission confirmation を MenuBar 自身に所有させたい場合。",
            ],
            alternatives:
            [
                new("Toolbar", "高頻度の少数 command を一列の button として常時表示します。"),
                new("CommandPalette", "title／id の検索、↑↓ 選択、Enter 実行を提供する command discovery 面です。"),
            ],
            usage: """
                MenuBar menu = EditorKit.MenuBar(
                    session.Commands,
                    contributions: () =>
                        session.Workspace.Active.Value?.Contributions ?? []);
                """,
            anatomy: "高さ 30 px の `Surface` bar に、`BuildMenu()` で得た root `MenuNode` を `LinkText` として横並びにします。root click で 200～380 px 幅の `ContextMenu` を開き、command row は左に title、右に gesture を描きます。深い path は fly-out ではなく divider と muted group heading を挟んで同じ列へ inline 展開します。",
            variants: "registry の登録 menu、`Order`／登録順、active contribution、command enablement／登録時 `Gesture` で内容が変わります。registry `Version` の変更は自動 rebuild し、contribution provider が build 中に読む signal も追跡されます。同じ path の末端 command は後勝ちです。",
            state: "command identity、title、run action、enablement、gesture override、menu path は `CommandRegistry` と `CommandContribution` が所有します。MenuBar は直近 build の `MenuNode` と root label 参照、realize context だけを保持します。表示列は `Command.Gesture` を使い、`EffectiveGesture` の override を直接読みません。open state と overlay lifecycle は `ContextMenu`／`UiHost` が所有し、MenuBar は command 実行後に menu を閉じます。永続 keymap は registry を構成する EditorKeymap 側の責任です。",
            pointer: "root label の click で dropdown を開き、別 root を click すると既存 overlay を閉じて開き直します。enabled row は hover と click を受け、click で overlay を閉じて `Command.Run()` を同期実行します。disabled row は cursor と click action を持ちません。外側 click で overlay を閉じます。",
            keyboard:
            [
                new("Escape", "dropdown overlay が開いているとき `UiHost` の overlay policy が閉じます。"),
                new("登録済み gesture", "MenuBar 自身は処理しません。`CommandRegistry.BindShortcuts(UiHost)` または EditorShell の binding が必要です。"),
                new("—", "root label／menu row は focus target を登録せず、矢印、Enter、Alt mnemonic による menu navigation はありません。"),
            ],
            focus: "menu を開いても focus を menu へ移動・拘束せず、以前の editor focus を保持します。起動は pointer の root click、実行は pointer row click です。item click、外側 click、`Escape` で閉じます。keyboard-only 利用者には CommandPalette または registry shortcut を別に提供します。",
            accessibility: new ControlDocsAccessibility(
                "root と command title は短く一意にし、gesture の文字だけで command の意味を表さないでください。",
                "MenuBar／root／row は custom `LinkText`／Widget で、menubar、menu、menuitem、disabled の semantic role を公開しません。",
                "disabled は muted 色、hover は `SurfaceAlt`、gesture は右列で示します。checked menu item、radio state、submenu expanded state はありません。",
                "bar の border、root text、dropdown frame、disabled text、hover surface が利用 theme で判別できるか確認します。",
                "hover fade 以外の必須 motion はなく、open/close animation に情報を依存しません。",
                "keyboard menu navigation、mnemonic、focus transfer、screen-reader menu semantics、深い fly-out は未実装です。重要 command は shortcut、Toolbar、CommandPalette でも到達可能にします。"),
            theme: new ControlDocsThemeLayout(
                "bar と dropdown は `Surface`、hairline／frame は `BorderColor`、hover は `SurfaceAlt`、enabled／disabled text は `Text`／`TextMuted` を使います。",
                "bar は app/window の上端で横方向に stretch させます。dropdown は root の左端近く、bar 下へ anchor され、viewport margin 4 px の overlay として配置されます。",
                "bar 高は 30 px。dropdown は内容の最大 intrinsic width + 8 px を 200～380 px に clamp し、既定 max height は 600 px です。長い menu は専用 scrolling を持たないため項目数を抑えます。"),
            constraints: new ControlDocsConstraints(
                "深い hierarchy は inline group で、fly-out、check/radio item、icon、separator contribution、scrolling はありません。open 後の row は enablement 変化へ live 更新せず、次回 open／rebuild で再評価します。keymap override は `EffectiveGesture` にだけ反映され、MenuBar の表示は元の `Command.Gesture` のままなので、現在の実装では表示と実効 binding が異なることがあります。command action は同期 `Action` で、exception、async progress、confirmation は呼び出し側が処理します。",
                "registry と contribution provider は session と同じ寿命にします。MenuBar の overlay は `UiBuildContext` ごとに同時に一つで、scope 解放時に撤去されます。registry registration／keymap persistence は MenuBar の責任外です。",
                "Retained UI overlay を持つ Browser／Native host で利用できます。OS native menu bar、platform mnemonic、global application menu への export は行いません。"),
            apiHighlights: "`registry` と `contributions`。`RootLabel(label)` と `OpenRoot(MenuNode)` は Gallery／test の検査用です。menu 内容は `CommandRegistry.BuildMenu`、表示 gesture は command の `Gesture`、実行可否は `Command.IsEnabled` から得ます。",
            eventContracts: "MenuBar 固有の `UiEvent` はありません。選択された enabled row が `Command.Run` を呼びます。model 更新、Undo、save、dialog、error reporting は command action と EditorSession service が行い、registry `Version` が構造変更を surface へ通知します。",
            related:
            [
                new("Controls/Editor/CommandPalette/Basic", "CommandPalette", "検索、↑↓、Enter、Escape を持つ keyboard-oriented な command discovery を確認します。", StoryKind.Basic),
                new("Examples/Workbench/Shell", "EditorShell 内の MenuBar", "active document contribution と core command を portable shell 上で確認します。", StoryKind.Example),
            ]),

        DetailedPage(
            type: "SceneEditorView",
            category: "Editor",
            summary: "不変 `SceneDoc` と `SceneTransaction` を、2D／3D の `ISceneSpaceAdapter` 経由で選択・marquee・移動・複製・削除・undo/redo・camera 操作・2D tile painting へ結ぶ focusable Editor viewport です。保存や runtime world を直接所有せず、編集 transaction と描画／入力の橋渡しに限定します。",
            useWhen:
            [
                "`SceneDocument` と接続し、scene entity selection／transform edit／tile edit を同じ undo history へ記録する場合。",
                "`SceneSpace2DAdapter` または `SceneSpace3DAdapter`、あるいは独自 adapter に座標変換、hit test、camera、描画を委譲する場合。",
                "Hierarchy／SceneInspector と `OnSelectionChanged`／`Revision`／`ApplyEdit` を介して同じ scene state を共有する場合。",
            ],
            avoidWhen:
            [
                "完全な runtime renderer、physics、play-mode world、asset streaming、GPU scene viewport をそのまま Editor に埋め込む場合。",
                "screen reader だけで scene spatial edit を完結させる必要がある場合。Hierarchy と schema-based Inspector を主要な非空間経路にします。",
                "source signal の差し替えへ自動追従する stateless preview が必要な場合。`source` は初期化時に読み、以後は `Load` を明示します。",
            ],
            alternatives:
            [
                new("SceneInspector", "現在の主選択 entity の component field を非空間の form として編集します。"),
                new("Canvas2D", "scene transaction、selection、adapter、history を持たない独自の 2D 描画／入力面を作ります。"),
                new("GpuView", "独自 GPU renderer が所有する framebuffer／command を専用 viewport に表示します。"),
            ],
            usage: """
                SceneEditorView editor = EditorKit.SceneEditorView(
                    source: scene,
                    viewWidth: 620,
                    viewHeight: 360);
                editor.SnapToGrid = true;
                editor.OnEdit = view => document.Replace(view.Scene.Doc);
                editor.OnSelectionChanged = view => selection.Set(
                    view.Scene.Selection.Entities,
                    view.Scene.Selection.Main);
                """,
            anatomy: "一つの focusable root に background と focus ring、clip された world layer、screen-space overlay、marquee overlay を作ります。adapter の `Attach` が grid、entity、name、selection、move handle などの retained node を構築し、`Refresh` が `SceneEditState` と theme から描き直します。SceneEditorView は pointer／keyboard を transaction に変換し、adapter は screen↔world、hit、camera、描画を所有します。",
            variants: "`SceneSpace.TwoD` は 2D grid／entity placeholder／XY handle／pan・zoomを、`SceneSpace.ThreeD` は OrbitCamera で地面 grid、transform3d AABB、XYZ handle を 2D canvas へ投影し middle drag を orbit、wheel を dolly として扱います。`Adapter` を明示すれば独自空間へ置換できます。`Tool` は Select／Brush／Rect／Eraser／Picker、`SnapToGrid`、`ActiveTile`、`ActiveLayer` を持ち、tile tool は `ISceneTileAdapter` 実装時だけ動作します。",
            state: "SceneEditorView は確定 `SceneEditState`、`SceneHistory`、selection、adapter／camera、tool、tile selection、drag preview を所有します。document change は transaction として history に積み、selection change は history／dirty に積みません。`Revision` は確定 document／selection change で増え、drag preview では増えません。保存済み snapshot、dirty、file binding は `SceneDocument`／EditorSession が `OnEdit` と `Serialize` で所有します。`source` は初回だけ読み、`Load(doc)` は selection、history、preview、adapter を reset します。",
            pointer: "left press は handle を最優先し、entity hit は未選択なら単一選択して move、既選択なら group move を開始します。`Ctrl`+entity click は selection toggle のみで move しません。空白 drag は selection を解除して marquee、middle drag は 2D pan／3D orbit、wheel は cursor 周辺 zoom／3D dolly です。move は drag 中 preview、release で一つの transaction として確定し、snap は release 時だけ適用します。Brush／Eraser は補間した 1 stroke、Rect は範囲塗り、Picker は click cell を `ActiveTile` へ読みます。",
            keyboard:
            [
                new("Ctrl+Z / Ctrl+Y", "viewport 内の `SceneHistory` を Undo／Redo します。"),
                new("Ctrl+A", "scene の全 entity を選択します。"),
                new("Ctrl+D", "現在 selection を adapter の offset policy で複製し、一つの transaction にします。"),
                new("Delete / Backspace", "現在 selection の entity を削除します。"),
                new("Escape", "document を変更せず selection を解除します。"),
            ],
            focus: "viewport root を pointer で押すと focus target を取得し focus ring を表示します。上記 shortcut は viewport が focus を持つ場合だけ処理します。SceneInspector の field を操作した後に scene shortcut を使うには viewport、Hierarchy、または shell command へ focus／routing を戻します。SceneEditorView に activation／dismissal はありません。",
            accessibility: new ControlDocsAccessibility(
                "viewport の周囲に scene 名、空間（2D／3D）、active tool、active layer、selection 件数を文字で示します。",
                "SceneEditorView は semantic image／application／list を公開せず、entity、handle、grid、marquee は canvas 描画だけです。",
                "selection、main entity、tool、snap、tile、undo可否を viewport だけに閉じず、Hierarchy、Inspector、Toolbar、StatusBar に同期します。",
                "background、grid、entity、selection outline、XYZ handle、marquee を light／dark theme と scene color の双方で識別できるか確認します。",
                "pan／zoom／orbit／drag preview は直接操作で、必須 animation はありません。camera motion が負担になる場合は ResetView と数値編集経路を提供します。",
                "spatial hit target と canvas 内容には screen-reader semantics がありません。完全な keyboard transform、nudge、tool switching、camera reset key、validation announcement も未実装です。Hierarchy／SceneInspector を同等の編集経路として提供します。"),
            theme: new ControlDocsThemeLayout(
                "root は theme `Background` と focus ring を使い、adapter は theme text／selection と軸色を組み合わせます。theme 変更で全 scene overlay を `Refresh` します。",
                "world layer は viewport 内で clip し、handle と marquee は screen-space overlay に描きます。DockHost 内では active view として有限領域を与え、Hierarchy／Inspector と並べる場合も viewport を主領域にします。",
                "`viewWidth`／`viewHeight` は既定 480×360、最小 160×120 に補正され、親 constraints でさらに制約されます。3D camera aspect は realize 時の view size から決まります。"),
            constraints: new ControlDocsConstraints(
                "各 refresh は adapter が grid／entity／handle scene を再構築します。entity 数、tile stroke cell 数、3D projected AABB が増えるほど CPU geometry／hit test が増え、virtualization はありません。pointer move 中は preview state を生成します。tile tool は 2D adapter のみで、`Update` 中の runtime scene、physics、multi-user conflict、asset renderer は扱いません。",
                "SceneEditorView と adapter の retained node は UI realization scope に従います。外部 `SceneDocument` は view の `OnEdit` を chain して dirty／saved snapshot を管理し、document／session close で関連 resource を解放します。`ApplyEdit` を外部 editor の唯一の mutation 経路にし、state を直接 mutate しないでください。",
                "Retained 2D canvas、pointer drag、scroll、keyboard focus を持つ Browser／Native UI host で動作します。built-in 3D adapter は OrbitCamera と AABB を 2D canvas へ投影する Editor representation で、native GPU runtime renderer ではありません。"),
            apiHighlights: "`source`、`viewWidth`、`viewHeight`。runtime property は `Adapter`、`SnapToGrid`、`Tool`、`ActiveTile`、`ActiveLayer`、`Scene`、`Revision`、selection／undo 状態。操作 API は `SelectEntity(ies)`、`ApplyEdit`、`Load`、`Undo`、`Redo`、`Pan`、`ResetView` と検査用座標 helper です。",
            eventContracts: "`OnEdit(SceneEditorView)` は document が変わる `Apply` と undo／redo で通知し、selection-only change では通知しません。`OnSelectionChanged(SceneEditorView)` は `Apply`／公開 selection API／undo／redo で main／entity 列が変わったとき通知しますが、現在の pointer click、Ctrl+click、空白解除、marquee の直接 selection path は `Revision` だけを進めて callback を呼びません。厳密な同期が必要な consumer はこの既知制限を考慮して `Revision` と `Scene.Selection` も監視します。save、dirty、selection service への反映は caller／`SceneDocument` が行います。",
            related:
            [
                new("Controls/Editor/SceneEditorView/Tiles", "2D tile tools", "Brush、Rect、Eraser、Picker と 1 stroke = 1 undo を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/SceneEditorView/ThreeD", "3D adapter", "OrbitCamera、projected AABB、3 軸 handle、orbit／dolly を確認します。", StoryKind.Unspecified),
                new("Controls/Editor/SceneEditorView/Inspector", "SceneInspector 連携", "schema field edit、component add/remove、undo の runnable context です。", StoryKind.Unspecified),
                new("Controls/Editor/SceneEditorView/Assets", "scene asset workflow", "AssetBrowser と atlas definition editor を scene workflow に組み込みます。", StoryKind.Unspecified),
                new("Examples/Apps/Studio/CoinGame", "Studio dogfood: CoinGame", "tile 描画、entity 追加、Inspector、保存、保存物からの play を通します。", StoryKind.Example),
                new("Examples/Apps/Studio/Mixed3D", "Studio dogfood: Mixed3D", "3D scene edit、Inspector、保存、2D→3D runtime 遷移を確認します。", StoryKind.Example),
            ]),

        DetailedPage(
            type: "SceneInspector",
            category: "Editor",
            summary: "接続した `SceneEditorView` の主選択 entity を `SchemaRegistry` で解釈し、component section、typed field editor、component add/remove を生成する schema-driven Inspector です。reflection で任意 CLR object を編集する PropertyGrid と異なり、すべての変更を `SceneEditorView.ApplyEdit` の scene transaction へ戻します。",
            useWhen:
            [
                "SceneEditorView／SceneDocument の主選択 entity を、2D／3D space に対応した component schema で編集する場合。",
                "field edit、component add/remove を scene history に積み、viewport の Ctrl+Z／shell Undo で戻せるようにする場合。",
                "game 固有 component を `IComponentSchema` 登録だけで Inspector に追加し、未知 field を失わず読み取り表示する場合。",
            ],
            avoidWhen:
            [
                "任意 CLR object の public property を reflection で編集する場合。`PropertyGrid` を使います。",
                "複数選択の mixed value、一括編集、validation message、property search、折りたたみ section が必要な場合。",
                "SceneEditorView なしで独立した form model を編集する場合。Inspector の state と undo owner は接続 editor です。",
            ],
            alternatives:
            [
                new("PropertyGrid", "一般 CLR object を reflection と attribute で編集します。"),
                new("SceneEditorView", "selection、spatial transform、tile edit、scene history の正本を所有する viewport です。"),
            ],
            usage: """
                SceneEditorView editor = EditorKit.SceneEditorView(scene);
                SchemaRegistry schemas = SceneSchemas.BuiltIns()
                    .Add(gameplaySchema);
                SceneInspector inspector = EditorKit.SceneInspector(
                    editor: editor,
                    schemas: schemas,
                    width: 280);
                """,
            anatomy: "未接続なら『エディタ未接続』、主選択が無ければ『選択なし』を表示します。選択時は entity name／id、component ごとの divider・display name・remove button、field label と型別 editor、最後に現在 space へ追加可能で未装着の schema を選ぶ `Select` と追加 button を並べます。値列は 150 px、label 列は残り幅です。",
            variants: "Bool=`Check`、Enum=`Select`、Color=`ColorPicker`、Vec2／Vec3=軸別 `TextField`、Quat=Euler degree の3軸表示、Int／Float／String／AssetRef=`TextField` です。`schemas` 省略時は `SceneSchemas.BuiltIns()`、追加候補は `SchemaRegistry.For(scene.Space)` で絞ります。schema に無い component／field は削除せず `SceneValue.ToString()` の読み取り文字として保全します。",
            state: "SceneInspector は `SceneEditorView.Revision` を購読して selection／document の確定変更ごとに再構築します。scene、main selection、component value、history、dirty は editor／SceneDocument が正本です。Inspector が保持するのは field editor 参照、component add choice、各 build で作る一時 signal だけです。各値変更は `SetField`、追加は `SetComponent`、削除は `RemoveComponent` として `ApplyEdit` へ渡り、一つの commit ごとに undo transaction になります。Inspector 自身に永続 draft はありません。",
            pointer: "Check、Select、ColorPicker、各 TextField、component remove `×`、component add button を直接操作します。Bool／enum／color は signal change ですぐ commit し、text／axis field も受理された文字列変更ごとに commit します。component add は schema default、remove は現在 component type を transaction へ渡します。unknown field は pointer edit できません。",
            keyboard:
            [
                new("TextField: ← / → / Home / End / Backspace / Delete / Ctrl+A/C/X/V", "Int、Float、String、AssetRef、Vec2／Vec3／Euler field の 1 行編集です。変更は各 keystroke で transaction になり得ます。"),
                new("Select: Enter / Space", "component／enum selector の overlay を開閉します。"),
                new("Select: ↑ / ↓", "現在の option index を直ちに変更し、enum field では commit します。"),
                new("ColorPicker: Enter / Space", "color overlay を開閉します。内部 hex field と RGB slider は各 control の操作に従います。"),
                new("Ctrl+Z / Ctrl+Y", "SceneInspector の専用 binding ではありません。viewport または EditorSession command routing に戻して scene history を Undo／Redo します。"),
            ],
            focus: "SceneInspector container、label、Check、add/remove `Button` は focus target を持ちません。TextField、Select、ColorPicker は focus を取得しますが、panel-level の Tab／arrow navigation や label から editor への focus association はありません。overlay は外側 click／`Escape` で閉じます。scene shortcut を使うときは viewport または shell command へ focus を戻します。",
            accessibility: new ControlDocsAccessibility(
                "entity name、component display name、field name、単位や asset path の期待形式を visible label と補足 text で示します。",
                "Inspector 全体の form／group／property semantics はなく、TextField／Select／ColorPicker 等の child が持つ semantics に依存します。label と editor の programmatic association はありません。",
                "選択なし、未知 field の読み取り、component の有無、enum／bool／color value は文字または child state で見えるようにします。validation error／mixed value／read-only reason の専用 state はありません。",
                "label／value 列、divider、muted component heading、focus ring、error を追加する場合の色を light／dark theme で確認します。color は hex 文字も併記します。",
                "必須 animation はありません。ColorPicker overlay や slider の変更だけに値を依存させず、文字値と undo を提供します。",
                "複数選択、mixed value、validation announcement、field description、unit semantics、label association、panel-level keyboard navigation は未実装です。schema 外 field は読み取りだけで、unknown component も remove button 自体は表示されます。"),
            theme: new ControlDocsThemeLayout(
                "text、muted heading、divider、control、focus ring、ColorPicker は共通 `UiTheme` を使い、viewport／Hierarchy／他 property pane と同じ surface 上に置きます。",
                "一列の `VStack` に component section を積み、各 field は label と 150 px editor の `HStack` です。固定 width の dock pane または `ScrollViewer` で包む composition を推奨します。SceneInspector 自身は scrolling を持ちません。",
                "既定 width は 260 px、value control は 150 px、label は最低 50 px です。200 px 未満では label と editor が詰まりやすく、field 数が多い場合は親側で縦 scrolling と十分な高さを与えます。"),
            constraints: new ControlDocsConstraints(
                "主選択一件だけを編集し、multi-edit、search、category collapse、validation summary、transaction batching はありません。text field は変更ごとに commit し、Float の途中入力が parse できないと helper が 0 として commit する可能性があるため、production schema editor では commit-on-confirm／validation layer の追加を検討します。Quat は Euler degree 表示のため round-trip 表現差が起こり得ます。",
                "`SceneEditorView` と `SchemaRegistry` は呼び出し側が所有します。Inspector は editor `Revision` で再構築されるため、外部で field state を保持しないでください。すべての mutation は `ApplyEdit` を通し、scene document／session close と同じ寿命で Inspector を破棄します。",
                "Retained UI input／overlay を持つ Browser／Native host で利用できます。reflection や platform native inspector は不要ですが、IME／clipboard／color overlay の能力は child control と platform service に従います。"),
            apiHighlights: "`editor`、`schemas`、`width`。公開 helper は `EditorOf(component, field)`、`AddComponent(type)`、`RemoveComponent(type)` です。schema API は `SchemaRegistry`、`IComponentSchema`、`SceneFieldDef`、`SceneFieldType`、space mask、default `SceneValue` を使います。",
            eventContracts: "SceneInspector 固有の `UiEvent` はありません。field／component 操作は `SceneEditorView.ApplyEdit` を通じて `Revision`、`OnEdit`、history、dirty へ伝播します。selection change は editor／Hierarchy が所有し、Inspector は `Revision` を読んで表示を追従します。",
            related:
            [
                new("Controls/Editor/SceneEditorView/Inspector", "schema Inspector の runnable context", "custom schema、field edit、component add/remove、viewport Ctrl+Z を一続きで確認します。", StoryKind.Unspecified),
                new("Examples/Apps/Studio/CoinGame", "Studio dogfood: CoinGame", "2D scene authoring で selection と behaviour/tint component を確認します。", StoryKind.Example),
                new("Examples/Apps/Studio/Mixed3D", "Studio dogfood: Mixed3D", "3D transform／mesh／camera schema を Inspector と保存フローで確認します。", StoryKind.Example),
            ]),

        DetailedPage(
            type: "Toolbar",
            category: "Overlay",
            summary: "`CommandRegistry.ToolbarCommands()` と active document contribution を、enabled command は ghost `Button`、disabled command は muted text として順番どおり横に投影する軽量 command surface です。command の state や実行処理は所有せず、頻繁な少数操作を常時見せる用途に限定します。",
            useWhen:
            [
                "Save、Undo、Play など高頻度で短い title の command を Editor chrome に常時表示する場合。",
                "active document の `CommandContribution.Toolbar=true` を global command と同じ順序規則で合成する場合。",
                "registry registration の変更に自動追従し、dirty／selection など外部 state 変化時だけ `Refresh()` で enablement を再評価する場合。",
            ],
            avoidWhen:
            [
                "すべての command の discovery、検索、階層、shortcut 表示が必要な場合。MenuBar／CommandPalette を併用します。",
                "icon-only action、tooltip、overflow menu、responsive wrap、toggle／radio state、async progress が必要な toolbar。",
                "keyboard focus で toolbar item を移動・実行することが必須な場合。現在の Button row は pointer-only です。",
            ],
            alternatives:
            [
                new("MenuBar", "階層化された application command と gesture を安定した入口として表示します。"),
                new("CommandPalette", "全 command を title／id で検索し keyboard で実行します。"),
            ],
            usage: """
                Toolbar toolbar = EditorKit.Toolbar(
                    session.Commands,
                    contributions: () =>
                        session.Workspace.Active.Value?.Contributions ?? []);

                Reactive.Effect(() =>
                {
                    _ = session.Workspace.AnyDirty.Value;
                    _ = session.Workspace.Active.Value;
                    toolbar.Refresh();
                });
                """,
            anatomy: "`CommandRegistry.ToolbarCommands()` の結果を 2 px 間隔の `HStack` へ並べます。enabled command は短い title の ghost `Button`、disabled command は左右 8 px margin の 12 px `TextMuted` です。Toolbar 自身は background、frame、row height、overflow chrome を描かず、EditorShell が 4×2 px padding の `Surface` border と 34 px 行を与えます。",
            variants: "global registry entry と active contribution、`Order`／登録順、title、enablement で項目と順序が変わります。registry `Version` は自動 rebuild し、contribution provider が読む reactive state も追跡されます。enablement predicate だけが変わる場合は `Refresh()` が内部 signal を進めます。disabled command は disabled Button ではなく Text へ形が変わります。",
            state: "command identity、title、run、enablement、toolbar membership は `CommandRegistry`／`CommandContribution` が所有します。Toolbar は enablement 再評価用の internal revision だけを持ち、選択、dirty、play state、progress を保持しません。`Refresh()` の呼び時は shell／composition が所有し、EditorShell は active document と any-dirty の変化で呼びます。",
            pointer: "enabled ghost Button の click で `Command.Run()` を同期実行します。disabled entry は Text のため hit target がなく、click できません。Toolbar 自体に drag、context menu、reorder、overflow はありません。",
            keyboard:
            [
                new("—", "Toolbar とその Button は focus target／arrow navigation／Enter／Space activation を登録しません。"),
                new("登録済み gesture", "command shortcut は Toolbar ではなく `CommandRegistry.BindShortcuts(UiHost)` または EditorShell が処理します。"),
            ],
            focus: "Toolbar を pointer で操作しても keyboard focus は toolbar へ移りません。active editor の focus を保ったまま command を実行します。dismissal はなく、項目の追加・削除は registry／contribution の rebuild で行います。keyboard-only 利用者には shortcut、MenuBar、CommandPalette を提供します。",
            accessibility: new ControlDocsAccessibility(
                "command title を action が分かる短い動詞句にし、記号だけの title には隣接説明または別経路を用意します。",
                "Toolbar／enabled Button／disabled Text は toolbar／button／disabled semantics を一貫して公開しません。特に disabled 時に widget type が変わります。",
                "disabled は muted text、enabled は ghost button で示しますが、toggle／checked／busy／progress state はありません。status／dialog に結果を出します。",
                "ghost button、muted text、hover、shell surface のコントラストを確認し、disabled と enabled を色だけで区別しない補助情報を MenuBar 等に持たせます。",
                "hover fade 以外の必須 motion はありません。長時間 command の progress は toolbar 外の status／dialog へ出します。",
                "keyboard navigation、semantic toolbar、tooltip、icon accessible name、overflow、busy announcement は未実装です。Toolbar を唯一の command 経路にしないでください。"),
            theme: new ControlDocsThemeLayout(
                "enabled は theme の ghost Button、disabled は `TextMuted` を使います。Toolbar 自身は透明で、EditorShell など親の `Surface`／padding／border を前提にします。",
                "2 px 間隔の一行 `HStack` で、wrap、scroll、overflow menu はありません。関連 command を registry `Order` で近くに置き、長い title と多数 item を避けます。",
                "固有の固定高はなく child intrinsic size です。EditorShell は 34 px 行を与えます。狭い window では右側 item が収まらないため、responsive command subset は contribution／registry 側で切り替えます。"),
            constraints: new ControlDocsConstraints(
                "enablement predicate は build／Refresh 時に同期評価されます。`Refresh()` を呼ばない外部 state 変化は表示へ反映されません。command action は同期 `Action` で、exception、async progress、confirmation、permission、undo transaction は caller の責任です。多数 command や長い title の overflow 処理はありません。",
                "registry と contribution provider は session と同じ寿命にします。Toolbar は resource を直接所有せず、reactive build scope だけを使います。active document／dirty 変化など enablement source を effect で読み、必要なときだけ `Refresh()` します。",
                "Browser／Native の Retained UI host で利用できます。native OS toolbar、touch-specific overflow、platform shortcut export は提供しません。"),
            apiHighlights: "`registry` と `contributions`、enablement 再評価の `Refresh()`。command 列は `CommandRegistry.ToolbarCommands()` が `Order`／登録順で返し、`Command.Title` と `Command.IsEnabled` を表示します。",
            eventContracts: "Toolbar 固有の `UiEvent` はありません。enabled item click が `Command.Run` を呼びます。command の結果、dirty、selection、play state、error／progress は registry action と EditorSession service が更新し、その変化を composition が `Refresh()` へ接続します。",
            related:
            [
                new("Examples/Workbench/Shell", "EditorShell 内の Toolbar", "active document、dirty、save command と production shell の refresh 配線を確認します。", StoryKind.Example),
                new("Examples/Workbench/Files", "Workbench: files", "Save／Reload の enablement と external change を Toolbar から実行します。", StoryKind.Example),
                new("Examples/Workbench/Inspector", "Workbench: inspector document", "Save／Undo／Redo の enablement を document state と同期します。", StoryKind.Example),
                new("Examples/Workbench/Material", "Workbench: material", "異なる document kind で同じ Save toolbar を再利用します。", StoryKind.Example),
            ]),
    ];

    private static ControlDocsPage DetailedPage(
        string type,
        string category,
        string summary,
        IReadOnlyList<string> useWhen,
        IReadOnlyList<string> avoidWhen,
        IReadOnlyList<ControlDocsAlternative> alternatives,
        string usage,
        string anatomy,
        string variants,
        string state,
        string pointer,
        IReadOnlyList<ControlDocsKeyboardBinding> keyboard,
        string focus,
        ControlDocsAccessibility accessibility,
        ControlDocsThemeLayout theme,
        ControlDocsConstraints constraints,
        string apiHighlights,
        string eventContracts,
        IReadOnlyList<ControlDocsStory>? related = null)
    {
        string prefix = $"Controls/{category}/{type}";
        var stories = new List<ControlDocsStory>
        {
            new($"{prefix}/Playground", "プレイグラウンド", $"{type} の生成済み引数を対話的に確認します。", StoryKind.Playground),
        };
        if (related is not null) stories.AddRange(related);

        return new ControlDocsPage(
            $"global::Luxel.Controls.{type}",
            type,
            summary,
            useWhen,
            avoidWhen,
            alternatives,
            usage,
            anatomy,
            variants,
            state,
            pointer,
            keyboard,
            focus,
            accessibility,
            theme,
            constraints,
            new ControlDocsApi(type, apiHighlights, eventContracts),
            new ControlDocsStory($"{prefix}/Basic", "基本例", $"{type} の canonical Basic story を実行します。", StoryKind.Basic),
            stories);
    }
}
