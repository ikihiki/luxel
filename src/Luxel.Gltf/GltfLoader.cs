using System.Numerics;
using System.Text.Json;
using Luxel.Assets;

namespace Luxel.Gltf;

/// <summary>
/// glTF 2.0 (.gltf / .glb) を <see cref="AssetDocument"/> (direct-ref モデル) に変換する loader。
/// glTF は index 参照だが、loader 内で 2 pass 走査し「オブジェクト作成 → 参照解決」してから返す。
/// </summary>
public sealed class GltfLoader : IAssetLoader
{
    /// <summary>拡張子 .gltf / .glb を受け付ける。</summary>
    public bool CanLoad(string path) =>
        path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

    /// <summary>ファイルを読み込み <see cref="AssetDocument"/> に変換。外部 .bin / 画像 / data:URI も解決する。</summary>
    public async Task<AssetDocument> LoadAsync(string path)
    {
        bool isGlb = path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        GltfJson json;
        byte[]? embeddedBin;
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";

        if (isGlb)
        {
            (json, embeddedBin) = await GlbReader.ReadAsync(path).ConfigureAwait(false);
        }
        else
        {
            var jsonText = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            json = JsonSerializer.Deserialize<GltfJson>(jsonText, JsonOpts)
                   ?? throw new InvalidDataException($"invalid glTF JSON: {path}");
            embeddedBin = null;
        }

        var buffers = new List<byte[]>();
        if (json.buffers is not null)
        {
            foreach (var b in json.buffers)
            {
                if (b.uri is null)
                {
                    if (embeddedBin is null) throw new InvalidDataException("buffer uri null but no embedded .glb binary");
                    buffers.Add(embeddedBin);
                }
                else if (b.uri.StartsWith("data:"))
                {
                    int comma = b.uri.IndexOf(',');
                    buffers.Add(Convert.FromBase64String(b.uri[(comma + 1)..]));
                }
                else
                {
                    var bufPath = Path.Combine(baseDir, Uri.UnescapeDataString(b.uri));
                    buffers.Add(await File.ReadAllBytesAsync(bufPath).ConfigureAwait(false));
                }
            }
        }

        var imageBytes = new List<byte[]>();
        if (json.images is not null)
        {
            foreach (var img in json.images)
            {
                byte[] bytes;
                if (img.uri is not null)
                {
                    if (img.uri.StartsWith("data:"))
                    {
                        int comma = img.uri.IndexOf(',');
                        bytes = Convert.FromBase64String(img.uri[(comma + 1)..]);
                    }
                    else
                    {
                        var imgPath = Path.Combine(baseDir, Uri.UnescapeDataString(img.uri));
                        bytes = await File.ReadAllBytesAsync(imgPath).ConfigureAwait(false);
                    }
                }
                else if (img.bufferView is int bv)
                {
                    var view = json.bufferViews![bv];
                    var buf = buffers[view.buffer];
                    int off = view.byteOffset ?? 0;
                    bytes = buf.AsSpan(off, view.byteLength).ToArray();
                }
                else bytes = Array.Empty<byte>();
                imageBytes.Add(bytes);
            }
        }

        return BuildAssetDocument(json, buffers, imageBytes, isGlb ? "glb" : "gltf");
    }

    /// <summary>パース済み GltfJson から AssetDocument を構築 (GltfStep からも利用)。</summary>
    internal static AssetDocument BuildAssetDocument(GltfJson json, List<byte[]> buffers, string sourceFormat)
        => BuildAssetDocument(json, buffers, new List<byte[]>(), sourceFormat);

    internal static AssetDocument BuildAssetDocument(GltfJson json, List<byte[]> buffers, List<byte[]> imageBytes, string sourceFormat)
    {
        var doc = new AssetDocument { SourceFormat = sourceFormat };

        // === Pass 1: 全オブジェクトの作成 (index-based) ===

        // Textures (image を含む統合型)
        if (json.textures is not null)
        {
            foreach (var t in json.textures)
            {
                var tex = new AssetTexture();
                if (t.source is int srcIdx && srcIdx >= 0 && srcIdx < imageBytes.Count)
                {
                    var bytes = imageBytes[srcIdx];
                    if (bytes.Length > 0)
                    {
                        if (IsPng(bytes))
                        {
                            var (w, h, rgba) = PngDecoder.DecodeRgba(bytes);
                            tex.Width = w; tex.Height = h;
                            tex.PixelData = rgba;
                            tex.MimeType = "image/png";
                        }
                        else
                        {
                            using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(bytes);
                            var rgba = new byte[img.Width * img.Height * 4];
                            img.CopyPixelDataTo(rgba);
                            tex.Width = img.Width; tex.Height = img.Height;
                            tex.PixelData = rgba;
                        }
                        tex.Format = AssetImageFormat.Rgba8;
                    }
                }
                doc.Textures.Add(tex);
            }
        }

        // Materials (BaseColorTexture の resolve は pass 2)
        if (json.materials is not null)
        {
            foreach (var m in json.materials)
            {
                var mat = new AssetMaterial { Name = m.name ?? "" };
                if (m.pbrMetallicRoughness?.baseColorFactor is float[] bc && bc.Length >= 4)
                    mat.BaseColorFactor = new Vector4(bc[0], bc[1], bc[2], bc[3]);
                doc.Materials.Add(mat);
            }
        }

        // Meshes + Primitives (Material の resolve は pass 2)
        var primitiveMaterialIndex = new List<(AssetPrimitive Prim, int MaterialIndex)>();
        if (json.meshes is not null)
        {
            foreach (var m in json.meshes)
            {
                var mesh = new AssetMesh { Name = m.name ?? "" };
                if (m.primitives is not null)
                {
                    foreach (var p in m.primitives)
                    {
                        var attr = new AssetVertexBuffer();
                        if (p.attributes is not null)
                        {
                            if (p.attributes.TryGetValue("POSITION", out var posAcc))
                                attr.Positions = ReadVec3Accessor(json, buffers, posAcc);
                            if (p.attributes.TryGetValue("NORMAL", out var nrmAcc))
                                attr.Normals = ReadVec3Accessor(json, buffers, nrmAcc);
                            if (p.attributes.TryGetValue("TEXCOORD_0", out var uvAcc))
                                attr.TexCoord0 = ReadVec2Accessor(json, buffers, uvAcc);
                            if (p.attributes.TryGetValue("TEXCOORD_1", out var uv1Acc))
                                attr.TexCoord1 = ReadVec2Accessor(json, buffers, uv1Acc);
                            if (p.attributes.TryGetValue("JOINTS_0", out var jAcc))
                                attr.Joints0 = ReadUShortAccessor(json, buffers, jAcc);
                            if (p.attributes.TryGetValue("WEIGHTS_0", out var wAcc))
                                attr.Weights0 = ReadVec4Accessor(json, buffers, wAcc);
                            if (p.attributes.TryGetValue("COLOR_0", out var cAcc))
                                attr.Color0 = ReadVec4Accessor(json, buffers, cAcc);
                        }
                        var prim = new AssetPrimitive { Attributes = attr };
                        if (p.indices is int idxAcc)
                            prim.Indices = ReadUIntAccessor(json, buffers, idxAcc);
                        else
                        {
                            prim.Indices = new uint[attr.Positions.Length];
                            for (uint i = 0; i < prim.Indices.Length; i++) prim.Indices[i] = i;
                        }
                        mesh.Primitives.Add(prim);
                        primitiveMaterialIndex.Add((prim, p.material ?? -1));
                    }
                }
                doc.Meshes.Add(mesh);
            }
        }

        // Nodes (Mesh/Skin/Children の resolve は pass 2)
        if (json.nodes is not null)
        {
            foreach (var n in json.nodes)
            {
                var node = new AssetNode { Name = n.name ?? "" };
                if (n.matrix is { Length: 16 } m)
                {
                    var mat = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                                            m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                    if (Matrix4x4.Decompose(mat, out var s, out var r, out var t))
                    { node.Scale = s; node.Rotation = r; node.Translation = t; }
                }
                else
                {
                    if (n.translation is { Length: >= 3 } t) node.Translation = new Vector3(t[0], t[1], t[2]);
                    if (n.rotation is { Length: >= 4 } r) node.Rotation = new Quaternion(r[0], r[1], r[2], r[3]);
                    if (n.scale is { Length: >= 3 } s) node.Scale = new Vector3(s[0], s[1], s[2]);
                }
                doc.Nodes.Add(node);
            }
        }

        // Skins (Joints/InverseBind の resolve は pass 2)
        var skinJointIndices = new List<int[]>();
        var skinSkeletonIdx = new List<int?>();
        if (json.skins is not null)
        {
            foreach (var s in json.skins)
            {
                var skin = new AssetSkin { Name = s.name ?? "" };
                if (s.inverseBindMatrices is int ibm)
                    skin.InverseBindMatrices = ReadMat4Accessor(json, buffers, ibm);
                doc.Skins.Add(skin);
                skinJointIndices.Add(s.joints ?? Array.Empty<int>());
                skinSkeletonIdx.Add(s.skeleton);
            }
        }

        // Animations (TargetNode の resolve は pass 2)
        var channelTargetNode = new List<(AssetAnimationChannel Ch, int TargetIdx)>();
        if (json.animations is not null)
        {
            foreach (var a in json.animations)
            {
                var anim = new AssetAnimation { Name = a.name ?? "" };
                if (a.channels is not null && a.samplers is not null)
                {
                    float maxDur = 0;
                    foreach (var ch in a.channels)
                    {
                        if (ch.target?.node is not int targetNode) continue;
                        var pathEnum = ch.target.path switch
                        {
                            "translation" => AssetAnimationPath.Translation,
                            "rotation" => AssetAnimationPath.Rotation,
                            "scale" => AssetAnimationPath.Scale,
                            "weights" => AssetAnimationPath.Weights,
                            _ => AssetAnimationPath.Translation,
                        };
                        var samp = a.samplers[ch.sampler];
                        var times = ReadFloatAccessor(json, buffers, samp.input);
                        var sampler = new AssetAnimationSampler
                        {
                            Times = times,
                            Interpolation = samp.interpolation switch
                            {
                                "STEP" => AssetInterpolation.Step,
                                "CUBICSPLINE" => AssetInterpolation.CubicSpline,
                                _ => AssetInterpolation.Linear,
                            },
                            Values = pathEnum switch
                            {
                                AssetAnimationPath.Translation or AssetAnimationPath.Scale => ReadVec3Accessor(json, buffers, samp.output),
                                AssetAnimationPath.Rotation => ReadQuatAccessor(json, buffers, samp.output),
                                AssetAnimationPath.Weights => ReadFloatAccessor(json, buffers, samp.output),
                                _ => Array.Empty<float>(),
                            },
                        };
                        var channel = new AssetAnimationChannel
                        {
                            Path = pathEnum,
                            Sampler = sampler,
                        };
                        anim.Channels.Add(channel);
                        channelTargetNode.Add((channel, targetNode));
                        if (times.Length > 0) maxDur = Math.Max(maxDur, times[^1]);
                    }
                    anim.Duration = maxDur;
                }
                doc.Animations.Add(anim);
            }
        }

        // === Pass 2: index → direct-ref 解決 ===

        // material.BaseColorTexture
        if (json.materials is not null)
        {
            for (int mi = 0; mi < json.materials.Length; mi++)
            {
                var jsm = json.materials[mi];
                var mat = doc.Materials[mi];
                if (jsm.pbrMetallicRoughness?.baseColorTexture is { } bct
                    && bct.index >= 0 && bct.index < doc.Textures.Count)
                {
                    mat.BaseColorTexture = new AssetTextureRef
                    {
                        Texture = doc.Textures[bct.index],
                        TexCoordSet = 0,
                    };
                }
            }
        }

        // primitive.Material
        foreach (var (prim, matIdx) in primitiveMaterialIndex)
        {
            if (matIdx >= 0 && matIdx < doc.Materials.Count)
                prim.Material = doc.Materials[matIdx];
        }

        // node.Mesh / node.Skin / node.Children
        if (json.nodes is not null)
        {
            for (int i = 0; i < json.nodes.Length; i++)
            {
                var jsn = json.nodes[i];
                var node = doc.Nodes[i];
                if (jsn.mesh is int mi && mi >= 0 && mi < doc.Meshes.Count) node.Mesh = doc.Meshes[mi];
                if (jsn.skin is int si && si >= 0 && si < doc.Skins.Count) node.Skin = doc.Skins[si];
                if (jsn.children is int[] ch)
                    foreach (var c in ch)
                        if (c >= 0 && c < doc.Nodes.Count) node.Children.Add(doc.Nodes[c]);
            }
        }

        // skin.Joints / skin.SkeletonRoot
        for (int i = 0; i < doc.Skins.Count; i++)
        {
            var skin = doc.Skins[i];
            foreach (var j in skinJointIndices[i])
                if (j >= 0 && j < doc.Nodes.Count) skin.Joints.Add(doc.Nodes[j]);
            if (skinSkeletonIdx[i] is int sr && sr >= 0 && sr < doc.Nodes.Count)
                skin.SkeletonRoot = doc.Nodes[sr];
        }

        // animation.channels.TargetNode
        foreach (var (ch, targetIdx) in channelTargetNode)
            if (targetIdx >= 0 && targetIdx < doc.Nodes.Count) ch.TargetNode = doc.Nodes[targetIdx];

        // === Scenes ===
        if (json.scenes is not null)
        {
            foreach (var s in json.scenes)
            {
                var scene = new AssetScene { Name = s.name };
                if (s.nodes is int[] roots)
                    foreach (var r in roots)
                        if (r >= 0 && r < doc.Nodes.Count) scene.Roots.Add(doc.Nodes[r]);
                doc.Scenes.Add(scene);
            }
            int sceneIdx = json.scene ?? 0;
            if (sceneIdx >= 0 && sceneIdx < doc.Scenes.Count)
                doc.DefaultScene = doc.Scenes[sceneIdx];
        }
        // fallback: どの scene も無ければ全 node を root にした暗黙シーンを 1 つ作る
        if (doc.Scenes.Count == 0)
        {
            var scene = new AssetScene();
            // 親を持たない node だけ root に
            var hasParent = new HashSet<AssetNode>();
            foreach (var n in doc.Nodes) foreach (var c in n.Children) hasParent.Add(c);
            foreach (var n in doc.Nodes) if (!hasParent.Contains(n)) scene.Roots.Add(n);
            doc.Scenes.Add(scene);
            doc.DefaultScene = scene;
        }

        return doc;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static bool IsPng(byte[] b) =>
        b.Length >= 8 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71;

    // === Accessor readers ===
    private static (byte[] buf, int offset, int count, int compSize, int numComp, int stride) AccessorInfo(GltfJson j, List<byte[]> buffers, int accIdx)
    {
        var acc = j.accessors![accIdx];
        var view = j.bufferViews![acc.bufferView!.Value];
        var buf = buffers[view.buffer];
        int offset = (view.byteOffset ?? 0) + (acc.byteOffset ?? 0);
        int compSize = acc.componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => 4,
        };
        int numComp = acc.type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" or "MAT2" => 4,
            "MAT3" => 9,
            "MAT4" => 16,
            _ => 1,
        };
        int stride = view.byteStride ?? (compSize * numComp);
        return (buf, offset, acc.count, compSize, numComp, stride);
    }

    private static Vector3[] ReadVec3Accessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var (buf, off, count, _, _, stride) = AccessorInfo(j, bufs, accIdx);
        var result = new Vector3[count];
        var span = buf.AsSpan(off);
        for (int i = 0; i < count; i++)
            result[i] = new Vector3(
                BitConverter.ToSingle(span.Slice(i * stride, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 4, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 8, 4)));
        return result;
    }

    private static Vector2[] ReadVec2Accessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var acc = j.accessors![accIdx];
        var (buf, off, count, _, _, stride) = AccessorInfo(j, bufs, accIdx);
        var result = new Vector2[count];
        var span = buf.AsSpan(off);
        for (int i = 0; i < count; i++)
        {
            float x, y;
            if (acc.componentType == 5126)
            {
                x = BitConverter.ToSingle(span.Slice(i * stride, 4));
                y = BitConverter.ToSingle(span.Slice(i * stride + 4, 4));
            }
            else if (acc.componentType == 5121)
            {
                byte bx = span[i * stride + 0], by = span[i * stride + 1];
                x = acc.normalized ? bx / 255f : bx;
                y = acc.normalized ? by / 255f : by;
            }
            else if (acc.componentType == 5123)
            {
                ushort sx = BitConverter.ToUInt16(span.Slice(i * stride, 2));
                ushort sy = BitConverter.ToUInt16(span.Slice(i * stride + 2, 2));
                x = acc.normalized ? sx / 65535f : sx;
                y = acc.normalized ? sy / 65535f : sy;
            }
            else throw new NotSupportedException($"VEC2 componentType {acc.componentType}");
            result[i] = new Vector2(x, y);
        }
        return result;
    }

    private static Vector4[] ReadVec4Accessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var (buf, off, count, _, _, stride) = AccessorInfo(j, bufs, accIdx);
        var result = new Vector4[count];
        var span = buf.AsSpan(off);
        for (int i = 0; i < count; i++)
            result[i] = new Vector4(
                BitConverter.ToSingle(span.Slice(i * stride, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 4, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 8, 4)),
                BitConverter.ToSingle(span.Slice(i * stride + 12, 4)));
        return result;
    }

    private static Quaternion[] ReadQuatAccessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var v = ReadVec4Accessor(j, bufs, accIdx);
        var r = new Quaternion[v.Length];
        for (int i = 0; i < v.Length; i++) r[i] = new Quaternion(v[i].X, v[i].Y, v[i].Z, v[i].W);
        return r;
    }

    private static float[] ReadFloatAccessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var (buf, off, count, _, _, stride) = AccessorInfo(j, bufs, accIdx);
        var result = new float[count];
        var span = buf.AsSpan(off);
        for (int i = 0; i < count; i++) result[i] = BitConverter.ToSingle(span.Slice(i * stride, 4));
        return result;
    }

    private static uint[] ReadUIntAccessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var acc = j.accessors![accIdx];
        var (buf, off, count, _, _, stride) = AccessorInfo(j, bufs, accIdx);
        var result = new uint[count];
        var span = buf.AsSpan(off);
        switch (acc.componentType)
        {
            case 5121: for (int i = 0; i < count; i++) result[i] = span[i * stride]; break;
            case 5123: for (int i = 0; i < count; i++) result[i] = BitConverter.ToUInt16(span.Slice(i * stride, 2)); break;
            case 5125: for (int i = 0; i < count; i++) result[i] = BitConverter.ToUInt32(span.Slice(i * stride, 4)); break;
            default: throw new NotSupportedException($"index componentType {acc.componentType}");
        }
        return result;
    }

    private static ushort[] ReadUShortAccessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var acc = j.accessors![accIdx];
        var (buf, off, count, _, numComp, stride) = AccessorInfo(j, bufs, accIdx);
        int total = count * numComp;
        var result = new ushort[total];
        var span = buf.AsSpan(off);
        switch (acc.componentType)
        {
            case 5121:
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < numComp; c++) result[i * numComp + c] = span[i * stride + c];
                break;
            case 5123:
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < numComp; c++) result[i * numComp + c] = BitConverter.ToUInt16(span.Slice(i * stride + c * 2, 2));
                break;
            default: throw new NotSupportedException($"joints componentType {acc.componentType}");
        }
        return result;
    }

    private static Matrix4x4[] ReadMat4Accessor(GltfJson j, List<byte[]> bufs, int accIdx)
    {
        var (buf, off, count, _, _, stride) = AccessorInfo(j, bufs, accIdx);
        var result = new Matrix4x4[count];
        var span = buf.AsSpan(off);
        for (int i = 0; i < count; i++)
        {
            int s = i * stride;
            result[i] = new Matrix4x4(
                BitConverter.ToSingle(span.Slice(s + 0, 4)), BitConverter.ToSingle(span.Slice(s + 4, 4)),
                BitConverter.ToSingle(span.Slice(s + 8, 4)), BitConverter.ToSingle(span.Slice(s + 12, 4)),
                BitConverter.ToSingle(span.Slice(s + 16, 4)), BitConverter.ToSingle(span.Slice(s + 20, 4)),
                BitConverter.ToSingle(span.Slice(s + 24, 4)), BitConverter.ToSingle(span.Slice(s + 28, 4)),
                BitConverter.ToSingle(span.Slice(s + 32, 4)), BitConverter.ToSingle(span.Slice(s + 36, 4)),
                BitConverter.ToSingle(span.Slice(s + 40, 4)), BitConverter.ToSingle(span.Slice(s + 44, 4)),
                BitConverter.ToSingle(span.Slice(s + 48, 4)), BitConverter.ToSingle(span.Slice(s + 52, 4)),
                BitConverter.ToSingle(span.Slice(s + 56, 4)), BitConverter.ToSingle(span.Slice(s + 60, 4)));
        }
        return result;
    }
}
