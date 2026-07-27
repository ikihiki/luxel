namespace Luxel.Diagnostics;

/// <summary>入力/操作イベント (離散ログ用)。<paramref name="Op"/>=click/keydown/char/compose/commit/focus/resize 等。</summary>
public readonly record struct DiagInput(string Op, string Info);

/// <summary>描画フラッシュ統計 (最新のみ保持/coalesce 対象)。RetainedCanvas.Flush の結果。</summary>
public readonly record struct DiagFlush(
    int TransformWrites, int StyleWrites, long SegmentBytes, bool FullRebuild, uint Width, uint Height);

/// <summary>フレーム画像 (最新のみ保持/coalesce 対象)。RGBA8 密 (stride=Width*4)。</summary>
public sealed record DiagFrame(int Width, int Height, byte[] Rgba);

/// <summary>個別 UiHost のフレーム画像 (最新のみ、per-host index)。RGBA8 密。</summary>
public sealed record DiagUiFrame(int Index, string Name, int Width, int Height, byte[] Rgba);

/// <summary>最終 2D プリミティブ 1 パス分 (GPU へ送る SoA の per-path レコード + 解決済み色)。</summary>
public readonly record struct DiagPath(
    int Index, uint SegStart, uint SegCount, uint TransformSlot, uint StyleSlot, uint ClipSlot,
    uint Kind, uint ColorRgba, float Opacity);

/// <summary>最終 2D プリミティブデータ (GPU 常駐 SoA バッファのスナップショット)。</summary>
public sealed record DiagPrimitives(
    int SegmentCount, int PathCount, int TransformCount, int StyleCount, int ClipCount, int OrderCount,
    long SegmentBytes, DiagPath[] Paths);

/// <summary>GPU へ発行された 1 コマンド。<paramref name="Kind"/>=upload/update/pipeline/rootargs/dispatch/barrier。</summary>
public readonly record struct DiagGpuCommand(string Kind, string Text);

/// <summary>1 フレームで GPU へ発行されたコマンド列。</summary>
public sealed record DiagGpu(DiagGpuCommand[] Commands);

/// <summary>リソース 1 ノード ((型,uri) 単位)。どのステップ/実行器で生成され、何に依存したか。</summary>
public readonly record struct DiagResourceNode(
    string Key, string Type, string Uri, string Status, int Version, string Step, string Executor, string[] Inputs);

/// <summary>ロード済みリソースのグラフ (何が・どのステップでロードされたか)。</summary>
public sealed record DiagResources(DiagResourceNode[] Nodes);

/// <summary>レンダーグラフ 1 パス分 (Compile 後の最終状態)。</summary>
public readonly record struct DiagRenderGraphPass(
    int Index, string Name, string Queue, bool Culled, int[] Reads, int[] Writes);

/// <summary>レンダーグラフ 1 リソース分 (Compile 後の物理割当含む)。</summary>
public readonly record struct DiagRenderGraphResource(
    int Id, string Name, string Kind, bool IsAliased, int PhysicalSlot,
    int FirstWritePass, int LastReadPass, ulong SizeBytes);

/// <summary>レンダーグラフの最終形 (パス × リソース DAG)。culling/aliasing 後のスナップショット。</summary>
public sealed record DiagRenderGraph(
    DiagRenderGraphPass[] Passes,
    DiagRenderGraphResource[] Resources,
    int PhysicalTransientCount,
    int ExecutedPassCount);

/// <summary>1 phase の実行時間 (ms)。</summary>
public readonly record struct DiagPhaseTiming(string Name, double Ms);

/// <summary>1 System (Friflo) 1 フレームの実行時間 (ms)。<paramref name="Phase"/> は EarlyUpdate/Update/...。</summary>
public readonly record struct DiagSystemTiming(string Phase, string Name, double Ms);

/// <summary>
/// FixedUpdate (固定タイムステップ) の 1 フレーム分の統計。<see cref="DiagPerf"/> に付随。
/// <paramref name="StepsThisFrame"/> がこのフレームで回った固定ステップ回数、<paramref name="Alpha"/> は描画補間係数 [0,1)、
/// <paramref name="AccumulatorMs"/> は次ステップまでに溜まっている未消化時間、<paramref name="DroppedTotal"/> は
/// MaxSteps 超過で捨てた固定ステップの累計 (0 でなければ処理落ちの兆候)。
/// </summary>
public readonly record struct DiagFixedStep(
    int StepsThisFrame, float Alpha, double AccumulatorMs, double FixedDeltaMs, long DroppedTotal);

/// <summary>1 フレームの性能内訳 (最新のみ保持)。System 単位の詳細は <see cref="Systems"/>。</summary>
public sealed record DiagPerf(
    long Frame,
    double FrameMs,
    double FpsAvg,
    DiagPhaseTiming[] Phases,
    DiagSystemTiming[] Systems,
    DiagFixedStep Fixed);

/// <summary>ECS 1 component のスナップショット (型名 + JSON 直列化した値)。</summary>
public readonly record struct DiagComponent(string Type, string Json);

/// <summary>ECS 1 entity のスナップショット (Component は値付き、Tag は型名のみ、read-only)。</summary>
public readonly record struct DiagEntity(int Id, DiagComponent[] Components, string[] TagTypes);

/// <summary>ECS 1 world のスナップショット。</summary>
public sealed record DiagWorld(string Name, int EntityCount, DiagEntity[] Entities);

/// <summary>複数 world 分の ECS スナップショット (最新のみ保持、on-demand emit)。
/// 数百〜数千 entity では全量が破綻するため、通常は <see cref="DiagEcsSummary"/> (一覧) +
/// 選択 entity のみの詳細 (この型) に切り替える。エンティティ数が閾値以下なら全量フォールバックで使う。</summary>
public sealed record DiagEcs(DiagWorld[] Worlds);

/// <summary>ECS 一覧用の 1 entity 軽量サマリ (値は含まない — id + 表示名 + アーキタイプのみ)。
/// <paramref name="Name"/> は <c>DebugName</c> component があればその名、無ければ空文字 (UI が Id 表示)。
/// <paramref name="Archetype"/> は所持 component/tag の型名列。</summary>
public readonly record struct DiagEntitySummary(int Id, string Name, string[] Archetype);

/// <summary>1 world 分の ECS サマリ。<paramref name="EntityCount"/> は総数、<paramref name="ShownCount"/> は
/// フィルタ/上限適用後に一覧へ載せた数 (差があれば truncation を UI に示せる)。</summary>
public sealed record DiagWorldSummary(string Name, int EntityCount, int ShownCount, DiagEntitySummary[] Entities);

/// <summary>複数 world 分の ECS 軽量サマリ (最新のみ保持、30f 毎)。値なしなので数千 entity でも安価。</summary>
public sealed record DiagEcsSummary(DiagWorldSummary[] Worlds);

/// <summary>ゲームが観測に載せた 1 件の key-value (<c>DevStats.Set</c>)。値は文字列化済み。</summary>
public readonly record struct DiagStat(string Key, string Value);

/// <summary>ゲーム側カスタム統計のスナップショット (<c>DevStats</c>、最新のみ保持、30f 毎)。
/// score/残弾/HP/状態機械の現在状態など「1 行で観測に載せる」printf デバッグの受け皿。</summary>
public sealed record DiagCustom(DiagStat[] Stats);

/// <summary>1 UiSurface のスナップショット (レート/累積描画/直近時刻等)。</summary>
public readonly record struct DiagSurface(
    string Name, string Kind, int Width, int Height, string Format,
    float RateHz, long RenderedFrames, double LastRenderedTime, bool NeedsRedraw);

/// <summary>登録された UiSurface 全体のスナップショット (最新のみ保持)。</summary>
public sealed record DiagSurfaces(DiagSurface[] Surfaces);

/// <summary>1 InputAction 分のスナップショット。<paramref name="Kind"/>=Button/Axis1D/Axis2D、<paramref name="Value"/> は JSON。</summary>
public readonly record struct DiagInputAction(string Name, string Kind, bool IsActive, string Value);

/// <summary>1 InputContext 分のスナップショット。</summary>
public readonly record struct DiagInputContext(string Name, bool Suspended, DiagInputAction[] Actions);

/// <summary>InputStack の全 context (最上位が先) のスナップショット。最新のみ保持。</summary>
public sealed record DiagInputState(DiagInputContext[] Contexts);

/// <summary>ゲームループの制御状態 (pause / step)。</summary>
public sealed record DiagEngineState(bool Paused);

/// <summary>1 AudioBus のスナップショット。Parent と EffectiveVolume で階層が復元できる。</summary>
public readonly record struct DiagAudioBus(string Name, string? Parent, float Volume, float EffectiveVolume);

/// <summary>1 AudioSource のスナップショット (Play state と各種 signal 値)。</summary>
public readonly record struct DiagAudioSource(
    string Name, bool IsPlaying, float Volume, float Pitch, float Pan, string? Bus);

/// <summary>登録済み AudioBus / AudioSource + Mixer 状態のスナップショット (最新のみ)。</summary>
public sealed record DiagAudio(int ActiveVoiceCount, DiagAudioBus[] Buses, DiagAudioSource[] Sources);

/// <summary>
/// .NET ランタイムのメトリクス (メモリ / GC / スレッド)。1 秒ごとの emit を想定。
/// <paramref name="TotalMemoryBytes"/> は <see cref="GC.GetTotalMemory(bool)"/> の非強制値。
/// </summary>
public sealed record DiagRuntime(
    long TotalMemoryBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadCount,
    int WorkingSetMB);
