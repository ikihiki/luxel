using Luxel.Gallery.Browser;
using Luxel.UI.Gallery;
using Luxel.Particles.Gallery;

BrowserGalleryApplicationBuilder builder = BrowserGalleryApplication.CreateBuilder();
builder.Services.AddUiGallery();
builder.Services.AddParticlesGallery();
await using BrowserGalleryHost app = builder.Build();
await app.RunAsync();
