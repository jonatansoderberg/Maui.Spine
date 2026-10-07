#if IOS || MACCATALYST
using System.Diagnostics;
using AVFoundation;
using CoreAnimation;
using CoreFoundation;
using CoreGraphics;
using CoreMedia;
using CoreVideo;
using Foundation;
using ImageIO;
using Microsoft.Maui.Handlers;
using Plugin.Maui.Spine.Barcodes;
using UIKit;
using Vision;

namespace Plugin.Maui.Spine.Scanner;

public sealed class BarcodeScannerViewHandler() : ViewHandler<BarcodeScannerView, ScannerPreviewView>(Mapper, CommandMapper)
{
    public static readonly IPropertyMapper<BarcodeScannerView, BarcodeScannerViewHandler> Mapper =
        new PropertyMapper<BarcodeScannerView, BarcodeScannerViewHandler>(ViewMapper)
        {
            [nameof(BarcodeScannerView.Formats)] = static (h, v) => h.PlatformView.SetFormats(v.Formats),
            [nameof(BarcodeScannerView.LightGrid)] = static (h, v) => h.PlatformView.SetLightGrid(v.LightGrid),
            [nameof(BarcodeScannerView.IsScanning)] = static (h, v) => h.PlatformView.SetWanted(v.IsScanning),
            [nameof(BarcodeScannerView.IsTorchOn)] = static (h, v) => h.PlatformView.SetTorch(v.IsTorchOn),
        };

    public static readonly CommandMapper<BarcodeScannerView, BarcodeScannerViewHandler> CommandMapper = new(ViewCommandMapper)
    {
        [BarcodeScannerView.FocusCommand] = static (h, _, arg) => { if (arg is Point point) h.PlatformView.FocusAt(point); },
    };

    protected override ScannerPreviewView CreatePlatformView() => new();

    protected override void ConnectHandler(ScannerPreviewView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Detected = (codes, frame) => VirtualView?.RaiseFrame(codes, frame);
        platformView.Problem = (p, m) => VirtualView?.RaiseProblem(p, m);
        platformView.TorchAvailable = a => VirtualView?.SetTorchAvailable(a);
        platformView.DiagnosticsChanged = d => VirtualView?.SetDiagnostics(d);
        platformView.TorchSwitchedOff = () => VirtualView?.SetTorchOff();
        platformView.SetOnScreen(platformView.Window is not null);
    }

    protected override void DisconnectHandler(ScannerPreviewView platformView)
    {
        platformView.Shutdown();
        base.DisconnectHandler(platformView);
    }
}

/// <summary>
/// The camera behind <see cref="BarcodeScannerView"/> on iOS and Mac Catalyst. Frames arrive on a serial queue of
/// their own as bi-planar full-range YUV: Vision reads the standard codes from the pixel buffer, and the light-grid reader
/// reads the Y plane, which is luminance as it is.
/// </summary>
/// <remarks>
/// The session is taken apart when the view leaves its window, not only in <see cref="Shutdown"/>: a host that never
/// disconnects the handlers of a closed sheet or popup would otherwise keep a configured session, its observers and the
/// frame delegate (which holds this view) for every scan.
/// </remarks>
public sealed class ScannerPreviewView : UIView
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);
    private static readonly CGPoint Centre = new(0.5, 0.5);

    private readonly AVCaptureSession _session = new();
    private readonly AVCaptureVideoPreviewLayer _previewLayer;
    // Frames keep their queue busy back to back while a frame takes as long as the camera's frame interval, and
    // anything queued behind them (the torch, focus, zoom) then waits for seconds; the camera is driven from its own
    private readonly DispatchQueue _queue = new("spine.scanner.session");
    private readonly DispatchQueue _frameQueue = new("spine.scanner.frames");
    private readonly List<NSObject> _observers = [];
    private AVCaptureDevice? _device;
    private FrameDelegate? _frames;
    private NSTimer? _watchdog;
    private bool _configured, _configuring, _wanted = true, _onScreen;
    private volatile bool _torch, _shutdown, _interrupted;
    private int _torchQueued;
    // Main thread only: moves on with every release, so a configuration that finishes after one is known to be stale
    private int _generation;
    private long _lastFrame;
    private int _processing;

    internal readonly FrameReader Reader = new();
    internal Action<IReadOnlyList<BarcodeScanResult>, long>? Detected;

    /// <summary>Places a hit's corners, in buffer pixels, in this view; main thread only.</summary>
    private BarcodeScanResult Place(FrameHit hit)
    {
        // The buffer is in the sensor's orientation, which is the space of the device point of interest
        var corners = hit.Corners
            .Select(c => _previewLayer.PointForCaptureDevicePointOfInterest(new CGPoint(c.X / hit.ImageWidth, c.Y / hit.ImageHeight)))
            .Select(p => new Point(p.X, p.Y))
            .ToArray();
        return hit.Result with { Corners = corners };
    }
    internal Action<ScannerProblem?, string?>? Problem;
    internal Action<bool>? TorchAvailable;
    internal Action? TorchSwitchedOff;
    internal Action<string?>? DiagnosticsChanged;

    public ScannerPreviewView()
    {
        BackgroundColor = UIColor.Black;
        _previewLayer = new AVCaptureVideoPreviewLayer(_session) { VideoGravity = AVLayerVideoGravity.ResizeAspectFill };
        Layer.AddSublayer(_previewLayer);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        // A sublayer's frame animates by itself, so the picture trailed a growing sheet and left a black band under it
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _previewLayer.Frame = Bounds;
        CATransaction.Commit();
    }

    // The one signal that always comes: a sheet can close without its page or MAUI saying so,
    // and a camera left running there fights the next scanner for the device
    public override void MovedToWindow()
    {
        base.MovedToWindow();
        SetOnScreen(Window is not null);
    }

    internal void SetWanted(bool wanted)
    {
        _wanted = wanted;
        Update();
    }

    internal void SetOnScreen(bool onScreen)
    {
        _onScreen = onScreen;
        Update();
    }

    internal void SetFormats(BarcodeFormat formats)
    {
        Reader.Formats = formats;
        _queue.DispatchAsync(ApplyLens);
    }

    internal void SetLightGrid(LightGridOptions? options)
    {
        Reader.SetLightGrid(options);
        _queue.DispatchAsync(ApplyLens);
    }

    /// <summary>Focuses and exposes on a point in this view until the scene changes; main thread only.</summary>
    internal void FocusAt(Point point)
    {
        if (_shutdown || !_configured) return;
        var target = _previewLayer.CaptureDevicePointOfInterestForPoint(new CGPoint(point.X, point.Y));
        _queue.DispatchAsync(() => Focus(target, AVCaptureFocusMode.AutoFocus, AVCaptureExposureMode.AutoExpose, watchScene: true));
    }

    internal void SetTorch(bool on)
    {
        _torch = on;
        QueueTorch();
    }

    // Switching the torch locks the camera and can wait for the session, so it runs on the session's queue,
    // never the main thread; taps that arrive while one is pending fold into it and only the last wish is applied
    private void QueueTorch()
    {
        if (Interlocked.Exchange(ref _torchQueued, 1) == 1) return;
        _queue.DispatchAsync(() =>
        {
            Volatile.Write(ref _torchQueued, 0);
            ApplyTorch(_torch && !_shutdown);
        });
    }

    /// <summary>Stops the camera for good and lets go of every buffer; the view is not reused after this.</summary>
    internal void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        Release();
    }

    /// <summary>
    /// Main thread. Takes the session apart and drops the observers; the next <see cref="Update"/> configures afresh. The
    /// work on the session runs on its queue, after anything already queued there, a configuration included.
    /// </summary>
    private void Release()
    {
        _generation++;
        StopWatchdog();
        foreach (var o in _observers) o.Dispose();
        _observers.Clear();
        _configured = false;
        _interrupted = false;
        _frames = null;
        _queue.DispatchAsync(() =>
        {
            ApplyTorch(false);
            if (_session.Running) _session.StopRunning();
            _session.BeginConfiguration();
            ClearSession();
            _session.CommitConfiguration();
            _device = null;
        });
    }

    /// <summary>Session queue, inside a configuration. Removes every output (and its delegate) and input.</summary>
    private void ClearSession()
    {
        foreach (var output in _session.Outputs)
        {
            // The output holds the delegate, and the delegate holds this view
            if (output is AVCaptureVideoDataOutput video) video.SetSampleBufferDelegate(null, null);
            _session.RemoveOutput(output);
        }
        foreach (var input in _session.Inputs) _session.RemoveInput(input);
    }

    // The property follows the lamp: a stop is the end of the torch, and the next start begins without it. A watchdog
    // restart does not come through here, so the torch survives that
    private void SwitchTorchOff()
    {
        if (!_torch) return;
        _torch = false;
        TorchSwitchedOff?.Invoke();
    }

    private async void Update()
    {
        if (_shutdown) return;
        bool run = _wanted && _onScreen;
        if (!run)
        {
            SwitchTorchOff();
            if (!_onScreen)
            {
                Release();
                return;
            }
            StopWatchdog();
            // The preview keeps the last frame, so a stop after a hit shows the code that was read
            if (_previewLayer.Connection is { } connection) connection.Enabled = false;
            _queue.DispatchAsync(() =>
            {
                ApplyTorch(false);
                if (_session.Running) _session.StopRunning();
            });
            return;
        }

        if (!_configured)
        {
            if (_configuring) return;
            _configuring = true;
            int generation = _generation;
            bool configured = false;
            try { configured = await ConfigureAsync(); }
            catch (Exception ex) { Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} ({ex.Message})"); }
            finally { _configuring = false; }

            // Released while the permission prompt or the configuration was pending: what was set up may already have
            // been taken apart, or may have been set up after the release. Either way start clean
            if (generation != _generation || _shutdown || !_onScreen)
            {
                Release();
                if (!_shutdown && _onScreen) Update();
                return;
            }
            _configured = configured;
            if (!_configured || !_wanted) return;
        }

        Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
        if (_previewLayer.Connection is { } live) live.Enabled = true;
        _queue.DispatchAsync(() =>
        {
            if (!_session.Running) _session.StartRunning();
            ApplyTorch(_torch && !_shutdown);
        });
        StartWatchdog();
    }

    private async Task<bool> ConfigureAsync()
    {
        // Asking without the usage string kills the app with no message; say what is missing instead
        if (NSBundle.MainBundle.ObjectForInfoDictionary("NSCameraUsageDescription") is null)
        {
            Report(ScannerProblem.Failed, "Add NSCameraUsageDescription to Info.plist to use the camera.");
            return false;
        }
        if (!await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video))
        {
            Report(ScannerProblem.PermissionDenied);
            return false;
        }

        var device = AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInWideAngleCamera, AVMediaTypes.Video, AVCaptureDevicePosition.Back)
            ?? AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (device is null)
        {
            Report(ScannerProblem.NoCamera);
            return false;
        }

        // On the session queue like every other change to the session, so a release queued before it cannot take apart
        // what it sets up afterwards
        var frames = new FrameDelegate(this);
        var failure = await OnSessionQueue(() => Configure(device, frames));
        if (failure is not null)
        {
            Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} ({failure})");
            return false;
        }
        _frames = frames;

        _queue.DispatchAsync(() =>
        {
            Focus(Centre, AVCaptureFocusMode.ContinuousAutoFocus, AVCaptureExposureMode.ContinuousAutoExposure, watchScene: false);
            ApplyLens();
        });
        // After the permission prompt this may run off the main thread; the header only follows main-thread changes
        bool hasTorch = device.HasTorch;
        BeginInvokeOnMainThread(() => { if (!_shutdown) TorchAvailable?.Invoke(hasTorch); });

        _observers.Add(AVCaptureSession.Notifications.ObserveRuntimeError(_session, (_, e) =>
            Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} ({e.Error?.LocalizedDescription})")));
        // While another app or a call has the camera no frames come, and that is not a stuck camera: the watchdog
        // leaves it alone, so the explanation stays on screen and the session is not restarted under the interruption
        _observers.Add(AVCaptureSession.Notifications.ObserveWasInterrupted(_session, (_, _) =>
        {
            _interrupted = true;
            Report(ScannerProblem.Interrupted);
        }));
        _observers.Add(AVCaptureSession.Notifications.ObserveInterruptionEnded(_session, (_, _) =>
        {
            Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
            _interrupted = false;
            Report(null);
        }));
        // After a tap, the phone moving on hands focus back to the camera, as the Camera app does
        _observers.Add(AVCaptureDevice.Notifications.ObserveSubjectAreaDidChange(device, (_, _) => _queue.DispatchAsync(() =>
            Focus(Centre, AVCaptureFocusMode.ContinuousAutoFocus, AVCaptureExposureMode.ContinuousAutoExposure, watchScene: false))));
        return true;
    }

    /// <summary>Session queue. Input and output for <paramref name="device"/>; the reason when it cannot be done.</summary>
    private string? Configure(AVCaptureDevice device, FrameDelegate frames)
    {
        _session.BeginConfiguration();
        try
        {
            // Whatever a configuration that failed half-way left behind
            ClearSession();
            // Enough detail for a 12 × 12 grid across a room, small enough to read every frame
            _session.SessionPreset = _session.CanSetSessionPreset(AVCaptureSession.Preset1280x720)
                ? AVCaptureSession.Preset1280x720
                : AVCaptureSession.PresetHigh;
            var input = AVCaptureDeviceInput.FromDevice(device, out var error);
            if (input is null || !_session.CanAddInput(input))
                return error?.LocalizedDescription ?? "the camera input could not be added";
            _session.AddInput(input);

            var output = new AVCaptureVideoDataOutput
            {
                AlwaysDiscardsLateVideoFrames = true,
                WeakVideoSettings = new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange }.Dictionary,
            };
            output.SetSampleBufferDelegate(frames, _frameQueue);
            if (!_session.CanAddOutput(output))
                return "the camera output could not be added";
            _session.AddOutput(output);
            _device = device;
            return null;
        }
        finally
        {
            _session.CommitConfiguration();
        }
    }

    private Task<T> OnSessionQueue<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.DispatchAsync(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    /// <summary>Runs on the session queue only.</summary>
    private void ApplyTorch(bool on)
    {
        if (_device is not { HasTorch: true } device || !_session.Running) return;
        var mode = on ? AVCaptureTorchMode.On : AVCaptureTorchMode.Off;
        if (device.TorchMode == mode) return;
        try
        {
            if (!device.LockForConfiguration(out var error))
            {
                Reader.ReportError("torch", new InvalidOperationException(error?.LocalizedDescription ?? "the camera is locked"));
                return;
            }
            if (device.IsTorchModeSupported(mode)) device.TorchMode = mode;
            device.UnlockForConfiguration();
        }
        catch (Exception ex)
        {
            Reader.ReportError("torch", ex);
        }
    }

    /// <summary>Runs on the session queue only. Focus and exposure at <paramref name="point"/>, in device point-of-interest space.</summary>
    private void Focus(CGPoint point, AVCaptureFocusMode focus, AVCaptureExposureMode exposure, bool watchScene)
    {
        if (_device is not { } device || _shutdown) return;
        try
        {
            if (!device.LockForConfiguration(out var error))
            {
                Reader.ReportError("focus", new InvalidOperationException(error?.LocalizedDescription ?? "the camera is locked"));
                return;
            }
            // The point only takes effect when the mode is set after it
            if (device.FocusPointOfInterestSupported) device.FocusPointOfInterest = point;
            if (device.IsFocusModeSupported(focus)) device.FocusMode = focus;
            if (device.ExposurePointOfInterestSupported) device.ExposurePointOfInterest = point;
            if (device.IsExposureModeSupported(exposure)) device.ExposureMode = exposure;
            device.SubjectAreaChangeMonitoringEnabled = watchScene;
            device.UnlockForConfiguration();
        }
        catch (Exception ex)
        {
            Reader.ReportError("focus", ex);
        }
    }

    /// <summary>
    /// Runs on the session queue only. Linear codes are scanned up close, so autofocus is kept to near distances for
    /// them. Unless a light grid is read across a room, the camera zooms in by as much as it cannot focus close.
    /// </summary>
    private void ApplyLens()
    {
        if (_device is not { } device || _shutdown) return;
        var formats = Reader.Formats;
        bool lightGrid = Reader.WantsLightGrid;
        bool linearOnly = !lightGrid && formats != BarcodeFormat.None && (formats & ~BarcodeFormat.OneDimensional) == 0;
        var range = linearOnly ? AVCaptureAutoFocusRangeRestriction.Near : AVCaptureAutoFocusRangeRestriction.None;
        nfloat zoom = lightGrid ? 1 : CloseUpZoom(device);
        try
        {
            if (!device.LockForConfiguration(out var error))
            {
                Reader.ReportError("lens", new InvalidOperationException(error?.LocalizedDescription ?? "the camera is locked"));
                return;
            }
            if (device.AutoFocusRangeRestrictionSupported) device.AutoFocusRangeRestriction = range;
            device.VideoZoomFactor = zoom;
            device.UnlockForConfiguration();
            Reader.Zoom = (float)zoom;
        }
        catch (Exception ex)
        {
            Reader.ReportError("lens", ex);
        }
    }

    /// <summary>
    /// The zoom at which an EAN-13 (37 mm) held at the camera's closest focus distance spans half the preview's width.
    /// The wide camera of a Pro iPhone focuses no closer than about 20 cm, where a product code is small; zoomed in,
    /// the user holds the phone where it is sharp and the code still looks big. 1 for a camera that focuses close.
    /// </summary>
    private static nfloat CloseUpZoom(AVCaptureDevice device)
    {
        const double codeWidth = 37, share = 0.5, most = 3;
        double closest = device.MinimumFocusDistance;
        var format = device.ActiveFormat;
        var size = (format.FormatDescription as CMVideoFormatDescription)?.Dimensions;
        if (closest <= 0 || size is not { Width: > 0, Height: > 0 } dims) return 1;
        // The field of view is across the frame's long side; held upright, the preview's width is the short side
        double halfAcross = Math.Tan(format.VideoFieldOfView * Math.PI / 360) * Math.Min(dims.Width, dims.Height) / Math.Max(dims.Width, dims.Height);
        double across = 2 * closest * halfAcross;
        double zoom = Math.Clamp(across * share / codeWidth, 1, Math.Min(most, format.VideoMaxZoomFactor));
        return zoom < 1.05 ? 1 : (nfloat)zoom;
    }

    // No frame for two seconds: say so and restart, rather than show a frozen preview
    private void StartWatchdog()
    {
        _watchdog ??= NSTimer.CreateRepeatingScheduledTimer(0.5, _ =>
        {
            DiagnosticsChanged?.Invoke(Reader.TakeDiagnostics());
            // A frame still being read is slow, not stuck, and an interrupted camera is held by someone else; only a
            // silent camera on screen is restarted
            if (Window is null || !_wanted || _interrupted || Volatile.Read(ref _processing) != 0
                || Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastFrame)) < FrameTimeout) return;
            Report(ScannerProblem.NoFrames);
            Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
            _queue.DispatchAsync(() =>
            {
                if (_session.Running) _session.StopRunning();
                _session.StartRunning();
                ApplyTorch(_torch && !_shutdown);
            });
        });
    }

    private void StopWatchdog()
    {
        _watchdog?.Invalidate();
        _watchdog = null;
    }

    private void Report(ScannerProblem? problem, string? message = null)
    {
        var text = message ?? (problem is { } p ? ScannerStrings.For(p) : null);
        BeginInvokeOnMainThread(() => { if (!_shutdown) Problem?.Invoke(problem, text); });
    }

    private sealed class FrameDelegate(ScannerPreviewView owner) : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        private readonly VNDetectBarcodesRequest _barcodes = new((_, _) => { });
        private BarcodeFormat _requested = BarcodeFormat.None;
        private bool _healthy = true;

        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            try
            {
                if (owner._shutdown) return;
                Interlocked.Exchange(ref owner._lastFrame, Stopwatch.GetTimestamp());
                Volatile.Write(ref owner._processing, 1);

                using var pixels = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
                if (pixels is null) return;
                long frame = owner.Reader.NextFrame();
                long started = Stopwatch.GetTimestamp();

                var formats = owner.Reader.Formats;
                List<FrameHit>? hits = null;
                // Its own guard: a Vision failure must not keep the light-grid reader from the frame
                if (formats != BarcodeFormat.None)
                {
                    try { hits = ReadStandard(pixels, formats); }
                    catch (Exception ex) { owner.Reader.ReportError("Vision", ex); }
                }

                if (hits is null && owner.Reader.WantsLightGrid)
                {
                    pixels.Lock(CVPixelBufferLock.ReadOnly);
                    try
                    {
                        if (owner.Reader.ReadLightGrid(Plane(pixels, out int w, out int h, out int stride), w, h, stride) is { } grid)
                            hits = [grid];
                    }
                    finally
                    {
                        pixels.Unlock(CVPixelBufferLock.ReadOnly);
                    }
                }

                owner.Reader.CountFrame(Stopwatch.GetElapsedTime(started).TotalMilliseconds, hits is not null);
                if (!_healthy)
                {
                    _healthy = true;
                    owner.Report(null);
                }
                if (hits is not null)
                    owner.BeginInvokeOnMainThread(() => { if (!owner._shutdown) owner.Detected?.Invoke(hits.ConvertAll(owner.Place), frame); });
            }
            catch (Exception ex)
            {
                owner.Reader.ReportError("frame", ex);
                // One bad frame is a miss; keep going, and say so once
                if (_healthy)
                {
                    _healthy = false;
                    owner.Report(ScannerProblem.Failed, $"{ScannerStrings.For(ScannerProblem.Failed)} ({ex.Message})");
                }
            }
            finally
            {
                Volatile.Write(ref owner._processing, 0);
                // Holding a sample buffer stalls the capture pipeline after a few frames
                sampleBuffer.Dispose();
            }
        }

        /// <summary>Every code Vision found in the frame; <see langword="null"/> when there was none.</summary>
        private List<FrameHit>? ReadStandard(CVPixelBuffer pixels, BarcodeFormat formats)
        {
            if (formats != _requested)
            {
                _barcodes.Symbologies = ToVision(formats);
                _requested = formats;
            }
            // The buffer's own orientation: codes read the same either way, and the corners stay in buffer space
            using var handler = new VNImageRequestHandler(pixels, CGImagePropertyOrientation.Up, new VNImageOptions());
            if (!handler.Perform([_barcodes], out var error))
                throw new InvalidOperationException(error?.LocalizedDescription ?? "Vision failed");
            float w = pixels.Width, h = pixels.Height;
            // Vision's points are normalised with the origin at the bottom left
            System.Numerics.Vector2 Pixel(CGPoint p) => new((float)p.X * w, (1 - (float)p.Y) * h);
            List<FrameHit>? hits = null;
            foreach (var observation in _barcodes.GetResults<VNBarcodeObservation>() ?? [])
                if (observation.PayloadStringValue is { Length: > 0 } value && FromVision(observation.Symbology) is var format && format != BarcodeFormat.None)
                    (hits ??= []).Add(new FrameHit(new BarcodeScanResult(value, format),
                        [Pixel(observation.TopLeft), Pixel(observation.TopRight), Pixel(observation.BottomRight), Pixel(observation.BottomLeft)], w, h));
            return hits;
        }

        private static unsafe ReadOnlySpan<byte> Plane(CVPixelBuffer pixels, out int width, out int height, out int stride)
        {
            width = (int)pixels.GetWidthOfPlane(0);
            height = (int)pixels.GetHeightOfPlane(0);
            stride = (int)pixels.GetBytesPerRowOfPlane(0);
            return new ReadOnlySpan<byte>((void*)pixels.GetBaseAddress(0), stride * height);
        }
    }

    private static VNBarcodeSymbology[] ToVision(BarcodeFormat formats)
    {
        var list = new List<VNBarcodeSymbology>();
        void Add(BarcodeFormat f, params VNBarcodeSymbology[] s) { if ((formats & f) != 0) list.AddRange(s); }
        Add(BarcodeFormat.QrCode, VNBarcodeSymbology.QR);
        Add(BarcodeFormat.DataMatrix, VNBarcodeSymbology.DataMatrix);
        Add(BarcodeFormat.Aztec, VNBarcodeSymbology.Aztec);
        Add(BarcodeFormat.Pdf417, VNBarcodeSymbology.Pdf417);
        Add(BarcodeFormat.Code128, VNBarcodeSymbology.Code128);
        Add(BarcodeFormat.Code39, VNBarcodeSymbology.Code39, VNBarcodeSymbology.Code39FullAscii);
        Add(BarcodeFormat.Code93, VNBarcodeSymbology.Code93);
        // Vision reports UPC-A as EAN-13 with a leading zero
        Add(BarcodeFormat.Ean13 | BarcodeFormat.UpcA, VNBarcodeSymbology.Ean13);
        Add(BarcodeFormat.Ean8, VNBarcodeSymbology.Ean8);
        Add(BarcodeFormat.UpcE, VNBarcodeSymbology.Upce);
        Add(BarcodeFormat.Itf, VNBarcodeSymbology.I2OF5, VNBarcodeSymbology.Itf14);
        Add(BarcodeFormat.Codabar, VNBarcodeSymbology.Codabar);
        return [.. list.Distinct()];
    }

    private static BarcodeFormat FromVision(VNBarcodeSymbology symbology) => symbology switch
    {
        VNBarcodeSymbology.QR => BarcodeFormat.QrCode,
        VNBarcodeSymbology.DataMatrix => BarcodeFormat.DataMatrix,
        VNBarcodeSymbology.Aztec => BarcodeFormat.Aztec,
        VNBarcodeSymbology.Pdf417 => BarcodeFormat.Pdf417,
        VNBarcodeSymbology.Code128 => BarcodeFormat.Code128,
        VNBarcodeSymbology.Code39 or VNBarcodeSymbology.Code39FullAscii => BarcodeFormat.Code39,
        VNBarcodeSymbology.Code93 => BarcodeFormat.Code93,
        VNBarcodeSymbology.Ean13 => BarcodeFormat.Ean13,
        VNBarcodeSymbology.Ean8 => BarcodeFormat.Ean8,
        VNBarcodeSymbology.Upce => BarcodeFormat.UpcE,
        VNBarcodeSymbology.I2OF5 or VNBarcodeSymbology.Itf14 => BarcodeFormat.Itf,
        VNBarcodeSymbology.Codabar => BarcodeFormat.Codabar,
        _ => BarcodeFormat.None,
    };
}
#endif
