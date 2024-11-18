namespace AspireRepro.Worker;

/// <summary>
/// A stream implementation that synchronizes reading and writing so it can be read and written to interchangeably
/// </summary>
public class ReadWriteStream : Stream
{
    private readonly Stream _innerStream = new MemoryStream();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private long _readPosition;
    private long _writePosition;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override long Length
    {
        get
        {
            using var _ = Wait();
            return _innerStream.Length;
        }
    }

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public long ReadPosition
    {
        get
        {
            using var _ = Wait();
            return _readPosition;
        }
    }

    public long WritePosition
    {
        get
        {
            using var _ = Wait();
            return _writePosition;
        }
    }

    public override void CopyTo(Stream destination, int bufferSize) => throw new NotSupportedException();

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public override void Flush() => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        using var _ = Wait();

        var bytesRead = _innerStream.Read(buffer);
        _readPosition += bytesRead;
        return bytesRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var _ = await WaitAsync(cancellationToken);

        var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _readPosition += bytesRead;
        return bytesRead;
    }

    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        var bytesRead = Read(buffer);
        return bytesRead == 1
            ? buffer[0]
            : -1;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        using var _ = Wait();

        _innerStream.Position = _writePosition;
        _innerStream.Write(buffer);
        _writePosition += buffer.Length;
        _innerStream.Position = _readPosition;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var _ = await WaitAsync(cancellationToken);

        _innerStream.Position = _writePosition;
        await _innerStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _writePosition += buffer.Length;
        _innerStream.Position = _readPosition;
    }

    public override void WriteByte(byte value) => Write([value]);

    private SemaphoreScope Wait()
    {
        _semaphore.Wait();
        return new SemaphoreScope(_semaphore);
    }

    private async ValueTask<SemaphoreScope> WaitAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new SemaphoreScope(_semaphore);
    }

    private readonly struct SemaphoreScope(SemaphoreSlim semaphore) : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = semaphore;

        public void Dispose() => _semaphore.Release();
    }
}
