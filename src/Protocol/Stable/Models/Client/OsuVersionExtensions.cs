using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Basil.Domain.Client;

namespace Basil.Protocol.Stable.Models.Client;

public static partial class OsuVersionExtensions
{
	public static OsuVersion Parse(string text)
	{
		return TryParse(text, out var version) ? version : throw new FormatException($"Invalid client build: {text}");
	}

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