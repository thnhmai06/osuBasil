using Basil.Domain.Cryptography;

namespace Basil.Domain.Tests.Cryptography;

/// <summary>A SHA-256 digest is 64 lower-case hexadecimal characters; the expected values are the FIPS 180-4 test vectors.</summary>
public sealed class Sha256Tests
{
	[Theory]
	[InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
	[InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
	public void SomeContent_LowerHexDigest(string content, string expected)
	{
		Assert.Equal(expected, new Sha256(System.Text.Encoding.UTF8.GetBytes(content)).ToString());
	}

	[Fact]
	public void UpperCaseDigest_KeptLowercase()
	{
		const string Text = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

		Assert.Equal(new Sha256(Text.ToLowerInvariant()), new Sha256(Text));
	}

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")]
	[InlineData("za7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
	public void NotHex64_Refused(string text)
	{
		Assert.Throws<ArgumentException>(() => new Sha256(text));
	}

	[Fact]
	public async Task Stream_EqualsBytesDigest()
	{
		using var stream = new MemoryStream("abc"u8.ToArray());

		Assert.Equal(new Sha256("abc"u8.ToArray()), await Sha256.ComputeAsync(stream, TestContext.Current.CancellationToken));
	}
}
