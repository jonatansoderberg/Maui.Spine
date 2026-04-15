using MauiSpineSampleApp.Pages.Settings;

namespace MauiBottomSheetPoc;

public class ShortcutHandler(INavigationService _navigation) : IShortcutHandler
{
    public static void Configure(IShortcutBuilder builder)
    {
        builder.Add(id: "settings", title: "Settings");
    }

    public Task InvokeAsync(string shortcutId) =>
        shortcutId switch
        {
            "settings" => _navigation.NavigateToAsync<SettingsPage>(),
            _ => Task.CompletedTask
        };
}
