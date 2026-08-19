using System.Text.Json;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public enum EditorThemePreference { System, Light, Dark }

public sealed record EditorSettings
{
    public EditorThemePreference Theme { get; init; } = EditorThemePreference.System;
    public float UiScale { get; init; } = 1f;
    public string EditorFont { get; init; } = "UDEV Gothic";
    public TimeSpan AutosaveInterval { get; init; } = TimeSpan.FromMinutes(2);
    public bool AutosaveEnabled { get; init; } = true;
    public bool ConfirmExit { get; init; } = true;

    public static EditorSettings Defaults { get; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Theme)) errors.Add("Theme preference is invalid.");
        if (!float.IsFinite(UiScale) || UiScale is < 0.5f or > 3f) errors.Add("UI scale must be between 0.5 and 3.0.");
        if (string.IsNullOrWhiteSpace(EditorFont)) errors.Add("Editor font is required.");
        if (AutosaveInterval < TimeSpan.FromSeconds(10) || AutosaveInterval > TimeSpan.FromHours(1))
            errors.Add("Autosave interval must be between 10 seconds and 1 hour.");
        return errors;
    }

    /// <summary>Resolve the color preference and apply UI-scale to all shell/control dimension tokens.</summary>
    public Theme ResolveTheme(Theme systemTheme)
    {
        Theme colors = Theme switch
        {
            EditorThemePreference.Light => Luxel.UI.Theme.Light,
            EditorThemePreference.Dark => Luxel.UI.Theme.Dark,
            _ => systemTheme,
        };
        float scale = UiScale;
        return colors with
        {
            Radius = colors.Radius * scale,
            RadiusLg = colors.RadiusLg * scale,
            Space = colors.Space * scale,
            Font = colors.Font * scale,
            FontSm = colors.FontSm * scale,
            FontLg = colors.FontLg * scale,
            FontHeading = colors.FontHeading * scale,
            ControlH = colors.ControlH * scale,
            BtnPadX = colors.BtnPadX * scale,
            BtnPadY = colors.BtnPadY * scale,
            PadIn = colors.PadIn * scale,
            CheckBox = colors.CheckBox * scale,
            CheckGap = colors.CheckGap * scale,
        };
    }
}

/// <summary>Cancellable interval scheduling abstraction used by Editor autosave and deterministic tests.</summary>
public interface IEditorIntervalScheduler
{
    IDisposable Schedule(TimeSpan interval, Action callback);
}

/// <summary>
/// An interval scheduler advanced explicitly by the UI host. Callbacks run synchronously on the pumping thread,
/// never overlap, and missed intervals are coalesced into one callback per pump.
/// </summary>
public interface IEditorIntervalPump : IEditorIntervalScheduler
{
    void Pump(TimeSpan elapsed);
}

public sealed class SerializedEditorIntervalPump : IEditorIntervalPump, IDisposable
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private TimeSpan _now;
    private TimeSpan _pendingElapsed;
    private bool _pumping;
    private bool _disposed;

    public IDisposable Schedule(TimeSpan interval, Action callback)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var entry = new Entry(this, interval, callback, _now + interval);
            _entries.Add(entry);
            return entry;
        }
    }

    public void Pump(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        lock (_gate)
        {
            if (_disposed) return;
            _pendingElapsed += elapsed;
            if (_pumping) return;
            _pumping = true;
            try
            {
                while (_pendingElapsed > TimeSpan.Zero && !_disposed)
                {
                    _now += _pendingElapsed;
                    _pendingElapsed = TimeSpan.Zero;
                    RunDueCallbacks();
                }
            }
            finally { _pumping = false; }
        }
    }

    private void RunDueCallbacks()
    {
        while (!_disposed)
        {
            Entry? due = _entries.FirstOrDefault(x => !x.IsDisposed && x.NextDue <= _now);
            if (due is null) break;
            due.NextDue = _now + due.Interval;
            due.Callback();
            _entries.RemoveAll(static x => x.IsDisposed);
        }
    }

    private void Cancel(Entry entry)
    {
        lock (_gate)
        {
            entry.IsDisposed = true;
            if (!_pumping) _entries.Remove(entry);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Entry entry in _entries) entry.IsDisposed = true;
            _entries.Clear();
            _pendingElapsed = TimeSpan.Zero;
        }
    }

    private sealed class Entry(
        SerializedEditorIntervalPump owner, TimeSpan interval, Action callback, TimeSpan nextDue) : IDisposable
    {
        public TimeSpan Interval { get; } = interval;
        public Action Callback { get; } = callback;
        public TimeSpan NextDue { get; set; } = nextDue;
        public bool IsDisposed { get; set; }
        public void Dispose() => owner.Cancel(this);
    }
}

/// <summary>Re-schedules autosave when settings change and cancels the active interval on disposal.</summary>
public sealed class EditorAutosaveScheduler : IDisposable
{
    private readonly EditorSettingsService _settings;
    private readonly IEditorIntervalScheduler _scheduler;
    private readonly Action _autosave;
    private readonly IDisposable _settingsEffect;
    private readonly object _callbackGate = new();
    private IDisposable? _scheduled;
    private bool _initialized;
    private bool _callbackRunning;
    private bool _disposed;
    private int _callbackThreadId;
    private int _scheduleVersion;
    private EditorSettings? _last;

    public EditorAutosaveScheduler(EditorSettingsService settings, IEditorIntervalScheduler scheduler, Action autosave)
    {
        _settings = settings;
        _scheduler = scheduler;
        _autosave = autosave;
        _settingsEffect = Reactive.Effect(Update);
    }

    private void Update()
    {
        EditorSettings current = _settings.Current.Value;
        IDisposable? previous;
        int version;
        lock (_callbackGate)
        {
            if (_disposed || _initialized && current == _last) return;
            _initialized = true;
            _last = current;
            previous = _scheduled;
            _scheduled = null;
            version = ++_scheduleVersion;
        }

        previous?.Dispose();
        IDisposable? replacement = current.AutosaveEnabled
            ? _scheduler.Schedule(current.AutosaveInterval, InvokeAutosave)
            : null;
        bool discard;
        lock (_callbackGate)
        {
            discard = _disposed || version != _scheduleVersion;
            if (!discard) _scheduled = replacement;
        }
        if (discard) replacement?.Dispose();
    }

    private void InvokeAutosave()
    {
        lock (_callbackGate)
        {
            if (_disposed || _callbackRunning) return;
            _callbackRunning = true;
            _callbackThreadId = Environment.CurrentManagedThreadId;
        }

        try { _autosave(); }
        finally
        {
            lock (_callbackGate)
            {
                _callbackRunning = false;
                _callbackThreadId = 0;
                Monitor.PulseAll(_callbackGate);
            }
        }
    }

    public void Dispose()
    {
        IDisposable? scheduled;
        lock (_callbackGate)
        {
            _disposed = true;
            _scheduleVersion++;
            scheduled = _scheduled;
            _scheduled = null;
        }
        _settingsEffect.Dispose();
        scheduled?.Dispose();
        lock (_callbackGate)
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            while (_callbackRunning && _callbackThreadId != currentThreadId) Monitor.Wait(_callbackGate);
        }
    }
}

public sealed class EditorSettingsService
{
    public const string SettingsKey = "editor.settings.v1";
    private readonly IEditorSettingsStore _store;
    public Signal<EditorSettings> Current { get; }
    public Signal<string?> Error { get; } = new(null);

    public EditorSettingsService(IEditorSettingsStore store)
    {
        _store = store;
        Current = new Signal<EditorSettings>(Load());
    }

    public EditorSettings Load()
    {
        try
        {
            string? json = _store.Read(SettingsKey);
            if (string.IsNullOrWhiteSpace(json)) { Error.Value = null; return EditorSettings.Defaults; }
            EditorSettings value = JsonSerializer.Deserialize<EditorSettings>(json) ?? EditorSettings.Defaults;
            IReadOnlyList<string> errors = value.Validate();
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            Error.Value = null;
            return value;
        }
        catch (Exception ex)
        {
            Error.Value = ex.Message;
            return EditorSettings.Defaults;
        }
    }

    public bool Reload()
    {
        EditorSettings loaded = Load();
        if (Error.Peek() is not null) return false;
        Current.Value = loaded;
        return true;
    }

    public bool Apply(EditorSettings settings)
    {
        IReadOnlyList<string> errors = settings.Validate();
        if (errors.Count > 0) { Error.Value = string.Join(" ", errors); return false; }
        try
        {
            _store.Write(SettingsKey, JsonSerializer.Serialize(settings));
            Current.Value = settings;
            Error.Value = null;
            return true;
        }
        catch (Exception ex)
        {
            Error.Value = ex.Message;
            return false;
        }
    }

    public bool Reset() => Apply(EditorSettings.Defaults);
}

public sealed class SettingsView : CompositeControl
{
    private readonly Signal<int> _theme = new(0);
    private readonly Signal<string> _scale = new("1");
    private readonly Signal<string> _font = new("");
    private readonly Signal<string> _autosaveSeconds = new("120");
    private readonly Signal<bool> _autosave = new(true);
    private readonly Signal<bool> _confirmExit = new(true);

    public SettingsView(EditorSettingsService settings)
    {
        Settings = settings;
        LoadDraft(settings.Current.Peek());
    }

    public EditorSettingsService Settings { get; }
    public Signal<string?> ActionError { get; } = new(null);

    public bool Apply(EditorSettings value)
    {
        bool applied = Settings.Apply(value);
        ActionError.Value = applied ? null : Settings.Error.Peek();
        if (applied) LoadDraft(value);
        return applied;
    }

    public bool Apply()
    {
        if (!float.TryParse(_scale.Peek(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float scale)
            || !double.TryParse(_autosaveSeconds.Peek(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double seconds))
        {
            ActionError.Value = "UI scale and autosave interval must be numeric.";
            return false;
        }
        var value = new EditorSettings
        {
            Theme = (EditorThemePreference)Math.Clamp(_theme.Peek(), 0, 2),
            UiScale = scale,
            EditorFont = _font.Peek(),
            AutosaveEnabled = _autosave.Peek(),
            AutosaveInterval = TimeSpan.FromSeconds(seconds),
            ConfirmExit = _confirmExit.Peek(),
        };
        bool applied = Settings.Apply(value);
        ActionError.Value = applied ? null : Settings.Error.Peek();
        return applied;
    }

    public void Reset()
    {
        Settings.Reset();
        LoadDraft(Settings.Current.Peek());
        ActionError.Value = null;
    }

    protected override Widget Build()
    {
        _ = Settings.Current.Value;
        var rows = new List<Widget>
        {
            Text("Editor Settings"),
            Select(Enum.GetNames<EditorThemePreference>(), _theme, width: 180),
            TextField(_scale, placeholder: "UI scale", width: 180),
            TextField(_font, placeholder: "Editor font", width: 240),
            TextField(_autosaveSeconds, placeholder: "Autosave seconds", width: 180),
            Check(_autosave, "Enable autosave"),
            Check(_confirmExit, "Confirm clean exit"),
            HStack(6)[Button(_ => Apply(), "Apply"), Button(_ => Reset(), "Reset")],
        };
        if (ActionError.Value is { } error) rows.Add(Text(error));
        return VStack(6)[rows.ToArray()];
    }

    private void LoadDraft(EditorSettings value)
    {
        _theme.Value = (int)value.Theme;
        _scale.Value = value.UiScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        _font.Value = value.EditorFont;
        _autosaveSeconds.Value = value.AutosaveInterval.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        _autosave.Value = value.AutosaveEnabled;
        _confirmExit.Value = value.ConfirmExit;
    }
}
