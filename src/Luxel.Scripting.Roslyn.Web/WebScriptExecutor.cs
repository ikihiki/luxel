using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Luxel.UI;

namespace Luxel.Scripting.Roslyn.Web;

public sealed class WebScriptExecutor
{
    private static readonly Regex ScriptLine = new(Regex.Escape(WebScriptCompiler.ScriptFileName) + @":line (\d+)", RegexOptions.Compiled);

    public WebScriptExecution Execute(ReadOnlyMemory<byte> peImage, ReadOnlyMemory<byte> pdbImage = default)
    {
        if (peImage.IsEmpty)
            return Failure("load", "The compiled assembly image is empty.");

        try
        {
            using var pe = new MemoryStream(peImage.ToArray(), writable: false);
            using var pdb = pdbImage.IsEmpty ? null : new MemoryStream(pdbImage.ToArray(), writable: false);
            var loadContext = new AssemblyLoadContext($"LuxelWebScript-{Guid.NewGuid():N}", isCollectible: true);
            loadContext.Resolving += static (_, name) => AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), name));
            Assembly assembly = pdb is null
                ? loadContext.LoadFromStream(pe)
                : loadContext.LoadFromStream(pe, pdb);
            return ExecuteAssembly(assembly);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return RuntimeFailure(exception.InnerException);
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception);
        }
    }

    public WebScriptExecution ExecuteAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            Type? entryType = assembly.GetType(WebScriptCompiler.EntryTypeName, throwOnError: false, ignoreCase: false);
            if (entryType is null)
                return Failure("entry-point", $"Assembly does not contain '{WebScriptCompiler.EntryTypeName}'.");
            if (!typeof(ILuxelWebScriptProgram).IsAssignableFrom(entryType) || entryType.IsAbstract)
                return Failure("entry-point", $"'{WebScriptCompiler.EntryTypeName}' does not implement {nameof(ILuxelWebScriptProgram)}.");
            if (entryType.GetConstructor(Type.EmptyTypes) is null)
                return Failure("entry-point", $"'{WebScriptCompiler.EntryTypeName}' must have a public parameterless constructor.");

            var program = (ILuxelWebScriptProgram)Activator.CreateInstance(entryType)!;
            Widget widget = program.Build();
            return widget is null
                ? Failure("runtime", "The script returned null instead of a Widget.")
                : new WebScriptExecution(true, widget);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return RuntimeFailure(exception.InnerException);
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception);
        }
    }

    private static WebScriptExecution RuntimeFailure(Exception exception)
    {
        Match match = ScriptLine.Match(exception.StackTrace ?? string.Empty);
        int? line = match.Success && int.TryParse(match.Groups[1].Value, out int parsed) ? parsed : null;
        return Failure("runtime", exception.Message, exception.GetType().FullName, line);
    }

    private static WebScriptExecution Failure(string kind, string message, string? exceptionType = null, int? line = null)
        => new(false, Failure: new WebScriptFailure(kind, message, exceptionType, line));
}

public sealed class InProcessWebScriptWorkerController(WebScriptCompiler compiler, WebScriptExecutor executor) : IWebScriptWorkerController
{
    public Task<WebScriptCompilation> CompileAsync(CompileScriptRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(compiler.Compile(request.Source));
    }

    public Task<WebScriptExecution> ExecuteAsync(ExecuteScriptRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executor.Execute(request.PeImage, request.PdbImage ?? []));
    }
}
