namespace Luxel.UI;

/// <summary>コントロール API の 1 メンバー (autodocs の ArgTypes 相当)。
/// <see cref="Kind"/> = "ctor" (ファクトリ/コンストラクタ引数) | "event" ([UiEvent]) | "param" ([UiParam])。</summary>
public sealed record ApiMember(string Name, string Type, string Kind, string Description,
                               bool Stateable = false, bool Inherited = false);

/// <summary>コントロール 1 つの API 記述 (クラスの XML doc summary + メンバー一覧)。
/// ソースジェネレーターが [UiComponent] から /// コメントごと焼き込む — reflection なし。</summary>
public sealed record ControlApi(string Namespace, string Name, string Summary, IReadOnlyList<ApiMember> Members);

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
