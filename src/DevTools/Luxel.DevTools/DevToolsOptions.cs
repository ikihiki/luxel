namespace Luxel.DevTools;

/// <summary>
/// DevTools 結線のオプトイン設定 (Q05 E)。ブラウザ版 (<see cref="DebugServer"/>) と
/// 内蔵版 (<c>DevToolsApp</c>) を個別に選択・併用できる。既定はどちらも off
/// (publish 成果物が勝手にサーバ/ウィンドウを立てないため)。
///
/// <para>純粋な設定/引数解釈なのでこの net10.0 アセンブリに置く (テストから直接参照できる)。
/// 実際に host へ載せる拡張 <c>WithDevTools</c> は Luxel.Framework.DevTools 側。</para>
/// </summary>
/// <param name="BrowserPort">ブラウザ版 DebugServer のポート。<c>null</c>=無効、<c>0</c>=空きポート自動割当。</param>
/// <param name="Native">内蔵版ネイティブ DevTools ウィンドウ (別スレッド UI 島) を起動するか。</param>
/// <param name="NativeDeviceFactory">内蔵版が使う第二 <see cref="Luxel.Graphics.GpuDevice"/> を作る factory
///   (島スレッド上で呼ばれる)。<paramref name="Native"/>=true のとき必須。</param>
public sealed record DevToolsOptions(
    int? BrowserPort = null,
    bool Native = false,
    Func<Luxel.Graphics.GpuDevice>? NativeDeviceFactory = null)
{
    /// <summary>どちらのフロントエンドも無効か。</summary>
    public bool IsDisabled => BrowserPort is null && !Native;

    /// <summary>
    /// コマンドライン引数から設定を組む。
    /// <list type="bullet">
    /// <item><c>--devtools</c> / <c>--devtools &lt;port&gt;</c> / <c>--devtools-port &lt;port&gt;</c>
    ///   → ブラウザ版 (port 省略=自動割当)。</item>
    /// <item><c>--devtools-native</c> → 内蔵版ネイティブウィンドウ (<paramref name="nativeDeviceFactory"/> が必要)。</item>
    /// </list>
    /// 両方指定すれば併用。該当引数が無ければ <see cref="IsDisabled"/> な設定を返す。
    /// </summary>
    public static DevToolsOptions Parse(string[] args, Func<Luxel.Graphics.GpuDevice>? nativeDeviceFactory = null)
    {
        int? browserPort = null;
        bool native = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if (a is "--devtools" or "--devtools-port")
            {
                browserPort = 0;   // 既定は自動割当
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) { browserPort = p; i++; }
            }
            else if (a == "--devtools-native")
            {
                native = true;
            }
        }
        return new DevToolsOptions(browserPort, native, native ? nativeDeviceFactory : null);
    }
}
