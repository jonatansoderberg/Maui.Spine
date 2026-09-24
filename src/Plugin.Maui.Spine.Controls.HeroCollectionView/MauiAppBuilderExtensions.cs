namespace Plugin.Maui.Spine.Controls;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Does nothing: <see cref="HeroCollectionView"/> needs no registration, with or without
    /// <c>UseSpine()</c>. Kept so existing builder chains still compile.
    /// </summary>
    public static MauiAppBuilder UseHeroCollectionView(this MauiAppBuilder builder)
    {
        return builder;
    }
}
