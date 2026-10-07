#if ANDROID
using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.Gms.Tasks;
using Android.Runtime;
using AndroidX.Camera.Core;
using AndroidX.Camera.Core.ResolutionSelector;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Java.Util.Concurrent;
using Microsoft.Maui.Handlers;
using Plugin.Maui.Spine.Barcodes;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Common;
using MLBarcode = Xamarin.Google.MLKit.Vision.Barcode.Common.Barcode;
using Task = System.Threading.Tasks.Task;

[assembly: UsesPermission(Android.Manifest.Permission.Camera)]
[assembly: UsesFeature("android.hardware.camera.any", Required = false)]

namespace Plugin.Maui.Spine.Scanner;

public sealed class BarcodeScannerViewHandler() : ViewHandler<BarcodeScannerView, Android.Widget.FrameLayout>(Mapper, CommandMapper)
{
    public static readonly IPropertyMapper<BarcodeScannerView, BarcodeScannerViewHandler> Mapper =
        new PropertyMapper<BarcodeScannerView, BarcodeScannerViewHandler>(ViewMapper)
        {
            [nameof(BarcodeScannerView.Formats)] = static (h, v) => h._camera?.SetFormats(v.Formats),
            [nameof(BarcodeScannerView.LightGrid)] = static (h, v) => h._camera?.Reader.SetLightGrid(v.LightGrid),
            [nameof(BarcodeScannerView.IsScanning)] = static (h, v) => h._camera?.SetWanted(v.IsScanning),
            [nameof(BarcodeScannerView.IsTorchOn)] = static (h, v) => h._camera?.SetTorch(v.IsTorchOn),
        };

    public static readonly CommandMapper<BarcodeScannerView, BarcodeScannerViewHandler> CommandMapper = new(ViewCommandMapper)
    {
        [BarcodeScannerView.FocusCommand] = static (h, _, arg) => { if (arg is Point point) h._camera?.FocusAt(point); },
    };

    private ScannerCamera? _camera;

    private PreviewView? _previewView;
    private Android.Widget.ImageView? _still;

    // The preview, and over it a still of its last frame for when the camera stops after a hit
    protected override Android.Widget.FrameLayout CreatePlatformView()
    {
        var container = new Android.Widget.FrameLayout(Context);
        _previewView = new PreviewView(Context);
        // TextureView rather than SurfaceView, so the preview clips and layers like any other MAUI view
        _previewView.SetImplementationMode(PreviewView.ImplementationMode.Compatible);
        _still = new Android.Widget.ImageView(Context) { Visibility = Android.Views.ViewStates.Gone };
        _still.SetScaleType(Android.Widget.ImageView.ScaleType.CenterCrop);
        var fill = Android.Views.ViewGroup.LayoutParams.MatchParent;
        container.AddView(_previewView, new Android.Widget.FrameLayout.LayoutParams(fill, fill));
        container.AddView(_still, new Android.Widget.FrameLayout.LayoutParams(fill, fill));
        return container;
    }

    protected override void ConnectHandler(Android.Widget.FrameLayout platformView)
    {
        _camera = new ScannerCamera(Context, _previewView!, _still!)
        {
            Detected = r => VirtualView?.RaiseDetected(r),
            Problem = (p, m) => VirtualView?.RaiseProblem(p, m),
            TorchAvailable = a => VirtualView?.SetTorchAvailable(a),
            DiagnosticsChanged = d => VirtualView?.SetDiagnostics(d),
        };
        base.ConnectHandler(platformView);
        // The one signal that always comes: a sheet can close without its page or MAUI saying so
        platformView.ViewAttachedToWindow += OnAttached;
        platformView.ViewDetachedFromWindow += OnDetached;
        _camera.SetOnScreen(platformView.IsAttachedToWindow);
    }

    private void OnAttached(object? sender, Android.Views.View.ViewAttachedToWindowEventArgs e) => _camera?.SetOnScreen(true);

    private void OnDetached(object? sender, Android.Views.View.ViewDetachedFromWindowEventArgs e) => _camera?.SetOnScreen(false);

    protected override void DisconnectHandler(Android.Widget.FrameLayout platformView)
    {
        platformView.ViewAttachedToWindow -= OnAttached;
        platformView.ViewDetachedFromWindow -= OnDetached;
        _camera?.Shutdown();
        _camera = null;
        base.DisconnectHandler(platformView);
    }
}

/// <summary>
/// The camera behind <see cref="BarcodeScannerView"/> on Android: CameraX with a preview and an analysis use
/// case bound to the activity's lifecycle. Each frame's Y plane goes to the light-grid reader; ML Kit reads the
/// standard codes from the same frame.
/// </summary>
/// <remarks>
/// Everything heavy is let go when the view leaves its window, not only in <see cref="Shutdown"/>: a host that never
/// disconnects the handlers of a closed sheet or popup would otherwise keep an ML Kit reader for every scan.
/// </remarks>
internal sealed class ScannerCamera : Java.Lang.Object, ImageAnalysis.IAnalyzer
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);

    // One analysis thread for the app, never shut down: a thread per scanner would outlive every view whose handler is
    // never disconnected, and ML Kit hands its results back on this executor, so shutting it down with a frame still
    // being read threw RejectedExecutionException on ML Kit's own thread
    private static readonly Lazy<IExecutorService> AnalysisThread = new(() => Executors.NewSingleThreadExecutor()!);

    private readonly Context _context;
    private readonly PreviewView _preview;
    private readonly Android.Widget.ImageView _still;
    private ProcessCameraProvider? _provider;
    private Preview? _previewCase;
    private ImageAnalysis? _analysisCase;
    private ICamera? _cameraControl;
    private LiveData? _cameraState;
    private CameraStateObserver? _cameraStateObserver;
    private IBarcodeScanner? _mlKit;
    private BarcodeFormat _mlKitFormats = BarcodeFormat.None;
    private System.Threading.Timer? _watchdog;
    private byte[] _luminance = [];
    private bool _wanted = true, _onScreen, _torch, _bound, _starting, _healthy = true;
    // Main thread only. _cameraOpen: frames can be expected; _interrupted: another app holds the camera
    private bool _cameraOpen, _interrupted;
    private volatile bool _shutdown, _analysing;
    private long _lastFrame;

    public ScannerCamera(Context context, PreviewView preview, Android.Widget.ImageView still)
    {
        _context = context;
        _preview = preview;
        _still = still;
    }

    public FrameReader Reader { get; } = new();
    public Action<BarcodeScanResult>? Detected;

    /// <summary>Places a hit's corners, in upright image pixels, in the preview, which fills and centres the image.</summary>
    private BarcodeScanResult Place(FrameHit hit)
    {
        float vw = _preview.Width, vh = _preview.Height;
        if (vw <= 0 || vh <= 0 || hit.ImageWidth <= 0) return hit.Result;
        float scale = Math.Max(vw / hit.ImageWidth, vh / hit.ImageHeight);
        float ox = (vw - hit.ImageWidth * scale) / 2, oy = (vh - hit.ImageHeight * scale) / 2;
        float density = _context.Resources?.DisplayMetrics?.Density ?? 1;
        var corners = hit.Corners.Select(c => new Point((ox + c.X * scale) / density, (oy + c.Y * scale) / density)).ToArray();
        return hit.Result with { Corners = corners };
    }
    public Action<ScannerProblem?, string?>? Problem;
    public Action<bool>? TorchAvailable;
    public Action<string?>? DiagnosticsChanged;

    public void SetFormats(BarcodeFormat formats) => Reader.Formats = formats;

    public void SetWanted(bool wanted)
    {
        _wanted = wanted;
        Update();
    }

    public void SetOnScreen(bool onScreen)
    {
        _onScreen = onScreen;
        Update();
    }

    public void SetTorch(bool on)
    {
        _torch = on;
        ApplyTorch();
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        Release();
    }

    /// <summary>Lets go of the camera and the reader; the next <see cref="Update"/> starts afresh.</summary>
    private void Release()
    {
        Unbind(keepLastFrame: false);
        // On the analysis thread, after any frame still being read there, so a late frame cannot create a new reader
        AnalysisThread.Value.Execute(new Java.Lang.Runnable(() =>
        {
            _mlKit?.Close();
            _mlKit = null;
        }));
    }

    private async void Update()
    {
        if (_shutdown) return;
        bool run = _wanted && _onScreen;
        if (!run)
        {
            // Paused on screen (after a hit): the last frame stays. Off screen: nothing is left holding on
            if (_onScreen) Unbind(keepLastFrame: true);
            else Release();
            return;
        }
        if (_bound || _starting) return;

        _starting = true;
        try { await BindAsync(); }
        catch (Exception ex) { Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} ({ex.Message})"); }
        finally { _starting = false; }

        // The page may have left while the permission prompt or the provider was pending
        if (_shutdown || !_onScreen) Release();
        else if (_bound && !_wanted) Unbind(keepLastFrame: true);
    }

    private async Task BindAsync()
    {
        PermissionStatus status;
        try
        {
            status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Camera>();
        }
        catch (PermissionException)
        {
            Report(ScannerProblem.Failed, "Declare android.permission.CAMERA in AndroidManifest.xml to use the camera.");
            return;
        }
        if (status != PermissionStatus.Granted)
        {
            Report(ScannerProblem.PermissionDenied);
            return;
        }

        if (Platform.CurrentActivity is not ILifecycleOwner owner)
        {
            Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} (no lifecycle owner)");
            return;
        }

        _provider ??= await ProviderAsync();
        if (_shutdown) return;

        var selector = _provider.HasCamera(CameraSelector.DefaultBackCamera!) ? CameraSelector.DefaultBackCamera!
            : _provider.HasCamera(CameraSelector.DefaultFrontCamera!) ? CameraSelector.DefaultFrontCamera!
            : null;
        if (selector is null)
        {
            Report(ScannerProblem.NoCamera);
            return;
        }

        // Enough detail for a 12 × 12 grid across a room, small enough to read every frame; the preview asks
        // for the same, so both show the same field of view and a hit's corners land on the code
        var resolution = new ResolutionSelector.Builder()
            .SetResolutionStrategy(new ResolutionStrategy(new Android.Util.Size(1280, 720), ResolutionStrategy.FallbackRuleClosestHigherThenLower))!
            .Build();
        var previewCase = new Preview.Builder().SetResolutionSelector(resolution)!.Build()!;
        previewCase.SetSurfaceProvider(ContextCompat.GetMainExecutor(_context), _preview.SurfaceProvider);
        _previewCase = previewCase;
        var analysisCase = new ImageAnalysis.Builder()
            .SetResolutionSelector(resolution)!
            .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)!
            .Build()!;
        analysisCase.SetAnalyzer(AnalysisThread.Value, this);
        _analysisCase = analysisCase;

        _cameraControl = _provider.BindToLifecycle(owner, selector, previewCase, analysisCase);
        _bound = true;
        _analysing = true;
        _still.Visibility = Android.Views.ViewStates.Gone;
        Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
        TorchAvailable?.Invoke(_cameraControl.CameraInfo?.HasFlashUnit == true);
        ApplyTorch();
        Report(null);
        // CameraX says when the camera is open, closed with the activity, or taken by another app. ObserveForever, not
        // Observe(activity): a lifecycle-bound observer goes quiet once the activity stops, so the close that comes with
        // the background never arrives and the watchdog takes the silence for a stuck camera. Removed in Unbind: the
        // camera's LiveData outlives this view and would otherwise hold it
        _cameraOpen = _interrupted = false;
        _cameraState = _cameraControl.CameraInfo?.CameraState;
        if (_cameraState is not null)
        {
            _cameraStateObserver = new CameraStateObserver(this);
            _cameraState.ObserveForever(_cameraStateObserver);
        }
        _watchdog ??= new System.Threading.Timer(_ => CheckFrames(), null, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5));
    }

    /// <summary>Main thread (LiveData). Turns CameraX's camera state into <see cref="ScannerProblem"/>.</summary>
    private void OnCameraState(CameraState state)
    {
        if (_shutdown || !_bound) return;
        bool open = state.GetType()?.Equals(CameraState.Type.Open) == true;
        if (open && !_cameraOpen)
            // The two seconds without a frame count from when the camera opened, not from the bind
            Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
        _cameraOpen = open;

        int? error = state.Error?.Code;
        switch (error)
        {
            // Another app or a call has the camera; CameraX opens it again by itself once it is free
            case CameraState.ErrorCameraInUse or CameraState.ErrorMaxCamerasInUse or CameraState.ErrorDoNotDisturbModeEnabled:
                _interrupted = true;
                Report(ScannerProblem.Interrupted);
                break;
            case CameraState.ErrorCameraDisabled or CameraState.ErrorCameraFatalError or CameraState.ErrorStreamConfig or CameraState.ErrorCameraRemoved:
                Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} (CameraX error {error})");
                break;
            default:
                if (open && _interrupted)
                {
                    _interrupted = false;
                    Report(null);
                }
                break;
        }
    }

    private Task<ProcessCameraProvider> ProviderAsync()
    {
        var tcs = new TaskCompletionSource<ProcessCameraProvider>();
        var future = ProcessCameraProvider.GetInstance(_context);
        future.AddListener(new Java.Lang.Runnable(() =>
        {
            try { tcs.TrySetResult((ProcessCameraProvider)future.Get()!); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }), ContextCompat.GetMainExecutor(_context));
        return tcs.Task;
    }

    private void Unbind(bool keepLastFrame)
    {
        _watchdog?.Dispose();
        _watchdog = null;
        if (_cameraStateObserver is not null) _cameraState?.RemoveObserver(_cameraStateObserver);
        _cameraStateObserver = null;
        _cameraState = null;
        _cameraOpen = _interrupted = false;
        if (!_bound) return;
        _bound = false;
        _analysing = false;
        // Keep the last frame on screen, so a stop after a hit shows the code that was read. Only then: the copy is a
        // bitmap the size of the view, and nobody sees it after a restart or off screen
        if (keepLastFrame && !_shutdown && _preview.Bitmap is { } frame)
        {
            _still.SetImageBitmap(frame);
            _still.Visibility = Android.Views.ViewStates.Visible;
        }
        // Off before the camera closes, so a torch never outlives the scanner
        if (_cameraControl?.CameraInfo?.HasFlashUnit == true) _cameraControl.CameraControl?.EnableTorch(false);
        if (_previewCase is not null && _analysisCase is not null)
            _provider?.Unbind(_previewCase, _analysisCase);
        _analysisCase?.ClearAnalyzer();
        _previewCase = null;
        _analysisCase = null;
        _cameraControl = null;
    }

    /// <summary>Focuses and meters on a point in the preview, in device-independent units; CameraX goes back to continuous focus after five seconds.</summary>
    public void FocusAt(Point point)
    {
        if (!_bound || _cameraControl?.CameraControl is not { } control) return;
        float density = _context.Resources?.DisplayMetrics?.Density ?? 1;
        var target = _preview.MeteringPointFactory!.CreatePoint((float)point.X * density, (float)point.Y * density);
        var action = new FocusMeteringAction.Builder(target, FocusMeteringAction.FlagAf | FocusMeteringAction.FlagAe).Build();
        try { control.StartFocusAndMetering(action); }
        catch (Exception ex) { Reader.ReportError("focus", ex); }
    }

    private void ApplyTorch()
    {
        if (_cameraControl?.CameraInfo?.HasFlashUnit == true)
            _cameraControl.CameraControl?.EnableTorch(_torch);
    }

    // No frame for two seconds from an open camera: say so and bind again, rather than show a frozen preview. A camera
    // that is not open is not stuck: CameraX closes it while the app is in the background and keeps retrying one that
    // another app holds, and binding again there every two seconds would only churn
    private void CheckFrames()
    {
        var diagnostics = Reader.TakeDiagnostics();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_shutdown) return;
            DiagnosticsChanged?.Invoke(diagnostics);
            if (!_bound || !_cameraOpen || _interrupted || Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastFrame)) < FrameTimeout) return;
            Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
            Report(ScannerProblem.NoFrames);
            Unbind(keepLastFrame: false);
            Update();
        });
    }

    // Java default methods on ImageAnalysis.Analyzer; the binding does not supply them, and CameraX calls them
    public Android.Util.Size? DefaultTargetResolution => null;

    public int TargetCoordinateSystem => ImageAnalysis.CoordinateSystemOriginal;

    public void UpdateTransform(Android.Graphics.Matrix? matrix) { }

    public void Analyze(IImageProxy? image)
    {
        if (image is null) return;
        bool handedOff = false;
        try
        {
            if (_shutdown || !_analysing) return;
            Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
            if (!_healthy)
            {
                _healthy = true;
                Report(null);
            }

            long started = Stopwatch.GetTimestamp();
            int rotation = image.ImageInfo?.RotationDegrees ?? 0;
            if (Reader.WantsLightGrid && ReadLightGrid(image) is { } grid)
            {
                Reader.CountFrame(Stopwatch.GetElapsedTime(started).TotalMilliseconds, true);
                Post(Upright(grid, rotation));
                return;
            }

            var formats = Reader.Formats;
            if (formats == BarcodeFormat.None || image.Image is not { } media)
            {
                Reader.CountFrame(Stopwatch.GetElapsedTime(started).TotalMilliseconds, false);
                return;
            }

            var input = InputImage.FromMediaImage(media, image.ImageInfo?.RotationDegrees ?? 0);
            handedOff = true;
            // ML Kit reads the frame asynchronously; the frame is closed when it is done
            MlKitFor(formats).Process(input).AddOnCompleteListener(AnalysisThread.Value, new CompleteListener(task =>
            {
                bool read = false;
                try
                {
                    if (task.IsSuccessful && task.Result is { } list)
                        foreach (var item in list.JavaCast<JavaList>()!)
                            if ((item as MLBarcode ?? (item as Java.Lang.Object)?.JavaCast<MLBarcode>()) is { RawValue: { Length: > 0 } value } barcode
                                && FromMlKit(barcode.Format) is var format && format != BarcodeFormat.None)
                            {
                                // ML Kit gives corners in the upright image, the frame turned by its rotation
                                bool turned = rotation % 180 != 0;
                                var points = barcode.GetCornerPoints() ?? [];
                                Post(new FrameHit(new BarcodeScanResult(value, format),
                                    [.. points.Select(p => new System.Numerics.Vector2(p.X, p.Y))],
                                    turned ? image.Height : image.Width, turned ? image.Width : image.Height));
                                read = true;
                                break;
                            }
                }
                finally
                {
                    Reader.CountFrame(Stopwatch.GetElapsedTime(started).TotalMilliseconds, read);
                    image.Close();
                }
            }));
        }
        catch (Exception ex)
        {
            Reader.ReportError("frame", ex);
            // One bad frame is a miss; keep going, and say so once
            if (_healthy)
            {
                _healthy = false;
                Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} ({ex.Message})");
            }
        }
        finally
        {
            if (!handedOff) image.Close();
        }
    }

    /// <summary>Turns a hit in the sensor's frame into the upright image ML Kit and the preview use.</summary>
    private static FrameHit Upright(FrameHit hit, int rotation)
    {
        float w = hit.ImageWidth, h = hit.ImageHeight;
        var corners = hit.Corners.Select(c => rotation switch
        {
            90 => new System.Numerics.Vector2(h - c.Y, c.X),
            180 => new System.Numerics.Vector2(w - c.X, h - c.Y),
            270 => new System.Numerics.Vector2(c.Y, w - c.X),
            _ => c,
        }).ToArray();
        bool turned = rotation % 180 != 0;
        return hit with { Corners = corners, ImageWidth = turned ? h : w, ImageHeight = turned ? w : h };
    }

    private FrameHit? ReadLightGrid(IImageProxy image)
    {
        // The Y plane is the luminance the reader wants; rows are padded to the row stride
        var plane = image.GetPlanes()![0];
        var buffer = plane.Buffer!;
        int length = buffer.Remaining();
        if (_luminance.Length < length) _luminance = new byte[length];
        buffer.Rewind();
        buffer.Get(_luminance, 0, length);
        return Reader.ReadLightGrid(_luminance.AsSpan(0, length), image.Width, image.Height, plane.RowStride);
    }

    private IBarcodeScanner MlKitFor(BarcodeFormat formats)
    {
        if (_mlKit is not null && formats == _mlKitFormats) return _mlKit;
        _mlKit?.Close();
        var codes = ToMlKit(formats);
        var options = new BarcodeScannerOptions.Builder().SetBarcodeFormats(codes[0], codes[1..]).Build();
        _mlKitFormats = formats;
        return _mlKit = BarcodeScanning.GetClient(options);
    }

    private void Post(FrameHit hit) =>
        MainThread.BeginInvokeOnMainThread(() => { if (!_shutdown) Detected?.Invoke(Place(hit)); });

    private void Report(ScannerProblem? problem, string? message = null)
    {
        var text = message ?? (problem is { } p ? ScannerStrings.For(p) : null);
        MainThread.BeginInvokeOnMainThread(() => { if (!_shutdown) Problem?.Invoke(problem, text); });
    }

    private static int[] ToMlKit(BarcodeFormat formats)
    {
        var list = new List<int>();
        void Add(BarcodeFormat f, int code) { if ((formats & f) != 0) list.Add(code); }
        Add(BarcodeFormat.QrCode, MLBarcode.FormatQrCode);
        Add(BarcodeFormat.DataMatrix, MLBarcode.FormatDataMatrix);
        Add(BarcodeFormat.Aztec, MLBarcode.FormatAztec);
        Add(BarcodeFormat.Pdf417, MLBarcode.FormatPdf417);
        Add(BarcodeFormat.Code128, MLBarcode.FormatCode128);
        Add(BarcodeFormat.Code39, MLBarcode.FormatCode39);
        Add(BarcodeFormat.Code93, MLBarcode.FormatCode93);
        Add(BarcodeFormat.Ean13, MLBarcode.FormatEan13);
        Add(BarcodeFormat.Ean8, MLBarcode.FormatEan8);
        Add(BarcodeFormat.UpcA, MLBarcode.FormatUpcA);
        Add(BarcodeFormat.UpcE, MLBarcode.FormatUpcE);
        Add(BarcodeFormat.Itf, MLBarcode.FormatItf);
        Add(BarcodeFormat.Codabar, MLBarcode.FormatCodabar);
        return list.Count > 0 ? [.. list] : [MLBarcode.FormatAllFormats];
    }

    private static BarcodeFormat FromMlKit(int format) => format switch
    {
        MLBarcode.FormatQrCode => BarcodeFormat.QrCode,
        MLBarcode.FormatDataMatrix => BarcodeFormat.DataMatrix,
        MLBarcode.FormatAztec => BarcodeFormat.Aztec,
        MLBarcode.FormatPdf417 => BarcodeFormat.Pdf417,
        MLBarcode.FormatCode128 => BarcodeFormat.Code128,
        MLBarcode.FormatCode39 => BarcodeFormat.Code39,
        MLBarcode.FormatCode93 => BarcodeFormat.Code93,
        MLBarcode.FormatEan13 => BarcodeFormat.Ean13,
        MLBarcode.FormatEan8 => BarcodeFormat.Ean8,
        MLBarcode.FormatUpcA => BarcodeFormat.UpcA,
        MLBarcode.FormatUpcE => BarcodeFormat.UpcE,
        MLBarcode.FormatItf => BarcodeFormat.Itf,
        MLBarcode.FormatCodabar => BarcodeFormat.Codabar,
        _ => BarcodeFormat.None,
    };

    private sealed class CompleteListener(Action<Android.Gms.Tasks.Task> done) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task task) => done(task);
    }

    private sealed class CameraStateObserver(ScannerCamera owner) : Java.Lang.Object, IObserver
    {
        public void OnChanged(Java.Lang.Object? value)
        {
            if (value?.JavaCast<CameraState>() is { } state) owner.OnCameraState(state);
        }
    }
}
#endif
