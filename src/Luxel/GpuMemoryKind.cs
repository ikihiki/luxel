namespace Luxel;

/// <summary>
/// ブログの 3 つのメモリ種別。<c>gpuMalloc</c> が返すメモリの性質を選ぶ。
/// </summary>
public enum GpuMemoryKind
{
    /// <summary>CPU から書き込みやすい (HOST_VISIBLE|DEVICE_LOCAL / GPU_UPLOAD)。アップロード用。</summary>
    HostMapped,

    /// <summary>GPU 専用 (DEVICE_LOCAL / DEFAULT)。テクスチャや大きな読み書きバッファ用。</summary>
    DeviceLocal,

    /// <summary>CPU からキャッシュ読み出ししやすい (HOST_CACHED / READBACK)。リードバック用。</summary>
    HostCached,
}
