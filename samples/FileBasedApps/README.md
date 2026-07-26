# .NET 10 file-based app samples

- `HelloLuxel.Linux.cs` uses `#:project` for repository development.
- `package/HelloLuxel.Package.cs` uses `#:package Luxel.UI.App@0.1.0` as an external consumer.

Run the package release gate after starting the Linux desktop and sourcing its environment:

```bash
source /tmp/luxel-desktop-${UID}/environment
samples/FileBasedApps/test-package.sh
```

The script packs Luxel, seeds a temporary local feed with its third-party dependency closure, then performs the consumer restore/build/run/publish outside the repository. The consumer NuGet configuration contains only that local feed.
