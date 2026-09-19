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
	///     already stripped by the caller, since they are not score fields.
	/// </param>
	/// <param name="beatmapMd5">The md5 hash of the beatmap.</param>
	/// <param name="userId">The user id of the player.</param>
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