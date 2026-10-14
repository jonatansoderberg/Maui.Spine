namespace Plugin.Maui.Spine.Controls;

public static class HighlightExtensions
{
    /// <summary>
    /// Registers <see cref="IHighlights"/>. <c>UseSpine()</c> calls it for an app that references this
    /// package; an app without Spine calls it itself. The attached <see cref="Highlight"/> properties work
    /// without it. Calling it more than once is harmless.
    /// </summary>
    public static MauiAppBuilder UseHighlights(this MauiAppBuilder builder)
    {
        if (builder.Services.Any(static d => d.ServiceType == typeof(IHighlights)))
            return builder;

        builder.Services.AddSingleton<IHighlights, Highlights>();
        return builder;
    }
}
