namespace Basil.Infrastructure.Scores.Storage;

/// <summary>A read-only, seekable view of a part of another stream; disposing it disposes that stream.</summary>
internal sealed class SliceStream : Stream
{
	private readonly Stream _inner;
	private readonly long _start;
	private long _position;

	/// <summary>
	///     Creates a view of <paramref name="length" /> bytes of <paramref name="inner" /> from <paramref name="start" />
	///     .
	/// </summary>
	/// <param name="inner">The seekable stream to read from; it belongs to the view afterwards.</param>
	/// <param name="start">Where the part starts in <paramref name="inner" />.</param>
	/// <param name="length">How many bytes the part takes.</param>
	public SliceStream(Stream inner, long start, long length)
	{
		_inner = inner;
		_start = start;
		Length = length;
	}

	/// <inheritdoc />
	public override bool CanRead => true;
	/// <inheritdoc />
	public override bool CanSeek => true;
	/// <inheritdoc />
	public override bool CanWrite => false;
	/// <inheritdoc />
	public override long Length { get; }

	/// <inheritdoc />
	public override long Position
	{
		get => _position;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			_position = value;
		}
	}

	/// <inheritdoc />
	public override int Read(byte[] buffer, int offset, int count)
	{
		return Read(buffer.AsSpan(offset, count));
	}

	/// <inheritdoc />
	public override int Read(Span<byte> buffer)
	{
		var window = Window(buffer.Length);
		if (window == 0) return 0;

		_inner.Position = _start + _position;
		var read = _inner.Read(buffer[..window]);
		_position += read;
		return read;
	}

	/// <inheritdoc />
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
	{
		var window = Window(buffer.Length);
		if (window == 0) return 0;

		_inner.Position = _start + _position;
		var read = await _inner.ReadAsync(buffer[..window], ct);
		_position += read;
		return read;
	}

	/// <inheritdoc />
	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
	{
		return ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
	}

	/// <inheritdoc />
	public override long Seek(long offset, SeekOrigin origin)
	{
		Position = origin switch
		{
			SeekOrigin.Begin => offset,
			SeekOrigin.Current => _position + offset,
			SeekOrigin.End => Length + offset,
			_ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
		};
		return _position;
	}

	/// <inheritdoc />
	public override void Flush()
	{
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

	/// <inheritdoc />
	public override ValueTask DisposeAsync()
	{
		return _inner.DisposeAsync();
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		if (disposing) _inner.Dispose();
		base.Dispose(disposing);
	}

	/// <summary>How many bytes of a read of <paramref name="wanted" /> bytes still lie inside the part.</summary>
	private int Window(int wanted)
	{
		return (int)Math.Clamp(Length - _position, 0, wanted);
	}
}