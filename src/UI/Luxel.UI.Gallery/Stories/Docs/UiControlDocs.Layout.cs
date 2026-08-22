using Luxel.Gallery;
using Luxel.Gallery.UI;

namespace Luxel.UI.Gallery;

internal static partial class UiControlDocs
{
    private static readonly ControlDocsPage[] LayoutPages =
    [
        Page(
            "Border",
            "Layout",
            "Border",
            "背景、角丸、padding、透明度、拡大率と任意のクリップを一つの子へまとめる装飾コンテナーです。名前に反して枠線を描く API はありません。",
            ["カードやパネルの一つの内容へ背景と内側余白を与える場合。", "角丸矩形の内側へ子の描画を明示的に切り取りたい場合。"],
            ["複数の子を直接並べる場合。", "枠線の太さや色を Border 単体で指定したい場合。"],
            "Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 12, padding: new Thickness(16), clip: true)[content]",
            "一つの子 Widget は Border インスタンスが参照し、同じ Border へ再度 `[...]` を適用すると参照先が置き換わります。装飾値は Bindable で、業務状態は子または呼び出し側の Signal が所有します。",
            "`background`、`rounded`、`corners`、`padding`、`clip`、`opacity`、`scale` と Widget 共通の寸法指定が中心です。枠線色・線幅の固有パラメーターはありません。",
            "Border 自身は hit target、focus target、操作イベントを追加しません。ポインターとキーボード入力は子が登録した範囲だけで処理されます。",
            "背景は未指定なら透明です。有限の親制約を padding 分だけ縮めて子へ渡し、明示した width/height は外寸として content box を拘束します。子の margin と hAlign/vAlign は Border の配置では参照しません。",
            "`clip: false` では子の描画と hit target を Border 矩形で切りません。`clip: true` は角丸と corners を含む祖先 RectClip を設定して子の描画・入力範囲を切りますが、スクロールやオーバーフロー配置は提供しません。",
            "Border",
            alternatives: [new("Box", "子を持たない塗り矩形だけが必要な場合。"), new("Stack", "複数の子を一方向へ配置する場合。")],
            related: [new("Controls/Layout/Border/Examples/Interactive", "装飾の対話例", "背景色、角丸、幅、高さを変更し、単一子との組み合わせを確認します。", StoryKind.Example)]
        ) with
        {
            Anatomy = "一つの背景 Scene2D ノードと、padding でオフセットされる最大一つの child Widget から構成します。`clip` を有効にすると同じ矩形が子サブツリーのクリップになります。",
            Variants = "`rounded` と `corners` で角を選び、`scale` でノードと子サブツリーを拡大縮小し、`clip` で子のオーバーフローを切り替えます。`opacity` は背景色の alpha だけへ掛かり、子全体の透明度ではありません。枠線 variant はありません。",
            FocusActivationDismissal = "Border はフォーカスを登録せず、activation と dismissal もありません。フォーカス順は子コントロールの登録順に従います。",
            Accessibility = new("Border 自体には名称を付けず、内容の見える見出しや子コントロールのラベルで目的を示します。", "専用 semantic role は公開せず、子の semantics を置き換えません。", "hover、選択、展開などの状態は保持・公開しません。", "背景と子の文字・アイコンのコントラストを利用テーマ上で確認します。", "固有アニメーションはありません。opacity/scale を外部 transition で動かす場合も情報を動きだけに依存させません。", "装飾境界は意味上のグループ名になりません。必要な見出しや説明は子として明示します。"),
            ThemeLayout = new("`background` をテーマ由来の Bindable にすると再実体化せず色変更へ追従します。未指定時は透明です。`rounded`、`corners`、`clip` は描画形状の再実体化が必要です。", "子には親制約を padding 分だけ deflate して渡し、子の margin/align を介さず左上を `(padding.Left, padding.Top)` に置きます。", "外寸は子の制約後サイズ + padding を基準に、共通 width/height と親制約で決まります。明示寸法より padding が大きい場合、子の利用可能寸法は 0 まで縮みます。"),
            Constraints = new("一つの子しか保持せず、複数子の配置、スクロール、枠線描画は行いません。`clip` は子孫の描画と hit 範囲に効き、`opacity` は背景だけへ効きます。", "Border は子インスタンスを複製・破棄しません。IDisposable な子や外部 Signal の寿命はそれぞれの所有者が管理します。", "標準の RetainedCanvas 2D 描画で動作し、追加の GPU callback やネイティブウィンドウを要求しません。"),
            Api = new("Border", "`background`、`opacity`、`scale`、`rounded`、`corners`、`padding`、`clip` と単一子 indexer が固有 API です。", "固有イベントはありません。Bindable の変更は装飾へ、子の状態変更は子自身へ反映します。"),
        },
        Page(
            "Box",
            "Layout",
            "Box",
            "背景色と角丸を描く子なしの矩形プリミティブです。固定領域、色見本、区切り、スケルトンなど意味を持たない面に使います。",
            ["幅と高さを持つ単純な塗り矩形を一ノードで描く場合。", "レイアウト内に視覚的な面や細い区切りを置く場合。"],
            ["矩形の中へ子 Widget を入れる場合。", "文字、画像、操作、アクセシビリティ上の意味が必要な場合。"],
            "Box(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: 8, width: 180, height: 96)",
            "内部状態と子要素は持ちません。背景と寸法を Signal/Bindable で更新する場合、その正本は呼び出し側が所有します。",
            "`background` と `rounded` が固有パラメーターです。幅、高さ、margin、hAlign、vAlign は Widget 共通指定を使います。",
            "Box は描画だけを行い、hit target、focus、イベントを登録しません。",
            "背景未指定時は `UiTheme.SurfaceAlt` を使います。Stretch の軸は有限の最大制約まで広がり、それ以外は指定寸法を親制約へ収めます。",
            "子、クリップ、枠線、テキスト、操作はありません。無限制約の Stretch 軸は 0 を基準にするため、必要な寸法を明示してください。",
            "Box",
            alternatives: [new("Border", "背景付き領域に一つの子、padding、clip が必要な場合。"), new("Spacer", "描画せず空間だけを確保する場合。")],
            related:
            [
                new("Controls/Layout/Box/Examples/Interactive", "矩形の対話例", "背景、角丸、幅、高さによる見た目を確認します。", StoryKind.Example),
                new("Controls/Layout/Layout/Examples/Units", "寸法単位", "Box の px、%、em、vw による幅指定を比較します。", StoryKind.Example),
            ]
        ) with
        {
            Anatomy = "角丸または通常の塗り矩形を持つ一つの Scene2D ノードだけで構成し、子 Widget は受け取りません。",
            Variants = "`rounded: 0` は矩形、正値は角丸です。半径は実寸の短辺の半分まで描画時に丸められます。色は `background` で指定します。",
            FocusActivationDismissal = "フォーカス、activation、dismissal はありません。操作面が必要なら Button などの操作コントロールを選びます。",
            Accessibility = new("装飾 Box には名称を付けず、意味は隣接する文字や実際の操作コントロールで表します。", "semantic role と支援技術向けノードを公開しません。", "選択・無効・進捗などの状態は保持しません。", "意味のある色分けに使う場合は文字や形も併用し、周囲とのコントラストを確認します。", "固有アニメーションはありません。Bindable 色や寸法を動かす場合は静的な情報経路も残します。", "Box だけでは名称、説明、操作を伝えられません。"),
            ThemeLayout = new("`background` 未指定時はテーマの `SurfaceAlt`、指定時はその Bindable 色を使います。", "Stretch は有限の親最大制約を採用し、Start/Center/End では共通 width/height の解決値を親制約へ収めます。", "子の自然サイズはありません。無指定寸法は 0 を基準にするため、区切りやプレースホルダーでは少なくとも一軸の寸法を指定します。"),
            Constraints = new("内容レイアウト、枠線、画像、クリップは提供しない葉 Widget です。", "専用資源を所有せず、一つの保持描画ノードとして再実体化されます。", "標準 2D ホストで利用でき、追加プラットフォーム API は不要です。"),
            Api = new("Box", "固有 API は `background` と `rounded` です。共通 width/height と整列指定が実寸を決めます。", "固有イベントと子 indexer はありません。"),
        },
        Page(
            "Center",
            "Layout",
            "Center",
            "一つの子を利用可能な矩形の中央へ置く、全域占有を既定とする単一子コンテナーです。",
            ["空状態、待機表示、プレビューなど一つの要素を親の中央へ置く場合。", "有限な幅・高さの中で子の自然サイズを保ったまま中央揃えする場合。"],
            ["複数要素の間隔や端揃えを管理する場合。", "無限制約の軸でも必ず特定の領域いっぱいへ広げたい場合。"],
            "Border(width: 320, height: 180)[Center()[child]]",
            "最大一つの子を参照し、再度 `[...]` を適用すると子参照を置き換えます。Center 自身に選択や開閉状態はありません。",
            "固有パラメーターは単一子 indexer だけです。生成 API に共通 width/height は現れますが、この実装はそれらを読まず、外側制約で領域を決めます。",
            "Center 自身は操作を登録しません。子が操作可能なら、その子の hit target と focus が中央位置で機能します。",
            "既定で両軸 Stretch です。有限制約では最大領域を取り、子へ loose 制約を渡して強制的に中央へ配置します。Center に width/height を渡さず、外側の Border/Grid などで有限領域を作ります。",
            "クリップ、スクロール、複数子、背景はありません。無限制約の軸では子の自然サイズへフォールバックします。",
            "Center",
            alternatives: [new("Stack", "複数子を順序付きで並べ、交差軸整列も選ぶ場合。"), new("Grid", "セル内の整列と複数領域を同時に扱う場合。")],
            related: [new("Controls/Layout/Center/Examples/Interactive", "中央配置の対話例", "外側寸法と中央の子矩形を変更して制約伝播を確認します。", StoryKind.Example)]
        ) with
        {
            Anatomy = "透明な一つのコンテナーノードと、中央へ配置される最大一つの child Widget から構成します。",
            Variants = "固有 variant はありません。外側から与える有限領域と、子の自然サイズ・margin の組み合わせで中央余白が変わります。Center 自身へ渡す width/height は現在効きません。",
            FocusActivationDismissal = "Center はフォーカスを登録せず、activation と dismissal もありません。子のフォーカス契約だけが有効です。",
            Accessibility = new("Center 自身ではなく、中央に置く内容へ見えるラベルを与えます。", "専用 semantic role はなく、中央配置という視覚位置も意味として公開しません。", "状態は保持しません。空状態や待機状態は文字でも明示します。", "中央の内容と背景のコントラストを外側コンテナーを含めて確認します。", "固有アニメーションはありません。", "位置だけでは重要度や状態を支援技術へ伝えられません。"),
            ThemeLayout = new("色や背景を描かないため、テーマは子と外側コンテナーが決めます。", "有限軸は親の `MaxW`/`MaxH` を取り、子を margin 分だけ縮めた loose 制約で中央配置します。", "無限軸では子の自然サイズを採用し、子がない無限軸は 0 になります。共通 width/height は現在無視されるため、中央余白は外側コンテナーの有限寸法で作ります。"),
            Constraints = new("子の整列は常に Center で、子自身の hAlign/vAlign は中央配置の選択には使いません。無限制約時は子を自然測定した後に配置用レイアウトを行うことがあります。描画のはみ出しを切る clip はありません。", "子 Widget の寿命と資源は子の所有者が管理します。Center は子を複製・破棄しません。", "標準レイアウトと 2D ホストだけを使います。"),
            Api = new("Center", "固有 API は単一子 indexer だけです。生成された共通 width/height は実装で参照されないため、外寸は親制約で指定します。", "固有イベントはありません。"),
        },
        Page(
            "Grid",
            "Layout",
            "Grid",
            "Fixed、Auto、Star のトラックへ複数の子を配置する行列レイアウトです。フォームや整列されたダッシュボード向けで、データ表の意味は持ちません。",
            ["複数要素を共有する列境界・行境界へ明示的に揃える場合。", "固定 px、内容基準の列幅、残り幅の比率配分を組み合わせる場合。"],
            ["一方向へ自然に並べるだけの場合。", "Auto 高の行、要素間 gap、スクロール、仮想化、表 semantics が必要な場合。"],
            "Grid(columns: [GridLength.Auto, GridLength.Star(1)], rows: [GridLength.Px(40), GridLength.Star(1)])[\n    label.GridCell(0, 0), content.GridCell(1, 0, rowSpan: 2)]",
            "Grid は追加順の子リストとトラック定義を保持します。セル位置は各子の attached property が所有し、データや選択状態は子・呼び出し側が所有します。",
            "`columns`、`rows` と子側の `GridColumn`、`GridRow`、`GridColumnSpan`、`GridRowSpan`、`GridCell` が中心です。Grid 自身に gap はなく、生成された共通 width/height も現在は参照しません。",
            "Grid 自身は操作を登録しません。セル内の操作と focus は各子が担い、Grid は table/grid semantic role を追加しません。",
            "int のトラック値は Star 比率です。Fixed と Auto 列を先に確定し、有限の残り幅を Star へ比率配分します。Grid の領域は外側制約で与えます。",
            "Auto は列だけを intrinsic 計測し、Auto 行は v1 では 0 です。クリップ、スクロール、仮想化、gap は提供せず、固定/Auto の合計が親を超えると子が報告矩形からオーバーフローし得ます。",
            "Grid",
            alternatives: [new("Stack", "一方向の順序と spacing だけで十分な場合。"), new("Wrap", "子の幅に応じて横方向に自動折返しする場合。"), new("DataGrid", "列見出し、行選択、仮想化、grid semantics が必要な場合。")],
            related:
            [
                new("Controls/Layout/Grid/Examples/Tracks", "Star トラック", "1:2:1 の列配分とセル配置を確認します。", StoryKind.Example),
                new("Controls/Layout/Grid/Examples/AttachedUtilities", "Attached utility", "fluent helper と `U.Grid.*` utility によるセル指定を確認します。", StoryKind.Example),
            ]
        ) with
        {
            Anatomy = "透明な Grid ノード、`columns`/`rows` のトラック配列、attached property でセルを選ぶ複数の child Widget から構成します。",
            Variants = "各トラックは `GridLength.Px`、`GridLength.Auto`、`GridLength.Star` です。未指定または空配列は各軸 Star(1) 一本になります。列・行 span は 1 以上です。",
            FocusActivationDismissal = "Grid はフォーカスを登録せず、activation と dismissal もありません。視覚上のセル順と子の操作順を一致させる設計は呼び出し側の責務です。",
            Accessibility = new("Grid そのものではなく、各子のラベルと関係を見出しや説明で示します。", "table、grid、row、cell の semantic role は公開しません。", "選択、並べ替え、編集状態は管理・公開しません。", "セル間の境界が必要なら別の Box/Border を配置し、文字と背景のコントラストを確認します。", "固有アニメーションはありません。", "視覚的な二次元配置だけでは表の読み順や見出し対応を支援技術へ伝えられません。データ表には DataGrid などを選びます。"),
            ThemeLayout = new("Grid 自身は色を描かず、テーマは各子が使用します。", "Fixed/Auto の使用量を引いた残りを Star へ配分し、各子へ margin を除いたセル矩形の制約を渡して hAlign/vAlign で配置します。", "有限の親幅・高さが Star の基準です。無限軸では基準総量が 0 となり Star は 0 になります。共通 width/height は無視されるため外側から有限制約を与えます。"),
            Constraints = new("Auto 列は同列から始まる子の `MaxIntrinsicWidth` 最大値だけを使い、span へ分配しません。Grid 自身の intrinsic 幅は 0、Auto 行は未対応で 0、範囲外の row/column は端へ clamp、span は残存トラックまでです。同一セルの複数子は追加順に重なります。", "Auto 列ごとに子全体を走査し、全子を毎回配置・実体化します。子リストを仮想化・再利用せず、子の IDisposable 資源は各所有者が管理します。", "標準レイアウト/2D ホストで動作します。大量セルや表操作にはコレクション系コントロールを使います。"),
            Api = new("Grid", "`GridLength[] columns/rows` と、子 Widget の typed attached properties / `U.Grid.*` utilities が主要 API です。共通 width/height は実装で参照されません。", "固有イベントはありません。トラックや attached property の変更は呼び出し側が再レイアウトを起こしたときに反映します。"),
        },
        Page(
            "Spacer",
            "Layout",
            "Spacer",
            "描画せず、共通 width/height で空間だけを確保する葉 Widget です。",
            ["二要素の間に一度だけ明示寸法の空白を置く場合。", "行や列の一部へ描画されない固定領域を予約する場合。"],
            ["同種の兄弟間隔を繰り返し作る場合。", "主軸の余剰を flex のように自動配分したい場合や、意味のある区切りを表示する場合。"],
            "Spacer(width: 16, height: 1)",
            "内部状態、子、描画資源を持ちません。寸法の正本は指定値または外部 Bindable です。",
            "固有パラメーターはなく、Widget 共通の width、height、margin、alignment を使います。",
            "操作、hit target、focus、イベントはありません。",
            "色を描かずテーマへ依存しません。解決した width/height を親制約へ収めます。",
            "Stack の主軸で余剰領域を吸収する flex spacer ではありません。構造的な spacing には Stack の `spacing` を優先します。",
            "Spacer",
            alternatives: [new("Stack", "複数兄弟の一貫した間隔を `spacing` で指定する場合。"), new("Box", "空間に色や区切り線も表示する場合。")],
            related: [new("Controls/Layout/Spacer/Examples/Interactive", "空間の対話例", "二つの Box の間で Spacer の幅と高さを変更します。", StoryKind.Example)]
        ) with
        {
            Anatomy = "レイアウトサイズだけを持つ一つの葉 Widget で、Scene2D ノードや child Widget は生成しません。",
            Variants = "共通 Length の px、%、em、vw などによる width/height と、親が与える tight 制約だけがバリエーションです。専用の weight はありません。",
            FocusActivationDismissal = "フォーカス、activation、dismissal はありません。",
            Accessibility = new("名称は不要です。意味のある区切りには見出し、文字、Divider 相当の描画を使います。", "semantic node を公開しません。", "状態を保持・公開しません。", "描画しないため固有のコントラスト要件はありません。", "アニメーションはありません。", "空白だけでグループ関係や順序を伝えないでください。"),
            ThemeLayout = new("テーマ色や文字尺度を直接参照しません。em/vw などの Length 解決は共通レイアウト文脈に従います。", "`ResolveW`/`ResolveH` の結果を親の min/max 制約へ収めます。親が tight 制約を渡す軸ではその寸法になります。", "主軸が unbounded な Stack では指定した自然寸法だけを取り、残り空間を自動的に埋めません。"),
            Constraints = new("描画、入力、flex weight、子、スクロールを持ちません。実体化ノードも作らないため共通 transform は可視効果を持ちません。過剰な Spacer はレイアウト意図を分散させます。", "解放すべき固有資源はありません。", "すべての標準レイアウトホストで利用できます。"),
            Api = new("Spacer", "固有 API はなく、継承した width/height と配置パラメーターだけを使います。", "イベントはありません。"),
        },
        Page(
            "Splitter",
            "Layout",
            "Splitter",
            "隣接ペインの境界として置く 6 px のドラッグバーです。二つのペインや比率は所有せず、ドラッグ終了時の移動量だけを通知します。",
            ["左右または上下の隣接領域について、呼び出し側の寸法モデルをポインタードラッグで更新する場合。"],
            ["Splitter 自身に二つの子、分割比率、最小寸法、連続リレイアウトを管理させたい場合。", "キーボードだけで調整できるアクセシブルな値入力が必要な場合。"],
            "Splitter(vertical: true, onResized: (_, delta) => ResizeLeftPane(delta))",
            "hover と pressed は Splitter 内部の Signal が保持します。ペイン寸法、最小/最大、比率、永続化は呼び出し側が所有し、`onResized` の delta を検証してモデルへ反映します。",
            "`vertical` と `onResized(Splitter sender, float delta)` が固有 API です。厚さは `Splitter.Thickness` の 6 px 固定で、共通 width/height は参照しません。",
            "全領域をドラッグ hit target とし、ドラッグ中はバーだけを transform でゴースト移動します。離した時に非 0 の delta を一度通知し、その後元の位置へ戻ります。",
            "通常はテーマの BorderColor、hover/drag 中は Primary で描きます。vertical は左右分割用の縦バー、false は上下分割用の横バーです。",
            "二つのペイン、ratio、最小サイズ、取消、連続 resize callback はありません。通知後の再レイアウトと範囲 clamp は呼び出し側で行います。",
            "Splitter",
            alternatives: [new("DockHost", "複数パネルの分割、移動、ドッキングをモデルごと扱う場合。"), new("Slider", "キーボードと値 semantics を持つ連続値入力が必要な場合。")]
        ) with
        {
            Anatomy = "6 px 厚の一つの Widget、中央の 2 px 描画バー、全矩形の drag hit target から構成します。ペイン child は持ちません。",
            Variants = "`vertical: true` は幅 6 pxで横方向 delta を返し、`false` は高さ 6 pxで縦方向 delta を返します。色は hover/pressed 状態で切り替わります。",
            FocusActivationDismissal = "フォーカスを登録せず、ポインタードラッグだけで起動します。Escape による取消や dismissal はありません。キーボード利用者向けに増減・リセット Button を併設します。",
            Accessibility = new("境界の対象を隣接見出しや別の操作ラベルで示します。", "separator、slider、value の semantic role は公開しません。", "現在値、最小値、最大値、pressed 状態を支援技術へ公開しません。", "通常色と hover/drag 色が背景から識別できることを確認します。6 px の視覚線だけへ依存せず周辺に十分な操作余白を設計します。", "ドラッグ中はバーがポインターに追従しますが、周辺ペインは連続アニメーションしません。", "キーボード操作、読上げ可能な値、ドラッグ取消を提供しません。代替操作を必ず用意します。"),
            ThemeLayout = new("中央バーは `BorderColor`、hover/pressed 中は `Primary` を使います。", "vertical は `(6, finite MaxH)`、horizontal は `(finite MaxW, 6)` を要求し、無限の長辺は 100 pxへフォールバックします。", "ペイン間へ兄弟として配置し、親側がペイン寸法を管理します。極端な delta は callback 側で clamp します。"),
            Constraints = new("ドラッグ中は自ノードの transform だけを更新し、レイアウト値は変えません。`onResized` は drag end の一回だけです。", "固有の外部資源はありません。callback が参照するモデルの寿命は呼び出し側が管理します。", "ポインタードラッグと resize cursor を扱えるホスト向けです。タッチや支援技術だけの利用経路は実装していません。"),
            Api = new("Splitter", "`vertical`、`OnResized`、定数 `Thickness = 6f` が主要 API です。生成された共通 width/height は実装で参照されません。", "drag end で delta が非 0 の場合だけ `OnResized(this, delta)` を一度発火します。callback は寸法更新と再レイアウトを行います。"),
        },
        Page(
            "StackPanel",
            "Layout",
            "Stack",
            "複数の子を縦または横の一方向へ、追加順と一定 spacing で並べる基本コンテナーです。",
            ["フォーム、ツール列、カード内の行などを一方向へ自然サイズで積む場合。", "兄弟間へ同じ spacing を一貫して適用する場合。"],
            ["折返し、二次元セル、主軸の余剰配分、スクロール、仮想化が必要な場合。"],
            "VStack(8)[heading, body, actions]",
            "Stack は追加順の子リストを保持します。選択やスクロール状態は持たず、各子の状態は子または外部モデルが所有します。",
            "`vertical`、`spacing`、複数子 indexer が固有 API です。`VStack` と `HStack` は `Stack` の sugar ですが、生成・sugar とも width/height は現在レイアウトで参照されません。",
            "Stack 自身は操作を登録しません。子の追加順が描画・操作上の基本順になります。",
            "色は描かず、交差軸には親の有限制約を伝えます。子の Stretch は交差軸だけを tight にし、主軸は自然サイズで測ります。",
            "主軸の flex 配分、折返し、clip、scroll、virtualization はありません。狭い親では子の合計が Stack の矩形を超えても自動再配置せず、主軸の百分率寸法も無限制約を基準にできません。",
            "Stack",
            alternatives: [new("Wrap", "横方向の端で次行へ自動折返しする場合。"), new("Grid", "列・行の境界を複数要素で共有する場合。"), new("ScrollViewer", "主軸の内容を固定 viewport 内でスクロールする場合。")],
            related: [new("Controls/Layout/Stack/Examples/Interactive", "方向と spacing", "vertical と spacing を変更し、一方向レイアウトを比較します。", StoryKind.Example)]
        ) with
        {
            Anatomy = "透明な一つのコンテナーノードと、追加順を保持する複数の child Widget リストから構成します。",
            Variants = "`vertical: true` の縦 Stack、`false` の横 Stackがあります。`VStack(spacing)` と `HStack(spacing)` は同じ実装を生成します。",
            FocusActivationDismissal = "Stack はフォーカスを登録せず、activation と dismissal もありません。子のフォーカス順を意味のある追加順にします。",
            Accessibility = new("Stack ではなく、各子へ見えるラベルや見出しを付けます。", "list、group などの semantic role は自動公開しません。", "選択・展開・並べ替え状態を保持しません。", "背景を描かないため、各子と外側表面のコントラストを確認します。", "固有アニメーションはありません。子の追加・削除をアニメーションする機能もありません。", "視覚的な縦横配置だけではリスト意味を支援技術へ伝えません。必要なら semantic 対応コレクションを選びます。"),
            ThemeLayout = new("Stack 自身はテーマ色を使わず、spacing は px 値です。", "主軸は各子へ無限最大制約を渡して自然サイズを合計し、交差軸は親の有限最大値から margin を引いて制約します。", "最終サイズは主軸合計 + spacing と交差軸最大を親制約へ収めます。共通 width/height は無視され、親に収まらない主軸内容は clip されないため、外側 Box/Grid や ScrollViewer で領域を作ります。"),
            Constraints = new("spacing は隣接子間だけに入り、margin は各子の外側へ加算されます。均等配分、weight、wrap はありません。", "レイアウトは子を一度測定した後に交差軸整列でもう一度走査し、全子を実体化します。リストの remove/virtualize API はなく、子の資源寿命は各所有者が管理します。", "標準レイアウト/2D ホストで動作します。大量項目には仮想化コレクションを使います。"),
            Api = new("Stack", "`vertical`、`spacing`、複数子 indexer と `VStack`/`HStack` sugar が主要 API です。共通 width/height は実装で参照されません。", "固有イベントはありません。子順や構成、レイアウト値の変更は呼び出し側が Widget ツリー再構築または再レイアウトを起こして反映します。"),
        },
        Page(
            "WrapPanel",
            "Layout",
            "Wrap",
            "子を左から右へ並べ、利用可能幅を超える前に次の行へ送る横方向専用の折返しコンテナーです。",
            ["チップ、可変幅カード、タグなどを横方向へ並べ、幅に応じて自動改行する場合。"],
            ["縦方向へ折り返す場合。", "列幅を揃える場合、項目を仮想化する場合、厳密な行列読み順が必要な場合。"],
            "Wrap(hgap: 8, vgap: 8, width: 320)[items]",
            "Wrap は追加順の子リストを保持し、折返し位置はその時点の幅制約と各子の測定結果から毎レイアウト計算します。",
            "`hgap`、`vgap` と複数子 indexer が固有 API です。方向指定はなく、常に左から右、上から下です。共通 width は折返し幅に使いますが height は参照しません。",
            "Wrap 自身は操作を登録しません。折返し後も子の追加順は変わらず、各子が自身の入力を扱います。",
            "色は描きません。親の有限幅または共通 width が折返し幅となり、各行高はその行の最大 child 高です。",
            "無限幅かつ width 未指定では一行になり、clip、scroll、均等列、virtualization はありません。親より大きい明示 width や大きすぎる先頭子は報告矩形からオーバーフローし得ます。",
            "WrapPanel",
            alternatives: [new("Stack", "折り返さず一方向へ並べる場合。"), new("Grid", "行列境界と列幅を明示する場合。"), new("GridView", "同サイズ項目を仮想化して複数列表示する場合。")]
        ) with
        {
            Anatomy = "透明な一つのコンテナーノード、追加順の child Widget リスト、行ごとの x/y/最大高を計算するレイアウトから構成します。",
            Variants = "横間隔 `hgap` と行間 `vgap` だけを切り替えます。縦向き、均等化、justify variant はありません。",
            FocusActivationDismissal = "Wrap はフォーカスを登録せず、activation と dismissal もありません。子の追加順を論理順として保ちます。",
            Accessibility = new("各項目に見えるラベルを持たせ、Wrap 自体には名称を付けません。", "list/grid semantic role は自動公開しません。", "選択・折返し行・列数を状態として公開しません。", "各項目の文字・境界と外側背景のコントラストを確認します。", "固有アニメーションはありません。幅変更時の再配置にも transition はありません。", "画面幅で視覚行が変わるため、行番号や位置だけに意味を持たせないでください。"),
            ThemeLayout = new("Wrap 自身はテーマ色を使わず、gap は px 値です。", "各子へ利用可能幅から margin を引いた最大幅と無限高を渡し、次の子が行末を超える場合に改行します。子の hAlign/vAlign は行内配置に使いません。", "有限幅では Wrap 自身の幅は利用可能幅になり、高さは各行の最大高 + vgap の合計です。無限幅では内容幅へ shrink-wrap して一行になり、共通 height は無視されます。"),
            Constraints = new("最初の子は利用可能幅へ制約されますが、子自身が制約を超えて描く場合や明示 width が親より大きい場合の clip はありません。行内の縦整列や均等列幅も提供しません。", "全子を毎回測定・実体化し、項目プールや仮想化はありません。子の資源寿命は各所有者が管理します。", "標準レイアウト/2D ホストで動作します。大量項目には GridView などを検討します。"),
            Api = new("WrapPanel", "`hgap`、`vgap`、複数子 indexer と共通 width が主要 API です。共通 height は実装で参照されません。", "固有イベントはありません。幅・子寸法の変更は呼び出し側が再レイアウトを起こしたときに折返しを再計算します。"),
        },
    ];
}
