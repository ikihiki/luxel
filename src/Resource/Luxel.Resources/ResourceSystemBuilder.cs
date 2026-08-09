using System.Collections.ObjectModel;

namespace Luxel.Resources;

internal sealed record ResourceDomainDescriptor(
    ResourceExecutionDomainId Id,
    ResourceExecutionDomainCapabilities Capabilities,
    Func<ResourceExecutionDomainBuildContext, IResourceExecutionDomain> Factory,
    bool AsyncOnly,
    string? MetricsName);

internal sealed record ResourceSourceDescriptor(
    IResourceSource Source,
    ResourceExecutionDomainHandle Domain,
    ResourceManagerHandle? Manager);

internal sealed record ResourceStepDescriptor(
    string Name,
    Type Input,
    Type Output,
    ResourceExecutionDomainHandle Domain,
    ResourceManagerHandle? Manager,
    ResourceOwnership Ownership,
    string[]? Extensions,
    string[]? FragmentPatterns,
    int Priority,
    Func<object, ResourceUri, LoadContext, Task<object>> Run);

internal sealed record ResourceManagerDescriptor(
    ResourceManagerId Id,
    ResourceExecutionDomainHandle DefaultDomain,
    Func<ResourceManagerBuildContext, IResourceManager> Factory,
    bool IsDefault,
    bool AsyncOnly,
    Func<Type, string?>? ValidateManagedType);

internal sealed class ResourceSystemDefinition
{
    public readonly List<ResourceDomainDescriptor> Domains = [];
    public readonly List<ResourceSourceDescriptor> Sources = [];
    public readonly List<ResourceStepDescriptor> Steps = [];
    public readonly List<ResourceManagerDescriptor> Managers = [];
    public readonly Dictionary<Type, ResourceManagerHandle> ManagerBindings = [];
}

public sealed class ResourceSystemBuilder
{
    private readonly ResourceSystemDefinition _definition = new();
    private int _sealed;

    public ResourceSystemBuilder()
    {
        Domains = new(this);
        Sources = new(this);
        Steps = new(this);
        Managers = new(this);
    }

    public ResourceDomainCollectionBuilder Domains { get; }
    public ResourceSourceCollectionBuilder Sources { get; }
    public ResourceStepCollectionBuilder Steps { get; }
    public ResourceManagerCollectionBuilder Managers { get; }

    internal ResourceSystemDefinition Definition => _definition;
    internal void EnsureMutable()
    {
        if (Volatile.Read(ref _sealed) != 0)
            throw new InvalidOperationException("The ResourceSystemBuilder is sealed and cannot be modified after build begins.");
    }

    public ResourceSystem Build()
    {
        if (_definition.Domains.Any(d => d.AsyncOnly) || _definition.Managers.Any(m => m.AsyncOnly))
            throw new InvalidOperationException("This ResourceSystem configuration contains async-only components. Use BuildAsync().");
        return BuildAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask<ResourceSystem> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _sealed, 1) != 0)
            throw new InvalidOperationException("Build may only be called once for a ResourceSystemBuilder.");

        ResourceBuildValidator.Validate(_definition);

        var managers = new Dictionary<ResourceManagerId, IResourceManager>();
        var domains = new Dictionary<ResourceExecutionDomainId, IResourceExecutionDomain>();
        var initialized = new Stack<IAsyncDisposable>();
        try
        {
            foreach (ResourceManagerDescriptor descriptor in _definition.Managers)
            {
                IResourceManager manager = descriptor.Factory(new(descriptor.Id, descriptor.DefaultDomain));
                if (manager.Id != descriptor.Id)
                    throw new InvalidOperationException($"Manager factory for '{descriptor.Id}' returned manager '{manager.Id}'.");
                managers.Add(descriptor.Id, manager);
                initialized.Push(manager);
            }

            foreach (ResourceDomainDescriptor descriptor in _definition.Domains)
            {
                IResourceExecutionDomain domain = descriptor.Factory(new(descriptor.Id, descriptor.Capabilities));
                if (domain.Id != descriptor.Id)
                    throw new InvalidOperationException($"Domain factory for '{descriptor.Id}' returned domain '{domain.Id}'.");
                domains.Add(descriptor.Id, domain);
                initialized.Push(domain);
            }

            foreach (IResourceManager manager in managers.Values)
                await manager.StartAsync(cancellationToken).ConfigureAwait(false);
            foreach (IResourceExecutionDomain domain in domains.Values)
                await domain.StartAsync(cancellationToken).ConfigureAwait(false);

            var domainTable = new ResourceDomainTable(new ReadOnlyDictionary<ResourceExecutionDomainId, IResourceExecutionDomain>(domains));
            ResourceManagerId? defaultManager = _definition.Managers.SingleOrDefault(m => m.IsDefault)?.Id;
            var managerTable = new ResourceManagerTable(
                new ReadOnlyDictionary<ResourceManagerId, IResourceManager>(managers),
                new ReadOnlyDictionary<Type, ResourceManagerId>(_definition.ManagerBindings.ToDictionary(p => p.Key, p => p.Value.Id)),
                new ReadOnlyDictionary<ResourceManagerId, ResourceExecutionDomainHandle>(_definition.Managers.ToDictionary(m => m.Id, m => m.DefaultDomain)),
                defaultManager);
            var stepTable = new ResourceStepTable(_definition.Sources.ToArray(), _definition.Steps.ToArray());
            return new ResourceSystem(domainTable, stepTable, managerTable);
        }
        catch
        {
            while (initialized.TryPop(out IAsyncDisposable? component))
            {
                try { await component.DisposeAsync().ConfigureAwait(false); }
                catch { }
            }
            throw;
        }
    }
}

public sealed class ResourceDomainCollectionBuilder(ResourceSystemBuilder owner)
{
    public ResourceDomainRegistrationBuilder Add(string id)
    {
        owner.EnsureMutable();
        return new(owner, new ResourceExecutionDomainId(id));
    }
}

public sealed class ResourceDomainRegistrationBuilder
{
    private readonly ResourceSystemBuilder _owner;
    private readonly ResourceExecutionDomainId _id;
    private Func<ResourceExecutionDomainBuildContext, IResourceExecutionDomain>? _factory;
    private ResourceExecutionDomainCapabilities _capabilities = new(Environment.ProcessorCount, ResourceThreadAffinity.AnyThread, ResourceProgressModel.Parallel);
    private bool _asyncOnly;
    private string? _metricsName;

    internal ResourceDomainRegistrationBuilder(ResourceSystemBuilder owner, ResourceExecutionDomainId id) { _owner = owner; _id = id; }

    public ResourceDomainRegistrationBuilder UseThreadPool(int maxConcurrency)
    {
        _capabilities = new(Math.Max(1, maxConcurrency), ResourceThreadAffinity.AnyThread,
            maxConcurrency == 1 ? ResourceProgressModel.Serialized : ResourceProgressModel.Parallel);
        _factory = context => new ThreadPoolResourceExecutionDomain(context.Id, context.Capabilities.MaxConcurrency,
            context.Capabilities.Affinity, context.Capabilities.ProgressModel, context.Capabilities.OperationBudget);
        return this;
    }

    public ResourceDomainRegistrationBuilder UseSerial()
    {
        _capabilities = new(1, ResourceThreadAffinity.AnyThread, ResourceProgressModel.Serialized);
        _factory = context => new SerialResourceExecutionDomain(context.Id);
        return this;
    }

    public ResourceDomainRegistrationBuilder UseDedicatedThread(string? threadName = null)
    {
        _capabilities = new(1, ResourceThreadAffinity.DedicatedThread, ResourceProgressModel.Serialized, true);
        _factory = context => new DedicatedThreadResourceExecutionDomain(context.Id, threadName);
        return this;
    }

    public ResourceDomainRegistrationBuilder UseFactory(
        Func<ResourceExecutionDomainBuildContext, IResourceExecutionDomain> factory,
        ResourceExecutionDomainCapabilities capabilities,
        bool asyncOnly = false)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _capabilities = capabilities;
        _asyncOnly = asyncOnly;
        return this;
    }

    public ResourceDomainRegistrationBuilder WithMetrics(string name) { _metricsName = name; return this; }
    public ResourceDomainRegistrationBuilder WithOperationBudget(TimeSpan budget) { _capabilities = _capabilities with { OperationBudget = budget }; return this; }

    public ResourceExecutionDomainHandle Register()
    {
        _owner.EnsureMutable();
        _owner.Definition.Domains.Add(new(_id, _capabilities,
            _factory ?? (context => new ThreadPoolResourceExecutionDomain(context.Id, context.Capabilities.MaxConcurrency)),
            _asyncOnly, _metricsName));
        return new(_id);
    }
}

public sealed class ResourceSourceCollectionBuilder(ResourceSystemBuilder owner)
{
    public ResourceSourceRegistrationBuilder Add(IResourceSource source)
    {
        owner.EnsureMutable();
        return new(owner, source);
    }
}

public sealed class ResourceSourceRegistrationBuilder
{
    private readonly ResourceSystemBuilder _owner;
    private readonly IResourceSource _source;
    private ResourceExecutionDomainHandle _domain;
    private ResourceManagerHandle? _manager;
    internal ResourceSourceRegistrationBuilder(ResourceSystemBuilder owner, IResourceSource source) { _owner = owner; _source = source ?? throw new ArgumentNullException(nameof(source)); }
    public ResourceSourceRegistrationBuilder RunOn(ResourceExecutionDomainHandle domain) { _domain = domain; return this; }
    public ResourceSourceRegistrationBuilder ManagedBy(ResourceManagerHandle manager) { _manager = manager; return this; }
    public void Register()
    {
        _owner.EnsureMutable();
        _owner.Definition.Sources.Add(new(_source, _domain, _manager));
    }
}

public sealed class ResourceStepCollectionBuilder(ResourceSystemBuilder owner)
{
    public ResourceStepRegistrationBuilder Add(IResourceStep step)
    {
        owner.EnsureMutable();
        return new(owner, step);
    }

    public ResourceStepRegistrationBuilder<TIn, TOut> Add<TIn, TOut>(IResourceStep<TIn, TOut> step)
    {
        owner.EnsureMutable();
        return new(owner, step);
    }
}

public sealed class ResourceStepRegistrationBuilder
{
    private readonly ResourceSystemBuilder _owner;
    private readonly IResourceStep _step;
    private readonly Type _input;
    private readonly Type _output;
    private readonly Func<object, ResourceUri, LoadContext, Task<object>> _run;
    private ResourceExecutionDomainHandle _domain;
    private ResourceManagerHandle? _manager;
    private ResourceOwnership _ownership = ResourceOwnership.Owned;
    private string[]? _extensions;
    private string[]? _fragments;
    private int _priority;

    internal ResourceStepRegistrationBuilder(ResourceSystemBuilder owner, IResourceStep step)
    {
        _owner = owner;
        _step = step ?? throw new ArgumentNullException(nameof(step));
        Type contract = step.GetType().GetInterfaces().SingleOrDefault(type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IResourceStep<,>))
            ?? throw new InvalidOperationException($"{step.GetType()} must implement exactly one IResourceStep<TIn,TOut> contract.");
        Type[] arguments = contract.GetGenericArguments();
        _input = arguments[0];
        _output = arguments[1];
        _extensions = ((IEnumerable<string>?)contract.GetProperty(nameof(IResourceStep<object, object>.Extensions))?.GetValue(step))
            ?.Select(NormalizeExtension).ToArray();
        _fragments = ((IEnumerable<string>?)contract.GetProperty(nameof(IResourceStep<object, object>.FragmentPatterns))?.GetValue(step))?.ToArray();
        _run = (Func<object, ResourceUri, LoadContext, Task<object>>)typeof(ResourceStepRegistrationBuilder)
            .GetMethod(nameof(CreateRun), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(_input, _output).Invoke(null, [step])!;
    }

    public ResourceStepRegistrationBuilder RunOn(ResourceExecutionDomainHandle domain) { _domain = domain; return this; }
    public ResourceStepRegistrationBuilder ManagedBy(ResourceManagerHandle manager) { _manager = manager; return this; }
    public ResourceStepRegistrationBuilder Owned() { _ownership = ResourceOwnership.Owned; return this; }
    public ResourceStepRegistrationBuilder Borrowed() { _ownership = ResourceOwnership.Borrowed; return this; }
    public ResourceStepRegistrationBuilder ForExtensions(params string[] extensions) { _extensions = extensions.Select(NormalizeExtension).ToArray(); return this; }
    public ResourceStepRegistrationBuilder ForFragments(params string[] patterns) { _fragments = patterns.ToArray(); return this; }
    public ResourceStepRegistrationBuilder WithPriority(int priority) { _priority = priority; return this; }
    public void Register()
    {
        _owner.EnsureMutable();
        _owner.Definition.Steps.Add(new(_step.GetType().Name, _input, _output, _domain, _manager, _ownership,
            _extensions, _fragments, _priority, _run));
    }

    private static Func<object, ResourceUri, LoadContext, Task<object>> CreateRun<TIn, TOut>(IResourceStep step)
    {
        var typed = (IResourceStep<TIn, TOut>)step;
        return async (input, uri, context) => (object)(await typed.RunAsync((TIn)input, uri, context).ConfigureAwait(false))!;
    }

    private static string NormalizeExtension(string extension) => extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
}

public sealed class ResourceStepRegistrationBuilder<TIn, TOut>
{
    private readonly ResourceSystemBuilder _owner;
    private readonly IResourceStep<TIn, TOut> _step;
    private ResourceExecutionDomainHandle _domain;
    private ResourceManagerHandle? _manager;
    private ResourceOwnership _ownership = ResourceOwnership.Owned;
    private string[]? _extensions;
    private string[]? _fragmentPatterns;
    private int _priority;

    internal ResourceStepRegistrationBuilder(ResourceSystemBuilder owner, IResourceStep<TIn, TOut> step) { _owner = owner; _step = step ?? throw new ArgumentNullException(nameof(step)); }
    public ResourceStepRegistrationBuilder<TIn, TOut> RunOn(ResourceExecutionDomainHandle domain) { _domain = domain; return this; }
    public ResourceStepRegistrationBuilder<TIn, TOut> ManagedBy(ResourceManagerHandle manager) { _manager = manager; return this; }
    public ResourceStepRegistrationBuilder<TIn, TOut> Owned() { _ownership = ResourceOwnership.Owned; return this; }
    public ResourceStepRegistrationBuilder<TIn, TOut> Borrowed() { _ownership = ResourceOwnership.Borrowed; return this; }
    public ResourceStepRegistrationBuilder<TIn, TOut> ForExtensions(params string[] extensions) { _extensions = extensions.Select(NormalizeExtension).ToArray(); return this; }
    public ResourceStepRegistrationBuilder<TIn, TOut> ForFragments(params string[] patterns) { _fragmentPatterns = patterns.ToArray(); return this; }
    public ResourceStepRegistrationBuilder<TIn, TOut> WithPriority(int priority) { _priority = priority; return this; }

    public void Register()
    {
        _owner.EnsureMutable();
        string[]? extensions = _extensions ?? _step.Extensions?.Select(NormalizeExtension).ToArray();
        string[]? fragments = _fragmentPatterns ?? _step.FragmentPatterns?.ToArray();
        _owner.Definition.Steps.Add(new(_step.GetType().Name, typeof(TIn), typeof(TOut), _domain, _manager, _ownership,
            extensions, fragments, _priority, async (input, uri, context) => (object)(await _step.RunAsync((TIn)input, uri, context).ConfigureAwait(false))!));
    }

    private static string NormalizeExtension(string extension) => extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
}

public sealed class ResourceManagerCollectionBuilder(ResourceSystemBuilder owner)
{
    public ResourceManagerRegistrationBuilder Add(string id)
    {
        owner.EnsureMutable();
        return new(owner, new ResourceManagerId(id));
    }

    public ResourceManagerTypeBindingBuilder<T> Manage<T>()
    {
        owner.EnsureMutable();
        return new(owner);
    }
}

public sealed class ResourceManagerRegistrationBuilder
{
    private readonly ResourceSystemBuilder _owner;
    private readonly ResourceManagerId _id;
    private ResourceExecutionDomainHandle _domain;
    private Func<ResourceManagerBuildContext, IResourceManager>? _factory;
    private Func<Type, string?>? _validateManagedType;
    private bool _default, _asyncOnly;
    internal ResourceManagerRegistrationBuilder(ResourceSystemBuilder owner, ResourceManagerId id) { _owner = owner; _id = id; }
    public ResourceManagerRegistrationBuilder RunOn(ResourceExecutionDomainHandle domain) { _domain = domain; return this; }
    public ResourceManagerRegistrationBuilder Use(Func<ResourceManagerBuildContext, IResourceManager> factory, bool asyncOnly = false) { _factory = factory; _asyncOnly = asyncOnly; return this; }
    public ResourceManagerRegistrationBuilder UseCpu() { _factory = context => new CpuResourceManager(context.Id); return this; }
    public ResourceManagerRegistrationBuilder UseIo() { _factory = context => new IoResourceManager(context.Id); return this; }
    /// <summary>Adds manager-specific build validation for explicitly managed output types.</summary>
    public ResourceManagerRegistrationBuilder ValidateManagedTypes(Func<Type, string?> validator)
    {
        _validateManagedType = validator ?? throw new ArgumentNullException(nameof(validator));
        return this;
    }
    public ResourceManagerRegistrationBuilder AsDefault() { _default = true; return this; }
    public ResourceManagerHandle Register()
    {
        _owner.EnsureMutable();
        _owner.Definition.Managers.Add(new(_id, _domain, _factory ?? (context => new CpuResourceManager(context.Id)), _default, _asyncOnly, _validateManagedType));
        return new(_id);
    }
}

public sealed class ResourceManagerTypeBindingBuilder<T>(ResourceSystemBuilder owner)
{
    private ResourceManagerHandle _manager;
    public ResourceManagerTypeBindingBuilder<T> With(ResourceManagerHandle manager) { _manager = manager; return this; }
    public void Register()
    {
        owner.EnsureMutable();
        if (!owner.Definition.ManagerBindings.TryAdd(typeof(T), _manager))
            throw new InvalidOperationException($"A resource manager binding for exact type '{typeof(T)}' is already registered.");
    }
}

internal static class ResourceBuildValidator
{
    public static void Validate(ResourceSystemDefinition definition)
    {
        var errors = new List<string>();
        ValidateDuplicates(definition.Domains.Select(d => d.Id), "execution domain", errors);
        ValidateDuplicates(definition.Managers.Select(m => m.Id), "resource manager", errors);
        if (definition.Managers.Count(m => m.IsDefault) > 1) errors.Add("More than one default resource manager is registered.");

        var domainIds = definition.Domains.Select(d => d.Id).ToHashSet();
        var managerIds = definition.Managers.Select(m => m.Id).ToHashSet();
        foreach (ResourceManagerDescriptor manager in definition.Managers)
            if (!domainIds.Contains(manager.DefaultDomain.Id)) errors.Add($"Manager '{manager.Id}' references unregistered domain '{manager.DefaultDomain.Id}'.");
        foreach (ResourceSourceDescriptor source in definition.Sources)
        {
            if (!domainIds.Contains(source.Domain.Id)) errors.Add($"Source '{source.Source.GetType().Name}' references unregistered domain '{source.Domain.Id}'.");
            if (source.Manager is null && !definition.ManagerBindings.ContainsKey(typeof(byte[])) && !definition.Managers.Any(m => m.IsDefault))
                errors.Add($"Source '{source.Source.GetType().Name}' output type 'System.Byte[]' has no resource manager binding.");
            if (source.Manager is { } manager && !managerIds.Contains(manager.Id)) errors.Add($"Source '{source.Source.GetType().Name}' references unregistered manager '{manager.Id}'.");
        }
        foreach (ResourceStepDescriptor step in definition.Steps)
        {
            if (!domainIds.Contains(step.Domain.Id)) errors.Add($"Step '{step.Name}' references unregistered domain '{step.Domain.Id}'.");
            if (step.Manager is { } manager)
            {
                if (!managerIds.Contains(manager.Id)) errors.Add($"Step '{step.Name}' references unregistered manager '{manager.Id}'.");
                else ValidateManagedType(definition, manager.Id, step.Output, $"Step '{step.Name}'", errors);
            }
            if (step.Manager is null && !definition.ManagerBindings.ContainsKey(step.Output) && !definition.Managers.Any(m => m.IsDefault))
                errors.Add($"Step '{step.Name}' output type '{step.Output}' has no resource manager binding.");
        }
        foreach ((Type type, ResourceManagerHandle handle) in definition.ManagerBindings)
        {
            if (!managerIds.Contains(handle.Id)) errors.Add($"Type '{type}' references unregistered manager '{handle.Id}'.");
            else ValidateManagedType(definition, handle.Id, type, $"Type '{type}'", errors);
        }

        var schemes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ResourceSourceDescriptor source in definition.Sources)
        foreach (string raw in source.Source.Schemes)
        {
            string scheme = raw.ToLowerInvariant();
            if (!schemes.TryAdd(scheme, source.Source.GetType().Name)) errors.Add($"Source scheme '{scheme}' is registered more than once.");
        }

        foreach (var group in definition.Steps.GroupBy(s => (s.Input, s.Output)))
        {
            ResourceStepDescriptor[] steps = group.ToArray();
            for (int i = 0; i < steps.Length; i++) for (int j = i + 1; j < steps.Length; j++)
                if (Overlaps(steps[i], steps[j]) && steps[i].Priority == steps[j].Priority)
                    errors.Add($"Ambiguous steps '{steps[i].Name}' and '{steps[j].Name}' for {group.Key.Input} -> {group.Key.Output}.");
        }

        DetectCycles(definition.Steps, errors);
        if (errors.Count > 0) throw new InvalidOperationException("ResourceSystem build validation failed:\n - " + string.Join("\n - ", errors.Distinct()));
    }

    private static void ValidateManagedType(ResourceSystemDefinition definition, ResourceManagerId managerId,
        Type type, string owner, List<string> errors)
    {
        ResourceManagerDescriptor descriptor = definition.Managers.Single(manager => manager.Id == managerId);
        string? error = descriptor.ValidateManagedType?.Invoke(type);
        if (!string.IsNullOrWhiteSpace(error)) errors.Add($"{owner} cannot be managed by '{managerId}': {error}");
    }

    private static void ValidateDuplicates<T>(IEnumerable<T> values, string kind, List<string> errors) where T : notnull
    {
        foreach (var group in values.GroupBy(v => v).Where(g => g.Count() > 1)) errors.Add($"Duplicate {kind} id '{group.Key}'.");
    }

    private static bool Overlaps(ResourceStepDescriptor a, ResourceStepDescriptor b)
    {
        bool fragments = a.FragmentPatterns is null && b.FragmentPatterns is null ||
            a.FragmentPatterns is not null && b.FragmentPatterns is not null && a.FragmentPatterns.Intersect(b.FragmentPatterns).Any();
        bool extensions = a.Extensions is null || b.Extensions is null || a.Extensions.Intersect(b.Extensions).Any();
        return fragments && extensions;
    }

    private static void DetectCycles(IEnumerable<ResourceStepDescriptor> steps, List<string> errors)
    {
        var graph = steps.GroupBy(s => s.Output).ToDictionary(g => g.Key, g => g.Select(s => s.Input).Distinct().ToArray());
        var visiting = new HashSet<Type>();
        var visited = new HashSet<Type>();
        bool Visit(Type type)
        {
            if (!visiting.Add(type)) return true;
            if (visited.Contains(type)) { visiting.Remove(type); return false; }
            if (graph.TryGetValue(type, out Type[]? inputs)) foreach (Type input in inputs) if (Visit(input)) return true;
            visiting.Remove(type); visited.Add(type); return false;
        }
        foreach (Type type in graph.Keys) if (Visit(type)) { errors.Add($"Resource step type graph contains a cycle involving '{type}'."); break; }
    }
}
