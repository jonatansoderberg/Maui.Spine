namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Base class for all Spine pages. Every page in a Spine application must derive from this class
/// and be decorated with either <see cref="NavigableRegionAttribute"/> or
/// <see cref="NavigableSheetAttribute"/>.
/// </summary>
/// <typeparam name="TViewModel">
/// The ViewModel type for this page. The ViewModel is resolved from the DI container and set as
/// the <c>BindingContext</c> automatically.
/// </typeparam>
/// <example>
/// <code>
/// [NavigableRegion(Title = "Home")]
/// public partial class HomePage : SpinePage&lt;HomePageViewModel&gt;
/// {
///     public HomePage() =&gt; InitializeComponent();
/// }
/// </code>
/// </example>
public abstract class SpinePage<TViewModel> : ContentView, INavigable, IPageFooterSource where TViewModel : ViewModelBase
{
    /// <summary>Identifies the <see cref="Footer"/> bindable property.</summary>
    public static readonly BindableProperty FooterProperty = BindableProperty.Create(
        nameof(Footer),
        typeof(View),
        typeof(SpinePage<TViewModel>),
        propertyChanged: (bindable, _, _) => ((SpinePage<TViewModel>)bindable)._footerChanged?.Invoke(bindable, EventArgs.Empty));

    private static IServiceProvider _services => IPlatformApplication.Current?.Services ?? throw new PlatformNotSupportedException();

    /// <summary>
    /// Shadows <c>ContentView.SafeAreaEdges</c> so that pages derived from <see cref="SpinePage{TViewModel}"/>
    /// can write <c>[NavigableRegion(SafeAreaEdges = SafeAreaEdges.None)]</c> or
    /// <c>[NavigableSheet(SafeAreaEdges = SafeAreaEdges.None)]</c> in attribute arguments
    /// without ambiguity. The constants forward directly to <see cref="Plugin.Maui.Spine.Core.SafeAreaEdges"/>.
    /// </summary>
    public new static class SafeAreaEdges
    {
        /// <inheritdoc cref="global::Plugin.Maui.Spine.Core.SafeAreaEdges.None"/>
        public const global::Plugin.Maui.Spine.Core.SafeAreaEdges None = global::Plugin.Maui.Spine.Core.SafeAreaEdges.None;

        /// <inheritdoc cref="global::Plugin.Maui.Spine.Core.SafeAreaEdges.Top"/>
        public const global::Plugin.Maui.Spine.Core.SafeAreaEdges Top = global::Plugin.Maui.Spine.Core.SafeAreaEdges.Top;

        /// <inheritdoc cref="global::Plugin.Maui.Spine.Core.SafeAreaEdges.Bottom"/>
        public const global::Plugin.Maui.Spine.Core.SafeAreaEdges Bottom = global::Plugin.Maui.Spine.Core.SafeAreaEdges.Bottom;

        /// <inheritdoc cref="global::Plugin.Maui.Spine.Core.SafeAreaEdges.Left"/>
        public const global::Plugin.Maui.Spine.Core.SafeAreaEdges Left = global::Plugin.Maui.Spine.Core.SafeAreaEdges.Left;

        /// <inheritdoc cref="global::Plugin.Maui.Spine.Core.SafeAreaEdges.Right"/>
        public const global::Plugin.Maui.Spine.Core.SafeAreaEdges Right = global::Plugin.Maui.Spine.Core.SafeAreaEdges.Right;

        /// <inheritdoc cref="global::Plugin.Maui.Spine.Core.SafeAreaEdges.All"/>
        public const global::Plugin.Maui.Spine.Core.SafeAreaEdges All = global::Plugin.Maui.Spine.Core.SafeAreaEdges.All;
    }

    /// <summary>
    /// Initializes the page and resolves <typeparamref name="TViewModel"/>
    /// from the DI container as the <c>BindingContext</c>.
    /// </summary>
    public SpinePage() 
    {
        base.Content = _contentPresenter;
        BindingContext = _services.GetService<TViewModel>();
    }

    private readonly ContentPresenter _contentPresenter = new();

    /// <summary>
    /// The main content view of the page. Set this from XAML or code-behind to provide the page UI.
    /// </summary>
    public new View Content
    {
        get => _contentPresenter.Content;
        set => _contentPresenter.Content = value;
    }

    /// <summary>
    /// A view pinned to the bottom of the page, outside its scrolling content: the page's own
    /// primary action, such as <em>Log in</em>, <em>Continue</em> or <em>Pay</em>. In a sheet it
    /// stays at the bottom of the visible part of the sheet at every detent and follows the sheet
    /// while it is dragged. The footer gets the page's <c>BindingContext</c>.
    /// </summary>
    /// <remarks>
    /// Save, Cancel and Done are not footer buttons: they are page actions in the header bar
    /// (<see cref="PageActionAttribute"/>).
    /// </remarks>
    public View? Footer
    {
        get => (View?)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    private EventHandler? _footerChanged;

    event EventHandler? IPageFooterSource.FooterChanged
    {
        add => _footerChanged += value;
        remove => _footerChanged -= value;
    }
}

/// <summary>A page that may carry a <see cref="SpinePage{TViewModel}.Footer"/>, seen without its view model type.</summary>
internal interface IPageFooterSource
{
    View? Footer { get; }

    event EventHandler? FooterChanged;
}
