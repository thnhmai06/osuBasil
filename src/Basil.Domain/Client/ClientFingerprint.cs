using System.Diagnostics.CodeAnalysis;

namespace Basil.Domain.Client;

/// <summary>
///     Represents the client fingerprint captured from an osu! client at login.
/// </summary>
/// <remarks>
///     Captured once at login and re-checked against every score submission from that session, as
///     part of the submission integrity validation.
/// </remarks>
[method: SetsRequiredMembers]
public readonly record struct ClientFingerprint(
	string OsuPathMd5,
	NetworkAdapters NetworkAdapters,
	string UninstallMd5,
	string DiskSignatureMd5)
	: IParsable<ClientFingerprint>
{
	/// <summary>The MD5 hash of the osu! executable path.</summary>
	public string OsuPathMd5 { get; init; } = OsuPathMd5;

	/// <summary>The combined network adapter.</summary>
	public required NetworkAdapters NetworkAdapters { get; init; } = NetworkAdapters;

	/// <summary>The MD5 hash of the uninstallation identifier.</summary>
	public required string UninstallMd5 { get; init; } = UninstallMd5;

	/// <summary>The MD5 hash of the disk signature.</summary>
	public required string DiskSignatureMd5 { get; init; } = DiskSignatureMd5;

	public bool Match(ClientFingerprint other)
	{
		return NetworkAdapters.IsRunningUnderWine
			? UninstallMd5 == other.UninstallMd5
			: UninstallMd5 == other.UninstallMd5 ||
			  NetworkAdapters == other.NetworkAdapters ||
			  DiskSignatureMd5 == other.DiskSignatureMd5;
	}

	public static ClientFingerprint Parse(string s, IFormatProvider? provider = null)
	{
		var hashParts = s[..^1].Split(':', 5);

		var osuPathMd5 = hashParts[0];
		var adaptersString = hashParts[1];
		var adaptersMd5 = hashParts[2];
		var uninstallMd5 = hashParts[3];
		var diskSignatureMd5 = hashParts[4];

		var adapters = new NetworkAdapters(adaptersString, adaptersMd5);

		return new ClientFingerprint(osuPathMd5, adapters, uninstallMd5,
			diskSignatureMd5);
	}

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ClientFingerprint result)
	{
		try
		{
			result = Parse(s!, provider);
			return true;
		}
		catch (Exception)
		{
			result = default;
			return false;
		}
	}
}