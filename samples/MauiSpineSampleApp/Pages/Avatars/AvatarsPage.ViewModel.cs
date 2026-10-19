using System.Net.Http;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Controls.Avatar;

namespace MauiSpineSampleApp.Pages.Avatars;

/// <summary>
/// An avatar the gallery shows and the detail page opens. Its card shows <see cref="Poster"/> once read from the package,
/// or the live avatar when <see cref="Live"/> is set (the package could not be read, or its posters are placeholders).
/// </summary>
public sealed record AvatarEntry(
    string Id, string Title, string Summary, string Profile, string Renderer, AvatarSource Source, AvatarPoster? Poster = null, bool Live = false)
{
    /// <summary>The source for the card's live avatar: none while the poster shows.</summary>
    public AvatarSource? LiveSource => Live ? Source : null;
}

/// <summary>A package's light and dark posters.</summary>
public sealed record AvatarPoster(ImageSource Light, ImageSource Dark);

public partial class AvatarsPageViewModel : SampleViewModel
{
    private const string KairoFile = "kairo.spineavatar";

    private readonly INavigationService _navigation;
    private readonly IHttpClientFactory _http;
    private readonly string _kairoPath = Path.Combine(FileSystem.AppDataDirectory, "avatars", KairoFile);

    public AvatarsPageViewModel(INavigationService navigation, IHttpClientFactory http)
    {
        _navigation = navigation;
        _http = http;
        UpdateKairo();
        _ = LoadPostersAsync();
        _ = CheckKairoAsync();
    }

    // glTF avatars are drawn by SceneKit on iOS and Mac Catalyst and by Skia on the CPU elsewhere (no textures).
    private static string Gltf => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? "glTF · SceneKit" : "glTF · Skia";

    /// <summary>Bundled as MauiAssets, packed by spine-avatar with 512 px posters and no source: about 2 MB together.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AvatarEntry> Bundled { get; set; } = BundledAvatars;

    private static readonly AvatarEntry[] BundledAvatars =
    [
        new("nova", "Nova", "A small robot with a light in each ear: a model generated in code, 15 mouth shapes, gestures and a mute badge.", "Character", Gltf, AvatarSource.FromMauiAsset("Avatars/nova.spineavatar")),
        new("plush-puff", "Puff", "A plush creature that squashes as it talks and blushes when it is happy.", "Character", Gltf, AvatarSource.FromMauiAsset("Avatars/plush-puff.spineavatar")),
        new("voice-totem", "Voice Totem", "A ring and three packets that follow the voice: a vector scene, crisp at any size.", "Ambient", "Vector · Skia", AvatarSource.FromMauiAsset("Avatars/voice-totem.spineavatar")),
        new("aurora-flow", "Aurora Flow", "Light that flows: one SkSL shader fed the levels, the mouth and the state.", "Ambient", "Shader · Skia", AvatarSource.FromMauiAsset("Avatars/aurora-flow.spineavatar")),
        new("bars", "Bars", "VoiceView's bars: the answer's 24 bands up from a line and the microphone's down, one part per band.", "Ambient", "Vector · Skia", AvatarSource.FromMauiAsset("Avatars/bars.spineavatar")),
        new("ring", "Ring", "VoiceView's ring: the answer's bands outward and the microphone's inward, round an empty middle.", "Ambient", "Vector · Skia", AvatarSource.FromMauiAsset("Avatars/ring.spineavatar")),
    ];

    /// <summary>Kairo, once downloaded; null until then.</summary>
    [ObservableProperty]
    public partial AvatarEntry? Kairo { get; set; }

    [ObservableProperty]
    public partial bool HasKairo { get; set; }

    [ObservableProperty]
    public partial string KairoStatus { get; set; } = "";

    [ObservableProperty]
    public partial double KairoProgress { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadKairoCommand))]
    public partial bool IsDownloading { get; set; }

    /// <summary>Kairo as the server offers it (its version, size and hash), once asked; null until then or offline.</summary>
    private AvatarDownload? _offered;

    /// <summary>True while there is something to download: no copy yet, or the server has a later version than the kept one.</summary>
    [ObservableProperty]
    public partial bool CanGetKairo { get; set; } = true;

    [ObservableProperty]
    public partial string DownloadText { get; set; } = "Download Kairo";

    // A gallery of four live avatars drew all four every frame; the posters cost one read of each package, off the UI thread.
    private async Task LoadPostersAsync()
    {
        var entries = await Task.WhenAll(BundledAvatars.Select(entry => Task.Run(async () =>
        {
            try
            {
                await using var stream = await entry.Source.OpenAsync(CancellationToken.None);
                var package = AvatarArchive.Read(stream);
                var light = package.GetFile(package.Manifest.Posters.Light).ToArray();
                var dark = package.GetFile(package.Manifest.Posters.Dark).ToArray();
                return entry with { Poster = new(ImageSource.FromStream(() => new MemoryStream(light)), ImageSource.FromStream(() => new MemoryStream(dark))) };
            }
            catch (Exception e) when (e is IOException or AvatarLoadException)
            {
                // The live avatar shows the package's own error on the card.
                return entry with { Live = true };
            }
        })));
        Bundled = entries;
    }

    [RelayCommand]
    private Task Open(AvatarEntry entry) => _navigation.NavigateToAsync<AvatarDetailPage, AvatarEntry>(entry);

    // The server lists what it offers with each package's version; the kept copy's manifest names its own. A later version on the
    // server turns the button into an update.
    private async Task CheckKairoAsync()
    {
        var offered = await AvatarDownloads.ListAsync(_http.CreateClient("SampleServer"));
        _offered = offered?.FirstOrDefault(a => a.Id == "kairo");
        UpdateKairo();
    }

    [RelayCommand(CanExecute = nameof(CanDownload), IncludeCancelCommand = true)]
    private async Task DownloadKairo(CancellationToken cancellationToken)
    {
        IsDownloading = true;
        KairoProgress = 0;
        try
        {
            var http = _http.CreateClient("SampleServer");
            _offered ??= (await AvatarDownloads.ListAsync(http, cancellationToken))?.FirstOrDefault(a => a.Id == "kairo");
            if (_offered is null)
            {
                KairoStatus = $"The sample server ({http.BaseAddress}) did not answer with its avatars.";
                return;
            }
            var progress = new Progress<(long Read, long Total)>(p =>
            {
                KairoProgress = (double)p.Read / p.Total;
                KairoStatus = $"{p.Read / 1_000_000.0:0.0} of {p.Total / 1_000_000.0:0.0} MB";
            });
            await AvatarDownloads.DownloadAsync(http, _offered, _kairoPath, progress, cancellationToken);
            UpdateKairo();
        }
        catch (OperationCanceledException)
        {
            KairoStatus = "Cancelled.";
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            KairoStatus = $"Download failed: {e.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private bool CanDownload() => !IsDownloading;

    [RelayCommand]
    private void ForgetKairo()
    {
        File.Delete(_kairoPath);
        UpdateKairo();
    }

    private void UpdateKairo()
    {
        var kept = File.Exists(_kairoPath) ? AvatarDownloads.VersionOf(_kairoPath) ?? "" : null;
        var size = _offered is null ? "" : $", {_offered.Bytes / 1_000_000.0:0.0} MB";
        if (kept is not null)
        {
            HasKairo = true;
            // A new entry, so the card and the detail page open the file as it is now.
            Kairo = new("kairo", "Kairo", "A head reconstructed from photos, with hair generated from four reference views and morph targets for the face.", "Character", Gltf, AvatarSource.FromFile(_kairoPath), Live: true);
            var newer = _offered is not null && _offered.IsNewerThan(kept);
            CanGetKairo = newer;
            DownloadText = newer ? $"Update to {_offered!.Version}" : "Download Kairo";
            KairoStatus = newer
                ? $"Kairo {kept} is kept in the app's data; the sample server has {_offered!.Version}{size}."
                : _offered is null
                    ? $"Kairo {kept}, kept in the app's data and opened from there."
                    : $"Kairo {kept}, the sample server's latest, kept in the app's data.";
        }
        else
        {
            HasKairo = false;
            Kairo = null;
            CanGetKairo = true;
            DownloadText = "Download Kairo";
            KairoStatus = _offered is null
                ? "From the sample server, kept in the app's data."
                : $"Kairo {_offered.Version}{size} from the sample server, kept in the app's data.";
        }
    }
}
