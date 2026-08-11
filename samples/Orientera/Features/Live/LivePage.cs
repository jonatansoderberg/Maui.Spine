namespace Orientera.Features.Live;

[NavigableTab(Title = "Live", Icon = "tab_live.svg", Order = 2)]
public partial class LivePage
{
    public LivePage()
    {
        InitializeComponent();

        // How wide the table may be is layout, not data: the view is the only one that knows how
        // much room the columns have before they have to scroll.
        Table.SizeChanged += (_, _) => (BindingContext as LivePageViewModel)?.Fit(Table.Width);
    }
}
