using Luxel;
using Luxel.Input;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Xunit;

namespace Luxel.Tests;

/// <summary>WindowSystem/Window の公開ラッパを Fake バックエンドで検証する (OS 非依存)。</summary>
public class WindowSystemTests
{
    private class FakeWindow : IWindowBackendWindow, IWindowTextInputContextFactory
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
        public Action<WindowPointerEvent>? PointerMoved { get; set; }
        public Action<WindowPointerEvent>? PointerDown { get; set; }
        public Action<WindowPointerEvent>? PointerUp { get; set; }
        public Action<WindowWheelEvent>? Wheel { get; set; }
        public Action<WindowKeyEvent>? KeyDown { get; set; }
        public Action<WindowKeyEvent>? KeyUp { get; set; }
        public Action<string>? TextInput { get; set; }

        public Window? ContextWindow;
        public Func<ITextInputClient?>? ContextClient;
        public Func<float>? ContextScale;

        public IWindowTextInputContext Create(Window window, Func<ITextInputClient?> getClient, Func<float>? getScale = null)
        {
            ContextWindow = window;
            ContextClient = getClient;
            ContextScale = getScale;
            return new FakeTextInputContext();
        }

        public void SetTitle(string title) => LastTitle = title;
        public void SetBounds(int? x, int? y, int? w, int? h) => LastBounds = (x, y, w, h);
        public void Show() => Shown = true;
        public void Hide() => Hidden = true;
        public void Focus() => Focused = true;
        public void Close() { CloseRequested = true; IsClosed = true; Closed?.Invoke(); }
        public void Dispose() => Disposed = true;
    }

    private sealed class OtherFakeWindow : FakeWindow
    {
        public OtherFakeWindow(in WindowDesc desc) : base(desc) { }
    }

    private sealed class FakeTextInputContext : IWindowTextInputContext
    {
        public bool Active => true;
        public bool ShouldDispatchTextInput => false;
        public void Dispose() { }
    }

    private sealed class RecordingInputHandler : IWindowInputHandler
    {
        public readonly List<string> Calls = new();
        public void PointerMoved(WindowPointerEvent input) => Calls.Add($"move:{input.X},{input.Y}");
        public void PointerDown(WindowPointerEvent input) => Calls.Add($"down:{input.Button}");
        public void PointerUp(WindowPointerEvent input) => Calls.Add($"up:{input.Button}");
        public void Wheel(WindowWheelEvent input) => Calls.Add($"wheel:{input.Delta}");
        public void KeyDown(WindowKeyEvent input) => Calls.Add($"key-down:{input.Key}");
        public void KeyUp(WindowKeyEvent input) => Calls.Add($"key-up:{input.Key}");
        public void TextInput(string text) => Calls.Add($"text:{text}");
    }

    private sealed class FakeBackend : IWindowBackend
    {
        public readonly List<FakeWindow> Created = new();
        public bool Disposed;
        public string Name => "Fake";

        public IWindowBackendWindow CreateWindow(in WindowDesc desc)
        {
            var w = new FakeWindow(desc);
            Created.Add(w);
            return w;
        }

        public bool Pump() => Created.Any(w => !w.IsClosed);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeClipboardBackend : IClipboardBackend
    {
        public string Name => "FakeClipboard";
        public string? Text;
        public bool Disposed;
        public string? GetText() => Text;
        public void SetText(string text) => Text = text;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void CreateWindow_Descをバックエンドへそのまま渡す()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        Window win = sys.CreateWindow(new WindowDesc("Hello", 640, 480) { X = 10, Y = 20, Visible = false });

        FakeWindow fake = backend.Created.Single();
        Assert.Equal("Hello", fake.Desc.Title);
        Assert.Equal((640, 480), (fake.Desc.Width, fake.Desc.Height));
        Assert.Equal((10, 20, false), (fake.Desc.X!.Value, fake.Desc.Y!.Value, fake.Desc.Visible));
        Assert.Equal("Hello", win.Title);
        Assert.Single(sys.Windows);
    }

    [Fact]
    public void BackendWindow_具体型を明示的に取得し型不一致を診断する()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        Window window = sys.CreateWindow(new WindowDesc("Typed", 100, 100));
        FakeWindow implementation = backend.Created.Single();

        Assert.Same(implementation, window.BackendWindow);
        Assert.Same(implementation, window.RequireBackendWindow<FakeWindow>());
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            window.RequireBackendWindow<OtherFakeWindow>);
        Assert.Contains(typeof(OtherFakeWindow).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FakeWindow).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pump_閉じたウィンドウを一覧から外す()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        Window a = sys.CreateWindow(new WindowDesc("A", 100, 100));
        Window b = sys.CreateWindow(new WindowDesc("B", 100, 100));
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
        Window win = sys.CreateWindow(new WindowDesc("T", 100, 100));
        FakeWindow fake = backend.Created.Single();

        (int w, int h) resized = default;
        WindowPointerEvent down = default;
        bool closed = false;
        win.Resized += (w, h) => resized = (w, h);
        WindowKeyEvent key = default;
        string? text = null;
        win.PointerDown += input => down = input;
        win.KeyDown += input => key = input;
        win.TextInput += input => text = input;
        win.Closed += () => closed = true;

        fake.Resized!(320, 240);
        fake.PointerDown!(new WindowPointerEvent(5, 6, WindowPointerButton.Right, WindowKeyModifiers.Control));
        fake.KeyDown!(new WindowKeyEvent(WindowKey.A, WindowKeyModifiers.Shift, IsRepeat: true));
        fake.TextInput!("😀");
        Assert.Equal((320, 240), resized);
        Assert.Equal(new WindowPointerEvent(5, 6, WindowPointerButton.Right, WindowKeyModifiers.Control), down);
        Assert.Equal(new WindowKeyEvent(WindowKey.A, WindowKeyModifiers.Shift, IsRepeat: true), key);
        Assert.Equal("😀", text);

        win.SetTitle("New");
        Assert.Equal("New", fake.LastTitle);
        Assert.Equal("New", win.Title);

        win.SetBounds(x: 1, clientWidth: 300);
        Assert.Equal((1, (int?)null, 300, (int?)null), fake.LastBounds!.Value);

        win.Close();
        Assert.True(closed);
    }

    [Fact]
    public void 入力ハンドラは全入力を受け取り解除できる()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        Window window = sys.CreateWindow(new WindowDesc("Input", 100, 100));
        FakeWindow fake = backend.Created.Single();
        var handler = new RecordingInputHandler();
        window.AddInputHandler(handler);
        window.AddInputHandler(handler); // duplicate is ignored

        fake.PointerMoved!(new(1, 2));
        fake.PointerDown!(new(1, 2, WindowPointerButton.Left));
        fake.PointerUp!(new(1, 2, WindowPointerButton.Left));
        fake.Wheel!(new(1, 2, 3));
        fake.KeyDown!(new(WindowKey.A));
        fake.KeyUp!(new(WindowKey.A));
        fake.TextInput!("a");

        Assert.Equal(["move:1,2", "down:Left", "up:Left", "wheel:3", "key-down:A", "key-up:A", "text:a"], handler.Calls);
        Assert.True(window.RemoveInputHandler(handler));
        Assert.False(window.RemoveInputHandler(handler));
        fake.KeyDown!(new(WindowKey.B));
        Assert.Equal(7, handler.Calls.Count);
    }

    [Fact]
    public void WindowがIMEコンテキスト生成をバックエンドへ委譲する()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        Window window = sys.CreateWindow(new WindowDesc("IME", 100, 100));
        Func<ITextInputClient?> client = () => null;
        Func<float> scale = () => 1.5f;

        using IWindowTextInputContext? context = window.CreateTextInputContext(client, scale);

        FakeWindow fake = backend.Created.Single();
        Assert.NotNull(context);
        Assert.Same(window, fake.ContextWindow);
        Assert.Same(client, fake.ContextClient);
        Assert.Same(scale, fake.ContextScale);
    }

    [Fact]
    public void WindowInputSourceがWindow入力を変換してPollでdrainする()
    {
        var backend = new FakeBackend();
        using var sys = new WindowSystem(backend);
        Window window = sys.CreateWindow(new WindowDesc("Input", 100, 100));
        FakeWindow fake = backend.Created.Single();
        using WindowInputSource source = window.CreateInputSource("test-window");

        fake.KeyDown!(new(WindowKey.A));
        fake.PointerDown!(new(3, 4, WindowPointerButton.X1));
        fake.PointerMoved!(new(5, 6));
        fake.Wheel!(new(5, 6, -2));
        fake.KeyUp!(new(WindowKey.A));

        var bus = new InputBus();
        source.Poll(bus);
        Assert.Collection(bus.Events,
            e => Assert.Equal((InputEventKind.KeyDown, KeyCode.A), (e.Kind, e.Key)),
            e => Assert.Equal((InputEventKind.KeyDown, KeyCode.Mouse3), (e.Kind, e.Key)),
            e => Assert.Equal((InputEventKind.PointerMoved, 5f, 6f), (e.Kind, e.Value, e.ValueY)),
            e => Assert.Equal((InputEventKind.AxisChanged, AxisCode.MouseWheel, -2f), (e.Kind, e.Axis, e.Value)),
            e => Assert.Equal((InputEventKind.KeyUp, KeyCode.A), (e.Kind, e.Key)));
        Assert.Equal(KeyCode.A, source.TakePressed());
        Assert.Null(source.TakePressed());

        bus.Clear();
        source.Poll(bus);
        Assert.Empty(bus.Events);
        source.Dispose();
        fake.KeyDown!(new(WindowKey.B));
        source.Poll(bus);
        Assert.Empty(bus.Events);
        Assert.Null(source.TakePressed());
    }

    [Fact]
    public void ClipboardはWindowなしでバックエンドを包む()
    {
        var backend = new FakeClipboardBackend { Text = "before" };
        using var clipboard = new Clipboard(backend);
        Assert.Equal("FakeClipboard", clipboard.Name);
        Assert.Equal("before", clipboard.GetText());
        clipboard.SetText("after");
        Assert.Equal("after", backend.Text);
        clipboard.Dispose();
        Assert.True(backend.Disposed);
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
}
