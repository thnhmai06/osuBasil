using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Client;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Protocol.Game.Stable.Models.Client;

public static class ClientFingerprintExtensions
{
	public static ClientFingerprint Parse(string text)
	{
		return TryParse(text, out var fingerprint)
			? fingerprint
			: throw new FormatException("The client hash is malformed.");
	}

	public static bool TryParse([NotNullWhen(true)] string? text, out ClientFingerprint fingerprint)
	{
		fingerprint = default;
		if (text is null || !text.EndsWith(':')) return false;

		var parts = text[..^1].Split(':');
		if (parts.Length != 5) return false;

		try
		{
			fingerprint = new ClientFingerprint(new Md5Hash(parts[0]), new Md5Hash(parts[3]), new Md5Hash(parts[4]),
				new NetworkAdapters { Adapters = parts[1], Hash = new Md5Hash(parts[2]) });
			return true;
		}
		catch (Exception ex) when (ex is ArgumentException or FormatException)
		{
			return false;
		}
	}

	public static string ToText(this ClientFingerprint fingerprint)
	{
		return $"{fingerprint.PathHash}:{fingerprint.NetworkAdapters.Adapters}:{fingerprint.NetworkAdapters.Hash}:" +
		       $"{fingerprint.UninstallHash}:{fingerprint.DiskSignatureHash}:";
	}
}