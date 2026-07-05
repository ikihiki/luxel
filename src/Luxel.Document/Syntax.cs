namespace Luxel.Document;

/// <summary>シンタックスハイライトのトークン分類 — VS Code (Dark+/Light+) が実際に**色分けしている粒度**に
/// 合わせた 15 種 (色はテーマが決める)。VS Code で同色のもの (parameter/property = variable 等) は統合。</summary>
public enum TokenKind : byte
{
    Text,
    Comment,
    /// <summary>文字列リテラル。</summary>
    String,
    /// <summary>文字列内のエスケープ (\n 等)。</summary>
    Escape,
    /// <summary>正規表現リテラル。</summary>
    Regexp,
    Number,
    /// <summary>言語定数 (true/false/null) と enum メンバ等の定数。</summary>
    Constant,
    /// <summary>宣言系キーワード (var/class/public/int 等 — storage 含む)。</summary>
    Keyword,
    /// <summary>制御フローキーワード (if/for/return/await 等 — VS Code では紫系)。</summary>
    KeywordControl,
    Operator,
    /// <summary>関数/メソッド名 (宣言・呼び出し)。</summary>
    Function,
    /// <summary>型名 (class/struct/enum/interface/namespace)。</summary>
    Type,
    /// <summary>変数/パラメータ/プロパティ (VS Code では同色)。</summary>
    Variable,
    /// <summary>マークアップのタグ名 (HTML/XML)。</summary>
    Tag,
    /// <summary>マークアップの属性名。</summary>
    Attribute,
}

/// <summary>コード文字列内のトークン 1 つ (文字 offset + 長さ)。</summary>
public readonly record struct SyntaxToken(int Start, int Length, TokenKind Kind);

/// <summary>
/// コードブロックのトークナイザ契約。実装は別アセンブリ (TextMateSharp 等) に置き、
/// エディタへ注入する — Document/Controls はライブラリ依存を持たない。
/// <see cref="Tokenize"/> は**ハイライトワーカースレッド (単一)** から呼ばれる:
/// 実装は再入を考慮しなくてよいが、UI スレッドの状態に触れないこと。
/// </summary>
public interface ISyntaxHighlighter
{
    /// <summary>この言語 id (```lang の lang) を扱えるか。</summary>
    bool Supports(string lang);

    /// <summary>コード全文をトークナイズする (開始 offset 昇順・非重複。隙間は Text 扱い)。</summary>
    SyntaxToken[] Tokenize(string lang, string code);
}
