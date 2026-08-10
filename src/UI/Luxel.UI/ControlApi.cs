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
public sealed record ControlApi(string Namespace, string Name, string Summary, IReadOnlyList<ApiMember> Members);

/// <summary>Gallery-neutral identity for a generated production UI component.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedComponentMetadataAttribute(
    Type componentType,
    string category,
    string factoryNamespace,
    string factoryClass,
    string factoryMethod,
    string summary) : Attribute
{
    public Type ComponentType { get; } = componentType;
    public string Category { get; } = category;
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
    private static readonly Dictionary<string, ControlApi> Apis = new();

    public static void Register(ControlApi api)
    {
        lock (Gate) Apis[api.Name] = api;
    }

    public static ControlApi? Find(string name)
    {
        lock (Gate) return Apis.GetValueOrDefault(name);
    }

    /// <summary>名前順のスナップショット。</summary>
    public static IReadOnlyList<ControlApi> All
    {
        get { lock (Gate) return Apis.Values.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray(); }
    }
}
