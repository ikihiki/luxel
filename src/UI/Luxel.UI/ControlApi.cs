namespace Luxel.UI;

/// <summary>Gallery-neutral raw Markdown fragment contract.</summary>
public interface IMarkdownFragment
{
    string Markdown { get; }
}

/// <summary>Gallery-neutral structured Markdown widget embed contract.</summary>
public interface IMarkdownEmbed
{
    Widget? Widget { get; }
    string Kind { get; }
    string? Reference { get; }
    bool Inline { get; }
    bool IncludeInherited { get; }
    Func<Widget>? WidgetFactory { get; }
}

/// <summary>コントロール API の 1 メンバー (autodocs の ArgTypes 相当)。
/// <see cref="Kind"/> = "ctor" (ファクトリ/コンストラクタ引数) | "event" ([UiEvent]) | "param" ([UiParam])。</summary>
public sealed record ApiMember(string Name, string Type, string Kind, string Description,
                               bool Stateable = false, bool Inherited = false);

/// <summary>コントロール 1 つの API 記述 (クラスの XML doc summary + メンバー一覧)。
/// ソースジェネレーターが [UiComponent] から /// コメントごと焼き込む — reflection なし。</summary>
public sealed record ControlApi(string Namespace, string Name, string Summary, IReadOnlyList<ApiMember> Members)
{
    /// <summary>Fully-qualified CLR identity used as the registry primary key.</summary>
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
}

/// <summary>Gallery-neutral identity for a generated production UI component.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedComponentMetadataAttribute(
    Type componentType,
    string assemblyOwner,
    string controlName,
    string factoryNamespace,
    string factoryClass,
    string factoryMethod,
    string summary) : Attribute
{
    public Type ComponentType { get; } = componentType;
    public string AssemblyOwner { get; } = assemblyOwner;
    public string ControlName { get; } = controlName;
    public string FactoryNamespace { get; } = factoryNamespace;
    public string FactoryClass { get; } = factoryClass;
    public string FactoryMethod { get; } = factoryMethod;
    public string Summary { get; } = summary;
}

/// <summary>Gallery-neutral generated metadata for one component parameter.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedComponentParameterMetadataAttribute(
    Type componentType,
    string name,
    Type valueType,
    string kind,
    string typeHint,
    bool own,
    string summary,
    int sequence) : Attribute
{
    public Type ComponentType { get; } = componentType;
    public string Name { get; } = name;
    public Type ValueType { get; } = valueType;
    public string Kind { get; } = kind;
    public string TypeHint { get; } = typeHint;
    public bool Own { get; } = own;
    public string Summary { get; } = summary;
    public int Sequence { get; } = sequence;
}

/// <summary>Gallery-neutral generated metadata for one component event.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedComponentEventMetadataAttribute(
    Type componentType,
    string name,
    Type[] argumentTypes,
    bool own,
    string summary,
    int sequence) : Attribute
{
    public Type ComponentType { get; } = componentType;
    public string Name { get; } = name;
    public Type[] ArgumentTypes { get; } = argumentTypes;
    public bool Own { get; } = own;
    public string Summary { get; } = summary;
    public int Sequence { get; } = sequence;
}

/// <summary>全アセンブリのコントロール API 登録先 (module initializer から Register される)。
/// docs の ApiTable が名前で引く。</summary>
public static class ControlApiRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ControlApi> Apis = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Localized = new(StringComparer.Ordinal);

    public static void Register(ControlApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        lock (Gate)
        {
            if (!Localized.Contains(api.FullName)) Apis[api.FullName] = api;
        }
    }

    /// <summary>Registers Gallery-localized metadata with precedence independent of module initializer order.</summary>
    public static void RegisterLocalized(ControlApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        lock (Gate)
        {
            Apis[api.FullName] = api;
            Localized.Add(api.FullName);
        }
    }

    public static ControlApi? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (Gate)
        {
            if (Apis.TryGetValue(name, out ControlApi? exact)) return exact;
            ControlApi? match = null;
            foreach (ControlApi api in Apis.Values)
            {
                if (!string.Equals(api.Name, name, StringComparison.Ordinal)) continue;
                if (match is not null) return null;
                match = api;
            }
            return match;
        }
    }

    /// <summary>Fully-qualified name order snapshot.</summary>
    public static IReadOnlyList<ControlApi> All
    {
        get { lock (Gate) return Apis.Values.OrderBy(a => a.FullName, StringComparer.Ordinal).ToArray(); }
    }
}
