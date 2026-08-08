using Luxel.Gallery.Browser;
using Luxel.Gallery.Stories;

BrowserGalleryApplicationBuilder builder = BrowserGalleryApplication.CreateBuilder();
builder.Services.AddCoreUiStory();
await using BrowserGalleryHost app = builder.Build();
await app.RunAsync();
