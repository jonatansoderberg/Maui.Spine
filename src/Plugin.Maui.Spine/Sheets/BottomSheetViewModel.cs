using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Plugin.Maui.Spine.Sheets;

/// <summary>
/// A synchronization helper that allows async code to wait until a user interaction completes.
/// </summary>
internal partial class AwaitUserInteraction
{
    private bool _result = false;

    /// <summary>The result set by <see cref="Release"/>. <see langword="false"/> until released.</summary>
    public bool Result => _result;

    private readonly ManualResetEventSlim _resetEvent = new(false);

    /// <summary>Releases the waiting task and stores <paramref name="result"/>.</summary>
    /// <param name="result">The value to expose via <see cref="Result"/>.</param>
    public void Release(bool result = false)
    {
        _result = result;
        _resetEvent.Set();
    }

    /// <summary>Asynchronously waits until <see cref="Release"/> is called.</summary>
    public async Task WaitForUserInteraction()
    {
        await new TaskFactory().StartNew(() =>
        {
            _resetEvent.Wait();
        });
    }
}

// Legacy PoC VM types kept for compatibility, but they no longer drive sheet dismissal.
/// <summary>Legacy base ViewModel for bottom-sheet pages. Prefer <see cref="Plugin.Maui.Spine.Core.ViewModelBase"/> for new sheets.</summary>
internal partial class BottomSheetViewModel : ObservableObject
{
    /// <summary>Command that invokes <see cref="Accept"/> when executed.</summary>
    public IRelayCommand AcceptCommand { get; }

    /// <summary>Initializes the ViewModel and wires up <see cref="AcceptCommand"/>.</summary>
    public BottomSheetViewModel()
    {
        AcceptCommand = new RelayCommand(Accept);
    }

    /// <summary>Called when <see cref="AcceptCommand"/> is executed. Override to add accept logic.</summary>
    protected virtual void Accept()
    {
    }
}

/// <summary>Legacy generic base ViewModel that carries a typed <see cref="Value"/> for bottom-sheet pages.</summary>
internal partial class BottomSheetViewModel<T> : BottomSheetViewModel
{
    /// <summary>The value associated with this sheet.</summary>
    [ObservableProperty]
    public partial T? Value { get; set; }

    /// <summary>Initializes the ViewModel and sets <see cref="Value"/> to <paramref name="value"/>.</summary>
    public BottomSheetViewModel(T value) : base()
    {
        Value = value;
    }
}