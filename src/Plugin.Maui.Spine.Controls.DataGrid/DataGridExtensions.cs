namespace Plugin.Maui.Spine.Controls;

public static class DataGridExtensions
{
    /// <summary>
    /// Registers what <see cref="DataGrid"/> needs from the app builder: on Android, the view that keeps
    /// a row's swipe from taking a scroll. <c>UseSpine()</c> calls it for an app that references this
    /// package; an app without Spine calls it itself. Calling it more than once is harmless.
    /// </summary>
    public static MauiAppBuilder UseDataGrid(this MauiAppBuilder builder)
    {
#if ANDROID
        builder.ConfigureMauiHandlers(static handlers => handlers.AddHandler<SwipeGate, SwipeGateHandler>());
#endif
        return builder;
    }
}
