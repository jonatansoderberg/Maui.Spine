using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#if IOS || MACCATALYST
using UIKit;
#endif

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Content-fit columns. <c>Width="Auto"</c> in a layout means "fit the widest of the header and the
/// loaded values". Grid's own Auto cannot do that here: every row is an independent grid, so per-row
/// Auto would misalign the columns. The grid measures the formatted strings with the platform text
/// engine instead and resolves Auto to one width shared by the header and every row.
/// </summary>
public partial class DataGrid
{
    private Dictionary<int, double>? _autoFitWidths;

    private const int AutoFitMaxItemsMeasured = 200;

    [RequiresUnreferencedCode(TrimmingMessage)]
    private Dictionary<int, double>? ComputeAutoFitWidths(DataGridLayout layout, DataGridStyleOptions options)
    {
        List<int>? autoIndices = null;
        for (var i = 0; i < layout.ColumnDefinitions.Count; i++)
        {
            if (layout.ColumnDefinitions[i].Width.IsAuto)
                (autoIndices ??= []).Add(i);
        }
        if (autoIndices is null)
            return null;

        // One measuring context per pass: fonts and paints are constant per (family, size).
        using var measure = new TextMeasureContext(Handler?.MauiContext);

        var result = new Dictionary<int, double>();
        foreach (var index in autoIndices)
        {
            double max = 0;
            foreach (var placement in layout.Placements)
            {
                if (FindColumn(placement.ColumnKey) is not { } column)
                    continue;

                // The header counts when it occupies exactly this column (it mirrors the value).
                if (placement.HeaderIsVisible && placement.Column == index && placement.ColumnSpan == 1
                    && layout.HeaderMode is DataGridHeaderMode.TopHeaderRow or DataGridHeaderMode.StaticLayoutHeader
                    && !string.IsNullOrEmpty(column.Header))
                {
                    var headerPadding = (placement.Padding ?? options.HeaderCellPadding).HorizontalThickness;
                    // Only the sorted column carries the caret, so only it reserves room for one.
                    var isSorted = column.IsSortable && SortColumnKey == column.Key && SortDirection != DataGridSortDirection.None;
                    var caretAllowance = isSorted ? options.SortIndicatorSize + 4 : 0;
                    var width = measure.Measure(column.Header, options.HeaderFontFamily ?? options.FontFamily, options.HeaderFontSize, options.HeaderFontAttributes)
                        + headerPadding + caretAllowance;
                    max = Math.Max(max, width);
                }

                if (placement.Column != index || placement.ColumnSpan != 1)
                    continue;

                if (column.Type == DataGridColumnType.Checkbox)
                {
                    max = Math.Max(max, options.CheckboxSize + 16);
                    continue;
                }
                // A NeverTruncate template is measured by its BindingPath, in the grid font and padding.
                if (column.Type is DataGridColumnType.Image
                    || (column.Type == DataGridColumnType.Template && !column.NeverTruncate))
                    continue; // not measurable: give these an explicit width

                // Wrapping cells need not fit on one line.
                if ((placement.MaxLines ?? column.MaxLines) > 1)
                    continue;

                if (_sourceList is null || string.IsNullOrEmpty(column.BindingPath))
                    continue;

                max = Math.Max(max, MeasureWidestValue(column, placement, options, measure, _sourceList, 0,
                    column.NeverTruncate ? int.MaxValue : AutoFitMaxItemsMeasured));
            }
            result[index] = FitWidth(max, HoldsNeverTruncateValue(layout, index));
        }
        return result;
    }

    // Safety factor: Android's TextView renders slightly wider than TextPaint.MeasureText reports, and
    // rounding differs per platform; 5% + 4 covers an ordinary column. A NeverTruncate column gets
    // twice the slack: digits differ in width, so two identifiers of the same length are not the same
    // width, and a cropped identifier reads as another one.
    private static double FitWidth(double measured, bool neverTruncate) => neverTruncate
        ? Math.Ceiling(measured * 1.10) + 8
        : Math.Ceiling(measured * 1.05) + 4;

    private bool HoldsNeverTruncateValue(DataGridLayout layout, int columnIndex)
    {
        foreach (var placement in layout.Placements)
        {
            if (placement.Column == columnIndex && placement.ColumnSpan == 1
                && FindColumn(placement.ColumnKey) is { NeverTruncate: true })
                return true;
        }
        return false;
    }

    /// <summary>Widest formatted value of <paramref name="column"/> among at most <paramref name="maxItems"/> items from <paramref name="start"/>, padding included.</summary>
    [RequiresUnreferencedCode(TrimmingMessage)]
    private static double MeasureWidestValue(
        DataGridColumn column,
        DataGridCellPlacement placement,
        DataGridStyleOptions options,
        TextMeasureContext measure,
        IList items,
        int start,
        int maxItems)
    {
        var fontFamily = column.FontFamily ?? options.FontFamily;
        var fontSize = placement.FontSize > 0 ? placement.FontSize
            : column.FontSize > 0 ? column.FontSize
            : options.FontSize;
        var attributes = placement.FontAttributes ?? column.FontAttributes;
        var cellPadding = (placement.Padding ?? options.CellPadding).HorizontalThickness;
        var formatter = DataGridFormatConverter.Applies(column) ? new DataGridFormatConverter(column) : null;

        double max = 0;
        var end = items.Count - start <= maxItems ? items.Count : start + maxItems;
        for (var i = start; i < end; i++)
        {
            // A group header in the display list has no column values.
            if (items[i] is DataGridGroup)
                continue;

            var value = column.GetItemValue(items[i]);
            var text = formatter is not null ? formatter.Format(value) : value?.ToString();
            if (string.IsNullOrEmpty(text))
                continue;
            max = Math.Max(max, measure.Measure(text, fontFamily, fontSize, attributes) + cellPadding);
        }
        return max;
    }

    // ---------------------------------------------------------------- NeverTruncate after an append

    /// <summary>First index of the rows the last sync appended in place, or -1 when it was not an append.</summary>
    private int _appendedFrom = -1;

    /// <summary>
    /// After the display list was synced: a fresh render measures the Auto columns again; an in-place
    /// edit keeps the widths frozen, so load more cannot reset the scroll position, unless appended
    /// rows need more room in a NeverTruncate column.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimmingMessage)]
    private void AfterDisplaySourceSynced(bool handledInPlace)
    {
        if (handledInPlace)
        {
            if (_appendedFrom >= 0 && NeverTruncateColumnOutgrown(_appendedFrom))
                QueueRebuild(autoFitOnly: true);
            return;
        }

        // A fresh list is measured now, in the same turn as the new ItemsSource. Queued, the platform
        // would realise every visible row with the old template first and throw them away when an
        // Auto width moved. Nothing is realised before the next layout pass.
        if (_rebuildQueued || Handler is null)
        {
            QueueRebuild(autoFitOnly: true);
            return;
        }

        try
        {
            Rebuild(autoFitOnly: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataGrid] rebuild failed: {ex}");
        }
    }

    /// <summary>
    /// True when a row appended from <paramref name="appendedFrom"/> needs more room in a NeverTruncate
    /// column than the frozen width. Only the new rows are measured.
    /// </summary>
    [RequiresUnreferencedCode(TrimmingMessage)]
    private bool NeverTruncateColumnOutgrown(int appendedFrom)
    {
        if (_activeLayout is not { } layout || _autoFitWidths is null)
            return false;

        TextMeasureContext? measure = null;
        try
        {
            var options = ResolveOptions();
            foreach (var placement in layout.Placements)
            {
                if (placement.ColumnSpan != 1
                    || !_autoFitWidths.TryGetValue(placement.Column, out var frozen)
                    || FindColumn(placement.ColumnKey) is not { NeverTruncate: true } column
                    || string.IsNullOrEmpty(column.BindingPath)
                    || column.Type is DataGridColumnType.Image or DataGridColumnType.Checkbox
                    || (placement.MaxLines ?? column.MaxLines) > 1)
                    continue;

                measure ??= new TextMeasureContext(Handler?.MauiContext);
                var widest = MeasureWidestValue(column, placement, options, measure, _displayItems, appendedFrom, int.MaxValue);
                if (FitWidth(widest, neverTruncate: true) > frozen + 0.5)
                    return true;
            }
            return false;
        }
        finally
        {
            measure?.Dispose();
        }
    }

#if DEBUG
    private HashSet<string>? _neverTruncateWarnings;
#endif

    /// <summary>Debug builds: reports a NeverTruncate column placed where its width is not measured.</summary>
    private void WarnUnmeasuredNeverTruncate(DataGridLayout layout)
    {
#if DEBUG
        foreach (var placement in layout.Placements)
        {
            if (FindColumn(placement.ColumnKey) is not { NeverTruncate: true } column)
                continue;
            var inAuto = placement.Column < layout.ColumnDefinitions.Count
                && layout.ColumnDefinitions[placement.Column].Width.IsAuto;
            if (inAuto && placement.ColumnSpan == 1)
                continue;
            if ((_neverTruncateWarnings ??= []).Add($"{layout.Name}/{column.Key}"))
            {
                Debug.WriteLine(
                    $"[DataGrid] layout '{layout.Name}': NeverTruncate column '{column.Key}' is placed in "
                    + (inAuto ? "a span of several columns" : "a fixed or star column")
                    + ", so its width is not measured and the value can be cropped. Place it alone in a Width=\"Auto\" column.");
            }
        }
#endif
    }

    private static bool AutoFitWidthsDiffer(Dictionary<int, double>? a, Dictionary<int, double>? b)
    {
        if (a is null || b is null)
            return !ReferenceEquals(a, b);
        if (a.Count != b.Count)
            return true;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || Math.Abs(value - other) > 0.5)
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- text measurement

    /// <summary>
    /// Single-line text width in device-independent units, measured with the platform text engine
    /// and the font MAUI resolves for the family. An estimate until the grid has a handler; attaching
    /// measures again.
    /// </summary>
    /// <remarks>
    /// One instance per pass, caching the platform objects per (family, size, attributes): on Android
    /// a <c>TextPaint</c> is a Java peer, and allocating one per measured cell meant hundreds per column.
    /// </remarks>
    private sealed class TextMeasureContext : IDisposable
    {
        private readonly IFontManager? _fontManager;
#if ANDROID
        private readonly Android.Util.DisplayMetrics? _metrics;
        private readonly Dictionary<(string?, double, FontAttributes), Android.Text.TextPaint> _paints = [];
#elif IOS || MACCATALYST
        private readonly Dictionary<(string?, double, FontAttributes), UIFont> _fonts = [];
#elif WINDOWS
        private readonly Dictionary<(string?, double, FontAttributes), Microsoft.UI.Xaml.Controls.TextBlock> _blocks = [];
#endif

        public TextMeasureContext(IMauiContext? mauiContext)
        {
            _fontManager = mauiContext?.Services?.GetService(typeof(IFontManager)) as IFontManager;
#if ANDROID
            // The handler's context (the activity), not Application.Context: an app may scale the font
            // on its activity, and sp must resolve against the scale the TextViews render with.
            _metrics = (mauiContext?.Context ?? Android.App.Application.Context).Resources?.DisplayMetrics;
#endif
        }

        public double Measure(string text, string? fontFamily, double fontSize, FontAttributes attributes)
        {
            if (_fontManager is null)
                return Estimate(text, fontSize);

            var font = Microsoft.Maui.Font.OfSize(
                fontFamily,
                fontSize,
                attributes.HasFlag(FontAttributes.Bold) ? FontWeight.Bold : FontWeight.Regular,
                attributes.HasFlag(FontAttributes.Italic) ? FontSlant.Italic : FontSlant.Default);
#if ANDROID
            if (_metrics is null)
                return Estimate(text, fontSize);

            return GetPaint(font, fontFamily, fontSize, attributes).MeasureText(text) / _metrics.Density;
#elif IOS || MACCATALYST
            using var nsText = new Foundation.NSString(text);
            return nsText.GetSizeUsingAttributes(new UIStringAttributes { Font = GetFont(font, fontFamily, fontSize, attributes) }).Width;
#elif WINDOWS
            var block = GetBlock(font, fontFamily, fontSize, attributes);
            block.Text = text;
            block.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return block.DesiredSize.Width;
#else
            return Estimate(text, fontSize);
#endif
        }

        /// <summary>A rough width for before the grid is attached.</summary>
        private static double Estimate(string text, double fontSize) => text.Length * fontSize * 0.6;

#if ANDROID
        private Android.Text.TextPaint GetPaint(Microsoft.Maui.Font font, string? fontFamily, double fontSize, FontAttributes attributes)
        {
            if (_paints.TryGetValue((fontFamily, fontSize, attributes), out var cached))
                return cached;

            var paint = new Android.Text.TextPaint
            {
                TextSize = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Sp, (float)fontSize, _metrics),
            };
            paint.SetTypeface(_fontManager!.GetTypeface(font));
            _paints[(fontFamily, fontSize, attributes)] = paint;
            return paint;
        }
#elif IOS || MACCATALYST
        private UIFont GetFont(Microsoft.Maui.Font font, string? fontFamily, double fontSize, FontAttributes attributes)
        {
            if (_fonts.TryGetValue((fontFamily, fontSize, attributes), out var cached))
                return cached;

            var uiFont = _fontManager!.GetFont(font, fontSize);
            _fonts[(fontFamily, fontSize, attributes)] = uiFont;
            return uiFont;
        }
#elif WINDOWS
        private Microsoft.UI.Xaml.Controls.TextBlock GetBlock(Microsoft.Maui.Font font, string? fontFamily, double fontSize, FontAttributes attributes)
        {
            if (_blocks.TryGetValue((fontFamily, fontSize, attributes), out var cached))
                return cached;

            var block = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                FontFamily = _fontManager!.GetFontFamily(font),
                FontSize = _fontManager.GetFontSize(font),
                FontWeight = font.Weight == FontWeight.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                FontStyle = font.Slant == FontSlant.Default ? global::Windows.UI.Text.FontStyle.Normal : global::Windows.UI.Text.FontStyle.Italic,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
            };
            _blocks[(fontFamily, fontSize, attributes)] = block;
            return block;
        }
#endif

        public void Dispose()
        {
#if ANDROID
            // Each TextPaint holds a Java peer; release them rather than leave them to the GC bridge.
            foreach (var paint in _paints.Values)
                paint.Dispose();
            _paints.Clear();
#elif IOS || MACCATALYST
            _fonts.Clear();
#elif WINDOWS
            _blocks.Clear();
#endif
        }
    }
}
