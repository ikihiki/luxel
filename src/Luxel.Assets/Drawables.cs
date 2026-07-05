using Friflo.Engine.ECS;

namespace Luxel.Assets;

// DRAW-M1 (DRAW-M8 で確定形): entity が「1 draw call ぶんの GPU リソース参照」を保持する component 群。
// archetype (どの component を持つか) の組み合わせで shader variant を自然に区別する:
//   DrawMesh + DrawInstance + DrawMaterial                → scene_pbr_tex (Sample 83/84/85)
//   DrawMesh + DrawInstance + DrawMaterial + DrawSkinning → scene_pbr_skinned (Sample 86)
// バッファは borrowed 参照のみ (Resources の handle / RenderBuffer が別途所有)。

/// <summary>頂点/インデックスバッファへの borrowed 参照。Resources の <c>ResourceHandle&lt;GpuBuffer&gt;.Value</c>
/// や <c>RenderBuffer.Buffer</c> をそのまま格納する。</summary>
public struct DrawMesh : IComponent
{
    /// <summary>頂点バッファ (borrowed)。</summary>
    public GpuBuffer Vertex;
    /// <summary>インデックスバッファ (borrowed)。</summary>
    public GpuBuffer Index;
    /// <summary>DrawIndexed 相当の index 数 (shader は SV_VertexID を index buffer で lookup)。</summary>
    public int IndexCount;
    /// <summary>vertex struct stride (32=Vertex, 56=SkinnedVertex)。shader の vAddr 計算用。</summary>
    public int VertexStride;
}

/// <summary>per-instance 定数 (world matrix + material index 等) を格納する GPU buffer 参照。
/// 実体は <c>RenderBuffer&lt;T&gt;</c> (Sample 側が per-frame CPU write する)。</summary>
public struct DrawInstance : IComponent
{
    /// <summary>instance 定数バッファ (borrowed)。</summary>
    public GpuBuffer Buffer;
    /// <summary>この entity が使う instance の buffer 内オフセット (通常 0、multi-instance 集約時に使用)。</summary>
    public int InstanceOffset;
    /// <summary>この entity が発行する instance 数 (通常 1)。</summary>
    public int InstanceCount;
}

/// <summary>material array の GPU buffer 参照と、この entity が使う material の index。
/// shader は <c>materialArray.Load&lt;Material&gt;(matIdx * 32)</c> で読み出す。</summary>
public struct DrawMaterial : IComponent
{
    /// <summary>material array の GPU buffer (borrowed)。</summary>
    public GpuBuffer MaterialArray;
    /// <summary>この entity が使う material の array 内 index。</summary>
    public int MaterialIndex;
}

/// <summary>skinning 用の joint matrix 配列。entity が持つとき shader は skinning path を辿る。
/// 実体は <c>RenderBuffer&lt;Matrix4x4&gt;</c> で per-frame CPU write。</summary>
public struct DrawSkinning : IComponent
{
    /// <summary>joint matrix 配列の GPU buffer (borrowed)。</summary>
    public GpuBuffer JointBuffer;
    /// <summary>joint 数 (= skin.Joints.Count)。</summary>
    public int JointCount;
}
