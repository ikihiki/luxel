using System.Runtime.CompilerServices;

namespace Luxel.UI;

/// <summary>Declares the widget target of a utility factory for compile-time validation.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class UtilityTargetAttribute(Type targetType) : Attribute
{
    public Type TargetType { get; } = targetType;
}

public enum UtilityKind
{
    Property,
    Layout,
    Attached,
    State,
    Transition,
    ControlSpecific,
    Custom,
}

/// <summary>Widget factory の <c>utilities: [...]</c> に渡す型付き Utility descriptor。</summary>
public readonly struct U
{
    private readonly Action<Widget, WidgetState>? _apply;

    private U(string name, UtilityKind kind, Type targetType, Type? valueType, Action<Widget, WidgetState> apply)
    {
        Name = name;
        Kind = kind;
        TargetType = targetType;
        ValueType = valueType;
        _apply = apply;
    }

    /// <summary>Stable property/utility name used by diagnostics and tooling.</summary>
    public string? Name { get; }
    public UtilityKind Kind { get; }
    /// <summary>Widget type accepted by this descriptor.</summary>
    public Type? TargetType { get; }
    /// <summary>Assigned value type when the descriptor represents a property or attached value.</summary>
    public Type? ValueType { get; }

    public static U Property<TTarget, TValue>(string name, Bindable<TValue> value, UtilityKind kind = UtilityKind.Property)
        where TTarget : Widget
    {
        if (kind == UtilityKind.Layout && value.IsReactive)
            throw new InvalidOperationException(
                $"Layout utility '{name}' cannot use a reactive value until layout invalidation is supported.");
        return new(name, kind, typeof(TTarget), typeof(TValue), (target, state) =>
        {
            if (!target.SetProp(name, state, value))
                throw new InvalidOperationException($"Utility '{name}' is not supported by {target.GetType().FullName}.");
        });
    }

    public static U Attached<TValue>(AttachedProperty<TValue> property, TValue value)
        => new(property.Id, UtilityKind.Attached, typeof(Widget), typeof(TValue),
            (target, _) => target.SetAttached(property, value));

    public static U Custom<TTarget>(string name, UtilityKind kind, Action<TTarget, WidgetState> apply)
        where TTarget : Widget
        => new(name, kind, typeof(TTarget), valueType: null,
            (target, state) => apply((TTarget)target, state));

    public static U State(WidgetState state, Utilities utilities)
        => new(state.ToString(), UtilityKind.State, typeof(Widget), valueType: null,
            (target, _) => utilities.ApplyTo(target, state));

    public void ApplyTo(Widget target, WidgetState state = WidgetState.Default)
    {
        if (_apply is null) return;
        if (TargetType is not null && !TargetType.IsInstanceOfType(target))
            throw new InvalidOperationException($"Utility '{Name}' targets {TargetType.FullName}, not {target.GetType().FullName}.");
        if (state != WidgetState.Default && Kind is UtilityKind.Layout or UtilityKind.Attached)
            throw new InvalidOperationException(
                $"Utility '{Name}' ({Kind}) cannot target state '{state}' until state-driven layout invalidation is supported.");
        _apply(target, state);
    }
}

[CollectionBuilder(typeof(UtilitiesBuilder), nameof(UtilitiesBuilder.Create))]
public readonly struct Utilities : IReadOnlyList<U>
{
    private readonly U[]? _items;

    internal Utilities(U[] items) => _items = items;

    public int Count => _items?.Length ?? 0;
    public U this[int index] => (_items ?? Array.Empty<U>())[index];

    public void ApplyTo(Widget target, WidgetState state = WidgetState.Default)
    {
        if (_items is null) return;
        foreach (U utility in _items) utility.ApplyTo(target, state);
    }

    public IEnumerator<U> GetEnumerator() => ((IEnumerable<U>)(_items ?? Array.Empty<U>())).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class UtilitiesBuilder
{
    public static Utilities Create(ReadOnlySpan<U> items) => new(items.ToArray());
}

/// <summary>Luxel.UI が提供する全 Widget 共通 Utility。</summary>
public static class CoreUtilityExtensions
{
    extension(U)
    {
        public static U Width(Length value) => Width((Bindable<Length>)value);
        public static U Width(Bindable<Length> value) => U.Property<Widget, Length>("Width", value, UtilityKind.Layout);
        public static U Height(Length value) => Height((Bindable<Length>)value);
        public static U Height(Bindable<Length> value) => U.Property<Widget, Length>("Height", value, UtilityKind.Layout);
        public static U Margin(Thickness value) => Margin((Bindable<Thickness>)value);
        public static U Margin(Bindable<Thickness> value) => U.Property<Widget, Thickness>("Margin", value, UtilityKind.Layout);
        public static U TranslateX(Bindable<float> value) => U.Property<Widget, float>("TranslateX", value);
        public static U TranslateY(Bindable<float> value) => U.Property<Widget, float>("TranslateY", value);
        public static U ScaleX(Bindable<float> value) => U.Property<Widget, float>("ScaleX", value);
        public static U ScaleY(Bindable<float> value) => U.Property<Widget, float>("ScaleY", value);
        public static U Rotate(Bindable<float> value) => U.Property<Widget, float>("Rotate", value);
        public static U When(WidgetState state, Utilities utilities) => U.State(state, utilities);
        public static U Hover(Utilities utilities) => U.State(WidgetState.Hover, utilities);
        public static U Pressed(Utilities utilities) => U.State(WidgetState.Pressed, utilities);
        public static U Focused(Utilities utilities) => U.State(WidgetState.Focused, utilities);
        public static U Disabled(Utilities utilities) => U.State(WidgetState.Disabled, utilities);
    }
}
