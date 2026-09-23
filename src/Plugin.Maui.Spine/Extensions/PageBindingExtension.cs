using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Binds to a member of the owning Spine page's view model from anywhere inside the page,
/// including item templates, without naming the ancestor type:
/// <c>{PageBinding Title}</c> is <c>{Binding BindingContext.Title, Source={RelativeSource AncestorType=…}}</c>
/// with the ancestor being the page.
/// </summary>
/// <example>
/// <code>
/// &lt;Label Text="{PageBinding Greeting}" /&gt;
/// &lt;Button Command="{PageCommand OpenLive}" CommandParameter="{Binding .}" /&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Path))]
[AcceptEmptyServiceProvider]
public class PageBindingExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The member path on the page's view model, e.g. <c>Title</c> or <c>Filter.Count</c>.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Binding mode; defaults to <see cref="BindingMode.Default"/>.</summary>
    public BindingMode Mode { get; set; } = BindingMode.Default;

    /// <summary>Optional value converter.</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>Optional converter parameter.</summary>
    public object? ConverterParameter { get; set; }

    /// <summary>Optional format string.</summary>
    public string? StringFormat { get; set; }

    /// <inheritdoc/>
    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(
            path: string.IsNullOrEmpty(Path) ? "BindingContext" : $"BindingContext.{Path}",
            mode: Mode,
            converter: Converter,
            converterParameter: ConverterParameter,
            stringFormat: StringFormat,
            source: new RelativeBindingSource(RelativeBindingSourceMode.FindAncestor, typeof(INavigable)));

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

/// <summary>
/// Binds a command on the owning Spine page's view model, from anywhere inside the page:
/// <c>{PageCommand OpenLive}</c> binds <c>OpenLiveCommand</c> on the page's view model.
/// The <c>Command</c> suffix is added when missing, so both <c>OpenLive</c> and
/// <c>OpenLiveCommand</c> work.
/// </summary>
[ContentProperty(nameof(Name))]
[AcceptEmptyServiceProvider]
public class PageCommandExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The command name, with or without the <c>Command</c> suffix.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        var name = Name.EndsWith("Command", StringComparison.Ordinal) ? Name : Name + "Command";

        return new Binding(
            path: $"BindingContext.{name}",
            mode: BindingMode.OneWay,
            source: new RelativeBindingSource(RelativeBindingSourceMode.FindAncestor, typeof(INavigable)));
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
