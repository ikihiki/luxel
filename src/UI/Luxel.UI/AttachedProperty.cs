namespace Luxel.UI;

/// <summary>
/// 親レイアウト等が子 Widget に付与する型付き添付プロパティ。
/// <see cref="Id"/> は tooling/serialization 用の安定 ID、実行時の識別はインスタンスで行う。
/// </summary>
public sealed class AttachedProperty<T>
{
    private readonly Func<T, bool>? _validate;

    private AttachedProperty(string id, Type? ownerType, T defaultValue, Func<T, bool>? validate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        OwnerType = ownerType;
        DefaultValue = defaultValue;
        _validate = validate;
    }

    /// <summary>Stable tooling/serialization identifier.</summary>
    public string Id { get; }
    /// <summary>Layout/control type that defines and consumes this metadata, if declared.</summary>
    public Type? OwnerType { get; }
    /// <summary>The value type exposed for non-generic tooling.</summary>
    public Type ValueType => typeof(T);
    public T DefaultValue { get; }

    public static AttachedProperty<T> Create(string id, T defaultValue = default!, Func<T, bool>? validate = null)
        => new(id, ownerType: null, defaultValue, validate);

    public static AttachedProperty<T> Create<TOwner>(string id, T defaultValue = default!, Func<T, bool>? validate = null)
        => new(id, typeof(TOwner), defaultValue, validate);

    internal void Validate(T value)
    {
        if (_validate is not null && !_validate(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Invalid value for attached property '{Id}'.");
    }

    public override string ToString() => Id;
}
