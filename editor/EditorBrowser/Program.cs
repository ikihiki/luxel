using Luxel.Editor.Browser;
using EditorBrowser;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddLuxelEditorBrowser();
await builder.Build().RunAsync();
