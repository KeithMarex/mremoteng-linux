using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.Views;

/// <summary>The images of Windows mRemoteNG: tree and toolbar images, and the connection icons.</summary>
public static class AppImages
{
    private const string Root = "avares://mRemoteNG.Avalonia/Assets/";
    private const string DefaultConnectionIcon = "mRemoteNG";

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();
    private static readonly Lazy<IReadOnlyList<string>> IconNames = new(ListConnectionIcons);

    /// <summary>The connection icons there are, by name (the connection's Icon property), sorted.</summary>
    public static IReadOnlyList<string> ConnectionIconNames => IconNames.Value;

    public static Bitmap? RootNode => Image("ASPWebSite_16x");

    public static Bitmap? Folder => Image("FolderClosed_16x");

    /// <summary>An image of Windows mRemoteNG's Resources (such as "FolderClosed_16x").</summary>
    public static Bitmap? Image(string name) => Load($"{Root}Images/{name}.png");

    /// <summary>A connection icon; an unknown name gets the mRemoteNG icon, as on Windows.</summary>
    public static Bitmap? ConnectionIcon(string? name) =>
        (string.IsNullOrEmpty(name) ? null : Load($"{Root}ConnectionIcons/{Uri.EscapeDataString(name)}.png"))
        ?? Load($"{Root}ConnectionIcons/{DefaultConnectionIcon}.png");

    public static Bitmap? MessageIcon(MessageClass messageClass) => messageClass switch
    {
        MessageClass.ErrorMsg => Image("LogError_16x"),
        MessageClass.WarningMsg => Image("LogWarning_16x"),
        _ => Image("StatusInformation_16x")
    };

    private static Bitmap? Load(string uri) => Cache.GetOrAdd(uri, static key =>
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri(key));
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            return null; // not an asset (or no Avalonia platform, in tests)
        }
    });

    private static IReadOnlyList<string> ListConnectionIcons()
    {
        try
        {
            return AssetLoader.GetAssets(new Uri($"{Root}ConnectionIcons/"), null)
                              .Select(uri => Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(uri.AbsolutePath)))
                              .Order(StringComparer.OrdinalIgnoreCase)
                              .ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return [DefaultConnectionIcon];
        }
    }
}
