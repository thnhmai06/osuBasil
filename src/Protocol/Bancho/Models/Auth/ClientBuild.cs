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
		var match = BuildPattern().Match(text);
		if (!match.Success)
			throw new FormatException($"Invalid client build: {text}");

		var date = DateOnly.ParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture);

		var revisionGroup = match.Groups["revision"];
		int? revision = revisionGroup.Success
			? int.Parse(revisionGroup.Value, CultureInfo.InvariantCulture)
			: null;

		var streamGroup = match.Groups["stream"];
		var stream = streamGroup.Success
			? Enum.Parse<ClientStream>(streamGroup.Value, true)
			: ClientStream.Stable;

		return new ClientBuild(date, revision, stream);
	}

	/// <summary>Reads a client build from its wire text without throwing.</summary>
	/// <param name="text">The wire text, or <see langword="null" />.</param>
	/// <param name="build">The client build when the text is well formed; otherwise <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="text" /> was read; otherwise, <see langword="false" />.</returns>
	public static bool TryParse(string? text, [NotNullWhen(true)] out ClientBuild? build)
	{
		build = null;
		if (text is null) return false;

		try
		{
			build = Parse(text);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
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
		@"^(?:b)?(?<date>(?:\d{8}|\d{4}\.\d{3}))(?:\.(?<revision>\d))?(?<stream>beta|cuttingedge|dev|tourney)?$")]
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
