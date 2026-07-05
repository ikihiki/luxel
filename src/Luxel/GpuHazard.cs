namespace Luxel;

/// <summary>
/// バリアの特殊ハザードフラグ。ブログの <c>HAZARD_DESCRIPTORS</c> 等に相当。
/// 通常のメモリ可視性に加えて必要となる、ディスクリプタキャッシュの無効化や
/// 間接引数の可視化などを指定する。
/// </summary>
[Flags]
public enum GpuHazard : uint
{
    None = 0,

    /// <summary>ディスクリプタ (bindless heap) の書き換えを後段から見えるようにする。</summary>
    Descriptors = 1 << 0,

    /// <summary>間接描画/ディスパッチ引数の書き込みを描画に見えるようにする。</summary>
    IndirectArguments = 1 << 1,
}
