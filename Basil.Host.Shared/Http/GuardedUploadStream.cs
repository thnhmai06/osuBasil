namespace Basil.Host.Shared.Http;

/// <summary>Passes an upload's bytes on while checking its first bytes against a signature and counting it against a limit.</summary>
internal sealed class GuardedUploadStream(Stream inner, long limit, UploadSignature signature) : Stream
{
	private readonly byte[] _header = new byte[UploadSignatures.HeaderLength];

	private int _headerLength;

	private int _headerRead;

	private bool _checked;

	private long _total;

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
	{
		if (!_checked) await CheckHeaderAsync(ct);

		if (_headerRead < _headerLength)
		{
			var count = Math.Min(buffer.Length, _headerLength - _headerRead);
			_header.AsSpan(_headerRead, count).CopyTo(buffer.Span);
			_headerRead += count;
			return count;
		}

		var read = await inner.ReadAsync(buffer, ct);
		Count(read);
		return read;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
	{
		return ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
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

	private async Task CheckHeaderAsync(CancellationToken ct)
	{
		_checked = true;
		if (signature == UploadSignature.None) return;

		_headerLength = await inner.ReadAtLeastAsync(_header, _header.Length, false, ct);
		Count(_headerLength);
		if (!signature.Matches(_header.AsSpan(0, _headerLength)))
			throw new UploadRefusedException(UploadRefusal.WrongContent);
	}

	private void Count(int read)
	{
		_total += read;
		if (_total > limit) throw new UploadRefusedException(UploadRefusal.TooLarge);
	}
}
