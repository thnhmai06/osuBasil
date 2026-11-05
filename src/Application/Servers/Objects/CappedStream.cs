namespace Basil.Application.Servers.Objects;

/// <summary>Signals that an upload is larger than its limit.</summary>
internal sealed class ArchiveTooLargeException : Exception;

/// <summary>Reads another stream and fails once more than a limit of bytes has been read.</summary>
internal sealed class CappedStream(Stream inner, long limit) : Stream
{
	private long _read;

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return Counted(inner.Read(buffer, offset, count));
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
		CancellationToken cancellationToken = default)
	{
		return Counted(await inner.ReadAsync(buffer, cancellationToken));
	}

	private int Counted(int read)
	{
		_read += read;
		return _read > limit ? throw new ArchiveTooLargeException() : read;
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		throw new NotSupportedException();
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}
}