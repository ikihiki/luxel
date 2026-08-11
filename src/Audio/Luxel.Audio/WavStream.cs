using System.Buffers.Binary;

namespace Luxel.Audio;

/// <summary>
/// WAV (RIFF PCM) を**逐次**デコードする <see cref="IAudioStream"/> — 依存なしの自前パーサ。
/// 16-bit 整数 PCM (fmt タグ 1) と 32-bit float (fmt タグ 3) に対応。ヘッダは構築時に読み、
/// data チャンクは <see cref="Read"/> で必要な分だけ下層 <see cref="Stream"/> から読み込む (全展開しない)。
/// <see cref="Reset"/> は data 先頭へ seek する (下層はシーク可能であること — FileStream 等)。
/// </summary>
public sealed class WavStream : IAudioStream
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly long _dataStart;
    private readonly long _dataBytes;
    private readonly int _sampleBytes;   // 1 チャンネル 1 サンプルのバイト数 (2 or 4)
    private readonly bool _isFloat;
    private readonly byte[] _buf = new byte[8192];
    private long _consumed;               // data 内で読んだバイト数

    public int SampleRate { get; }
    public int Channels { get; }

    /// <summary>WAV ファイルを開く便利メソッド (ストリームの寿命は返す <see cref="WavStream"/> が持つ)。</summary>
    public static WavStream Open(string path) => new(File.OpenRead(path));

    public WavStream(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;

        Span<byte> h = stackalloc byte[12];
        ReadExact(h);
        if (h[0] != 'R' || h[1] != 'I' || h[2] != 'F' || h[3] != 'F' || h[8] != 'W' || h[9] != 'A' || h[10] != 'V' || h[11] != 'E')
            throw new FormatException("WAV: RIFF/WAVE ヘッダではありません。");

        int channels = 0, sampleRate = 0, bits = 0, fmtTag = 0;
        long dataStart = -1, dataBytes = 0;
        Span<byte> ch = stackalloc byte[8];
        while (dataStart < 0)
        {
            if (!TryReadExact(ch)) throw new FormatException("WAV: data チャンクが見つかりません。");
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(ch[4..]);
            if (ch[0] == 'f' && ch[1] == 'm' && ch[2] == 't' && ch[3] == ' ')
            {
                Span<byte> fmt = stackalloc byte[16];
                ReadExact(fmt);
                fmtTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                Skip(size - 16);   // fmt 拡張分を飛ばす
            }
            else if (ch[0] == 'd' && ch[1] == 'a' && ch[2] == 't' && ch[3] == 'a')
            {
                dataStart = _stream.Position;
                dataBytes = size;
            }
            else
            {
                Skip(size + (size & 1));   // 未知チャンク (奇数長は 1B パディング)
            }
        }

        if (channels <= 0 || sampleRate <= 0) throw new FormatException("WAV: fmt チャンクが不正です。");
        _isFloat = fmtTag == 3;
        if (fmtTag == 1 && bits == 16) _sampleBytes = 2;
        else if (fmtTag == 3 && bits == 32) _sampleBytes = 4;
        else throw new NotSupportedException($"WAV: 未対応の形式 (tag={fmtTag}, bits={bits}) — 16bit PCM か 32bit float のみ。");

        SampleRate = sampleRate;
        Channels = channels;
        _dataStart = dataStart;
        _dataBytes = dataBytes;
        _consumed = 0;
    }

    public int Read(Span<float> dst)
    {
        long remainBytes = _dataBytes - _consumed;
        int maxSamples = (int)Math.Min(dst.Length, remainBytes / _sampleBytes);
        if (maxSamples <= 0) return 0;

        int produced = 0;
        int perBatch = _buf.Length / _sampleBytes;
        while (produced < maxSamples)
        {
            int batch = Math.Min(maxSamples - produced, perBatch);
            int want = batch * _sampleBytes;
            int got = ReadUpTo(_buf, want);
            int gotSamples = got / _sampleBytes;
            for (int i = 0; i < gotSamples; i++)
                dst[produced + i] = Decode(_buf, i * _sampleBytes);
            produced += gotSamples;
            _consumed += (long)gotSamples * _sampleBytes;
            if (gotSamples < batch) break;   // 下層が尽きた
        }
        return produced;
    }

    public void Reset()
    {
        _stream.Seek(_dataStart, SeekOrigin.Begin);
        _consumed = 0;
    }

    private float Decode(byte[] b, int off)
        => _isFloat
            ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(off))
            : BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(off)) / 32768f;

    private void ReadExact(Span<byte> dst)
    {
        if (!TryReadExact(dst)) throw new EndOfStreamException("WAV: ヘッダが途中で終わっています。");
    }

    private bool TryReadExact(Span<byte> dst)
    {
        int total = 0;
        while (total < dst.Length)
        {
            int n = _stream.Read(dst[total..]);
            if (n == 0) return false;
            total += n;
        }
        return true;
    }

    private int ReadUpTo(byte[] dst, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = _stream.Read(dst, total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private void Skip(long count)
    {
        if (count <= 0) return;
        if (_stream.CanSeek) { _stream.Seek(count, SeekOrigin.Current); return; }
        long left = count;
        while (left > 0)
        {
            int n = _stream.Read(_buf, 0, (int)Math.Min(_buf.Length, left));
            if (n == 0) break;
            left -= n;
        }
    }

    public void Dispose()
    {
        if (!_leaveOpen) _stream.Dispose();
    }
}
