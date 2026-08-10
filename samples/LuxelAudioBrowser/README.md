# LuxelAudioBrowser

Browser-WASM Web Audio sample for the clip/queued-buffer MVP.

```sh
dotnet publish samples/LuxelAudioBrowser/LuxelAudioBrowser.csproj -c Release
```

Serve the publish `wwwroot` directory over HTTP(S), open it in a Web Audio capable browser, and select **Enable Audio** before playing a tone.

## Using the backend from another browser-WASM app

Reference `src/Audio/Luxel.Audio.Browser`, copy its `wwwroot/luxel-audio-browser.js` into the host's published `wwwroot`, and register the ES module before starting .NET:

```js
import * as audio from "./luxel-audio-browser.js";
const runtime = await dotnet.create();
runtime.setModuleImports("./luxel-audio-browser.js", audio);
```

Create the backend with `await BrowserAudioBackend.CreateAsync()`, then call `await backend.ResumeAsync()` directly from a click/tap handler. `CreateAsync()` may leave the `AudioContext` suspended because of browser autoplay policy.
