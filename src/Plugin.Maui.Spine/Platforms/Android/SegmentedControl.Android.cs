using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.Shape;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Color = Microsoft.Maui.Graphics.Color;
using Rect = Microsoft.Maui.Graphics.Rect;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Material 3 segmented buttons: a <see cref="MaterialButtonToggleGroup"/> with one outlined
/// <see cref="MaterialButton"/> per segment, single selection, and a check mark on the picked one
/// when it has no icon of its own. Styled in code, since the app's theme is Material Components.
/// </summary>
internal sealed class SegmentedControlHandler : ViewHandler<SegmentedControl, MaterialButtonToggleGroup>
{
    private const double Height = 40;
    private const double IconSize = 18;
    private const string CheckSvg = "check.svg";

    private static readonly Color OutlineLight = Color.FromArgb("#79747E");
    private static readonly Color OutlineDark = Color.FromArgb("#938F99");

    public static readonly IPropertyMapper<SegmentedControl, SegmentedControlHandler> Mapper = new PropertyMapper<SegmentedControl, SegmentedControlHandler>(ViewMapper)
    {
        [nameof(SegmentedControl.Segments)] = static (handler, _) => handler.SetSegments(),
        [nameof(SegmentedControl.SelectedIndex)] = static (handler, _) => handler.SetSelection(),
        [nameof(SegmentedControl.SelectedSegmentColor)] = static (handler, _) => handler.SetColors(),
        [nameof(IView.IsEnabled)] = static (handler, _) => handler.SetEnabled(),
    };

    private readonly List<MaterialButton> _buttons = [];
    private readonly List<Drawable?> _icons = [];
    private readonly CheckedListener _listener;
    private Drawable? _check;
    private bool _updating;

    public SegmentedControlHandler() : base(Mapper) => _listener = new CheckedListener(this);

    protected override MaterialButtonToggleGroup CreatePlatformView() => new(Context)
    {
        SingleSelection = true,
        SelectionRequired = true,
        MeasureWithLargestChildEnabled = true,
    };

    /// <remarks>
    /// Segments are as wide as the widest, as on Apple: measured at its content, the group takes
    /// the widest segment times their number; laid out wider, as when it fills, it shares the width
    /// out equally, but only when it is measured at that width, which MAUI does not do by itself.
    /// </remarks>
    public override void PlatformArrange(Rect frame)
    {
        var width = (int)Context.ToPixels(frame.Width);
        var height = (int)Context.ToPixels(frame.Height);
        if (PlatformView.MeasuredWidth != width || PlatformView.MeasuredHeight != height)
            PlatformView.Measure(MeasureSpecMode.Exactly.MakeMeasureSpec(width), MeasureSpecMode.Exactly.MakeMeasureSpec(height));

        base.PlatformArrange(frame);
    }

    protected override void ConnectHandler(MaterialButtonToggleGroup platformView)
    {
        base.ConnectHandler(platformView);
        platformView.AddOnButtonCheckedListener(_listener);
    }

    protected override void DisconnectHandler(MaterialButtonToggleGroup platformView)
    {
        platformView.RemoveOnButtonCheckedListener(_listener);
        base.DisconnectHandler(platformView);
    }

    private void SetSegments()
    {
        _updating = true;
        try
        {
            PlatformView.RemoveAllViews();
            _buttons.Clear();
            _icons.Clear();

            foreach (var segment in VirtualView.Segments)
            {
                var button = CreateButton(segment);
                _buttons.Add(button);
                _icons.Add(Icon(segment.Svg));
                PlatformView.AddView(button, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WrapContent, 1));
            }
        }
        finally
        {
            _updating = false;
        }

        SetEnabled();
        SetColors();
        SetSelection();
    }

    /// <remarks>The group passes its own state on to every button, so a disabled segment is set after it.</remarks>
    private void SetEnabled()
    {
        PlatformView.Enabled = VirtualView.IsEnabled;

        for (var i = 0; i < _buttons.Count && i < VirtualView.Segments.Count; i++)
            _buttons[i].Enabled = VirtualView.IsEnabled && VirtualView.Segments[i].IsEnabled;
    }

    private MaterialButton CreateButton(Segment segment)
    {
        var button = new MaterialButton(Context)
        {
            Id = AView.GenerateViewId(),
            Checkable = true,
            StateListAnimator = null,
            Elevation = 0,
            InsetTop = 0,
            InsetBottom = 0,
            CornerRadius = 0,
            StrokeWidth = (int)Context.ToPixels(1),
            IconSize = (int)Context.ToPixels(IconSize),
            IconGravity = MaterialButton.IconGravityTextStart,
            ShapeAppearanceModel = ShapeAppearanceModel.InvokeBuilder().SetAllCorners(CornerFamily.Rounded, Context.ToPixels(Height / 2)).Build(),
        };

        button.SetAllCaps(false);
        button.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        button.SetTypeface(Typeface.Create("sans-serif-medium", TypefaceStyle.Normal), TypefaceStyle.Normal);
        button.SetMinHeight((int)Context.ToPixels(Height));
        button.SetMinimumHeight((int)Context.ToPixels(Height));
        button.SetMinWidth(0);
        button.SetMinimumWidth(0);
        var padding = (int)Context.ToPixels(12);
        button.SetPaddingRelative(padding, 0, padding, 0);
        button.SetSingleLine(true);
        button.Ellipsize = Android.Text.TextUtils.TruncateAt.End;

        button.Text = segment.Title;
        button.ContentDescription = string.IsNullOrEmpty(segment.Title) ? null : segment.Title;
        return button;
    }

    private void SetSelection()
    {
        var index = VirtualView.SelectedIndex;

        _updating = true;
        try
        {
            if (index >= 0 && index < _buttons.Count)
                PlatformView.Check(_buttons[index].Id);
            else
                PlatformView.ClearChecked();
        }
        finally
        {
            _updating = false;
        }

        SetIcons();
    }

    /// <summary>The picked segment shows a check mark in place of a missing icon.</summary>
    private void SetIcons()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var button = _buttons[i];
            var icon = _icons[i] ?? (button.Checked ? _check ??= Icon(CheckSvg) : null);
            button.Icon = icon;
            button.IconPadding = icon is null || string.IsNullOrEmpty(button.Text) ? 0 : (int)Context.ToPixels(8);
        }

        VirtualView?.InvalidateMeasure();
    }

    private void SetColors()
    {
        var theme = SegmentedControl.CurrentTheme;
        var dark = theme == AppTheme.Dark;
        var onSurface = dark ? Colors.White : Colors.Black;

        // Unset, the tonal container Material 3 gives a picked segment: from the accent, or
        // Material's baseline secondary container when the app has none.
        var (fill, onFill) = VirtualView.SelectedSegmentColor is { } color
            ? (color, SpineAccent.TextOn(color))
            : (SpineTheme.GetAccent(theme)?.WithAlpha(dark ? 0.32f : 0.2f) ?? Color.FromArgb(dark ? "#4A4458" : "#E8DEF8"), onSurface);
        var disabled = onSurface.WithAlpha(0.38f);

        var background = States(fill, Colors.Transparent, Colors.Transparent);
        var foreground = States(onFill, onSurface, disabled);
        var outline = ColorStateList.ValueOf((dark ? OutlineDark : OutlineLight).ToPlatform());
        var ripple = ColorStateList.ValueOf(onSurface.WithAlpha(0.12f).ToPlatform());

        foreach (var button in _buttons)
        {
            button.BackgroundTintList = background;
            button.SetTextColor(foreground);
            button.IconTint = foreground;
            button.StrokeColor = outline;
            button.RippleColor = ripple;
        }
    }

    private static ColorStateList States(Color picked, Color normal, Color disabled) => new(
        [[-Android.Resource.Attribute.StateEnabled], [Android.Resource.Attribute.StateChecked], []],
        [disabled.ToPlatform(), picked.ToPlatform(), normal.ToPlatform()]);

    private Drawable? Icon(string? svg)
    {
        if (MenuButton.Icon(this, svg, IconSize, Colors.Black) is not { } png)
            return null;

        var bitmap = BitmapFactory.DecodeByteArray(png, 0, png.Length);
        return bitmap is null ? null : new BitmapDrawable(Context.Resources, bitmap);
    }

    private void OnChecked(int id, bool isChecked)
    {
        // A tap checks the new button before the group unchecks the old one, so both events count.
        SetIcons();

        if (!isChecked || _updating || VirtualView is null)
            return;

        var index = _buttons.FindIndex(b => b.Id == id);
        if (index >= 0)
            VirtualView.OnUserSelected(index);
    }

    private sealed class CheckedListener(SegmentedControlHandler handler) : Java.Lang.Object, MaterialButtonToggleGroup.IOnButtonCheckedListener
    {
        public void OnButtonChecked(MaterialButtonToggleGroup? group, int checkedId, bool isChecked) => handler.OnChecked(checkedId, isChecked);
    }
}
