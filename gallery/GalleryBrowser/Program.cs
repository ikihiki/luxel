using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Luxel.Gallery;
using GalleryBrowser;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddCoreUiStory();
await builder.Build().RunAsync();
