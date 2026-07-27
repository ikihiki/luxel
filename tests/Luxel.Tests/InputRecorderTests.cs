using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

/// <summary>入力記録 → 決定的リプレイ (11-B)。GpuDevice なし (UiHost headless) で検証。</summary>
public class InputRecorderTests
{
    private static (UiHost Host, Button Btn, System.Func<int, System.Threading.Tasks.Task> Step) NewCounter(System.Action onClick)
    {
        Button btn = Button(_ => onClick(), "inc");
        var host = new UiHost(new RetainedCanvas(), VectorFont.LoadSystem(), 400, 200);
        host.SetRoot(btn);
        System.Func<int, System.Threading.Tasks.Task> step = n =>
        {
            for (int i = 0; i < n; i++) host.Tick(1f / 60f);
            return System.Threading.Tasks.Task.CompletedTask;
        };
        return (host, btn, step);
    }

    [Fact]
    public async System.Threading.Tasks.Task Record_ThenReplay_ReproducesClicks()
    {
        int count = 0;
        (UiHost host, Button btn, var step) = NewCounter(() => count++);
        float cx = btn.WorldPos.X + btn.Size.Width / 2;
        float cy = btn.WorldPos.Y + btn.Size.Height / 2;

        var rec = new InputRecorder();
        rec.Attach(host);
        var driver = new PlayDriver(host, step, _ => { });

        // 記録: 3 クリック
        rec.Start();
        await driver.Click(cx, cy);
        await driver.Click(cx, cy);
        await driver.Click(cx, cy);
        rec.Stop();
        InputRecording recording = rec.Snapshot();

        Assert.Equal(3, count);
        Assert.True(recording.Frames > 0, "フレームが進んでいない (Ticked 未発火?)");
        // クリック 1 回 = pointerdown + pointerup の 2 イベント
        Assert.Equal(6, recording.Events.Count);
        rec.Detach();   // 再生中に再記録しない

        // リプレイ: 状態をリセットして記録を流す → 同じ回数クリックされる
        count = 0;
        await InputReplayer.Replay(host, recording, step);
        Assert.Equal(3, count);
    }

    [Fact]
    public async System.Threading.Tasks.Task CapturedEvents_HaveExpectedKindsAndFrames()
    {
        (UiHost host, Button btn, var step) = NewCounter(() => { });
        float cx = btn.WorldPos.X + btn.Size.Width / 2;
        float cy = btn.WorldPos.Y + btn.Size.Height / 2;

        var rec = new InputRecorder();
        rec.Attach(host);
        var driver = new PlayDriver(host, step, _ => { });

        rec.Start();
        await driver.Click(cx, cy);          // down @0, up @1
        rec.Stop();
        InputRecording r = rec.Snapshot();

        Assert.Equal(InputKind.PointerDown, r.Events[0].Kind);
        Assert.Equal(0, r.Events[0].Frame);
        Assert.Equal(InputKind.PointerUp, r.Events[1].Kind);
        Assert.Equal(1, r.Events[1].Frame);   // Click は down → step(1) → up
        Assert.Equal(cx, r.Events[0].X, 3);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var rec0 = new InputRecording(InputRecording.CurrentVersion, 5, new[]
        {
            new RecordedInput(0, InputKind.PointerDown, 10, 20),
            new RecordedInput(1, InputKind.PointerUp, 10, 20),
            new RecordedInput(2, InputKind.KeyDown, Key: Key.Enter, Shift: true),
            new RecordedInput(3, InputKind.Char, Text: "a"),
            new RecordedInput(4, InputKind.Wheel, 5, 6, 1.5f),
        });

        InputRecording rec1 = InputRecording.FromJson(rec0.ToJson());

        Assert.Equal(rec0.Version, rec1.Version);
        Assert.Equal(rec0.Frames, rec1.Frames);
        Assert.Equal(rec0.Events, rec1.Events);   // RecordedInput は値等価
    }

    [Fact]
    public void Json_RejectsWrongVersion()
    {
        string json = new InputRecording(999, 0, []).ToJson();
        Assert.Throws<System.FormatException>(() => InputRecording.FromJson(json));
    }

    [Fact]
    public void PlayCode_CoalescesClickDragTypeKeyWheel()
    {
        var rec = new InputRecording(InputRecording.CurrentVersion, 10, new[]
        {
            // クリック (down → up、移動なし)
            new RecordedInput(0, InputKind.PointerDown, 10, 20),
            new RecordedInput(1, InputKind.PointerUp, 10, 20),
            // ドラッグ (down → move → up)
            new RecordedInput(2, InputKind.PointerDown, 0, 0),
            new RecordedInput(3, InputKind.PointerMove, 50, 25),
            new RecordedInput(4, InputKind.PointerMove, 100, 50),
            new RecordedInput(5, InputKind.PointerUp, 100, 50),
            // タイプ (連続 Char)
            new RecordedInput(6, InputKind.Char, Text: "h"),
            new RecordedInput(6, InputKind.Char, Text: "i"),
            // キー
            new RecordedInput(7, InputKind.KeyDown, Key: Key.Enter),
            // ホイール
            new RecordedInput(8, InputKind.Wheel, 200, 200, 1.5f),
        });

        string code = InputScript.ToPlayCode(rec);

        Assert.Contains("await d.Click(10, 20);", code);
        Assert.Contains("await d.Drag(0, 0, 100, 50);", code);
        Assert.Contains("await d.Type(\"hi\");", code);
        Assert.Contains("d.Key(Key.Enter);", code);
        Assert.Contains("d.Wheel(200, 200, 1.5);", code);
    }
}
