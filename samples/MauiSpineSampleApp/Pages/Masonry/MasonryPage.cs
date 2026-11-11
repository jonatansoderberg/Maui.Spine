namespace MauiSpineSampleApp.Pages.Masonry;

[NavigableRegion(Title = "Masonry layout")]
public partial class MasonryPage
{
    public MasonryPage()
    {
        InitializeComponent();
        // The window sizes on the desktop: the layout's width for a number of columns, and what lies around it on the page.
        BindingContextChanged += (_, _) =>
        {
            if (BindingContext is MasonryPageViewModel vm)
                vm.WindowWidthFor = columns => Masonry.WidthFor(columns) + Math.Max(0, Width - Masonry.Width);
        };
    }
}
