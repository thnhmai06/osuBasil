using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Basil.Domain.Client;

public readonly partial record struct ClientVersion(DateOnly Date, int? Revision, ClientVersionStream Stream)
	: IParsable<ClientVersion>, IFormattable
{
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