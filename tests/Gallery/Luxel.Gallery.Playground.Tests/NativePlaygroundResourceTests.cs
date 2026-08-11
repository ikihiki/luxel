using System.Text;
using Luxel.Gallery.Playground;
using Luxel.Graphics;
using Luxel.Resources;
using Luxel.Scripting.Roslyn.Web;
using Luxel.Settings;
using Luxel.Shaders;

namespace Luxel.Gallery.Playground.Tests;

public sealed class NativePlaygroundResourceTests
{
    [Fact]
    public async Task Session_workspace_tracks_add_update_rename_delete_and_has_no_file_source()
    {
        using var session = new NativePlaygroundSession(new InMemoryFileStore(), Template());
        Assert.Equal("original", Encoding.UTF8.GetString(
            await session.ResourceSession.Workspace.ReadAsync("data.txt", default)));

        PlaygroundFile added = session.AddFile("extra.txt", "one", id: "extra");
        session.UpdateFile(added.Id, "two");
        session.RenameFile(added.Id, "renamed.txt");

        Assert.False(session.ResourceSession.Workspace.Exists("extra.txt"));
        Assert.Equal("two", Encoding.UTF8.GetString(
            await session.ResourceSession.Workspace.ReadAsync("renamed.txt", default)));
        Assert.Throws<InvalidOperationException>(() =>
            session.ResourceSession.Load<byte[]>("file://outside.txt"));

        session.DeleteFile(added.Id);
        Assert.False(session.ResourceSession.Workspace.Exists("renamed.txt"));
    }

    [Fact]
    public async Task Native_runner_preloads_slang_and_script_consumes_gpu_shader_code_through_public_facade()
    {
        var compiler = new FakeSlangCompiler();
        PlaygroundTemplate template = Template() with
        {
            Files =
            [
                new PlaygroundFile("main", "Main.csx", "csharp", """
                    var shader = WebScriptResources.Get<GpuShaderCode>("shader.slang");
                    return Kit.Text(shader.Value.Wgsl is not null ? "shader ready" : "shader missing");
                    """),
                new PlaygroundFile("shader", "shader.slang", "slang", "[shader(\"compute\")] void main() {}"),
                new PlaygroundFile("include", "common.slangh", "slang", "struct Common {}"),
                new PlaygroundFile("data", "data.txt", "plaintext", "original"),
            ],
        };
        using var session = new NativePlaygroundSession(
            new InMemoryFileStore(),
            template,
            new NativePlaygroundResourceOptions
            {
                BackendKind = GpuBackendKind.WebGpu,
                SlangCompiler = compiler,
            });

        NativePlaygroundRunResult result = await new NativePlaygroundRunner().RunAsync(session);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.NotNull(result.Widget);
        Assert.Equal("shader.slang", compiler.Source?.Path);
        Assert.Contains("common.slangh", compiler.Source!.SupportingFiles.Keys);
        Assert.Equal(SlangCompileTarget.Wgsl, compiler.Options?.Target);
    }

    [Fact]
    public async Task Slang_workspace_without_gpu_backend_reports_capability_failure_instead_of_fake_results()
    {
        PlaygroundTemplate template = Template() with
        {
            Files =
            [
                new PlaygroundFile("main", "Main.csx", "csharp", "return Kit.Text(\"unused\");"),
                new PlaygroundFile("shader", "shader.slang", "slang", "[shader(\"compute\")] void main() {}"),
            ],
        };
        using var session = new NativePlaygroundSession(new InMemoryFileStore(), template);

        NativePlaygroundRunResult result = await new NativePlaygroundRunner().RunAsync(session);

        Assert.False(result.Success);
        ShaderDiagnostic diagnostic = Assert.Single(result.ShaderDiagnostics!);
        Assert.Equal("SLANG_TOOL_UNAVAILABLE", diagnostic.Code);
        Assert.Contains("no GPU backend context", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_facade_routes_through_scoped_fake_resource_provider()
    {
        var code = new GpuShaderCode { Wgsl = Encoding.UTF8.GetBytes("shader") };
        var provider = new FakeResourceProvider(new WebScriptResource<GpuShaderCode>(
            code,
            new WebScriptResourceMetadata(
                "workspace://shader.slang#compute",
                "shader.slang",
                "compute",
                typeof(GpuShaderCode).FullName!,
                new Dictionary<string, string>())));

        using (WebScriptResources.Push(provider))
        {
            WebScriptResource<GpuShaderCode> loaded = WebScriptResources.Get<GpuShaderCode>("shader.slang");
            Assert.Same(code, loaded.Value);
            Assert.Equal(1, provider.LoadCount);
        }

        Assert.False(WebScriptResources.TryGet<GpuShaderCode>("shader.slang", out _));
    }

    [Fact]
    public async Task Language_service_probe_uses_fake_supervised_process_and_preserves_unavailable_status()
    {
        var process = new FakeLanguageServiceProcess(new SlangLanguageServiceProcessResult(9, "", "failed"));
        var probe = new NativeSlangLanguageServiceProbe(process);
        var discovered = new NativeSlangLanguageServiceCapability(true, "found", "/fake/slangd");

        NativeSlangLanguageServiceCapability result = await probe.ProbeAsync(discovered, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAvailable);
        Assert.Contains("exited with code 9", result.Message, StringComparison.Ordinal);
        Assert.Equal("/fake/slangd", process.Request?.FileName);
        var language = new NativeSlangCodeLanguage(result);
        Assert.Empty(language.Complete("anything", 0));
        Assert.Empty(language.Diagnose("anything"));
        Assert.Null(language.Hover("anything", 0));
    }

    private static PlaygroundTemplate Template() => new(
        "resources",
        "Resources",
        "",
        "Main.csx",
        [
            new PlaygroundFile("main", "Main.csx", "csharp", "return Kit.Text(\"ok\");"),
            new PlaygroundFile("data", "data.txt", "plaintext", "original"),
        ]);

    private sealed class FakeSlangCompiler : ISlangCompiler
    {
        public SlangSource? Source { get; private set; }
        public SlangCompileOptions? Options { get; private set; }

        public Task<SlangCompilation> CompileAsync(
            SlangSource source,
            SlangCompileOptions options,
            CancellationToken cancellationToken = default)
        {
            Source = source;
            Options = options;
            return Task.FromResult(new SlangCompilation(
                SlangCompileTarget.Wgsl,
                options.ProgramKind,
                [new SlangArtifact("main", ShaderEntryPointStage.Compute, Encoding.UTF8.GetBytes("@compute @workgroup_size(1) fn main() {}"))]));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeResourceProvider(WebScriptResource<GpuShaderCode> shader) : IWebScriptResourceProvider
    {
        public int LoadCount { get; private set; }

        public bool TryGet<T>(string name, out WebScriptResource<T>? resource)
        {
            LoadCount++;
            if (typeof(T) == typeof(GpuShaderCode) && name == "shader.slang")
            {
                resource = (WebScriptResource<T>)(object)shader;
                return true;
            }
            resource = null;
            return false;
        }
    }

    private sealed class FakeLanguageServiceProcess(SlangLanguageServiceProcessResult result)
        : ISlangLanguageServiceProcess
    {
        public SlangLanguageServiceProcessRequest? Request { get; private set; }

        public Task<SlangLanguageServiceProcessResult> RunAsync(
            SlangLanguageServiceProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }
}
