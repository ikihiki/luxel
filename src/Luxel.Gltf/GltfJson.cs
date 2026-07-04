using System.Text.Json.Serialization;

namespace Luxel.Gltf;

// glTF 2.0 JSON schema (https://github.com/KhronosGroup/glTF/tree/main/specification/2.0)
// 必須最小限のみ実装、未対応 field は無視。

internal sealed class GltfJson
{
    public GltfAsset? asset { get; set; }
    public int? scene { get; set; }
    public GltfSceneDef[]? scenes { get; set; }
    public GltfNodeDef[]? nodes { get; set; }
    public GltfMeshDef[]? meshes { get; set; }
    public GltfMaterialDef[]? materials { get; set; }
    public GltfTextureDef[]? textures { get; set; }
    public GltfImageDef[]? images { get; set; }
    public GltfSamplerDef[]? samplers { get; set; }
    public GltfAccessorDef[]? accessors { get; set; }
    public GltfBufferViewDef[]? bufferViews { get; set; }
    public GltfBufferDef[]? buffers { get; set; }
    public GltfAnimationDef[]? animations { get; set; }
    public GltfSkinDef[]? skins { get; set; }
}

internal sealed class GltfAsset { public string? version { get; set; } public string? generator { get; set; } }
internal sealed class GltfSceneDef { public int[]? nodes { get; set; } public string? name { get; set; } }
internal sealed class GltfNodeDef
{
    public string? name { get; set; }
    public int[]? children { get; set; }
    public int? mesh { get; set; }
    public int? skin { get; set; }
    public float[]? translation { get; set; }
    public float[]? rotation { get; set; }
    public float[]? scale { get; set; }
    public float[]? matrix { get; set; }
}
internal sealed class GltfMeshDef
{
    public string? name { get; set; }
    public GltfPrimitiveDef[]? primitives { get; set; }
}
internal sealed class GltfPrimitiveDef
{
    public Dictionary<string, int>? attributes { get; set; }
    public int? indices { get; set; }
    public int? material { get; set; }
    public int? mode { get; set; }  // 4=Triangles default
}
internal sealed class GltfMaterialDef
{
    public string? name { get; set; }
    public GltfPbrDef? pbrMetallicRoughness { get; set; }
}
internal sealed class GltfPbrDef
{
    public float[]? baseColorFactor { get; set; }
    public GltfTextureInfoDef? baseColorTexture { get; set; }
}
internal sealed class GltfTextureInfoDef { public int index { get; set; } public int? texCoord { get; set; } }
internal sealed class GltfTextureDef { public int? source { get; set; } public int? sampler { get; set; } }
internal sealed class GltfImageDef
{
    public string? uri { get; set; }
    public int? bufferView { get; set; }
    public string? mimeType { get; set; }
}
internal sealed class GltfSamplerDef { public int? magFilter { get; set; } public int? minFilter { get; set; } }
internal sealed class GltfAccessorDef
{
    public int? bufferView { get; set; }
    public int? byteOffset { get; set; }
    public int componentType { get; set; }  // 5120=byte 5121=ubyte 5122=short 5123=ushort 5125=uint 5126=float
    public bool normalized { get; set; }    // true: 整数を [0..1] または [-1..1] に正規化して読む
    public int count { get; set; }
    public string? type { get; set; }       // SCALAR/VEC2/VEC3/VEC4/MAT4
}
internal sealed class GltfBufferViewDef
{
    public int buffer { get; set; }
    public int? byteOffset { get; set; }
    public int byteLength { get; set; }
    public int? byteStride { get; set; }
    public int? target { get; set; }
}
internal sealed class GltfBufferDef { public string? uri { get; set; } public int byteLength { get; set; } }
internal sealed class GltfAnimationDef
{
    public string? name { get; set; }
    public GltfAnimChannelDef[]? channels { get; set; }
    public GltfAnimSamplerDef[]? samplers { get; set; }
}
internal sealed class GltfAnimChannelDef
{
    public int sampler { get; set; }
    public GltfAnimTargetDef? target { get; set; }
}
internal sealed class GltfAnimTargetDef { public int? node { get; set; } public string? path { get; set; } }
internal sealed class GltfAnimSamplerDef
{
    public int input { get; set; }
    public int output { get; set; }
    public string? interpolation { get; set; }
}
internal sealed class GltfSkinDef
{
    public string? name { get; set; }
    public int? inverseBindMatrices { get; set; }
    public int[]? joints { get; set; }
    public int? skeleton { get; set; }
}
