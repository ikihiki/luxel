namespace Luxel.Gallery.Stories;

public static partial class LearnAnimationParticles
{
    [Story]
    public static StoryResult ParticleViewSample(StoryContext ctx) => ParticleViewStories.View(ctx);

    [Story]
    public static StoryResult Particles2DSample(StoryContext ctx) => ParticleStories.Particles(ctx);

    [Story]
    public static StoryResult Particles3DSample(StoryContext ctx) => Particle3DStories.Particles(ctx);
}
