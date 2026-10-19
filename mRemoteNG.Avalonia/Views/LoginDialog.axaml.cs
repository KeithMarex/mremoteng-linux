using Avalonia.Controls;
using Avalonia.Interactivity;
using mRemoteNG.Avalonia.Services;

namespace mRemoteNG.Avalonia.Views;

/// <summary>Asks for a user name, domain and password; closes with null when cancelled.</summary>
public partial class LoginDialog : Window
{
    public LoginDialog() : this(string.Empty, new LoginCredentials(string.Empty, string.Empty, string.Empty))
    {
    }

    public LoginDialog(string prompt, LoginCredentials initial)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        UserBox.Text = initial.Username;
        DomainBox.Text = initial.Domain;
        PasswordBox.Text = initial.Password;
        // Start where something is missing.
        Opened += (_, _) => (initial.Username.Length == 0 ? UserBox : PasswordBox).Focus();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        LoginCredentials result = new((UserBox.Text ?? string.Empty).Trim(), (DomainBox.Text ?? string.Empty).Trim(),
                                      PasswordBox.Text ?? string.Empty);
        PasswordBox.Text = string.Empty;
        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
