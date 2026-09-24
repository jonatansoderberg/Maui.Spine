namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static partial void ConfigureHandlers(MauiAppBuilder builder)
    {
        ConfigureScrollInsets();
        ConfigureMaterials();
        ConfigureTypography();
        ConfigureMenus();
    }
}
