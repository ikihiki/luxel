using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>
/// Declares a reflection-free Storybook-style story for a <see cref="UiComponentAttribute"/> widget.
/// The source generator resolves the component's generated factory and emits the story registration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ComponentStoryAttribute(Type componentType, string path) : Attribute
{
    public Type ComponentType { get; } = componentType;
    public string Path { get; } = path;
    public Type? Factory { get; set; }
    public string? FactoryMethod { get; set; }
    public string? Template { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Theme { get; set; }
    public int Order { get; set; } = 1000;
    public bool RealWindowOnly { get; set; }
    public string? SampleBundle { get; set; }
    public string? RuntimeBundleId { get; set; }
}

/// <summary>
/// Selects one public story arg. <paramref name="member"/> normally names a component
/// <see cref="UiParamAttribute"/>. For synthetic args, set <see cref="Apply"/> to a static
/// <c>void Apply(TComponent, TValue)</c> method on the declaring story class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ComponentArgAttribute(string member, object? defaultValue) : Attribute
{
    public string Member { get; } = member;
    public object? DefaultValue { get; } = defaultValue;
    public string? Name { get; set; }
    public string? Apply { get; set; }
    public string? Description { get; set; }
    public int Order { get; set; } = 1000;
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    public double Step { get; set; } = double.NaN;
}

/// <summary>
/// Reactive host used by generated component stories. Args are declared once on the story context;
/// reads performed by <paramref name="build"/> are tracked by <see cref="CompositeControl"/>, so a
/// changed arg signal invalidates and reconstructs the preview without reflection.
/// </summary>
public sealed class ComponentStoryPreview(Func<Widget> build) : CompositeControl
{
    private readonly Func<Widget> _build = build ?? throw new ArgumentNullException(nameof(build));

    protected override Widget Build() => _build();
}
