using System.Windows.Input;
using Plugin.Maui.Spine.Barcodes;

namespace Plugin.Maui.Spine.Scanner;

/// <summary>
/// A live camera preview that reads barcodes: the standard formats through the platform's reader
/// (Vision on iOS and Mac Catalyst, ML Kit on Android), and a code shown by a grid of lamps through
/// <see cref="LightGridReader"/> when <see cref="LightGrid"/> is set.
/// </summary>
/// <remarks>
/// <para>An iOS or Mac Catalyst app must have <c>NSCameraUsageDescription</c> in Info.plist; on Android the package
/// declares <c>android.permission.CAMERA</c> itself and needs API 23. The view asks for the permission when it first
/// shows; when anything stops it from scanning, <see cref="Problem"/> and <see cref="ProblemChanged"/> say why.</para>
/// <para>The camera runs only while the native view is in a window and <see cref="IsScanning"/> is true. It stops,
/// with the torch off, the moment the view leaves its window, whether or not the page tells anyone it closed.</para>
/// <para>The camera focuses continuously; a tap on the view focuses on that spot until the scene changes (iOS) or for
/// five seconds (Android). On iOS the scanner also zooms in when the camera cannot focus close, as on the Pro iPhones,
/// and, when only linear codes are read, keeps autofocus to near distances.</para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <BarcodeScannerView Formats="QrCode" DetectedCommand="{Binding FoundCommand}" />
/// ]]></code>
/// </example>
public class BarcodeScannerView : View
{
    public static readonly BindableProperty FormatsProperty = BindableProperty.Create(
        nameof(Formats), typeof(BarcodeFormat), typeof(BarcodeScannerView), BarcodeFormat.All);

    public static readonly BindableProperty LightGridProperty = BindableProperty.Create(
        nameof(LightGrid), typeof(LightGridOptions), typeof(BarcodeScannerView));

    public static readonly BindableProperty IsScanningProperty = BindableProperty.Create(
        nameof(IsScanning), typeof(bool), typeof(BarcodeScannerView), true);

    public static readonly BindableProperty IsTorchOnProperty = BindableProperty.Create(
        nameof(IsTorchOn), typeof(bool), typeof(BarcodeScannerView), false, BindingMode.TwoWay);

    public static readonly BindableProperty DetectedCommandProperty = BindableProperty.Create(
        nameof(DetectedCommand), typeof(ICommand), typeof(BarcodeScannerView));

    public static readonly BindableProperty RepeatIntervalProperty = BindableProperty.Create(
        nameof(RepeatInterval), typeof(TimeSpan), typeof(BarcodeScannerView), TimeSpan.FromSeconds(2));

    public static readonly BindableProperty ScanAreaProperty = BindableProperty.Create(
        nameof(ScanArea), typeof(Rect?), typeof(BarcodeScannerView));

    public static readonly BindableProperty ConfirmationReadsProperty = BindableProperty.Create(
        nameof(ConfirmationReads), typeof(int), typeof(BarcodeScannerView), 2,
        validateValue: static (_, value) => value is int reads && reads >= 1);

    private static readonly BindablePropertyKey ProblemPropertyKey = BindableProperty.CreateReadOnly(
        nameof(Problem), typeof(ScannerProblem?), typeof(BarcodeScannerView), null);

    public static readonly BindableProperty ProblemProperty = ProblemPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey DiagnosticsPropertyKey = BindableProperty.CreateReadOnly(
        nameof(Diagnostics), typeof(string), typeof(BarcodeScannerView), null);

    public static readonly BindableProperty DiagnosticsProperty = DiagnosticsPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsTorchAvailablePropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsTorchAvailable), typeof(bool), typeof(BarcodeScannerView), false);

    public static readonly BindableProperty IsTorchAvailableProperty = IsTorchAvailablePropertyKey.BindableProperty;

    // Reads of the same value this many analysed frames apart, or closer, count as in a row
    private const int ConfirmationFrameGap = 2;

    private string? _lastValue;
    private DateTime _lastAt;
    private string? _candidate;
    private int _candidateReads;
    private long _candidateFrame;

    internal const string FocusCommand = "SpineScannerFocus";

    static BarcodeScannerView() => ScannerStrings.EnsureRegistered();

    public BarcodeScannerView()
    {
        // A tap focuses and exposes on that spot for a moment, then the camera goes back to focusing by itself
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            if (e.GetPosition(this) is { } point)
                Handler?.Invoke(FocusCommand, point);
        };
        GestureRecognizers.Add(tap);
    }

    /// <summary>The standard symbologies to read. <see cref="BarcodeFormat.None"/> leaves only <see cref="LightGrid"/>.</summary>
    public BarcodeFormat Formats
    {
        get => (BarcodeFormat)GetValue(FormatsProperty);
        set => SetValue(FormatsProperty, value);
    }

    /// <summary>A grid of lamps to read as well, such as <c>new LightGridOptions(12, 12)</c>; <see langword="null"/> skips it.</summary>
    public LightGridOptions? LightGrid
    {
        get => (LightGridOptions?)GetValue(LightGridProperty);
        set => SetValue(LightGridProperty, value);
    }

    /// <summary>Whether the camera runs. Turn it off to pause without leaving the page.</summary>
    public bool IsScanning
    {
        get => (bool)GetValue(IsScanningProperty);
        set => SetValue(IsScanningProperty, value);
    }

    /// <summary>
    /// Whether the torch is lit while the camera runs. Set back to <see langword="false"/> when scanning stops or the view
    /// leaves its window, so a torch button never shows a lamp that is off.
    /// </summary>
    public bool IsTorchOn
    {
        get => (bool)GetValue(IsTorchOnProperty);
        set => SetValue(IsTorchOnProperty, value);
    }

    /// <summary>Whether the camera in use has a torch.</summary>
    public bool IsTorchAvailable => (bool)GetValue(IsTorchAvailableProperty);

    /// <summary>Run with the <see cref="BarcodeScanResult"/> for each code read, on the main thread.</summary>
    public ICommand? DetectedCommand
    {
        get => (ICommand?)GetValue(DetectedCommandProperty);
        set => SetValue(DetectedCommandProperty, value);
    }

    /// <summary>
    /// How many reads of the same value in a row it takes before a standard code is reported; default 2, 1 reports at
    /// once. The platform readers check the check digit, but a partly seen linear code can still come out as another
    /// valid number; a second read costs one frame. "In a row" is counted in analysed frames: reads at most two frames
    /// apart, with no other value in between. A light grid is reported on its first read: it is read a few times a
    /// second at most, and Data Matrix corrects its own errors.
    /// </summary>
    public int ConfirmationReads
    {
        get => (int)GetValue(ConfirmationReadsProperty);
        set => SetValue(ConfirmationReadsProperty, value);
    }

    /// <summary>How long the same value stays quiet after it was reported, while the camera keeps seeing it.</summary>
    public TimeSpan RepeatInterval
    {
        get => (TimeSpan)GetValue(RepeatIntervalProperty);
        set => SetValue(RepeatIntervalProperty, value);
    }

    /// <summary>
    /// Where in the view a code counts, in device-independent units: a code whose centre lies outside is ignored, so the
    /// neighbouring code on a shelf or a sheet is not read by mistake. A code may be larger than the area (held close, a
    /// code outgrows any aim), so only its centre is tested. When several codes are inside, the one nearest the area's
    /// centre wins. <see langword="null"/>, the default, counts the whole view.
    /// </summary>
    public Rect? ScanArea
    {
        get => (Rect?)GetValue(ScanAreaProperty);
        set => SetValue(ScanAreaProperty, value);
    }

    /// <summary>
    /// A line about what the camera and the readers are doing, updated twice a second: frames per second, time per
    /// frame, and for a light grid whether the grid is found and read. Show it while setting up a code, to see why
    /// nothing is read.
    /// </summary>
    public string? Diagnostics => (string?)GetValue(DiagnosticsProperty);

    /// <summary>What stops the view from scanning, or <see langword="null"/> while it scans.</summary>
    public ScannerProblem? Problem => (ScannerProblem?)GetValue(ProblemProperty);

    /// <summary>A code was read. Raised on the main thread; the same value repeats only after <see cref="RepeatInterval"/>.</summary>
    public event EventHandler<BarcodeDetectedEventArgs>? Detected;

    /// <summary><see cref="Problem"/> changed, with a message to show.</summary>
    public event EventHandler<ScannerProblemEventArgs>? ProblemChanged;

    /// <summary>
    /// The codes one frame held, placed in this view, with the frame's number in the camera's count of analysed frames.
    /// Main thread only. Picks the code in the scan area, then lets it through once it has been read
    /// <see cref="ConfirmationReads"/> times in a row.
    /// </summary>
    internal void RaiseFrame(IReadOnlyList<BarcodeScanResult> codes, long frame)
    {
        if (Pick(codes) is not { } result) return;

        // A frame number that does not move forward (a new handler counts afresh) starts over
        long gap = frame - _candidateFrame;
        if (result.Value == _candidate && gap > 0 && gap <= ConfirmationFrameGap)
            _candidateReads++;
        else
        {
            _candidate = result.Value;
            _candidateReads = 1;
        }
        _candidateFrame = frame;
        if (!result.IsLightGrid && _candidateReads < ConfirmationReads) return;

        var now = DateTime.UtcNow;
        if (result.Value == _lastValue && now - _lastAt < RepeatInterval)
        {
            _lastAt = now;
            return;
        }
        _lastValue = result.Value;
        _lastAt = now;

        Detected?.Invoke(this, new BarcodeDetectedEventArgs(result));
        if (DetectedCommand is { } command && command.CanExecute(result))
            command.Execute(result);
    }

    private BarcodeScanResult? Pick(IReadOnlyList<BarcodeScanResult> codes)
    {
        if (codes.Count == 0) return null;
        if (ScanArea is not { } area) return codes[0];

        BarcodeScanResult? best = null;
        double bestDistance = double.MaxValue;
        foreach (var code in codes)
        {
            // A reader that gave no corners cannot be placed; it only counts when nothing narrows the view
            if (code.Corners.Count == 0) continue;
            var centre = new Point(code.Corners.Average(c => c.X), code.Corners.Average(c => c.Y));
            if (!area.Contains(centre)) continue;
            double distance = centre.Distance(area.Center);
            if (distance < bestDistance)
            {
                best = code;
                bestDistance = distance;
            }
        }
        return best;
    }

    internal void RaiseProblem(ScannerProblem? problem, string? message)
    {
        if (problem == Problem && problem is null) return;
        SetValue(ProblemPropertyKey, problem);
        ProblemChanged?.Invoke(this, new ScannerProblemEventArgs(problem, message));
    }

    internal void SetTorchAvailable(bool available) => SetValue(IsTorchAvailablePropertyKey, available);

    /// <summary>The camera stopped and took the lamp with it.</summary>
    internal void SetTorchOff()
    {
        if (IsTorchOn) IsTorchOn = false;
    }

    internal void SetDiagnostics(string? text) => SetValue(DiagnosticsPropertyKey, text);
}
