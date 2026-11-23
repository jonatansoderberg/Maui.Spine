using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// The widget board's provider, served from the app's own process: the board creates it through COM from the class
/// the manifest declares, starting the app (with <see cref="ComServerArgument"/>, its window hidden) when it is not
/// running. One instance answers every widget; what it draws is the timeline document the app wrote, so a widget the
/// board shows after a restart of either is drawn without running the provider.
/// </summary>
internal sealed partial class WidgetBoardProvider : IWidgetProvider
{
    /// <summary>The argument COM starts the app with to serve the class; <c>ExeServer Arguments</c> in the manifest.</summary>
    public const string ComServerArgument = "-RegisterProcessAsComServer";

    /// <summary>The verb of a <see cref="Common.W.Button"/>, followed by its action id.</summary>
    public const string ButtonVerb = "spine.button:";

    /// <summary>
    /// The verb of a tap on the widget, followed by its link. The provider opens the app itself: a card that opened the
    /// app's URL scheme would have Windows ask "Did you mean to switch apps?" at every tap.
    /// </summary>
    public const string OpenVerb = "spine.open:";

    /// <summary>The app's window shown in front, and the link handed to the provider's <see cref="Common.IWidgetLinkHandler"/>.</summary>
    private static void Open(IServiceProvider services, string link)
    {
        foreach (var window in Application.Current?.Windows ?? [])
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
            {
                native.AppWindow.Show(true);
                native.Activate();
                // The board keeps the foreground through the call, and Windows lets a process take it only after input
                // of its own; an Alt press and release counts, and closes nothing.
                keybd_event(VkMenu, 0, 0, 0);
                keybd_event(VkMenu, 0, KeyEventFKeyUp, 0);
                SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(native));
            }
        if (Uri.TryCreate(link, UriKind.Absolute, out var url))
            Extensions.SpineWidgetsExtensions.TryHandleLink(services, url);
    }

    public static WidgetBoardProvider Instance { get; } = new();

    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);
    private Timer? _idle;

    /// <summary>Set once the app's services exist; until then a call from the board only records the widget.</summary>
    public NativeWidgetPlatform? Platform { get; set; }

    public bool IsActive(string widgetId) => _active.ContainsKey(widgetId);

    public static bool StartedByBoard { get; } = Environment.GetCommandLineArgs().Contains(ComServerArgument, StringComparer.OrdinalIgnoreCase);

    // The board's objects are valid only during the call, so each method copies what it needs before going async.
    public void CreateWidget(WidgetContext widgetContext)
    {
        var (id, kind) = (widgetContext.Id, widgetContext.DefinitionId);
        _active[id] = 0;
        Platform?.Show(kind, id, widgetContext.Size, refreshWhenEmpty: true);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        _active.TryRemove(widgetId, out _);
        WhenIdle();
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var (verb, kind) = (actionInvokedArgs.Verb, actionInvokedArgs.WidgetContext.DefinitionId);
        if (IPlatformApplication.Current?.Services is not { } services) return;
        if (verb.StartsWith(OpenVerb, StringComparison.Ordinal))
        {
            var link = verb[OpenVerb.Length..];
            MainThread.BeginInvokeOnMainThread(() => Open(services, link));
            return;
        }
        if (!verb.StartsWith(ButtonVerb, StringComparison.Ordinal)) return;
        var at = DateTimeOffset.Now;
        Extensions.SpineWidgetsExtensions.HandleActionAsync(services, kind, verb[ButtonVerb.Length..], at)
            .SafeFireAndForget(e => Platform?.Logger.LogError(e, "Action \"{Verb}\" of widget \"{Kind}\" failed.", verb, kind));
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var context = contextChangedArgs.WidgetContext;
        Platform?.Show(context.DefinitionId, context.Id, context.Size, refreshWhenEmpty: false);
    }

    public void Activate(WidgetContext widgetContext)
    {
        var (id, kind) = (widgetContext.Id, widgetContext.DefinitionId);
        _active[id] = 0;
        _idle?.Dispose();
        _idle = null;
        // The board is on screen: the entry that applies now, which may have turned while the board was closed.
        Platform?.Show(kind, id, widgetContext.Size, refreshWhenEmpty: true);
    }

    public void Deactivate(string widgetId)
    {
        _active.TryRemove(widgetId, out _);
        WhenIdle();
    }

    /// <summary>
    /// A process the board started, whose window the user never opened, ends a while after the board stops showing
    /// its widgets: the board starts it again when it needs content, and a hidden app should not stay in memory for it.
    /// </summary>
    private void WhenIdle()
    {
        if (!StartedByBoard || !_active.IsEmpty) return;
        _idle?.Dispose();
        _idle = new Timer(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_active.IsEmpty || AnyWindowShown()) return;
            Platform?.Logger.LogInformation("The widget board no longer shows the widgets; ending the process it started.");
            Application.Current?.Quit();
        }), null, IdleTimeout, Timeout.InfiniteTimeSpan);
    }

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(1);

    private const byte VkMenu = 0x12;
    private const uint KeyEventFKeyUp = 0x2;

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    private static bool AnyWindowShown() =>
        Application.Current?.Windows.Any(w => w.Handler?.PlatformView is Microsoft.UI.Xaml.Window { AppWindow.IsVisible: true }) == true;
}

/// <summary>
/// Registers <see cref="WidgetBoardProvider"/> as the manifest's COM class, so the board's <c>CoCreateInstance</c> reaches
/// this process: the running app when there is one, otherwise the one COM starts for it.
/// </summary>
internal static partial class WidgetBoardServer
{
    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;

    private static ClassFactory? _factory;
    private static uint _cookie;

    public static void Register(Guid classId, ILogger logger)
    {
        if (_factory is not null) return;
        _factory = new ClassFactory();
        var unknown = new StrategyBasedComWrappers().GetOrCreateComInterfaceForObject(_factory, CreateComInterfaceFlags.None);
        try
        {
            var result = CoRegisterClassObject(classId, unknown, ClsctxLocalServer, RegclsMultipleUse, out _cookie);
            if (result < 0) logger.LogError("Registering the widget provider {ClassId} with COM failed: 0x{Result:X8}.", classId, result);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoRegisterClassObject(in Guid rclsid, nint pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [GeneratedComInterface]
    [Guid("00000001-0000-0000-C000-000000000046")]
    internal partial interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(nint outer, in Guid riid, out nint instance);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    [GeneratedComClass]
    internal sealed partial class ClassFactory : IClassFactory
    {
        private const int ClassENoAggregation = unchecked((int)0x80040110);

        public int CreateInstance(nint outer, in Guid riid, out nint instance)
        {
            instance = 0;
            if (outer != 0) return ClassENoAggregation;
            var provider = MarshalInspectable<IWidgetProvider>.FromManaged(WidgetBoardProvider.Instance);
            try { return Marshal.QueryInterface(provider, in riid, out instance); }
            finally { Marshal.Release(provider); }
        }

        public int LockServer(bool fLock) => 0;
    }
}
