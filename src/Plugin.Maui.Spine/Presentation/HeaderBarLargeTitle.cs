using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The large title of a <see cref="NavigableAttribute.LargeTitle"/> page: put it first in the
/// page's scroll content. It shows the page's title (set <see cref="Label.Text"/> to show something
/// else), takes the platform's large-title size, weight, row height and margin from
/// <see cref="HeaderBarConstants"/>, the header's foreground colour when the page fixes one, and is
/// what Spine measures to know when the header bar's title should be in.
/// </summary>
/// <example>
/// <code>
/// &lt;CollectionView ItemsSource="{Binding Messages}"&gt;
///     &lt;CollectionView.Header&gt;
///         &lt;HeaderBarLargeTitle /&gt;
///     &lt;/CollectionView.Header&gt;
/// &lt;/CollectionView&gt;
/// </code>
/// </example>
public class HeaderBarLargeTitle : Label
{
    private Element? _page;
    private ViewModelBase? _viewModel;

    /// <summary>Initializes the title with the platform's large-title measurements.</summary>
    public HeaderBarLargeTitle()
    {
        FontSize = HeaderBarConstants.LargeTitleFontSize;
        FontAttributes = HeaderBarConstants.LargeTitleFontAttributes;
        HeightRequest = HeaderBarConstants.LargeTitleHeight;
        Margin = HeaderBarConstants.LargeTitleMargin;
        VerticalTextAlignment = TextAlignment.Center;
        LineBreakMode = LineBreakMode.TailTruncation;
        SemanticProperties.SetHeadingLevel(this, SemanticHeadingLevel.Level1);

        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        SizeChanged += (_, _) => _page?.SetValue(HeaderBar.LargeTitleViewProperty, this);
    }

    private void Attach()
    {
        Detach();

        for (var parent = Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INavigable && parent.BindingContext is ViewModelBase vm)
            {
                _page = parent;
                _viewModel = vm;
                break;
            }
        }

        if (_page is null || _viewModel is null)
            return;

        // The page's title, unless the app gave the label text of its own.
        if (!IsSet(TextProperty))
            SetBinding(TextProperty, new Binding(nameof(ViewModelBase.Title), source: _viewModel));

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyForeground();

        _page.SetValue(HeaderBar.LargeTitleViewProperty, this);
    }

    private void Detach()
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_page?.GetValue(HeaderBar.LargeTitleViewProperty) == this)
            _page.ClearValue(HeaderBar.LargeTitleViewProperty);

        _page = null;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModelBase.HeaderBarForeground))
            ApplyForeground();
    }

    // A fixed header colour (a title over a photo) applies to the large title too; otherwise the
    // app's Label style and theme decide, as for any other text.
    private void ApplyForeground()
    {
        if (_viewModel?.HeaderBarForeground is { } foreground)
            TextColor = foreground;
    }
}
