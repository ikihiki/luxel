using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Luxel.Gallery;
using Luxel.Audio.Gallery;
using Luxel.Resources.Gallery;
using GalleryBrowser;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddResourceGallery();
builder.Services.AddAudioGallery();
builder.Services.AddCoreUiStory();
await builder.Build().RunAsync();
