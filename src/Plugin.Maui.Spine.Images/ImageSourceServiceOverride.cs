using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace Plugin.Maui.Spine.Images;

/// <summary>
/// Registers an image source service that wins over every other registration for its source type, whether
/// the app configures it before or after <c>UseMauiApp</c>.
/// </summary>
/// <remarks>
/// MAUI builds its image source services when <see cref="IImageSourceServiceCollection"/> is first resolved,
/// by running every <c>ConfigureImageSources</c> delegate in registration order, and the last service
/// registered for a type wins. MAUI Controls registers its own <see cref="UriImageSource"/> service in
/// <c>UseMauiApp</c>, so a plain <c>ConfigureImageSources</c> call before it loses. Wrapping MAUI's factory
/// for the collection adds this service after all of those delegates have run; a check at startup throws if
/// something still resolves another service.
/// </remarks>
internal static class ImageSourceServiceOverride
{
    public static MauiAppBuilder OverrideImageSourceService<TImageSource, TService>(this MauiAppBuilder builder)
        where TImageSource : IImageSource
        where TService : class, IImageSourceService<TImageSource>
    {
        // Adds MAUI's collection and provider when the builder was made without defaults; otherwise a no-op.
        builder.ConfigureImageSources(null);

        var descriptor = builder.Services.Last(static s => s.ServiceType == typeof(IImageSourceServiceCollection) && !s.IsKeyedService);
        var create = descriptor.ImplementationFactory
            ?? throw new InvalidOperationException(
                $"{typeof(TService).Name} cannot be registered for {typeof(TImageSource).Name}: {nameof(IImageSourceServiceCollection)} is no longer registered with a factory, as MAUI registers it.");

        builder.Services.Remove(descriptor);
        builder.Services.AddSingleton(services =>
        {
            var collection = (IImageSourceServiceCollection)create(services);
            collection.AddService<TImageSource, TService>();
            return collection;
        });
        builder.Services.AddSingleton<IMauiInitializeService>(new Check<TImageSource, TService>());
        return builder;
    }

    internal sealed class Check<TImageSource, TService> : IMauiInitializeService
        where TImageSource : IImageSource
    {
        public void Initialize(IServiceProvider services)
        {
            var resolved = services.GetRequiredService<IImageSourceServiceProvider>().GetImageSourceService(typeof(TImageSource));
            if (resolved is not TService)
                throw new InvalidOperationException(
                    $"MAUI loads {typeof(TImageSource).Name} with {resolved?.GetType().FullName ?? "no service"} instead of {typeof(TService).FullName}. " +
                    $"Something replaced {nameof(IImageSourceServiceCollection)} after Spine registered its service: call UseSpine() or UseSpineImages() after that registration in MauiProgram.");
        }
    }
}
