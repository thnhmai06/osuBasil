using Basil.Infrastructure.Scores.Storage;

namespace Basil.Infrastructure.Scores.Tests.Storage;

/// <summary>Contract tests of SliceStream: a read-only, seekable view of one part of another stream.</summary>
public sealed class SliceStreamTests
{
	private static readonly byte[] Source = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

	[Fact]
	public void View_MatchesPartBounds()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.Equal(4, slice.Length);
		Assert.Equal(0, slice.Position);
	}

	[Fact]
	public void EndRead_GivesOnlyPart()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.Equal([3, 4, 5, 6], ReadAll(slice));
	}

	[Fact]
	public void OversizedRead_StopsAtEnd()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);
		var buffer = new byte[10];

		Assert.Equal(4, slice.Read(buffer, 0, buffer.Length));
		Assert.Equal(0, slice.Read(buffer, 0, buffer.Length));
	}

	[Fact]
	public void Reading_IgnoresInnerPosition()
	{
		var inner = new MemoryStream(Source) { Position = 8 };
		using var slice = new SliceStream(inner, 3, 4);

		Assert.Equal([3, 4, 5, 6], ReadAll(slice));
	}

	[Theory]
	[InlineData(SeekOrigin.Begin, 1, new byte[] { 4, 5, 6 })]
	[InlineData(SeekOrigin.Current, 2, new byte[] { 5, 6 })]
	[InlineData(SeekOrigin.End, -1, new byte[] { 6 })]
	public void Seeking_MovesWithinPart(SeekOrigin origin, long offset, byte[] expected)
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		slice.Seek(offset, origin);

		Assert.Equal(expected, ReadAll(slice));
	}

	[Fact]
	public void SetPosition_ReadsFromThere()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		slice.Position = 3;

		Assert.Equal([6], ReadAll(slice));
	}

	[Fact]
	public void NegativePosition_Refused()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.Throws<ArgumentOutOfRangeException>(() => slice.Position = -1);
	}

	[Fact]
	public async Task AsyncRead_GivesOnlyPart()
	{
		await using var slice = new SliceStream(new MemoryStream(Source), 3, 4);
		using var copy = new MemoryStream();

		await slice.CopyToAsync(copy, TestContext.Current.CancellationToken);

		Assert.Equal([3, 4, 5, 6], copy.ToArray());
	}

	[Fact]
	public void View_NotWritable()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.False(slice.CanWrite);
		Assert.Throws<NotSupportedException>(() => slice.Write([1], 0, 1));
		Assert.Throws<NotSupportedException>(() => slice.SetLength(1));
	}

	[Fact]
	public void ViewDisposed_InnerStreamDisposed()
	{
		var inner = new MemoryStream(Source);

		new SliceStream(inner, 3, 4).Dispose();

		Assert.False(inner.CanRead);
	}

	[Fact]
	public async Task AsyncViewDisposal_DisposesInner()
	{
		var inner = new MemoryStream(Source);

		await new SliceStream(inner, 3, 4).DisposeAsync();

		Assert.False(inner.CanRead);
	}

	private static byte[] ReadAll(Stream stream)
	{
		using var copy = new MemoryStream();
		stream.CopyTo(copy);
		return copy.ToArray();
	}
}