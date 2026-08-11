namespace Luxel.UI;

/// <summary>UiHost に流れた入力を<b>フレーム番号付き</b>で記録する。
/// <para>UiHost の <see cref="UiHost.InputCaptured"/> (各操作) と <see cref="UiHost.Ticked"/>
/// (フレーム前進) を購読するだけ — 記録中でないときは何もしない。記録は
/// <see cref="InputReplayer"/> で決定的に再生でき、<see cref="InputScript.ToPlayCode"/> で
/// play コードに起こせる。</para>
/// <para>スレッド規約: UiHost と同じ (UI スレッド専有)。</para></summary>
public sealed class InputRecorder
{
    private readonly List<RecordedInput> _events = new();
    private UiHost? _host;
    private int _frame;
    private bool _recording;

    /// <summary>記録中か。</summary>
    public bool Recording => _recording;

    /// <summary>これまでに記録した操作数 (記録中の UI 表示用)。</summary>
    public int Count => _events.Count;

    /// <summary>host のイベントに購読する。多重 Attach は最後のものだけ有効 (先に Detach)。</summary>
    public void Attach(UiHost host)
    {
        Detach();
        _host = host;
        host.InputCaptured += OnInput;
        host.Ticked += OnTick;
    }

    /// <summary>購読を解除する (未 Attach は no-op)。記録は保持したまま。</summary>
    public void Detach()
    {
        if (_host is null) return;
        _host.InputCaptured -= OnInput;
        _host.Ticked -= OnTick;
        _host = null;
    }

    /// <summary>記録を開始する (既存の記録は破棄、フレーム 0 起点)。</summary>
    public void Start()
    {
        _events.Clear();
        _frame = 0;
        _recording = true;
    }

    /// <summary>記録を停止する (フレームカウンタはそのまま — 直後の <see cref="Snapshot"/> が総フレーム数を持つ)。</summary>
    public void Stop() => _recording = false;

    /// <summary>現在の記録をイミュータブルなスナップショットとして取り出す。</summary>
    public InputRecording Snapshot() => new(InputRecording.CurrentVersion, _frame, _events.ToArray());

    private void OnInput(RecordedInput e)
    {
        if (_recording) _events.Add(e with { Frame = _frame });
    }

    private void OnTick()
    {
        if (_recording) _frame++;
    }
}
