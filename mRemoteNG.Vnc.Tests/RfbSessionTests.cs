using System.Buffers.Binary;
using System.Collections.Concurrent;
using NUnit.Framework;

namespace mRemoteNG.Vnc.Tests;

public class RfbSessionTests
{
    private const uint Red = 0x00FF0000;
    private const uint Green = 0x0000FF00;
    private const uint Blue = 0x000000FF;
    private const uint White = 0x00FFFFFF;

    private RfbTestServer _server = null!;
    private RfbClient _client = null!;
    private BlockingCollection<ScreenRect> _updates = null!;
    private BlockingCollection<Exception?> _closed = null!;

    [SetUp]
    public async Task Setup()
    {
        _updates = new BlockingCollection<ScreenRect>();
        _closed = new BlockingCollection<Exception?>();
        _server = new RfbTestServer();
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", _server.Port, new RfbLogin(), CancellationToken.None);
        await _server.OfferAsync(1);
        _server.Accept(64, 32);
        _client = await connecting;
        _client.Updated += _updates.Add;
        _client.Closed += _closed.Add;
        _client.Start();
    }

    [TearDown]
    public void Teardown()
    {
        _client.Dispose();
        _server.Dispose();
    }

    [Test]
    public void RawPixelsAreDrawnAndANewUpdateIsAskedFor()
    {
        _server.BeginUpdate(1);
        _server.Rect(2, 3, 2, 1, encoding: 0);
        _server.Pixel(Red);
        _server.Pixel(Green);

        Assert.That(NextUpdate(), Is.EqualTo(new ScreenRect(2, 3, 2, 1)));
        Assert.That((Pixel(2, 3), Pixel(3, 3), Pixel(4, 3)), Is.EqualTo((Red, Green, 0u)));
        Assert.That(_server.Read(10), Is.EqualTo(new byte[] { 3, 1, 0, 0, 0, 0, 0, 64, 0, 32 }));
    }

    [Test]
    public void CopyRectCopiesAnotherPartOfTheScreen()
    {
        _server.BeginUpdate(2);
        _server.Rect(0, 0, 2, 1, encoding: 0);
        _server.Pixel(Red);
        _server.Pixel(Blue);
        _server.Rect(1, 0, 2, 1, encoding: 1);
        _server.Write([0, 0, 0, 0]);

        NextUpdate();
        Assert.That((Pixel(0, 0), Pixel(1, 0), Pixel(2, 0)), Is.EqualTo((Red, Red, Blue)));
    }

    [Test]
    public void RreFillsTheBackgroundAndSubRectangles()
    {
        _server.BeginUpdate(1);
        _server.Rect(10, 10, 4, 4, encoding: 2);
        _server.WriteUInt32(1);
        _server.Pixel(White);
        _server.Pixel(Red);
        _server.Write([0, 1, 0, 1, 0, 2, 0, 2]);

        NextUpdate();
        Assert.That((Pixel(10, 10), Pixel(11, 11), Pixel(12, 12), Pixel(13, 13)), Is.EqualTo((White, Red, Red, White)));
    }

    [Test]
    public void HextileDrawsRawAndSubRectangleTiles()
    {
        // 20×4: a 16×4 tile with a background and one coloured sub-rectangle, then a raw 4×4 tile.
        _server.BeginUpdate(1);
        _server.Rect(0, 0, 20, 4, encoding: 5);
        _server.Write([2 | 8 | 16]);
        _server.Pixel(White);
        _server.Write([1]);
        _server.Pixel(Blue);
        _server.Write([0x21, 0x10]); // At (2, 1), 2×1.
        _server.Write([1]);
        for (int i = 0; i < 16; i++)
            _server.Pixel(Green);

        NextUpdate();
        Assert.That((Pixel(0, 0), Pixel(2, 1), Pixel(3, 1), Pixel(4, 1), Pixel(17, 2)), Is.EqualTo((White, Blue, Blue, White, Green)));
    }

    [Test]
    public void ZrleTilesDecodeAcrossRectangles()
    {
        // A solid tile, then (on the same zlib stream) a palette tile and a run-length tile.
        _server.BeginUpdate(1);
        _server.Rect(0, 0, 4, 2, encoding: 16);
        _server.Zrle([1, .. Compact(Red)]);
        NextUpdate();

        _server.BeginUpdate(2);
        _server.Rect(4, 0, 4, 1, encoding: 16);
        _server.Zrle([2, .. Compact(White), .. Compact(Blue), 0b0101_0000]); // 1 bit per pixel: white, blue, white, blue.
        _server.Rect(8, 0, 4, 2, encoding: 16);
        _server.Zrle([128, .. Compact(Green), 2, .. Compact(Red), 4]); // Runs of 3 and 5.
        NextUpdate();

        Assert.That((Pixel(0, 0), Pixel(3, 1)), Is.EqualTo((Red, Red)));
        Assert.That(Enumerable.Range(4, 4).Select(x => Pixel(x, 0)), Is.EqualTo(new[] { White, Blue, White, Blue }));
        Assert.That(Enumerable.Range(8, 4).Select(x => Pixel(x, 0)).Concat(Enumerable.Range(8, 4).Select(x => Pixel(x, 1))),
                    Is.EqualTo(new[] { Green, Green, Green, Red, Red, Red, Red, Red }));
    }

    [Test]
    public void ANewScreenSizeReplacesTheFramebuffer()
    {
        ManualResetEventSlim resized = new();
        _client.Resized += resized.Set;

        _server.BeginUpdate(1);
        _server.Rect(0, 0, 100, 50, encoding: -223);

        Assert.That(resized.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That((_client.Framebuffer.Width, _client.Framebuffer.Height), Is.EqualTo((100, 50)));
        Assert.That(_server.Read(10)[6..], Is.EqualTo(new byte[] { 0, 100, 0, 50 }));
    }

    [Test]
    public void ThePointerShapeComesWithItsMask()
    {
        BlockingCollection<CursorShape> shapes = new();
        _client.CursorChanged += shapes.Add;

        _server.BeginUpdate(1);
        _server.Rect(1, 0, 2, 1, encoding: -239);
        _server.Pixel(Red);
        _server.Pixel(Blue);
        _server.Write([0b1000_0000]);

        Assert.That(shapes.TryTake(out CursorShape? shape, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That((shape!.HotX, shape.HotY, shape.Pixels[0], shape.Pixels[1]), Is.EqualTo((1, 0, Red | 0xFF000000, 0u)));
    }

    [Test]
    public void ARectangleOutsideTheScreenEndsTheSession()
    {
        _server.BeginUpdate(1);
        _server.Rect(60, 0, 10, 1, encoding: 0);

        Assert.That(_closed.TryTake(out Exception? error, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(error, Is.TypeOf<RfbException>());
    }

    [Test]
    public void KeysPointerAndClipboardAreSent()
    {
        _client.SendKey(0xFF0D, down: true);
        _client.SendPointer(300, 2, buttons: 0b101);
        _client.SendClipboard("héllo\r\n");

        Assert.That(_server.Read(8), Is.EqualTo(new byte[] { 4, 1, 0, 0, 0, 0, 0xFF, 0x0D }));
        Assert.That(_server.Read(6), Is.EqualTo(new byte[] { 5, 0b101, 0x01, 0x2C, 0, 2 }));
        Assert.That(_server.Read(8 + 6), Is.EqualTo(new byte[] { 6, 0, 0, 0, 0, 0, 0, 6, (byte)'h', 0xE9, (byte)'l', (byte)'l', (byte)'o', (byte)'\n' }));
    }

    [Test]
    public void TheRemoteClipboardAndBellAreReported()
    {
        BlockingCollection<string> clipboard = new();
        ManualResetEventSlim bell = new();
        _client.ClipboardChanged += clipboard.Add;
        _client.Bell += bell.Set;

        _server.Write([2]);
        _server.Write([3, 0, 0, 0]);
        _server.WriteUInt32(4);
        _server.Write([(byte)'c', (byte)'a', (byte)'f', 0xE9]);

        Assert.That(bell.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(clipboard.TryTake(out string? text, TimeSpan.FromSeconds(10)) ? text : null, Is.EqualTo("café"));
    }

    [Test]
    public void TheServerClosingIsReported()
    {
        _server.Dispose();

        Assert.That(_closed.TryTake(out Exception? error, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void ClosingHereIsReportedWithoutAnError()
    {
        _client.Dispose();

        Assert.That(_closed.TryTake(out Exception? error, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(error, Is.Null);
    }

    private ScreenRect NextUpdate()
    {
        Assert.That(_updates.TryTake(out ScreenRect rect, TimeSpan.FromSeconds(10)), Is.True, "no update");
        return rect;
    }

    private uint Pixel(int x, int y)
    {
        lock (_client.Framebuffer.SyncRoot)
            return _client.Framebuffer.Pixels[y * _client.Framebuffer.Width + x];
    }

    private static byte[] Compact(uint pixel) => [(byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16)];
}
