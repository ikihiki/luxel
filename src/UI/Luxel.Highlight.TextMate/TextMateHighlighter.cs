using Luxel.Document;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Luxel.Highlight;

/// <summary>
/// TextMate 文法 (VS Code と同じ .tmLanguage、TextMateSharp.Grammars 同梱) による
/// <see cref="ISyntaxHighlighter"/> 実装。scope 名を粗い <see cref="TokenKind"/> へ写像し、
/// 実際の色は Luxel の Theme が決める (VS Code テーマは使わない — 配色の一貫性を守る)。
/// 文法ロード/トークナイズは HighlightQueue の単一ワーカーから呼ばれる前提 (初回ロードも UI を塞がない)。
/// </summary>
public sealed class TextMateHighlighter : ISyntaxHighlighter
{
    public static readonly TextMateHighlighter Instance = new();

    private readonly object _gate = new();
    private RegistryOptions? _options;
    private Registry? _registry;
    private readonly Dictionary<string, IGrammar?> _grammars = new();

    /// <summary>```lang のエイリアス → TextMate 言語 id。</summary>
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "csharp",
        ["csharp"] = "csharp",
        ["js"] = "javascript",
        ["javascript"] = "javascript",
        ["ts"] = "typescript",
        ["typescript"] = "typescript",
        ["json"] = "json",
        ["xml"] = "xml",
        ["html"] = "html",
        ["css"] = "css",
        ["py"] = "python",
        ["python"] = "python",
        ["rs"] = "rust",
        ["rust"] = "rust",
        ["cpp"] = "cpp",
        ["c"] = "c",
        ["hlsl"] = "hlsl",
        ["sh"] = "shellscript",
        ["bash"] = "shellscript",
        ["shell"] = "shellscript",
        ["md"] = "markdown",
        ["markdown"] = "markdown",
        ["yaml"] = "yaml",
        ["yml"] = "yaml",
    };

    public bool Supports(string lang) => Alias.ContainsKey(lang);

    public SyntaxToken[] Tokenize(string lang, string code)
    {
        IGrammar? grammar = GetGrammar(lang);
        if (grammar is null) return [];
        var tokens = new List<SyntaxToken>();
        IStateStack? state = null;
        int offset = 0;
        foreach (string line in code.Split('\n'))
        {
            ITokenizeLineResult r = grammar.TokenizeLine(line, state, TimeSpan.FromMilliseconds(200));
            state = r.RuleStack;
            foreach (IToken t in r.Tokens)
            {
                TokenKind kind = Classify(t.Scopes);
                if (kind == TokenKind.Text) continue;
                int s = offset + t.StartIndex;
                int e = offset + Math.Min(t.EndIndex, line.Length);
                if (e > s) tokens.Add(new SyntaxToken(s, e - s, kind));
            }
            offset += line.Length + 1;   // '\n'
        }
        return [.. tokens];
    }

    /// <summary>scope 名 (後ろほど具体的) → TokenKind。VS Code Dark+/Light+ のテーマ規則と同じ
    /// 対応付け — 具体的な scope を先に判定する (string.regexp を string より先、等)。</summary>
    private static TokenKind Classify(List<string> scopes)
    {
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            string s = scopes[i];
            if (s.StartsWith("comment")) return TokenKind.Comment;
            if (s.StartsWith("constant.character.escape")) return TokenKind.Escape;
            if (s.StartsWith("string.regexp")) return TokenKind.Regexp;
            if (s.StartsWith("string")) return TokenKind.String;
            if (s.StartsWith("constant.numeric")) return TokenKind.Number;
            if (s.StartsWith("constant.language") || s.StartsWith("constant.other")
                || s.StartsWith("variable.other.enummember")) return TokenKind.Constant;
            if (s.StartsWith("keyword.control")) return TokenKind.KeywordControl;
            if (s.StartsWith("keyword.operator")) return TokenKind.Operator;
            if (s.StartsWith("keyword") || s.StartsWith("storage")) return TokenKind.Keyword;
            if (s.StartsWith("entity.name.function") || s.StartsWith("support.function")
                || s.StartsWith("meta.function-call")) return TokenKind.Function;
            if (s.StartsWith("entity.name.type") || s.StartsWith("entity.name.class")
                || s.StartsWith("entity.name.struct") || s.StartsWith("entity.name.enum")
                || s.StartsWith("entity.name.interface") || s.StartsWith("entity.name.namespace")
                || s.StartsWith("support.type") || s.StartsWith("support.class")) return TokenKind.Type;
            if (s.StartsWith("entity.name.tag")) return TokenKind.Tag;
            if (s.StartsWith("entity.other.attribute-name")) return TokenKind.Attribute;
            if (s.StartsWith("variable") || s.StartsWith("support.variable")) return TokenKind.Variable;
        }
        return TokenKind.Text;
    }

    private IGrammar? GetGrammar(string lang)
    {
        if (!Alias.TryGetValue(lang, out string? id)) return null;
        lock (_gate)
        {
            _options ??= new RegistryOptions(ThemeName.LightPlus);   // テーマは未使用 (scope 写像のみ)
            _registry ??= new Registry(_options);
            if (!_grammars.TryGetValue(id, out IGrammar? g))
            {
                string? scope = _options.GetScopeByLanguageId(id);
                g = scope is null ? null : _registry.LoadGrammar(scope);
                _grammars[id] = g;
            }
            return g;
        }
    }
}
