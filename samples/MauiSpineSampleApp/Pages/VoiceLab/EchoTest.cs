using System.Diagnostics;
using Plugin.Maui.Spine.Voice;

namespace MauiSpineSampleApp.Pages.VoiceLab;

/// <summary>One take of the echo test: the reference played through the engine while the microphone records.</summary>
/// <param name="VoiceProcessingAsked">Whether echo cancellation was asked for.</param>
/// <param name="VoiceProcessingOn">Whether the platform gave it.</param>
/// <param name="Route">Where the sound went.</param>
/// <param name="ReferenceDbfs">The reference's level as played.</param>
/// <param name="NoiseDbfs">The room before playback.</param>
/// <param name="EchoDbfs">
/// The microphone while the reference played, as the median of 100 ms windows: the echo, robust to someone talking.
/// </param>
/// <param name="LoudestDbfs">The loudest 100 ms window while the reference played: talk passed through, or echo leaking.</param>
/// <param name="LatencyMs">Output to input, from the cross-correlation; only meaningful without echo cancellation.</param>
/// <param name="CpuPercent">The process's CPU while it ran, as a share of one core.</param>
public sealed record EchoTake(
    bool VoiceProcessingAsked, bool VoiceProcessingOn, string Route,
    double ReferenceDbfs, double NoiseDbfs, double EchoDbfs, double LoudestDbfs, double? LatencyMs, double CpuPercent)
{
    /// <summary>How far below the reference the echo is.</summary>
    public double EchoReturnLossDb => ReferenceDbfs - EchoDbfs;
}

/// <summary>Both takes, and what echo cancellation removed.</summary>
public sealed record EchoResult(string Device, string System, DateTimeOffset At, EchoTake Without, EchoTake With)
{
    /// <summary>Echo return loss enhancement: how much quieter the echo is with echo cancellation.</summary>
    public double ErleDb => Without.EchoDbfs - With.EchoDbfs;
}

/// <summary>Plays a known recording through the engine's own player while the microphone records, with and without echo cancellation.</summary>
public static class EchoTest
{
    private const int Rate = 24_000;
    private const int Decimation = 8;

    public static async Task<EchoResult> RunAsync(IVoiceAudio audio, short[] reference, bool speaker, Action<string> progress, CancellationToken cancellationToken)
    {
        progress("Without echo cancellation…");
        var without = await TakeAsync(audio, reference, voiceProcessing: false, speaker, cancellationToken);
        progress("With echo cancellation…");
        var with = await TakeAsync(audio, reference, voiceProcessing: true, speaker, cancellationToken);
        return new EchoResult($"{DeviceInfo.Manufacturer} {DeviceInfo.Model}", $"{DeviceInfo.Platform} {DeviceInfo.VersionString}", DateTimeOffset.Now, without, with);
    }

    private static async Task<EchoTake> TakeAsync(IVoiceAudio audio, short[] reference, bool voiceProcessing, bool speaker, CancellationToken cancellationToken)
    {
        var captured = new List<short>(Rate * 20);
        void OnInput(ReadOnlySpan<byte> pcm)
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm);
            lock (captured)
                captured.AddRange(samples);
        }

        audio.InputAvailable += OnInput;
        try
        {
            await audio.StartAsync(new VoiceAudioOptions { VoiceProcessing = voiceProcessing, PreferSpeaker = speaker }, cancellationToken);

            // The echo canceller settles in about a second; the half second after it is the room's noise.
            await Task.Delay(1000, cancellationToken);
            int noiseStart;
            lock (captured) noiseStart = captured.Count;
            await Task.Delay(500, cancellationToken);
            int playStart;
            lock (captured) playStart = captured.Count;

            var cpuBefore = ProcessCpu.Time();
            var clock = Stopwatch.StartNew();

            var bytes = new byte[reference.Length * 2];
            Buffer.BlockCopy(reference, 0, bytes, 0, bytes.Length);
            audio.Enqueue("reference", bytes);
            audio.Complete("reference");
            while (audio.IsPlaying)
                await Task.Delay(50, cancellationToken);
            await Task.Delay(500, cancellationToken);

            var cpu = (ProcessCpu.Time() - cpuBefore).TotalSeconds / clock.Elapsed.TotalSeconds * 100;
            var route = audio.Route ?? "?";
            var processing = audio.IsVoiceProcessingOn;
            await audio.StopAsync();

            short[] all;
            lock (captured) all = [.. captured];
            await File.WriteAllBytesAsync(
                Path.Combine(FileSystem.AppDataDirectory, voiceProcessing ? "voice-lab-with.wav" : "voice-lab-without.wav"), Wav(all), cancellationToken);
            var noise = all.AsSpan(noiseStart, playStart - noiseStart);
            var echo = all.AsSpan(playStart, Math.Min(all.Length - playStart, reference.Length + Rate / 2));

            var windows = Windows(echo);
            return new EchoTake(voiceProcessing, processing, route,
                Dbfs(reference), Dbfs(noise), Median(windows), windows.DefaultIfEmpty(-120).Max(),
                voiceProcessing ? null : LatencyMs(reference, echo), cpu);
        }
        finally
        {
            audio.InputAvailable -= OnInput;
            if (audio.IsRunning)
                await audio.StopAsync();
        }
    }

    /// <summary>The level of each 100 ms window.</summary>
    private static double[] Windows(ReadOnlySpan<short> samples)
    {
        var size = Rate / 10;
        var levels = new double[samples.Length / size];
        for (var i = 0; i < levels.Length; i++)
            levels[i] = Dbfs(samples.Slice(i * size, size));
        return levels;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
            return -120;
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    /// <summary>RMS in dB below full scale.</summary>
    public static double Dbfs(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty)
            return -120;
        double sum = 0;
        foreach (var s in samples)
            sum += (double)s * s;
        var rms = Math.Sqrt(sum / samples.Length) / 32768.0;
        return 20 * Math.Log10(Math.Max(rms, 1e-6));
    }

    /// <summary>The lag in milliseconds at which the recording best matches the reference's envelope, up to 600 ms.</summary>
    public static double? LatencyMs(ReadOnlySpan<short> reference, ReadOnlySpan<short> recording)
    {
        var a = Envelope(reference);
        var b = Envelope(recording);
        var maxLag = Math.Min(b.Length - 1, 600 * Rate / 1000 / Decimation);
        if (maxLag <= 0)
            return null;

        double best = double.MinValue;
        var bestLag = -1;
        for (var lag = 0; lag <= maxLag; lag++)
        {
            double sum = 0;
            var n = Math.Min(a.Length, b.Length - lag);
            for (var i = 0; i < n; i++)
                sum += a[i] * b[i + lag];
            if (sum > best)
            {
                best = sum;
                bestLag = lag;
            }
        }
        // A peak at the edge of the window is the correlation failing, not a latency.
        return bestLag <= 0 || bestLag >= maxLag ? null : bestLag * Decimation * 1000.0 / Rate;
    }

    private static double[] Envelope(ReadOnlySpan<short> samples)
    {
        var envelope = new double[samples.Length / Decimation];
        for (var i = 0; i < envelope.Length; i++)
        {
            double sum = 0;
            for (var j = 0; j < Decimation; j++)
                sum += Math.Abs((int)samples[i * Decimation + j]);
            envelope[i] = sum / Decimation;
        }
        var mean = envelope.Length > 0 ? envelope.Average() : 0;
        for (var i = 0; i < envelope.Length; i++)
            envelope[i] -= mean;
        return envelope;
    }

    /// <summary>A 16-bit mono PCM WAV file at 24 kHz, for listening to a take afterwards.</summary>
    public static byte[] Wav(short[] samples)
    {
        var data = samples.Length * 2;
        using var stream = new MemoryStream(44 + data);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + data);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data);
        foreach (var s in samples)
            writer.Write(s);
        return stream.ToArray();
    }

    /// <summary>The samples of a 16-bit PCM WAV file, from its data chunk.</summary>
    public static short[] ReadWav(byte[] wav)
    {
        var offset = 12;
        while (offset + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, offset, 4);
            var size = BitConverter.ToInt32(wav, offset + 4);
            if (id == "data")
            {
                var samples = new short[Math.Min(size, wav.Length - offset - 8) / 2];
                Buffer.BlockCopy(wav, offset + 8, samples, 0, samples.Length * 2);
                return samples;
            }
            offset += 8 + size + (size & 1);
        }
        throw new InvalidDataException("The WAV file has no data chunk.");
    }
}

/// <summary>The process's CPU time. <see cref="Process.TotalProcessorTime"/> is not supported on Apple platforms, so it asks getrusage there.</summary>
internal static partial class ProcessCpu
{
    public static TimeSpan Time()
    {
#if IOS || MACCATALYST
        if (getrusage(0, out var usage) != 0)
            return TimeSpan.Zero;
        return TimeSpan.FromSeconds(usage.UserSeconds + usage.UserMicroseconds / 1e6 + usage.SystemSeconds + usage.SystemMicroseconds / 1e6);
#else
        return Process.GetCurrentProcess().TotalProcessorTime;
#endif
    }

#if IOS || MACCATALYST
    // struct rusage on 64-bit Darwin: two timevals (long seconds, int microseconds, padded to 16 bytes), then 14 longs.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 144)]
    private struct Rusage
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public long UserSeconds;
        [System.Runtime.InteropServices.FieldOffset(8)] public int UserMicroseconds;
        [System.Runtime.InteropServices.FieldOffset(16)] public long SystemSeconds;
        [System.Runtime.InteropServices.FieldOffset(24)] public int SystemMicroseconds;
    }

    [System.Runtime.InteropServices.LibraryImport("libc")]
    private static partial int getrusage(int who, out Rusage usage);
#endif
}
