using Avalonia.Controls;
using Avalonia.Interactivity;

namespace mRemoteNG.Avalonia.Views;

/// <summary>A message with OK, or a question with Yes/No (closes with true for Yes/OK).</summary>
public partial class MessageDialog : Window
{
    public MessageDialog() : this(string.Empty, string.Empty, isQuestion: false)
    {
    }

    public MessageDialog(string title, string message, bool isQuestion)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        YesButton.IsVisible = NoButton.IsVisible = isQuestion;
        OkButton.IsVisible = !isQuestion;
        YesButton.IsDefault = isQuestion;
    }

    private void OnYesClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNoClick(object? sender, RoutedEventArgs e) => Close(false);
}
