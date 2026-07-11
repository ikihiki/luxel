namespace Luxel.SceneEdit;

/// <summary>
/// タイル番号 → プレースホルダ色 (RGBA packed uint、R|G&lt;&lt;8|B&lt;&lt;16|A&lt;&lt;24 = Color2D と同配置)。
/// エディタ (SceneSpace2DAdapter) とランタイム (Luxel.Player) が**同じ見た目**になるよう共有する。
/// 実アトラス描画 (TileSet/SpriteAtlas) が配線されたらどちらも差し替える。決定的 (golden 前提)。
/// </summary>
public static class TilePalette
{
    public static uint ColorOf(int tile) => tile switch
    {
        1 => Pack(106, 170, 100),   // 草
        2 => Pack(160, 120, 84),    // 土
        3 => Pack(140, 144, 152),   // 石
        4 => Pack(222, 186, 92),    // 金
        _ => Pack((byte)(70 + tile * 53 % 150), (byte)(70 + tile * 97 % 150), (byte)(70 + tile * 29 % 150)),
    };

    public static uint Pack(byte r, byte g, byte b, byte a = 255)
        => r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
}
