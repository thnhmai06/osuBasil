using Basil.Domain.Client;
using Basil.Domain.Utilities;

namespace Basil.Domain.Scores;

/// <summary>
///     Represents a score submitted by an osu! client.
/// </summary>
/// <remarks>
///     Built by parsing the decrypted colon-delimited submission string sent by the client, then
///     completing the identity fields once the caller knows the beatmap and player.
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


	/// <summary>
	///     Parses the decrypted score submission string into a <see cref="Submission" />.
	/// </summary>
	/// <param name="submitFields">
	///     The colon-delimited submission fields, with the leading beatmap MD5 and username entries
	///     already stripped by the caller, since they are not score fields. The first remaining
	///     entry is the submission MD5 and the final entry carries the client flags encoded as
	///     spaces.
	/// </param>
	/// <param name="beatmapHash">The MD5 hash of the beatmap played, carried into the score.</param>
	/// <param name="userId">The user ID of the player, carried into the score.</param>
	/// <returns>A submission populated with the parsed values.</returns>
	public static Submission Parse(IReadOnlyList<string> submitFields, Md5? beatmapHash = null, int? userId = null)
	{
		Md5 checksum = submitFields[0];
		return new Submission
		{
			Score = ScoreData.Parse(submitFields, beatmapHash, userId) with { Checksum = checksum },
			HashByClient = checksum,
			ClientFlags = (ClientFlags)(submitFields[15].Count(c => c == ' ') & ~4)
		};
	}
}