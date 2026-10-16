using Avalonia.Controls;
using Avalonia.Interactivity;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.Avalonia.Views;

/// <summary>Asks whether to trust a host key or certificate; Cancel is the default, as with ssh.</summary>
public partial class TrustDialog : Window
{
    public TrustDialog() : this(string.Empty, string.Empty)
    {
    }

    public TrustDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void OnRememberClick(object? sender, RoutedEventArgs e) => Close(TrustDecision.TrustAndRemember);

    private void OnOnceClick(object? sender, RoutedEventArgs e) => Close(TrustDecision.TrustOnce);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(TrustDecision.Reject);
}
