using System.Reflection;

namespace Luxel.Resources;

/// <summary>uri → byte[] のソース (スキーム別)。ユーザーが実装インスタンスを構築して登録。</summary>
public interface IResourceSource
{
    IEnumerable<string> Schemes { get; }
    Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx);
    IReloadToken? Watch(ResourceUri uri, Action onChanged) => null;
}

/// <summary>すべての <see cref="IResourceStep{TIn,TOut}"/> の非ジェネリック marker。
/// 異なる (TIn,TOut) を持つ Step を型安全に 1 つの配列で受け渡すために存在する (メンバなし)。</summary>
public interface IResourceStep { }

/// <summary>TIn → TOut の 1 変換ステップ。ユーザーが実装インスタンスを構築 (必要な依存は ctor 引数で受け取る) して登録。</summary>
public interface IResourceStep<TIn, TOut> : IResourceStep
{
    Executor Executor { get; }
    IEnumerable<string>? Extensions => null;
    /// <summary>fragment 依存の step が担当する fragment パターン (<c>"mesh/*"</c> の様に末尾 <c>*</c> で前方一致)。
    /// null なら fragment 無し URI のみを担当。</summary>
    IEnumerable<string>? FragmentPatterns => null;
    Task<TOut> RunAsync(TIn input, ResourceUri uri, LoadContext ctx);
}

/// <summary>明示フルローダ (一回限りのエスケープハッチ)。</summary>
public delegate Task<T> Loader<T>(LoadContext ctx);

/// <summary>非ジェネリックに正規化したステップ。</summary>
internal sealed class StepAdapter(string name, Type input, Type output, Executor exec, string[]? ext, string[]? fragmentPatterns,
    Func<object, ResourceUri, LoadContext, Task<object>> run)
{
    public string Name { get; } = name;
    public Type Input { get; } = input;
    public Type Output { get; } = output;
    public Executor Executor { get; } = exec;
    public string[]? Extensions { get; } = ext;
    public string[]? FragmentPatterns { get; } = fragmentPatterns;
    public Func<object, ResourceUri, LoadContext, Task<object>> Run { get; } = run;
}

/// <summary>ソース/ステップのレジストリ + 出力型逆引き選択。ユーザーは構築済みインスタンスを渡す。</summary>
internal sealed class Pipeline
{
    private readonly Dictionary<string, IResourceSource> _sources = new();
    private readonly Dictionary<Type, List<StepAdapter>> _byOutput = new();

    public void AddSource(IResourceSource src)
    {
        foreach (string s in src.Schemes) _sources[s.ToLowerInvariant()] = src;
    }

    public void AddStep(IResourceStep step)
    {
        Type stepType = step.GetType();
        Type itf = stepType.GetInterfaces().FirstOrDefault(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResourceStep<,>))
            ?? throw new InvalidOperationException($"{stepType.Name} は IResourceStep<TIn,TOut> を実装していません。");
        Type inT = itf.GetGenericArguments()[0], outT = itf.GetGenericArguments()[1];
        var exec = (Executor)itf.GetProperty("Executor")!.GetValue(step)!;
        var extEnum = (IEnumerable<string>?)itf.GetProperty("Extensions")!.GetValue(step);
        string[]? ext = extEnum?.Select(e => e.ToLowerInvariant()).ToArray();
        var fragEnum = (IEnumerable<string>?)itf.GetProperty("FragmentPatterns")!.GetValue(step);
        string[]? fragPatterns = fragEnum?.ToArray();

        var run = (Func<object, ResourceUri, LoadContext, Task<object>>)typeof(Pipeline)
            .GetMethod(nameof(MakeRun), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(inT, outT).Invoke(null, [step])!;

        var adapter = new StepAdapter(stepType.Name, inT, outT, exec, ext, fragPatterns, run);
        if (!_byOutput.TryGetValue(outT, out var list)) _byOutput[outT] = list = new();
        list.Add(adapter);
    }

    private static Func<object, ResourceUri, LoadContext, Task<object>> MakeRun<TIn, TOut>(IResourceStep step)
    {
        var s = (IResourceStep<TIn, TOut>)step;
        return async (input, uri, ctx) => (object)(await s.RunAsync((TIn)input, uri, ctx))!;
    }

    public IResourceSource? Source(string scheme)
        => _sources.TryGetValue(scheme.ToLowerInvariant(), out var s) ? s : null;

    /// <summary>出力型 + 拡張子 + fragment で 1 ステップ選択。</summary>
    public StepAdapter? Select(Type output, string ext, string fragment)
    {
        if (!_byOutput.TryGetValue(output, out var list) || list.Count == 0) return null;
        List<StepAdapter> candidates;
        if (fragment.Length > 0)
        {
            candidates = list.Where(s => s.FragmentPatterns is { } fp && fp.Any(p => FragmentMatch(p, fragment))).ToList();
        }
        else
        {
            candidates = list.Where(s => s.FragmentPatterns is null).ToList();
        }
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        StepAdapter? generic = null;
        foreach (var s in candidates)
        {
            if (s.Extensions == null) { generic ??= s; continue; }
            if (Array.IndexOf(s.Extensions, ext) >= 0) return s;
        }
        return generic ?? candidates[0];
    }

    private static bool FragmentMatch(string pattern, string fragment)
    {
        if (pattern.EndsWith("/*", StringComparison.Ordinal)) return fragment.StartsWith(pattern[..^1], StringComparison.Ordinal);
        return string.Equals(pattern, fragment, StringComparison.Ordinal);
    }
}
