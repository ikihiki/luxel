using System.Text;
using Luxel.Resources;
using Luxel.Scripting;

namespace Luxel.Gallery.Playground;

public sealed record PlaygroundFile
{
    public PlaygroundFile(string FileName, string Source)
        : this(Guid.NewGuid().ToString("N"), FileName, InferLanguage(FileName), Source, 0)
    {
    }

    public PlaygroundFile(string Id, string Path, string Language, string Source, long Version = 0)
    {
        this.Id = ValidateId(Id);
        this.Path = PlaygroundWorkspaceValidation.NormalizePath(Path);
        this.Language = ValidateLanguage(Language);
        this.Source = Source ?? throw new ArgumentNullException(nameof(Source));
        if (Version < 0) throw new ArgumentOutOfRangeException(nameof(Version));
        this.Version = Version;
    }

    public string Id { get; init; }
    public string Path { get; init; }
    public string Language { get; init; }
    public string Source { get; init; }
    public long Version { get; init; }

    // Compatibility with the original single-file playground contract.
    public string FileName => Path;

    public void Deconstruct(out string FileName, out string Source)
    {
        FileName = Path;
        Source = this.Source;
    }

    internal PlaygroundFile WithSource(string source) => new(Id, Path, Language, source, checked(Version + 1));
    internal PlaygroundFile WithPath(string path, string? language = null) =>
        new(Id, path, language ?? (Language == InferLanguage(Path) ? InferLanguage(path) : Language), Source, checked(Version + 1));

    public static string InferLanguage(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csx" => "csharp-script",
        ".cs" => "csharp",
        ".slang" or ".slangh" => "slang",
        ".json" => "json",
        ".md" or ".markdown" => "markdown",
        ".xml" => "xml",
        ".html" or ".htm" => "html",
        ".css" => "css",
        ".js" or ".mjs" => "javascript",
        ".ts" => "typescript",
        _ => "plaintext",
    };

    private static string ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }

    private static string ValidateLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return language.Trim().ToLowerInvariant();
    }
}

public sealed record PlaygroundTemplate(
    string Id,
    string Title,
    string Description,
    string MainFileName,
    IReadOnlyList<PlaygroundFile> Files)
{
    public PlaygroundDraft CreateDraft()
    {
        PlaygroundWorkspaceValidation.ValidateFiles(Files);
        PlaygroundFile main = Files.Single(file =>
            string.Equals(file.Path, PlaygroundWorkspaceValidation.NormalizePath(MainFileName), StringComparison.OrdinalIgnoreCase));
        return new PlaygroundDraft(Id, Title, main.Id, main.Id, Files.Select(file => file with { }).ToArray(), 0);
    }
}

public sealed record PlaygroundDraft
{
    public PlaygroundDraft(
        string TemplateId,
        string Title,
        string MainFileName,
        IReadOnlyList<PlaygroundFile> Files)
        : this(
            TemplateId,
            Title,
            FindByPath(Files, MainFileName).Id,
            FindByPath(Files, MainFileName).Id,
            Files,
            0)
    {
    }

    public PlaygroundDraft(
        string TemplateId,
        string Title,
        string MainFileId,
        string SelectedFileId,
        IReadOnlyList<PlaygroundFile> Files,
        long Revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TemplateId);
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(MainFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SelectedFileId);
        if (Revision < 0) throw new ArgumentOutOfRangeException(nameof(Revision));
        PlaygroundWorkspaceValidation.ValidateFiles(Files);
        if (!Files.Any(file => file.Id == MainFileId))
            throw new ArgumentException("The main file must exist in the workspace.", nameof(MainFileId));
        if (!Files.Any(file => file.Id == SelectedFileId))
            throw new ArgumentException("The selected file must exist in the workspace.", nameof(SelectedFileId));

        this.TemplateId = TemplateId;
        this.Title = Title;
        this.MainFileId = MainFileId;
        this.SelectedFileId = SelectedFileId;
        this.Files = Files.ToArray();
        this.Revision = Revision;
    }

    public string TemplateId { get; init; }
    public string Title { get; init; }
    public string MainFileId { get; init; }
    public string SelectedFileId { get; init; }
    public IReadOnlyList<PlaygroundFile> Files { get; init; }
    public long Revision { get; init; }

    public PlaygroundFile MainFile => Files.Single(file => file.Id == MainFileId);
    public PlaygroundFile SelectedFile => Files.Single(file => file.Id == SelectedFileId);
    public string MainFileName => MainFile.Path;

    public PlaygroundDraft AddFile(
        string path,
        string source = "",
        string? language = null,
        string? id = null,
        long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        string normalized = PlaygroundWorkspaceValidation.NormalizePath(path);
        EnsurePathAvailable(normalized);
        var file = new PlaygroundFile(id ?? Guid.NewGuid().ToString("N"), normalized,
            language ?? PlaygroundFile.InferLanguage(normalized), source, 0);
        return Next(Files.Append(file).ToArray(), SelectedFileId);
    }

    public PlaygroundDraft UpdateFile(string fileNameOrId, string source, long? expectedRevision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameOrId);
        ArgumentNullException.ThrowIfNull(source);
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        return Next(Files.Select(file => file.Id == target.Id ? file.WithSource(source) : file).ToArray(), SelectedFileId);
    }

    public PlaygroundDraft RenameFile(
        string fileNameOrId,
        string newPath,
        string? language = null,
        long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        string normalized = PlaygroundWorkspaceValidation.NormalizePath(newPath);
        EnsurePathAvailable(normalized, target.Id);
        return Next(Files.Select(file => file.Id == target.Id ? file.WithPath(normalized, language) : file).ToArray(), SelectedFileId);
    }

    public PlaygroundDraft DeleteFile(string fileNameOrId, long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        if (target.Id == MainFileId)
            throw new InvalidOperationException("The main file cannot be deleted.");
        if (Files.Count == 1)
            throw new InvalidOperationException("A workspace must contain at least one file.");
        PlaygroundFile[] remaining = Files.Where(file => file.Id != target.Id).ToArray();
        string selected = SelectedFileId == target.Id ? MainFileId : SelectedFileId;
        return Next(remaining, selected);
    }

    public PlaygroundDraft SelectFile(string fileNameOrId, long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        if (target.Id == SelectedFileId) return this;
        return Next(Files, target.Id);
    }

    private PlaygroundDraft Next(IReadOnlyList<PlaygroundFile> files, string selectedFileId) =>
        new(TemplateId, Title, MainFileId, selectedFileId, files, checked(Revision + 1));

    private PlaygroundFile Find(string fileNameOrId)
    {
        PlaygroundFile? byId = Files.SingleOrDefault(file => file.Id == fileNameOrId);
        if (byId is not null) return byId;
        string path = PlaygroundWorkspaceValidation.NormalizePath(fileNameOrId);
        return Files.SingleOrDefault(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"The draft does not contain '{fileNameOrId}'.", nameof(fileNameOrId));
    }

    private void EnsurePathAvailable(string path, string? exceptId = null)
    {
        if (Files.Any(file => file.Id != exceptId && string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"The draft already contains '{path}'.", nameof(path));
    }

    private void EnsureRevision(long? expectedRevision)
    {
        if (expectedRevision is { } expected && expected != Revision)
            throw new StalePlaygroundRevisionException(expected, Revision);
    }

    private static PlaygroundFile FindByPath(IReadOnlyList<PlaygroundFile> files, string path)
    {
        ArgumentNullException.ThrowIfNull(files);
        string normalized = PlaygroundWorkspaceValidation.NormalizePath(path);
        return files.SingleOrDefault(file => string.Equals(file.Path, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"The draft does not contain its main file '{path}'.", nameof(path));
    }
}

public sealed class StalePlaygroundRevisionException(long expectedRevision, long actualRevision)
    : InvalidOperationException($"Workspace revision {expectedRevision} is stale; current revision is {actualRevision}.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}

public static class PlaygroundWorkspaceValidation
{
    public static string NormalizePath(string path) => WorkspacePath.Normalize(path);

    public static void ValidateFiles(IReadOnlyList<PlaygroundFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
            throw new ArgumentException("A playground workspace must contain at least one file.", nameof(files));
        if (files.Count > WorkspaceLimits.MaxFileCount)
            throw new ArgumentException($"A playground workspace cannot contain more than {WorkspaceLimits.MaxFileCount} files.", nameof(files));
        if (files.Any(file => file is null))
            throw new ArgumentException("Workspace files cannot be null.", nameof(files));
        if (files.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new ArgumentException("Playground file IDs must be unique.", nameof(files));
        if (files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new ArgumentException("Playground file paths must be unique ignoring case.", nameof(files));

        long totalSourceBytes = 0;
        foreach (PlaygroundFile file in files)
        {
            int sourceBytes = Encoding.UTF8.GetByteCount(file.Source);
            if ((file.Language is "csharp" or "csharp-script") && sourceBytes > WorkspaceLimits.MaxCSharpFileBytes)
                throw new ArgumentException($"C# file '{file.Path}' exceeds the {WorkspaceLimits.MaxCSharpFileBytes} byte limit.", nameof(files));
            totalSourceBytes += sourceBytes;
            if (totalSourceBytes > WorkspaceLimits.MaxTotalSourceBytes)
                throw new ArgumentException($"Workspace source exceeds the {WorkspaceLimits.MaxTotalSourceBytes} byte limit.", nameof(files));
        }
    }
}

public static class PlaygroundContract
{
    public const string StoryPath = "Examples/Scripting/Playground";
}

public static class PlaygroundTemplates
{
    public static PlaygroundTemplate Button { get; } = new(
        Id: "button",
        Title: "Button",
        Description: "A minimal button playground that records a click in the output log.",
        MainFileName: "Button.csx",
        Files:
        [
            new PlaygroundFile("button-entry", "Button.csx", "csharp-script", """
                // Return a real Luxel Widget. Click it to write to the Output panel.
                var label = "Click me";
                return Kit.Button(_ => Log("Button clicked."), label);
                """),
        ]);

    public static PlaygroundTemplate SlangCube { get; } = new(
        Id: "slang-cube",
        Title: "3D Slang Cube",
        Description: "A rotating depth-tested cube rendered by an editable Slang graphics shader.",
        MainFileName: "Cube.csx",
        Files:
        [
            new PlaygroundFile("slang-cube-entry", "Cube.csx", "csharp-script", """
                // The host compiles the workspace Slang file before this script runs.
                var shader = WebScriptResources.Get<GpuShaderCode>("Shaders/cube.slang");
                Log($"Loaded {shader.Metadata.Path} as {shader.Metadata.Properties["target"]}.");
                return Kit.GpuView(320, 320, new SlangCubeScene(shader.Value), animated: true);
                """),
            new PlaygroundFile("slang-cube-scene", "SlangCubeScene.cs", "csharp", """
                using System;
                using System.Runtime.InteropServices;
                using Luxel.Controls;
                using Luxel.Graphics;

                public sealed class SlangCubeScene : IGpuScene
                {
                    [StructLayout(LayoutKind.Sequential)]
                    private struct Vertex
                    {
                        public float X, Y, Z, W;
                        public float R, G, B, A;
                    }

                    [StructLayout(LayoutKind.Sequential)]
                    private struct DrawArgs
                    {
                        public uint VertexBufferIndex;
                        public float Time;
                        public float Aspect;
                        public float Padding;
                    }

                    private readonly GpuShaderCode _shader;
                    private GpuDevice? _device;
                    private GpuTexture? _color;
                    private GpuTexture? _depth;
                    private GpuBuffer? _vertices;
                    private GpuBuffer? _output;
                    private GpuPipeline? _pipeline;
                    private uint _width, _height;

                    public SlangCubeScene(GpuShaderCode shader) => _shader = shader;

                    public void Init(GpuDevice device, int width, int height)
                    {
                        _device = device;
                        _width = (uint)width;
                        _height = (uint)height;
                        _color = device.CreateRenderTarget(_width, _height, GpuFormat.Rgba8Unorm);
                        _depth = device.CreateDepthTarget(_width, _height);
                        Vertex[] vertices = BuildCube();
                        _vertices = device.Malloc((ulong)vertices.Length * 32u, GpuMemoryKind.HostMapped);
                        vertices.CopyTo(_vertices.Span<Vertex>(vertices.Length));
                        _output = device.Malloc(_width * _height * 4u, GpuMemoryKind.HostMapped);
                        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
                        raster.DepthTest = true;
                        raster.DepthWrite = true;
                        raster.CullMode = GpuCullMode.Back;
                        _pipeline = device.CreateGraphicsPipeline(_shader, raster);
                    }

                    public (int BindlessIndex, int StridePixels) Render(float time)
                    {
                        var args = new DrawArgs
                        {
                            VertexBufferIndex = _vertices!.BindlessIndex,
                            Time = time,
                            Aspect = (float)_width / _height,
                        };
                        using GpuCommandBuffer command = _device!.MainQueue.StartCommandRecording();
                        command.BeginRendering(_color!, _depth, 0.025f, 0.04f, 0.08f, 1f)
                            .SetGraphicsPipeline(_pipeline!)
                            .SetRootArguments(args)
                            .Draw(36)
                            .EndRendering()
                            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                            .CopyTextureToBuffer(_color!, _output!);
                        command.Finish();
                        _device.MainQueue.Submit(command);
                        return ((int)_output!.BindlessIndex, (int)_width);
                    }

                    private static Vertex[] BuildCube()
                    {
                        var vertices = new Vertex[36];
                        int index = 0;
                        void Face((float X, float Y, float Z) a, (float X, float Y, float Z) b,
                                  (float X, float Y, float Z) c, (float X, float Y, float Z) d,
                                  (float R, float G, float B) color)
                        {
                            Add(a); Add(b); Add(c); Add(a); Add(c); Add(d);
                            void Add((float X, float Y, float Z) p) => vertices[index++] = new Vertex
                            {
                                X = p.X, Y = p.Y, Z = p.Z, W = 1,
                                R = color.R, G = color.G, B = color.B, A = 1,
                            };
                        }

                        Face((-1,-1, 1), ( 1,-1, 1), ( 1, 1, 1), (-1, 1, 1), (0.20f,0.55f,1.00f));
                        Face(( 1,-1,-1), (-1,-1,-1), (-1, 1,-1), ( 1, 1,-1), (1.00f,0.30f,0.45f));
                        Face((-1,-1,-1), (-1,-1, 1), (-1, 1, 1), (-1, 1,-1), (0.35f,0.90f,0.55f));
                        Face(( 1,-1, 1), ( 1,-1,-1), ( 1, 1,-1), ( 1, 1, 1), (1.00f,0.70f,0.20f));
                        Face((-1, 1, 1), ( 1, 1, 1), ( 1, 1,-1), (-1, 1,-1), (0.75f,0.40f,1.00f));
                        Face((-1,-1,-1), ( 1,-1,-1), ( 1,-1, 1), (-1,-1, 1), (0.20f,0.85f,0.95f));
                        return vertices;
                    }

                    public void Dispose()
                    {
                        _pipeline?.Dispose(); _pipeline = null;
                        _output?.Dispose(); _output = null;
                        _vertices?.Dispose(); _vertices = null;
                        _depth?.Dispose(); _depth = null;
                        _color?.Dispose(); _color = null;
                        _device = null;
                    }
                }
                """),
            new PlaygroundFile("slang-cube-shader", "Shaders/cube.slang", "slang", """
                [[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];

                struct DrawArgs
                {
                    uint vertexBufferIndex;
                    float time;
                    float aspect;
                    float padding;
                };
                [[vk::push_constant]] DrawArgs g_args;

                struct Vertex
                {
                    float4 position;
                    float4 color;
                };

                struct VSOut
                {
                    float4 position : SV_Position;
                    float4 color : COLOR0;
                    float shade : TEXCOORD0;
                };

                [shader("vertex")]
                VSOut vsMain(uint vertexId : SV_VertexID)
                {
                    Vertex vertex = g_buffers[g_args.vertexBufferIndex].Load<Vertex>(vertexId * 32);
                    float angle = g_args.time * 0.8;
                    float sy = sin(angle), cy = cos(angle);
                    float sx = sin(angle * 0.7), cx = cos(angle * 0.7);
                    float3 p = vertex.position.xyz;
                    p = float3(cy * p.x + sy * p.z, p.y, -sy * p.x + cy * p.z);
                    p = float3(p.x, cx * p.y - sx * p.z, sx * p.y + cx * p.z);
                    p.z += 4.2;

                    VSOut output;
                    output.position = float4(p.x * 1.8 / g_args.aspect, p.y * 1.8, p.z - 0.1, p.z);
                    output.color = vertex.color;
                    output.shade = saturate(0.45 + 0.12 * p.x + 0.10 * p.y);
                    return output;
                }

                [shader("pixel")]
                float4 psMain(VSOut input) : SV_Target
                {
                    return float4(input.color.rgb * input.shade, 1.0);
                }
                """),
        ]);

    public static IReadOnlyList<PlaygroundTemplate> All { get; } = [Button, SlangCube];
}

public enum PlaygroundStatus
{
    Idle,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

public sealed record PlaygroundState
{
    public required PlaygroundDraft Draft { get; init; }
    public PlaygroundStatus Status { get; init; } = PlaygroundStatus.Idle;
    public long ExecutionId { get; init; }
    public ScriptExecutionResult? Result { get; init; }
    public ScriptExecutionResult? LastSuccessfulResult { get; init; }
    public string? LastSuccessfulPreview => LastSuccessfulResult?.ReturnValue;
    public bool CanRun => Status != PlaygroundStatus.Running;
    public bool CanCancel => Status == PlaygroundStatus.Running;
    public string StatusText => Status switch
    {
        PlaygroundStatus.Idle => "Ready",
        PlaygroundStatus.Running => "Running",
        PlaygroundStatus.Succeeded => "Succeeded",
        PlaygroundStatus.Canceled => "Canceled",
        _ => Result?.Outcome switch
        {
            ScriptExecutionOutcome.CompilationFailed => "Compilation failed",
            ScriptExecutionOutcome.RuntimeFailed => "Runtime failed",
            ScriptExecutionOutcome.InvalidRequest => "Invalid request",
            ScriptExecutionOutcome.TimedOut => "Timed out",
            _ => "Failed",
        },
    };
}
