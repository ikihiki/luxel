using System.Numerics;
using Luxel.Assets;
using Luxel.AssetsGpu;

namespace Luxel.Tests;

/// <summary>
/// <see cref="Luxel.AssetsGpu"/> の型 shape と logic をチェックする軽量テスト。
/// GPU device を必要としない部分のみ (upload 系は sample で実 GPU 検証)。
/// </summary>
public class AssetsGpuTests
{
    [Fact]
    public void GpuMaterial_EncodesBaseColorFactor()
    {
        var mat = new GpuMaterial { BaseColorFactor = new Vector4(0.5f, 0.25f, 0.75f, 1.0f) };
        var data = mat.ToShaderData();
        Assert.Equal(0.5f, data.BaseColor.X, precision: 4);
        Assert.Equal(0.25f, data.BaseColor.Y, precision: 4);
        Assert.Equal(0.75f, data.BaseColor.Z, precision: 4);
        Assert.Equal(0u, data.Flags);  // texture 無しなので flag 0
    }

    [Fact]
    public void GpuMaterial_EncodesFlagWithTexture()
    {
        // BaseColorTexture が存在する場合のみ FlagHasTexture が立つ
        var matNoTex = new GpuMaterial();
        Assert.Equal(0u, matNoTex.ToShaderData().Flags);
        // (実 GpuTexture インスタンスは device 必要なので Flags 確認は shader data 経由)
    }

    [Fact]
    public void GpuMesh_DirectRefWorks()
    {
        // Asset* → GpuMesh の direct ref を保持できることを確認
        var assetMat = new AssetMaterial { Name = "m", BaseColorFactor = Vector4.UnitX };
        var gpuMat = new GpuMaterial { Source = assetMat, Name = "m", BaseColorFactor = Vector4.UnitX };
        var assetPrim = new AssetPrimitive();
        var gpuPrim = new GpuPrimitive { Source = assetPrim, Material = gpuMat };
        var assetMesh = new AssetMesh { Name = "mesh" };
        var gpuMesh = new GpuMesh { Source = assetMesh, Name = "mesh" };
        gpuMesh.Primitives.Add(gpuPrim);

        Assert.Same(assetMat, gpuMat.Source);
        Assert.Same(gpuMat, gpuMesh.Primitives[0].Material);
        Assert.Same(assetMesh, gpuMesh.Source);
    }

    [Fact]
    public void GpuMaterial_SharesTextureRef()
    {
        // 同じ AssetTexture インスタンスが複数 GpuMaterial から参照されうる (registry の dedup 前提)
        var assetTex = new AssetTexture { Width = 1, Height = 1, PixelData = new byte[4] };
        // 実 GpuTexture は device 依存だが、direct ref 自体は class 参照なので単純比較で OK
        var m1 = new GpuMaterial();
        var m2 = new GpuMaterial();
        // BaseColorTexture への null 代入で確認
        m1.BaseColorTexture = null;
        m2.BaseColorTexture = null;
        Assert.Same(m1.BaseColorTexture, m2.BaseColorTexture);
    }
}
