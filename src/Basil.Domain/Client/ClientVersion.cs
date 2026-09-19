using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Basil.Domain.Client;

/// <summary>
///     Represents the version string an osu! client reports when it connects.
/// </summary>
/// <remarks>
///     The canonical wire format is <c>bYYYYMMDD[.revision][stream]</c>: a literal <c>b</c> prefix,
///     a build date, an optional single-digit revision, and an optional stream suffix. The leading
///     <c>b</c> and a stable stream suffix are omitted when formatting.
/// </remarks>
public readonly partial record struct ClientVersion(DateOnly Date, int? Revision, ClientVersionStream Stream)
	: IParsable<ClientVersion>, IFormattable
{
	/// <summary>
	///     Formats this version as the protocol version string.
	/// </summary>
	/// <remarks>
	///     The date is rendered as <c>yyyyMMdd</c> in the invariant culture. A non-null revision is
	///     appended as <c>.R</c>, and the stream is appended in lowercase unless it is
	///     <see cref="ClientVersionStream.Stable" />, which is omitted entirely. For example,
	///     <c>b20240815.2beta</c> or <c>b20240815</c>.
	/// </remarks>
	/// <param name="format">Ignored; the canonical version string is always produced.</param>
	/// <param name="formatProvider">Ignored; formatting always uses the invariant culture.</param>
	/// <returns>The version string in <c>bYYYYMMDD[.revision][stream]</c> form.</returns>
	public string ToString(string? format = null, IFormatProvider? formatProvider = null)
	{
		var date = Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var revision = Revision is null ? string.Empty : $".{Revision}";
		var stream = Stream == ClientVersionStream.Stable
			? string.Empty
			: Stream.ToString().ToLowerInvariant();

		return $"b{date}{revision}{stream}";
	}

	/// <summary>
	///     Parses an osu! version string into an <see cref="ClientVersion" />.
	/// </summary>
	/// <remarks>
	///     The leading <c>b</c> prefix is optional on input. The date may appear as eight digits
	///     (<c>yyyyMMdd</c>) or in the <c>yyyy.123</c> dotted form accepted by the pattern; only the
	///     undotted form is actually convertible to a <see cref="DateOnly" />.
	/// </remarks>
	/// <param name="s">The version string reported by the client.</param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <returns>The parsed version.</returns>
	/// <exception cref="FormatException">
	///     <paramref name="s" /> does not match the expected version format.
	/// </exception>
	public static ClientVersion Parse(string s, IFormatProvider? provider = null)
	{
		var match = VersionPattern().Match(s);
		if (!match.Success)
			throw new FormatException($"Invalid client version: {s}");

		var dateText = match.Groups["date"].Value;
		var date = DateOnly.ParseExact(dateText, "yyyyMMdd");

		var revisionGroup = match.Groups["revision"];
		int? revision = revisionGroup.Success
			? int.Parse(revisionGroup.Value)
			: null;

		var streamGroup = match.Groups["stream"];
		var stream = streamGroup.Success
			? Enum.Parse<ClientVersionStream>(streamGroup.Value, true)
			: ClientVersionStream.Stable;

		return new ClientVersion(date, revision, stream);
	}

	/// <summary>
	///     Attempts to parse an osu! version string.
	/// </summary>
	/// <param name="s">The version string to parse, or <see langword="null" />.</param>
	/// <param name="provider">The format provider, which is ignored.</param>
	/// <param name="result">
	///     When this method returns <see langword="true" />, the parsed version; otherwise, the
	///     default value.
	/// </param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="s" /> was parsed successfully; otherwise,
	///     <see langword="false" />. A <see langword="null" /> input never parses successfully.
	/// </returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ClientVersion result)
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

	[GeneratedRegex(
		@"^(?:b)?(?<date>(?:\d{8}|\d{4}\.\d{3}))(?:\.(?<revision>\d))?(?<stream>beta|cuttingedge|dev|tourney)?$")]
	private static partial Regex VersionPattern();

	/// <summary>
	///     Returns the version in the canonical protocol format, with no format specification or
	///     provider.
	/// </summary>
	/// <returns>The version string in <c>bYYYYMMDD[.revision][stream]</c> form.</returns>
	public override string ToString()
	{
		return ToString();
	}
}

/// <summary>
///     Represents the release stream of an osu! client.
/// </summary>
public enum ClientVersionStream : byte
{
	/// <summary>The stable release stream.</summary>
	Stable,

	/// <summary>The beta release stream.</summary>
	Beta,

	/// <summary>The cutting-edge release stream.</summary>
	CuttingEdge,

	/// <summary>The tournament build stream.</summary>
	Tourney,

	/// <summary>The developer release stream.</summary>
	Dev
}