using System.Text;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>Builds the content streams the file storage tests hand to a storage.</summary>
internal static class TestStreams
{
	/// <summary>Creates a stream over the UTF-8 bytes of a text.</summary>
	/// <param name="text">The text.</param>
	/// <returns>The stream, positioned at its start.</returns>
	public static MemoryStream Of(string text)
	{
		return new MemoryStream(Encoding.UTF8.GetBytes(text));
	}

	/// <summary>Reads a stream to its end as UTF-8 text and disposes it.</summary>
	/// <param name="stream">The stream; <see langword="null" /> fails the test.</param>
	/// <returns>The text.</returns>
	public static async Task<string> ReadAllAsync(Stream? stream)
	{
		Assert.NotNull(stream);
		await using (stream)
		{
			using var reader = new StreamReader(stream, Encoding.UTF8);
			return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
		}
	}

	/// <summary>A stream that delivers some bytes and then fails, the way an upload that dies mid-transfer fails.</summary>
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

	/// <summary>A stream that delivers its first part at once and its last part only after it is released.</summary>
	/// <param name="first">The text delivered at once.</param>
	/// <param name="last">The text delivered after <see cref="Release" />.</param>
	public sealed class Gated(string first, string last) : Stream
	{
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _stage;

		/// <summary>Gets a task that ends once the first part has been read.</summary>
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

		/// <summary>Lets the last part be read.</summary>
		public void Release()
		{
			_released.TrySetResult();
		}

		public override void Flush()
		{
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
			CancellationToken cancellationToken = default)
		{
			switch (_stage++)
			{
				case 0:
					var firstBytes = Encoding.UTF8.GetBytes(first);
					firstBytes.CopyTo(buffer);
					_started.TrySetResult();
					return firstBytes.Length;
				case 1:
					await _released.Task.WaitAsync(cancellationToken);
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