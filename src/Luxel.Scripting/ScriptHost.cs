using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Luxel.Scripting;

/// <summary>スクリプト診断 (1 始まりの行/桁 — エディタのインライン表示用)。</summary>
public sealed record ScriptDiagnostic(int Line, int Column, string Message, bool IsError);

/// <summary>1 回の実行結果。コンパイル失敗は <see cref="Diagnostics"/>、実行時例外は
/// <see cref="Exception"/> + 取れれば <see cref="ExceptionLine"/> (スクリプト内の 1 始まり行)。</summary>
public sealed class ScriptResult
{
    public bool Success { get; init; }
    public object? ReturnValue { get; init; }
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; } = [];
    public Exception? Exception { get; init; }
    public int? ExceptionLine { get; init; }
}

/// <summary>
/// C# スクリプト (.csx 意味論 — 最後の式が戻り値、globals のメンバーが裸で見える) のホスト。
/// Roslyn Scripting の薄いラッパで、エンジンへの参照/using はホストアプリが注入する:
/// <code>
/// var host = new ScriptHost(
///     references: [typeof(Widget).Assembly, typeof(Kit).Assembly],
///     usings: ["System", "Luxel.UI", "Luxel.Controls.Kit"],   // 静的クラスも書ける
///     globalsType: typeof(MyGlobals));
/// ScriptResult r = host.Run("Button(_ => Log(\"hi\"), \"OK\")", myGlobals);
/// </code>
/// <list type="bullet">
/// <item>コンパイルは**ソース文字列キー**でキャッシュ — 同一ソースの再 Run は再コンパイルしない</item>
/// <item>デバッグ情報付き emit — 実行時例外からスクリプト行番号を取り出せる</item>
/// <item>決定性の規約: スクリプト内で wall-clock/乱数/await を使わない (snap/E2E と同じ)</item>
/// <item>既知の制限 (v1): 編集ごとのアセンブリはプロセス終了まで残る (開発時ツールの割り切り —
///   アンロードが要件になったら collectible ALC 化)</item>
/// </list>
/// </summary>
public sealed class ScriptHost
{
    private const string FileName = "script.csx";
    private static readonly Regex LineRe = new(@"script\.csx:line (\d+)", RegexOptions.Compiled);

    private readonly ScriptOptions _options;
    private readonly Type _globalsType;
    private readonly object _gate = new();
    private readonly Dictionary<string, Script<object>> _cache = new();

    /// <summary>キャッシュ済みスクリプト数 (テスト/診断用)。</summary>
    public int CachedScripts { get { lock (_gate) return _cache.Count; } }

    public ScriptHost(IEnumerable<Assembly> references, IEnumerable<string> usings, Type globalsType)
    {
        _options = ScriptOptions.Default
            .WithReferences(references.ToArray())
            .WithImports(usings.ToArray())
            .WithEmitDebugInformation(true)         // 実行時例外 → スクリプト行番号のため
            .WithFileEncoding(System.Text.Encoding.UTF8)
            .WithFilePath(FileName);
        _globalsType = globalsType;
    }

    /// <summary>コンパイル診断のみ (エディタのインライン表示用 — 実行しない)。</summary>
    public IReadOnlyList<ScriptDiagnostic> Check(string code)
        => Map(GetScript(code).Compile());

    /// <summary>コンパイル (キャッシュ) して実行する。globals は ctor の globalsType のインスタンス。</summary>
    public ScriptResult Run(string code, object globals)
    {
        Script<object> script = GetScript(code);
        ImmutableArray<Diagnostic> diags = script.Compile();
        IReadOnlyList<ScriptDiagnostic> mapped = Map(diags);
        if (mapped.Any(d => d.IsError))
            return new ScriptResult { Success = false, Diagnostics = mapped };

        try
        {
            // 同期実行 — スクリプトは決定性規約で await を使わない (継続はここで完了する)
            ScriptState<object> state = script.RunAsync(globals).GetAwaiter().GetResult();
            return new ScriptResult { Success = true, ReturnValue = state.ReturnValue, Diagnostics = mapped };
        }
        catch (Exception e)
        {
            return new ScriptResult
            {
                Success = false,
                Diagnostics = mapped,
                Exception = e,
                ExceptionLine = ExtractLine(e),
            };
        }
    }

    private Script<object> GetScript(string code)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(code, out Script<object>? hit)) return hit;
            Script<object> script = CSharpScript.Create<object>(code, _options, _globalsType);
            _cache[code] = script;
            return script;
        }
    }

    private static IReadOnlyList<ScriptDiagnostic> Map(ImmutableArray<Diagnostic> diags)
    {
        var list = new List<ScriptDiagnostic>();
        foreach (Diagnostic d in diags)
        {
            if (d.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)) continue;
            FileLinePositionSpan span = d.Location.GetLineSpan();
            list.Add(new ScriptDiagnostic(
                span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                d.GetMessage(), d.Severity == DiagnosticSeverity.Error));
        }
        return list;
    }

    /// <summary>例外スタックからスクリプト内の行番号を取り出す (デバッグ emit + FilePath 前提)。</summary>
    private static int? ExtractLine(Exception e)
    {
        for (Exception? cur = e; cur is not null; cur = cur.InnerException)
        {
            Match m = LineRe.Match(cur.StackTrace ?? "");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int line)) return line;
        }
        return null;
    }
}
