using Luxel.DevTools;

namespace Luxel.Tests;

/// <summary>
/// <see cref="FrameChannel"/> (Q05 F1: ライブフレームのリングバッファ) の GPU 不要テスト。
/// body 形状 / latest-wins rev / 304 相当 / リサイズ / 二読者所有権 (書き手が読み取り中スロットを踏まない) を検証する。
/// </summary>
public class FrameChannelTests
{
    private static byte[] Rgba(int w, int h, byte fill)
    {
        var b = new byte[w * h * 4];
        Array.Fill(b, fill);
        return b;
    }

    [Fact]
    public void Publish_Then_Read_ReturnsHeaderAndRgba()
    {
        var ch = new FrameChannel();
        Assert.Equal(0, ch.Rev);
        Assert.Null(ch.Read(null).body);              // 未発行は null

        ch.Publish(2, 1, Rgba(2, 1, 0x7F));
        (byte[]? body, long rev) = ch.Read(null);
        Assert.NotNull(body);
        Assert.Equal(1, rev);
        Assert.Equal(8 + 2 * 1 * 4, body!.Length);
        Assert.Equal(2, BitConverter.ToInt32(body, 0));   // width LE
        Assert.Equal(1, BitConverter.ToInt32(body, 4));   // height LE
        Assert.All(body[8..], x => Assert.Equal(0x7F, x));
    }

    [Fact]
    public void Read_SameRev_Returns304()
    {
        var ch = new FrameChannel();
        ch.Publish(1, 1, Rgba(1, 1, 1));
        (byte[]? _, long rev) = ch.Read(null);
        Assert.Null(ch.Read(rev).body);               // 同 rev → null (= 304)
        ch.Publish(1, 1, Rgba(1, 1, 2));
        Assert.NotNull(ch.Read(rev).body);            // 進んだら本体
    }

    [Fact]
    public void LatestWins_RevEqualsPublishCount()
    {
        var ch = new FrameChannel();
        for (int i = 0; i < 100; i++) ch.Publish(1, 1, Rgba(1, 1, (byte)i));
        (byte[]? body, long rev) = ch.Read(null);
        Assert.Equal(100, rev);
        Assert.Equal(99, body![8]);                    // 最新の fill
    }

    [Fact]
    public void Resize_GrowsBody()
    {
        var ch = new FrameChannel();
        ch.Publish(2, 1, Rgba(2, 1, 5));
        Assert.Equal(8 + 2 * 1 * 4, ch.Read(null).body!.Length);
        ch.Publish(4, 2, Rgba(4, 2, 6));               // 拡大
        (byte[]? body, _) = ch.Read(null);
        Assert.Equal(8 + 4 * 2 * 4, body!.Length);
        Assert.Equal(4, BitConverter.ToInt32(body, 0));
        Assert.Equal(2, BitConverter.ToInt32(body, 4));
    }

    [Fact]
    public void Publish_IgnoresUndersizedSource()
    {
        var ch = new FrameChannel();
        ch.Publish(4, 4, new byte[8]);                 // len 不足 → 無視
        Assert.Equal(0, ch.Rev);
    }

    // ---- 二読者所有権: 書き手が高速に周回しても、読み手は常に「破れていない」1 枚を得る ----
    // 各フレームは全 tight バイトを rev%256 で塗る。torn なら body 内でバイトが混在するので検出できる。
    [Fact]
    public void ConcurrentReaders_NeverSeeTornFrame()
    {
        const int W = 64, H = 48;      // 12KB tight — コピー中に書き手が周回しうるサイズ
        const int Frames = 20000;
        var ch = new FrameChannel(slots: 3);
        ch.Publish(W, H, Rgba(W, H, 0));   // 初期フレーム

        var stop = new ManualResetEventSlim(false);
        Exception? readerError = null;

        void Reader()
        {
            try
            {
                var src = new byte[W * H * 4];
                while (!stop.IsSet)
                {
                    (byte[]? body, _) = ch.Read(null);
                    if (body is null) continue;
                    Assert.Equal(W, BitConverter.ToInt32(body, 0));
                    Assert.Equal(H, BitConverter.ToInt32(body, 4));
                    byte first = body[8];
                    for (int i = 8; i < body.Length; i++)
                        if (body[i] != first) { readerError = new Xunit.Sdk.XunitException("torn frame: byte mismatch"); return; }
                }
            }
            catch (Exception e) { readerError = e; }
        }

        var r1 = new Thread(Reader) { IsBackground = true };
        var r2 = new Thread(Reader) { IsBackground = true };   // 二読者 (HTTP + 島スレッド相当)
        r1.Start(); r2.Start();

        var fill = new byte[W * H * 4];
        for (int f = 1; f <= Frames && readerError is null; f++)
        {
            Array.Fill(fill, (byte)(f & 0xFF));
            ch.Publish(W, H, fill);
        }
        stop.Set();
        r1.Join(); r2.Join();

        Assert.Null(readerError);
    }
}
