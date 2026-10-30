using Basil.Domain.Client;
using Basil.Domain.Common;

namespace Basil.Domain.Scores;

/// <summary>
///     Represents a score submitted by an osu! client.
/// </summary>
/// <remarks>
///     The client's own claim about a play: the score, the checksum it computed and the anticheat flags it reported.
/// </remarks>
public sealed record Submission
{
	/// <summary>Gets the parsed score, including its hit counts, total score, and mode.</summary>
	public required ScoreData Score { get; init; }

	/// <summary>Gets the anticheat flags the client reported with the submission.</summary>
	/// <remarks>Kept as reported, including bits that no <see cref="Flags" /> member names.</remarks>
	public required OsuFlags Flags { get; init; }

	/// <summary>Gets the submission checksum that the client sent.</summary>
	public required Md5 HashByClient { get; init; }
}