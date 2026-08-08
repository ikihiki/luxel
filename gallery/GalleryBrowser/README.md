# Luxel Gallery Browser

A statically hostable Blazor WebAssembly Gallery. Blazor resolves stories directly from the injected `StoryCatalog`; no runtime manifest or GallerySite export is required.

```bash
dotnet run --project gallery/GalleryBrowser
# production static assets
dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release
```

Open `/?story=Examples%2F3D%2FTriangle`. Optional canonical JSON arguments use the `args` query parameter. Host the published `wwwroot` over HTTPS or localhost for WebGPU.
