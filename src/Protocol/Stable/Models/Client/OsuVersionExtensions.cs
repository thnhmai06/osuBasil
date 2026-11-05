using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Basil.Domain.Client;

namespace Basil.Protocol.Stable.Models.Client;

/// <summary>
///     Reads and writes the build string an osu! client reports, <c>bYYYYMMDD[.revision][stream]</c>: a <c>b</c>
///     prefix, a build date, an optional single-digit revision and an optional stream suffix.
/// </summary>
public static partial class OsuVersionExtensions
{
	/// <summary>Reads a client version from its wire text.</summary>
	/// <param name="text">The wire text; the leading <c>b</c> is optional.</param>
	/// <returns>The client version.</returns>
	/// <exception cref="FormatException"><paramref name="text" /> is not a client build string.</exception>
	public static OsuVersion Parse(string text)
	{
		return TryParse(text, out var version) ? version : throw new FormatException($"Invalid client build: {text}");
	}

	/// <summary>Reads a client version from its wire text without throwing.</summary>
	/// <param name="text">The wire text, or <see langword="null" />.</param>
	/// <param name="version">The client version when the text is well formed; otherwise the default value.</param>
	/// <returns><see langword="true" /> if <paramref name="text" /> was read; otherwise, <see langword="false" />.</returns>
	public static bool TryParse([NotNullWhen(true)] string? text, out OsuVersion version)
	{
		version = default;
		if (text is null) return false;

		var match = BuildPattern().Match(text);
		if (!match.Success) return false;

		if (!DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
			    DateTimeStyles.None, out var date))
			return false;

		var revisionGroup = match.Groups["revision"];
		int? revision = null;
		if (revisionGroup.Success)
		{
			if (!int.TryParse(revisionGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture,
				    out var parsedRevision))
				return false;

			revision = parsedRevision;
		}

		var streamGroup = match.Groups["stream"];
		var stream = OsuVersionStream.Stable;
		if (streamGroup.Success && !Enum.TryParse(streamGroup.Value, true, out stream))
			return false;

		version = new OsuVersion(date, revision, stream);
		return true;
	}

	/// <summary>
	///     Returns the wire text of a client version: <c>b</c>, the date as <c>yyyyMMdd</c>, <c>.R</c> for a revision
	///     and the lowercase stream name unless the stream is <see cref="OsuVersionStream.Stable" />.
	/// </summary>
	/// <param name="version">The client version.</param>
	/// <returns>The wire text, for example <c>b20240815.2beta</c> or <c>b20240815</c>.</returns>
	public static string ToText(this OsuVersion version)
	{
		var date = version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var revision = version.Revision is null ? string.Empty : $".{version.Revision}";
		var stream = version.Stream == OsuVersionStream.Stable
			? string.Empty
			: version.Stream.ToString().ToLowerInvariant();

		return $"b{date}{revision}{stream}";
	}

	[GeneratedRegex(
		@"^(?:b)?(?<date>[0-9]{8})(?:\.(?<revision>[0-9]))?(?<stream>beta|cuttingedge|dev|tourney)?\z")]
	private static partial Regex BuildPattern();
}