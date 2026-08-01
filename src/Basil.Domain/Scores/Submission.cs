using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;

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
	/// <summary>
	///     Parses the decrypted score submission string into a <see cref="Submission" />.
	/// </summary>
	/// <param name="fields">
	///     The colon-delimited submission fields, with the leading beatmap MD5 and username entries
	///     already stripped by the caller, since they are not score fields.
	/// </param>
	/// <returns>A submission populated with the parsed values.</returns>
	/// <remarks>
	///     <see cref="BeatmapMd5" /> and <see cref="UserId" /> are set to placeholders here, because
	///     the caller does not know the beatmap or player until after the parse completes. They are
	///     meant to be overwritten immediately after this call.
	/// </remarks>
	public static Submission FromSubmission(IReadOnlyList<string> fields)
	{
		var mods = (Mods)int.Parse(fields[11], CultureInfo.InvariantCulture);

		return new Submission
		{
			BeatmapMd5 = string.Empty,
			UserId = 0,
			ClientChecksum = fields[0],
			HitCounts = new HitCounts(
				int.Parse(fields[1], CultureInfo.InvariantCulture),
				int.Parse(fields[2], CultureInfo.InvariantCulture),
				int.Parse(fields[3], CultureInfo.InvariantCulture),
				int.Parse(fields[4], CultureInfo.InvariantCulture),
				int.Parse(fields[5], CultureInfo.InvariantCulture),
				int.Parse(fields[6], CultureInfo.InvariantCulture)),
			Score = long.Parse(fields[7], CultureInfo.InvariantCulture),
			MaxCombo = int.Parse(fields[8], CultureInfo.InvariantCulture),
			IsFullCombo = fields[9] == "True",
			Grade = Enum.Parse<Grade>(fields[10], true),
			Mods = mods,
			IsPassed = fields[12] == "True",
			Mode = (GameMode)int.Parse(fields[13], CultureInfo.InvariantCulture),
			ClientTime = DateTime.ParseExact(fields[14], "yyMMddHHmmss", CultureInfo.InvariantCulture),
			ClientFlags = (ClientFlags)(fields[15].Count(c => c == ' ') & ~4)
		};
	}

	/// <summary>
	///     Computes the online checksum for the score submission.
	/// </summary>
	/// <param name="playerName">The player's username.</param>
	/// <param name="osuVersion">The osu! version string of the client.</param>
	/// <param name="osuClientHash">The client hash of the submitting client.</param>
	/// <param name="storyboardChecksum">The MD5 of the beatmap's storyboard file.</param>
	/// <returns>The MD5 hex string of the checksum.</returns>
	/// <remarks>
	///     The exact format string and field order must be preserved byte-for-byte, because the
	///     client verifies this value against its own computation. Note that the format-argument
	///     order does not match the field order in the template: storyboardChecksum appears before
	///     osuVersion.
	/// </remarks>
	public string ComputeOnlineChecksum(string playerName, string osuVersion, string osuClientHash,
		string storyboardChecksum)
	{
		var raw =
			$"chickenmcnuggets{HitCounts.x100 + HitCounts.x300}o15{HitCounts.x50}{HitCounts.xGeki}" +
			$"smustard{HitCounts.xKatu}{HitCounts.xMiss}uu{BeatmapMd5}{MaxCombo}" +
			$"{IsFullCombo}{playerName}{Score}{Grade}{(int)Mods}Q{IsPassed}{(int)Mode}" +
			$"{osuVersion}{ClientTime:yyMMddHHmmss}{osuClientHash}{storyboardChecksum}";

		var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
		return Convert.ToHexStringLower(hash);
	}

	/// <summary>
	///     Verifies that the client-supplied checksum matches the server-computed one.
	/// </summary>
	/// <param name="playerName">The player's username.</param>
	/// <param name="osuVersion">The osu! version string of the client.</param>
	/// <param name="clientHash">The client hash of the submitting client.</param>
	/// <param name="storyboardMd5">
	///     The MD5 of the beatmap's storyboard file, or <see langword="null" /> if the beatmap has
	///     none.
	/// </param>
	/// <exception cref="ScoreSubmissionIntegrityException">The checksums do not match.</exception>
	public void ValidateScoreChecksum(string playerName, string osuVersion, string clientHash, string? storyboardMd5)
	{
		var serverChecksum = ComputeOnlineChecksum(playerName, osuVersion, clientHash, storyboardMd5 ?? "");
		if (ClientChecksum != serverChecksum)
			throw new ScoreSubmissionIntegrityException(
				$"online score checksum mismatch ({serverChecksum} != {ClientChecksum})");
	}

	/// <summary>
	///     Runs the full set of submission integrity checks.
	/// </summary>
	/// <param name="clientDetails">The client details captured at login for the submitting session.</param>
	/// <param name="loginOsuVersionDate">The build date of the osu! version captured at login.</param>
	/// <param name="playerName">The player's username.</param>
	/// <param name="osuVersion">The osu! version string of the client.</param>
	/// <param name="clientHash">The client hash of the submitting client.</param>
	/// <param name="uniqueIds">The pipe-delimited unique-id string sent by the client.</param>
	/// <param name="storyboardMd5">
	///     The MD5 of the beatmap's storyboard file, or <see langword="null" /> if the beatmap has
	///     none.
	/// </param>
	/// <param name="submissionBeatmapMd5">The beatmap MD5 the client submitted.</param>
	/// <param name="updatedBeatmapHash">The current beatmap hash on the server.</param>
	/// <remarks>
	///     Checks the client details, the online checksum, and the beatmap hash in order. Callers
	///     decide whether to treat a failure as fatal; this method only reports the mismatch.
	/// </remarks>
	/// <exception cref="ScoreSubmissionIntegrityException">Any of the checks fail.</exception>
	public void ValidateSubmissionIntegrity(
		ClientDetails? clientDetails,
		DateOnly loginOsuVersionDate,
		string playerName,
		string osuVersion,
		string clientHash,
		string uniqueIds,
		string? storyboardMd5,
		string submissionBeatmapMd5,
		string updatedBeatmapHash)
	{
		var uniqueIdHashes = ParseUniqueIdHashes(uniqueIds);
		ValidateClientDetails(clientDetails, loginOsuVersionDate, osuVersion, clientHash, uniqueIdHashes);
		ValidateScoreChecksum(playerName, osuVersion, clientHash, storyboardMd5);
		ValidateBeatmapHash(submissionBeatmapMd5, updatedBeatmapHash);
	}

	/// <summary>
	///     Splits the client's unique-id string into two hashed values.
	/// </summary>
	/// <param name="uniqueIds">The pipe-delimited unique-id string sent by the client.</param>
	/// <returns>The MD5 hashes of the two unique-id values.</returns>
	public static UniqueIdHashes ParseUniqueIdHashes(string uniqueIds)
	{
		var parts = uniqueIds.Split('|', 2);
		return new UniqueIdHashes(Md5Hex(parts[0]), Md5Hex(parts[1]));
	}

	/// <summary>
	///     Verifies that the client details captured at login match the current submission.
	/// </summary>
	/// <param name="clientDetails">The client details captured at login.</param>
	/// <param name="loginOsuVersionDate">The build date of the osu! version captured at login.</param>
	/// <param name="osuVersion">The osu! version string of the client.</param>
	/// <param name="clientHash">The client hash of the submitting client.</param>
	/// <param name="uniqueIdHashes">The hashes parsed from the client's unique-id string.</param>
	/// <exception cref="ScoreSubmissionIntegrityException">Any of the details do not match.</exception>
	public static void ValidateClientDetails(
		ClientDetails? clientDetails, DateOnly loginOsuVersionDate, string osuVersion, string clientHash,
		UniqueIdHashes uniqueIdHashes)
	{
		if (clientDetails is null) throw new ScoreSubmissionIntegrityException("missing client details");

		if (osuVersion != loginOsuVersionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			throw new ScoreSubmissionIntegrityException("osu! version mismatch");

		if (clientHash != clientDetails.Hash()) throw new ScoreSubmissionIntegrityException("client hash mismatch");

		if (uniqueIdHashes.UniqueId1Md5 != clientDetails.UninstallMd5)
			throw new ScoreSubmissionIntegrityException(
				$"unique_id1 mismatch ({uniqueIdHashes.UniqueId1Md5} != {clientDetails.UninstallMd5})");

		if (uniqueIdHashes.UniqueId2Md5 != clientDetails.DiskSignatureMd5)
			throw new ScoreSubmissionIntegrityException(
				$"unique_id2 mismatch ({uniqueIdHashes.UniqueId2Md5} != {clientDetails.DiskSignatureMd5})");
	}

	/// <summary>
	///     Verifies that the beatmap MD5 submitted by the client matches the current beatmap hash.
	/// </summary>
	/// <param name="submissionBeatmapMd5">The beatmap MD5 the client submitted.</param>
	/// <param name="updatedBeatmapHash">The current beatmap hash on the server.</param>
	/// <exception cref="ScoreSubmissionIntegrityException">The hashes do not match.</exception>
	public static void ValidateBeatmapHash(string submissionBeatmapMd5, string updatedBeatmapHash)
	{
		if (submissionBeatmapMd5 != updatedBeatmapHash)
			throw new ScoreSubmissionIntegrityException(
				$"beatmap hash mismatch ({submissionBeatmapMd5} != {updatedBeatmapHash})");
	}

	private static string Md5Hex(string value)
	{
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
	}

	#region Identity

	/// <summary>Gets or sets the MD5 hash of the beatmap the score was played on.</summary>
	public required string BeatmapMd5 { get; init; }
	/// <summary>Gets or sets the id of the player who submitted the score.</summary>
	public required int UserId { get; init; }

	#endregion

	#region Mechanic

	/// <summary>Gets or sets the game mode the score was played in.</summary>
	public required GameMode Mode { get; init; }
	/// <summary>Gets or sets the mods applied to the play.</summary>
	public required Mods Mods { get; init; }

	#endregion

	#region Stats

	/// <summary>Gets or sets the hit-judgment counts of the play.</summary>
	public required HitCounts HitCounts { get; init; }
	/// <summary>Gets or sets the total score of the play.</summary>
	public required long Score { get; init; }
	/// <summary>Gets or sets the maximum combo achieved in the play.</summary>
	public required int MaxCombo { get; init; }
	/// <summary>Gets or sets the letter grade of the play.</summary>
	public required Grade Grade { get; init; }

	/// <summary>
	///     Gets the accuracy percentage of the play.
	/// </summary>
	/// <value>A percentage from 0 to 100, computed from the hit counts.</value>
	public double Accuracy => HitCounts.CalculateAccuracy(Mode, Mods);

	/// <summary>Gets or sets a value that indicates whether the play was passed.</summary>
	public required bool IsPassed { get; init; }
	/// <summary>Gets or sets a value that indicates whether the play was a full combo. Also known as IsPerfect.</summary>
	public required bool IsFullCombo { get; init; } // IsPerfect

	#endregion

	#region Synchronization

	/// <summary>Gets or sets the time the client recorded for the play, in UTC.</summary>
	public required DateTime ClientTime { get; init; }
	/// <summary>Gets the time the server received the submission, in UTC.</summary>
	public DateTime ServerTime { get; init; } = DateTime.UtcNow;
	/// <summary>Gets or sets the duration of the play.</summary>
	public TimeSpan TimeElapsed { get; init; }

	#endregion

	#region Integrity

	/// <summary>Gets or sets the anticheat flags the client reported with the submission.</summary>
	public ClientFlags ClientFlags { get; init; } = ClientFlags.Clean;
	/// <summary>Gets or sets the checksum the client supplied with the submission.</summary>
	public string ClientChecksum { get; init; } = string.Empty;

	#endregion
}

/// <summary>
///     Represents a failure to validate the integrity of a score submission.
/// </summary>
/// <remarks>
///     Thrown when a submission fails one of the integrity checks. It is a distinct type so that
///     callers can catch integrity failures narrowly.
/// </remarks>
public sealed class ScoreSubmissionIntegrityException(string message) : Exception(message);

/// <summary>
///     Holds the two MD5 hashes parsed from a client's unique-id string.
/// </summary>
/// <param name="UniqueId1Md5">The MD5 hash of the first unique-id value.</param>
/// <param name="UniqueId2Md5">The MD5 hash of the second unique-id value.</param>
public sealed record UniqueIdHashes(string UniqueId1Md5, string UniqueId2Md5);
