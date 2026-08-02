using System.Text.RegularExpressions;

namespace Basil.Domain.Login;

/// <summary>
///     Represents the release stream of an osu! client.
/// </summary>
public enum OsuStream : byte
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

/// <summary>
///     Represents the version of an osu! client.
/// </summary>
/// <param name="Date">The build date of the client.</param>
/// <param name="Revision">
///     The build revision, or <see langword="null" /> if the version string carried none.
/// </param>
/// <param name="Stream">The release stream of the client.</param>
public sealed partial record OsuVersion(DateOnly Date, int? Revision, OsuStream Stream)
{
	[GeneratedRegex(
		@"^(?:b)?(?<date>(?:\d{8}|\d{4}\.\d{3}))(?:\.(?<revision>\d))?(?<stream>beta|cuttingedge|dev|tourney)?$")]
	private static partial Regex VersionPattern();

	/// <summary>
	///     Parses an osu! version string into an <see cref="OsuVersion" />.
	/// </summary>
	/// <param name="osuVersionString">
	///     The version string reported by the client, for example, "b20240801.1beta".
	/// </param>
	/// <returns>The parsed version.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="osuVersionString" /> does not match the expected version format.
	/// </exception>
	public static OsuVersion From(string osuVersionString)
	{
		var match = VersionPattern().Match(osuVersionString);
		if (!match.Success) throw new ArgumentException($"Invalid client version: {osuVersionString}");

		var dateText = match.Groups["date"].Value;
		var date = DateOnly.ParseExact(dateText, "yyyyMMdd");

		var revisionGroup = match.Groups["revision"];
		int? revision = revisionGroup.Success ? int.Parse(revisionGroup.Value) : null;

		var streamGroup = match.Groups["stream"];
		var stream = streamGroup.Success ? Enum.Parse<OsuStream>(streamGroup.Value, true) : OsuStream.Stable;

		return new OsuVersion(date, revision, stream);
	}
}