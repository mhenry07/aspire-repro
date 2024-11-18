namespace AspireRepro.Worker;

public sealed class SemaphoreStream(Stream innerStream) : Stream
{
    private readonly Stream _innerStream = innerStream;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanTimeout => _innerStream.CanTimeout;
    public override bool CanWrite => _innerStream.CanWrite;

    public override long Length
    {
        get
        {
            using var _ = Wait();
            return _innerStream.Length;
        }
    }

    public override long Position
    {
        get
        {
            using var _ = Wait();
            return _innerStream.Position;
        }

        set
        {
            using var _ = Wait();
            _innerStream.Position = value;
        }
    }

    public override int ReadTimeout
    {
        get
        {
            using var _ = Wait();
            return _innerStream.ReadTimeout;
        }
        set
        {
            using var _ = Wait();
            _innerStream.ReadTimeout = value;
        }
    }

    public override int WriteTimeout
    {
        get
        {
            using var _ = Wait();
            return _innerStream.WriteTimeout;
        }
        set
        {
            using var _ = Wait();
            _innerStream.WriteTimeout = value;
        }
    }

    public override void Close()
    {
        using var _ = Wait();
        try
        {
            _innerStream.Close();
        }
        finally
        {
            base.Dispose(true);
        }
    }

    public override void CopyTo(Stream destination, int bufferSize) => throw new NotSupportedException();

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        using var _ = Wait();
        try
        {
            if (disposing)
                ((IDisposable)_innerStream).Dispose();
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        using var _ = await WaitAsync(default);
        await _innerStream.DisposeAsync();
    }

    public override void Flush()
    {
        using var _ = Wait();
        _innerStream.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        using var _ = await WaitAsync(cancellationToken);
        await _innerStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        using var _ = Wait();
        return _innerStream.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var _ = await WaitAsync(cancellationToken);
        return await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override int ReadByte()
    {
        using var _ = Wait();
        return _innerStream.ReadByte();
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => throw new NotSupportedException();

    public override int EndRead(IAsyncResult asyncResult)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
    {
        using var _ = Wait();
        return _innerStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        using var _ = Wait();
        _innerStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        using var _ = Wait();
        _innerStream.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var _ = await WaitAsync(cancellationToken);
        await _innerStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override void WriteByte(byte value)
    {
        using var _ = Wait();
        _innerStream.WriteByte(value);
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => throw new NotSupportedException();

    public override void EndWrite(IAsyncResult asyncResult)
        => throw new NotSupportedException();

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
