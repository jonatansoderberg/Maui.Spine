using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Plugin.Maui.Spine.Widgets.Services;
using SkiaSharp;

namespace Plugin.Maui.Spine.Widgets;

/// <summary>Where <see cref="WidgetPreview"/> draws a tree: the surfaces a widget or Live Activity is shown on.</summary>
public enum WidgetPreviewPlace
{
    /// <summary>A Live Activity on the Lock Screen.</summary>
    LockScreen,
    /// <summary>The expanded Dynamic Island: leading, trailing, center and bottom.</summary>
    IslandExpanded,
    /// <summary>The compact Dynamic Island: leading and trailing beside the camera.</summary>
    IslandCompact,
    /// <summary>The minimal Dynamic Island, beside another activity.</summary>
    IslandMinimal,
    /// <summary>The activity's Small presentation: CarPlay's Dashboard and the watch's Smart Stack.</summary>
    CarPlay,
    /// <summary>Android's Live Update: its text, and the tree as the expanded notification draws it.</summary>
    Notification,
    /// <summary>A widget at <see cref="WidgetPreview.Family"/>.</summary>
    HomeScreen,
}

/// <summary>
/// Draws a widget tree or a Live Activity in the app, at the size of the place it is shown and in light or dark, from
/// the same JSON the platforms get: shared nodes put in, the overrides for the platform and the place applied.
/// <see cref="Notes"/> is what <c>Explain()</c> says about it. The drawing is MAUI's, close to iOS's and Android's
/// without being either: for checking a layout while it is written, not instead of looking at the real surface.
/// </summary>
public sealed class WidgetPreview : ContentView
{
    /// <summary>The bindable property behind <see cref="Activity"/>.</summary>
    public static readonly BindableProperty ActivityProperty = BindableProperty.Create(nameof(Activity), typeof(LiveActivityLayout), typeof(WidgetPreview), propertyChanged: Redraw);
    /// <summary>The bindable property behind <see cref="Tree"/>.</summary>
    public static readonly BindableProperty TreeProperty = BindableProperty.Create(nameof(Tree), typeof(WidgetNode), typeof(WidgetPreview), propertyChanged: Redraw);
    /// <summary>The bindable property behind <see cref="Place"/>.</summary>
    public static readonly BindableProperty PlaceProperty = BindableProperty.Create(nameof(Place), typeof(WidgetPreviewPlace), typeof(WidgetPreview), WidgetPreviewPlace.LockScreen, propertyChanged: Redraw);
    /// <summary>The bindable property behind <see cref="Family"/>.</summary>
    public static readonly BindableProperty FamilyProperty = BindableProperty.Create(nameof(Family), typeof(WidgetFamily), typeof(WidgetPreview), WidgetFamily.Small, propertyChanged: Redraw);
    /// <summary>The bindable property behind <see cref="Platform"/>.</summary>
    public static readonly BindableProperty PlatformProperty = BindableProperty.Create(nameof(Platform), typeof(WidgetPlatform), typeof(WidgetPreview), WidgetPlatform.iOS, propertyChanged: Redraw);
    /// <summary>The bindable property behind <see cref="IsDark"/>.</summary>
    public static readonly BindableProperty IsDarkProperty = BindableProperty.Create(nameof(IsDark), typeof(bool), typeof(WidgetPreview), false, propertyChanged: Redraw);
    /// <summary>The bindable property behind <see cref="Notes"/>.</summary>
    public static readonly BindableProperty NotesProperty = BindableProperty.Create(nameof(Notes), typeof(IReadOnlyList<string>), typeof(WidgetPreview), Array.Empty<string>());

    /// <summary>The Live Activity to draw; its region for <see cref="Place"/> is drawn.</summary>
    public LiveActivityLayout? Activity { get => (LiveActivityLayout?)GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }

    /// <summary>A widget tree to draw at <see cref="Family"/>, when there is no <see cref="Activity"/>.</summary>
    public WidgetNode? Tree { get => (WidgetNode?)GetValue(TreeProperty); set => SetValue(TreeProperty, value); }

    /// <summary>Where it is drawn.</summary>
    public WidgetPreviewPlace Place { get => (WidgetPreviewPlace)GetValue(PlaceProperty); set => SetValue(PlaceProperty, value); }

    /// <summary>The widget's size, for <see cref="WidgetPreviewPlace.HomeScreen"/>.</summary>
    public WidgetFamily Family { get => (WidgetFamily)GetValue(FamilyProperty); set => SetValue(FamilyProperty, value); }

    /// <summary>The platform whose overrides apply.</summary>
    public WidgetPlatform Platform { get => (WidgetPlatform)GetValue(PlatformProperty); set => SetValue(PlatformProperty, value); }

    /// <summary>Dark mode, where the place follows it (widgets and Android's notification; the Lock Screen and the island are always dark).</summary>
    public bool IsDark { get => (bool)GetValue(IsDarkProperty); set => SetValue(IsDarkProperty, value); }

    /// <summary>What <c>Explain()</c> says about the tree in this place.</summary>
    public IReadOnlyList<string> Notes { get => (IReadOnlyList<string>)GetValue(NotesProperty); private set => SetValue(NotesProperty, value); }

    static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((WidgetPreview)bindable).Draw();

    bool _dark;

    void Draw()
    {
        _dark = Place is WidgetPreviewPlace.HomeScreen or WidgetPreviewPlace.Notification ? IsDark : true;
        var android = Platform == WidgetPlatform.Android || Place == WidgetPreviewPlace.Notification;
        if (Activity is { } activity)
        {
            var layout = JsonNode.Parse(WidgetJson.ResolveShared(activity.ToJson())) as JsonObject ?? [];
            JsonObject? Region(string name, string surface)
            {
                var place = new DrawPlace(android ? "android" : "ios", [surface], null, android ? 17 : 26);
                return layout[name] is JsonObject tree ? WidgetOverrides.Apply(tree, place) as JsonObject : null;
            }
            var regions = Place switch
            {
                WidgetPreviewPlace.IslandExpanded => ["expandedLeading", "expandedTrailing", "expandedCenter", "expandedBottom"],
                WidgetPreviewPlace.IslandCompact => ["compactLeading", "compactTrailing"],
                WidgetPreviewPlace.IslandMinimal => ["minimal"],
                WidgetPreviewPlace.CarPlay => ["small"],
                _ => new[] { "lockScreen" },
            };
            Notes = [.. activity.Explain(android ? WidgetPlatform.Android : WidgetPlatform.iOS).Where(line => regions.Any(r => line.StartsWith(r + ":", StringComparison.Ordinal)) || line.StartsWith("notification:", StringComparison.Ordinal) && Place == WidgetPreviewPlace.Notification)];
            Content = Place switch
            {
                WidgetPreviewPlace.IslandExpanded => Island(Region("expandedLeading", "island"), Region("expandedTrailing", "island"), Region("expandedCenter", "island"), Region("expandedBottom", "island")),
                WidgetPreviewPlace.IslandCompact => Compact(Region("compactLeading", "island"), Region("compactTrailing", "island")),
                WidgetPreviewPlace.IslandMinimal => Minimal(Region("minimal", "island")),
                WidgetPreviewPlace.CarPlay => Card(Region("small", "carPlay"), 300, null, Hex("#1C1C1E"), 14, 10),
                WidgetPreviewPlace.Notification => Notification(Region("lockScreen", "notification"), activity.Android),
                _ => Card(Region("lockScreen", "lockScreen"), 360, null, activity.Background is { } background ? Paint(background.Value) ?? Colors.Black : Hex("#E6000000"), 22, 0),
            };
            return;
        }
        if (Tree is { } tree)
        {
            var surface = Family is WidgetFamily.AccessoryCircular or WidgetFamily.AccessoryRectangular or WidgetFamily.AccessoryInline ? WidgetPlace.LockScreen : WidgetPlace.HomeScreen;
            if (surface == WidgetPlace.LockScreen)
                _dark = true;
            Notes = tree.Explain(Platform, surface, Family);
            var json = JsonSerializer.SerializeToNode(tree, WidgetJsonContext.Default.WidgetNode) as JsonObject;
            var size = WidgetJson.FamilyKey(Family);
            var drawn = json is null ? null : WidgetOverrides.Apply(json, new DrawPlace(android ? "android" : "ios", [surface == WidgetPlace.LockScreen ? "lockScreen" : "homeScreen"], size, android ? 17 : 26)) as JsonObject;
            var (width, height) = Size(Family);
            Content = surface == WidgetPlace.LockScreen
                ? Card(drawn, width, height, Hex("#40FFFFFF"), Family == WidgetFamily.AccessoryCircular ? width / 2 : 10, 4)
                : Card(drawn, width, height, _dark ? Hex("#1C1C1E") : Colors.White, android ? 24 : 22, android ? 12 : 16);
            return;
        }
        Notes = [];
        Content = null;
    }

    static (double Width, double Height) Size(WidgetFamily family) => family switch
    {
        WidgetFamily.Medium => (338, 158),
        WidgetFamily.Large => (338, 354),
        WidgetFamily.ExtraLarge => (715, 338),
        WidgetFamily.AccessoryCircular => (72, 72),
        WidgetFamily.AccessoryRectangular => (160, 72),
        WidgetFamily.AccessoryInline => (234, 26),
        _ => (158, 158),
    };

    View Card(JsonObject? tree, double width, double? height, Color background, double corner, double padding) => new Border
    {
        WidthRequest = width,
        HeightRequest = height ?? -1,
        HorizontalOptions = LayoutOptions.Start,
        Background = background,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = corner },
        Padding = padding,
        Content = tree is null ? null : Node(tree),
    };

    View Minimal(JsonObject? tree)
    {
        var view = tree is null ? null : Node(tree);
        if (view is not null) view.HorizontalOptions = view.VerticalOptions = LayoutOptions.Center;
        return new Border { WidthRequest = 37, HeightRequest = 37, HorizontalOptions = LayoutOptions.Start, Background = Colors.Black, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18.5 }, Padding = 4, Content = view };
    }

    View Island(JsonObject? leading, JsonObject? trailing, JsonObject? center, JsonObject? bottom)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(110), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
            RowSpacing = 4,
        };
        if (leading is not null) grid.Add(Node(leading), 0, 0);
        if (trailing is not null) { var view = Node(trailing); view.HorizontalOptions = LayoutOptions.End; grid.Add(view, 2, 0); }
        if (center is not null) { var view = Node(center); grid.Add(view, 0, 1); Grid.SetColumnSpan(view, 3); }
        if (bottom is not null) { var view = Node(bottom); grid.Add(view, 0, 2); Grid.SetColumnSpan(view, 3); }
        return new Border { WidthRequest = 370, HorizontalOptions = LayoutOptions.Start, Background = Colors.Black, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 44 }, Padding = new Thickness(14, 12), Content = grid };
    }

    View Compact(JsonObject? leading, JsonObject? trailing)
    {
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Star), new(120), new(GridLength.Star)], HeightRequest = 37 };
        if (leading is not null) { var view = Node(leading); view.HorizontalOptions = LayoutOptions.Start; view.VerticalOptions = LayoutOptions.Center; grid.Add(view, 0, 0); }
        if (trailing is not null) { var view = Node(trailing); view.HorizontalOptions = LayoutOptions.End; view.VerticalOptions = LayoutOptions.Center; grid.Add(view, 2, 0); }
        return new Border { WidthRequest = 250, HorizontalOptions = LayoutOptions.Start, Background = Colors.Black, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18.5 }, Padding = new Thickness(12, 0), Content = grid };
    }

    View Notification(JsonObject? tree, LiveUpdateText? text)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        var muted = _dark ? Hex("#B3FFFFFF") : Hex("#99000000");
        stack.Add(new Label { Text = $"{AppInfo.Current.Name} · now", FontSize = 12, TextColor = muted });
        if (text is { Title: not null } or { Body: not null })
        {
            if (text.Title is { } title) stack.Add(new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = _dark ? Colors.White : Colors.Black });
            if (text.Body is { } body) stack.Add(new Label { Text = body, FontSize = 14, TextColor = muted });
            if (text.Chip is { } chip)
                stack.Add(new Border
                {
                    HorizontalOptions = LayoutOptions.Start, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Background = Hex("#3DDC84"), Padding = new Thickness(8, 2),
                    Content = new Label { Text = chip, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black },
                });
        }
        else
            stack.Add(new Label { Text = "Title and text derived from the tree's parts on the device.", FontSize = 12, FontAttributes = FontAttributes.Italic, TextColor = muted });
        if (tree is not null)
        {
            stack.Add(new BoxView { HeightRequest = 0.5, Color = muted, Margin = new Thickness(0, 4) });
            stack.Add(Node(tree));
        }
        return new Border { WidthRequest = 360, HorizontalOptions = LayoutOptions.Start, Background = _dark ? Hex("#2B2B2B") : Hex("#F3F3F3"), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 24 }, Padding = 16, Content = stack };
    }

    // ---- Nodes ----

    View Node(JsonObject node)
    {
        var view = Shape(node);
        if (node["margin"] is JsonArray margin)
            view.Margin = Insets(margin);
        if (Text(node, "hAlign") is { } placed)
            view.HorizontalOptions = placed switch { "center" => LayoutOptions.Center, "end" => LayoutOptions.End, "fill" => LayoutOptions.Fill, _ => LayoutOptions.Start };
        if (Text(node, "description") is { } description)
            SemanticProperties.SetDescription(view, description);
        return view;
    }

    View Shape(JsonObject node)
    {
        switch (Text(node, "type"))
        {
            case "vstack": return Box(node, Stack(node, vertical: true));
            case "hstack": return Box(node, Stack(node, vertical: false));
            case "zstack": return Box(node, Stack(node, vertical: null));
            case "text": return Label(node, Text(node, "text") ?? "");
            case "relative": return Label(node, (Text(node, "prefix") ?? "") + Ago(node));
            case "timer": return Label(node, (Text(node, "prefix") ?? "") + Clock(Math.Max(0, (Date(node, "until") - DateTimeOffset.UtcNow).TotalSeconds)));
            case "image": return Icon(node);
            case "asset" when Services?.GetService<IWidgetPlatform>()?.AssetPath(Text(node, "asset") ?? "") is { } file:
                return new Image { Source = ImageSource.FromFile(file), HeightRequest = Number(node, "height") ?? 40, Aspect = Aspect.AspectFit };
            case "asset":
                return new Border
                {
                    HeightRequest = Number(node, "height") ?? 40, WidthRequest = Number(node, "height") ?? 40, StrokeThickness = 0, Background = Hex("#33888888"),
                    StrokeShape = new RoundRectangle { CornerRadius = 6 },
                    Content = new Label { Text = Text(node, "asset"), FontSize = 8, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, TextColor = Ink() },
                };
            case "progress":
                return new ProgressBar { Progress = Math.Clamp(Number(node, "value") ?? 0, 0, 1), ProgressColor = Paint(Text(node, "color")) ?? Accent() };
            case "ring":
            {
                var size = Number(node, "height") ?? 56;
                return new GraphicsView { WidthRequest = size, HeightRequest = size, Drawable = new RingDrawing(Number(node, "value"), Number(node, "thickness"), Paint(Text(node, "color")) ?? Accent()) };
            }
            case "sparkline":
            {
                var height = Number(node, "height") ?? 48;
                var view = new GraphicsView { HeightRequest = height, HorizontalOptions = LayoutOptions.Fill, Drawable = new SparklineDrawing(node, this) };
                if (Number(node, "maxAspect") is { } maxAspect) view.MaximumWidthRequest = height * maxAspect;
                return view;
            }
            case "spacer": return new ContentView { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
            case "divider": return new BoxView { HeightRequest = 0.5, Color = Faint(), HorizontalOptions = LayoutOptions.Fill };
            case "button":
                return new Border
                {
                    StrokeThickness = 1, Stroke = Accent(), StrokeShape = new RoundRectangle { CornerRadius = 8 }, Padding = new Thickness(6, 3), HorizontalOptions = LayoutOptions.Start,
                    Content = node["child"] is JsonObject child ? Node(child) : null,
                };
            case "adaptive":
            {
                var tree = node["trees"]?[WidgetJson.FamilyKey(Family)] as JsonObject ?? node["fallback"] as JsonObject;
                return tree is null ? new ContentView() : Node(tree);
            }
            default: return new ContentView();
        }
    }

    Layout Stack(JsonObject node, bool? vertical)
    {
        var spacing = Number(node, "spacing") ?? 4;
        var align = Text(node, "align");
        var children = (node["children"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var grid = new Grid { RowSpacing = vertical == true ? spacing : 0, ColumnSpacing = vertical == false ? spacing : 0 };
        if (node["insets"] is JsonArray insets) grid.Padding = Insets(insets);
        else if (Number(node, "padding") is { } padding) grid.Padding = padding;

        if (vertical is null)
        {
            foreach (var child in children)
            {
                var view = Node(child);
                view.HorizontalOptions = align is "topStart" or "start" or "bottomStart" ? LayoutOptions.Start : align is "topEnd" or "end" or "bottomEnd" ? LayoutOptions.End : LayoutOptions.Center;
                view.VerticalOptions = align is "topStart" or "top" or "topEnd" ? LayoutOptions.Start : align is "bottomStart" or "bottom" or "bottomEnd" ? LayoutOptions.End : LayoutOptions.Center;
                grid.Add(view);
            }
            return grid;
        }

        var evenly = vertical == false && Text(node, "distribute") == "evenly";
        var index = 0;
        void Gap()
        {
            if (vertical == true) grid.RowDefinitions.Add(new(GridLength.Star)); else grid.ColumnDefinitions.Add(new(GridLength.Star));
            index++;
        }
        if (evenly) Gap();
        foreach (var child in children)
        {
            var type = Text(child, "type");
            var grows = type == "spacer" || Bool(child, "fill") || vertical == false && type != "text" && Widens(child);
            var length = grows ? GridLength.Star : GridLength.Auto;
            var view = Node(child);
            if (vertical == true)
            {
                grid.RowDefinitions.Add(new(length));
                if (Text(child, "hAlign") is null && type is not ("spacer" or "divider" or "sparkline" or "progress"))
                    view.HorizontalOptions = Widens(child) || Bool(child, "centered") || Bool(child, "trailing") ? LayoutOptions.Fill
                        : align == "center" ? LayoutOptions.Center : align == "end" ? LayoutOptions.End : LayoutOptions.Start;
                grid.Add(view, 0, index);
            }
            else
            {
                grid.ColumnDefinitions.Add(new(length));
                view.VerticalOptions = align == "top" ? LayoutOptions.Start : align == "bottom" ? LayoutOptions.End : LayoutOptions.Center;
                grid.Add(view, index, 0);
            }
            index++;
            if (evenly) Gap();
        }
        if (vertical == true) grid.ColumnDefinitions.Add(new(GridLength.Star));
        if (vertical == false && !children.Any(Widens)) grid.HorizontalOptions = LayoutOptions.Start;
        return grid;
    }

    /// <summary>Whether SwiftUI would give the node all the width it is offered: a spacer, a graph, or a stack holding one.</summary>
    static bool Widens(JsonObject node) => Text(node, "type") switch
    {
        "spacer" or "sparkline" or "progress" or "divider" => true,
        "vstack" or "hstack" or "zstack" => Bool(node, "fill") || Text(node, "background") is not null
            || (node["children"] as JsonArray ?? []).OfType<JsonObject>().Any(Widens),
        "text" => Bool(node, "centered") || Bool(node, "trailing"),
        "relative" or "timer" => true,
        _ => false,
    };

    View Box(JsonObject node, Layout content)
    {
        if (Text(node, "background") is null && Number(node, "cornerRadius") is null)
        {
            if (Bool(node, "fill")) content.HorizontalOptions = LayoutOptions.Fill;
            return content;
        }
        return new Border
        {
            Background = Paint(Text(node, "background")),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = Number(node, "cornerRadius") ?? 0 },
            Content = content,
            HorizontalOptions = Bool(node, "fill") ? LayoutOptions.Fill : LayoutOptions.Start,
        };
    }

    Label Label(JsonObject node, string text)
    {
        // Enums are written by name ("Caption"), as the Swift renderer reads them, case-blind.
        var role = Text(node, "font")?.ToLowerInvariant();
        var size = Number(node, "size") ?? role switch
        {
            "caption" => 12,
            "headline" => 17,
            "title" => 22,
            "largetitle" => 34,
            _ => 17,
        };
        var bold = Bool(node, "bold") || role == "headline";
        var label = new Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = (bold ? FontAttributes.Bold : FontAttributes.None) | (Bool(node, "italic") ? FontAttributes.Italic : FontAttributes.None),
            TextColor = Paint(Text(node, "color")) ?? Ink(),
            HorizontalTextAlignment = Bool(node, "centered") ? TextAlignment.Center : Bool(node, "trailing") ? TextAlignment.End : TextAlignment.Start,
        };
        if (Number(node, "singleLine") is not null || Number(node, "maxLines") is not null)
        {
            label.MaxLines = (int)(Number(node, "maxLines") ?? 1);
            label.LineBreakMode = LineBreakMode.TailTruncation;
        }
        return label;
    }

    View Icon(JsonObject node)
    {
        var size = Number(node, "height") ?? (Platform == WidgetPlatform.Android ? 20 : 18);
        var color = Paint(Text(node, "color")) ?? Ink();
        var png = Services?.GetService<WidgetIconAssets>()?.Picture(Text(node, "systemImage") ?? "", Sk(color));
        return png is null
            ? new Label { Text = "?", FontSize = size * 0.8, TextColor = color, WidthRequest = size, HeightRequest = size, HorizontalTextAlignment = TextAlignment.Center }
            : new Image { Source = ImageSource.FromStream(() => new MemoryStream(png)), WidthRequest = size, HeightRequest = size };
    }

    IServiceProvider? Services => Handler?.MauiContext?.Services ?? Application.Current?.Handler?.MauiContext?.Services;

    static SKColor Sk(Color color) => new((byte)(color.Red * 255), (byte)(color.Green * 255), (byte)(color.Blue * 255), (byte)(color.Alpha * 255));

    static string Ago(JsonObject node)
    {
        var seconds = Math.Max(0, (DateTimeOffset.UtcNow - Date(node, "date")).TotalSeconds);
        if (Bool(node, "ago"))
            return seconds < 60 ? "now" : seconds < 3600 ? $"{(int)(seconds / 60)} min ago" : $"{(int)(seconds / 3600)} hr ago";
        if (Bool(node, "compact"))
            return Clock(seconds);
        if (Bool(node, "rounded"))
            return seconds < 3600 ? $"{(int)(seconds / 60)} minutes" : $"{(int)(seconds / 3600)} hours, {(int)(seconds % 3600 / 60)} minutes";
        return $"{(int)(seconds / 60)} min, {(int)(seconds % 60)} secs";
    }

    static string Clock(double seconds) =>
        seconds >= 3600 ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    // ---- Colors and values ----

    Color Ink() => _dark ? Colors.White : Colors.Black;

    Color Accent() => Hex("#0A84FF");

    Color Faint() => _dark ? Hex("#40FFFFFF") : Hex("#33000000");

    internal Color? Paint(string? value)
    {
        if (value is null) return null;
        if (value.IndexOf('/') is var slash and > 0) value = _dark ? value[(slash + 1)..] : value[..slash];
        return value switch
        {
            "primary" => Ink(),
            "secondary" => _dark ? Hex("#99EBEBF5") : Hex("#993C3C43"),
            "accent" => Accent(),
            "surface" => _dark ? Hex("#1C1C1E") : Colors.White,
            "onAccent" => Colors.White,
            "green" => Hex("#30D158"),
            "red" => Hex("#FF453A"),
            "orange" => Hex("#FF9F0A"),
            "yellow" => Hex("#FFD60A"),
            "blue" => Hex("#0A84FF"),
            _ => Hex(value),
        };
    }

    static Color Hex(string hex) => Color.FromArgb(hex);

    static Thickness Insets(JsonArray values)
    {
        double At(int i) => values.Count > i && values[i] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
        return new Thickness(At(0), At(1), At(2), At(3));
    }

    internal static string? Text(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static double? Number(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    static bool Bool(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    static DateTimeOffset Date(JsonObject node, string key) =>
        Text(node, key) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTimeOffset.UtcNow;

    sealed class RingDrawing(double? value, double? thickness, Color color) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var width = (float)(thickness ?? rect.Width / 8);
            var bounds = rect.Inflate(-width / 2, -width / 2);
            canvas.StrokeSize = width;
            canvas.StrokeColor = color.WithAlpha(0.25f);
            canvas.DrawEllipse(bounds);
            if (value is not { } share) return;
            canvas.StrokeColor = color;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawArc(bounds, 90, 90 - (float)(360 * Math.Clamp(share, 0, 1)), true, false);
        }
    }

    sealed class SparklineDrawing(JsonObject node, WidgetPreview preview) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var values = (node["values"] as JsonArray ?? []).Select(v => v is JsonValue j && j.TryGetValue<double>(out var d) ? d : (double?)null).ToArray();
            var known = values.OfType<double>().ToArray();
            if (known.Length < 2) return;
            var low = Number(node, "low");
            var high = Number(node, "high");
            var min = Math.Min(Number(node, "min") ?? known.Min(), known.Min());
            var max = Math.Max(Number(node, "max") ?? known.Max(), known.Max());
            if (max <= min) max = min + 1;
            var color = preview.Paint(Text(node, "color")) ?? preview.Accent();
            var above = preview.Paint(Text(node, "aboveColor")) ?? color;
            var below = preview.Paint(Text(node, "belowColor")) ?? color;
            float X(int i) => rect.Left + rect.Width * i / Math.Max(1, values.Length - 1);
            float Y(double v) => rect.Bottom - 3 - (float)((v - min) / (max - min)) * (rect.Height - 6);
            Color Of(double v) => high is { } h && v > h ? above : low is { } l && v < l ? below : color;

            if (node["shade"] is JsonValue && low is { } bandLow && high is { } bandHigh)
            {
                canvas.FillColor = color.WithAlpha(0.12f);
                canvas.FillRectangle(rect.Left, Y(bandHigh), rect.Width, Y(bandLow) - Y(bandHigh));
            }
            canvas.StrokeSize = 2;
            canvas.StrokeLineCap = LineCap.Round;
            int? last = null;
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is not { } v) continue;
                if (last is { } j && values[j] is { } previous)
                {
                    if (node["area"] is JsonValue)
                    {
                        var area = new PathF();
                        area.MoveTo(X(j), Y(previous));
                        area.LineTo(X(i), Y(v));
                        area.LineTo(X(i), rect.Bottom);
                        area.LineTo(X(j), rect.Bottom);
                        area.Close();
                        canvas.FillColor = Of(v).WithAlpha(0.18f);
                        canvas.FillPath(area);
                    }
                    canvas.StrokeColor = Of(v);
                    canvas.DrawLine(X(j), Y(previous), X(i), Y(v));
                }
                last = i;
            }
            if (node["dot"] is JsonValue && last is { } end && values[end] is { } final)
            {
                canvas.FillColor = Of(final);
                canvas.FillCircle(X(end), Y(final), 3.5f);
            }
        }
    }
}
