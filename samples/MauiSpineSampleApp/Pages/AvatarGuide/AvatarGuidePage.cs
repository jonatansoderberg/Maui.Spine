using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.AvatarGuide;

[NavigableRegion(Title = "Avatar guide")]
public partial class AvatarGuidePage
{
    public AvatarGuidePage()
    {
        InitializeComponent();
        // The tour's guide and the hint's live in the tour card, outside the page; the view model plays to them and to the
        // page's views, so it gets them here.
        Attach();
        BindingContextChanged += (_, _) => Attach();
    }

    // SpinePage sets the view model before InitializeComponent, so it is there already; a later one is attached too.
    private void Attach() => (BindingContext as AvatarGuidePageViewModel)?.Attach(Presenter, ScriptButton, SearchField, NewNoteButton, SettingsButton, StreakCard);
}
