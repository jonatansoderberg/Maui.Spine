using System.Net.Http;
using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Avatars;

/// <summary>An avatar the gallery shows and the detail page opens.</summary>
public sealed record AvatarEntry(string Id, string Title, string Summary, string Profile, string Renderer, AvatarSource Source);

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
    }

    // glTF avatars are drawn by SceneKit on iOS and Mac Catalyst and by Skia on the CPU elsewhere (no textures).
    private static string Gltf => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? "glTF · SceneKit" : "glTF · Skia";

    /// <summary>Bundled as MauiAssets, stripped of their generators' source: about 1.9 MB together.</summary>
    public IReadOnlyList<AvatarEntry> Bundled { get; } =
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
            Kairo = new("kairo", "Kairo", "A head reconstructed from photos, with hair made in Blender: 322,000 triangles and morph targets for the face.", "Character", Gltf, AvatarSource.FromFile(_kairoPath));
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
