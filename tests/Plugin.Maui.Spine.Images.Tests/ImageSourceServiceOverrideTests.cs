using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Xunit;

namespace Plugin.Maui.Spine.Images.Tests;

public class ImageSourceServiceOverrideTests
{
    sealed class TestApp : Application;

    sealed class SpineService : IImageSourceService<UriImageSource>;

    sealed class OtherService : IImageSourceService<UriImageSource>;

    static IImageSourceService? Resolve(MauiAppBuilder builder, out IServiceProvider services)
    {
        services = builder.Services.BuildServiceProvider();
        return services.GetRequiredService<IImageSourceServiceProvider>().GetImageSourceService(typeof(UriImageSource));
    }

    static void RunChecks(IServiceProvider services)
    {
        foreach (var initializer in services.GetServices<IMauiInitializeService>().OfType<ImageSourceServiceOverride.Check<UriImageSource, SpineService>>())
            initializer.Initialize(services);
    }

    [Fact]
    public void A_plain_registration_before_UseMauiApp_loses_to_MAUI()
    {
        var builder = MauiApp.CreateBuilder();
        builder.ConfigureImageSources(static s => s.AddService<UriImageSource, SpineService>());
        builder.UseMauiApp<TestApp>();

        Assert.IsNotType<SpineService>(Resolve(builder, out _));
    }

    [Fact]
    public void Wins_when_registered_before_UseMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.OverrideImageSourceService<UriImageSource, SpineService>();
        builder.UseMauiApp<TestApp>();

        Assert.IsType<SpineService>(Resolve(builder, out var services));
        RunChecks(services);
    }

    [Fact]
    public void Wins_when_registered_after_UseMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<TestApp>();
        builder.OverrideImageSourceService<UriImageSource, SpineService>();

        Assert.IsType<SpineService>(Resolve(builder, out var services));
        RunChecks(services);
    }

    [Fact]
    public void Wins_over_a_later_ConfigureImageSources()
    {
        var builder = MauiApp.CreateBuilder();
        builder.OverrideImageSourceService<UriImageSource, SpineService>();
        builder.UseMauiApp<TestApp>();
        builder.ConfigureImageSources(static s => s.AddService<UriImageSource, OtherService>());

        Assert.IsType<SpineService>(Resolve(builder, out _));
    }

    [Fact]
    public void Wins_in_a_builder_without_defaults()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        builder.OverrideImageSourceService<UriImageSource, SpineService>();
        builder.UseMauiApp<TestApp>();

        Assert.IsType<SpineService>(Resolve(builder, out _));
    }

    [Fact]
    public void Startup_check_names_the_fix_when_another_service_wins()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<TestApp>();
        builder.OverrideImageSourceService<UriImageSource, SpineService>();
        builder.OverrideImageSourceService<UriImageSource, OtherService>();

        Assert.IsType<OtherService>(Resolve(builder, out var services));
        var error = Assert.Throws<InvalidOperationException>(() => RunChecks(services));
        Assert.Contains(nameof(SpineService), error.Message);
        Assert.Contains("UseSpineImages()", error.Message);
    }
}
