using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>Shows an <see cref="ActionSheet"/> the platform's way; see <see cref="INavigationService.ShowActionsAsync"/>.</summary>
internal static partial class ActionSheetPresenter
{
    /// <summary>Shows <paramref name="sheet"/> and completes with the picked row, after its command ran, or <see langword="null"/>.</summary>
    public static async Task<MenuAction?> ShowAsync(IServiceProvider services, ActionSheet sheet, View? anchor)
    {
        var picked = await ShowPlatformAsync(services, sheet, sheet.VisibleActions, anchor);
        if (picked is not null)
            sheet.Pick(picked);
        return picked;
    }

    static string CancelText(ActionSheet sheet) => sheet.CancelText ?? SpineStrings.Current["Spine.Header.Cancel"];

    private static partial Task<MenuAction?> ShowPlatformAsync(IServiceProvider services, ActionSheet sheet, IReadOnlyList<MenuAction> actions, View? anchor);
}
