using Luxel.Gallery;
using Luxel.Gallery.Native;

NativeGalleryApplicationBuilder builder = NativeGalleryApplication.CreateBuilder(args);
builder.Services.AddGalleryStory();
using NativeGalleryApplication app = builder.Build();
return app.Run();
