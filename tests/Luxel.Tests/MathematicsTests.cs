using System.Numerics;
using Luxel.Mathematics;
using Xunit;

namespace Luxel.Tests;

public sealed class MathematicsTests
{
    [Fact]
    public void MathematicsAssembly_HasNoLuxelProjectDependencies()
    {
        string[] references = typeof(OrbitCamera).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? "")
            .ToArray();
        Assert.DoesNotContain(references, name => name.StartsWith("Luxel", StringComparison.Ordinal));
    }

    [Fact]
    public void PureTypes_AreOwnedByMathematicsAssembly()
    {
        Assert.Equal("Luxel.Mathematics", typeof(OrbitCamera).Namespace);
        Assert.Equal("Luxel.Mathematics", typeof(Xorshift64).Namespace);
        Assert.Equal("Luxel.Mathematics", typeof(Affine2D).Namespace);
        Assert.Equal("Luxel.Mathematics", typeof(RectF).Namespace);
        Assert.Equal("Luxel.Mathematics", typeof(Geometry2D).Namespace);
    }

    [Fact]
    public void Geometry2D_DistancePointToSegment_HandlesProjectionAndDegenerateSegments()
    {
        Assert.Equal(3f, Geometry2D.DistancePointToSegment(new Vector2(5, 3), Vector2.Zero, new Vector2(10, 0)), 5);
        Assert.Equal(5f, Geometry2D.DistancePointToSegment(new Vector2(3, 4), Vector2.Zero, Vector2.Zero), 5);
    }
}
