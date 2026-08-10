namespace Orientera.Features.Home;

/// <summary>Picks the card layout for each block kind. Templates are supplied from XAML.</summary>
public sealed class HomeBlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LiveNow { get; set; }
    public DataTemplate? NextForMe { get; set; }
    public DataTemplate? LatestResult { get; set; }
    public DataTemplate? Group { get; set; }
    public DataTemplate? Discovery { get; set; }
    public DataTemplate? Development { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container) => item switch
    {
        LiveNowBlock => LiveNow,
        NextForMeBlock => NextForMe,
        LatestResultBlock => LatestResult,
        GroupBlock => Group,
        DiscoveryBlock => Discovery,
        DevelopmentBlock => Development,
        _ => null,
    };
}
