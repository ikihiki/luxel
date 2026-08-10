using System.Text;
using System.Text.RegularExpressions;

namespace Luxel.Shaders;

/// <summary>Normalizes Slang-generated WGSL to Luxel's fixed WebGPU resource ABI.</summary>
public static class WgslNormalizer
{
    private const uint ArenaStrideWords = 64;
    private const int TextureCount = 16;
    private const int SamplerCount = 16;

    public static string Normalize(string wgsl, ShaderProgramKind programKind, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(wgsl);

        wgsl = wgsl.Replace("\r\n", "\n", StringComparison.Ordinal);
        // Slang 2026.14 emits function-scope `const`; WGSL uses `let` for local immutable values.
        wgsl = Regex.Replace(wgsl, @"(?m)^(\s+)const\s+(\w+)\s*:", "$1let $2 :");
        // Slang currently lowers SV_Depth to a scalar color location; restore the WGSL depth builtin.
        wgsl = wgsl.Replace("@location(0) output_0 : f32,", "@builtin(frag_depth) output_0 : f32,", StringComparison.Ordinal);
        wgsl = Regex.Replace(wgsl, @"(?m)^var<uniform>\s+(\w+)\s*:", "@group(0) @binding(1) var<uniform> $1 :");
        wgsl = Regex.Replace(wgsl,
            @"(?m)^@binding\(0\) @group\(0\) var g_buffers_0 : array<array<u32>>;$",
            programKind == ShaderProgramKind.Compute
                ? "@group(0) @binding(0) var<storage, read_write> g_buffers_0 : array<u32>;"
                : "@group(0) @binding(0) var<storage, read> g_buffers_0 : array<u32>;");
        wgsl = Regex.Replace(wgsl, @"g_buffers_0\[([^\]\r\n]+)\]\[([^\]\r\n]+)\]",
            match => $"g_buffers_0[((({match.Groups[1].Value}) * {ArenaStrideWords}u) + ({match.Groups[2].Value}))]");

        if (!wgsl.Contains("g_textures_0", StringComparison.Ordinal))
            return wgsl;

        wgsl = Regex.Replace(wgsl, @"(?m)^@binding\(1\) @group\(0\) var g_textures_0 : array<texture_2d<f32>>;\n?", "");
        wgsl = Regex.Replace(wgsl, @"(?m)^@binding\(2\) @group\(0\) var g_samplers_0 : array<sampler>;\n?", "");
        wgsl = Regex.Replace(wgsl,
            @"textureSample\(\(g_textures_0\[([^\]]+)\]\), \(g_samplers_0\[([^\]]+)\]\), \(([^\)]+)\)\)",
            match => $"luxelSample2d({match.Groups[1].Value}, {match.Groups[2].Value}, {match.Groups[3].Value})");
        if (wgsl.Contains("g_textures_0", StringComparison.Ordinal) || wgsl.Contains("g_samplers_0", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported generated texture access remains in {sourcePath ?? "WGSL source"}.");

        return BuildSampledResourceCompat() + wgsl;
    }

    private static string BuildSampledResourceCompat()
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Fixed WebGPU sampled-resource ABI: group 1, textures 0..15, samplers 16..31.");
        for (int i = 0; i < TextureCount; i++)
            builder.AppendLine($"@group(1) @binding({i}) var luxelTexture{i} : texture_2d<f32>;");
        for (int i = 0; i < SamplerCount; i++)
            builder.AppendLine($"@group(1) @binding({TextureCount + i}) var luxelSampler{i} : sampler;");
        builder.AppendLine();
        for (int texture = 0; texture < TextureCount; texture++)
        {
            builder.AppendLine($"fn luxelSampleTexture{texture}(samplerIndex: u32, uv: vec2<f32>) -> vec4<f32> {{");
            builder.AppendLine("  switch samplerIndex {");
            for (int sampler = 0; sampler < SamplerCount; sampler++)
                builder.AppendLine($"    case {sampler}u: {{ return textureSample(luxelTexture{texture}, luxelSampler{sampler}, uv); }}");
            builder.AppendLine($"    default: {{ return textureSample(luxelTexture{texture}, luxelSampler0, uv); }}");
            builder.AppendLine("  }");
            builder.AppendLine("}");
        }
        builder.AppendLine("fn luxelSample2d(textureIndex: u32, samplerIndex: u32, uv: vec2<f32>) -> vec4<f32> {");
        builder.AppendLine("  switch textureIndex {");
        for (int texture = 0; texture < TextureCount; texture++)
            builder.AppendLine($"    case {texture}u: {{ return luxelSampleTexture{texture}(samplerIndex, uv); }}");
        builder.AppendLine("    default: { return luxelSampleTexture0(samplerIndex, uv); }");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        return builder.ToString();
    }
}
