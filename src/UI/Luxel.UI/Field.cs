namespace Luxel.UI;

/// <summary>
/// フォームフィールド (signals)。値 signal + バリデータ → エラー computed。値変更で自動再評価。
/// </summary>
public sealed class Field<T>
{
    private readonly Func<T, string?> _validate;
    public Signal<T> Value { get; }
    public Signal<bool> Touched { get; } = new(false);
    public Computed<string?> Error { get; }

    public Field(T initial, Func<T, string?> validate)
    {
        _validate = validate;
        Value = new Signal<T>(initial);
        Error = new Computed<string?>(() => _validate(Value.Value));
    }

    public bool IsValid => _validate(Value.Value) == null;
    public void Touch() => Touched.Value = true;
}
