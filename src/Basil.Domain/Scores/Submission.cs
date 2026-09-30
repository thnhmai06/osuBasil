using System.Diagnostics.CodeAnalysis;
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

	/// <summary>Gets or sets the anticheat flags the client reported with the submission.</summary>
	public ClientFlags ClientFlags
	{
		get;
		init
		{
			value.ThrowIfUndefined();
			field = value;
		}
	} = ClientFlags.Clean;

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
		return new Submission
		{
			Score = ScoreData.Parse(submitFields, beatmapHash, userId),
			HashByClient = submitFields[0],
			ClientFlags = (ClientFlags)(submitFields[15].Count(c => c == ' ') & ~4)
		};
	}

	/// <summary>
	///     Verifies that the submission is authentic, recomputing the legacy checksum formulas that
	///     mirror the osu! server and comparing them with the client-sent values.
	/// </summary>
	/// <remarks>
	///     Checks, in order: that the server knows a fingerprint for the player, that the client's
	///     version date matches, that the client's fingerprint hash and its serial-derived hashes
	///     match the server's record, that the submission MD5 matches the recomputed value, and
	///     that the beatmap MD5 the client claims matches the beatmap the server hands out. On the
	///     first mismatch the reason is reported in <paramref name="error" /> and validation stops.
	/// </remarks>
	/// <param name="fromServer">
	///     The data known by the server: the stored client fingerprint (if any), the client
	///     version, the beatmap MD5 and storyboard MD5, and the player's name.
	/// </param>
	/// <param name="fromClient">
	///     The data the client sent with the submission: the client hash and the unique ids, the
	///     version date, and the beatmap MD5 it claims to have played.
	/// </param>
	/// <param name="error">
	///     When this method returns <see langword="false" />, contains a message describing why the
	///     submission was rejected.
	/// </param>
	/// <returns>
	///     <see langword="true" /> if every check passes; otherwise, <see langword="false" />.
	/// </returns>
	public bool Validate(
		(ClientFingerprint? Fingerprint, ClientVersion Version, (Md5 Hash, Md5? StoryboardHash) Beatmap, string
			playerName) fromServer,
		((string Hash, string Serial) Fingerprint, string VersionDate, Md5 beatmapMd5) fromClient,
		[MaybeNullWhen(true)] out string error)
	{
		error = null;
		var serialHash = ComputeSerialHash();
		var md5ByServer = ComputeSubmissionMd5();

		if (fromServer.Fingerprint is not { } serverFingerprint) error = "Client fingerprint is missing.";
		else if (fromClient.VersionDate != fromServer.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			error = "Client version is mismatched.";
		else if (fromClient.Fingerprint.Hash != serverFingerprint.ToString()) error = "Client hash is mismatched.";
		else if (serialHash?.UninstallHash != serverFingerprint.UninstallHash) error = "Uninstaller hash is mismatched.";
		else if (serialHash?.DiskSignatureHash != serverFingerprint.DiskSignatureHash)
			error = "Disk signature hash is mismatched.";
		else if (HashByClient != md5ByServer) error = "Submission hash is mismatched.";
		else if (fromClient.beatmapMd5 != fromServer.Beatmap.Hash) error = "Beatmap hash is mismatched.";

		return error is null;

		#region DON'T CHANGE THESE FORMULA!!!

		string ComputeSubmissionMd5()
		{
			var hitCounts = Score.HitCounts;

			var raw =
				$"chickenmcnuggets{hitCounts.Num100 + hitCounts.Num300}o15{hitCounts.Num50}{hitCounts.NumGeki}" +
				$"smustard{hitCounts.NumKatu}{hitCounts.NumMiss}uu{fromServer.Beatmap.Hash}{Score.MaxCombo}" +
				$"{Score.IsFullCombo}{fromServer.playerName}{Score.TotalScore}{Score.Grade.ToString().ToUpperInvariant()}" +
				$"{(int)Score.Mods}Q{Score.IsPassed}{(int)Score.Mode}" +
				$"{fromClient.VersionDate}{Score.Timestamp:yyMMddHHmmss}{fromClient.Fingerprint.Hash}{fromServer.Beatmap.StoryboardHash ?? string.Empty}";
			var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
			return Convert.ToHexStringLower(hash);
		}

		(Md5 UninstallHash, Md5 DiskSignatureHash)? ComputeSerialHash()
		{
			const char delimiter = '|';

			var parts = fromClient.Fingerprint.Serial.Split(delimiter, 2);
			if (parts.Length < 2) return null;
			return (new Md5(Encoding.UTF8.GetBytes(parts[0])), new Md5(Encoding.UTF8.GetBytes(parts[1])));
		}

		#endregion
	}
}