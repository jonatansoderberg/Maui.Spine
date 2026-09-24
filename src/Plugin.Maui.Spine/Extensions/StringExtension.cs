using System.Globalization;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Text from <see cref="ISpineStrings"/> by key, re-resolved when the culture changes:
/// <c>{String Home.Greeting}</c>. <see cref="Args"/> binds up to three format placeholders and
/// <see cref="Count"/> picks the plural form with the count as <c>{0}</c>.
/// </summary>
/// <example>
/// <code>
/// &lt;Label Text="{String Home.Greeting, Args={Binding UserName}}" /&gt;
/// &lt;Label Text="{String Events.Count, Count={Binding Total}}" /&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class StringExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The string's key, e.g. <c>Home.Greeting</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>A binding whose value is the count for a plural key.</summary>
    public BindingBase? Count { get; set; }

    /// <summary>A binding for <c>{0}</c>.</summary>
    public BindingBase? Args { get; set; }

    /// <summary>A binding for <c>{1}</c>.</summary>
    public BindingBase? Arg1 { get; set; }

    /// <summary>A binding for <c>{2}</c>.</summary>
    public BindingBase? Arg2 { get; set; }

    /// <inheritdoc/>
    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        var source = StringSource.Instance;

        // Always a MultiBinding: an indexer path cannot carry a dotted key, the binding parser
        // reads the dot as a member access inside the brackets.
        var multi = new MultiBinding
        {
            Converter = new FormatConverter(Key, Count is not null),
            Bindings = { new Binding(nameof(StringSource.Version), BindingMode.OneWay, source: source) },
        };

        foreach (var binding in new[] { Count, Args, Arg1, Arg2 })
        {
            if (binding is not null)
                multi.Bindings.Add(binding);
        }

        return multi;
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);

    /// <summary>
    /// The one binding source behind every <c>{String}</c>: a version that moves with the store's
    /// <see cref="ISpineStrings.Changed"/>, so every binding re-converts on a culture switch.
    /// </summary>
    private sealed class StringSource : System.ComponentModel.INotifyPropertyChanged
    {
        public static StringSource Instance { get; } = new();

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public int Version { get; private set; }

        private StringSource()
        {
            SpineStrings.Current.Changed += (_, _) =>
            {
                Version++;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Version)));
            };
        }
    }

    private sealed class FormatConverter(string key, bool plural) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0] is the version, there only to re-run the conversion on a culture switch.
            var strings = SpineStrings.Current;

            if (plural)
                return strings.Get(key, values.Length > 1 ? ToInt(values[1]) : 0);

            return values.Length > 1 ? strings.Get(key, values[1..]) : strings[key];
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static int ToInt(object? value) => value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var i) => i,
            _ => 0,
        };
    }
}
