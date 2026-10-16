using System.Security;
using Avalonia.Controls;
using Avalonia.Interactivity;
using mRemoteNG.Security;

namespace mRemoteNG.Avalonia.Views;

/// <summary>Asks for a password; with confirmation it must be typed twice. Closes with null when cancelled.</summary>
public partial class PasswordDialog : Window
{
    private readonly bool _confirm;

    public PasswordDialog() : this(string.Empty, confirm: false)
    {
    }

    public PasswordDialog(string prompt, bool confirm)
    {
        InitializeComponent();
        _confirm = confirm;
        PromptText.Text = prompt;
        ConfirmBox.IsVisible = confirm;
        Opened += (_, _) => PasswordBox.Focus();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        string password = PasswordBox.Text ?? string.Empty;
        if (_confirm && password != (ConfirmBox.Text ?? string.Empty))
        {
            ErrorText.IsVisible = true;
            return;
        }

        SecureString result = password.ConvertToSecureString();
        PasswordBox.Text = ConfirmBox.Text = string.Empty;
        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
