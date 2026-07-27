# Luxel.UI.App

A .NET 10 facade for creating Luxel UI applications. It owns UI-oriented window orchestration (`WindowManager`, `WindowHost`, `IWindowContent`, and `UiContent`) while platform backends remain in `Luxel.Platform.*`. On Linux, the package uses Silk.NET windowing and Vulkan.

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

`NavigationView(navigation, items)[content]` is a regular single-child layout control and can also be used outside `LuxelApp`. `Navigate` pushes a history entry, `Replace` changes the current entry, and `Back` returns to the previous entry. The first version uses exact, ordinal case-sensitive paths, recreates a screen when it is revisited, and does not animate screen changes. The original `LuxelApp.Run(() => widget)` API remains supported.

Packages are published to GitHub Packages when a GitHub Release is published. Release tags may use `vMAJOR.MINOR.PATCH` (for example, `v0.1.0`); the leading `v` is removed from the NuGet version. Maintainers can also run the **Publish NuGet package** workflow manually with an explicit version.

Consumers must add the owner feed `https://nuget.pkg.github.com/ikihiki/index.json` to their NuGet configuration and authenticate with a GitHub token that can read packages.

The package carries the required `GpuDeviceRasterizer2D` SPIR-V shaders, bundled font, Linux native dependencies, and UI source generator. The backend-neutral `IRasterizer2D` contract also supports `SkiaRasterizer2D` for offscreen CPU RGBA rendering, but the real-window path remains GPU-only because presentation currently requires a GPU framebuffer.

Framework-dependent deployment and Native AOT on Ubuntu/glibc `linux-x64` are supported. Native AOT still publishes GLFW, HarfBuzz, shaders, and fonts as sidecars; arm64, musl/Alpine, and a completely static single executable are not yet supported. Publish with an explicit RID:

```bash
dotnet publish app.cs -c Release -r linux-x64
```
