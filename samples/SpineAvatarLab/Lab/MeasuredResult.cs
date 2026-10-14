using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Controls.Avatar.Feeds;

namespace SpineAvatarLab.Lab;

/// <summary>
/// <c>measured-result.json</c>: what this device measured for the avatar on screen, with the device,
/// OS and build it was measured on. What the lab cannot measure is written as NotMeasured, never Pass.
/// </summary>
internal static class MeasuredResult
{
    public static string Create(AvatarView avatar, string source)
    {
        var stats = avatar.Stats;
        var package = avatar.Package;
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("measuredAt", DateTimeOffset.Now.ToString("O"));
            json.WriteString("source", source);
            json.WriteString("avatar", package?.Manifest.Id);
            json.WriteString("assetVersion", package?.Manifest.AssetVersion);
            json.WriteString("renderer", avatar.RendererName);

            json.WriteStartObject("device");
            json.WriteString("platform", DeviceInfo.Platform.ToString());
            json.WriteString("model", DeviceInfo.Model);
            json.WriteString("manufacturer", DeviceInfo.Manufacturer);
            json.WriteString("os", DeviceInfo.VersionString);
            json.WriteString("type", DeviceInfo.DeviceType.ToString());
            json.WriteNumber("density", DeviceDisplay.MainDisplayInfo.Density);
#if DEBUG
            json.WriteString("build", "Debug");
#else
            json.WriteString("build", "Release");
#endif
            json.WriteString("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            json.WriteEndObject();

            json.WriteNumber("sizeDip", avatar.Width);
            json.WriteNumber("maxFramesPerSecond", avatar.MaxFramesPerSecond);
            if (stats is not null)
            {
                json.WriteNumber("framesPerSecond", Math.Round(stats.FramesPerSecond, 1));
                json.WriteNumber("renderMsP50", Math.Round(stats.Percentile(0.5), 3));
                json.WriteNumber("renderMsP95", Math.Round(stats.Percentile(0.95), 3));
                json.WriteNumber("allocatedBytesPerFramePeak", stats.MaxAllocatedBytes);
                json.WriteNumber("frames", stats.Frames);
                if (stats.FirstFrameTimestamp > 0)
                    json.WriteNumber("firstFrameMs", Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(avatar.LoadStartedTimestamp, stats.FirstFrameTimestamp).TotalMilliseconds, 1));
            }
            json.WriteNumber("loadMs", Math.Round(avatar.LoadMilliseconds, 1));
            json.WriteNumber("managedHeapBytes", GC.GetTotalMemory(false));
            json.WriteNumber("workingSetBytes", Environment.WorkingSet);
            json.WriteString("playbackClock", AvatarPcmPlayer.ClockDescription);

            if (avatar.Scheduler?.Diagnostics is { } d)
            {
                json.WriteStartObject("scheduler");
                json.WriteNumber("acceptedCues", d.AcceptedCues);
                json.WriteNumber("staleCues", d.StaleCues);
                json.WriteNumber("rejectedCues", d.RejectedCues);
                json.WriteNumber("underruns", d.Underruns);
                json.WriteNumber("resets", d.Resets);
                json.WriteEndObject();
            }

            json.WriteStartObject("notMeasured");
            json.WriteString("lipSyncOffset", "needs an external recording of sound and screen against a shared reference");
            json.WriteString("nativeMemory", "needs the platform profiler (Instruments, Android Studio)");
            json.WriteString("bluetoothLatency", "not run in the lab");
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
