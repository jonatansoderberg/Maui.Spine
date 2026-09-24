using Plugin.Maui.Spine.Svg.Icons;

namespace MauiSpineSampleApp.Pages.SvgIcons;

public partial class SvgIconsPageViewModel : ViewModelBase
{
    // Every icon in Plugin.Maui.Spine.Svg.Icons; the set has 219.
    public IReadOnlyList<string> Icons => SpineIcons.All;
}
