using System.Numerics;
using Luxel.Terminal.Session;
using Luxel.Terminal.UI;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Framework.UI;
#if LUXEL_WINDOWS
using Luxel.Terminal.Windows;
#else
using Luxel.Terminal.Linux;
#endif

TerminalSampleOptions sample = TerminalSampleOptions.Parse(args);
ITerminalPty pty = CreatePty();
var session = new TerminalSession(pty, sample.Columns, sample.Rows, sample.Scrollback);
TerminalLaunchOptions launch = new()
{
    FileName = sample.Shell,
    Arguments = sample.ShellArguments,
    WorkingDirectory = sample.WorkingDirectory,
    Columns = sample.Columns,
    Rows = sample.Rows,
};
await session.StartAsync(launch);

VectorFont primary = LoadTerminalFont(sample.FontPath);
VectorFont? nerd = sample.NerdFontPath is null || Path.GetFullPath(sample.NerdFontPath) == Path.GetFullPath(sample.FontPath ?? "")
    ? null : VectorFont.Load(sample.NerdFontPath);
var fonts = new TerminalFontSet(primary, nerdFont: nerd, ownsFonts: true);
var terminal = new TerminalView(session, fonts, ownsSession: true, ownsFonts: true)
{
    CellWidth = sample.CellWidth,
    CellHeight = sample.CellHeight,
    GlyphOffset = new Vector2(sample.GlyphOffsetX, sample.GlyphOffsetY),
    GlyphScale = sample.GlyphScale,
    GlyphAdvanceScale = sample.GlyphAdvanceScale,
    FontSize = sample.FontSize,
};

var app = new LuxelAppOptions
{
    Title = "Luxel.Terminal",
    Width = sample.Width,
    Height = sample.Height,
    Theme = Theme.Dark,
};
try { LuxelApp.Run(() => terminal, app); }
finally { await terminal.DisposeAsync(); }

static ITerminalPty CreatePty()
{
#if LUXEL_WINDOWS
    return new WindowsConPty();
#else
    return new LinuxPty();
#endif
}

static VectorFont LoadTerminalFont(string? configured)
{
    if (!string.IsNullOrWhiteSpace(configured)) return VectorFont.Load(configured);
    string bundled = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "BIZUDGothic-Regular.ttf");
    if (File.Exists(bundled)) return VectorFont.Load(bundled);
    return VectorFont.LoadSystem();
}

file sealed record TerminalSampleOptions
{
    public required string Shell { get; init; }
    public IReadOnlyList<string> ShellArguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public string? FontPath { get; init; }
    public string? NerdFontPath { get; init; }
    public int Columns { get; init; } = 120;
    public int Rows { get; init; } = 32;
    public int Scrollback { get; init; } = 10_000;
    public int Width { get; init; } = 1100;
    public int Height { get; init; } = 700;
    public float FontSize { get; init; } = 16;
    public float CellWidth { get; init; }
    public float CellHeight { get; init; } = 19;
    public float GlyphOffsetX { get; init; }
    public float GlyphOffsetY { get; init; }
    public float GlyphScale { get; init; } = 1;
    public float GlyphAdvanceScale { get; init; } = 1;

    public static TerminalSampleOptions Parse(string[] args)
    {
        string? Value(string name) => args.Select((v, i) => (v, i)).FirstOrDefault(x => x.v == name && x.i + 1 < args.Length) is var hit && hit.v is not null ? args[hit.i + 1] : null;
        int Int(string name, int fallback) => int.TryParse(Value(name), out int v) && v > 0 ? v : fallback;
        float PositiveFloat(string name, float fallback) => float.TryParse(Value(name), out float v) && v > 0 ? v : fallback;
        float AnyFloat(string name, float fallback) => float.TryParse(Value(name), out float v) && float.IsFinite(v) ? v : fallback;
        float NonNegativeFloat(string name, float fallback) => float.TryParse(Value(name), out float v) && float.IsFinite(v) && v >= 0 ? v : fallback;
        string shell = Value("--shell") ?? (OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
            : Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh");
        var shellArguments = new List<string>();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--arg") shellArguments.Add(args[++i]);
        return new TerminalSampleOptions
        {
            Shell = shell,
            ShellArguments = shellArguments,
            WorkingDirectory = Value("--cwd"),
            FontPath = Value("--font") ?? Environment.GetEnvironmentVariable("LUXEL_TERMINAL_FONT"),
            NerdFontPath = Value("--nerd-font") ?? Environment.GetEnvironmentVariable("LUXEL_TERMINAL_NERD_FONT"),
            Columns = Int("--columns", 120), Rows = Int("--rows", 32), Scrollback = Int("--scrollback", 10_000),
            Width = Int("--width", 1100), Height = Int("--height", 700), FontSize = PositiveFloat("--font-size", 16),
            CellWidth = NonNegativeFloat("--cell-width", 0), CellHeight = PositiveFloat("--cell-height", 19),
            GlyphOffsetX = AnyFloat("--glyph-offset-x", 0), GlyphOffsetY = AnyFloat("--glyph-offset-y", 0),
            GlyphScale = PositiveFloat("--glyph-scale", 1), GlyphAdvanceScale = PositiveFloat("--advance-scale", 1),
        };
    }
}
