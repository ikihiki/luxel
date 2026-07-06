using System.Text;
using Luxel.Resources;

namespace Luxel.TwoD;

/// <summary>
/// byte[] (UTF-8 JSON) → <see cref="SpriteAtlas"/> のリソースステップ (CPU ステージ)。
/// リソース DAG に登録すると <c>resources.Load&lt;SpriteAtlas&gt;("sprites.atlas.json")</c> で読める。
/// 出力型 <see cref="SpriteAtlas"/> が一意なので拡張子で曖昧化する必要はない。
/// <para>テクスチャ (<see cref="SpriteAtlas.TextureUri"/>) のロード + GPU アップロード + <see cref="SpriteAtlas.Bind"/> は
/// 呼び出し側が行う (このステップは GPU 非依存で、アトラス定義のパースだけを担う)。</para>
/// </summary>
public sealed class SpriteAtlasStep : IResourceStep<byte[], SpriteAtlas>
{
    public Executor Executor => Executor.Cpu;

    public Task<SpriteAtlas> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(SpriteAtlas.FromJson(Encoding.UTF8.GetString(input)));
}
