using System.Text;
using Luxel.Graphics;
using Luxel.Resources;
using Luxel.Shaders;

namespace Luxel.Shaders.Tests;

public sealed class SharedShaderTests
{
    [Fact]
    public void SourceRejectsWorkspaceEscape()
    {
        Assert.Throws<ArgumentException>(() => new SlangSource("../outside.slang", ""));
        Assert.Throws<ArgumentException>(() => new SlangSource("/absolute.slang", ""));
        Assert.Throws<ArgumentException>(() => new SlangSource("main.slang", "", new Dictionary<string, string> { ["a/../b.slang"] = "" }));
    }

    [Fact]
    public void DescriptorIsCanonicalAcrossDictionaryOrder()
    {
        var first = new SlangCompileOptions
        {
            ProgramKind = ShaderProgramKind.Compute,
            Target = SlangCompileTarget.SpirV,
            Defines = new Dictionary<string, string?> { ["Z"] = "2", ["A"] = null },
        };
        var second = new SlangCompileOptions
        {
            ProgramKind = ShaderProgramKind.Compute,
            Target = SlangCompileTarget.SpirV,
            Defines = new Dictionary<string, string?> { ["A"] = null, ["Z"] = "2" },
        };

        Assert.Equal(first.CanonicalDescriptor, second.CanonicalDescriptor);
        Assert.Contains("spirv-profile=spirv_1_6", first.CanonicalDescriptor);
    }

    [Fact]
    public async Task ResourceStepsSelectBackendAndProgramKind()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.Set("shaders/main.slang", Encoding.UTF8.GetBytes("[shader] void vsMain() {}"));
        var compiler = new RecordingCompiler();
        using var resources = new ResourceSystem(
            [new FileSource(fileSystem)],
            [new SlangSourceStep(), new SlangCompileStep(compiler, GpuBackendKind.WebGpu)]);

        using ResourceHandle<GpuShaderCode> handle = resources.Load<GpuShaderCode>("shaders/main.slang#graphics");
        await handle.Ready;

        Assert.Equal(ShaderProgramKind.Graphics, compiler.Options!.ProgramKind);
        Assert.Equal(SlangCompileTarget.Wgsl, compiler.Options.Target);
        Assert.Equal("shaders/main.slang", compiler.Source!.Path);
        Assert.Equal("wgsl", Encoding.UTF8.GetString(handle.Value.Wgsl!));
    }

    private sealed class RecordingCompiler : ISlangCompiler
    {
        public SlangSource? Source { get; private set; }
        public SlangCompileOptions? Options { get; private set; }

        public Task<SlangCompilation> CompileAsync(SlangSource source, SlangCompileOptions options, CancellationToken cancellationToken = default)
        {
            Source = source;
            Options = options;
            return Task.FromResult(new SlangCompilation(
                SlangCompileTarget.Wgsl,
                options.ProgramKind,
                [new SlangArtifact("module", ShaderEntryPointStage.Vertex, Encoding.UTF8.GetBytes("wgsl"))]));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
