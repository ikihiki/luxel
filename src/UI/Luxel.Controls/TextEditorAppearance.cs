using Luxel.Document;

namespace Luxel.Controls;

/// <summary>文書ブロックの見た目。null の項目は文書形式側の既定値を使う。</summary>
public sealed record TextEditorBlockAppearance(
    float? FontSize = null,
    float? FontScale = null,
    FontVariant? FontVariant = null,
    uint? Foreground = null,
    uint? Background = null,
    uint? Accent = null,
    float? Indent = null,
    float? BarWidth = null);

/// <summary>
/// <see cref="TextEditorView"/> へ一括で渡す外観設定。Editor 共通の基準値と、文書形式が公開する
/// ブロック種別キーごとの上書きを保持する。インスタンスは不変で、変更は <see cref="WithBlock"/> が新しい値を返す。
/// </summary>
public sealed class TextEditorAppearance
{
    public static readonly TextEditorAppearance Default = new();

    private readonly IReadOnlyDictionary<string, TextEditorBlockAppearance> _blocks;

    public TextEditorAppearance(float? fontSize = 16f, float? lineHeight = null, float? wrapLineHeight = null,
        IEnumerable<KeyValuePair<string, TextEditorBlockAppearance>>? blocks = null)
    {
        FontSize = fontSize;
        LineHeight = lineHeight;
        WrapLineHeight = wrapLineHeight;
        _blocks = blocks is null
            ? new Dictionary<string, TextEditorBlockAppearance>(StringComparer.Ordinal)
            : new Dictionary<string, TextEditorBlockAppearance>(blocks, StringComparer.Ordinal);
    }

    /// <summary>Editor 全体の基準フォントサイズ。null はテーマまたは個別パラメータを使う。</summary>
    public float? FontSize { get; }
    /// <summary>ブロック間を含む通常行送り倍率。null は Editor の既定値を使う。</summary>
    public float? LineHeight { get; }
    /// <summary>折返した段落内の行送り倍率。null は View の設定を使う。</summary>
    public float? WrapLineHeight { get; }
    /// <summary>登録済みのブロック外観。</summary>
    public IReadOnlyDictionary<string, TextEditorBlockAppearance> Blocks => _blocks;

    public TextEditorBlockAppearance? Block(string kind)
        => _blocks.TryGetValue(kind, out TextEditorBlockAppearance? value) ? value : null;

    public TextEditorAppearance WithBlock(string kind, TextEditorBlockAppearance appearance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(appearance);
        var blocks = new Dictionary<string, TextEditorBlockAppearance>(_blocks, StringComparer.Ordinal)
        {
            [kind] = appearance,
        };
        return new TextEditorAppearance(FontSize, LineHeight, WrapLineHeight, blocks);
    }
}
