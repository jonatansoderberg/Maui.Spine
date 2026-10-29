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

/// <summary>
/// One card in the gallery: a bundled avatar, or one the sample server hosts, with its download state. A hosted one opens
/// once it is kept; until then its card shows the server's poster, its size and a Download button.
/// </summary>
public sealed partial class AvatarItem(AvatarEntry entry, AvatarDownload? hosted = null) : ObservableObject
{
    [ObservableProperty]
    public partial AvatarEntry Entry { get; set; } = entry;

    /// <summary>What the server offers, or null for a bundled avatar (or a kept one the server no longer lists).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHosted))]
    public partial AvatarDownload? Hosted { get; set; } = hosted;

    /// <summary>Hosted by the server or downloaded from it, rather than bundled.</summary>
    public bool IsHosted => Hosted is not null || Kept;

    /// <summary>Whether a copy is kept in the app's data, so the card opens it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHosted))]
    public partial bool Kept { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>Download or Update; empty when there is nothing to get.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGet))]
    public partial string GetText { get; set; } = "";

    public bool CanGet => GetText.Length > 0 && !IsDownloading;

    partial void OnIsDownloadingChanged(bool value) => OnPropertyChanged(nameof(CanGet));

    internal CancellationTokenSource? Cancel { get; set; }

    internal string? Path { get; set; }
}

public partial class AvatarsPageViewModel : SampleViewModel
{
    private readonly INavigationService _navigation;
    private readonly IHttpClientFactory _http;

    public AvatarsPageViewModel(INavigationService navigation, IHttpClientFactory http)
    {
        _navigation = navigation;
        _http = http;
        Items = [.. BundledAvatars.Select(e => new AvatarItem(e)), .. KeptItems()];
        _ = LoadPostersAsync();
        _ = ListHostedAsync();
    }

    // glTF avatars are drawn by SceneKit on iOS and Mac Catalyst and by Skia on the CPU elsewhere (no textures).
    private static string Gltf => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? "glTF · SceneKit" : "glTF · Skia";

    /// <summary>Every avatar: the bundled ones, then those the server hosts (downloaded or not).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AvatarItem> Items { get; set; }

    private static readonly AvatarEntry[] BundledAvatars =
    [
        new("nova", "Nova", "A small robot with a light in each ear: a model generated in code, 15 mouth shapes, gestures and a mute badge.", "Character", Gltf, AvatarSource.FromMauiAsset("Avatars/nova.spineavatar")),
        new("plush-puff", "Puff", "A plush creature that squashes as it talks and blushes when it is happy.", "Character", Gltf, AvatarSource.FromMauiAsset("Avatars/plush-puff.spineavatar")),
        new("voice-totem", "Voice Totem", "A ring and three packets that follow the voice: a vector scene, crisp at any size.", "Ambient", "Vector · Skia", AvatarSource.FromMauiAsset("Avatars/voice-totem.spineavatar")),
        new("aurora-flow", "Aurora Flow", "Light that flows: one SkSL shader fed the levels, the mouth and the state.", "Ambient", "Shader · Skia", AvatarSource.FromMauiAsset("Avatars/aurora-flow.spineavatar")),
        new("mini", "Mini", "Nine small figures in one package of 33 KB: seven hand-drawn shapes, round or tall eyes and a colour of your own. One SkSL shader; pick a figure on its page.", "Character", "Shader · Skia", AvatarSource.FromMauiAsset("Avatars/mini.spineavatar")),
        new("bars", "Bars", "Built into Spine: the answer's 24 bands up from a line and the microphone's down, one part per band, the microphone in the app's accent.", "Built in", "Vector · Skia", AvatarSource.BuiltIn("bars")),
        new("ring", "Ring", "Built into Spine: the answer's bands outward and the microphone's inward round an empty middle, for round a button or a picture.", "Built in", "Vector · Skia", AvatarSource.BuiltIn("ring")),
    ];

    // What the gallery says about an avatar the server hosts; the server gives its name and profile, not a description.
    private static readonly Dictionary<string, string> HostedSummaries = new(StringComparer.Ordinal)
    {
        ["kairo"] = "A head reconstructed from photos, with hair generated from four reference views and morph targets for the face. About 10 MB: hosted, not bundled.",
    };

    private static string SummaryOf(string id) =>
        HostedSummaries.GetValueOrDefault(id) ?? "Hosted by the sample server: downloaded when asked, kept in the app's data, updated when the server has a later version.";

    private static string Title(string profile) => profile.Length == 0 ? profile : char.ToUpperInvariant(profile[0]) + profile[1..];

    // Kept copies show at once, before the server answers, so the page works offline.
    private static IEnumerable<AvatarItem> KeptItems() =>
        AvatarDownloads.Kept().Select(k =>
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(k.Path);
            var item = new AvatarItem(new(id, k.Name, SummaryOf(id), "Character", "Hosted", AvatarSource.FromFile(k.Path))) { Path = k.Path };
            Refresh(item);
            return item;
        });

    // A gallery of live avatars drew all of them every frame; the posters cost one read of each package, off the UI thread.
    private async Task LoadPostersAsync()
    {
        await Task.WhenAll(Items.Where(i => !i.IsHosted || i.Kept).Select(LoadPosterAsync));
    }

    private static async Task LoadPosterAsync(AvatarItem item)
    {
        var entry = item.Entry;
        var loaded = await Task.Run(async () =>
        {
            try
            {
                await using var stream = await entry.Source.OpenAsync(CancellationToken.None);
                var package = AvatarArchive.Read(stream);
                var light = package.GetFile(package.Manifest.Posters.Light).ToArray();
                var dark = package.GetFile(package.Manifest.Posters.Dark).ToArray();
                return entry with { Poster = new(ImageSource.FromStream(() => new MemoryStream(light)), ImageSource.FromStream(() => new MemoryStream(dark))), Live = false };
            }
            catch (Exception e) when (e is IOException or AvatarLoadException)
            {
                // The live avatar shows the package's own error on the card.
                return entry with { Live = true };
            }
        });
        MainThread.BeginInvokeOnMainThread(() => item.Entry = loaded);
    }

    // The server lists what it hosts with each package's name, profile and version: new avatars join the list with the
    // server's posters, kept ones learn whether the server has a later version.
    private async Task ListHostedAsync()
    {
        var http = _http.CreateClient("SampleServer");
        var offered = await AvatarDownloads.ListAsync(http);
        if (offered is null)
        {
            foreach (var item in Items.Where(i => i.Kept))
                Refresh(item);
            return;
        }

        var items = Items.ToList();
        foreach (var avatar in offered)
        {
            var path = AvatarDownloads.PathOf(avatar);
            if (items.FirstOrDefault(i => i.Path == path) is { } kept)
            {
                kept.Hosted = avatar;
                Refresh(kept);
                continue;
            }
            var poster = new AvatarPoster(
                ImageSource.FromUri(new Uri(http.BaseAddress!, $"sample/avatars/{avatar.Id}/poster-light.png")),
                ImageSource.FromUri(new Uri(http.BaseAddress!, $"sample/avatars/{avatar.Id}/poster-dark.png")));
            var entry = new AvatarEntry(avatar.Id, avatar.Name ?? avatar.Id, SummaryOf(avatar.Id), Title(avatar.Profile ?? "character"), "Hosted", AvatarSource.FromFile(path), poster);
            var item = new AvatarItem(entry, avatar) { Path = path };
            Refresh(item);
            items.Add(item);
        }
        Items = items;
    }

    [RelayCommand]
    private async Task Open(AvatarItem item)
    {
        // A hosted avatar opens once it is kept; until then a tap downloads it.
        if (item.IsHosted && !item.Kept)
        {
            await Get(item);
            return;
        }
        await _navigation.NavigateToAsync<AvatarDetailPage, AvatarEntry>(item.Entry);
    }

    [RelayCommand]
    private async Task Get(AvatarItem item)
    {
        if (item.Hosted is not { } avatar || item.IsDownloading || item.Path is not { } path)
            return;
        item.IsDownloading = true;
        item.Progress = 0;
        using var cancel = item.Cancel = new CancellationTokenSource();
        try
        {
            // Progress<T> posts its reports; one that arrives after the download has ended must not overwrite the result.
            var progress = new Progress<(long Read, long Total)>(p =>
            {
                if (!item.IsDownloading)
                    return;
                item.Progress = (double)p.Read / p.Total;
                item.Status = $"Downloading {p.Read / 1_000_000.0:0.0} of {p.Total / 1_000_000.0:0.0} MB";
            });
            await AvatarDownloads.DownloadAsync(_http.CreateClient("SampleServer"), avatar, path, progress, cancel.Token);
            item.IsDownloading = false;
            // A new source, so the card and the detail page open the file as it is now.
            item.Entry = item.Entry with { Source = AvatarSource.FromFile(path) };
            Refresh(item);
            await LoadPosterAsync(item);
        }
        catch (OperationCanceledException)
        {
            item.IsDownloading = false;
            Refresh(item);
            item.Status = "Cancelled. " + item.Status;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException)
        {
            item.IsDownloading = false;
            Refresh(item);
            item.Status = $"Download failed: {e.Message}";
        }
        finally
        {
            item.Cancel = null;
        }
    }

    [RelayCommand]
    private static void Cancel(AvatarItem item) => item.Cancel?.Cancel();

    [RelayCommand]
    private void Forget(AvatarItem item)
    {
        if (item.Path is not { } path)
            return;
        File.Delete(path);
        if (item.Hosted is null)
            Items = [.. Items.Where(i => i != item)];
        else
            Refresh(item);
    }

    /// <summary>The card's status and button from the kept copy and what the server offers.</summary>
    private static void Refresh(AvatarItem item)
    {
        var kept = item.Path is { } path && File.Exists(path) ? AvatarDownloads.VersionOf(path) ?? "" : null;
        var offered = item.Hosted;
        item.Kept = kept is not null;
        if (kept is null)
        {
            item.GetText = offered is null ? "" : "Download";
            item.Status = offered is null ? "No longer on the server." : $"On the sample server · {offered.Version} · {offered.Size}";
        }
        else if (offered is not null && offered.IsNewerThan(kept))
        {
            item.GetText = $"Update to {offered.Version}";
            item.Status = $"Downloaded {kept} · {offered.Version} available · {offered.Size}";
        }
        else
        {
            item.GetText = "";
            item.Status = offered is null ? $"Downloaded {kept} · server not reached" : $"Downloaded {kept} · up to date";
        }
    }
}
