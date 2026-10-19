using Avalonia.Controls;
using Avalonia.Interactivity;

namespace mRemoteNG.Avalonia.Views;

/// <summary>Edits the external tools; closes with true when the changes are to be kept.</summary>
public partial class ExternalToolsWindow : Window
{
    public ExternalToolsWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
