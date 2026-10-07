# Barcodes and scanning

```bash
dotnet add package Plugin.Maui.Spine.Barcodes   # draw codes
dotnet add package Plugin.Maui.Spine.Scanner    # read them with the camera
```

Two packages, split so that an app that only shows codes needs no camera permission and no ML Kit:

- **`Plugin.Maui.Spine.Barcodes`** encodes QR, Data Matrix, Aztec, PDF417 and the common linear codes to a `BarcodeMatrix`, an SVG or a `BarcodeView`. A symbol can be held to an exact size, such as a 12 × 12 Data Matrix for a 12 × 12 display. `LightGridReader` reads a code shown by a grid of lamps. The codec targets `net10.0` too, so a server or a test can use it.
- **`Plugin.Maui.Spine.Scanner`** reads codes with the camera: a `BarcodeScannerView` and a ready-made scan sheet, `BarcodeScannerPage`. Vision reads the standard codes on iOS and Mac Catalyst, ML Kit on Android, and the light-grid reader runs next to them when asked.

Encoding is built on [ZXing.Net](https://github.com/micjahn/ZXing.Net) (Apache-2.0), behind Spine's own types. The Showcase app's Barcodes page has a generator, a 12 × 12 word clock that shows a pairing code, and the scan sheet.

---

## Platforms

| Platform | Barcodes | Scanner |
|---|---|---|
| iOS | ✅ `BarcodeView`, encoding, `LightGridReader` | ✅ AVFoundation + Vision |
| Mac Catalyst | ✅ | ✅ AVFoundation + Vision, exercised less than iOS |
| Android | ✅ | ✅ API 23+: CameraX + ML Kit (bundled model) |
| Windows | ✅ | ❌ No handler yet |
| `net10.0` (server, tests) | ✅ Encoding and `LightGridReader`; no `BarcodeView` | — |

Neither package needs a registration call of its own: Barcodes has nothing to register, and `UseSpine()` registers the Scanner (see [Scanning](#scanning)).

---

## Generating

```csharp
var qr = Barcode.Encode("https://example.com", BarcodeFormat.QrCode);

for (int y = 0; y < qr.Height; y++)
    for (int x = 0; x < qr.Width; x++)
        if (qr[x, y]) FillModule(x, y);
```

`Barcode.Encode(value, format)` uses the format's default options; `Barcode.Encode(value, options)` takes one of the options records below. Both return a `BarcodeMatrix`:

| Member | Purpose |
|---|---|
| `Width`, `Height` | The symbol in modules, without the quiet zone |
| `this[x, y]` | `true` for a dark module: a bar, or a lit lamp on a display that shows the code as light |
| `QuietZone` | The light modules to leave around the code when drawing it; not part of `Width` or `Height` |
| `IsLinear` | A linear code: one row, drawn as bars stretched to any height |
| `ToSvg(foreground, background, linearHeight)` | The code as an SVG document, one unit per module, quiet zone included |
| `ToString()` | The modules as `#` and `.`, one line per row, for a test or a log |

A value the format cannot carry, or one that does not fit the requested size, throws `BarcodeEncodingException`. Its message names the format, the length and the size: *Cannot encode 11 characters as DataMatrix 12 × 12: …*.

### Formats

| `BarcodeFormat` | What it holds | Default quiet zone |
|---|---|---|
| `QrCode` | Any text; 21 × 21 (version 1) to 177 × 177 (version 40) | 4 |
| `DataMatrix` | Any text; square from 10 × 10, or rectangular | 1 |
| `Aztec` | Any text; 15 × 15 and up | 0 |
| `Pdf417` | Any text, as stacked rows | 2 |
| `Code128` | Full ASCII | 10 |
| `Code39` | Digits, upper-case letters and a few symbols | 10 |
| `Code93` | The Code 39 set, denser | 10 |
| `Ean13` | 12 digits; the check digit is added (13 digits are checked) | 10 |
| `Ean8` | 7 digits and a check digit | 10 |
| `UpcA` | 11 digits and a check digit | 10 |
| `UpcE` | 7 digits (the number system and six more), the zero-suppressed UPC | 10 |
| `Itf` | An even number of digits (Interleaved 2 of 5) | 10 |
| `Codabar` | Digits and `-$:/.+`, framed by A–D | 10 |

`BarcodeFormat` is a flags enum: encoding takes exactly one flag, a scanner takes a combination. `TwoDimensional`, `OneDimensional` and `All` are the ready-made combinations.

Text outside ASCII needs `CharacterSet = "UTF-8"` on the options for QR; its default is ISO-8859-1.

### Options

| Record | Settings |
|---|---|
| `BarcodeOptions` | `Format`, `QuietZone` (`null` for the standard's minimum), `CharacterSet`; the linear codes use it as it is |
| `QrCodeOptions` | `ErrorCorrection` (`Low` ~7 %, `Medium` ~15 %, `Quartile` ~25 %, `High` ~30 %; default `Medium`), `Version` (1–40, `null` for the smallest that fits) |
| `DataMatrixOptions` | `Shape` (`Square` by default, `Rectangle`, `Any`), `Size` (an exact size, see below) |
| `AztecOptions` | `ErrorCorrectionPercent` (default 33; the standard recommends 23 or more), `Layers` (negative for the compact form) |
| `Pdf417Options` | `ErrorCorrectionLevel` (0–8, default 2), `Compact` |

`Barcode.DefaultOptions(format)` and `Barcode.DefaultQuietZone(format)` return what `Encode(value, format)` uses.

### Fixed sizes

By default an encoder picks the smallest symbol that holds the value. A display with a fixed number of lamps needs one exact size, so `DataMatrixOptions.Size` fixes it, and a value that does not fit throws instead of growing the symbol:

```csharp
var code = Barcode.Encode("4711081542", new DataMatrixOptions { Size = (12, 12) });
```

What fits in the square sizes, as the package encodes them:

| Size | Digits | Letters of one case, with digits |
|---|---|---|
| 10 × 10 | 6 | 3 |
| 12 × 12 | 10 | 6 |
| 14 × 14 | 16 | 10 |
| 16 × 16 | 24 | 16 |
| 18 × 18 | 36 | 25 |
| 20 × 20 | 44 | 31 |
| 22 × 22 | 60 | 43 |
| 24 × 24 | 72 | 52 |
| 26 × 26 | 88 | 64 |

A value that mixes letters and runs of digits can fit more, because digits pack two to a codeword. Binary data fits less: 12 × 12 holds 3 bytes.

`QrCodeOptions.Version` and `AztecOptions.Layers` fix the size of those formats the same way.

### SVG

```csharp
string svg = qr.ToSvg();                                   // black on white
string mono = qr.ToSvg(foreground: "currentColor", background: "");   // no background, ink from the host
string bars = Barcode.Encode("SPINE-128", BarcodeFormat.Code128).ToSvg(linearHeight: 30);
```

The document is one unit per module with the quiet zone included, and `shape-rendering="crispEdges"`, so it scales without seams. Each run of dark modules in a row is one path segment, which keeps the file small. A linear code is drawn `linearHeight` modules high (default 40).

---

## BarcodeView

A `GraphicsView` that draws a code. Set `Value` and `Format`, or hand it a ready `Matrix`:

```xml
<BarcodeView x:Name="Code" Value="{Binding Url}" Format="QrCode" HeightRequest="220" />
<Label Text="{Binding Error, Source={x:Reference Code}, x:DataType=BarcodeView}" TextColor="OrangeRed" />
```

| Member | Purpose |
|---|---|
| `Value` | The text to encode |
| `Format` | The symbology, with its default options; default `QrCode` |
| `Options` | A `QrCodeOptions`, `DataMatrixOptions`, …; when set, its format wins over `Format` |
| `Matrix` | The modules being drawn. Set it to draw a matrix built elsewhere; it is replaced when `Value` changes |
| `ModuleColor` | The dark modules; default black |
| `BackgroundColor` | The light modules and the quiet zone; white unless set |
| `IsInverted` | Swaps the two colours, for a light code on a dark display. Most phone cameras read it; not every scanner does |
| `ModuleShape` | `Square` (default) or `Dot`, a round dot per module; phone cameras still read dots at a normal size |
| `Error` | Read-only: why `Value` could not be encoded, or `null`. Nothing is drawn while it is set |

A 2D code keeps its aspect and centres itself; a linear code fills the width and height it is given. Without a `WidthRequest` or `HeightRequest`, a 2D code takes the height its modules need at the width it is offered. Square modules are drawn as horizontal runs without anti-aliasing, so neighbouring modules show no seams.

Keep the background light and the quiet zone clear; a scanner needs light around the code. The namespace is `Plugin.Maui.Spine.Barcodes` (assembly `Plugin.Maui.Spine.Barcodes`); add it to the app's global xmlns to use the view without a prefix.

---

## Scanning

### Setup

1. Reference `Plugin.Maui.Spine.Scanner`. `UseSpine()` registers it: the camera handlers, the scan sheet and its strings. Without Spine, call `builder.UseSpineScanner()`; the view then works, the sheet does not (it is a Spine page).
2. **iOS and Mac Catalyst:** add `NSCameraUsageDescription` to `Platforms/iOS/Info.plist` and `Platforms/MacCatalyst/Info.plist`. iOS ends an app that asks for the camera without it; the view checks first, and instead reports `ScannerProblem.Failed` with *Add NSCameraUsageDescription to Info.plist to use the camera.* A sandboxed Mac Catalyst app also needs the `com.apple.security.device.camera` entitlement.

   ```xml
   <key>NSCameraUsageDescription</key>
   <string>Scans the code on the clock to pair it.</string>
   ```

3. **Android:** API 23 or later. CameraX 1.6 declares 23, so an app below it fails the manifest merge; set `SupportedOSPlatformVersion` for Android to 23.0. The package declares `android.permission.CAMERA` itself (and `android.hardware.camera.any` as not required), so the app's manifest needs nothing. ML Kit's model is bundled, so scanning works offline from the first launch.

The view asks for the camera permission the first time it shows. On Android a permission granted later in Settings is picked up when the app comes back, without reopening the scanner (iOS restarts the app when a privacy setting changes).

### The scan sheet

`BarcodeScannerPage` is a sheet that scans until it reads one code and returns it; closing it returns no value.
By default:
- it opens at half height and can be pulled to full screen;
- the camera fills the sheet under a transparent header, with Spine's close button and, when the device has one, a torch that turns white while it is on and taps with a light haptic;
- **aim corners**, white with a soft shadow like the system code scanner, breathe outwards where to point the camera: square for 2D codes and light grids, wide when only linear codes are read. Only a code whose centre is inside them counts, with a margin of a fifth of their shorter side, and of several there the one nearest the middle, so another code elsewhere in the picture is not read by mistake; with `ShowReticle` off the whole camera counts;
- on a hit, the frame stops, the code is redrawn as accent dots in the perspective it was found in, straightens to a flat code square to the screen in the middle of the aim corners and **bursts towards the user** while the frame dims to half, with the success haptic and a short sound; the sheet closes when that has finished, about half a second later.

For a light grid the dots are the code encoded again from its value and turned to match the lamps that were lit, so each dot lands on its lamp. For a 2D code from the platform reader the code is encoded again with default options, so the dots may differ in detail from the pattern on screen.

```csharp
var scan = await navigation.NavigateToWithResultAsync<BarcodeScannerPage, BarcodeScanOptions, BarcodeScanResult>(
    new BarcodeScanOptions
    {
        Formats = BarcodeFormat.QrCode,
        LightGrid = new LightGridOptions(12, 12),
    });

if (scan is { IsSuccess: true, Value: { } code })
    await PairAsync(code.Value);
```

| `BarcodeScanOptions` | Purpose |
|---|---|
| `Formats` | The standard symbologies to read; default `All`. `None` reads only `LightGrid` |
| `LightGrid` | A grid of lamps to read as well, such as `new LightGridOptions(12, 12)`; `null` skips it |
| `Title` | The sheet's title; `null` for the localised "Scan code" |
| `ShowReticle` | The breathing aim corners; default `true` |
| `ShowDetection` | The burst on a hit before the sheet closes; default `true`. Off returns at once |
| `PlaySound` | A short sound on a hit: the "Tink" system sound on iOS (muted by the silent switch), the acknowledge tone on Android; default `true` |
| `ShowTorch` | A torch in the header when the device has one; default `true` |
| `ShowPrompt` | A text box between the aim corners and the bottom edge with `Prompt`; default `false`. A problem is shown there even when it is off |
| `Prompt` | The prompt's text; `null` for the localised "Point the camera at the code" |
| `Detents` | The sheet sizes to drag between; default `[SheetDetent.Medium, SheetDetent.FullScreen]` |
| `InitialDetent` | The size it opens at; `null` for the first of `Detents` |
| `ShowDiagnostics` | Frames per second, time per frame and what the light-grid reader sees, at the bottom; default `false` |

The sheet's sizes come from the options rather than its `[NavigableSheet]` attribute through Spine's `ISheetDetentsProvider`,
which any sheet's view model can implement (see [Sheets](sheets.md#sizes-chosen-per-navigation)).

`BarcodeScanResult` has `Value` (the text), `Format`, `IsLightGrid` (read by the light-grid reader, not the platform's reader), `Corners` (the code's corners in the scanner view) and, for a light grid, `Grid`.

### BarcodeScannerView

For a scanner inside a page of the app's own:

```xml
<BarcodeScannerView Formats="QrCode"
                    IsScanning="{Binding IsScanning}"
                    IsTorchOn="{Binding TorchOn}"
                    DetectedCommand="{Binding FoundCommand}" />
```

| Member | Purpose |
|---|---|
| `Formats` | The standard symbologies to read; default `All`. `None` leaves only `LightGrid` |
| `LightGrid` | A `LightGridOptions` to read a grid of lamps as well; `null` (default) skips it |
| `IsScanning` | Whether the camera runs; default `true`. Turn it off to pause without leaving the page |
| `IsTorchOn` | The torch; two-way. Set back to `false` when scanning stops or the view leaves its window |
| `IsTorchAvailable` | Read-only: whether the camera in use has a torch |
| `RepeatInterval` | How long the same value stays quiet after it was reported, while the camera keeps seeing it; default 2 s |
| `ScanArea` | A `Rect?` in the view's device-independent units: a code counts only when its centre lies inside, and of several codes inside, the one nearest the area's centre wins. `null` (default) counts the whole view |
| `ConfirmationReads` | How many reads of the same value in a row a standard code needs before it is reported; default 2, 1 reports at once. Counted in analysed frames (at most two apart); a light grid is reported on its first read |
| `Detected` | Event with `BarcodeDetectedEventArgs.Result`, on the main thread |
| `DetectedCommand` | Run with the `BarcodeScanResult`, on the main thread, when `CanExecute` allows |
| `Problem` | Read-only: what stops the view from scanning, or `null` while it scans |
| `Diagnostics` | Read-only, twice a second: frames per second, time per frame, the zoom when above 1×, and for a light grid whether it was found and read, plus the latest error |
| `ProblemChanged` | Event with `Problem` and a localised `Message` to show; `Problem` is `null` when scanning resumes |

The camera runs only while the native view is in a window and `IsScanning` is true, and it stops, with the torch off, the moment the view leaves its window. That follows the native view, not MAUI's `Loaded` and `Unloaded` or the page's lifecycle, which a closing sheet does not always raise; a camera left running there would fight the next scanner for the device. Leaving the window also lets go of everything the camera holds (the capture session and its observers on iOS, the ML Kit reader on Android), so a host that never disconnects the handler of a closed sheet or popup does not keep one per scan; the next time the view shows, the camera starts afresh. When scanning stops the view keeps the last frame on screen. The torch is switched on the camera's own queue and only the latest wish is applied, so fast taps do not queue up. Frames are 1280 × 720, or the closest size the camera has: enough detail for a 12 × 12 grid across a room, small enough to read every frame.

The camera focuses continuously on the middle of the picture. A tap on the view focuses and meters on that spot instead: on iOS until the phone moves on to a new scene, on Android for five seconds; then the camera focuses by itself again. The scanner also sets the lens for what it reads:

- unless it reads a light grid, it zooms in when the camera cannot focus close: far enough that an EAN-13 held at the closest sharp distance spans half the preview, at most 3×. That is about 2× on the Pro iPhones, whose wide camera focuses no closer than about 20 cm, about 1.8× on a tablet's wide camera in landscape, and nothing on a camera that focuses close. Without it the user moves closer to fill the aim, past the closest focus, and every frame blurs. On Android the zoom is worked out again when the preview changes size, as when a tablet is turned. With a light grid it stays at 1×, to see a clock across a room;
- on iOS, when it reads only linear codes and no light grid, autofocus is kept to near distances, where product codes are scanned.

A code held in front of the camera is seen many times a second. `ConfirmationReads` first asks for the same value twice in a row: the platform readers check the check digit, but a partly seen linear code can still come out as another valid number, and the second read costs one frame. `RepeatInterval` then turns the stream of reads into one report: the same value is reported again only after the camera has not seen it for that long. A different value is reported once it is confirmed. To stop after the first hit, set `IsScanning` to false in the command, as the scan sheet does.

On iOS the standard formats are tried first on each frame, then the light grid. On Android the light grid comes first, because ML Kit reads asynchronously and holds the frame until it is done.

### Problems

| `ScannerProblem` | When | Default text (key) |
|---|---|---|
| `PermissionDenied` | The user said no to the camera, or turned it off in Settings | `Spine.Scanner.PermissionDenied` |
| `NoCamera` | No camera, or none the scanner can use | `Spine.Scanner.NoCamera` |
| `Interrupted` | The system took the camera away: a call, another app, split view; on Android also do-not-disturb or too many cameras open. Cleared when the camera comes back | `Spine.Scanner.Interrupted` |
| `NoFrames` | No frame for two seconds from a camera that is open and not interrupted; the scanner restarts the camera | `Spine.Scanner.NoFrames` |
| `Failed` | Anything else, including a missing `NSCameraUsageDescription`; the message has the details | `Spine.Scanner.Failed` |

The texts ship in English and Swedish under `Spine.Scanner.*`, with `Spine.Scanner.Title`, `Spine.Scanner.Prompt` and `Spine.Scanner.Torch` for the sheet. An app overrides any key in its own strings (see [Strings](strings.md)). A problem that comes from a platform error carries the platform's own message in brackets after the default text, so a report from a user says what went wrong.

On iOS, Vision reports a UPC-A code as EAN-13 with a leading zero, so asking for `UpcA` gives a result with `Format = Ean13` and 13 digits there.

---

## Light grids

A word clock is a grid of letters behind glass, each lit from behind. A 12 × 12 clock can show a 12 × 12 Data Matrix: a lit letter is a dark module, an unlit letter a light one. That turns the clock into a display for a pairing code, a Wi-Fi setup code or a serial number, with no screen.

Platform barcode readers do not read it. They expect filled squares with sharp edges, and a lit letter is thin strokes with a dark middle. The unlit letters are still visible, the glass reflects the room, and the lamps bloom into each other in a dark room. `LightGridReader` is made for that:

1. It finds the lights: the bright blobs in the frame.
2. It grows a regular grid from them and fits it with a homography, so the clock can be seen at an angle. Blobs that do not sit on the grid, such as a lamp in the room, are dropped.
3. It measures each cell by its brightest few percent of pixels, not their average, so a lit I and a lit W read the same.
4. It splits the cells into lit and unlit, tries the four rotations, a mirror image and the inverted code, keeps the variants whose Data Matrix border (solid left and bottom edges, alternating top and right) is nearly intact, and decodes them with ZXing, error correction included.

Measured on an iPhone 16 Pro: 9–12 ms per 1280 × 720 frame, so it runs on every camera frame next to the platform's reader.

### In the scanner

Set `LightGrid` on the view or the sheet options. Results from it have `IsLightGrid = true` and `Format = DataMatrix`.

```csharp
new BarcodeScanOptions { Formats = BarcodeFormat.None, LightGrid = new LightGridOptions(12, 12) }
```

`Formats = BarcodeFormat.None` reads only the grid; leave the standard formats on when the same screen also scans printed codes.

### On its own

`LightGridReader` is in the Barcodes package and targets `net10.0` too, so it can read frames from any source: a camera of the app's own, a stored photo, a test image.

```csharp
var reader = new LightGridReader(new LightGridOptions(12, 12));

LightGridResult result = reader.Read(yPlane, width, height, stride);
if (result.IsDecoded)
    Pair(result.Text!);
else
    logger.LogDebug("No code: {Steps}", result.Diagnostics);
```

`Read` takes an 8-bit luminance image, such as the Y plane of a camera frame, with its row stride.

| `LightGridResult` | Purpose |
|---|---|
| `Text`, `IsDecoded` | The decoded text, or `null` when no code was read |
| `Corners` | The grid's outer corners in image pixels (top-left, top-right, bottom-right, bottom-left in grid order), or empty. The grid can be found without the code being read, which is useful for a guide on screen |
| `Cells` | Which lamps were taken as lit, `[column, row]`, when a grid was found |
| `Diagnostics` | The steps and their timings. Log it when a code is not read |

One instance keeps its working memory between images, so a live camera does not allocate per frame. It is not thread-safe: give each camera its own. `Read` does not throw for a bad image; a frame it cannot make sense of is a miss, and `Diagnostics` says why. It throws only for wrong arguments, such as a stride smaller than the width.

---

## A pairing code on a word clock

The device, a 12 × 12 clock, makes up a code, lights it, and waits. The phone scans it with `LightGrid` set and sends the code to the device or a backend, which pairs the two.

```csharp
// On the device, or wherever the clock's picture is made
string code = Random.Shared.NextInt64(0, 10_000_000_000).ToString("D10");
var matrix = Barcode.Encode(code, new DataMatrixOptions { Size = (12, 12) });

for (int y = 0; y < 12; y++)
    for (int x = 0; x < 12; x++)
        clock.SetLamp(x, y, lit: matrix[x, y]);
```

A 12 × 12 Data Matrix (ISO/IEC 16022, ECC 200) fits the grid exactly: 5 data codewords and 7 Reed–Solomon codewords. That holds 10 digits, 6 upper-case letters and digits, or 3 bytes. The error correction lets the code read with a few lamps wrong.

The dark frame around the letters is the code's quiet zone. Keep at least one lamp's width of it clear of other lights.

### Codes people can type

Ten digits work as a code to type in by hand as well, and are the most the grid holds. When the app wants letters, keep digits in the code and change only how the app shows them. Pick a number below the size of the letter space, put its ten digits in the code, and show it as letters:

| Shown as | Values | Pick below |
|---|---|---|
| 10 digits | 10 000 000 000 | 10¹⁰ |
| 7 letters A–Z | 8 031 810 176 | 26⁷ |
| 6 Crockford Base32 characters (no I, L, O or U) | 1 073 741 824 | 32⁶ |

Seven letters do not fit the grid as letters (six is the limit), but their number does fit as ten digits.

```csharp
const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

// On the device: the code carries the digits, the device shows the letters
long n = Random.Shared.NextInt64(0, 8_031_810_176);   // 26^7; 1_073_741_824 (32^6) for Crockford
string inTheCode = n.ToString("D10");
string shown = Spell(n, Letters, 7);                  // Spell(n, Crockford, 6) for Crockford

// On the phone, with the BarcodeScanResult from the scan
string scanned = Spell(long.Parse(code.Value), Letters, 7);

static string Spell(long n, string alphabet, int length)
{
    var chars = new char[length];
    for (int i = length - 1; i >= 0; i--)
    {
        chars[i] = alphabet[(int)(n % alphabet.Length)];
        n /= alphabet.Length;
    }
    return new string(chars);
}
```

Crockford Base32 reads `I` and `L` as `1` and `O` as `0` when a person types it back; map those before looking the letters up.

---

## Limits

- **Light grids are Data Matrix only**, in the square sizes 10 × 10 to 26 × 26 (even sizes). `LightGridReader` throws `NotSupportedException` for any other grid; a custom code for other grid sizes may come later.
- **No Micro QR.** ZXing cannot encode it, and a 12 × 12 Data Matrix fits a small grid better.
- **No scanning on Windows yet.** `BarcodeScannerView` has no Windows handler; `BarcodeView` and encoding work there.
- **Android 6.0 (API 23) for the Scanner.** The Barcodes package stays at the core's minimum.
- **The back camera.** The scanner uses the back camera; without one it falls back to another camera on the device. There is no camera switch.
- **Light on dark.** Most phone cameras read an `IsInverted` code; not every scanner does.

## Related

- [Strings](strings.md): overriding the scanner's text
- [Navigation results](navigation-results.md): how `NavigateToWithResultAsync` returns the scan
- [Sheets](sheets.md): the sheet the scanner opens in
- [Packages](packages.md)
