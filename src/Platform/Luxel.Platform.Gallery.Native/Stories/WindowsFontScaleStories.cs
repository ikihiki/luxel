using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>Windows標準フォントの小サイズ可読性を実描画で比較するNative専用Story。</summary>
[StoryMeta("Examples/RealWindow/Typography")]
public static class WindowsFontScaleStories
{
    private sealed record Face(string Name, string FileName, VectorFont Font);

    private static readonly Lazy<IReadOnlyList<Face>> Faces = new(LoadFaces);

    [Story(RealWindowOnly = true)]
    public static StoryResult WindowsFontScale(StoryContext ctx)
    {
        IReadOnlyList<Face> faces = Faces.Value;
        if (faces.Count == 0)
            return Text("読み込めるWindowsシステムフォントがありません。", 16,
                color: Bind.From(() => UiTheme.T.Text));

        Signal<int> selected = ctx.Signal("font", 0, "比較するWindowsシステムフォント");
        Signal<float> baseSize = ctx.Signal("baseSize", 16f, "比較の基準サイズ（8〜32px）");
        return new FontScalePage(faces, selected, baseSize);
    }

    private static IReadOnlyList<Face> LoadFaces()
    {
        string directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        (string Name, string File)[] candidates =
        [
            ("Segoe UI", "segoeui.ttf"),
            ("Meiryo", "meiryo.ttc"),
            ("Yu Gothic", "YuGothM.ttc"),
            ("BIZ UDGothic", "BIZ-UDGothicR.ttc"),
            ("Arial", "arial.ttf"),
            ("Consolas", "consola.ttf"),
        ];
        var result = new List<Face>();
        foreach ((string name, string file) in candidates)
        {
            string path = Path.Combine(directory, file);
            if (!File.Exists(path)) continue;
            try { result.Add(new Face(name, file, VectorFont.Load(path))); }
            catch (NotSupportedException) { }
        }
        return result;
    }

    private sealed class FontScalePage(
        IReadOnlyList<Face> faces,
        Signal<int> selected,
        Signal<float> baseSize) : CompositeControl
    {
        private const string Sample = "Hamburgefonts 0123456789  日本語の小さい文字を確認  あいうえお  漢字  !?.,:;()[]{}";
        private Luxel.Controls.Select? _fontSelect;

        protected override Widget Build()
        {
            Face face = faces[Math.Clamp(selected.Value, 0, faces.Count - 1)];
            float basis = Math.Clamp(baseSize.Value, 8f, 32f);
            float[] sizes =
            [
                MathF.Max(8, basis - 6), MathF.Max(8, basis - 4), MathF.Max(8, basis - 2), basis,
                MathF.Min(48, basis + 2), MathF.Min(48, basis + 4), MathF.Min(48, basis + 8),
                MathF.Min(48, basis + 16),
            ];
            sizes = sizes.Distinct().Order().ToArray();

            uint textColor = UiTheme.T.Text;
            uint mutedColor = UiTheme.T.TextMuted;
            var rows = new List<Widget>(sizes.Length);
            foreach (float size in sizes)
            {
                var sample = RichTextView(
                    [new TextSpan(Sample, new SpanStyle { Font = face.Font, Size = size, Color = textColor })],
                    fontSize: size, width: 690, wrap: TextWrap.Word, lineHeight: 1.25f);
                sample.Fonts = new FontCollection(face.Font);
                rows.Add(Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), padding: new Thickness(10, 7),
                    rounded: 5, width: 760)[VStack(4)[
                        Text(size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " px", 12, color: mutedColor),
                        sample]]);
            }

            return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(18),
                width: 800, height: 480)[VStack(10)[
                    Text("Windows font scale", 24, color: textColor),
                    Text(face.Name + "  ·  " + face.FileName + "  ·  基準 "
                        + basis.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " px", 13, color: mutedColor),
                    HStack(12)[
                        _fontSelect ??= Select(faces.Select(item => item.Name).ToArray(), selected),
                        Slider(baseSize, min: 8f, max: 32f, width: 320)],
                    Scroll(350, width: 764)[VStack(7)[rows.ToArray()]]]];
        }
    }
}
