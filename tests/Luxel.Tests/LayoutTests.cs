using System.Runtime.InteropServices;
using Luxel;
using Xunit;

namespace Luxel.Tests;

public class ShaderCodeTests
{
    [Fact]
    public void ForBackend_ReturnsSpirvForVulkan()
    {
        var code = new GpuShaderCode { SpirV = [1, 2, 3], Dxil = null };
        Assert.Equal(new byte[] { 1, 2, 3 }, code.ForBackend(GpuBackendKind.Vulkan));
    }

    [Fact]
    public void ForBackend_ThrowsWhenMissing()
    {
        var code = new GpuShaderCode { SpirV = null, Dxil = [9] };
        Assert.Throws<InvalidOperationException>(() => code.ForBackend(GpuBackendKind.Vulkan));
    }
}

/// <summary>
/// 統一 RootArgs (compute01.slang) の CPU ミラーが scalar レイアウト (offset 0 / 4 / 8) と
/// 一致することを検証する。ずれると GPU が誤ったインデックス/要素数を読むため重要。
/// </summary>
public class RootArgsLayoutTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RootArgs
    {
        public uint InputIndex;
        public uint OutputIndex;
        public uint Count;
    }

    [Fact]
    public void Offsets_MatchScalarLayout()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<RootArgs>(nameof(RootArgs.InputIndex)));
        Assert.Equal(4, (int)Marshal.OffsetOf<RootArgs>(nameof(RootArgs.OutputIndex)));
        Assert.Equal(8, (int)Marshal.OffsetOf<RootArgs>(nameof(RootArgs.Count)));
        Assert.Equal(12, Marshal.SizeOf<RootArgs>());
    }
}
