# LuxelAudioBrowser

Browser-WASM Web Audio sample for the clip/queued-buffer MVP.

```sh
dotnet publish samples/LuxelAudioBrowser/LuxelAudioBrowser.csproj -c Release
```

Serve the publish `wwwroot` directory over HTTP(S), open it in a Web Audio capable browser, and select **Enable Audio** before playing a tone.
