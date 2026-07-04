using System.Text;
using System.Text.Json;
using Luxel.Resources;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Gltf;

/// <summary>
/// <see cref="Luxel.Resources.ResourceSystem"/> 統合用 step。
/// .gltf/.glb ファイルの byte[] を受け取り、<see cref="AssetDocument"/> に変換。
/// 外部 .bin / 画像は <see cref="LoadContext.Load{U}(string)"/> で自動キャッシュ取得 (Resources の dependency graph に組込まれる)。
/// </summary>
public sealed class GltfStep : IResourceStep<byte[], AssetDocument>
{
    /// <summary>CPU executor で実行 (パースのみ)。</summary>
    public Executor Executor => Executor.Cpu;
    /// <summary>処理対象の拡張子。</summary>
    public IEnumerable<string> Extensions => new[] { ".gltf", ".glb" };

    /// <summary>byte[] をパースし <see cref="AssetDocument"/> に変換。外部参照は <c>ctx.Load</c> で解決。</summary>
    public async Task<AssetDocument> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
    {
        string path = uri.Path;
        bool isGlb = path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

        // .gltf JSON or .glb chunk
        GltfJson json;
        byte[]? embeddedBin = null;
        if (isGlb)
        {
            (json, embeddedBin) = GlbReader.ReadFromBytes(input);
        }
        else
        {
            var jsonText = Encoding.UTF8.GetString(input);
            json = JsonSerializer.Deserialize<GltfJson>(jsonText, JsonOpts)
                   ?? throw new InvalidDataException("invalid glTF JSON");
        }

        // Buffers: data:URI / 埋込み / 外部 → 外部参照は ctx.Load<byte[]> で Resources 経由
        var buffers = new List<byte[]>();
        if (json.buffers is not null)
        {
            string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? "";
            foreach (var b in json.buffers)
            {
                if (b.uri is null)
                {
                    if (embeddedBin is null) throw new InvalidDataException("buffer uri null but no embedded .glb");
                    buffers.Add(embeddedBin);
                }
                else if (b.uri.StartsWith("data:"))
                {
                    int comma = b.uri.IndexOf(',');
                    buffers.Add(Convert.FromBase64String(b.uri[(comma + 1)..]));
                }
                else
                {
                    // ★ Resources 経由で取得 (同一 uri は cache 共有、ファイル変更で reload 伝播)
                    string subUri = System.IO.Path.Combine(baseDir, Uri.UnescapeDataString(b.uri));
                    var handle = ctx.Load<byte[]>("file:///" + subUri.Replace('\\', '/'));
                    await handle.Ready.ConfigureAwait(false);
                    buffers.Add(handle.Value);
                }
            }
        }

        // Images: data:URI / 埋込み bufferView / 外部ファイル
        var imageBytes = new List<byte[]>();
        if (json.images is not null)
        {
            string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? "";
            foreach (var img in json.images)
            {
                byte[] bytes;
                if (img.uri is not null)
                {
                    if (img.uri.StartsWith("data:"))
                    {
                        int comma = img.uri.IndexOf(',');
                        bytes = Convert.FromBase64String(img.uri[(comma + 1)..]);
                    }
                    else
                    {
                        string subUri = System.IO.Path.Combine(baseDir, Uri.UnescapeDataString(img.uri));
                        var handle = ctx.Load<byte[]>("file:///" + subUri.Replace('\\', '/'));
                        await handle.Ready.ConfigureAwait(false);
                        bytes = handle.Value;
                    }
                }
                else if (img.bufferView is int bv)
                {
                    var view = json.bufferViews![bv];
                    var buf = buffers[view.buffer];
                    int off = view.byteOffset ?? 0;
                    bytes = buf.AsSpan(off, view.byteLength).ToArray();
                }
                else bytes = Array.Empty<byte>();
                imageBytes.Add(bytes);
            }
        }

        return GltfLoader.BuildAssetDocument(json, buffers, imageBytes, sourceFormat: isGlb ? "glb" : "gltf");
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>
/// <see cref="AssetDocument"/> を <see cref="SceneAssets"/> (GpuBuffer 確保済み) に build。
/// <see cref="GpuDevice"/> と <see cref="Luxel.Ecs.World"/> をコンストラクタで受け取る。
/// </summary>
public sealed class SceneAssetsStep(GpuDevice device, Luxel.Ecs.World world) : IResourceStep<AssetDocument, SceneAssets>
{
    private readonly GpuDevice _device = device;
    private readonly Luxel.Ecs.World _world = world;
    /// <summary>GPU executor で実行 (buffer/texture 確保のため)。</summary>
    public Executor Executor => Executor.Gpu;

    /// <summary>SceneBuilder で <see cref="AssetDocument"/> から <see cref="SceneAssets"/> を構築。</summary>
    public Task<SceneAssets> RunAsync(AssetDocument input, ResourceUri uri, LoadContext ctx)
    {
        var assets = SceneBuilder.Build(_world, input, _device);
        return Task.FromResult(assets);
    }
}
