using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Basil.Protocol.Bancho.Models.Auth;

/// <summary>
///     The build string an osu! client reports, <c>bYYYYMMDD[.revision][stream]</c>: a <c>b</c> prefix, a build
///     date, an optional single-digit revision and an optional stream suffix.
/// </summary>
/// <param name="Date">The build date.</param>
/// <param name="Revision">The revision, or <see langword="null" /> when there is none.</param>
/// <param name="Stream">The release stream.</param>
public sealed partial record ClientBuild(DateOnly Date, int? Revision, ClientStream Stream)
{
	/// <summary>Reads a client build from its wire text.</summary>
	/// <param name="text">The wire text; the leading <c>b</c> is optional.</param>
	/// <returns>The client build.</returns>
	/// <exception cref="FormatException"><paramref name="text" /> is not a client build string.</exception>
	public static ClientBuild Parse(string text)
	{
		return TryParse(text, out var build) ? build : throw new FormatException($"Invalid client build: {text}");
	}

	/// <summary>Reads a client build from its wire text without throwing.</summary>
	/// <param name="text">The wire text, or <see langword="null" />.</param>
	/// <param name="build">The client build when the text is well formed; otherwise <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="text" /> was read; otherwise, <see langword="false" />.</returns>
	public static bool TryParse(string? text, [NotNullWhen(true)] out ClientBuild? build)
	{
		build = null;
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
		var stream = ClientStream.Stable;
		if (streamGroup.Success && !Enum.TryParse(streamGroup.Value, true, out stream))
			return false;

		build = new ClientBuild(date, revision, stream);
		return true;
	}

	/// <summary>
	///     Returns the wire text of this build: <c>b</c>, the date as <c>yyyyMMdd</c>, <c>.R</c> for a revision and
	///     the lowercase stream name unless the stream is <see cref="ClientStream.Stable" />.
	/// </summary>
	/// <returns>The wire text, for example <c>b20240815.2beta</c> or <c>b20240815</c>.</returns>
	public override string ToString()
	{
		var date = Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var revision = Revision is null ? string.Empty : $".{Revision}";
		var stream = Stream == ClientStream.Stable
			? string.Empty
			: Stream.ToString().ToLowerInvariant();

		return $"b{date}{revision}{stream}";
	}

	[GeneratedRegex(
		@"^(?:b)?(?<date>[0-9]{8})(?:\.(?<revision>[0-9]))?(?<stream>beta|cuttingedge|dev|tourney)?\z")]
	private static partial Regex BuildPattern();
}

/// <summary>The release stream of an osu! client build.</summary>
public enum ClientStream : byte
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