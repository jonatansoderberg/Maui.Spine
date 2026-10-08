using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Service.QuickSettings;
using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// The Quick Settings tile behind a control kind. Android names a tile by its service class, which has to
/// exist at compile time, so the package carries nine and the build's manifest overlay wires the first N
/// to the <c>&lt;SpineControl&gt;</c> items in declaration order. Not an active tile: Android binds it each
/// time the panel shows it, so it draws the stored state at once and asks the provider for a fresh one.
/// Runs in the app's process, so a click runs the provider's handler directly.
/// </summary>
[SupportedOSPlatform("android24.0")]
internal abstract class SpineControlTile(int _index) : TileService
{
    private const string Tag = "SpineWidgets";

    private static readonly Type[] Slots =
    [
        typeof(SpineControlTile0), typeof(SpineControlTile1), typeof(SpineControlTile2),
        typeof(SpineControlTile3), typeof(SpineControlTile4), typeof(SpineControlTile5),
        typeof(SpineControlTile6), typeof(SpineControlTile7), typeof(SpineControlTile8),
    ];

    // The tiles bound right now, by slot, so a refresh from the app can redraw an open panel.
    private static readonly SpineControlTile?[] Listening = new SpineControlTile?[Slots.Length];

    private string? Kind => WidgetStore.ControlKinds(this).ElementAtOrDefault(_index);

    private bool IsToggle => WidgetStore.ControlTypes(this).ElementAtOrDefault(_index) == "toggle";

    public override void OnStartListening()
    {
        base.OnStartListening();
        Listening[_index] = this;
        Draw();

        if (Kind is { } kind && IPlatformApplication.Current?.Services is { } services)
            services.GetRequiredService<IControlService>().RefreshAsync(kind)
                .SafeFireAndForget(e => Android.Util.Log.Warn(Tag, $"Refreshing control \"{kind}\" failed: {e.Message}"));
    }

    public override void OnStopListening()
    {
        if (Listening[_index] == this) Listening[_index] = null;
        base.OnStopListening();
    }

    public override void OnClick()
    {
        base.OnClick();
        if (Kind is not { } kind || IPlatformApplication.Current?.Services is not { } services) return;

        // A toggle flips at once, as on iOS; the handler's rebuild then says what really happened.
        bool? isOn = IsToggle ? !(Read(this, kind)?.IsOn ?? false) : null;
        if (isOn is { } value && QsTile is { } tile)
        {
            tile.State = value ? TileState.Active : TileState.Inactive;
            tile.UpdateTile();
        }

        // Now, and not something read off an intent: the click arrives the moment the tile is tapped.
        var at = DateTimeOffset.Now;
        Extensions.SpineWidgetsExtensions.HandleControlActionAsync(services, kind, isOn, at)
            .SafeFireAndForget(e => Android.Util.Log.Warn(Tag, $"The tap on control \"{kind}\" failed: {e.Message}"));
    }

    /// <summary>Redraws the tile from the stored document: label, subtitle, icon and state.</summary>
    private void Draw()
    {
        if (QsTile is not { } tile || Kind is not { } kind) return;
        var state = Read(this, kind);

        tile.Label = state?.Title ?? Label(this, _index) ?? kind;
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) tile.Subtitle = state?.Status;
        // A button is never "on"; inactive is the neutral look Android's own action tiles have.
        tile.State = state?.IsOn == true ? TileState.Active : TileState.Inactive;
        if (TileIcon(this, state?.Icon) is { } icon) tile.Icon = icon;
        tile.UpdateTile();
    }

    /// <summary>Redraws <paramref name="kind"/> if the panel shows it; otherwise it is drawn the next time it does.</summary>
    internal static void Update(Context context, string kind)
    {
        var index = Array.IndexOf(WidgetStore.ControlKinds(context), kind);
        if (index < 0 || index >= Slots.Length || Listening[index] is not { } tile) return;
        MainThread.BeginInvokeOnMainThread(tile.Draw);
    }

    /// <summary>Android 13's prompt to add the tile to Quick Settings; true when added or already there.</summary>
    [SupportedOSPlatform("android33.0")]
    internal static Task<bool> RequestAddAsync(Context context, string kind, ControlState state, ILogger logger)
    {
        var index = Array.IndexOf(WidgetStore.ControlKinds(context), kind);
        if (index < 0 || index >= Slots.Length) return Task.FromResult(false);

        var manager = (StatusBarManager)context.GetSystemService(Context.StatusBarService)!;
        var result = new TaskCompletionSource<bool>();
        var icon = TileIcon(context, state.Icon) ?? Icon.CreateWithResource(context, Resource.Drawable.spine_control_icon)!;
        manager.RequestAddTileService(Component(context, index), state.Title, icon, context.MainExecutor!, new AddCallback(code =>
        {
            logger.LogDebug("Adding the tile of control \"{Kind}\" answered {Code}.", kind, code);
            result.TrySetResult((TileAddRequestResult)code is TileAddRequestResult.TileAdded or TileAddRequestResult.TileAlreadyAdded);
        }));
        return result.Task;
    }

    private static ComponentName Component(Context context, int index) =>
        new(context, Java.Lang.Class.FromType(Slots[index]));

    private static ControlState? Read(Context context, string kind)
    {
        var path = WidgetStore.ControlPath(context, kind);
        if (!File.Exists(path)) return null;
        try { return WidgetJson.DeserializeControl(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Android.Util.Log.Warn(Tag, $"Control \"{kind}\" failed to load: {e.Message}");
            return null;
        }
    }

    private static string? Label(Context context, int index)
    {
        var id = context.Resources!.GetIdentifier($"spine_control_{index}_label", "string", context.PackageName);
        return id == 0 ? null : context.GetString(id);
    }

    // The SVG the app rasterized as a white mask, which is what a tile icon is; null keeps the manifest's.
    private static Icon? TileIcon(Context context, string? name)
    {
        var pixels = (int)(24 * (context.Resources?.DisplayMetrics?.Density ?? 3));
        return new WidgetIcons(context).Bitmap(name, pixels) is { } bitmap ? Icon.CreateWithBitmap(bitmap) : null;
    }

    private sealed class AddCallback(Action<int> _answered) : Java.Lang.Object, Java.Util.Functions.IConsumer
    {
        public void Accept(Java.Lang.Object? t) => _answered(t is Java.Lang.Integer code ? code.IntValue() : -1);
    }
}

[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile0")] internal sealed class SpineControlTile0() : SpineControlTile(0);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile1")] internal sealed class SpineControlTile1() : SpineControlTile(1);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile2")] internal sealed class SpineControlTile2() : SpineControlTile(2);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile3")] internal sealed class SpineControlTile3() : SpineControlTile(3);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile4")] internal sealed class SpineControlTile4() : SpineControlTile(4);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile5")] internal sealed class SpineControlTile5() : SpineControlTile(5);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile6")] internal sealed class SpineControlTile6() : SpineControlTile(6);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile7")] internal sealed class SpineControlTile7() : SpineControlTile(7);
[SupportedOSPlatform("android24.0"), Register("plugin/maui/spine/widgets/SpineControlTile8")] internal sealed class SpineControlTile8() : SpineControlTile(8);
