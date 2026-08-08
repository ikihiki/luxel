namespace Luxel.Assets.Gltf;

internal static class GltfValidator
{
    public static void Validate(GltfIndexDocument document)
    {
        if (document.Version != "2.0") throw new InvalidDataException($"Unsupported glTF version '{document.Version}'.");
        Check(document.DefaultScene, document.Scenes.Count, "scene");
        foreach (var scene in document.Scenes) foreach (var node in scene.Nodes) Check(node, document.Nodes.Count, "scene.nodes");
        foreach (var node in document.Nodes)
        {
            foreach (var child in node.Children) Check(child, document.Nodes.Count, "node.children");
            Check(node.Mesh, document.Meshes.Count, "node.mesh");
            Check(node.Skin, document.Skins.Count, "node.skin");
            if (node.Matrix is not null && node.Matrix.Length != 16) throw new InvalidDataException("node.matrix must contain 16 values.");
        }
        foreach (var mesh in document.Meshes)
        foreach (var primitive in mesh.Primitives)
        {
            foreach (var accessor in primitive.Attributes.Values) Check(accessor, document.Accessors.Count, "primitive.attributes");
            Check(primitive.Indices, document.Accessors.Count, "primitive.indices");
            Check(primitive.Material, document.Materials.Count, "primitive.material");
            foreach (var target in primitive.Targets)
                foreach (var accessor in target.Values) Check(accessor, document.Accessors.Count, "primitive.targets");
        }
        foreach (var material in document.Materials)
            if (material.BaseColorTexture is { } texture) Check(texture.Texture, document.Textures.Count, "material.baseColorTexture");
        foreach (var texture in document.Textures)
        {
            Check(texture.Source, document.Images.Count, "texture.source");
            Check(texture.Sampler, document.Samplers.Count, "texture.sampler");
        }
        foreach (var image in document.Images) Check(image.BufferView, document.BufferViews.Count, "image.bufferView");
        foreach (var view in document.BufferViews)
        {
            Check(view.Buffer, document.Buffers.Count, "bufferView.buffer");
            if (view.ByteOffset < 0 || view.ByteLength < 0) throw new InvalidDataException("bufferView range must be non-negative.");
        }
        foreach (var accessor in document.Accessors)
        {
            Check(accessor.BufferView, document.BufferViews.Count, "accessor.bufferView");
            if (accessor.Count < 0 || accessor.ByteOffset < 0) throw new InvalidDataException("accessor count and offset must be non-negative.");
            if (accessor.BufferView is null) throw new NotSupportedException("Sparse or zero-initialized accessors are not supported.");
            _ = ComponentCount(accessor.Type);
            _ = ComponentSize(accessor.ComponentType);
        }
        foreach (var skin in document.Skins)
        {
            Check(skin.InverseBindMatrices, document.Accessors.Count, "skin.inverseBindMatrices");
            Check(skin.Skeleton, document.Nodes.Count, "skin.skeleton");
            foreach (var joint in skin.Joints) Check(joint, document.Nodes.Count, "skin.joints");
        }
        foreach (var animation in document.Animations)
        {
            foreach (var sampler in animation.Samplers)
            {
                Check(sampler.Input, document.Accessors.Count, "animation.sampler.input");
                Check(sampler.Output, document.Accessors.Count, "animation.sampler.output");
            }
            foreach (var channel in animation.Channels)
            {
                if (channel.Sampler.Value < 0 || channel.Sampler.Value >= animation.Samplers.Count)
                    Invalid("animation.channel.sampler", channel.Sampler.Value, animation.Samplers.Count);
                Check(channel.Target, document.Nodes.Count, "animation.channel.target.node");
            }
        }
    }

    public static int ComponentSize(int type) => type switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new InvalidDataException($"Unsupported accessor componentType {type}.")
    };

    public static int ComponentCount(string type) => type switch
    {
        "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" or "MAT2" => 4,
        "MAT3" => 9, "MAT4" => 16,
        _ => throw new InvalidDataException($"Unsupported accessor type '{type}'.")
    };

    private static void Check(NodeIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(MeshIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(MaterialIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(TextureIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(ImageIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(BufferIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(BufferViewIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(AccessorIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(SkinIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(SamplerIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void Check(SceneIndex? index, int count, string relation) { if (index is { } value) CheckValue(value.Value, count, relation); }
    private static void CheckValue(int value, int count, string relation) { if (value < 0 || value >= count) Invalid(relation, value, count); }
    private static void Invalid(string relation, int value, int count) =>
        throw new InvalidDataException($"Invalid {relation} index {value}; expected 0..{count - 1}.");
}
