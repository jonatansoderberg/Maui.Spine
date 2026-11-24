namespace MauiSpineSampleApp.Pages.Marquee;

[NavigableRegion(Title = "AnimatedLabel")]
public partial class MarqueePage
{
    public MarqueePage() => InitializeComponent();

    private void OnGlintNow(object? sender, EventArgs e)
    {
        foreach (var label in new[] { Gold, Silver, Bronze, Big, Light })
            label.Glint();
    }
}
