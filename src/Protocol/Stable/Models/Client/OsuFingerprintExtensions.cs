using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Client;
using Basil.Domain.Cryptography;
using Basil.Domain.Shared;

namespace Basil.Protocol.Stable.Models.Client;

/// <summary>
///     Reads and writes the client hash the osu! client sends at login and with each score:
///     <c>osuPath:adapters:adaptersHash:uninstall:disk:</c>, ending with a colon.
/// </summary>
public static class OsuFingerprintExtensions
{
	/// <summary>Reads a client fingerprint from its wire text.</summary>
	/// <param name="text">The wire text, including the trailing colon.</param>
	/// <returns>The client fingerprint.</returns>
	/// <exception cref="FormatException">
	///     <paramref name="text" /> lacks the trailing colon, does not have exactly five components, or a component is
	///     not a valid hash or adapters string.
	/// </exception>
	public static OsuFingerprint Parse(string text)
	{
		return TryParse(text, out var fingerprint)
			? fingerprint
			: throw new FormatException("The client hash is malformed.");
	}

	/// <summary>Reads a client fingerprint from its wire text without throwing.</summary>
	/// <param name="text">The wire text, or <see langword="null" />.</param>
	/// <param name="fingerprint">The client fingerprint when the text is well formed; otherwise the default value.</param>
	/// <returns><see langword="true" /> if <paramref name="text" /> was read; otherwise, <see langword="false" />.</returns>
	public static bool TryParse([NotNullWhen(true)] string? text, out OsuFingerprint fingerprint)
	{
		fingerprint = default;
		if (text is null || !text.EndsWith(':')) return false;

		var parts = text[..^1].Split(':');
		if (parts.Length != 5) return false;

		try
		{
			fingerprint = new OsuFingerprint(new Md5(parts[0]), new Md5(parts[3]), new Md5(parts[4]),
				new NetworkAdapters { Adapters = parts[1], Hash = new Md5(parts[2]) });
			return true;
		}
		catch (Exception ex) when (ex is ArgumentException or FormatException)
		{
			return false;
		}
	}

	/// <summary>Returns the wire text of a client fingerprint, ending with a colon.</summary>
	/// <param name="fingerprint">The client fingerprint.</param>
	/// <returns>The wire text, with the hashes in lowercase.</returns>
	public static string ToText(this OsuFingerprint fingerprint)
	{
		return $"{fingerprint.PathHash}:{fingerprint.NetworkAdapters.Adapters}:{fingerprint.NetworkAdapters.Hash}:" +
		       $"{fingerprint.UninstallHash}:{fingerprint.DiskSignatureHash}:";
	}
}