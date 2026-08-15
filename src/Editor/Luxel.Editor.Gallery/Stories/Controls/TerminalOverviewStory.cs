using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

/// <summary>Terminal control overview and integration guide.</summary>
[StoryMeta("Controls/Terminal")]
public static class TerminalOverviewStory
{
    [Story]
    public static StoryResult Overview(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());
        return $$"""
        # 端末エミュレータ (Luxel.Terminal)

        {{Toc()}}

        Luxelの端末機能は、VT/ANSIを解釈するOS非依存コア、OSごとのPTY backend、Luxel UIへ描画する`TerminalView`の3層です。WindowsではConPTY、LinuxではUnix PTYを使用します。`Luxel.Controls`とは相互に依存せず、アプリが必要な層だけを組み合わせます。

        ```mermaid
        flowchart TB
        app[アプリ] --> view[Luxel.Terminal.UI - TerminalView]
        app --> session[Luxel.Terminal - TerminalSession]
        view --> session
        view --> ui[Luxel.UI]
        view --> typo[Luxel.Typography.TwoD]
        session --> pty[ITerminalPty]
        pty --> win[Luxel.Terminal.Windows - ConPTY]
        pty --> linux[Luxel.Terminal.Linux - forkpty]
        ```

        ## プロジェクト

        | プロジェクト | 役割 |
        | --- | --- |
        | `Luxel.Terminal` | VT/ANSI parser、screen/grid、scrollback、入力encode、session lifecycle |
        | `Luxel.Terminal.UI` | 固定セル描画、selection、clipboard、IME、font fallback、viewport resize |
        | `Luxel.Terminal.Windows` | Windows ConPTY、pipe、Job Objectによるprocess tree cleanup |
        | `Luxel.Terminal.Linux` | glibc Linux向けUnix PTY、nonblocking I/O、window size、process group cleanup |

        `Luxel.Terminal`はUIやGPUを参照しないため、parser・buffer・sessionはヘッドレスでテストできます。`Luxel.Terminal.UI`も`Luxel.Controls`を参照せず、`Luxel.UI`の`Widget`として直接利用します。

        ## 最小構成

        backendを選び、`TerminalSession`を開始してから、フォントと`TerminalView`を構築します。所有権フラグを有効にすると、viewの破棄時にsessionとfont setも破棄されます。

        ```csharp
        using Luxel.Terminal.Session;
        using Luxel.Terminal.UI;
        using Luxel.Typography;
        #if LUXEL_WINDOWS
        using Luxel.Terminal.Windows;
        #else
        using Luxel.Terminal.Linux;
        #endif

        #if LUXEL_WINDOWS
        ITerminalPty pty = new WindowsConPty();
        #else
        ITerminalPty pty = new LinuxPty();
        #endif

        var session = new TerminalSession(pty, columns: 120, rows: 32,
                                          scrollbackLimit: 10_000);
        await session.StartAsync(new TerminalLaunchOptions
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Columns = 120,
            Rows = 32,
        });

        VectorFont primary = VectorFont.Load("/path/to/Mono-Regular.ttf");
        var fonts = new TerminalFontSet(primary, ownsFonts: true);
        var terminal = new TerminalView(session, fonts,
            ownsSession: true, ownsFonts: true);
        ```

        shellを自動選択するだけなら`TerminalLaunchOptions.DefaultShell(columns, rows)`も使用できます。起動時には未指定の場合だけ`TERM=xterm-256color`、`COLORTERM=truecolor`、`TERM_PROGRAM=Luxel.Terminal`が補われます。working directoryや追加環境変数は`WorkingDirectory` / `Environment`で渡します。

        ## TerminalViewと文字配置

        `TerminalView`は、各セルの描画boxと調整値を`Luxel.Typography.TwoD`のbox版`AppendText`へ渡します。view自身は`Measure`やascentからglyph位置・baselineを逆算しません。セル幅の既定値`0`はprimary fontの`"0"` advanceから自動計算され、フォント固有の等幅advanceへ追従します。

        ```csharp
        using System.Numerics;
        using Luxel.Typography.TwoD;

        var terminal = new TerminalView(session, fonts,
            ownsSession: true, ownsFonts: true)
        {
            FontSize = 16,
            CellWidth = 0,       // primary fontの "0" advance
            CellHeight = 19,
            GlyphOffset = new Vector2(0, 0),
            GlyphScale = 1,
            GlyphAdvanceScale = 1,
            GlyphHorizontalAlignment = TextBoxHorizontalAlignment.Center,
            GlyphVerticalAlignment = TextBoxVerticalAlignment.Center,
        };
        ```

        - `CellWidth > 0` — 自動値を明示的に上書き
        - `GlyphOffset` — alignment後の描画位置をlogical pixel単位で微調整
        - `GlyphScale` — セルboxを変えずにglyphの見た目だけを一様scale
        - `GlyphAdvanceScale` — 複数glyph clusterのadvanceと自動セル幅を調整
        - wide characterは2セルを占有し、combining markは元のclusterと同じセルで描画

        primary fontとNerd Fontを分ける場合は`new TerminalFontSet(primary, fallbacks, nerdFont)`を使います。PowerlineなどPrivate Use Areaのcode pointではNerd Fontが優先されます。現在のvector font rendererではTTF/TTC outlineを使うため、Nerd FontはCFF/CFF2 OTFではなくTTF版を選んでください。

        ## 折り返し・scrollback・resize

        parserはDEC autowrapのdelayed wrapを実装します。右端へ文字を書いた時点ではcursorを保留し、次の印字文字でsoft wrapします。CR/LFによるhard breakとは別にsoft-wrap metadataを保持するため、selection copyではsoft wrapを改行としてコピーしません。

        viewport幅が変わるとprimary screenとscrollbackをsoft-wrap境界でreflowします。wide characterは途中で分割しません。alternate screenはscrollbackへ混ぜず、現在の画面だけをresizeします。PTYのwindow size更新とbufferのreflowは`TerminalSession.ResizeAsync`の同じ直列command経路で処理されます。

        ## 入力・selection・IME

        `TerminalView`は通常文字、矢印・function key、Ctrl key、pasteを端末sequenceへencodeします。bracketed pasteとapplication cursor modeは現在のterminal stateに従います。pointer drag selectionはscrollbackを含む絶対行座標を使い、`Ctrl+C` / `Ctrl+V`はselectionとclipboardへ接続されます。

        IME preeditはcursor位置のセルboxへoverlay描画し、candidate window用のcaret rectangleも実効セルサイズから返します。確定文字列だけがPTYへ送られます。

        ## 終了処理

        `TerminalLaunchOptions.CloseMode`の既定値は`TerminateTree`です。windowを閉じたときにshellだけでなく、その配下で起動したprocessもcleanupします。`Graceful`はbackend固有の正常終了経路を開始してtimeoutまで待ち、`Detach`はprocessを残す用途です。通常は`TerminalView`と`TerminalSession`を`DisposeAsync`してください。

        ```csharp
        await terminal.DisposeAsync();
        ```

        ## sampleを実行する

        実行可能sampleは`samples/LuxelTerminal`にあります。Galleryのdocs storyは起動時のindex作成やgolden testでもbuildされるため、shell processを自動起動するlive embedにはしていません。

        ```bash
        dotnet run --project samples/LuxelTerminal/LuxelTerminal.csproj -- \
          --shell /bin/bash \
          --font /path/to/YourNerdFontMono-Regular.ttf
        ```

        primary fontとNerd Font fallbackを分ける場合は`--nerd-font`、配置を調整する場合は`--cell-width`、`--cell-height`、`--glyph-offset-x/y`、`--glyph-scale`、`--advance-scale`を指定します。oh-my-poshは通常どおり`.bashrc`や`.zshrc`から初期化してください。

        Linuxの初期support targetはglibc x64です。WindowsではConPTYが利用可能な環境が必要です。Linux Native AOT publish手順を含む詳細なCLI例は`samples/LuxelTerminal/README.md`を参照してください。
        """;
    }
}
