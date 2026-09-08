using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Plugin.Maui.Spine.Push;

/// <summary>
/// The one call Spine.Push needs before the app starts, on iOS and Mac Catalyst.
/// </summary>
/// <remarks>
/// <para>
/// MAUI's lifecycle API has no hooks for the remote-notification delegate methods, so Spine adds
/// them to the app's <c>AppDelegate</c> class at runtime. It has to happen before
/// <c>UIApplication.Main</c>: UIKit reads which callbacks the delegate implements when the delegate
/// is assigned, and that is inside <c>UIApplication.Main</c>.
/// </para>
/// <code>
/// static void Main(string[] args)
/// {
///     SpinePush.Install();
///     UIApplication.Main(args, null, typeof(AppDelegate));
/// }
/// </code>
/// <para>
/// A method the app already implements is left alone; call the matching
/// <see cref="Forward"/> member from it instead.
/// </para>
/// </remarks>
public static unsafe class SpinePush
{
    private const string Objc = "/usr/lib/libobjc.dylib";

    [DllImport(Objc)] private static extern IntPtr sel_registerName(string name);
    [DllImport(Objc)] private static extern IntPtr objc_lookUpClass(string name);
    [DllImport(Objc)] private static extern byte class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(Objc)] private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr sel);

    /// <summary>Selectors the app already implemented, which Spine therefore did not add.</summary>
    public static IReadOnlyList<string> NotInstalled => _notInstalled;

    private static readonly List<string> _notInstalled = [];

    /// <summary>
    /// Adds the remote-notification delegate methods to the app's <c>AppDelegate</c> class. Call from
    /// <c>Main</c>, before <c>UIApplication.Main</c>.
    /// </summary>
    /// <param name="appDelegateClassName">
    /// The Objective-C name of the delegate class. The template's is <c>AppDelegate</c>, which is the
    /// default; pass another when the app registered a different one.
    /// </param>
    public static void Install(string appDelegateClassName = "AppDelegate")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDelegateClassName);

        var cls = objc_lookUpClass(appDelegateClassName);
        if (cls == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Spine.Push: no Objective-C class named '{appDelegateClassName}'. Pass the name the app's " +
                "delegate is registered under, and call Install() before UIApplication.Main.");
        }

        Add(cls, "application:didRegisterForRemoteNotificationsWithDeviceToken:", "v@:@@",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnRegistered);

        Add(cls, "application:didFailToRegisterForRemoteNotificationsWithError:", "v@:@@",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnFailed);

        Add(cls, "application:didReceiveRemoteNotification:fetchCompletionHandler:", "v@:@@@",
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnReceived);
    }

    private static void Add(IntPtr cls, string selector, string types, IntPtr implementation)
    {
        var sel = sel_registerName(selector);

        if (class_getInstanceMethod(cls, sel) != IntPtr.Zero)
        {
            // The app implements it. Overwriting would silently break whatever it does there.
            _notInstalled.Add(selector);
            return;
        }

        class_addMethod(cls, sel, implementation, types);
    }

    [UnmanagedCallersOnly]
    private static void OnRegistered(IntPtr self, IntPtr cmd, IntPtr application, IntPtr deviceToken) =>
        Forward.DidRegister(Runtime.GetNSObject<NSData>(deviceToken, owns: false));

    [UnmanagedCallersOnly]
    private static void OnFailed(IntPtr self, IntPtr cmd, IntPtr application, IntPtr error) =>
        Forward.DidFailToRegister(Runtime.GetNSObject<NSError>(error, owns: false));

    [UnmanagedCallersOnly]
    private static void OnReceived(IntPtr self, IntPtr cmd, IntPtr application, IntPtr userInfo, IntPtr completionHandler)
    {
        var info = Runtime.GetNSObject<NSDictionary>(userInfo, owns: false);
        Forward.DidReceive(info, result => InvokeBlock(completionHandler, (nuint)(long)result));
    }

    /// <summary>
    /// Call these from the app's own <c>AppDelegate</c> when it already implements one of the methods
    /// Spine would have added. <see cref="NotInstalled"/> lists which ones that is.
    /// </summary>
    public static class Forward
    {
        /// <summary>The device registered and APNs handed over a token.</summary>
        /// <param name="deviceToken">The token, as APNs gave it.</param>
        public static void DidRegister(NSData? deviceToken) => ApplePushPlatform.DidRegister(deviceToken);

        /// <summary>Registration failed.</summary>
        /// <param name="error">Why.</param>
        public static void DidFailToRegister(NSError? error) => ApplePushPlatform.DidFailToRegister(error);

        /// <summary>A remote notification arrived.</summary>
        /// <param name="userInfo">The payload.</param>
        /// <param name="completionHandler">Called with the fetch result when the app is done.</param>
        public static void DidReceive(NSDictionary? userInfo, Action<UIBackgroundFetchResult> completionHandler) =>
            ApplePushPlatform.DidReceive(userInfo, completionHandler);
    }

    /// <summary>
    /// Calls an Objective-C block. A block literal is
    /// <c>{ void* isa; int32 flags; int32 reserved; void (*invoke)(void*, ...); … }</c>, so on a
    /// 64-bit target the function pointer sits sixteen bytes in.
    /// </summary>
    private static void InvokeBlock(IntPtr block, nuint argument)
    {
        if (block == IntPtr.Zero) return;
        var invoke = *(IntPtr*)(block + 16);
        ((delegate* unmanaged[Cdecl]<IntPtr, nuint, void>)invoke)(block, argument);
    }
}
