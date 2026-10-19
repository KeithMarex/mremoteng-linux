using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.FileTransfer;
using mRemoteNG.FileTransfer.Tests;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

/// <summary>The file transfer tab's columns: sorting by a column header and the widths, both remembered in the settings.</summary>
public class FileListColumnsTests
{
    private FakeRemoteFileSystem _server = null!;
    private AppSettings _settings = null!;
    private int _saves;

    [SetUp]
    public void Setup()
    {
        _server = new FakeRemoteFileSystem();
        // The sizes the server reports for directories are not shown, so they do not count when sorting by size.
        Add("Alpha", RemoteEntryKind.Directory, 12288, new DateTime(2025, 1, 1), "drwx------");
        Add("beta", RemoteEntryKind.Directory, 4096, new DateTime(2026, 1, 1), "drwxr-xr-x");
        Add("a.txt", RemoteEntryKind.File, 300, new DateTime(2026, 3, 1), "-rw-r--r--");
        Add("B.log", RemoteEntryKind.File, 10, null, "-rw-------");
        Add("c.bin", RemoteEntryKind.File, 2000, new DateTime(2024, 6, 1), "-rwxr-xr-x");
        _settings = new AppSettings();
        _saves = 0;
    }

    [Test]
    public async Task ByDefaultTheListIsSortedByNameWithTheDirectoriesFirst()
    {
        RemotePaneViewModel pane = await Pane();

        Assert.That(Names(pane), Is.EqualTo(new[] { "..", "Alpha", "beta", "a.txt", "B.log", "c.bin" }));
        Assert.That(pane.Columns.Select(column => column.SortMark), Is.EqualTo(new[] { "▲", "", "", "" }));
    }

    [Test]
    public async Task ClickingAColumnSortsAscendingAndAgainDescendingWithTheDirectoriesOnTop()
    {
        RemotePaneViewModel pane = await Pane();

        pane.SizeColumn.SortCommand.Execute(null);
        Assert.That(Names(pane), Is.EqualTo(new[] { "..", "Alpha", "beta", "B.log", "a.txt", "c.bin" }));
        Assert.That(pane.SizeColumn.SortMark, Is.EqualTo("▲"));
        Assert.That(pane.NameColumn.SortMark, Is.Empty);

        pane.SizeColumn.SortCommand.Execute(null);
        Assert.That(Names(pane), Is.EqualTo(new[] { "..", "Alpha", "beta", "c.bin", "a.txt", "B.log" }));
        Assert.That(pane.SizeColumn.SortMark, Is.EqualTo("▼"));

        pane.SortBy(FileSortColumn.Name);
        Assert.That(pane.SortDescending, Is.False, "another column starts ascending");
        pane.SortBy(FileSortColumn.Name);
        Assert.That(Names(pane), Is.EqualTo(new[] { "..", "beta", "Alpha", "c.bin", "B.log", "a.txt" }));
    }

    [Test]
    public async Task SortingByDateAndPermissions()
    {
        RemotePaneViewModel pane = await Pane();

        pane.SortBy(FileSortColumn.Modified);
        Assert.That(Names(pane), Is.EqualTo(new[] { "..", "Alpha", "beta", "B.log", "c.bin", "a.txt" }), "no date comes first");

        pane.SortBy(FileSortColumn.Permissions);
        Assert.That(Names(pane), Is.EqualTo(new[] { "..", "Alpha", "beta", "B.log", "a.txt", "c.bin" }));
    }

    [Test]
    public async Task SortingKeepsTheSelection()
    {
        RemotePaneViewModel pane = await Pane();
        FileEntryViewModel[] chosen = pane.Entries.Where(entry => entry.Name is "a.txt" or "c.bin").ToArray();
        foreach (FileEntryViewModel entry in chosen)
            pane.SelectedEntries.Add(entry);

        pane.SortBy(FileSortColumn.Size);

        Assert.That(pane.SelectedEntries, Is.EquivalentTo(chosen));
    }

    [Test]
    public async Task SortOrderAndWidthsAreStoredForNewTabs()
    {
        RemotePaneViewModel pane = await Pane();

        pane.SortBy(FileSortColumn.Size);
        pane.SortBy(FileSortColumn.Size);
        pane.NameColumn.Width = 400;
        pane.SaveLayout();

        Assert.That(_saves, Is.EqualTo(3));
        Assert.That(_settings.RemoteFileList.SortColumn, Is.EqualTo(FileSortColumn.Size));
        Assert.That(_settings.RemoteFileList.SortDescending, Is.True);
        Assert.That(_settings.RemoteFileList.NameWidth, Is.EqualTo(400));
        Assert.That(_settings.LocalFileList.SortColumn, Is.EqualTo(FileSortColumn.Name), "this computer's side is apart");

        RemotePaneViewModel next = await Pane();
        Assert.That(next.NameColumn.Width, Is.EqualTo(400));
        Assert.That(next.SizeColumn.SortMark, Is.EqualTo("▼"));
        Assert.That(Names(next), Is.EqualTo(new[] { "..", "Alpha", "beta", "c.bin", "a.txt", "B.log" }));
    }

    [Test]
    public void StoredWidthsOutOfRangeAndAnUnknownColumnAreCorrected()
    {
        FileListLayout layout = new()
        {
            SortColumn = (FileSortColumn)7, NameWidth = 0, SizeWidth = -5, ModifiedWidth = 99999, PermissionsWidth = double.NaN
        };

        RemotePaneViewModel pane = new(layout, () => { });

        Assert.That(pane.Columns.Select(column => column.Width), Is.EqualTo(new[] { 60, 40, FileColumnViewModel.MaxWidth, 40 }));
        Assert.That(pane.SortColumn, Is.EqualTo(FileSortColumn.Name));
        Assert.That(pane.NameColumn.SortMark, Is.EqualTo("▲"));
    }

    [Test]
    public void EachSideOfTheTabUsesItsOwnLayout()
    {
        _settings.LocalFileList.SortColumn = FileSortColumn.Modified;
        _settings.RemoteFileList.SizeWidth = 111;
        FakeDialogService dialogs = new();
        ConnectionInfo connection = new() { Name = "files", Protocol = ProtocolType.SFTP, Hostname = "files.example" };

        using FileTransferViewModel tab = new(connection, (_, _) => throw new InvalidOperationException(), dialogs,
                                              new FileTransferPrompts(dialogs, new CertificatePins(new AppSettings(), () => { })),
                                              action => action(), _settings, () => _saves++);
        tab.Remote.SaveLayout();

        Assert.That(tab.Local.SortColumn, Is.EqualTo(FileSortColumn.Modified));
        Assert.That(tab.Remote.SortColumn, Is.EqualTo(FileSortColumn.Name));
        Assert.That(tab.Remote.SizeColumn.Width, Is.EqualTo(111));
        Assert.That(_saves, Is.EqualTo(1), "the app's settings are saved");
    }

    private async Task<RemotePaneViewModel> Pane()
    {
        RemotePaneViewModel pane = new(_settings.RemoteFileList, () => _saves++);
        pane.Attach(_server);
        Assert.That(await pane.NavigateAsync("/data"), Is.True);
        return pane;
    }

    private void Add(string name, RemoteEntryKind kind, long size, DateTime? modified, string permissions) =>
        _server.AddListingEntry("/data", new RemoteEntry(name, "/data/" + name, kind, size, modified, permissions));

    private static string[] Names(FilePaneViewModel pane) => pane.Entries.Select(entry => entry.Name).ToArray();
}
