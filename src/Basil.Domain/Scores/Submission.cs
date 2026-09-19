using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Client;

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
	public required Score Score { get; init; }

	/// <summary>Gets or sets the anticheat flags the client reported with the submission.</summary>
	public ClientFlags ClientFlags { get; init; } = ClientFlags.Clean;

	/// <summary>Gets or sets the submission checksum that the client sent.</summary>
	public string Md5ByClient { get; init; } = string.Empty;


	/// <summary>
	///     Parses the decrypted score submission string into a <see cref="Submission" />.
	/// </summary>
	/// <param name="submitFields">
	///     The colon-delimited submission fields, with the leading beatmap MD5 and username entries
	///     already stripped by the caller, since they are not score fields. The first remaining
	///     entry is the submission MD5 and the final entry carries the client flags encoded as
	///     spaces.
	/// </param>
	/// <param name="beatmapMd5">The MD5 hash of the beatmap played, carried into the score.</param>
	/// <param name="userId">The user ID of the player, carried into the score.</param>
	/// <returns>A submission populated with the parsed values.</returns>
	public static Submission Parse(IReadOnlyList<string> submitFields, string? beatmapMd5 = null, int? userId = null)
	{
		return new Submission
		{
			Score = Score.Parse(submitFields, beatmapMd5, userId),
			Md5ByClient = submitFields[0].ToLowerInvariant(),
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
	/// <param name="server">
	///     The data known by the server: the stored client fingerprint (if any), the client
	///     version, the beatmap MD5 and storyboard MD5, and the player's name.
	/// </param>
	/// <param name="client">
	///     The data the client sent with the submission: the fingerprint MD5 and serial, the
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
		(ClientFingerprint? Fingerprint, ClientVersion Version,
			(string Md5, string? StoryboardMd5) Beatmap, string playerName) server,
		((string Md5, string Serial) Fingerprint, string VersionDate, string beatmapMd5) client,
		[MaybeNullWhen(true)] out string error)
	{
		error = null;
		var (clientUninstallMd5, clientDiskSignatureMd5) = ComputeSerialMd5();
		var md5ByServer = ComputeSubmissionMd5();

		if (server.Fingerprint is not { } serverFingerprint) error = "Client fingerprint is missing.";
		else if (client.VersionDate != server.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			error = "Client version is mismatched.";
		else if (client.Fingerprint.Md5 != serverFingerprint.ToString()) error = "Client hash is mismatched.";
		else if (clientUninstallMd5 != serverFingerprint.UninstallMd5) error = "Uninstaller hash is mismatched.";
		else if (clientDiskSignatureMd5 != serverFingerprint.DiskSignatureMd5)
			error = "Disk signature hash is mismatched.";
		else if (Md5ByClient != md5ByServer) error = "Submission hash is mismatched.";
		else if (client.beatmapMd5 != server.Beatmap.Md5) error = "Beatmap hash is mismatched.";

		return error is null;

		#region DON'T CHANGE THESE FORMULA!!!

		string ComputeSubmissionMd5()
		{
			var hitCounts = Score.HitCounts;

			var raw =
				$"chickenmcnuggets{hitCounts.num100 + hitCounts.num300}o15{hitCounts.num50}{hitCounts.numGeki}" +
				$"smustard{hitCounts.numKatu}{hitCounts.numMiss}uu{Score.BeatmapMd5}{Score.MaxCombo}" +
				$"{Score.IsFullCombo}{server.playerName}{Score}{Score.Grade}{(int)Score.Mods}Q{Score.IsPassed}{(int)Score.Mode}" +
				$"{client.VersionDate}{Score.OccuredAt:yyMMddHHmmss}{client.Fingerprint.Md5}{server.Beatmap.StoryboardMd5 ?? string.Empty}";
			var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
			return Convert.ToHexStringLower(hash);
		}

		(string UninstallMd5, string DiskSignatureMd5) ComputeSerialMd5()
		{
			const char delimiter = '|';

			var parts = client.Fingerprint.Serial.Split(delimiter, 2);
			return (Md5Hex(parts[0]), Md5Hex(parts[1]));

			static string Md5Hex(string v)
			{
				return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(v)));
			}
		}

		#endregion
	}
}