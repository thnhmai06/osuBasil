using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Services.Contracts.Scores;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Match;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Client;
using Basil.Domain.Multiplayer.Round;
using Basil.Domain.Scores;
using Basil.Domain.Utilities;

namespace Basil.Application.Services.Implementations.Scores;

/// <summary>
///     Validates and stores submitted scores, their replays, and the submitter's updated statistics.
/// </summary>
internal sealed class ScoreService(
	IScoreRepository scores,
	IRoundScoreRepository roundScores,
	IReplayStorage replays,
	IUserStatsRepository stats,
	ILobby lobby) : IScoreService
{
	/// <summary>The shortest replay, in bytes, that is kept.</summary>
	internal const int MinReplayLength = 24;

	private readonly Channel<ScoreEvent> _events = Channel.CreateUnbounded<ScoreEvent>();

	/// <inheritdoc />
	public ChannelReader<ScoreEvent> Events => _events.Reader;

	/// <inheritdoc />
	public async Task<ScoreRejection?> SubmitAsync(
		BanchoConnection connection,
		Submission submission,
		BeatmapChecksums? beatmap,
		SubmittedClient client,
		byte[]? replay,
		CancellationToken cancellationToken = default)
	{
		var room = lobby.RoomOf(connection);
		var round = room?.Rounds.LastRound is { } last && last.BeatmapHash == client.BeatmapHash ? last : null;
		var checkedBeatmap =
			beatmap ?? (round is not null ? new BeatmapChecksums(client.BeatmapHash, null) : null);
		if (checkedBeatmap is null) return ScoreRejection.UnknownBeatmap;

		var loginClient = connection.Login.Client!;
		if (Validate(submission, loginClient, checkedBeatmap, connection.User.Value.Name, client) is { } rejection)
			return rejection;

		var team = round is not null ? room!.Slots.Find(connection)?.Team : null;
		Score score;
		try
		{
			score = await scores.CreateAsync(
				submission.Score with { UserId = connection.User.Id }, cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			return ScoreRejection.Duplicate;
		}

		var roundScore = round is not null
			? await roundScores.CreateAsync(new RoundScore { Score = score, Round = round, Team = team },
				cancellationToken)
			: null;

		if (submission.Score.IsPassed && replay is { Length: >= MinReplayLength })
		{
			await using var content = new MemoryStream(replay, false);
			await replays.SaveAsync(score, content, cancellationToken);
		}

		// ponytail: get-then-write per user; add an atomic increment to the contract if one user's submissions can overlap.
		var current = await stats.GetAsync(connection.User, submission.Score.Mode, cancellationToken);
		current.PlayCount++;
		current.TotalScore += submission.Score.TotalScore;
		if (submission.Score.IsPassed)
			// Every beatmap reports as Approved (see Beatmapset.Status), so every passed score counts toward ranked score.
			current.RankedScore += submission.Score.TotalScore;

		await stats.CreateOrUpdateAsync(current, cancellationToken);

		_events.Writer.TryWrite(new ScoreSubmitted(connection.User, score, current, roundScore, round is not null ? room : null));
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
	/// <param name="submittedClient">The osu! client information sent with the submission.</param>
	/// <returns><see langword="null" /> if the submission is authentic; otherwise, the first reason it is rejected.</returns>
	private static ScoreRejection? Validate(
		Submission submission,
		ClientInfo client,
		BeatmapChecksums beatmap,
		string playerName,
		SubmittedClient submittedClient)
	{
		var md5ByServer = ComputeSubmissionMd5();

		if (submittedClient.VersionDate != client.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			return ScoreRejection.VersionMismatch;
		if (submittedClient.Fingerprint != client.Fingerprint) return ScoreRejection.ClientHashMismatch;
		if (new Md5(Encoding.UTF8.GetBytes(submittedClient.UninstallId)) != client.Fingerprint.UninstallHash)
			return ScoreRejection.UninstallerHashMismatch;
		if (new Md5(Encoding.UTF8.GetBytes(submittedClient.DiskSignature)) != client.Fingerprint.DiskSignatureHash)
			return ScoreRejection.DiskSignatureHashMismatch;
		if (submission.HashByClient != md5ByServer) return ScoreRejection.SubmissionHashMismatch;
		if (submittedClient.BeatmapHash != beatmap.Hash) return ScoreRejection.BeatmapHashMismatch;

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
				$"{submittedClient.VersionDate}{submission.Score.Timestamp:yyMMddHHmmss}{submittedClient.ClientHash}{beatmap.StoryboardHash?.ToString() ?? string.Empty}";
			var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
			return Convert.ToHexStringLower(hash);
		}

		#endregion
	}
}