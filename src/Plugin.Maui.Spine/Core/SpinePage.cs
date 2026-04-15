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
public abstract class SpinePage<TViewModel> : ContentView, INavigable where TViewModel : ViewModelBase
{
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
}
