using Basil.Domain.Cryptography;

namespace Basil.Domain.Tests.Cryptography;

/// <summary>An MD5 digest reads from 32 hexadecimal characters in either case, and refuses anything else without throwing.</summary>
public sealed class Md5Tests
{
	[Theory]
	[InlineData("d41d8cd98f00b204e9800998ecf8427e")]
	[InlineData("D41D8CD98F00B204E9800998ECF8427E")]
	public void TryParse_32HexCharacters_ReturnsTheLowerCaseDigest(string text)
	{
		Assert.True(Md5.TryParse(text, null, out var digest));
		Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", digest.ToString());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("d41d8cd98f00b204e9800998ecf8427")]
	[InlineData("d41d8cd98f00b204e9800998ecf8427ee")]
	[InlineData("g41d8cd98f00b204e9800998ecf8427e")]
	public void TryParse_NotADigest_ReturnsFalse(string? text)
	{
		Assert.False(Md5.TryParse(text, null, out _));
	}

	[Fact]
	public void Parse_NotADigest_ThrowsFormatException()
	{
		Assert.Throws<FormatException>(() => Md5.Parse("nope"));
	}
}
