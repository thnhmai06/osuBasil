using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

	/// <summary>
	///     Verifies that the submission is authentic, recomputing the legacy checksum formulas that
	///     mirror the osu! server and comparing them with the client-sent values.
	/// </summary>
	/// <remarks>
	///     Checks, in order: that the client's version date matches the version the client logged in
	///     with, that the client's fingerprint hash and its serial-derived hashes match the ones
	///     reported at login, that the submission MD5 matches the recomputed value, and that the
	///     beatmap MD5 the client claims matches the beatmap the score is checked against. On the
	///     first mismatch the reason is returned and validation stops.
	/// </remarks>
	/// <param name="client">The osu! client the player logged in with.</param>
	/// <param name="beatmap">The MD5 and storyboard MD5 of the beatmap the score is checked against.</param>
	/// <param name="playerName">The player's name as the server knows it.</param>
	/// <param name="clientFingerprint">The client hash and unique ids sent with the submission.</param>
	/// <param name="clientVersionDate">The client version date sent with the submission.</param>
	/// <param name="clientBeatmapHash">The beatmap MD5 the client claims to have played.</param>
	/// <returns><see langword="null" /> if the submission is authentic; otherwise, the first reason it is rejected.</returns>
	public ScoreRejection? Validate(
		ClientInfo client,
		(Md5 Hash, Md5? StoryboardHash) beatmap,
		string playerName,
		(string Hash, string Serial) clientFingerprint,
		string clientVersionDate,
		Md5 clientBeatmapHash)
	{
		var serialHash = ComputeSerialHash();
		var md5ByServer = ComputeSubmissionMd5();

		if (clientVersionDate != client.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			return ScoreRejection.VersionMismatch;
		if (clientFingerprint.Hash != client.Fingerprint.ToString()) return ScoreRejection.ClientHashMismatch;
		if (serialHash?.UninstallHash != client.Fingerprint.UninstallHash)
			return ScoreRejection.UninstallerHashMismatch;
		if (serialHash?.DiskSignatureHash != client.Fingerprint.DiskSignatureHash)
			return ScoreRejection.DiskSignatureHashMismatch;
		if (HashByClient != md5ByServer) return ScoreRejection.SubmissionHashMismatch;
		if (clientBeatmapHash != beatmap.Hash) return ScoreRejection.BeatmapHashMismatch;

		return null;

		#region DON'T CHANGE THESE FORMULA!!!

		string ComputeSubmissionMd5()
		{
			var hitCounts = Score.HitCounts;

			var raw =
				$"chickenmcnuggets{hitCounts.Num100 + hitCounts.Num300}o15{hitCounts.Num50}{hitCounts.NumGeki}" +
				$"smustard{hitCounts.NumKatu}{hitCounts.NumMiss}uu{beatmap.Hash}{Score.MaxCombo}" +
				$"{Score.IsFullCombo}{playerName}{Score.TotalScore}{Score.Grade.ToString().ToUpperInvariant()}" +
				$"{(int)Score.Mods}Q{Score.IsPassed}{(int)Score.Mode}" +
				$"{clientVersionDate}{Score.Timestamp:yyMMddHHmmss}{clientFingerprint.Hash}{beatmap.StoryboardHash ?? string.Empty}";
			var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
			return Convert.ToHexStringLower(hash);
		}

		(Md5 UninstallHash, Md5 DiskSignatureHash)? ComputeSerialHash()
		{
			const char delimiter = '|';

			var parts = clientFingerprint.Serial.Split(delimiter, 2);
			if (parts.Length < 2) return null;
			return (new Md5(Encoding.UTF8.GetBytes(parts[0])), new Md5(Encoding.UTF8.GetBytes(parts[1])));
		}

		#endregion
	}
}