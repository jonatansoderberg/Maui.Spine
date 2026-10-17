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
    private const long KairoBytes = 16_607_659;

    private readonly INavigationService _navigation;
    private readonly IHttpClientFactory _http;
    private readonly string _kairoPath = Path.Combine(FileSystem.AppDataDirectory, "avatars", KairoFile);

    public AvatarsPageViewModel(INavigationService navigation, IHttpClientFactory http)
    {
        _navigation = navigation;
        _http = http;
        UpdateKairo();
        _ = LoadPostersAsync();
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

    [RelayCommand(CanExecute = nameof(CanDownload), IncludeCancelCommand = true)]
    private async Task DownloadKairo(CancellationToken cancellationToken)
    {
        IsDownloading = true;
        KairoProgress = 0;
        var partial = _kairoPath + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_kairoPath)!);
            using var response = await _http.CreateClient("SampleServer").GetAsync($"sample/avatars/{KairoFile}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                KairoStatus = $"The server said {(int)response.StatusCode} {response.ReasonPhrase} for {response.RequestMessage?.RequestUri}.";
                return;
            }

            var total = response.Content.Headers.ContentLength ?? KairoBytes;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                    read += n;
                    KairoProgress = (double)read / total;
                    KairoStatus = $"{read / 1_000_000.0:0.0} of {total / 1_000_000.0:0.0} MB";
                }
            }
            // Only a complete file takes the cached name, so a cancelled download is never opened.
            File.Move(partial, _kairoPath, overwrite: true);
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
            File.Delete(partial);
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
        if (File.Exists(_kairoPath))
        {
            HasKairo = true;
            // The hair's alpha-masked cards need SceneKit; the Skia glTF renderer leaves Kairo to its poster.
            var renderer = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? "glTF · SceneKit" : "glTF · poster";
            // Live: the sample server serves the lab's Kairo, whose posters are 1×1 placeholders until it is packed (#542).
            Kairo = new("kairo", "Kairo", "A head reconstructed from photos, with hair made in Blender: 322,000 triangles and morph targets for the face.", "Character", renderer, AvatarSource.FromFile(_kairoPath), Live: true);
            KairoStatus = $"Downloaded, {new FileInfo(_kairoPath).Length / 1_000_000.0:0.0} MB in the app's data; opened from there from now on.";
        }
        else
        {
            HasKairo = false;
            Kairo = null;
            KairoStatus = $"{KairoBytes / 1_000_000.0:0.0} MB from the sample server, kept in the app's data.";
        }
    }
}
