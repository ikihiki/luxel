# Luxel.UI.App

A .NET 10 facade for creating Luxel UI applications. It owns UI-oriented window orchestration (`WindowManager`, `WindowHost`, `IWindowContent`, and `UiContent`) while platform backends remain in `Luxel.Platform.*`. Builder defaults detect the environment: Windows uses Win32 + Direct3D 12, while Linux uses Silk.NET X11 + Vulkan. `WindowBackend` and `GraphicsBackend` can still override either choice explicitly.

```csharp
#:package Luxel.UI.App@0.1.0
#:property TargetFramework=net10.0

using Luxel.UI.App;
using static Luxel.Controls.Kit;

LuxelApp.Run(() => Heading("Hello Luxel"));
```

For multiple fixed-path screens, use the Minimal-API-style builder. Navigation state and the content host are provided by `Luxel.UI`; `MapScreen` registration and startup integration live in this package.

```csharp
using Luxel.Controls;
using Luxel.UI.App;
using static Luxel.Controls.Kit;

var builder = LuxelApp.CreateBuilder(args);
builder.Options.Title = "My App";

var app = builder.Build();
app.MapScreen("/", navigation =>
    Center()[Button(_ => navigation.Navigate("/settings"), "Settings")]);
app.MapScreen("/settings", navigation =>
    Center()[Button(_ => navigation.Back(), "Back")]);

app.Run("/", (navigation, content) =>
    NavigationView(
        navigation,
        [
            new("/", "Home"),
            new("/settings", "Settings"),
        ])[content]);
```

The builder also exposes runtime lifecycle hooks for application shells that need the selected GPU/font, DevTools commands, or per-frame synchronization:

```csharp
builder.ConfigureRuntime(runtime => shell.Attach(runtime.Device, runtime.Font));
builder.OnStarted(runtime => StartDebugServer(runtime.Commands, runtime.WindowManager));
builder.OnFrame((runtime, dt) => shell.Update(dt));
```

`NavigationView(navigation, items)[content]` is a regular single-child layout control and can also be used outside `LuxelApp`. `Navigate` pushes a history entry, `Replace` changes the current entry, and `Back` returns to the previous entry. The first version uses exact, ordinal case-sensitive paths, recreates a screen when it is revisited, and does not animate screen changes. The original `LuxelApp.Run(() => widget)` API remains supported.

## Terminal emulator

The facade package also carries the optional, Controls-independent terminal assemblies:

- `Luxel.Terminal`: VT/ANSI parser, screen/scrollback model, input encoding, and session contract.
- `Luxel.Terminal.UI`: fixed-cell `TerminalView`, Nerd Font fallback, selection, clipboard, IME overlay, and resize handling.
- `Luxel.Terminal.Windows`: Windows ConPTY backend.
- `Luxel.Terminal.Linux`: glibc x64 Unix PTY backend.

`Luxel.Controls` and `Luxel.Terminal*` do not reference each other. Applications compose both at the root when needed. See `samples/LuxelTerminal` for a complete shell host, Nerd Font configuration, and oh-my-posh usage. The renderer accepts TTF/TTC Nerd Fonts and supports BMP/supplementary private-use glyphs, 256 colors, and True Color.

## Install from GitHub Packages

Every update to `main` publishes a preview package named `0.1.0-ci.<run-number>`. Publishing a GitHub Release creates the stable version from its `vMAJOR.MINOR.PATCH` tag, and maintainers can also run the **Publish NuGet package** workflow with an explicit version. The workflow builds the package and verifies it from a clean consumer project before publishing.

GitHub's NuGet registry requires authentication when restoring packages. Create a GitHub personal access token (classic) with `read:packages` permission (`repo` is also required when the repository/package is private), then register the Luxel feed once:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/ikihiki/index.json" \
  --name github_luxel \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_TOKEN \
  --store-password-in-clear-text
```

Install a published version into a .NET 10 project:

```bash
dotnet add package Luxel.UI.App --version 0.1.0-ci.RUN_NUMBER
```

For CI, avoid committing a token. Put the source in `NuGet.config` and inject the password through the `NuGetPackageSourceCredentials_github_luxel` environment variable:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github_luxel" value="https://nuget.pkg.github.com/ikihiki/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

```bash
export NuGetPackageSourceCredentials_github_luxel="Username=YOUR_GITHUB_USERNAME;Password=YOUR_GITHUB_TOKEN;ValidAuthenticationTypes=Basic"
dotnet restore
```

The package carries the required `GpuDeviceRasterizer2D` SPIR-V and DXIL shaders, bundled font, native dependencies, and UI source generator. The backend-neutral `IRasterizer2D` contract also supports `SkiaRasterizer2D` for offscreen CPU RGBA rendering, but the real-window path remains GPU-only because presentation currently requires a GPU framebuffer.

Framework-dependent deployment and Native AOT on Ubuntu/glibc `linux-x64` are supported. Native AOT still publishes GLFW, HarfBuzz, shaders, and fonts as sidecars; arm64, musl/Alpine, and a completely static single executable are not yet supported. Publish with an explicit RID:

```bash
dotnet publish app.cs -c Release -r linux-x64
```
