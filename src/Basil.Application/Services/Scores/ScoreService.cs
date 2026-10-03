using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Common;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Scores;
using Basil.Application.Multiplayer;
using Basil.Application.Scores;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Services.Scores;

/// <summary>
///     Validates and stores submitted scores, their replays, and the submitter's updated statistics.
/// </summary>
internal sealed class ScoreService(
	IScoreRepository scores,
	IReplayStorage replays,
	IUserStatsRepository stats,
	ILoginRepository logins,
	Lobby lobby,
	IRoomService rooms) : IScoreService
{
	private readonly Channel<ScoreEvent> _events = Channel.CreateUnbounded<ScoreEvent>();

	/// <inheritdoc />
	public ChannelReader<ScoreEvent> Events => _events.Reader;

	/// <summary>The shortest replay, in bytes, that is kept.</summary>
	internal const int MinReplayLength = 24;

	/// <inheritdoc />
	public async Task<ScoreRejection?> SubmitAsync(
		BanchoConnection connection,
		Submission submission,
		(Md5 Hash, Md5? StoryboardHash)? beatmap,
		(string Hash, string Serial) clientFingerprint,
		string clientVersionDate,
		Md5 clientBeatmapHash,
		byte[]? replay,
		CancellationToken cancellationToken = default)
	{
		var room = lobby.RoomOf(connection);
		var round = room?.LastRound is { } last && last.BeatmapHash == clientBeatmapHash ? last : null;
		var checkedBeatmap = beatmap ?? (round is not null ? (clientBeatmapHash, (Md5?)null) : null);
		if (checkedBeatmap is not { } known) return ScoreRejection.UnknownBeatmap;

		if (submission.ClientFlags != ClientFlags.Clean)
			await rooms.ReportClientFlagsAsync(connection, submission.ClientFlags, cancellationToken);

		var latest = await logins.ListAsync(new LoginQuery(connection.User), new PageRequest(0, 1), cancellationToken);
		var client = latest.Items.FirstOrDefault()?.Client ?? connection.Login.Client!;
		if (Validate(submission, client, known, connection.User.Value.Name, clientFingerprint, clientVersionDate,
			    clientBeatmapHash) is { } rejection)
			return rejection;

		var team = round is not null ? room!.Slots.Find(connection)?.Team : null;
		var score = await scores.CreateAsync(
			submission.Score with { UserId = connection.User.Id, Round = round, Team = team }, cancellationToken);
		if (score is null) return ScoreRejection.Duplicate;

		if (submission.Score.IsPassed && replay is { Length: >= MinReplayLength })
		{
			await using var content = new MemoryStream(replay, false);
			await replays.SaveAsync(score, content, cancellationToken);
		}

		// ponytail: get-then-write per user; add an atomic increment to the contract if one user's submissions can overlap.
		var current = await stats.GetAsync(connection.User, submission.Score.Mode, cancellationToken);
		current.PlayCount++;
		if (submission.Score.IsPassed)
		{
			// Every beatmap reports as Approved (see Beatmapset.Status), so every passed score counts
			// toward ranked score too.
			current.TotalScore += submission.Score.TotalScore;
			current.RankedScore += submission.Score.TotalScore;
		}

		await stats.CreateOrUpdateAsync(current, cancellationToken);

		if (room is not null && round is not null)
			await rooms.RecordScoreAsync(room, connection.User, score, cancellationToken);

		_events.Writer.TryWrite(new ScoreSubmitted(connection.User, score, current));
		return null;
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
	/// <param name="submission">The parsed submission.</param>
	/// <param name="client">The osu! client the player logged in with.</param>
	/// <param name="beatmap">The MD5 and storyboard MD5 of the beatmap the score is checked against.</param>
	/// <param name="playerName">The player's name as the server knows it.</param>
	/// <param name="clientFingerprint">The client hash and unique ids sent with the submission.</param>
	/// <param name="clientVersionDate">The client version date sent with the submission.</param>
	/// <param name="clientBeatmapHash">The beatmap MD5 the client claims to have played.</param>
	/// <returns><see langword="null" /> if the submission is authentic; otherwise, the first reason it is rejected.</returns>
	private static ScoreRejection? Validate(
		Submission submission,
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
		if (submission.HashByClient != md5ByServer) return ScoreRejection.SubmissionHashMismatch;
		if (clientBeatmapHash != beatmap.Hash) return ScoreRejection.BeatmapHashMismatch;

		return null;

		#region DON'T CHANGE THESE FORMULA!!!

		string ComputeSubmissionMd5()
		{
			var hitCounts = submission.Score.HitCounts;

			var raw =
				$"chickenmcnuggets{hitCounts.Num100 + hitCounts.Num300}o15{hitCounts.Num50}{hitCounts.NumGeki}" +
				$"smustard{hitCounts.NumKatu}{hitCounts.NumMiss}uu{beatmap.Hash}{submission.Score.MaxCombo}" +
				$"{submission.Score.IsFullCombo}{playerName}{submission.Score.TotalScore}{submission.Score.Grade.ToString().ToUpperInvariant()}" +
				$"{(int)submission.Score.Mods}Q{submission.Score.IsPassed}{(int)submission.Score.Mode}" +
				$"{clientVersionDate}{submission.Score.Timestamp:yyMMddHHmmss}{clientFingerprint.Hash}{beatmap.StoryboardHash?.ToString() ?? string.Empty}";
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