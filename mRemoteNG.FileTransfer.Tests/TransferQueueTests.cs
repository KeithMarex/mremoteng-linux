using NUnit.Framework;

namespace mRemoteNG.FileTransfer.Tests;

public class TransferQueueTests
{
    private string _local = null!;
    private FakeRemoteFileSystem _remote = null!;
    private TransferQueue _queue = null!;

    [SetUp]
    public void Setup()
    {
        _local = Path.Combine(Path.GetTempPath(), "mremoteng-filetransfer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_local);
        _remote = new FakeRemoteFileSystem();
        _queue = new TransferQueue(_ => Task.FromResult<IRemoteFileSystem>(_remote));
    }

    [TearDown]
    public async Task Teardown()
    {
        await _queue.DisposeAsync();
        Directory.Delete(_local, recursive: true);
    }

    [Test]
    public async Task AFileIsDownloadedWithoutLeavingAPartialFile()
    {
        _remote.AddFile("/srv/report.txt", "quarterly numbers");

        TransferItem item = _queue.Download(_remote.Entry("/srv/report.txt"), _local, overwrite: false);
        await Finished(item);

        Assert.That(item.State, Is.EqualTo(TransferState.Done));
        Assert.That(File.ReadAllText(Path.Combine(_local, "report.txt")), Is.EqualTo("quarterly numbers"));
        Assert.That(Directory.GetFiles(_local), Has.Length.EqualTo(1));
        Assert.That((item.BytesDone, item.BytesTotal), Is.EqualTo((17L, 17L)));
    }

    [Test]
    public async Task ADirectoryIsDownloadedWithEverythingInIt()
    {
        _remote.AddFile("/srv/site/index.html", "<html>");
        _remote.AddFile("/srv/site/css/main.css", "body{}");

        TransferItem item = _queue.Download(_remote.Entry("/srv/site"), _local, overwrite: false);
        await Finished(item);

        Assert.That(item.State, Is.EqualTo(TransferState.Done));
        Assert.That(File.ReadAllText(Path.Combine(_local, "site", "css", "main.css")), Is.EqualTo("body{}"));
        Assert.That(item.BytesTotal, Is.EqualTo(12));
    }

    [Test]
    public async Task ADirectoryIsUploadedWithEverythingInIt()
    {
        Directory.CreateDirectory(Path.Combine(_local, "photos", "2026"));
        File.WriteAllText(Path.Combine(_local, "photos", "2026", "a.jpg"), "jpeg");
        _remote.AddDirectory("/upload");

        TransferItem item = _queue.Upload(Path.Combine(_local, "photos"), "/upload", overwrite: false);
        await Finished(item);

        Assert.That(item.State, Is.EqualTo(TransferState.Done));
        Assert.That(_remote.Content("/upload/photos/2026/a.jpg"), Is.EqualTo("jpeg"));
    }

    [Test]
    public async Task ExistingFilesAreSkippedUnlessOverwriting()
    {
        _remote.AddFile("/srv/a.txt", "new");
        File.WriteAllText(Path.Combine(_local, "a.txt"), "old");

        TransferItem skip = _queue.Download(_remote.Entry("/srv/a.txt"), _local, overwrite: false);
        await Finished(skip);
        Assert.That((File.ReadAllText(Path.Combine(_local, "a.txt")), skip.FilesSkipped), Is.EqualTo(("old", 1)));

        TransferItem overwrite = _queue.Download(_remote.Entry("/srv/a.txt"), _local, overwrite: true);
        await Finished(overwrite);
        Assert.That(File.ReadAllText(Path.Combine(_local, "a.txt")), Is.EqualTo("new"));
    }

    [Test]
    public async Task ANameFromTheServerCannotEscapeTheDownloadDirectory()
    {
        _remote.AddDirectory("/srv/evil");
        _remote.AddListingEntry("/srv/evil", new RemoteEntry("../escaped.txt", "/srv/escaped.txt", RemoteEntryKind.File, 3, null, ""));
        _remote.AddFile("/srv/escaped.txt", "bad");

        TransferItem item = _queue.Download(_remote.Entry("/srv/evil"), _local, overwrite: true);
        await Finished(item);

        Assert.That(item.State, Is.EqualTo(TransferState.Failed));
        Assert.That(item.Error, Does.Contain("not allowed"));
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(_local)!, "escaped.txt")), Is.False);
    }

    [Test]
    public async Task AFailedDownloadLeavesNoFile()
    {
        _remote.AddFile("/srv/big.iso", "data");
        _remote.FailDownloads = true;

        TransferItem item = _queue.Download(_remote.Entry("/srv/big.iso"), _local, overwrite: false);
        await Finished(item);

        Assert.That((item.State, item.Error), Is.EqualTo((TransferState.Failed, "connection lost")));
        Assert.That(Directory.GetFiles(_local), Is.Empty);
    }

    [Test]
    public async Task ACancelledTransferEndsAsCancelled()
    {
        _remote.AddFile("/srv/a.txt", "a");
        _remote.Gate = new TaskCompletionSource();

        TransferItem first = _queue.Download(_remote.Entry("/srv/a.txt"), _local, overwrite: true);
        TransferItem second = _queue.Download(_remote.Entry("/srv/a.txt"), _local, overwrite: true);
        second.Cancel();
        first.Cancel();
        _remote.Gate.SetResult();
        await Finished(first);
        await Finished(second);

        Assert.That((first.State, second.State), Is.EqualTo((TransferState.Cancelled, TransferState.Cancelled)));
        Assert.That(Directory.GetFiles(_local), Is.Empty);
    }

    private static async Task Finished(TransferItem item)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!item.IsFinished && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.That(item.IsFinished, Is.True, "the transfer did not finish");
    }
}
