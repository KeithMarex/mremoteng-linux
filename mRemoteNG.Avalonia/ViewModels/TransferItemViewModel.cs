using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>A row of the transfer list.</summary>
public sealed partial class TransferItemViewModel(TransferItem item) : ObservableObject
{
    private static readonly string[] Shown = [nameof(Percent), nameof(StateText), nameof(IsFinished), nameof(HasFailed)];

    public TransferItem Item { get; } = item;

    public string Name => Item.Name;

    /// <summary>↓ for a download, ↑ for an upload.</summary>
    public string DirectionText => Item.Direction == TransferDirection.Download ? "↓" : "↑";

    /// <summary>From where to where, as a tooltip.</summary>
    public string Route => $"{Item.Source} → {Item.DestinationDirectory}";

    public double Percent => Item.BytesTotal > 0 ? 100.0 * Item.BytesDone / Item.BytesTotal : Item.State == TransferState.Done ? 100 : 0;

    public bool IsFinished => Item.IsFinished;

    public bool HasFailed => Item.State == TransferState.Failed;

    public string StateText => Item.State switch
    {
        TransferState.Queued => UiText.TransferQueued,
        TransferState.Running when Item.BytesTotal == 0 => UiText.TransferPreparing,
        TransferState.Running => UiText.TransferProgress(FileSizes.Format(Item.BytesDone), FileSizes.Format(Item.BytesTotal), Item.CurrentFile),
        TransferState.Done => UiText.TransferDone(Item.FilesSkipped),
        TransferState.Failed => UiText.TransferFailed(Item.Error ?? string.Empty),
        _ => UiText.TransferCancelled
    };

    /// <summary>Shows the item's current state; called on the UI thread.</summary>
    public void Refresh()
    {
        foreach (string property in Shown)
            OnPropertyChanged(property);
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanCancel => !Item.IsFinished;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => Item.Cancel();
}
