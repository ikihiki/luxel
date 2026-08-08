# Gallery browser runtime

`gallery/GalleryBrowser` is a statically hostable Blazor WebAssembly application. `Program.cs` registers the browser story composition root and Blazor injects `StoryCatalog` directly into `App.razor`.

There is no `browser-runtime-manifest.json` and no Gallery static-site exporter. A story is browser-runnable exactly when it is registered in the Browser Gallery catalog. `App.razor` resolves the `story` and `args` query parameters, then starts `BrowserGalleryApplication` against the same catalog instance.

The JavaScript module in `wwwroot/main.js` only supplies the Luxel canvas bridge and parent-frame protocol. The protocol version remains `2` for message compatibility, but it no longer describes catalog membership.

Publish the static application with:

```bash
dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release
```

Deploy `gallery/GalleryBrowser/bin/Release/net10.0/publish/wwwroot` to a static host. WebGPU requires HTTPS or localhost.
