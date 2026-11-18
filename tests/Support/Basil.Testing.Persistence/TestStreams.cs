using System.Text;

namespace Basil.Testing.Persistence;

public static class TestStreams
{
	public static MemoryStream Of(string text)
	{
		return new MemoryStream(Encoding.UTF8.GetBytes(text));
	}

	public static async Task<string> ReadAllAsync(Stream? stream)
	{
		Assert.NotNull(stream);
		await using (stream)
		{
			using var reader = new StreamReader(stream, Encoding.UTF8);
			return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
		}
	}

	public sealed class Failing : Stream
	{
		private bool _delivered;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => 0;
			set => throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_delivered) throw new IOException("The transfer failed.");
			_delivered = true;
			buffer[offset] = (byte)'x';
			return 1;
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

	public sealed class Gated(string first, string last) : Stream
	{
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _stage;

		public Task Started => _started.Task;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => 0;
			set => throw new NotSupportedException();
		}

		public void Release()
		{
			_released.TrySetResult();
		}

		public override void Flush()
		{
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
			CancellationToken ct = default)
		{
			switch (_stage++)
			{
				case 0:
					var firstBytes = Encoding.UTF8.GetBytes(first);
					firstBytes.CopyTo(buffer);
					_started.TrySetResult();
					return firstBytes.Length;
				case 1:
					await _released.Task.WaitAsync(ct);
					var lastBytes = Encoding.UTF8.GetBytes(last);
					lastBytes.CopyTo(buffer);
					return lastBytes.Length;
				default:
					return 0;
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
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
}