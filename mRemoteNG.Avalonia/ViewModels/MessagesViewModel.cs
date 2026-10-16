using System.Collections.ObjectModel;
using System.Collections.Specialized;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public sealed record MessageItem(DateTime Date, MessageClass Class, string Text)
{
    public string Time => Date.ToString("HH:mm:ss");
    public string Level => Class switch
    {
        MessageClass.ErrorMsg => "Error",
        MessageClass.WarningMsg => "Warning",
        MessageClass.InformationMsg => "Info",
        _ => "Debug"
    };
}

/// <summary>The notifications panel: messages from Core's message collector that are meant for the user.</summary>
public sealed class MessagesViewModel
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
            _dispatch(Items.Clear);
        }
    }

    private void Add(IMessage message)
    {
        // Log-only and debug messages are not shown in the panel (as in the WinForms notification panel defaults).
        if (message.OnlyLog || message.Class == MessageClass.DebugMsg)
            return;
        Items.Add(new MessageItem(message.Date, message.Class, message.Text));
    }
}
