using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Markdown;

[NavigableRegion(Title = "Markdown")]
public partial class MarkdownPage
{
    public MarkdownPage() => InitializeComponent();

    // How much of the streamed text each render rebuilt: almost always only the last block.
    private void OnStreamRendered(object? sender, MarkdownRenderedEventArgs e) =>
        ((MarkdownPageViewModel)BindingContext).Rebuilt = $"Last render rebuilt {e.Rebuilt} of {e.Blocks} blocks";

    // Every link comes through here first; not handled, the view opens it in the browser.
    private void OnLinkTapped(object? sender, MarkdownLinkTappedEventArgs e) =>
        ((MarkdownPageViewModel)BindingContext).LastLink = $"Tapped {e.Text}: {e.Url}";
}
