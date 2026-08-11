using Luxel.Typography;
using Luxel.Typography.TwoD;
using Xunit;

namespace Luxel.Tests;

public sealed class TypographyAssemblyTests
{
    [Fact]
    public void CoreTypography_HasNoLuxelProjectDependencies()
    {
        string[] references = typeof(VectorFont).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? "")
            .ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("Luxel", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoDAdapter_OwnsSceneDrawingExtensions()
    {
        Assert.Equal("Luxel.Typography", typeof(VectorFont).Assembly.GetName().Name);
        Assert.Equal("Luxel.Typography.TwoD", typeof(TypographyTwoDExtensions).Assembly.GetName().Name);

        string[] references = typeof(TypographyTwoDExtensions).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? "")
            .ToArray();
        Assert.Contains("Luxel.Typography", references);
        Assert.Contains("Luxel.Graphics.TwoD", references);

        Assert.DoesNotContain(typeof(VectorFont).GetMethods(), method => method.Name == "AppendText");
        Assert.DoesNotContain(typeof(TextLayout).GetMethods(), method => method.Name is "Draw" or "DrawColorRuns");
    }
}
