namespace Luxel;

/// <summary>
/// 併存コンパイルされたシェーダ IR を保持する。Vulkan は SPIR-V、D3D12 は DXIL を使う。
/// graphics では DXIL は頂点/ピクセルで別 blob (DXC は 1 entry/blob) になる。
/// </summary>
public sealed class GpuShaderCode
{
    /// <summary>Vulkan 用 SPIR-V (compute は単一エントリ、graphics は vs/ps 両エントリを含む)。</summary>
    public byte[]? SpirV { get; init; }

    /// <summary>D3D12 用 compute DXIL (エントリ main)。</summary>
    public byte[]? Dxil { get; init; }

    /// <summary>D3D12 用 頂点シェーダ DXIL。</summary>
    public byte[]? DxilVertex { get; init; }

    /// <summary>D3D12 用 ピクセルシェーダ DXIL。</summary>
    public byte[]? DxilPixel { get; init; }

    /// <summary>compute 用: 指定バックエンドの IR。</summary>
    public byte[] ForBackend(GpuBackendKind kind) => kind switch
    {
        GpuBackendKind.Vulkan => SpirV ?? throw Missing(kind, "SPIR-V (.spv)"),
        GpuBackendKind.D3D12 => Dxil ?? throw Missing(kind, "compute DXIL (.dxil)"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>graphics 用: 頂点シェーダ blob。</summary>
    public byte[] VertexBlob(GpuBackendKind kind) => kind switch
    {
        GpuBackendKind.Vulkan => SpirV ?? throw Missing(kind, "SPIR-V (.spv)"),
        GpuBackendKind.D3D12 => DxilVertex ?? throw Missing(kind, "頂点 DXIL (.vs.dxil)"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>graphics 用: ピクセルシェーダ blob。</summary>
    public byte[] PixelBlob(GpuBackendKind kind) => kind switch
    {
        GpuBackendKind.Vulkan => SpirV ?? throw Missing(kind, "SPIR-V (.spv)"),
        GpuBackendKind.D3D12 => DxilPixel ?? throw Missing(kind, "ピクセル DXIL (.ps.dxil)"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static InvalidOperationException Missing(GpuBackendKind kind, string what)
        => new($"バックエンド {kind} 用の {what} がこの GpuShaderCode に含まれていません。");

    /// <summary>
    /// 出力ディレクトリ (実行ファイル隣) の shaders/ から
    /// <c>&lt;baseName&gt;.spv</c> / <c>.dxil</c> / <c>.vs.dxil</c> / <c>.ps.dxil</c> を読み込む。
    /// </summary>
    public static GpuShaderCode Load(string baseName, string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "shaders");
        byte[]? Read(string ext)
        {
            string p = Path.Combine(directory, baseName + ext);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }
        return new GpuShaderCode
        {
            SpirV = Read(".spv"),
            Dxil = Read(".dxil"),
            DxilVertex = Read(".vs.dxil"),
            DxilPixel = Read(".ps.dxil"),
        };
    }
}
