using Avalonia.Controls;
using Avalonia.Interactivity;

namespace mRemoteNG.Avalonia.Views;

/// <summary>Asks for one line of text; closes with null when cancelled.</summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog() : this(string.Empty, string.Empty, string.Empty)
    {
    }

    public TextInputDialog(string title, string prompt, string initialText)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText;
        Opened += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(InputBox.Text ?? string.Empty);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
