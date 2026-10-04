using Basil.Domain.Client;
using Basil.Domain.Utilities;

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
	/// <remarks>Kept as reported, including bits that no <see cref="Client.ClientFlags" /> member names.</remarks>
	public ClientFlags ClientFlags { get; init; } = ClientFlags.Clean;

	/// <summary>Gets or sets the submission checksum that the client sent.</summary>
	public required Md5 HashByClient { get; init; }

}
