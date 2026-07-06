using System.Numerics;

namespace Luxel.TwoD;

/// <summary>ワールド → スクリーン (px) 変換。2D はほぼ恒等、3D はゲームの viewProj + ビューポート。</summary>
public delegate Vector2 WorldToScreen(Vector3 world);

/// <summary><see cref="DebugDraw.Flush"/> がテキストを描くための委譲 (Typography 非依存化のため)。
/// 典型的には <c>(sc, t, x, y, h, c) =&gt; font.AppendText(sc, t, x, y, h, c)</c> を渡す。</summary>
public delegate void DebugTextDrawer(Scene2D scene, string text, float x, float y, float pixelHeight, uint color);

/// <summary>
/// 即時モードのデバッグ描画 (gizmo の基盤)。ゲームコード / DevTools が任意の場所で
/// <see cref="Line"/>/<see cref="Rect"/>/<see cref="Circle"/>/<see cref="Text"/> を呼び、
/// 描画側が毎フレーム <see cref="Flush"/> で最前面オーバーレイ (<see cref="Scene2D"/>) へ流し込む。
///
/// <para><b>2D/3D 両対応</b>: コマンドはワールド空間で溜め、<see cref="Flush"/> に渡す
/// <see cref="WorldToScreen"/> 委譲で投影する (2D は恒等、3D はカメラの viewProj)。</para>
///
/// <para><b>カテゴリと ON/OFF</b>: 既定は全 OFF。<see cref="Enable"/> したカテゴリ (kind) のコマンドだけ溜まる。
/// <c>"all"</c> を Enable すると全カテゴリが出る。OFF のカテゴリへの呼び出しは
/// <see cref="IsEnabled"/> の HashSet 判定だけで**割り当てゼロ**で抜ける (購読者ゼロ規律に相当)。</para>
///
/// <para><b>スレッド</b>: 即時モードにつきスレッド安全ではない — 描画スレッド (app スレッド) から使う。</para>
/// </summary>
public static class DebugDraw
{
    /// <summary>既定カテゴリ (kind 省略時)。</summary>
    public const string Default = "default";
    /// <summary>全カテゴリを一括で有効にする特別 kind。</summary>
    public const string All = "all";

    private enum Kind { Line, Rect, Circle, Text }

    private readonly record struct Cmd(
        Kind Shape, Vector3 A, Vector3 B, float Radius, uint Color, float Width, int Segments, string? Text, float TextSize);

    private static readonly HashSet<string> EnabledKinds = new();
    private static readonly List<Cmd> Commands = new();

    /// <summary>カテゴリを有効化する。</summary>
    public static void Enable(string kind) => EnabledKinds.Add(kind);
    /// <summary>カテゴリを無効化する。</summary>
    public static void Disable(string kind) => EnabledKinds.Remove(kind);
    /// <summary>カテゴリの ON/OFF を設定する (<c>gizmo.enable</c> コマンドの受け口)。</summary>
    public static void SetEnabled(string kind, bool on) { if (on) Enable(kind); else Disable(kind); }
    /// <summary>そのカテゴリが描かれるか (直接指定 or <see cref="All"/> が有効)。</summary>
    public static bool IsEnabled(string kind) => EnabledKinds.Contains(kind) || EnabledKinds.Contains(All);
    /// <summary>有効なカテゴリ一覧 (DevTools 表示用)。</summary>
    public static IReadOnlyCollection<string> ActiveKinds => EnabledKinds;
    /// <summary>溜まっているコマンド数 (テスト/診断用)。</summary>
    public static int PendingCount => Commands.Count;

    /// <summary>溜めたコマンドを捨てる (flush せずに破棄したいとき)。</summary>
    public static void ClearCommands() => Commands.Clear();
    /// <summary>カテゴリとコマンドを全消去する (シーン切替 / テストの分離)。</summary>
    public static void Reset() { EnabledKinds.Clear(); Commands.Clear(); }

    // ---- 即時 API (ワールド空間、OFF 時ゼロ割り当て) ----

    /// <summary>線分 (3D ワールド)。</summary>
    public static void Line(Vector3 a, Vector3 b, uint color, float width = 1.5f, string kind = Default)
    {
        if (!IsEnabled(kind)) return;
        Commands.Add(new Cmd(Kind.Line, a, b, 0, color, width, 0, null, 0));
    }
    /// <summary>線分 (2D、z=0)。</summary>
    public static void Line(Vector2 a, Vector2 b, uint color, float width = 1.5f, string kind = Default)
        => Line(new Vector3(a, 0), new Vector3(b, 0), color, width, kind);

    /// <summary>矩形アウトライン (XY 平面、<paramref name="min"/>–<paramref name="max"/> の対角)。投影後に 4 辺の折れ線で描く。</summary>
    public static void Rect(Vector3 min, Vector3 max, uint color, float width = 1.5f, string kind = Default)
    {
        if (!IsEnabled(kind)) return;
        Commands.Add(new Cmd(Kind.Rect, min, max, 0, color, width, 0, null, 0));
    }
    /// <summary>矩形アウトライン (2D、左上 + サイズ)。</summary>
    public static void Rect(float x, float y, float w, float h, uint color, float width = 1.5f, string kind = Default)
        => Rect(new Vector3(x, y, 0), new Vector3(x + w, y + h, 0), color, width, kind);

    /// <summary>円アウトライン (XY 平面、<paramref name="segments"/> 角形の折れ線で近似)。</summary>
    public static void Circle(Vector3 center, float radius, uint color, float width = 1.5f, int segments = 24, string kind = Default)
    {
        if (!IsEnabled(kind)) return;
        Commands.Add(new Cmd(Kind.Circle, center, default, radius, color, width, Math.Max(3, segments), null, 0));
    }
    /// <summary>円アウトライン (2D)。</summary>
    public static void Circle(float cx, float cy, float radius, uint color, float width = 1.5f, int segments = 24, string kind = Default)
        => Circle(new Vector3(cx, cy, 0), radius, color, width, segments, kind);

    /// <summary>テキストラベル (投影した位置にスクリーン空間で描く)。</summary>
    public static void Text(Vector3 pos, string text, uint color, float size = 12f, string kind = Default)
    {
        if (!IsEnabled(kind)) return;
        Commands.Add(new Cmd(Kind.Text, pos, default, 0, color, 0, 0, text, size));
    }
    /// <summary>テキストラベル (2D)。</summary>
    public static void Text(float x, float y, string text, uint color, float size = 12f, string kind = Default)
        => Text(new Vector3(x, y, 0), text, color, size, kind);

    /// <summary>
    /// 溜めたコマンドを <paramref name="scene"/> へ描いてクリアする (毎フレーム 1 回)。
    /// <paramref name="worldToScreen"/> でワールド→スクリーン px に投影。テキストは
    /// <paramref name="text"/> 委譲があるときだけ描く (無ければテキストコマンドは黙って飛ばす)。
    /// </summary>
    public static void Flush(Scene2D scene, WorldToScreen worldToScreen, DebugTextDrawer? text = null)
    {
        foreach (Cmd c in Commands)
        {
            switch (c.Shape)
            {
                case Kind.Line:
                    {
                        Vector2 p0 = worldToScreen(c.A), p1 = worldToScreen(c.B);
                        scene.StrokeLine(c.Color, c.Width, p0.X, p0.Y, p1.X, p1.Y);
                        break;
                    }
                case Kind.Rect:
                    {
                        // XY 平面の 4 隅を投影し、閉じた折れ線で描く (透視でも歪んだ四角として正しく出る)
                        float z = c.A.Z;
                        Vector2 a = worldToScreen(new Vector3(c.A.X, c.A.Y, z));
                        Vector2 b = worldToScreen(new Vector3(c.B.X, c.A.Y, z));
                        Vector2 d = worldToScreen(new Vector3(c.B.X, c.B.Y, z));
                        Vector2 e = worldToScreen(new Vector3(c.A.X, c.B.Y, z));
                        scene.StrokePolyline(c.Color, c.Width, a, b, d, e, a);
                        break;
                    }
                case Kind.Circle:
                    {
                        int n = c.Segments;
                        var pts = new Vector2[n + 1];
                        for (int i = 0; i <= n; i++)
                        {
                            float t = i / (float)n * MathF.Tau;
                            var w = new Vector3(c.A.X + MathF.Cos(t) * c.Radius, c.A.Y + MathF.Sin(t) * c.Radius, c.A.Z);
                            pts[i] = worldToScreen(w);
                        }
                        scene.StrokePolyline(c.Color, c.Width, pts);
                        break;
                    }
                case Kind.Text:
                    if (text is not null && c.Text is not null)
                    {
                        Vector2 p = worldToScreen(c.A);
                        text(scene, c.Text, p.X, p.Y, c.TextSize, c.Color);
                    }
                    break;
            }
        }
        Commands.Clear();
    }
}
