namespace Basil.Infrastructure.Scores.Storage;

internal sealed class SliceStream(Stream inner, long start, long length) : Stream
{
	private long _position;

	public override bool CanRead => true;

	public override bool CanSeek => true;

	public override bool CanWrite => false;

	public override long Length { get; } = length;

	public override long Position
	{
		get => _position;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			_position = value;
		}
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return Read(buffer.AsSpan(offset, count));
	}

	public override int Read(Span<byte> buffer)
	{
		var window = Window(buffer.Length);
		if (window == 0) return 0;

		inner.Position = start + _position;
		var read = inner.Read(buffer[..window]);
		_position += read;
		return read;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
	{
		var window = Window(buffer.Length);
		if (window == 0) return 0;

		inner.Position = start + _position;
		var read = await inner.ReadAsync(buffer[..window], ct);
		_position += read;
		return read;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
	{
		return ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
	}

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

	public override void Flush()
	{
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	public override ValueTask DisposeAsync()
	{
		return inner.DisposeAsync();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) inner.Dispose();
		base.Dispose(disposing);
	}

	private int Window(int wanted)
	{
		return (int)Math.Clamp(Length - _position, 0, wanted);
	}
}