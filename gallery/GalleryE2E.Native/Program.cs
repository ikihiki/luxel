using Luxel.Gallery;
using Luxel.Gallery.Native;

NativeGalleryE2eApplicationBuilder builder = NativeGalleryE2eApplication.CreateBuilder(args);
builder.Services.AddGalleryStory();
using NativeGalleryE2eApplication app = builder.Build();
return app.Run();
