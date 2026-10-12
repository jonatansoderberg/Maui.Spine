using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Fonts, sizes and colours of a <see cref="ChatView"/> and its <see cref="ChatComposer"/>. Set on one control through
/// <c>StyleOptions</c>, or app-wide as a resource keyed <c>DefaultChatStyleOptions</c>; see
/// <see cref="SpineStyleOptions{TSelf}"/> for the chain. The markdown of answers takes <c>DefaultMarkdownStyleOptions</c>.
/// </summary>
/// <remarks>A colour left <see langword="null"/> follows the theme; the user's bubble and the send button follow the accent.</remarks>
public class ChatStyleOptions : SpineStyleOptions<ChatStyleOptions>
{
    static readonly bool IsAndroid = DeviceInfo.Platform == DevicePlatform.Android;
    static readonly bool IsWindows = DeviceInfo.Platform == DevicePlatform.WinUI;

    /// <summary>Font of the user's messages and the composer. <see langword="null"/> = the app's default font.</summary>
    public string? FontFamily { get; set; }

    public double FontSize { get; set; } = IsAndroid ? 16 : IsWindows ? 14 : 17;

    /// <summary>Space between messages.</summary>
    public double MessageSpacing { get; set; } = 18;

    /// <summary>Space round the conversation.</summary>
    public Thickness Padding { get; set; } = new(16, 12);

    public double BubbleCornerRadius { get; set; } = 18;

    /// <summary>The widest a user's bubble gets, as a share of the view's width.</summary>
    public double BubbleMaxWidth { get; set; } = 0.8;

    /// <summary>
    /// Height of the composer's field on one line, and the size of its round send button: the platform's standard
    /// control size (44 points on Apple, the 48 dp touch target on Android, 40 on Windows).
    /// </summary>
    public double ControlHeight { get; set; } = IsAndroid ? 48 : IsWindows ? 40 : 44;

    /// <summary>How many lines the composer grows to before it scrolls.</summary>
    public int ComposerMaxLines { get; set; } = 5;

    /// <summary>The user's bubble. <see langword="null"/> = the accent.</summary>
    public Color? UserBubbleColor { get; set; }

    public Color? UserTextColor { get; set; }

    /// <summary>Secondary text: tool cards, "Stopped", the thinking label.</summary>
    public Color? SecondaryTextColor { get; set; }

    public Color? ToolCardBackground { get; set; }
    public Color? ErrorColor { get; set; }
    public Color? ComposerFieldBackground { get; set; }
    public Color? ComposerTextColor { get; set; }

    /// <summary>The send and stop button. <see langword="null"/> = the accent.</summary>
    public Color? SendButtonColor { get; set; }

    public Color? ChipBackground { get; set; }
    public Color? ChipTextColor { get; set; }

    /// <summary>The round "jump to latest" button.</summary>
    public Color? JumpButtonBackground { get; set; }

    protected override void InheritColorsFrom(ChatStyleOptions source)
    {
        UserBubbleColor ??= source.UserBubbleColor;
        UserTextColor ??= source.UserTextColor;
        SecondaryTextColor ??= source.SecondaryTextColor;
        ToolCardBackground ??= source.ToolCardBackground;
        ErrorColor ??= source.ErrorColor;
        ComposerFieldBackground ??= source.ComposerFieldBackground;
        ComposerTextColor ??= source.ComposerTextColor;
        SendButtonColor ??= source.SendButtonColor;
        ChipBackground ??= source.ChipBackground;
        ChipTextColor ??= source.ChipTextColor;
        JumpButtonBackground ??= source.JumpButtonBackground;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;
        var accent = SpineTheme.GetAccent(theme) ?? Color.FromArgb(dark ? "#0A84FF" : "#007AFF");

        UserBubbleColor ??= accent;
        UserTextColor ??= Colors.White;
        SecondaryTextColor ??= Color.FromArgb(dark ? "#98989F" : "#6C6C70");
        ToolCardBackground ??= Color.FromArgb(dark ? "#2C2C2E" : "#F2F2F7");
        ErrorColor ??= Color.FromArgb(dark ? "#FF6B6B" : "#C62828");
        ComposerFieldBackground ??= Color.FromArgb(dark ? "#2C2C2E" : "#F2F2F7");
        ComposerTextColor ??= dark ? Colors.White : Colors.Black;
        SendButtonColor ??= accent;
        ChipBackground ??= Color.FromArgb(dark ? "#2C2C2E" : "#F2F2F7");
        ChipTextColor ??= dark ? Colors.White : Colors.Black;
        JumpButtonBackground ??= Color.FromArgb(dark ? "#3A3A3C" : "#FFFFFF");
    }
}
