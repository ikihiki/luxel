using System.Text;
using System.Threading.Channels;
using Luxel.Graphics.TwoD;
using Luxel.Typography.TwoD;
using Luxel.Terminal.Input;
using Luxel.Terminal.Screen;
using Luxel.Terminal.Session;
using Luxel.Terminal.UI;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Terminal.UI.Tests;

public sealed class TerminalUiTests
{
    [Fact]
    public void GlyphResolver_RecognizesNerdFontPrivateUsePlanes()
    {
        Assert.True(GlyphResolver.IsNerdFontCodePoint(0xE0B0));
        Assert.True(GlyphResolver.IsNerdFontCodePoint(0xF0001));
        Assert.True(GlyphResolver.IsNerdFontCodePoint(0x100001));
        Assert.False(GlyphResolver.IsNerdFontCodePoint('A'));
        Assert.False(GlyphResolver.IsNerdFontCodePoint(0x1F600));
    }

    [Theory]
    [InlineData("\u0301", 0)]
    [InlineData("A", 1)]
    [InlineData("界", 2)]
    [InlineData("\uE0B0", 1)]
    public void TerminalCells_HaveZeroOneOrTwoCellWidths(string text, int expected)
    {
        Rune rune = text.EnumerateRunes().Single();
        Assert.Equal(expected, TerminalCellWidth.GetWidth(rune));
    }

    [Fact]
    public void Viewport_CombinesScrollbackAndScreenAtStableOffsets()
    {
        TerminalSnapshot snapshot = Snapshot(["old1", "old2"], ["now1", "now2"]);
        Assert.Equal(["now1", "now2"], TerminalViewport.VisibleLines(snapshot, 2, 0).Select(Text));
        Assert.Equal(["old2", "now1"], TerminalViewport.VisibleLines(snapshot, 2, 1).Select(Text));
        Assert.Equal(["old1", "old2"], TerminalViewport.VisibleLines(snapshot, 2, 20).Select(Text));
    }

    [Fact]
    public void Selection_UsesAbsoluteHistoryCoordinatesAndSkipsWideContinuation()
    {
        TerminalSnapshot snapshot = Snapshot([], ["A界B", "next"]);
        var selection = new TerminalSelection(new TerminalPoint(0, 1), new TerminalPoint(1, 2));
        Assert.Equal("界B\nne", TerminalViewport.ExtractSelection(snapshot, selection));
    }

    [Fact]
    public void SelectionCopyOmitsNewlineAcrossSoftWrapButKeepsHardBreak()
    {
        TerminalSnapshot soft = Snapshot([], ["abcd", "ef"] ) with { LineWraps = [true, false] };
        var selection = new TerminalSelection(new TerminalPoint(0, 0), new TerminalPoint(1, 2));
        Assert.Equal("abcdef", TerminalViewport.ExtractSelection(soft, selection));

        TerminalSnapshot hard = soft with { LineWraps = [false, false] };
        Assert.Equal($"abcd{Environment.NewLine}ef", TerminalViewport.ExtractSelection(hard, selection));
    }

    [Fact]
    public void Palette_ResolvesAnsiCubeGrayAndTrueColor()
    {
        var palette = new TerminalPalette();
        Assert.NotEqual(palette.Foreground, palette.Resolve(TerminalColor.Indexed(1), true));
        Assert.Equal(0xFF0000FFu, palette.Resolve(TerminalColor.Rgb(255, 0, 0), true));
        Assert.NotEqual(palette.Resolve(TerminalColor.Indexed(16), true), palette.Resolve(TerminalColor.Indexed(231), true));
        Assert.NotEqual(palette.Resolve(TerminalColor.Indexed(232), true), palette.Resolve(TerminalColor.Indexed(255), true));
    }

    [Fact]
    public void BoxTextDrawing_AppliesAlignmentAndOffsetInsideDrawingApi()
    {
        using VectorFont font = LoadTestFont();
        var baseline = new Scene2D();
        var adjusted = new Scene2D();
        var box = new TextRect(10, 20, 40, 24);
        font.AppendText(baseline, "A", box, 16, 0xffffffff, new TextDrawOptions
        {
            HorizontalAlignment = TextBoxHorizontalAlignment.Center,
            VerticalAlignment = TextBoxVerticalAlignment.Center,
        });
        font.AppendText(adjusted, "A", box, 16, 0xffffffff, new TextDrawOptions
        {
            HorizontalAlignment = TextBoxHorizontalAlignment.Center,
            VerticalAlignment = TextBoxVerticalAlignment.Center,
            Offset = new System.Numerics.Vector2(2, 3),
        });

        System.Numerics.Vector2[] first = baseline.ExportContours().SelectMany(static points => points).ToArray();
        System.Numerics.Vector2[] second = adjusted.ExportContours().SelectMany(static points => points).ToArray();
        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i].X + 2, second[i].X, 3);
            Assert.Equal(first[i].Y + 3, second[i].Y, 3);
        }
    }

    [Fact]
    public async Task TerminalView_AutoCellWidthUsesPrimaryAdvanceAndSupportsFineAdjustment()
    {
        var pty = new FakePty();
        await using var session = new TerminalSession(pty, 4, 2);
        using VectorFont font = LoadTestFont();
        using var fonts = new TerminalFontSet(font);
        await using var view = new TerminalView(session, fonts) { FontSize = 16, CellWidth = 0 };
        float natural = font.Measure("0", 16).width;
        Assert.Equal(natural, view.ResolveCellWidth(), 3);
        view.GlyphAdvanceScale = 0.9f;
        Assert.Equal(natural * 0.9f, view.ResolveCellWidth(), 3);
        view.CellWidth = 10;
        Assert.Equal(10, view.ResolveCellWidth());
    }

    [Fact]
    public async Task TerminalView_RendersFixedCellsSelectionDecorationsAndImeOverlay()
    {
        var pty = new FakePty();
        await using var session = new TerminalSession(pty, 4, 2);
        using VectorFont font = LoadTestFont();
        using var fonts = new TerminalFontSet(font);
        await using var view = new TerminalView(session, fonts) { CellWidth = 10, CellHeight = 20, FontSize = 16 };
        view.Layout(Constraints.Tight(new Size(40, 40)), new LayoutContext { Font = font });
        view.SetComposition(new ImeComposition("編", 1, 0, 1));
        TerminalSnapshot snapshot = Snapshot([], ["A界", "B"]);

        var scene = view.Render(snapshot);

        Assert.True(scene.CountEncoded().Paths >= 4); // background, cursor, glyphs and IME decoration
        Assert.Equal(new Size(40, 40), view.Size);
    }

    [Fact]
    public async Task TerminalView_EncodesKeysTextPasteAndResize()
    {
        var pty = new FakePty();
        await using var session = new TerminalSession(pty, 4, 2);
        await session.StartAsync(new TerminalLaunchOptions { FileName = "fake", Columns = 4, Rows = 2 });
        using VectorFont font = LoadTestFont();
        using var fonts = new TerminalFontSet(font);
        await using var view = new TerminalView(session, fonts, new MemoryClipboard("paste"));

        Assert.True(view.HandleKey(new KeyEvent(Key.Up)));
        view.HandleText("x");
        view.Paste();
        await view.ResizeViewportAsync(10, 5);

        Assert.Equal("\x1b[A", Encoding.UTF8.GetString(await pty.ReadWriteAsync()));
        Assert.Equal("x", Encoding.UTF8.GetString(await pty.ReadWriteAsync()));
        Assert.Equal("paste", Encoding.UTF8.GetString(await pty.ReadWriteAsync()));
        Assert.Equal((10, 5), await pty.ReadResizeAsync());
    }

    [Fact]
    public async Task TerminalView_CancellationStopsPendingInputOnDispose()
    {
        var pty = new FakePty();
        await using var session = new TerminalSession(pty, 4, 2);
        await session.StartAsync(new TerminalLaunchOptions { FileName = "fake", Columns = 4, Rows = 2 });
        using VectorFont font = LoadTestFont();
        using var fonts = new TerminalFontSet(font);
        var view = new TerminalView(session, fonts);
        view.Dispose();
        Assert.False(view.HandleKey(new KeyEvent(Key.Enter)));
    }

    private static VectorFont LoadTestFont()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../assets/fonts/BIZUDGothic-Regular.ttf"));
        return File.Exists(path) ? VectorFont.Load(path) : VectorFont.LoadSystem();
    }

    private static TerminalSnapshot Snapshot(string[] history, string[] screen)
    {
        int columns = Math.Max(1, history.Concat(screen).Select(s => s.EnumerateRunes().Sum(r => Math.Max(1, TerminalCellWidth.GetWidth(r)))).DefaultIfEmpty(1).Max());
        IReadOnlyList<TerminalCell> Make(string value)
        {
            var cells = Enumerable.Range(0, columns).Select(_ => new TerminalCell()).ToArray();
            int column = 0;
            foreach (Rune rune in value.EnumerateRunes())
            {
                int width = Math.Max(1, TerminalCellWidth.GetWidth(rune));
                cells[column].Text = rune.ToString(); cells[column].Width = width;
                if (width == 2 && column + 1 < cells.Length)
                {
                    cells[column + 1].Text = string.Empty; cells[column + 1].Width = 0; cells[column + 1].Continuation = true;
                }
                column += width;
            }
            return cells;
        }
        return new TerminalSnapshot(columns, screen.Length, screen.Select(Make).ToArray(), history.Select(Make).ToArray(),
            new TerminalCursor(0, 0), 1, false, null, false);
    }

    private static string Text(IReadOnlyList<TerminalCell> line)
        => string.Concat(line.Where(c => !c.Continuation).Select(c => c.Text)).TrimEnd();

    private sealed class MemoryClipboard(string? text) : ITerminalClipboard
    {
        public string? GetText() => text;
        public void SetText(string value) => text = value;
    }

    private sealed class FakePty : ITerminalPty
    {
        private readonly Channel<byte[]> _writes = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<(int, int)> _resizes = Channel.CreateUnbounded<(int, int)>();
        private readonly TaskCompletionSource<TerminalExitStatus> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => _writes.Writer.WriteAsync(buffer.ToArray(), cancellationToken);
        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
            => _resizes.Writer.WriteAsync((columns, rows), cancellationToken);
        public Task<TerminalExitStatus> WaitForExitAsync(CancellationToken cancellationToken = default)
            => _exit.Task.WaitAsync(cancellationToken);
        public Task CloseAsync(TerminalCloseMode mode, TimeSpan timeout, CancellationToken cancellationToken = default)
        { _exit.TrySetResult(new TerminalExitStatus(0, true)); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { _exit.TrySetResult(new TerminalExitStatus(0, true)); return ValueTask.CompletedTask; }
        public ValueTask<byte[]> ReadWriteAsync() => _writes.Reader.ReadAsync();
        public ValueTask<(int, int)> ReadResizeAsync() => _resizes.Reader.ReadAsync();
    }
}
