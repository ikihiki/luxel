namespace Luxel.UI;

/// <summary>記録済み入力を<b>決定的</b>に再生する。入力ドライバと同じく
/// 「フレーム前進を外から注入する」形 (step デリゲート) — E2E ランナーなら固定 dt 同期、
/// ライブ UI 内再生なら実フレーム待ち、どちらでも同じ記録が同じ結果になる。</summary>
public static class InputReplayer
{
    /// <summary>記録を host へ再生する。フレーム f のイベントを配送 → 1 フレーム step、を
    /// <see cref="InputRecording.Frames"/> まで繰り返す (記録した静定フレームまで再現)。
    /// <para>決定性の要: wall-clock を挟まないこと。<paramref name="step"/> は固定 dt で進める
    /// (E2E: <c>host.Step(1/60)</c> を n 回 / live: 実フレーム待ち)。</para></summary>
    public static async Task Replay(UiHost host, InputRecording rec, Func<int, Task> step)
    {
        int idx = 0;
        for (int frame = 0; frame <= rec.Frames; frame++)
        {
            while (idx < rec.Events.Count && rec.Events[idx].Frame == frame)
                Dispatch(host, rec.Events[idx++]);
            if (frame < rec.Frames) await step(1);
        }
    }

    private static void Dispatch(UiHost host, RecordedInput e)
    {
        switch (e.Kind)
        {
            case InputKind.PointerDown: host.PointerDown(e.X, e.Y); break;
            case InputKind.PointerUp: host.PointerUp(e.X, e.Y); break;
            case InputKind.PointerMove: host.PointerMove(e.X, e.Y); break;
            case InputKind.Wheel: host.Wheel(e.X, e.Y, e.Delta); break;
            case InputKind.KeyDown: host.KeyDown(e.Key, e.Shift, e.Ctrl, e.Alt); break;
            case InputKind.Char: host.Char(e.Text); break;
        }
    }
}
