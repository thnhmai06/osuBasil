using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Basil.Domain.Client;

/// <summary>
///     Represents the client fingerprint captured from an osu! client at login.
/// </summary>
public readonly partial record struct ClientFingerprint : IParsable<ClientFingerprint>, IFormattable
{
	public string OsuPathMd5 { get; }
	public NetworkAdapters NetworkAdapters { get; }
	public string UninstallMd5 { get; }
	public string DiskSignatureMd5 { get; }

	public ClientFingerprint(string osuPathMd5, NetworkAdapters networkAdapters,
		string uninstallMd5, string diskSignatureMd5)
	{
		ValidateMd5(osuPathMd5, nameof(osuPathMd5));
		ValidateMd5(uninstallMd5, nameof(uninstallMd5));
		ValidateMd5(diskSignatureMd5, nameof(diskSignatureMd5));

		OsuPathMd5 = osuPathMd5;
		NetworkAdapters = networkAdapters;
		UninstallMd5 = uninstallMd5;
		DiskSignatureMd5 = diskSignatureMd5;
	}

	public bool MatchWith(ClientFingerprint other)
	{
		return NetworkAdapters.IsRunningUnderWine
			? UninstallMd5 == other.UninstallMd5
			: UninstallMd5 == other.UninstallMd5
			  || NetworkAdapters == other.NetworkAdapters
			  || DiskSignatureMd5 == other.DiskSignatureMd5;
	}

	public static ClientFingerprint Parse(string s, IFormatProvider? provider = null)
	{
		if (!s.EndsWith(':')) throw new FormatException("Client fingerprint is missing trailing delimiter.");
		var parts = s[..^1].Split(':', 5);

		return parts.Length == 5
			? new ClientFingerprint(parts[0], new NetworkAdapters(parts[1], parts[2]), parts[3], parts[4])
			: throw new FormatException("Client fingerprint must contain 5 components.");
	}


	/// <summary>
	///     Formats this fingerprint as the osu! client hash used by the protocol.
	/// </summary>
	public string ToString(string? format = null, IFormatProvider? formatProvider = null)
	{
		return $"{OsuPathMd5}:{NetworkAdapters.Adapters}:{NetworkAdapters.Md5}:" +
		       $"{UninstallMd5}:{DiskSignatureMd5}:";
	}

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ClientFingerprint result)
	{
		if (s is null)
		{
			result = default;
			return false;
		}

		try
		{
			result = Parse(s, provider);
			return true;
		}
		catch (FormatException)
		{
			result = default;
			return false;
		}
	}

	public override string ToString()
	{
		return ToString();
	}

	[GeneratedRegex("^[a-fA-F0-9]{32}$")]
	private static partial Regex Md5Pattern();

	private static void ValidateMd5(string value, string? paramName = null)
	{
		paramName ??= nameof(value);
		if (!Md5Pattern().IsMatch(value)) throw new FormatException($"{paramName} must be a valid MD5.");
	}
}