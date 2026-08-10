using System.Text;
using Luxel.Resources;

namespace Luxel.Particles;

/// <summary>
/// byte[] (UTF-8 JSON) → <see cref="ParticleConfig"/> のリソースステップ (CPU ステージ)。
/// リソース DAG に登録すると <c>resources.Load&lt;ParticleConfig&gt;("explosion.particle.json")</c> で読め、
/// watch/reload に乗るとエフェクトのライブ編集になる。
/// </summary>
public sealed class ParticleConfigStep : IResourceStep<byte[], ParticleConfig>
{
    public Task<ParticleConfig> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(ParticleConfigJson.FromJson(Encoding.UTF8.GetString(input)));
}
