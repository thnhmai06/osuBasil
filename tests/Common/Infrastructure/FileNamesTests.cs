using System.Text;
using Basil.Common.Infrastructure.Files;

namespace Basil.Common.Infrastructure.Tests;

/// <summary>Contract tests of the shaping of file names.</summary>
public sealed class FileNamesTests
{
	[Fact]
	public void A_name_that_fits_is_returned_as_it_is()
	{
		Assert.Equal("12 Artist - Title ", FileNames.Fitted("12 Artist - Title ", 100));
	}

	[Fact]
	public void A_name_is_cut_to_the_bytes_it_may_take()
	{
		Assert.Equal("abcde", FileNames.Fitted("abcdefghij", 5));
	}

	[Fact]
	public void A_multi_byte_character_is_never_split()
	{
		// Each character takes 3 bytes in UTF-8, so 7 bytes hold two of them.
		var fitted = FileNames.Fitted(new string('漢', 5), 7);

		Assert.Equal(new string('漢', 2), fitted);
	}

	[Fact]
	public void A_surrogate_pair_is_never_split()
	{
		// U+1F600 takes 4 bytes in UTF-8, so 6 bytes hold one of them.
		var fitted = FileNames.Fitted("ab\U0001F600\U0001F600", 7);

		Assert.Equal("ab\U0001F600", fitted);
		Assert.True(Encoding.UTF8.GetByteCount(fitted) <= 7);
	}

	[Fact]
	public void A_cut_name_loses_the_spaces_and_dots_it_ends_with()
	{
		Assert.Equal("ab", FileNames.Fitted("ab . .cdef", 6));
	}
}