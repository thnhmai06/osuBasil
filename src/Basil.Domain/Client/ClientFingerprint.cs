using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Basil.Domain.Client;

/// <summary>
///     Represents the client fingerprint captured from an osu! client at login.
/// </summary>
/// <remarks>
///     The fingerprint is the osu! client hash: five colon-separated components identifying the
///     installation (and, after re-installation, the machine) a player is using:
///     <c>osuPathMd5:adapters:adaptersMd5:uninstallMd5:diskSignatureMd5:</c>.
/// </remarks>
public readonly partial record struct ClientFingerprint : IParsable<ClientFingerprint>, IFormattable
{
	/// <summary>
	///     Creates a fingerprint from its five components.
	/// </summary>
	/// <param name="osuPathMd5">The MD5 checksum of the osu! installation path.</param>
	/// <param name="networkAdapters">The network adapter string and its MD5 checksum.</param>
	/// <param name="uninstallMd5">The MD5 checksum identifying the osu! uninstall entry.</param>
	/// <param name="diskSignatureMd5">The MD5 checksum of the disk signature.</param>
	/// <exception cref="FormatException">
	///     An MD5 checksum is not a valid 32-character hexadecimal string, or
	///     <paramref name="networkAdapters" /> is not a valid adapter pair.
	/// </exception>
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

	/// <summary>The MD5 checksum of the osu! installation path.</summary>
	public string OsuPathMd5 { get; }

	/// <summary>The network adapter string and its MD5 checksum reported by the client.</summary>
	public NetworkAdapters NetworkAdapters { get; }

	/// <summary>The MD5 checksum identifying the osu! uninstall entry.</summary>
	public string UninstallMd5 { get; }

	/// <summary>The MD5 checksum of the disk signature reported by the client.</summary>
	public string DiskSignatureMd5 { get; }


	/// <summary>
	///     Formats this fingerprint as the osu! client hash used by the protocol.
	/// </summary>
	/// <remarks>
	///     Produces <c>osuPathMd5:adapters:adaptersMd5:uninstallMd5:diskSignatureMd5:</c>, ending
	///     with the trailing colon delimiter.
	/// </remarks>
	/// <param name="format">Ignored; the canonical client hash is always produced.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <returns>The client hash string.</returns>
	public string ToString(string? format = null, IFormatProvider? formatProvider = null)
	{
		return $"{OsuPathMd5}:{NetworkAdapters.Adapters}:{NetworkAdapters.Md5}:" +
		       $"{UninstallMd5}:{DiskSignatureMd5}:";
	}

	/// <summary>
	///     Parses a client fingerprint string.
	/// </summary>
	/// <param name="s">
	///     The fingerprint in <c>osuPathMd5:adapters:adaptersMd5:uninstallMd5:diskSignatureMd5:</c>
	///     form, including the trailing colon.
	/// </param>
	/// <param name="provider">Ignored.</param>
	/// <returns>The parsed fingerprint.</returns>
	/// <exception cref="FormatException">
	///     <paramref name="s" /> does not end with a colon, does not contain exactly five
	///     colon-separated components, or contains an invalid component.
	/// </exception>
	public static ClientFingerprint Parse(string s, IFormatProvider? provider = null)
	{
		if (!s.EndsWith(':')) throw new FormatException("Client fingerprint is missing trailing delimiter.");
		var parts = s[..^1].Split(':', 5);

		return parts.Length == 5
			? new ClientFingerprint(parts[0], new NetworkAdapters(parts[1], parts[2]), parts[3], parts[4])
			: throw new FormatException("Client fingerprint must contain 5 components.");
	}

	/// <summary>
	///     Attempts to parse a client fingerprint string.
	/// </summary>
	/// <param name="s">The fingerprint string to parse, or <see langword="null" />.</param>
	/// <param name="provider">Ignored.</param>
	/// <param name="result">
	///     When this method returns <see langword="true" />, the parsed fingerprint; otherwise, the
	///     default value.
	/// </param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="s" /> was parsed successfully; otherwise,
	///     <see langword="false" />.
	/// </returns>
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

	/// <summary>
	///     Determines whether this fingerprint matches <paramref name="other" /> closely enough to
	///     represent the same osu! installation.
	/// </summary>
	/// <remarks>
	///     When this fingerprint's client is running under Wine, only the uninstall MD5 must match.
	///     Otherwise the fingerprints match when the uninstall MD5, the network adapters, or the
	///     disk signature MD5 agree.
	/// </remarks>
	/// <param name="other">The fingerprint to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the fingerprints match under the Wine-aware comparison rules;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool MatchWith(ClientFingerprint other)
	{
		return NetworkAdapters.IsRunningUnderWine
			? UninstallMd5 == other.UninstallMd5
			: UninstallMd5 == other.UninstallMd5
			  || NetworkAdapters == other.NetworkAdapters
			  || DiskSignatureMd5 == other.DiskSignatureMd5;
	}

	/// <summary>
	///     Returns the fingerprint in the canonical protocol format, with no format specification or
	///     provider.
	/// </summary>
	/// <returns>The client hash string.</returns>
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