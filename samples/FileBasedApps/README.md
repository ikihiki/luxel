# .NET 10 file-based app samples

- `HelloLuxel.Linux.cs` uses `#:project` for repository development.
- `package/HelloLuxel.Package.cs` uses `#:package Luxel.Framework.UI@0.1.0` as an external consumer.

Run the package release gate after starting the Linux desktop and sourcing its environment:

```bash
source /tmp/luxel-desktop-${UID}/environment
samples/FileBasedApps/test-package.sh
```

The script packs Luxel, seeds a temporary local feed with its third-party dependency closure, then performs the consumer restore/build/run/publish outside the repository. The consumer NuGet configuration contains only that local feed.

## Native AOT (`linux-x64`)

Ubuntu requires `clang`, `zlib1g-dev`, `binutils`, and `file`; the Luxel Dev Container image includes them. The dedicated project-reference sample is `HelloLuxel.Aot.Linux.cs`.

```bash
source /tmp/luxel-desktop-${UID}/environment
dotnet publish samples/FileBasedApps/HelloLuxel.Aot.Linux.cs -c Release -r linux-x64 -o /tmp/luxel-aot-project
LUXEL_RUN_FRAMES=1 /tmp/luxel-aot-project/HelloLuxel.Aot.Linux
```

The package-consumer release gate packs Luxel, publishes a repository-external `#:package` app as Native AOT, validates the ELF/native dependencies and sidecar assets, then renders one frame from a foreign working directory:

```bash
samples/FileBasedApps/test-package-aot.sh
```

Native AOT v1 targets Ubuntu/glibc `linux-x64`. GLFW, HarfBuzz, shaders, and fonts remain sidecars; arm64, musl/Alpine, and a completely static single executable are not yet supported.
