using System.ComponentModel;
using Plugin.Maui.Spine.Core;
using SafeAreaEdges = Plugin.Maui.Spine.Core.SafeAreaEdges;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Attached properties that let a scrolling view take over the system-bar edges its page left
/// unpadded, so content keeps drawing behind the bar while the last row can still scroll clear of it.
/// </summary>
/// <example>
/// <code>
/// [NavigableRegion(SafeAreaEdges = SafeAreaEdges.Top | SafeAreaEdges.Left | SafeAreaEdges.Right)]
/// </code>
/// <code>
/// &lt;CollectionView spine:SafeArea.ScrollInset="Bottom" ... /&gt;
/// </code>
/// </example>
public static class SafeArea
{
    internal const string MapperKey = "SpineScrollInset";

    /// <summary>
    /// The edges on which a <see cref="ScrollView"/> or <see cref="CollectionView"/> adds the page's
    /// <see cref="ViewModelBase.SafeAreaInsets"/> as a native content inset. An edge only produces an
    /// inset when the page excluded it from its <c>SafeAreaEdges</c>; inside the tab host the bottom
    /// value includes the floating tab bar.
    /// </summary>
    public static readonly BindableProperty ScrollInsetProperty = BindableProperty.CreateAttached(
        "ScrollInset", typeof(SafeAreaEdges), typeof(SafeArea), SafeAreaEdges.None, propertyChanged: OnScrollInsetChanged);

    /// <summary>Gets the edges on which <paramref name="view"/> adds the page's safe-area inset to its scrollable range.</summary>
    public static SafeAreaEdges GetScrollInset(BindableObject view) => (SafeAreaEdges)view.GetValue(ScrollInsetProperty);

    /// <summary>Sets the edges on which <paramref name="view"/> adds the page's safe-area inset to its scrollable range.</summary>
    public static void SetScrollInset(BindableObject view, SafeAreaEdges value) => view.SetValue(ScrollInsetProperty, value);

    /// <summary>The inset resolved for the view right now, read by the platform mappers.</summary>
    internal static readonly BindableProperty ResolvedInsetProperty = BindableProperty.CreateAttached(
        "ResolvedInset", typeof(Thickness), typeof(SafeArea), Thickness.Zero,
        propertyChanged: static (bindable, _, _) => (bindable as View)?.Handler?.UpdateValue(MapperKey));

    internal static Thickness GetResolvedInset(BindableObject view) => (Thickness)view.GetValue(ResolvedInsetProperty);

    static readonly BindableProperty TrackerProperty = BindableProperty.CreateAttached(
        "Tracker", typeof(Tracker), typeof(SafeArea), null);

    static void OnScrollInsetChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        var tracker = (Tracker?)view.GetValue(TrackerProperty);

        if ((SafeAreaEdges)newValue == SafeAreaEdges.None)
        {
            tracker?.Dispose();
            view.SetValue(TrackerProperty, null);
            view.SetValue(ResolvedInsetProperty, Thickness.Zero);
            return;
        }

        if (tracker is null)
            view.SetValue(TrackerProperty, new Tracker(view));
        else
            tracker.Apply();
    }

    /// <summary>
    /// Finds the view model of the Spine page that hosts <paramref name="element"/>, or
    /// <see langword="null"/> when the element is not inside a Spine page.
    /// </summary>
    static ViewModelBase? FindPageViewModel(Element element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INavigable && parent.BindingContext is ViewModelBase vm)
                return vm;
        }

        return null;
    }

    /// <summary>
    /// Follows one view from load to unload: finds its page's view model, mirrors
    /// <see cref="ViewModelBase.SafeAreaInsets"/> into <see cref="ResolvedInsetProperty"/> masked by
    /// the requested edges, and lets go of the view model when the view leaves the tree.
    /// </summary>
    sealed class Tracker : IDisposable
    {
        readonly View _view;
        ViewModelBase? _viewModel;

        public Tracker(View view)
        {
            _view = view;
            view.Loaded += OnLoaded;
            view.Unloaded += OnUnloaded;

            if (view.IsLoaded)
                Attach();
        }

        void OnLoaded(object? sender, EventArgs e) => Attach();

        void OnUnloaded(object? sender, EventArgs e) => Detach();

        void Attach()
        {
            var viewModel = FindPageViewModel(_view);

            if (!ReferenceEquals(viewModel, _viewModel))
            {
                Detach();
                _viewModel = viewModel;

                if (viewModel is not null)
                    viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            Apply();
        }

        void Detach()
        {
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = null;
        }

        void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModelBase.SafeAreaInsets))
                Apply();
        }

        public void Apply()
        {
            var edges = GetScrollInset(_view);
            var insets = _viewModel?.SafeAreaInsets ?? Thickness.Zero;

            _view.SetValue(ResolvedInsetProperty, new Thickness(
                (edges & SafeAreaEdges.Left) != 0 ? insets.Left : 0,
                (edges & SafeAreaEdges.Top) != 0 ? insets.Top : 0,
                (edges & SafeAreaEdges.Right) != 0 ? insets.Right : 0,
                (edges & SafeAreaEdges.Bottom) != 0 ? insets.Bottom : 0));
        }

        public void Dispose()
        {
            _view.Loaded -= OnLoaded;
            _view.Unloaded -= OnUnloaded;
            Detach();
        }
    }
}
