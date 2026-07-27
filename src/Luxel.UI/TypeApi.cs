namespace Luxel.UI;

/// <summary>参照アセンブリの公開型 API をドキュメント化する opt-in。アセンブリに
/// <c>[assembly: GenerateAssemblyApi("Luxel.Graphics.TwoD")]</c> と書くと、ソースジェネレーター
/// (AssemblyApiGenerator) がその名前空間の公開型を XML doc コメントごと
/// <see cref="TypeApiRegistry"/> へ焼き込む — docs の型 API リファレンスが実行時に組み立てる。</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GenerateAssemblyApiAttribute(string ns) : Attribute
{
    /// <summary>対象の名前空間 (参照アセンブリの公開型を走査する)。</summary>
    public string Namespace { get; } = ns;
}

/// <summary>公開型 1 つの API 記述 (コントロール以外の型用 — <see cref="ControlApi"/> の型版)。
/// <see cref="Kind"/> = "class" | "struct" | "interface" | "enum"。メンバーは
/// <see cref="ApiMember"/> を再利用 (Kind = "ctor" | "method" | "prop" | "event" | "field")。</summary>
public sealed record TypeApi(string Namespace, string Name, string Kind, string Summary,
                             IReadOnlyList<ApiMember> Members);

/// <summary>型 API の登録先 (module initializer から Register される)。docs が名前空間で列挙する。</summary>
public static class TypeApiRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, TypeApi> Apis = new();

    public static void Register(TypeApi api)
    {
        lock (Gate) Apis[$"{api.Namespace}.{api.Name}"] = api;
    }

    public static TypeApi? Find(string name)
    {
        lock (Gate)
            return Apis.GetValueOrDefault(name)
                ?? Apis.Values.FirstOrDefault(a => a.Name == name);
    }

    /// <summary>登録されている名前空間 (名前順)。</summary>
    public static IReadOnlyList<string> Namespaces
    {
        get { lock (Gate) return Apis.Values.Select(a => a.Namespace).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(); }
    }

    /// <summary>指定名前空間の型 (名前順)。</summary>
    public static IReadOnlyList<TypeApi> InNamespace(string ns)
    {
        lock (Gate)
            return Apis.Values.Where(a => a.Namespace == ns)
                .OrderBy(a => a.Name, StringComparer.Ordinal).ToArray();
    }
}
