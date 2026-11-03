using Basil.Domain.Common;
using Basil.Protocol.Stable.Models.Client;

namespace Basil.Protocol.Stable.Tests.Models.Auth;

/// <summary>
///     Verifies the client hash contract <c>osuPath:adapters:adaptersHash:uninstall:disk:</c>: exactly five
///     colon-separated components and a required trailing colon, every hash a 32-digit hex string, the adapters a
///     dot-terminated list or the wine marker, and <see cref="OsuFingerprintExtensions.ToText" /> restoring the wire text.
///     Anything else is rejected.
/// </summary>
public class OsuFingerprintExtensionsTests
{
	private const string Path = "0123456789abcdef0123456789abcdef";
	private const string AdaptersHash = "fedcba9876543210fedcba9876543210";
	private const string Uninstall = "00112233445566778899aabbccddeeff";
	private const string Disk = "ffeeddccbbaa99887766554433221100";

	public static TheoryData<string> ValidAdapters()
	{
		return ["1.2.3.", "a.", "runningunderwine"];
	}

	public static TheoryData<string> Rejects()
	{
		return
		[
			// no trailing colon
			$"{Path}:1.2.:{AdaptersHash}:{Uninstall}:{Disk}",
			// four components
			$"{Path}:1.2.:{AdaptersHash}:{Uninstall}:",
			// six components
			$"{Path}:1.2.:{AdaptersHash}:{Uninstall}:{Disk}:{Disk}:",
			// a component that is not a hash
			$"nothex:1.2.:{AdaptersHash}:{Uninstall}:{Disk}:",
			$"{Path}:1.2.:{AdaptersHash}:{Uninstall}:{Disk[..31]}:",
			// adapters that are empty or lack the closing dot
			$"{Path}::{AdaptersHash}:{Uninstall}:{Disk}:",
			$"{Path}:1.2:{AdaptersHash}:{Uninstall}:{Disk}:",
			""
		];
	}

	[Theory]
	[MemberData(nameof(ValidAdapters))]
	public void Parse_FiveComponents_YieldsEveryField(string adapters)
	{
		var fingerprint = OsuFingerprintExtensions.Parse($"{Path}:{adapters}:{AdaptersHash}:{Uninstall}:{Disk}:");

		Assert.Equal(new Md5(Path), fingerprint.PathHash);
		Assert.Equal(adapters, fingerprint.NetworkAdapters.Adapters);
		Assert.Equal(new Md5(AdaptersHash), fingerprint.NetworkAdapters.Hash);
		Assert.Equal(new Md5(Uninstall), fingerprint.UninstallHash);
		Assert.Equal(new Md5(Disk), fingerprint.DiskSignatureHash);
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void TryParse_MalformedText_ReturnsFalse(string text)
	{
		Assert.False(OsuFingerprintExtensions.TryParse(text, out _));
	}

	[Fact]
	public void TryParse_NullText_ReturnsFalse()
	{
		Assert.False(OsuFingerprintExtensions.TryParse(null, out _));
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void Parse_MalformedText_ThrowsFormatException(string text)
	{
		Assert.Throws<FormatException>(() => OsuFingerprintExtensions.Parse(text));
	}

	[Theory]
	[MemberData(nameof(ValidAdapters))]
	public void ToText_ParsedFingerprint_RestoresTheWireText(string adapters)
	{
		var text = $"{Path}:{adapters}:{AdaptersHash}:{Uninstall}:{Disk}:";

		Assert.Equal(text, OsuFingerprintExtensions.ToText(OsuFingerprintExtensions.Parse(text)));
	}

	[Fact]
	public void ToText_UppercaseHashes_AreWrittenInLowercase()
	{
		var fingerprint = OsuFingerprintExtensions.Parse($"{Path.ToUpperInvariant()}:1.:{AdaptersHash}:{Uninstall}:{Disk}:");

		Assert.Equal($"{Path}:1.:{AdaptersHash}:{Uninstall}:{Disk}:", OsuFingerprintExtensions.ToText(fingerprint));
	}
}
