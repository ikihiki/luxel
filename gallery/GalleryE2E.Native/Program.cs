using Luxel.Gallery;
using Luxel.Gallery.Native;

Luxel.Platform.PlatformFileSystems.RegisterPhysicalFactory(static root => new Luxel.Platform.Silk.SilkPlatformFileSystem(root));

NativeGalleryE2eApplicationBuilder builder = NativeGalleryE2eApplication.CreateBuilder(args);
builder.Services.AddGalleryStory();
using NativeGalleryE2eApplication app = builder.Build();
return app.Run();
