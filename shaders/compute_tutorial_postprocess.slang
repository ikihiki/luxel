// Learn/Rendering/FirstRenderGraph: transient scene pixels -> external presentation framebuffer.

[[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];

struct PostProcessArgs
{
    uint sourceBufferIndex;
    uint destinationBufferIndex;
    uint width;
    uint height;
    uint stridePixels;
};
[[vk::push_constant]] PostProcessArgs g_args;

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= g_args.width || tid.y >= g_args.height) return;
    uint address = (tid.y * g_args.stridePixels + tid.x) * 4;
    uint packed = g_buffers[g_args.sourceBufferIndex].Load(address);
    float3 color = float3(
        ((packed >> 0) & 255u) / 255.0,
        ((packed >> 8) & 255u) / 255.0,
        ((packed >> 16) & 255u) / 255.0);

    // A visible but deterministic post-process: cool shadows and a soft vignette.
    float2 uv = (float2(tid.xy) + 0.5) / float2(g_args.width, g_args.height);
    float vignette = saturate(1.15 - dot(uv - 0.5, uv - 0.5) * 1.25);
    color = lerp(color, color * float3(0.72, 0.86, 1.08), 0.28) * vignette;

    uint r = (uint)(saturate(color.r) * 255.0 + 0.5);
    uint g = (uint)(saturate(color.g) * 255.0 + 0.5);
    uint b = (uint)(saturate(color.b) * 255.0 + 0.5);
    g_buffers[g_args.destinationBufferIndex].Store(address, r | (g << 8) | (b << 16) | 0xff000000u);
}
