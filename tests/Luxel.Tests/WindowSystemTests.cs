using Luxel;
using Luxel.Abstraction;
using Xunit;

namespace Luxel.Tests;

/// <summary>WindowSystem/NativeWindow の公開ラッパを Fake バックエンドで検証する (OS 非依存)。</summary>
public class WindowSystemTests
{
    private sealed class FakeWindow : IWindowBackendWindow
    {
        public WindowDesc Desc;
        public string? LastTitle;
        public (int? x, int? y, int? w, int? h)? LastBounds;
        public bool Disposed;
        public bool Shown, Hidden, Focused, CloseRequested;

        public FakeWindow(in WindowDesc desc) { Desc = desc; Width = desc.Width; Height = desc.Height; }

        public nint Handle => 0x1234;
        public int Width { get; set; }
        public int Height { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsClosed { get; set; }
        public bool IsVisible => !Hidden;
        public bool IsFocused => Focused;

        public Action<int, int>? Resized { get; set; }
        public Action<int, int>? Moved { get; set; }
        public Action? Closed { get; set; }
        public Action<bool>? FocusChanged { get; set; }
        public Action<float, float>? MouseMoved { get; set; }
        public Action<float, float, int>? MouseDown { get; set; }
        public Action<float, float, int>? MouseUp { get; set; }
        public Action<float, float, float>? Wheeled { get; set; }
        public Action<ushort>? KeyDown { get; set; }
        public Action<ushort>? KeyUp { get; set; }
        public Action<char>? CharTyped { get; set; }
        public Func<ushort, nint, bool>? KeyPreFilter { get; set; }

        public void SetTitle(string title) => LastTitle = title;
        public void SetBounds(int? x, int? y, int? w, int? h) => LastBounds = (x, y, w, h);
        public void Show() => Shown = true;
        public void Hide() => Hidden = true;
        public void Focus() => Focused = true;
        public void Close() { CloseRequested = true; IsClosed = true; Closed?.Invoke(); }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeBackend : IWindowBackend
    {
        public readonly List<FakeWindow> Created = new();
        public bool Disposed;
        public int WaitCalls;
        public WaitHandle? LastWakeSignal;
        public int LastTimeout;
        public string Name => "Fake";

        public IWindowBackendWindow CreateWindow(in WindowDesc desc)
        {
            var w = new FakeWindow(desc);
            Created.Add(w);
            return w;
        }

        public bool Pump() => Created.Any(w => !w.IsClosed);
        public bool WaitForEvents(WaitHandle? wakeSignal, int timeoutMilliseconds)
        {
            WaitCalls++;
            LastWakeSignal = wakeSignal;
            LastTimeout = timeoutMilliseconds;
            return Pump();
        }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void CreateWindow_Descをバックエンドへそのまま渡す()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        NativeWindow win = sys.CreateWindow(new WindowDesc("Hello", 640, 480) { X = 10, Y = 20, Visible = false });

        FakeWindow fake = backend.Created.Single();
        Assert.Equal("Hello", fake.Desc.Title);
        Assert.Equal((640, 480), (fake.Desc.Width, fake.Desc.Height));
        Assert.Equal((10, 20, false), (fake.Desc.X!.Value, fake.Desc.Y!.Value, fake.Desc.Visible));
        Assert.Equal("Hello", win.Title);
        Assert.Single(sys.Windows);
    }

    [Fact]
    public void Pump_閉じたウィンドウを一覧から外す()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        NativeWindow a = sys.CreateWindow(new WindowDesc("A", 100, 100));
        NativeWindow b = sys.CreateWindow(new WindowDesc("B", 100, 100));
        Assert.Equal(2, sys.Windows.Count);

        a.Close();
        Assert.True(sys.Pump());               // B が生存
        Assert.Single(sys.Windows);
        Assert.Equal("B", sys.Windows[0].Title);

        b.Close();
        Assert.False(sys.Pump());              // 全滅
        Assert.Empty(sys.Windows);
    }

    [Fact]
    public void イベント中継とタイトル_操作の委譲()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        NativeWindow win = sys.CreateWindow(new WindowDesc("T", 100, 100));
        FakeWindow fake = backend.Created.Single();

        (int w, int h) resized = default;
        (float x, float y, int b) down = default;
        bool closed = false;
        win.Resized += (w, h) => resized = (w, h);
        win.MouseDown += (x, y, b) => down = (x, y, b);
        win.Closed += () => closed = true;

        fake.Resized!(320, 240);
        fake.MouseDown!(5, 6, 1);
        Assert.Equal((320, 240), resized);
        Assert.Equal((5f, 6f, 1), down);

        win.SetTitle("New");
        Assert.Equal("New", fake.LastTitle);
        Assert.Equal("New", win.Title);

        win.SetBounds(x: 1, clientWidth: 300);
        Assert.Equal((1, (int?)null, 300, (int?)null), fake.LastBounds!.Value);

        win.Close();
        Assert.True(closed);
    }

    [Fact]
    public void Dispose_全ウィンドウとバックエンドを破棄()
    {
        var backend = new FakeBackend();
        var sys = new WindowSystem(backend);
        sys.CreateWindow(new WindowDesc("A", 100, 100));
        sys.CreateWindow(new WindowDesc("B", 100, 100));
        sys.Dispose();

        Assert.All(backend.Created, w => Assert.True(w.Disposed));
        Assert.True(backend.Disposed);
        Assert.Throws<ObjectDisposedException>(() => sys.CreateWindow(new WindowDesc("C", 1, 1)));
    }

    [Fact]
    public void WaitForEvents_外部シグナルとタイムアウトをバックエンドへ渡す()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        sys.CreateWindow(new WindowDesc("A", 100, 100));
        using var wake = new AutoResetEvent(false);

        Assert.True(sys.WaitForEvents(wake, 25));
        Assert.Equal(1, backend.WaitCalls);
        Assert.Same(wake, backend.LastWakeSignal);
        Assert.Equal(25, backend.LastTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => sys.WaitForEvents(timeoutMilliseconds: -2));
    }
}
