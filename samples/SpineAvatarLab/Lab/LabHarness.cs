#if DEBUG
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SpineAvatarLab.Lab;

/// <summary>
/// Drives the lab without taps, for measurements and screenshots from a script. Debug builds only.
/// Mac Catalyst: run the app binary with <c>AVATARLAB_HARNESS=stdin</c> and write commands to its
/// standard input; results come back on standard output (the app's container is closed to other
/// processes). iOS simulator and Android: write commands to <c>harness.txt</c> in the app data
/// directory; results go to <c>harness-out.txt</c> and screenshots to <c>&lt;name&gt;.png</c> beside it.
/// </summary>
internal static class LabHarness
{
    private static FileStream? _stdout;

    public static void Start(LabPage page)
    {
        if (Environment.GetEnvironmentVariable("AVATARLAB_HARNESS") == "stdin")
        {
            _stdout = new FileStream(new SafeFileHandle(1, ownsHandle: false), FileAccess.Write);
            var stdin = new StreamReader(new FileStream(new SafeFileHandle(0, ownsHandle: false), FileAccess.Read));
            Task.Run(async () =>
            {
                while (await stdin.ReadLineAsync() is { } line)
                    await Run(page, line);
            });
            Write("harness ready");
            return;
        }

        var commands = Path.Combine(FileSystem.AppDataDirectory, "harness.txt");
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(400);
                if (!File.Exists(commands))
                    continue;
                var lines = await File.ReadAllLinesAsync(commands);
                File.Delete(commands);
                foreach (var line in lines)
                    await Run(page, line);
            }
        });
    }

    private static async Task Run(LabPage page, string line)
    {
        line = line.Trim();
        if (line.Length == 0)
            return;
        try
        {
            if (line.StartsWith("wait ", StringComparison.Ordinal))
            {
                await Task.Delay(int.Parse(line[5..], System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            var result = await MainThread.InvokeOnMainThreadAsync(() => page.RunCommandAsync(line));
            if (result is byte[] png)
            {
                var name = line.Split(' ', 2) is [_, var n] ? n : "shot";
                if (_stdout is not null)
                    Write($"png {name} {Convert.ToBase64String(png)}");
                else
                {
                    await File.WriteAllBytesAsync(Path.Combine(FileSystem.AppDataDirectory, name + ".png"), png);
                    Write($"png {name} saved");
                }
            }
            else
                Write($"ok {line} {result}");
        }
        catch (Exception e)
        {
            Write($"error {line}: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Write(string text)
    {
        if (_stdout is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(text + "\n");
            lock (_stdout)
            {
                _stdout.Write(bytes);
                _stdout.Flush();
            }
            return;
        }
        File.AppendAllText(Path.Combine(FileSystem.AppDataDirectory, "harness-out.txt"), text + "\n");
    }

    /// <summary>The window as a PNG, drawn the way the screen shows it.</summary>
    public static byte[] Screenshot(Page page)
    {
#if IOS || MACCATALYST
        var view = (page.Window?.Handler?.PlatformView as UIKit.UIWindow) ?? throw new InvalidOperationException("No window.");
        var renderer = new UIKit.UIGraphicsImageRenderer(view.Bounds.Size);
        var image = renderer.CreateImage(_ => view.DrawViewHierarchy(view.Bounds, afterScreenUpdates: true));
        return image.AsPNG()!.ToArray();
#elif ANDROID
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No activity.");
        var root = activity.Window!.DecorView.RootView!;
        var bitmap = Android.Graphics.Bitmap.CreateBitmap(root.Width, root.Height, Android.Graphics.Bitmap.Config.Argb8888!)!;
        root.Draw(new Android.Graphics.Canvas(bitmap));
        using var stream = new MemoryStream();
        bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
#endif
