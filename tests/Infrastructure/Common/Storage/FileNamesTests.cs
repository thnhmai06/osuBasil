using System.Text;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Common.Tests.Storage;

/// <summary>Contract tests of the shaping of file names.</summary>
public sealed class FileNamesTests
{
	[Fact]
	public void FittingName_ReturnedUnchanged()
	{
		Assert.Equal("12 Artist - Title ", FileNames.Fitted("12 Artist - Title ", 100));
	}

	[Fact]
	public void OverlongName_TrimmedToLimit()
	{
		Assert.Equal("abcde", FileNames.Fitted("abcdefghij", 5));
	}

	[Fact]
	public void MultiByteCharacter_NeverSplit()
	{
		// Each character takes 3 bytes in UTF-8, so 7 bytes hold two of them.
		var fitted = FileNames.Fitted(new string('漢', 5), 7);

		Assert.Equal(new string('漢', 2), fitted);
	}

	[Fact]
	public void SurrogatePair_NeverSplit()
	{
		// U+1F600 takes 4 bytes in UTF-8, so 6 bytes hold one of them.
		var fitted = FileNames.Fitted("ab\U0001F600\U0001F600", 7);

		Assert.Equal("ab\U0001F600", fitted);
		Assert.True(Encoding.UTF8.GetByteCount(fitted) <= 7);
	}

	[Fact]
	public void CutName_TrimsTrailingSeparators()
	{
		Assert.Equal("ab", FileNames.Fitted("ab . .cdef", 6));
	}
}