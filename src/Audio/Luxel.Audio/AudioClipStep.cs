using Luxel.Resources;

namespace Luxel.Audio;

/// <summary>
/// Resources 統合: <c>resources.Load&lt;AudioClip&gt;("*.wav")</c> / <c>*.ogg</c> で自動デコード。
/// hot-reload 対応 (ファイル変更で <see cref="ResourceHandle{T}.Reloaded"/> が発火)。
/// </summary>
public sealed class AudioClipStep : IResourceStep<byte[], AudioClip>
{
    public IEnumerable<string> Extensions => new[] { ".wav", ".ogg" };

    public Task<AudioClip> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
    {
        var clip = AudioClipLoader.Load(input, uri.Extension, name: uri.Path);
        return Task.FromResult(clip);
    }
}
