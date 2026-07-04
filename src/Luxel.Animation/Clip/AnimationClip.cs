namespace Luxel.Animation;

/// <summary>
/// 複数 Track の束 = 1 つの「アニメーション」全体。Luxel.Resources の (型, uri) でロード対象。
/// Importer (glTF / CSS / Lottie subset) が出力し、Player へ供給する不変データ。
/// </summary>
public sealed class AnimationClip
{
    public string Name { get; }
    public TrackBase[] Tracks { get; }
    public float Duration { get; }

    public AnimationClip(string name, TrackBase[] tracks)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tracks);
        Name = name;
        Tracks = tracks;
        float max = 0f;
        foreach (var t in tracks)
            if (t.Duration > max) max = t.Duration;
        Duration = max;
    }
}
