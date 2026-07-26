# Luxel.UI.App

A .NET 10 facade for creating Luxel UI applications. On Linux, the package uses Silk.NET windowing and Vulkan.

```csharp
#:package Luxel.UI.App@0.1.0
#:property TargetFramework=net10.0

using Luxel.UI.App;
using static Luxel.Controls.Kit;

return LuxelApp.Run(() => Heading("Hello Luxel"));
```

Packages are published to GitHub Packages when a GitHub Release is published. Release tags may use `vMAJOR.MINOR.PATCH` (for example, `v0.1.0`); the leading `v` is removed from the NuGet version. Maintainers can also run the **Publish NuGet package** workflow manually with an explicit version.

Consumers must add the owner feed `https://nuget.pkg.github.com/ikihiki/index.json` to their NuGet configuration and authenticate with a GitHub token that can read packages.

The package carries the required Rasterizer2D SPIR-V shaders, bundled font, Linux native dependencies, and UI source generator. Framework-dependent deployment is supported; trimming, Native AOT, and single-file publish are not yet supported.
