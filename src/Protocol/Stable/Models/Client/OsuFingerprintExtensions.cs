using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Game.Client;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Protocol.Game.Stable.Models.Client;

public static class OsuFingerprintExtensions
{
	public static OsuFingerprint Parse(string text)
	{
		return TryParse(text, out var fingerprint)
			? fingerprint
			: throw new FormatException("The client hash is malformed.");
	}

	public static bool TryParse([NotNullWhen(true)] string? text, out OsuFingerprint fingerprint)
	{
		fingerprint = default;
		if (text is null || !text.EndsWith(':')) return false;

		var parts = text[..^1].Split(':');
		if (parts.Length != 5) return false;

		try
		{
			fingerprint = new OsuFingerprint(new Md5Hash(parts[0]), new Md5Hash(parts[3]), new Md5Hash(parts[4]),
				new NetworkAdapters { Adapters = parts[1], Hash = new Md5Hash(parts[2]) });
			return true;
		}
		catch (Exception ex) when (ex is ArgumentException or FormatException)
		{
			return false;
		}
	}

	public static string ToText(this OsuFingerprint fingerprint)
	{
		return $"{fingerprint.PathHash}:{fingerprint.NetworkAdapters.Adapters}:{fingerprint.NetworkAdapters.Hash}:" +
		       $"{fingerprint.UninstallHash}:{fingerprint.DiskSignatureHash}:";
	}
}