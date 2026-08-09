using System.Numerics;
using System.Text;
using Luxel.Resources;
using Luxel.Graphics.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// レベル (.tmj) の読み込みを <see cref="ResourceSystem"/> 経由で管理するエントリポイント。生バイトを
/// <c>res://</c> スキーム (<see cref="EmbeddedResourceSource"/>) 越しに読み、キャッシュ/型付きノード/(将来の)
/// リロードをリソースシステムに任せる。パース自体は <see cref="CavernTiled"/> (純ロジック)。
///
/// <para>既定は自前の ResourceSystem を持つ (レベルは Core.dll 埋め込み)。ホストの共有 ResourceSystem を渡せば
/// そちらに <see cref="EmbeddedResourceSource"/> を足して相乗りする。インスタンス管理 (static を持たない)。</para>
/// </summary>
public sealed class CavernLevelLoader : IDisposable
{
    /// <summary>埋め込みレベルの URI。</summary>
    public const string LevelUri = "res://levels/cavern1.tmj";

    private readonly ResourceSystem _res;
    private readonly bool _ownsResources;
    private ResourceHandle<byte[]>? _handle;
    private Vector2[]? _torches;

    /// <summary>レベル読み込みに使う ResourceSystem。</summary>
    public ResourceSystem Resources => _res;

    /// <param name="resources">必要な <see cref="EmbeddedResourceSource"/> がbuild時に登録済みの共有 ResourceSystem。
    /// 未指定なら埋め込み専用の ResourceSystem を自前で生成し所有する。</param>
    public CavernLevelLoader(ResourceSystem? resources = null)
    {
        if (resources is not null)
        {
            _res = resources;
            _ownsResources = false;
        }
        else
        {
            _res = CavernResources.CreateEmbedded();
            _ownsResources = true;
        }
    }

    /// <summary>レベル JSON を ResourceSystem 経由で取得する (byte[] ノードはハンドル保持でキャッシュされる)。</summary>
    public string LoadJson()
    {
        _handle ??= _res.Load<byte[]>(LevelUri);
        _handle.Ready.GetAwaiter().GetResult();
        if (_handle.Error is not null)
            throw new InvalidOperationException($"レベル読み込みに失敗: {LevelUri}", _handle.Error);
        return Encoding.UTF8.GetString(_handle.Value);
    }

    /// <summary>タイル層だけのマップ (エンティティ抜き。物理テスト用)。</summary>
    public TileMap BuildMap(TileSet ts) => CavernTiled.BuildMap(ts, LoadJson());

    /// <summary>マップ + エンティティを配置した <see cref="CavernSim"/> を作る。</summary>
    public CavernSim CreateSim()
    {
        TileSet ts = CavernLevel.BuildTileSet(CavernLevel.BuildAtlas());
        CavernSim sim = CavernTiled.BuildSim(LoadJson(), ts, CavernLevel.Spawn, new Vector2(12, 22), out Vector2[] torches);
        _torches = torches;
        return sim;
    }

    /// <summary>松明の位置 (演出レイヤ用。一度読んでキャッシュ)。</summary>
    public Vector2[] Torches => _torches ??= CavernTiled.ParseTorches(LoadJson());

    public void Dispose()
    {
        _handle?.Dispose();
        if (_ownsResources) _res.Dispose();
    }
}
