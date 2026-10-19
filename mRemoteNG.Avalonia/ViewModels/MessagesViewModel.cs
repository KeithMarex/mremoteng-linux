using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.Avalonia.Views;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public sealed record MessageItem(DateTime Date, MessageClass Class, string Text)
{
    public string Time => Date.ToString("HH:mm:ss");

    /// <summary>When, in full, for the details pane.</summary>
    public string DateText => Date.ToString("G");

    public string Level => Class switch
    {
        MessageClass.ErrorMsg => "Error",
        MessageClass.WarningMsg => "Warning",
        MessageClass.InformationMsg => "Info",
        _ => "Debug"
    };

    /// <summary>The icon Windows shows for the kind of message.</summary>
    public Bitmap? Icon => AppImages.MessageIcon(Class);
}

/// <summary>
/// The notifications panel: messages from Core's message collector that are meant for the user. As on Windows, the
/// newest message is shown in full beside the list until another one is selected.
/// </summary>
public sealed partial class MessagesViewModel : ObservableObject
{
    private readonly Action<Action> _dispatch;

    /// <param name="dispatch">Runs an action on the UI thread; messages can arrive from any thread.</param>
    public MessagesViewModel(MessageCollector collector, Action<Action> dispatch)
    {
        _dispatch = dispatch;
        foreach (IMessage message in collector.Messages)
            Add(message);
        collector.CollectionChanged += OnMessagesChanged;
    }

    public ObservableCollection<MessageItem> Items { get; } = [];

    [ObservableProperty]
    private MessageItem? _selected;

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            IMessage[] added = e.NewItems.OfType<IMessage>().ToArray();
            _dispatch(() =>
            {
                foreach (IMessage message in added)
                    Add(message);
            });
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _dispatch(() =>
            {
                Items.Clear();
                Selected = null;
            });
        }
    }

    private void Add(IMessage message)
    {
        // Log-only and debug messages are not shown in the panel (as in the WinForms notification panel defaults).
        if (message.OnlyLog || message.Class == MessageClass.DebugMsg)
            return;
        MessageItem item = new(message.Date, message.Class, message.Text);
        Items.Add(item);
        Selected = item;
    }
}
