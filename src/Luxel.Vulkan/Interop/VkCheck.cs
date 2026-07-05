using Silk.NET.Vulkan;

namespace Luxel.Vulkan.Interop;

internal static class VkCheck
{
    /// <summary>Vulkan の <see cref="Result"/> が成功でなければ例外を投げる。</summary>
    public static void Ok(Result result, string what)
    {
        if (result != Result.Success)
            throw new VulkanException($"{what} に失敗しました: {result}");
    }
}

/// <summary>Vulkan API 呼び出しの失敗を表す例外。</summary>
public sealed class VulkanException(string message) : Exception(message);
