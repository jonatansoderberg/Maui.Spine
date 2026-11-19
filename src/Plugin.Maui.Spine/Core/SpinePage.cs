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

    /// <summary>Identifies the <see cref="HeaderRow"/> bindable property.</summary>
    public static readonly BindableProperty HeaderRowProperty = BindableProperty.Create(
        nameof(HeaderRow),
        typeof(View),
        typeof(SpinePage<TViewModel>),
        propertyChanged: (bindable, _, _) => ((SpinePage<TViewModel>)bindable)._headerRowChanged?.Invoke(bindable, EventArgs.Empty));

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
        BindingContext = _services.GetService<TViewModel>();
    }

    /// <summary>
    /// The main content view of the page. Set this from XAML or code-behind to provide the page UI; either way it
    /// inherits the page's view model as its <c>BindingContext</c>.
    /// </summary>
    /// <remarks>
    /// The same property as <see cref="ContentView.Content"/>, kept so code compiled against earlier versions still
    /// finds it. It once held a <see cref="ContentPresenter"/>, which never hands its content a binding context outside
    /// a control template: content set in code bound to nothing, while XAML set the base property and did not notice.
    /// </remarks>
    public new View? Content
    {
        get => base.Content;
        set => base.Content = value;
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

    /// <summary>
    /// A row under the header bar that belongs to the header, as the search row does: what the page is about or a
    /// choice that holds for all of it (whose figures, which period), kept in view while the content scrolls. It moves
    /// with the page in a push or a pop. Under a floating header (a large title, <c>Overlay</c> or a scroll edge
    /// background) the content scrolls under the row as well as the bar: <c>SafeAreaInsets.Top</c> and the scroll
    /// inset include it, and the scroll edge effect or solid background reaches below it. The row is as tall as it
    /// measures, spans the page's width inside the safe area (give it the page's side margin yourself) and gets the
    /// page's <c>BindingContext</c>. It goes under a search row when the page has one.
    /// </summary>
    public View? HeaderRow
    {
        get => (View?)GetValue(HeaderRowProperty);
        set => SetValue(HeaderRowProperty, value);
    }

    private EventHandler? _footerChanged;
    private EventHandler? _headerRowChanged;

    event EventHandler? IPageFooterSource.HeaderRowChanged
    {
        add => _headerRowChanged += value;
        remove => _headerRowChanged -= value;
    }

    event EventHandler? IPageFooterSource.FooterChanged
    {
        add => _footerChanged += value;
        remove => _footerChanged -= value;
    }
}

/// <summary>
/// A page that may carry a <see cref="SpinePage{TViewModel}.Footer"/> or a <see cref="SpinePage{TViewModel}.HeaderRow"/>,
/// seen without its view model type.
/// </summary>
internal interface IPageFooterSource
{
    View? Footer { get; }

    View? HeaderRow { get; }

    event EventHandler? FooterChanged;

    event EventHandler? HeaderRowChanged;
}
