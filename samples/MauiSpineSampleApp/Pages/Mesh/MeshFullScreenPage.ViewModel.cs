using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Mesh;

/// <summary>The mesh on the Mesh backgrounds page, carried to the full-screen page.</summary>
public sealed record MeshSettings(MeshPreset Preset, MeshDrift Drift, int Columns, int Rows, int FrameRate);

public partial class MeshFullScreenPageViewModel : SampleViewModel, IReceivesNavigationParameter<MeshSettings>
{
    [ObservableProperty]
    public partial MeshSettings Settings { get; set; } = new(MeshPreset.Aurora, MeshDrift.Slow, 3, 3, 30);

    public Task OnNavigationParameterAsync(MeshSettings param)
    {
        Settings = param;
        Title = param.Preset.ToString();
        return Task.CompletedTask;
    }
}
