using Basil.Infrastructure.Scores.Storage;

namespace Basil.Infrastructure.Scores.Tests.Storage;

/// <summary>Contract tests of SliceStream: a read-only, seekable view of one part of another stream.</summary>
public sealed class SliceStreamTests
{
	private static readonly byte[] Source = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

	[Fact]
	public void The_view_has_the_length_of_the_part_and_starts_at_its_beginning()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.Equal(4, slice.Length);
		Assert.Equal(0, slice.Position);
	}

	[Fact]
	public void Reading_to_the_end_gives_only_the_part()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.Equal([3, 4, 5, 6], ReadAll(slice));
	}

	[Fact]
	public void A_read_larger_than_the_part_stops_at_its_end_and_the_next_read_gives_nothing()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);
		var buffer = new byte[10];

		Assert.Equal(4, slice.Read(buffer, 0, buffer.Length));
		Assert.Equal(0, slice.Read(buffer, 0, buffer.Length));
	}

	[Fact]
	public void Reading_does_not_depend_on_where_the_inner_stream_was_left()
	{
		var inner = new MemoryStream(Source) { Position = 8 };
		using var slice = new SliceStream(inner, 3, 4);

		Assert.Equal([3, 4, 5, 6], ReadAll(slice));
	}

	[Theory]
	[InlineData(SeekOrigin.Begin, 1, new byte[] { 4, 5, 6 })]
	[InlineData(SeekOrigin.Current, 2, new byte[] { 5, 6 })]
	[InlineData(SeekOrigin.End, -1, new byte[] { 6 })]
	public void Seeking_moves_within_the_part(SeekOrigin origin, long offset, byte[] expected)
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		slice.Seek(offset, origin);

		Assert.Equal(expected, ReadAll(slice));
	}

	[Fact]
	public void Setting_the_position_reads_from_there()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		slice.Position = 3;

		Assert.Equal([6], ReadAll(slice));
	}

	[Fact]
	public void A_negative_position_is_refused()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.Throws<ArgumentOutOfRangeException>(() => slice.Position = -1);
	}

	[Fact]
	public async Task Reading_asynchronously_gives_only_the_part()
	{
		await using var slice = new SliceStream(new MemoryStream(Source), 3, 4);
		using var copy = new MemoryStream();

		await slice.CopyToAsync(copy, TestContext.Current.CancellationToken);

		Assert.Equal([3, 4, 5, 6], copy.ToArray());
	}

	[Fact]
	public void The_view_cannot_be_written()
	{
		using var slice = new SliceStream(new MemoryStream(Source), 3, 4);

		Assert.False(slice.CanWrite);
		Assert.Throws<NotSupportedException>(() => slice.Write([1], 0, 1));
		Assert.Throws<NotSupportedException>(() => slice.SetLength(1));
	}

	[Fact]
	public void Disposing_the_view_disposes_the_stream_it_reads_from()
	{
		var inner = new MemoryStream(Source);

		new SliceStream(inner, 3, 4).Dispose();

		Assert.False(inner.CanRead);
	}

	[Fact]
	public async Task Disposing_the_view_asynchronously_disposes_the_stream_it_reads_from()
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