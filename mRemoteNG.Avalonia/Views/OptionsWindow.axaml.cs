using Avalonia.Controls;
using Avalonia.Interactivity;

namespace mRemoteNG.Avalonia.Views;

/// <summary>Edits an OptionsViewModel; closes with true on OK.</summary>
public partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
