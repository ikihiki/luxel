using System.Buffers.Binary;
using System.Numerics;
using Luxel.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Luxel.Assets.Gltf;

/// <summary>型付き index DTO を direct-reference の AssetDocument へ明示変換する。</summary>
public static class GltfAssetConverter
{
    public static AssetDocument Convert(GltfIndexDocument source, IReadOnlyList<byte[]> buffers,
        IReadOnlyList<byte[]> images, string sourceFormat = "gltf")
    {
        ArgumentNullException.ThrowIfNull(source);
        GltfValidator.Validate(source);
        if (buffers.Count != source.Buffers.Count) throw new ArgumentException("Buffer count does not match the index document.", nameof(buffers));
        if (images.Count != source.Images.Count) throw new ArgumentException("Image count does not match the index document.", nameof(images));
        var reader = new AccessorReader(source, buffers);
        var result = new AssetDocument { SourceFormat = sourceFormat, Generator = source.Generator };

        foreach (var sampler in source.Samplers) result.Samplers.Add(ConvertSampler(sampler));
        foreach (var texture in source.Textures)
        {
            var asset = new AssetTexture { Name = texture.Name };
            if (texture.Sampler is { } sampler) asset.Sampler = result.Samplers[sampler.Value];
            if (texture.Source is { } imageIndex)
            {
                var image = source.Images[imageIndex.Value];
                var encoded = images[imageIndex.Value];
                if (encoded.Length > 0)
                {
                    using var decoded = Image.Load<Rgba32>(encoded);
                    asset.Width = decoded.Width;
                    asset.Height = decoded.Height;
                    asset.PixelData = new byte[decoded.Width * decoded.Height * 4];
                    decoded.CopyPixelDataTo(asset.PixelData);
                    asset.Format = AssetImageFormat.Rgba8;
                    asset.MimeType = image.MimeType ?? DetectMime(encoded);
                    asset.SourceUri = image.Uri;
                }
            }
            result.Textures.Add(asset);
        }

        foreach (var material in source.Materials)
        {
            var asset = new AssetMaterial
            {
                Name = material.Name,
                BaseColorFactor = material.BaseColorFactor,
                MetallicFactor = material.MetallicFactor,
                RoughnessFactor = material.RoughnessFactor,
                AlphaMode = material.AlphaMode switch
                {
                    "MASK" => AssetAlphaMode.Mask,
                    "BLEND" => AssetAlphaMode.Blend,
                    _ => AssetAlphaMode.Opaque,
                },
                AlphaCutoff = material.AlphaCutoff,
                DoubleSided = material.DoubleSided,
            };
            if (material.BaseColorTexture is { } texture)
                asset.BaseColorTexture = new AssetTextureRef
                {
                    Texture = result.Textures[texture.Texture.Value],
                    TexCoordSet = texture.TexCoord,
                };
            result.Materials.Add(asset);
        }

        foreach (var mesh in source.Meshes)
        {
            var assetMesh = new AssetMesh { Name = mesh.Name };
            foreach (var primitive in mesh.Primitives)
            {
                var attributes = new AssetVertexBuffer();
                if (primitive.Attributes.TryGetValue("POSITION", out var position)) attributes.Positions = reader.ReadVector3(position);
                if (primitive.Attributes.TryGetValue("NORMAL", out var normal)) attributes.Normals = reader.ReadVector3(normal);
                if (primitive.Attributes.TryGetValue("TANGENT", out var tangent)) attributes.Tangents = reader.ReadVector4(tangent);
                if (primitive.Attributes.TryGetValue("TEXCOORD_0", out var tex0)) attributes.TexCoord0 = reader.ReadVector2(tex0);
                if (primitive.Attributes.TryGetValue("TEXCOORD_1", out var tex1)) attributes.TexCoord1 = reader.ReadVector2(tex1);
                if (primitive.Attributes.TryGetValue("COLOR_0", out var color)) attributes.Color0 = reader.ReadColor(color);
                if (primitive.Attributes.TryGetValue("JOINTS_0", out var joints)) attributes.Joints0 = reader.ReadJoints(joints);
                if (primitive.Attributes.TryGetValue("WEIGHTS_0", out var weights)) attributes.Weights0 = reader.ReadVector4(weights);
                var assetPrimitive = new AssetPrimitive
                {
                    Attributes = attributes,
                    Indices = primitive.Indices is { } indices ? reader.ReadIndices(indices) : null,
                    Material = primitive.Material is { } material ? result.Materials[material.Value] : null,
                    Topology = ConvertTopology(primitive.Mode),
                };
                if (primitive.Targets.Count > 0)
                {
                    assetPrimitive.MorphTargets = [];
                    foreach (var target in primitive.Targets)
                    {
                        var morph = new AssetMorphTarget();
                        if (target.TryGetValue("POSITION", out var deltaPosition)) morph.DeltaPositions = reader.ReadVector3(deltaPosition);
                        if (target.TryGetValue("NORMAL", out var deltaNormal)) morph.DeltaNormals = reader.ReadVector3(deltaNormal);
                        if (target.TryGetValue("TANGENT", out var deltaTangent)) morph.DeltaTangents = reader.ReadVector3(deltaTangent);
                        assetPrimitive.MorphTargets.Add(morph);
                    }
                }
                assetMesh.Primitives.Add(assetPrimitive);
            }
            result.Meshes.Add(assetMesh);
        }

        foreach (var node in source.Nodes)
        {
            var asset = new AssetNode { Name = node.Name, Weights = node.Weights };
            if (node.Matrix is { } m)
                asset.OverrideMatrix = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                    m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            else
            {
                if (node.Translation is { Length: >= 3 } t) asset.Translation = new Vector3(t[0], t[1], t[2]);
                if (node.Rotation is { Length: >= 4 } r) asset.Rotation = new Quaternion(r[0], r[1], r[2], r[3]);
                if (node.Scale is { Length: >= 3 } s) asset.Scale = new Vector3(s[0], s[1], s[2]);
            }
            result.Nodes.Add(asset);
        }

        foreach (var skin in source.Skins)
        {
            var asset = new AssetSkin { Name = skin.Name };
            if (skin.InverseBindMatrices is { } matrices) asset.InverseBindMatrices = reader.ReadMatrix4x4(matrices);
            result.Skins.Add(asset);
        }

        for (int i = 0; i < source.Nodes.Count; i++)
        {
            var node = source.Nodes[i];
            var asset = result.Nodes[i];
            if (node.Mesh is { } mesh)
            {
                asset.Mesh = result.Meshes[mesh.Value];
                int morphCount = asset.Mesh.Primitives.FirstOrDefault()?.MorphTargets?.Count ?? 0;
                if (asset.Weights is null && morphCount > 0)
                    asset.Weights = source.Meshes[mesh.Value].Weights ?? new float[morphCount];
            }
            if (node.Skin is { } skin) asset.Skin = result.Skins[skin.Value];
            foreach (var child in node.Children) asset.Children.Add(result.Nodes[child.Value]);
        }
        for (int i = 0; i < source.Skins.Count; i++)
        {
            var skin = source.Skins[i];
            var asset = result.Skins[i];
            foreach (var joint in skin.Joints) asset.Joints.Add(result.Nodes[joint.Value]);
            if (skin.Skeleton is { } skeleton) asset.SkeletonRoot = result.Nodes[skeleton.Value];
        }

        for (int animationIndex = 0; animationIndex < source.Animations.Count; animationIndex++)
        {
            var animation = source.Animations[animationIndex];
            var asset = new AssetAnimation { Name = animation.Name };
            foreach (var channel in animation.Channels)
            {
                if (channel.Target is null) continue;
                var sampler = animation.Samplers[channel.Sampler.Value];
                var path = channel.Path switch
                {
                    "translation" => AssetAnimationPath.Translation,
                    "rotation" => AssetAnimationPath.Rotation,
                    "scale" => AssetAnimationPath.Scale,
                    "weights" => AssetAnimationPath.Weights,
                    _ => throw new NotSupportedException($"Animation target path '{channel.Path}' is not supported."),
                };
                var times = reader.ReadScalars(sampler.Input);
                asset.Channels.Add(new AssetAnimationChannel
                {
                    TargetNode = result.Nodes[channel.Target.Value.Value],
                    Path = path,
                    Sampler = new AssetAnimationSampler
                    {
                        Times = times,
                        Interpolation = sampler.Interpolation switch
                        {
                            "STEP" => AssetInterpolation.Step,
                            "CUBICSPLINE" => AssetInterpolation.CubicSpline,
                            _ => AssetInterpolation.Linear,
                        },
                        Values = path switch
                        {
                            AssetAnimationPath.Translation or AssetAnimationPath.Scale => reader.ReadVector3(sampler.Output),
                            AssetAnimationPath.Rotation => reader.ReadQuaternions(sampler.Output),
                            _ => reader.ReadScalars(sampler.Output),
                        },
                    },
                });
                if (times.Length > 0) asset.Duration = Math.Max(asset.Duration, times[^1]);
            }
            result.Animations.Add(asset);
        }

        foreach (var scene in source.Scenes)
        {
            var asset = new AssetScene { Name = scene.Name };
            foreach (var root in scene.Nodes) asset.Roots.Add(result.Nodes[root.Value]);
            result.Scenes.Add(asset);
        }
        if (result.Scenes.Count == 0)
        {
            var scene = new AssetScene();
            var children = result.Nodes.SelectMany(node => node.Children).ToHashSet();
            foreach (var node in result.Nodes) if (!children.Contains(node)) scene.Roots.Add(node);
            result.Scenes.Add(scene);
        }
        result.DefaultScene = source.DefaultScene is { } defaultScene
            ? result.Scenes[defaultScene.Value]
            : result.Scenes.FirstOrDefault();
        return result;
    }

    private static AssetTopology ConvertTopology(int mode) => mode switch
    {
        0 => AssetTopology.Points,
        1 => AssetTopology.Lines,
        3 => AssetTopology.LineStrip,
        4 => AssetTopology.Triangles,
        5 => AssetTopology.TriangleStrip,
        2 => throw new NotSupportedException("LINE_LOOP topology is not represented by AssetTopology."),
        6 => throw new NotSupportedException("TRIANGLE_FAN topology is not represented by AssetTopology."),
        _ => throw new InvalidDataException($"Invalid primitive mode {mode}."),
    };

    private static AssetSampler ConvertSampler(GltfSampler sampler) => new()
    {
        Name = sampler.Name,
        MagFilter = sampler.MagFilter == 9728 ? AssetFilter.Nearest : AssetFilter.Linear,
        MinFilter = sampler.MinFilter is 9728 or 9984 or 9986 ? AssetFilter.Nearest : AssetFilter.Linear,
        MipFilter = sampler.MinFilter switch { 9984 or 9985 => AssetMipFilter.Nearest, 9986 or 9987 => AssetMipFilter.Linear, _ => AssetMipFilter.None },
        WrapU = ConvertWrap(sampler.WrapS),
        WrapV = ConvertWrap(sampler.WrapT),
    };

    private static AssetWrapMode ConvertWrap(int wrap) => wrap switch
    {
        33071 => AssetWrapMode.ClampToEdge,
        33648 => AssetWrapMode.MirroredRepeat,
        _ => AssetWrapMode.Repeat,
    };

    private static string? DetectMime(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 ? "image/png"
        : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg" : null;

    private sealed class AccessorReader(GltfIndexDocument document, IReadOnlyList<byte[]> buffers)
    {
        public float[] ReadScalars(AccessorIndex index) => ReadComponents(index, 1);
        public Vector2[] ReadVector2(AccessorIndex index) { var v = ReadComponents(index, 2); return Enumerable.Range(0, v.Length / 2).Select(i => new Vector2(v[i * 2], v[i * 2 + 1])).ToArray(); }
        public Vector3[] ReadVector3(AccessorIndex index) { var v = ReadComponents(index, 3); return Enumerable.Range(0, v.Length / 3).Select(i => new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2])).ToArray(); }
        public Vector4[] ReadVector4(AccessorIndex index) { var v = ReadComponents(index, 4); return Enumerable.Range(0, v.Length / 4).Select(i => new Vector4(v[i * 4], v[i * 4 + 1], v[i * 4 + 2], v[i * 4 + 3])).ToArray(); }
        public Quaternion[] ReadQuaternions(AccessorIndex index) => ReadVector4(index).Select(v => new Quaternion(v.X, v.Y, v.Z, v.W)).ToArray();
        public Vector4[] ReadColor(AccessorIndex index)
        {
            int components = GltfValidator.ComponentCount(document.Accessors[index.Value].Type);
            var values = ReadComponents(index, components);
            return Enumerable.Range(0, document.Accessors[index.Value].Count).Select(i => components == 3
                ? new Vector4(values[i * 3], values[i * 3 + 1], values[i * 3 + 2], 1)
                : new Vector4(values[i * 4], values[i * 4 + 1], values[i * 4 + 2], values[i * 4 + 3])).ToArray();
        }
        public ushort[] ReadJoints(AccessorIndex index)
        {
            var accessor = document.Accessors[index.Value];
            int components = GltfValidator.ComponentCount(accessor.Type);
            if (components != 4 || accessor.ComponentType is not (5121 or 5123)) throw new InvalidDataException("JOINTS_0 must be an unsigned byte or unsigned short VEC4.");
            var info = GetInfo(accessor);
            var result = new ushort[accessor.Count * 4];
            for (int i = 0; i < accessor.Count; i++) for (int c = 0; c < 4; c++)
                result[i * 4 + c] = accessor.ComponentType == 5121 ? info.Data[info.Offset + i * info.Stride + c] : BinaryPrimitives.ReadUInt16LittleEndian(info.Data.AsSpan(info.Offset + i * info.Stride + c * 2));
            return result;
        }
        public uint[] ReadIndices(AccessorIndex index)
        {
            var accessor = document.Accessors[index.Value];
            if (accessor.Type != "SCALAR" || accessor.ComponentType is not (5121 or 5123 or 5125)) throw new InvalidDataException("Indices must use an unsigned integer SCALAR accessor.");
            var info = GetInfo(accessor); var result = new uint[accessor.Count];
            for (int i = 0; i < result.Length; i++) result[i] = accessor.ComponentType switch
            {
                5121 => info.Data[info.Offset + i * info.Stride],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(info.Data.AsSpan(info.Offset + i * info.Stride)),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(info.Data.AsSpan(info.Offset + i * info.Stride)),
            };
            return result;
        }
        public Matrix4x4[] ReadMatrix4x4(AccessorIndex index)
        {
            var values = ReadComponents(index, 16); var result = new Matrix4x4[values.Length / 16];
            for (int i = 0; i < result.Length; i++) { int s = i * 16; result[i] = new Matrix4x4(values[s], values[s+1], values[s+2], values[s+3], values[s+4], values[s+5], values[s+6], values[s+7], values[s+8], values[s+9], values[s+10], values[s+11], values[s+12], values[s+13], values[s+14], values[s+15]); }
            return result;
        }
        private float[] ReadComponents(AccessorIndex index, int required)
        {
            var accessor = document.Accessors[index.Value]; int components = GltfValidator.ComponentCount(accessor.Type);
            if (components != required) throw new InvalidDataException($"Accessor {index.Value} has {components} components; {required} are required.");
            var info = GetInfo(accessor); int size = GltfValidator.ComponentSize(accessor.ComponentType); var result = new float[accessor.Count * components];
            for (int i = 0; i < accessor.Count; i++) for (int c = 0; c < components; c++) result[i * components + c] = ReadFloat(info.Data.AsSpan(info.Offset + i * info.Stride + c * size), accessor.ComponentType, accessor.Normalized);
            return result;
        }
        private (byte[] Data, int Offset, int Stride) GetInfo(GltfAccessor accessor)
        {
            var view = document.BufferViews[accessor.BufferView!.Value.Value];
            int elementSize = GltfValidator.ComponentSize(accessor.ComponentType) * GltfValidator.ComponentCount(accessor.Type);
            return (buffers[view.Buffer.Value], view.ByteOffset + accessor.ByteOffset, view.ByteStride ?? elementSize);
        }
        private static float ReadFloat(ReadOnlySpan<byte> value, int type, bool normalized) => type switch
        {
            5120 => normalized ? Math.Max((sbyte)value[0] / 127f, -1f) : (sbyte)value[0],
            5121 => normalized ? value[0] / 255f : value[0],
            5122 => normalized ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(value) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(value),
            5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(value) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(value),
            5125 => normalized ? BinaryPrimitives.ReadUInt32LittleEndian(value) / 4294967295f : BinaryPrimitives.ReadUInt32LittleEndian(value),
            5126 => BinaryPrimitives.ReadSingleLittleEndian(value),
            _ => throw new InvalidDataException($"Unsupported componentType {type}."),
        };
    }
}
