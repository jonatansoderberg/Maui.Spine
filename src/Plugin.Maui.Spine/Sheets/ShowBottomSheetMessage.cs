using CommunityToolkit.Mvvm.Messaging.Messages;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Sheets;

/// <summary>
/// Internal messenger message used to request that a bottom sheet be displayed.
/// Sent by the navigation service and handled by the Spine host page.
/// </summary>
internal sealed class ShowBottomSheetMessage
    : AsyncRequestMessage<bool>
{ 
    /// <summary>The page view to embed inside the bottom sheet.</summary>
    public View? Content { get; set; }

    /// <summary>Visual treatment applied to the page behind the sheet while it is open.</summary>
    public BackgroundPageOverlay BackgroundPageOverlay { get; set; } = BackgroundPageOverlay.Dimmed;

    /// <summary>The set of snap heights the sheet can be dragged to.</summary>
    public IReadOnlyList<SheetDetent> AllowedDetents { get; set; } = [SheetDetent.MediumDetent];

    /// <summary>The snap height the sheet opens at.</summary>
    public SheetDetent SelectedDetent { get; set; } = SheetDetent.MediumDetent;
}