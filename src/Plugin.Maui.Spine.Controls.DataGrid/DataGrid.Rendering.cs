using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Controls;

public partial class DataGrid
{
    private DataGridLayout? _activeLayout;

    /// <summary>Whether the current ItemTemplate is the grouped selector.</summary>
    private bool _templateIsGrouped;

    /// <summary>
    /// The realized row grids, so their striping can be fixed without rebuilding the template.
    /// </summary>
    /// <remarks>
    /// A row's background is set when it is bound, which covers a row handed another item. An
    /// in-place removal shifts the surviving rows up without rebinding them, so their stripes would
    /// invert below the removal; <see cref="RefreshRowStriping"/> repaints them. Weak, because the list
    /// discards and creates row containers as it pleases; pruned on every pass.
    /// </remarks>
    private readonly List<WeakReference<Grid>> _rowGrids = [];

    /// <param name="autoFitOnly">
    /// True when the caller only wants to know whether an Auto width moved. The widths are measured
    /// either way, but when nothing moved the method returns before the ItemTemplate assignment,
    /// which would throw away and re-create every realized row.
    /// </param>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimmingMessage)]
    private void Rebuild(bool autoFitOnly = false)
    {
        if (Columns.Count == 0)
        {
            _activeLayout = null;
            _headerHost.IsVisible = false;
            _collectionView.ItemTemplate = null;
            return;
        }

        var options = ResolveOptions();
        var layout = ResolveActiveLayout();
        WarnUnmeasuredNeverTruncate(layout);
        var autoFitWidths = ComputeAutoFitWidths(layout, options);

        if (autoFitOnly
            && ReferenceEquals(layout, _activeLayout)
            && !AutoFitWidthsDiffer(autoFitWidths, _autoFitWidths))
            return;

        _activeLayout = layout;
        _autoFitWidths = autoFitWidths;

        // Every realized row is about to be replaced, and so is the row a pending press is on.
        _rowGrids.Clear();
        CancelRowPress();

        RebuildHeader();
        UpdateEmptyView();
        UpdateStatusRow();

        var leftActions = LeftSwipeActions.ToArray();
        var rightActions = RightSwipeActions.ToArray();
        var copyEnabled = IsCellCopyEnabled;
        var rowTemplate = new DataTemplate(() => BuildRow(layout, options, autoFitWidths, leftActions, rightActions, copyEnabled));

        _templateIsGrouped = IsGroupingActive;
        if (IsGroupingActive)
        {
            // Two view types: MeasureFirstItem would size every item from whichever realizes first.
            // Measuring each is still cheap, since both templates carry a fixed HeightRequest.
            _collectionView.ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems;
            _collectionView.ItemTemplate = new GroupAwareTemplateSelector
            {
                RowTemplate = rowTemplate,
                HeaderTemplate = GroupHeaderTemplate ?? new DataTemplate(() => BuildDefaultGroupHeader(options)),
            };
        }
        else
        {
            _collectionView.ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem;
            _collectionView.ItemTemplate = rowTemplate;
        }

        if (!ReferenceEquals(_collectionView.ItemsSource, _displayItems))
            _collectionView.ItemsSource = _displayItems;

        // -1 before the first layout: the new rows are measured at whatever width arrives.
        _rowsMeasuredAtWidth = _collectionView.Width;
    }

    private sealed class GroupAwareTemplateSelector : DataTemplateSelector
    {
        public required DataTemplate RowTemplate { get; init; }
        public required DataTemplate HeaderTemplate { get; init; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
            => item is DataGridGroup ? HeaderTemplate : RowTemplate;
    }

    private DataGridLayout ResolveActiveLayout()
    {
        DataGridLayout? layout = null;
        if (!string.IsNullOrEmpty(LayoutMode))
            layout = Layouts.FirstOrDefault(l => l.Name == LayoutMode);
        layout ??= Layouts.FirstOrDefault();
        return layout ?? BuildAutoFlatLayout();
    }

    /// <summary>A flat table for a grid without layouts: the columns in order, one header row.</summary>
    private DataGridLayout BuildAutoFlatLayout()
    {
        var layout = new DataGridLayout { Name = "Auto", HeaderMode = DataGridHeaderMode.TopHeaderRow };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        for (var i = 0; i < Columns.Count; i++)
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = Columns[i].Width });
            layout.Placements.Add(new DataGridCellPlacement { ColumnKey = Columns[i].Key, Column = i });
        }
        return layout;
    }

    // ---------------------------------------------------------------- header

    private void RebuildHeader()
    {
        if (_activeLayout is null)
            return;

        // The header cells a tooltip was anchored to are about to be replaced.
        HideTooltip();

        var options = ResolveOptions();
        var layout = _activeLayout;

        if (layout.HeaderMode is DataGridHeaderMode.None or DataGridHeaderMode.InsideItem)
        {
            _headerHost.IsVisible = false;
            _headerHost.Content = null;
            return;
        }

        var headerGrid = new Grid
        {
            ColumnDefinitions = layout.CloneColumnDefinitions(_autoFitWidths),
            ColumnSpacing = 0,
            RowSpacing = 0,
            BackgroundColor = options.HeaderBackgroundColor,
        };

        // Header rows are Auto so a wrapped caption fits; only item rows have a fixed height.
        var headerRowCount = layout.HeaderMode == DataGridHeaderMode.TopHeaderRow
            ? 1
            : Math.Max(1, layout.RowDefinitions.Count);
        var rows = new RowDefinitionCollection();
        for (var i = 0; i < headerRowCount; i++)
            rows.Add(new RowDefinition { Height = GridLength.Auto });
        headerGrid.RowDefinitions = rows;

        foreach (var placement in layout.Placements)
        {
            if (!placement.HeaderIsVisible)
                continue;
            if (FindColumn(placement.ColumnKey) is not { } column || string.IsNullOrEmpty(column.Header))
                continue;

            var cell = BuildHeaderCell(column, placement, options);
            var topRow = layout.HeaderMode == DataGridHeaderMode.TopHeaderRow;
            headerGrid.Add(cell, placement.Column, topRow ? 0 : placement.Row);
            if (placement.ColumnSpan > 1)
                Grid.SetColumnSpan(cell, placement.ColumnSpan);
            if (!topRow && placement.RowSpan > 1)
                Grid.SetRowSpan(cell, placement.RowSpan);
        }

        _headerHost.Content = headerGrid;
        _headerHost.IsVisible = true;
    }

    /// <summary>
    /// A caption with a space may wrap onto a second line and breaks at the space; a single word
    /// keeps one line and truncates. Character-by-character filling would split a long second word
    /// in half, and splitting a word is worse than an ellipsis.
    /// </summary>
    private static void ApplyHeaderMaxLines(Label label)
    {
        var wraps = label.Text is { Length: > 0 } text && text.Any(char.IsWhiteSpace);
        label.MaxLines = wraps ? 2 : 1;
        label.LineBreakMode = wraps ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation;
    }

    /// <summary>
    /// Centres a centred column on the cell rather than on its padded text box. Cell padding is
    /// asymmetric on purpose (more on the left, to separate a value from the column before it);
    /// averaging the two sides keeps the total inset, and so every Auto width, unchanged.
    /// </summary>
    private static Thickness CenterAwarePadding(Thickness padding, TextAlignment alignment)
    {
        if (alignment != TextAlignment.Center || padding.Left.Equals(padding.Right))
            return padding;

        var horizontal = (padding.Left + padding.Right) / 2;
        return new Thickness(horizontal, padding.Top, horizontal, padding.Bottom);
    }

    private View BuildHeaderCell(DataGridColumn column, DataGridCellPlacement placement, DataGridStyleOptions options)
    {
        var alignment = placement.HeaderHorizontalTextAlignment
            ?? placement.HorizontalTextAlignment
            ?? column.HorizontalTextAlignment;

        // Bound rather than copied: a {String} or other binding may deliver the text after the
        // header is built.
        var label = new Label
        {
            FontFamily = options.HeaderFontFamily ?? options.FontFamily,
            FontSize = options.HeaderFontSize,
            FontAttributes = options.HeaderFontAttributes,
            TextColor = options.HeaderTextColor,
            HorizontalTextAlignment = alignment,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        label.SetBinding(Label.TextProperty, static (DataGridColumn c) => c.Header, source: column);
        ApplyHeaderMaxLines(label);
        label.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == Label.TextProperty.PropertyName && sender is Label header)
            {
                ApplyHeaderMaxLines(header);
                SetHeaderSemantics(header.Parent as View, column);
            }
        };

        var direction = column.IsSortable && SortColumnKey == column.Key ? SortDirection : DataGridSortDirection.None;
        var isSorted = direction != DataGridSortDirection.None;

        // The caret is a separate element, so the caption truncates and the caret never wraps below
        // it. Its column exists only while the column is sorted: a centred header sits on the cell's
        // midpoint unsorted and moves left by the caret while sorted.
        var container = new Grid
        {
            ColumnDefinitions = isSorted
                ? [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)]
                : [new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 4,
            Padding = CenterAwarePadding(placement.Padding ?? options.HeaderCellPadding, alignment),
            BackgroundColor = Colors.Transparent,
            MinimumHeightRequest = options.HeaderRowHeight,
        };
        container.Add(label, 0, 0);

        if (isSorted)
            container.Add(BuildSortCaret(direction, options), 1, 0);

        // A tap recognizer on every header, sortable or not: it is also what ends a short press for
        // the tooltip. Android does not deliver PointerReleased once its tap detector has claimed the
        // touch, so without it a plain tap would grow into a long press.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            CancelHeaderLongPress();

            if (column.IsSortable)
                OnHeaderSortTapped(column);
        };
        container.GestureRecognizers.Add(tap);

        AttachHeaderTooltip(container, column, options);
        SetHeaderSemantics(container, column);

        return container;
    }

    private void SetHeaderSemantics(View? cell, DataGridColumn column)
    {
        if (cell is null || !column.IsSortable)
            return;

        var strings = SpineStrings.Current;
        var direction = SortColumnKey == column.Key ? SortDirection : DataGridSortDirection.None;
        SemanticProperties.SetDescription(cell, direction switch
        {
            DataGridSortDirection.Ascending => strings.Get("Spine.DataGrid.Header.SortedAscending", column.Header),
            DataGridSortDirection.Descending => strings.Get("Spine.DataGrid.Header.SortedDescending", column.Header),
            _ => column.Header,
        });
        SemanticProperties.SetHint(cell, strings["Spine.DataGrid.Header.SortHint"]);
    }

    private static Microsoft.Maui.Controls.Shapes.Path BuildSortCaret(DataGridSortDirection direction, DataGridStyleOptions options)
    {
        var size = options.SortIndicatorSize;
        return new Microsoft.Maui.Controls.Shapes.Path
        {
            // An upward caret; descending turns it over.
            Data = Polyline(new(size * 0.1, size * 0.7), new(size * 0.5, size * 0.3), new(size * 0.9, size * 0.7)),
            Stroke = options.SortIndicatorColor,
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = size,
            HeightRequest = size,
            Rotation = direction == DataGridSortDirection.Descending ? 180 : 0,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
    }

    private static PathGeometry Polyline(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(new PointCollection(points[1..])));
        return new PathGeometry { Figures = { figure } };
    }

    // ---------------------------------------------------------------- row template

    [RequiresUnreferencedCode(TrimmingMessage)]
    private View BuildRow(
        DataGridLayout layout,
        DataGridStyleOptions options,
        IReadOnlyDictionary<int, double>? autoFitWidths,
        DataGridSwipeAction[] leftActions,
        DataGridSwipeAction[] rightActions,
        bool copyEnabled)
    {
        // The Material list scale: 56 for one sub-row, +16 per extra one. The layout's own height wins.
        var subRowCount = Math.Max(1, layout.RowDefinitions.Count);
        var itemHeight = layout.ItemHeight > 0
            ? layout.ItemHeight
            : options.ItemHeight + options.MultiRowItemExtraHeight * (subRowCount - 1);

        var rowGrid = new Grid
        {
            ColumnDefinitions = layout.CloneColumnDefinitions(autoFitWidths),
            RowDefinitions = layout.CloneRowDefinitions(),
            ColumnSpacing = 0,
            RowSpacing = 0,
            HeightRequest = itemHeight,
        };

        // IndexOf is O(n) per bind; fine up to a few thousand rows (documented).
        rowGrid.BindingContextChanged += OnRowBindingContextChanged;
        _rowGrids.Add(new WeakReference<Grid>(rowGrid));

        var cells = new List<RowCell>();

        foreach (var placement in layout.Placements)
        {
            if (FindColumn(placement.ColumnKey) is not { } column)
                continue;

            var cell = BuildCellView(column, placement, options, copyEnabled, out var rowCell);
            rowGrid.Add(cell, placement.Column, placement.Row);
            if (placement.ColumnSpan > 1)
                Grid.SetColumnSpan(cell, placement.ColumnSpan);
            if (placement.RowSpan > 1)
                Grid.SetRowSpan(cell, placement.RowSpan);

            if (rowCell is not null)
                cells.Add(rowCell);

            if (layout.HeaderMode == DataGridHeaderMode.InsideItem
                && placement.HeaderIsVisible
                && !string.IsNullOrEmpty(column.Header))
            {
                var headerAlignment = placement.HeaderHorizontalTextAlignment
                    ?? placement.HorizontalTextAlignment
                    ?? column.HorizontalTextAlignment;
                var headerLabel = new Label
                {
                    Text = column.Header,
                    FontFamily = options.FontFamily,
                    FontSize = options.FontSize * 0.8,
                    TextColor = options.MutedTextColor,
                    Padding = CenterAwarePadding(placement.Padding ?? options.CellPadding, headerAlignment),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1,
                    HorizontalTextAlignment = headerAlignment,
                    VerticalTextAlignment = TextAlignment.Start,
                    InputTransparent = true,
                };
                rowGrid.Add(headerLabel, placement.Column, placement.Row);
                if (placement.ColumnSpan > 1)
                    Grid.SetColumnSpan(headerLabel, placement.ColumnSpan);
                if (placement.RowSpan > 1)
                    Grid.SetRowSpan(headerLabel, placement.RowSpan);
            }
        }

        AttachRowPress(rowGrid, [.. cells]);

        if (leftActions.Length == 0 && rightActions.Length == 0)
            return rowGrid;

        // A SwipeView costs a native container per row, so only pages with actions pay for it.
        var swipe = CreateSwipeView(rowGrid);
        if (leftActions.Length > 0)
            swipe.LeftItems = BuildSwipeItems(leftActions, options);
        if (rightActions.Length > 0)
            swipe.RightItems = BuildSwipeItems(rightActions, options);

        return GateSwipe(swipe);
    }

    private void OnRowBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not Grid rowGrid || rowGrid.BindingContext is null)
            return;
        ApplyRowStripe(rowGrid, ResolveOptions());
    }

    private void ApplyRowStripe(Grid rowGrid, DataGridStyleOptions options)
    {
        var index = GetStripeIndex(rowGrid.BindingContext);
        rowGrid.BackgroundColor = index >= 0 && index % 2 == 1
            ? options.AlternatingRowBackgroundColor
            : options.RowBackgroundColor;
    }

    /// <summary>Re-applies the stripe colour to every realized row; see <see cref="_rowGrids"/>.</summary>
    private void RefreshRowStriping()
    {
        if (_rowGrids.Count == 0)
            return;

        var options = ResolveOptions();
        for (var i = _rowGrids.Count - 1; i >= 0; i--)
        {
            if (!_rowGrids[i].TryGetTarget(out var rowGrid))
            {
                _rowGrids.RemoveAt(i);
                continue;
            }

            // A parked container with no item gets its colour when it is bound.
            if (rowGrid.BindingContext is not null)
                ApplyRowStripe(rowGrid, options);
        }
    }

    // ---------------------------------------------------------------- group header

    /// <summary>
    /// The default group header: a chevron zone that toggles <see cref="DataGridGroup.IsExpanded"/>
    /// and a text zone that runs <see cref="GroupTappedCommand"/>. Its binding context is the group.
    /// </summary>
    private View BuildDefaultGroupHeader(DataGridStyleOptions options)
    {
        var headerGrid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 0,
            HeightRequest = options.GroupHeaderHeight,
            BackgroundColor = options.GroupHeaderBackgroundColor,
        };

        // A downward chevron, turned to point right while collapsed.
        const double size = 14;
        var chevron = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = Polyline(new(size * 0.2, size * 0.35), new(size * 0.5, size * 0.65), new(size * 0.8, size * 0.35)),
            Stroke = options.GroupChevronColor,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = size,
            HeightRequest = size,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
        chevron.SetBinding(RotationProperty, static (DataGridGroup g) => g.IsExpanded, converter: ChevronRotationConverter.Instance);

        // At least 44 wide: Apple's minimum touch target.
        var chevronZone = new Grid
        {
            WidthRequest = Math.Max(44, options.GroupHeaderHeight),
            BackgroundColor = Colors.Transparent,
        };
        chevronZone.Add(chevron);
        var toggleTap = new TapGestureRecognizer();
        toggleTap.Tapped += (sender, _) =>
        {
            if (((BindableObject)sender!).BindingContext is DataGridGroup group)
                group.IsExpanded = !group.IsExpanded;
        };
        chevronZone.GestureRecognizers.Add(toggleTap);
        chevronZone.BindingContextChanged += (_, _) => SetGroupSemantics(chevronZone);
        headerGrid.Add(chevronZone, 0, 0);

        var text = new Label
        {
            FontFamily = options.GroupHeaderFontFamily ?? options.HeaderFontFamily ?? options.FontFamily,
            FontSize = options.GroupHeaderFontSize,
            FontAttributes = options.GroupHeaderFontAttributes,
            TextColor = options.GroupHeaderTextColor,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
        text.SetBinding(Label.TextProperty, static (DataGridGroup g) => g.DisplayText);

        var textZone = new Grid
        {
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(0, 0, options.HeaderCellPadding.Right, 0),
        };
        textZone.Add(text);
        var navTap = new TapGestureRecognizer();
        navTap.Tapped += (sender, _) =>
        {
            if (((BindableObject)sender!).BindingContext is not DataGridGroup group)
                return;
            // Without a command, the whole header toggles.
            if (GroupTappedCommand is not { } command)
            {
                group.IsExpanded = !group.IsExpanded;
                return;
            }
            if (!group.IsUngrouped && command.CanExecute(group))
                command.Execute(group);
        };
        textZone.GestureRecognizers.Add(navTap);
        headerGrid.Add(textZone, 1, 0);

        // A hairline in the row colour, so two collapsed groups read as two bands.
        var separator = new BoxView
        {
            HeightRequest = 1,
            VerticalOptions = LayoutOptions.End,
            Color = options.RowBackgroundColor,
            InputTransparent = true,
        };
        headerGrid.Add(separator, 0, 0);
        Grid.SetColumnSpan(separator, 2);

        return headerGrid;
    }

    private static void SetGroupSemantics(View chevronZone)
    {
        if (chevronZone.BindingContext is not DataGridGroup group)
            return;

        void Apply()
        {
            var strings = SpineStrings.Current;
            SemanticProperties.SetDescription(chevronZone, strings.Get(
                group.IsExpanded ? "Spine.DataGrid.Group.Expanded" : "Spine.DataGrid.Group.Collapsed", group.DisplayText));
            SemanticProperties.SetHint(chevronZone, strings["Spine.DataGrid.Group.ToggleHint"]);
        }

        Apply();
        group.PropertyChanged += (_, e) =>
        {
            if (ReferenceEquals(chevronZone.BindingContext, group)
                && e.PropertyName is nameof(DataGridGroup.IsExpanded) or nameof(DataGridGroup.DisplayText))
                Apply();
        };
    }

    private sealed class ChevronRotationConverter : IValueConverter
    {
        public static readonly ChevronRotationConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is true ? 0d : -90d;

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- cells

    [RequiresUnreferencedCode(TrimmingMessage)]
    private View BuildCellView(DataGridColumn column, DataGridCellPlacement placement, DataGridStyleOptions options, bool copyEnabled, out RowCell? rowCell)
    {
        rowCell = null;

        switch (column.Type)
        {
            case DataGridColumnType.Template when column.CellTemplate is not null:
                return column.CellTemplate.CreateContent() as View
                    ?? throw new InvalidOperationException($"The CellTemplate of column '{column.Key}' must create a View.");

            case DataGridColumnType.Checkbox:
                var checkBox = BuildCheckbox(column, options);
                var container = new Grid { BackgroundColor = Colors.Transparent, Children = { checkBox } };
                rowCell = new RowCell(container, column, RowCellKind.Checkbox, null, checkBox);
                return container;

            case DataGridColumnType.Image:
                var image = new Image
                {
                    Aspect = Aspect.AspectFit,
                    Margin = placement.Padding ?? new Thickness(4),
                };
                image.SetBinding(Image.SourceProperty, new Binding(column.BindingPath));
                return image;

            default:
                return BuildTextCell(column, placement, options, copyEnabled, out rowCell);
        }
    }

    [RequiresUnreferencedCode(TrimmingMessage)]
    private static View BuildTextCell(DataGridColumn column, DataGridCellPlacement placement, DataGridStyleOptions options, bool copyEnabled, out RowCell? rowCell)
    {
        var isLink = column.CellCommand is not null || column.IsSet(DataGridColumn.CellCommandProperty);
        var alignment = placement.HorizontalTextAlignment ?? column.HorizontalTextAlignment;

        var label = new Label
        {
            FontFamily = column.Type == DataGridColumnType.Glyph ? column.FontFamily : column.FontFamily ?? options.FontFamily,
            FontSize = placement.FontSize > 0 ? placement.FontSize
                : column.FontSize > 0 ? column.FontSize
                : options.FontSize,
            FontAttributes = placement.FontAttributes ?? column.FontAttributes,
            TextColor = isLink ? options.LinkColor : column.TextColor ?? options.TextColor,
            TextDecorations = isLink ? TextDecorations.Underline : TextDecorations.None,
            Padding = CenterAwarePadding(placement.Padding ?? options.CellPadding, alignment),
            LineBreakMode = column.LineBreakMode,
            MaxLines = placement.MaxLines ?? column.MaxLines,
            HorizontalTextAlignment = alignment,
            VerticalTextAlignment = placement.VerticalTextAlignment ?? column.VerticalTextAlignment,
        };

        label.SetBinding(Label.TextProperty, DataGridFormatConverter.Applies(column)
            ? new Binding(column.BindingPath, converter: new DataGridFormatConverter(column))
            : new Binding(column.BindingPath));

        // Glyphs are icons, not values.
        var copySource = copyEnabled && column.Type != DataGridColumnType.Glyph ? label : null;
        if (isLink || copySource is not null)
            rowCell = new RowCell(label, column, isLink ? RowCellKind.Link : RowCellKind.Text, copySource, null);
        else
            rowCell = null;

        return label;
    }

    [RequiresUnreferencedCode(TrimmingMessage)]
    private static CheckBox BuildCheckbox(DataGridColumn column, DataGridStyleOptions options)
    {
        var checkBox = new CheckBox
        {
            Color = options.AccentColor,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = options.CheckboxSize + 8,
            HeightRequest = options.CheckboxSize + 8,
            // The row's press handler owns the tap, so the whole cell is the target on every platform.
            InputTransparent = true,
        };
        if (!string.IsNullOrEmpty(column.BindingPath))
            checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(column.BindingPath, BindingMode.TwoWay));
        if (!string.IsNullOrEmpty(column.IsEnabledPath))
            checkBox.SetBinding(IsEnabledProperty, new Binding(column.IsEnabledPath));
        return checkBox;
    }

    // ---------------------------------------------------------------- swipe actions

    [RequiresUnreferencedCode(TrimmingMessage)]
    private static SwipeItems BuildSwipeItems(DataGridSwipeAction[] actions, DataGridStyleOptions options)
    {
        var items = new SwipeItems
        {
            Mode = SwipeMode.Reveal,
            SwipeBehaviorOnInvoked = SwipeBehaviorOnInvoked.Close,
        };

        foreach (var action in actions)
        {
            // A SwipeItemView rather than a SwipeItem: the platform decides a SwipeItem's text colour.
            var textColor = action.TextColor ?? options.SwipeActionTextColor!;
            var stack = new VerticalStackLayout
            {
                Spacing = 4,
                Padding = new Thickness(12, 0),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            if (!string.IsNullOrEmpty(action.IconSvg))
            {
                // Rendered once at the icon size rather than through SvgImageSource, whose size
                // follows the view's: a swipe item is measured natively, at the bitmap's own size,
                // and the icon came out a third of its size on iOS.
                var names = IPlatformApplication.Current?.Services.GetService(typeof(ResourceNameCache)) as ResourceNameCache;
                stack.Add(new Image
                {
                    Source = SvgBitmapLoader.LoadFromEmbedded(names?.Resolve(action.IconSvg) ?? action.IconSvg, action.IconSize, action.IconSize, textColor),
                    WidthRequest = action.IconSize,
                    HeightRequest = action.IconSize,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center,
                });
            }
            else if (!string.IsNullOrEmpty(action.IconGlyph))
            {
                stack.Add(new Label
                {
                    Text = action.IconGlyph,
                    FontFamily = action.IconFontFamily,
                    FontSize = action.IconSize,
                    TextColor = textColor,
                    HorizontalTextAlignment = TextAlignment.Center,
                });
            }
            // One line, so a multi-word label never wraps and clips against the row height; the
            // action widens instead.
            var textLabel = new Label
            {
                FontFamily = options.FontFamily,
                FontSize = options.FontSize,
                TextColor = textColor,
                LineBreakMode = LineBreakMode.NoWrap,
                MaxLines = 1,
                HorizontalTextAlignment = TextAlignment.Center,
            };
            textLabel.SetBinding(Label.TextProperty, static (DataGridSwipeAction a) => a.Text, source: action);
            stack.Add(textLabel);

            var itemView = new SwipeItemView
            {
                Content = new Grid
                {
                    BackgroundColor = action.BackgroundColor ?? options.SwipeActionBackgroundColor,
                    MinimumWidthRequest = 88,
                    Children = { stack },
                },
            };
            itemView.SetBinding(SwipeItemView.CommandProperty, static (DataGridSwipeAction a) => a.Command, source: action);
            itemView.SetBinding(SwipeItemView.CommandParameterProperty, new Binding(action.CommandParameterPath ?? "."));
            if (!string.IsNullOrEmpty(action.IsVisiblePath))
                itemView.SetBinding(IsVisibleProperty, new Binding(action.IsVisiblePath));
            if (!string.IsNullOrEmpty(action.IsEnabledPath))
                itemView.SetBinding(IsEnabledProperty, new Binding(action.IsEnabledPath));

            items.Add(itemView);
        }

        return items;
    }
}
