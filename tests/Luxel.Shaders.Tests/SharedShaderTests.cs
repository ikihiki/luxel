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

    [Fact]
    public void WgslNormalizerAppliesGraphicsResourceAbi()
    {
        const string source = """
            var<uniform> g_args_0 : DrawArgs_std430_0;
            @binding(0) @group(0) var g_buffers_0 : array<array<u32>>;
            fn load(index: u32, offset: u32) -> u32 {
                return g_buffers_0[index][offset];
            }
            """;

        string normalized = WgslNormalizer.Normalize(source, ShaderProgramKind.Graphics);

        Assert.Contains("@group(0) @binding(1) var<uniform> g_args_0", normalized);
        Assert.Contains("@group(0) @binding(0) var<storage, read> g_buffers_0 : array<u32>;", normalized);
        Assert.Contains("g_buffers_0[(((index) * 64u) + (offset))]", normalized);
    }

    [Fact]
    public void WgslNormalizerMakesComputeArenaWritable()
    {
        const string source = "@binding(0) @group(0) var g_buffers_0 : array<array<u32>>;";

        string normalized = WgslNormalizer.Normalize(source, ShaderProgramKind.Compute);

        Assert.Contains("var<storage, read_write> g_buffers_0", normalized);
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
