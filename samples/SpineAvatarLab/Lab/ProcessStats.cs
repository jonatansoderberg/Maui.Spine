using System.Runtime.InteropServices;

namespace SpineAvatarLab.Lab;

/// <summary>
/// The app process's CPU time and memory footprint, for comparing renderers. A web view renders in
/// WebKit's own processes (WebContent, GPU), which these numbers do not include.
/// </summary>
internal static partial class ProcessStats
{
    /// <summary>User plus system CPU seconds used by this process so far.</summary>
    public static double CpuSeconds()
    {
        if (getrusage(0, out var usage) != 0)
            return double.NaN;
        return usage.UserSeconds + usage.UserMicroseconds / 1e6 + usage.SystemSeconds + usage.SystemMicroseconds / 1e6;
    }

    /// <summary>Physical footprint in bytes (what Xcode's memory gauge shows on Apple; resident set on Android).</summary>
    public static long FootprintBytes()
    {
#if IOS || MACCATALYST
        var info = new TaskVmInfo();
        var count = (uint)(Marshal.SizeOf<TaskVmInfo>() / sizeof(int));
        return task_info(task_self_trap(), 22 /* TASK_VM_INFO */, ref info, ref count) == 0 ? info.PhysFootprint : -1;
#elif ANDROID
        var statm = File.ReadAllText("/proc/self/statm").Split(' ');
        return long.Parse(statm[1], System.Globalization.CultureInfo.InvariantCulture) * Environment.SystemPageSize;
#else
        return Environment.WorkingSet;
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RUsage
    {
        public long UserSeconds;
        public long UserMicroseconds;
        public long SystemSeconds;
        public long SystemMicroseconds;
        private long _r0, _r1, _r2, _r3, _r4, _r5, _r6, _r7, _r8, _r9, _r10, _r11, _r12, _r13;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int getrusage(int who, out RUsage usage);

#if IOS || MACCATALYST
    [StructLayout(LayoutKind.Sequential)]
    private struct TaskVmInfo
    {
        public ulong VirtualSize;
        public int RegionCount;
        public int PageSize;
        public ulong ResidentSize;
        public ulong ResidentSizePeak;
        public ulong Device;
        public ulong DevicePeak;
        public ulong Internal;
        public ulong InternalPeak;
        public ulong External;
        public ulong ExternalPeak;
        public ulong Reusable;
        public ulong ReusablePeak;
        public ulong PurgeableVolatilePmap;
        public ulong PurgeableVolatileResident;
        public ulong PurgeableVolatileVirtual;
        public ulong Compressed;
        public ulong CompressedPeak;
        public ulong CompressedLifetime;
        public long PhysFootprint;
    }

    [LibraryImport("/usr/lib/libSystem.dylib")]
    private static partial uint task_self_trap();

    [LibraryImport("/usr/lib/libSystem.dylib")]
    private static partial int task_info(uint task, int flavor, ref TaskVmInfo info, ref uint count);
#endif
}
