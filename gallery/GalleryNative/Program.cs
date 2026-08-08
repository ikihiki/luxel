using Luxel.Gallery;
using Luxel.Gallery.Native;

Luxel.Platform.PlatformFileSystems.RegisterPhysicalFactory(static root => new Luxel.Platform.Silk.SilkPlatformFileSystem(root));

NativeGalleryApplicationBuilder builder = NativeGalleryApplication.CreateBuilder(args);
builder.Services.AddGalleryStory();
using NativeGalleryApplication app = builder.Build();
return app.Run();
