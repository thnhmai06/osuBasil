using System.Text;
using Basil.Shared.Structures.Cryptography;


namespace Basil.Domain.Tests.Cryptography;

public sealed class Sha256Tests
{
	[Theory]
	[InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
	[InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
	public void SomeContent_LowerHexDigest(string content, string expected)
	{
		Assert.Equal(expected, new Sha256Hash(Encoding.UTF8.GetBytes(content)).ToString());
	}

	[Fact]
	public void UpperCaseDigest_KeptLowercase()
	{
		const string Text = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

		Assert.Equal(new Sha256Hash(Text.ToLowerInvariant()), new Sha256Hash(Text));
	}

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")]
	[InlineData("za7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
	public void NotHex64_Refused(string text)
	{
		Assert.Throws<ArgumentException>(() => new Sha256Hash(text));
	}

	[Fact]
	public async Task Stream_EqualsBytesDigest()
	{
		using var stream = new MemoryStream("abc"u8.ToArray());

		Assert.Equal(new Sha256Hash("abc"u8.ToArray()),
			await Sha256Hash.ComputeAsync(stream, TestContext.Current.CancellationToken));
	}
}