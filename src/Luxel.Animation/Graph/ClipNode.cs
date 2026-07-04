namespace Luxel.Animation;

/// <summary>1 つの <see cref="AnimationClip"/> をラップするリーフノード。各 Track を sample して output に書く。</summary>
public sealed class ClipNode : GraphNode
{
    public AnimationClip Clip { get; }
    public override float Duration => Clip.Duration;

    public ClipNode(AnimationClip clip)
    {
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
    }

    public override void Evaluate(float time, GraphEvaluator output)
    {
        foreach (var track in Clip.Tracks)
        {
            object value = TrackValue.Sample(track, time);
            output.Set(track.TargetPath, value, track);
        }
    }
}
