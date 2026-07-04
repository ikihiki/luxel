using System.Diagnostics;

namespace Luxel.Diagnostics;

/// <summary>
/// エンジン計装の単一窓口。<see cref="System.Diagnostics.DiagnosticListener"/> ("Luxel") へ
/// イベントを書くだけ。リスナー(別プロジェクト)が居なければ <see cref="IsEnabled"/>=false でゼロコスト。
/// エンジンは「書く」だけで、誰が購読するか・どう使うかは一切知らない (疎結合)。
/// </summary>
public static class EngineDiagnostics
{
    public const string SourceName = "Luxel";

    // イベント名 (購読側と共有)
    public const string Input = "Luxel.Input";          // payload: DiagInput  (離散ログ)
    public const string RenderFlush = "Luxel.Render.Flush"; // payload: DiagFlush  (最新のみ/統計)
    public const string Tree = "Luxel.Tree";            // payload: object (UI ツリー snapshot, 最新のみ、単一)
    public const string Trees = "Luxel.Trees";          // payload: object (複数 UiHost の tree 束、最新のみ)
    public const string UiFrame = "Luxel.UiFrame";      // payload: DiagUiFrame (個別 UiHost の RGBA、per-index 最新)
    public const string Frame = "Luxel.Frame";          // payload: DiagFrame  (最新のみ/フレーム画像)
    public const string Primitives = "Luxel.Primitives"; // payload: DiagPrimitives (最新のみ/2D SoA)
    public const string Gpu = "Luxel.Gpu";              // payload: DiagGpu  (最新のみ/GPU 発行コマンド)
    public const string Resources = "Luxel.Resources";  // payload: DiagResources (最新のみ/ロードグラフ)
    public const string RenderGraph = "Luxel.RenderGraph";  // payload: DiagRenderGraph (最新のみ/パス×リソース DAG)
    public const string Perf = "Luxel.Perf";                // payload: DiagPerf (最新のみ/フレーム時間内訳)
    public const string Ecs = "Luxel.Ecs";                  // payload: DiagEcs (最新のみ/world × entity)
    public const string Surfaces = "Luxel.Surfaces";        // payload: DiagSurfaces (最新のみ/UiSurface 群)
    public const string InputState = "Luxel.InputState";    // payload: DiagInputState (最新のみ/InputStack スナップ)
    public const string EngineState = "Luxel.EngineState";  // payload: DiagEngineState (paused / stepReq)
    public const string Audio = "Luxel.Audio";              // payload: DiagAudio (AudioBus 階層 + Source 群)
    public const string Runtime = "Luxel.Runtime";          // payload: DiagRuntime (GC / Memory / Thread)

    private static readonly DiagnosticListener Listener = new(SourceName);

    /// <summary>このイベントに購読者が居るか (重い payload 構築を避けるガード)。</summary>
    public static bool IsEnabled(string name) => Listener.IsEnabled(name);

    /// <summary>イベントを書く。購読者が居なければ何もしない。</summary>
    public static void Emit(string name, object? payload)
    {
        if (Listener.IsEnabled(name)) Listener.Write(name, payload);
    }
}
