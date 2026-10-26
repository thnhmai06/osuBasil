using Basil.Protocol.Bancho.Models.Auth;

namespace Basil.Protocol.Bancho.Tests.Models.Auth;

/// <summary>
///     Verifies the client hash contract <c>osuPath:adapters:adaptersHash:uninstall:disk:</c>: exactly five
///     colon-separated components and a required trailing colon, empty components allowed, and
///     <see cref="ClientHash.ToString" /> restoring the wire text. Anything else is rejected.
/// </summary>
public class ClientHashTests
{
	public static TheoryData<string, string, string, string, string, string> ValidHashes()
	{
		return new TheoryData<string, string, string, string, string, string>
		{
			{ "a:b:c:d:e:", "a", "b", "c", "d", "e" },
			{ "aa:bb:cc:dd:ee:", "aa", "bb", "cc", "dd", "ee" },
			{ ":::::", "", "", "", "", "" },
			{ "p1:,2:hx:H:U:", "p1", ",2", "hx", "H", "U" }
		};
	}

	public static TheoryData<string> Rejects()
	{
		return new TheoryData<string>
		{
			// no trailing colon
			"a:b:c:d:e",
			// four components
			"a:b:c:d:",
			// six components
			"a:b:c:d:e:f:",
			""
		};
	}

	[Theory]
	[MemberData(nameof(ValidHashes))]
	public void Parse_FiveComponents_YieldsEveryField(
		string text,
		string osuPathHash,
		string adapters,
		string adaptersHash,
		string uninstallHash,
		string diskSignatureHash)
	{
		var hash = ClientHash.Parse(text);

		Assert.Equal(
			new ClientHash(osuPathHash, adapters, adaptersHash, uninstallHash, diskSignatureHash),
			hash);
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void TryParse_MalformedText_ReturnsFalse(string text)
	{
		Assert.False(ClientHash.TryParse(text, out _));
	}

	[Fact]
	public void TryParse_NullText_ReturnsFalse()
	{
		Assert.False(ClientHash.TryParse(null, out _));
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void Parse_MalformedText_ThrowsFormatException(string text)
	{
		Assert.Throws<FormatException>(() => ClientHash.Parse(text));
	}

	[Theory]
	[MemberData(nameof(ValidHashes))]
	public void ToString_HashOfComponents_BuildsTheWireText(
		string text,
		string osuPathHash,
		string adapters,
		string adaptersHash,
		string uninstallHash,
		string diskSignatureHash)
	{
		var hash = new ClientHash(osuPathHash, adapters, adaptersHash, uninstallHash, diskSignatureHash);

		Assert.Equal(text, hash.ToString());
	}
}