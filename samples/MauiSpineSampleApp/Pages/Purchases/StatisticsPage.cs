using Plugin.Maui.Spine.Purchases;

namespace MauiSpineSampleApp.Pages.Purchases;

[NavigableRegion(Title = "Statistics"), RequiresPurchase(ShowcasePurchase.Pro)]
public partial class StatisticsPage : SpinePage<StatisticsPageViewModel>
{
    public StatisticsPage()
    {
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = HeaderBarConstants.PagePadding,
                Spacing = 12,
                Children =
                {
                    new Label { Text = "A Pro page", FontSize = 22, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "[RequiresPurchase(ShowcasePurchase.Pro)] on the page: it opened because Pro is active, or after the paywall sold it." },
                },
            },
        };
    }
}

public sealed class StatisticsPageViewModel : ViewModelBase;
