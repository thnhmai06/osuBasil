namespace Basil.Application.Servers.Objects;

/// <summary>Signals that an upload is larger than its limit.</summary>
internal sealed class ArchiveTooLargeException : Exception;

/// <summary>Reads another stream and fails once more than a limit of bytes has been read.</summary>
internal sealed class CappedStream(Stream inner, long limit) : Stream
{
	private long _read;

	/// <inheritdoc />
	public override bool CanRead => true;

	/// <inheritdoc />
	public override bool CanSeek => false;

	/// <inheritdoc />
	public override bool CanWrite => false;

	/// <inheritdoc />
	public override long Length => throw new NotSupportedException();

	/// <inheritdoc />
	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	/// <inheritdoc />
	public override int Read(byte[] buffer, int offset, int count)
	{
		return Counted(inner.Read(buffer, offset, count));
	}

	/// <inheritdoc />
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
		CancellationToken ct = default)
	{
		return Counted(await inner.ReadAsync(buffer, ct));
	}

	private int Counted(int read)
	{
		_read += read;
		return _read > limit ? throw new ArchiveTooLargeException() : read;
	}

	/// <inheritdoc />
	public override void Flush()
	{
	}

	/// <inheritdoc />
	public override long Seek(long offset, SeekOrigin origin)
	{
		throw new NotSupportedException();
	}

	/// <inheritdoc />
	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	/// <inheritdoc />
	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}
}