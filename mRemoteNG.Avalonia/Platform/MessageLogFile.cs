using System.Collections.Specialized;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.Platform;

/// <summary>
/// Appends the messages of Core's message collector (also the log-only ones, but not debug messages) to
/// a text file. A file that has grown past <c>maxBytes</c> is moved to "*.1" when logging starts.
/// </summary>
public sealed class MessageLogFile : IDisposable
{
    private readonly MessageCollector _collector;
    private readonly object _lock = new();

    public MessageLogFile(MessageCollector collector, string path, long maxBytes = 1_000_000)
    {
        _collector = collector;
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
            File.Move(path, path + ".1", overwrite: true);

        Write(collector.Messages);
        collector.CollectionChanged += OnMessagesChanged;
    }

    public string Path { get; }

    public void Dispose() => _collector.CollectionChanged -= OnMessagesChanged;

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            Write(e.NewItems.OfType<IMessage>());
    }

    private void Write(IEnumerable<IMessage> messages)
    {
        string[] lines = messages
            .Where(message => message.Class != MessageClass.DebugMsg)
            .Select(message => $"{message.Date:yyyy-MM-dd HH:mm:ss} [{message.Class}] {message.Text}")
            .ToArray();
        if (lines.Length == 0)
            return;

        try
        {
            lock (_lock)
                File.AppendAllLines(Path, lines);
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
