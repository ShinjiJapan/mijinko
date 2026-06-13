namespace Filer.Core.Tests;

/// <summary>
/// テスト用のメモリ全二重ストリーム対。2本の <see cref="ByteChannel"/> で繋いだ
/// 2つの端点を返し、片方の Write がもう片方の Read に届く(名前付きパイプの代用)。
/// 読みと書きを別スレッドで同時に使え、Dispose(=切断)で相手の Read が 0 を返す。
/// </summary>
internal sealed class DuplexStream : Stream
{
    private readonly ByteChannel _read;
    private readonly ByteChannel _write;

    private DuplexStream(ByteChannel read, ByteChannel write)
    {
        _read = read;
        _write = write;
    }

    public static (DuplexStream, DuplexStream) CreatePair()
    {
        var a2b = new ByteChannel();
        var b2a = new ByteChannel();
        return (new DuplexStream(b2a, a2b), new DuplexStream(a2b, b2a));
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;

    public override int Read(byte[] buffer, int offset, int count) =>
        _read.Read(buffer.AsSpan(offset, count));

    public override void Write(byte[] buffer, int offset, int count) =>
        _write.Write(buffer.AsSpan(offset, count));

    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _write.Close();   // 切断: 相手の Read が 0(EOF)を返すように
        base.Dispose(disposing);
    }

    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>ブロッキングのバイトチャネル(生産者/消費者)。</summary>
    private sealed class ByteChannel
    {
        private readonly object _gate = new();
        private readonly Queue<byte[]> _chunks = new();
        private byte[]? _current;
        private int _pos;
        private bool _closed;

        public void Write(ReadOnlySpan<byte> data)
        {
            var copy = data.ToArray();
            lock (_gate)
            {
                if (_closed) return;
                _chunks.Enqueue(copy);
                Monitor.PulseAll(_gate);
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                Monitor.PulseAll(_gate);
            }
        }

        public int Read(Span<byte> buffer)
        {
            lock (_gate)
            {
                while (true)
                {
                    if (_current is not null && _pos < _current.Length)
                    {
                        var n = Math.Min(buffer.Length, _current.Length - _pos);
                        _current.AsSpan(_pos, n).CopyTo(buffer);
                        _pos += n;
                        return n;
                    }
                    if (_chunks.Count > 0)
                    {
                        _current = _chunks.Dequeue();
                        _pos = 0;
                        continue;
                    }
                    if (_closed) return 0;
                    Monitor.Wait(_gate);
                }
            }
        }
    }
}
