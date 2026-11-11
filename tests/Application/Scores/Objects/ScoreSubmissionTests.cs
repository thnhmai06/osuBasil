using Basil.Application.Shared;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Client;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Multiplayer.Handlers;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Scores.Tests.Objects;

/// <summary>
///     Submitting a score on a game client, as the <c>SubmitAsync</c> contract states it: the client's own
///     privilege, one rejection reason per failed check, no duplicates, the replay rule, the statistics and the
///     association with the latest round of the player's room.
/// </summary>
public sealed class ScoreSubmissionTests
{
	/// <summary>The wait for scores the server settings default to, 10 seconds.</summary>
	private static readonly TimeSpan ScoreWait = TimeSpan.FromSeconds(10);

	private static readonly BeatmapChecksums Checksums = new(ScoreSubmissionFixture.Beatmap, null);

	private static Task<(ScoreSubmissionOutcome Result, ScoreRejectionReason? Rejection)> SubmitAsync(
		ScoreSubmissionFixture f, Submission submission, SubmittedClient? client = null,
		BeatmapChecksums? beatmap = null, Stream? replay = null, bool unknownBeatmap = false)
	{
		return f.App.Provider.GetRequiredService<ScoreSubmitter>().ScopeTo(f.Client).SubmitAsync(submission,
			unknownBeatmap ? null : beatmap ?? Checksums,
			client ?? ScoreSubmissionFixture.GenuineClient(), replay, TestContext.Current.CancellationToken);
	}

	private static MemoryStream Replay(int length)
	{
		return new MemoryStream([.. Enumerable.Range(0, length).Select(i => (byte)(i + 1))]);
	}

	private static void AssertNothingRecorded(ScoreSubmissionFixture f)
	{
		Assert.Empty(f.Scores);
		Assert.Empty(f.Replays);
		Assert.Null(f.StatsOf(GameMode.Standard));
		Assert.Empty(f.App.ScoreEvents.Events);
	}

	[Fact]
	public async Task StatsNotStored_ErrorNoPublish()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		f.App.Provider.GetRequiredService<IScoreRepository>()
			.CreateWithPlayAsync(Arg.Any<ScoreData>(), Arg.Any<long>())
			.Returns(_ => Task.FromException<(ScoreRecord, UserStats)>(new InvalidOperationException("store down")));
		var score = ScoreSubmissionFixture.PassedScore();

		await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitAsync(f, f.Genuine(score)));

		Assert.Empty(f.App.ScoreEvents.Events);
	}

	#region The submission checksum is the legacy osu! formula

	[Fact]
	public async Task LegacyChecksum_Accepted()
	{
		// raw = "chickenmcnuggets100o1500smustard00uu" + beatmap + "100" + "True" + "Submitter" + "1000" + "S" + "0" + "Q" + "True"
		//       + "0" + "20260101" + "260102030405" + "client-hash"; its MD5 was computed by md5sum, not by this code base.
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = new Submission
		{
			Score = ScoreSubmissionFixture.PassedScore(),
			Flags = OsuFlags.Clean,
			HashByClient = new Md5("cc362f4892f70954e6f38d1c967ba2b1")
		};

		var (result, rejection) = await SubmitAsync(f, submission);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result);
		Assert.Null(rejection);
	}

	[Fact]
	public async Task StoryboardOnBeatmap_ChecksumIncludesStoryboard()
	{
		// The osu! client appends the storyboard checksum of the beatmap when it has one.
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var storyboard = new Md5("fedcba9876543210fedcba9876543210");
		var score = ScoreSubmissionFixture.PassedScore();

		var withStoryboard = await SubmitAsync(f, f.Genuine(score, storyboard),
			beatmap: new BeatmapChecksums(ScoreSubmissionFixture.Beatmap, storyboard));
		var without = await SubmitAsync(f, f.Genuine(score),
			beatmap: new BeatmapChecksums(ScoreSubmissionFixture.Beatmap, storyboard));

		Assert.Equal(ScoreSubmissionOutcome.Ok, withStoryboard.Result);
		Assert.Equal(ScoreRejectionReason.SubmissionHashMismatch, without.Rejection);
	}

	#endregion

	#region Privilege and ownership

	[Fact]
	public async Task NoSubmitPrivilege_DeniedNamingIt()
	{
		using var f =
			await ScoreSubmissionFixture.CreateAsync(privileges: UserPrivilegesExtensions.Default &
			                                                      ~UserPrivileges.Player);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		Assert.False(result == ScoreSubmissionOutcome.Ok);
		Assert.Equal(ScoreSubmissionOutcome.Denied, result);
		Assert.Null(rejection);
		AssertNothingRecorded(f);
	}

	#endregion

	#region One test per rejection reason

	[Fact]
	public async Task OtherVersion_RejectedVersionMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient() with { VersionDate = "20250101" };

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.VersionMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task OtherClientHash_ClientHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var genuine = ScoreSubmissionFixture.GenuineClient();
		var client = genuine with
		{
			Fingerprint = genuine.Fingerprint with { PathHash = new Md5("ffffffffffffffffffffffffffffffff") }
		};

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.ClientHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task OtherUninstallId_UninstallerHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient() with { UninstallId = "someone-else" };

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.UninstallerHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task OtherDiskSignature_DiskHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient() with { DiskSignature = "another-disk" };

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.DiskSignatureHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task ChecksumMismatch_SubmissionHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var score = ScoreSubmissionFixture.PassedScore();
		var submission = new Submission
			{ Score = score, Flags = OsuFlags.Clean, HashByClient = new Md5("00000000000000000000000000000000") };

		var (result, rejection) = await SubmitAsync(f, submission);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.SubmissionHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task ScoreFieldChanged_SubmissionHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var genuine = f.Genuine(ScoreSubmissionFixture.PassedScore());
		var tampered = genuine with
		{
			Score = genuine.Score with { LastState = genuine.Score.LastState with { TotalScore = 999_999 } }
		};

		var (_, rejection) = await SubmitAsync(f, tampered);

		Assert.Equal(ScoreRejectionReason.SubmissionHashMismatch, rejection);
	}

	[Fact]
	public async Task OtherBeatmap_BeatmapHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient(new Md5("11111111111111111111111111111111"));

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.BeatmapHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task BeatmapOffServer_UnknownBeatmap()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.UnknownBeatmap, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SeveralChecksFail_FirstInOrder()
	{
		// Documented order: version, client hash, uninstaller, disk signature, submission checksum, beatmap.
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var genuine = ScoreSubmissionFixture.GenuineClient();
		var badSubmission = new Submission
		{
			Flags = OsuFlags.Clean,
			Score = ScoreSubmissionFixture.PassedScore(), HashByClient = new Md5("00000000000000000000000000000000")
		};

		var beatmapAndHash = await SubmitAsync(f, badSubmission,
			genuine with { BeatmapHash = new Md5("11111111111111111111111111111111") });
		var disk = await SubmitAsync(f, badSubmission, genuine with { DiskSignature = "x" });
		var uninstall = await SubmitAsync(f, badSubmission, genuine with { DiskSignature = "x", UninstallId = "x" });
		var fingerprint = await SubmitAsync(f, badSubmission, genuine with
		{
			DiskSignature = "x", UninstallId = "x",
			Fingerprint = genuine.Fingerprint with { PathHash = new Md5("ffffffffffffffffffffffffffffffff") }
		});
		var version = await SubmitAsync(f, badSubmission, genuine with
		{
			DiskSignature = "x", UninstallId = "x", VersionDate = "20250101",
			Fingerprint = genuine.Fingerprint with { PathHash = new Md5("ffffffffffffffffffffffffffffffff") }
		});

		Assert.Equal(ScoreRejectionReason.SubmissionHashMismatch, beatmapAndHash.Rejection);
		Assert.Equal(ScoreRejectionReason.DiskSignatureHashMismatch, disk.Rejection);
		Assert.Equal(ScoreRejectionReason.UninstallerHashMismatch, uninstall.Rejection);
		Assert.Equal(ScoreRejectionReason.ClientHashMismatch, fingerprint.Rejection);
		Assert.Equal(ScoreRejectionReason.VersionMismatch, version.Rejection);
	}

	#endregion

	#region Duplicates

	[Fact]
	public async Task ChecksumStored_RejectedAsDuplicate()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = f.Genuine(ScoreSubmissionFixture.PassedScore());
		Assert.Equal(ScoreSubmissionOutcome.Ok, (await SubmitAsync(f, submission)).Result);

		var (result, rejection) = await SubmitAsync(f, submission, replay: Replay(100));

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.Duplicate, rejection);
		Assert.Single(f.Scores);
		Assert.Single(f.App.ScoreEvents.Events);
		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task ValidScore_StoresClientChecksum()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = f.Genuine(ScoreSubmissionFixture.PassedScore());

		await SubmitAsync(f, submission);

		Assert.Equal(submission.HashByClient, Assert.Single(f.Scores).Value.Checksum);
	}

	[Fact]
	public async Task Duplicate_NotCountedStats()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = f.Genuine(ScoreSubmissionFixture.PassedScore());
		await SubmitAsync(f, submission);

		await SubmitAsync(f, submission);

		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(1, stats.PlayCount);
		Assert.Equal(1000, stats.TotalScore);
	}

	#endregion

	#region Recording

	[Fact]
	public async Task ValidScore_StoredAndPublished()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		Assert.Equal(ScoreSubmissionOutcome.Ok, result);
		Assert.Null(rejection);
		var stored = Assert.Single(f.Scores);
		Assert.Equal(f.User.Id, stored.Value.UserId);
		var published = Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));
		Assert.Equal(f.User, published.Player);
		Assert.Equal(stored, published.Score);
	}

	[Fact]
	public async Task StatsPublished_AfterScore()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		var published = Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));
		Assert.Equal(1, published.Stats.PlayCount);
		Assert.Equal(f.StatsOf(GameMode.Standard), published.Stats);
	}

	[Fact]
	public async Task PassedPlay_PlayTotalRanked()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(1, stats.PlayCount);
		Assert.Equal(1000, stats.TotalScore);
		Assert.Equal(1000, stats.RankedScore);
	}

	[Fact]
	public async Task FailedPlay_TotalNoRank()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var failed = ScoreSubmissionFixture.PassedScore() with { IsPassed = false, Grade = Grade.F };

		var (result, _) = await SubmitAsync(f, f.Genuine(failed));

		Assert.Equal(ScoreSubmissionOutcome.Ok, result);
		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(1, stats.PlayCount);
		Assert.Equal(1000, stats.TotalScore);
		Assert.Equal(0, stats.RankedScore);
	}

	[Fact]
	public async Task WithSeveralScores_AccumulatesTheStatistics()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var first = ScoreSubmissionFixture.PassedScore();
		var second = first with { LastState = first.LastState with { TotalScore = 250 } };

		await SubmitAsync(f, f.Genuine(first));
		await SubmitAsync(f, f.Genuine(second));

		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(2, stats.PlayCount);
		Assert.Equal(1250, stats.TotalScore);
		Assert.Equal(1250, stats.RankedScore);
	}

	[Fact]
	public async Task OtherMode_StatsKeptApart()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var taiko = ScoreSubmissionFixture.PassedScore() with { Mode = GameMode.Taiko };

		await SubmitAsync(f, f.Genuine(taiko));

		Assert.Equal(1, f.StatsOf(GameMode.Taiko)!.PlayCount);
		Assert.Null(f.StatsOf(GameMode.Standard));
	}

	#endregion

	#region Replay: only a passed play, only 24 bytes or more

	[Theory]
	[InlineData(24)]
	[InlineData(25)]
	[InlineData(4096)]
	public async Task PassedWithReplay_KeepsReplay(int length)
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		using var replay = Replay(length);

		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), replay: replay);

		var kept = Assert.Single(f.Replays);
		Assert.Equal(f.Scores[0], kept.Score);
		Assert.Equal(Replay(length).ToArray(), kept.Content);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(23)]
	public async Task ShortReplay_ScoreRecorded(int length)
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		using var replay = Replay(length);

		var (result, _) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), replay: replay);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result);
		Assert.Single(f.Scores);
		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task FailedWithReplay_KeepsNoReplay()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var failed = ScoreSubmissionFixture.PassedScore() with { IsPassed = false, Grade = Grade.F };
		using var replay = Replay(100);

		await SubmitAsync(f, f.Genuine(failed), replay: replay);

		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task NoReplay_ScoreRecorded()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		var (result, _) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		Assert.Equal(ScoreSubmissionOutcome.Ok, result);
		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task Replay_LeavesStreamOpen()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		using var replay = Replay(100);

		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), replay: replay);

		Assert.True(replay.CanRead);
	}

	#endregion

	#region Rounds

	private static async Task<Room> StartRoundAsync(ScoreSubmissionFixture f,
		GameTeamType teamType = GameTeamType.HeadToHead)
	{
		var (room, opened) = await f.App.Lobby.ScopeTo(f.Client).OpenRoomAsync("round room", "", RoomSource.Game, false);
		Assert.True(opened == RoomOutcome.Ok);
		var scope = room!.ScopeTo(f.Client);
		Assert.True((await scope.Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap, TeamType: teamType))) == RoomOutcome.Ok);
		Assert.True((await scope.Rounds.StartAsync()) == RoomOutcome.Ok);
		return room;
	}

	[Fact]
	public async Task RoundOffServer_Accepted()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		await StartRoundAsync(f);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result);
		Assert.Null(rejection);
		Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));
	}

	[Fact]
	public async Task InRound_TakenOnEvent()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var room = await StartRoundAsync(f);
		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));
		var submitted = Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));

		await new ScoreHandler(f.App.Lobby).Handle(submitted, TestContext.Current.CancellationToken);
		Assert.True((await room.ScopeTo(f.Client).Rounds.CompleteAsync()) == RoomOutcome.Ok);

		var taken = Assert.Single(f.App.RoomEvents.Events.OfType<RoomRoundScoreSubmitted>());
		Assert.Equal(submitted.Score, taken.Score.Score);
		Assert.Equal(room.Rounds.LastRound, taken.Round);
		var decided = Assert.Single(f.App.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Equal(f.User.Id, Assert.IsType<HeadToHeadResult>(decided.Result).WinnerId);
	}

	[Fact]
	public async Task
		SubmitScoreAsync_AfterCompletingTheRoundAndLeaving_StillAcceptsTheRoundBeatmapTheServerDoesNotHave()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var room = await StartRoundAsync(f);
		var stays = await RoomFixture.PlayerAsync(f.App, "Stays");
		Assert.True((await room.ScopeTo(stays).Members.JoinAsync("")) == RoomOutcome.Ok);
		Assert.True((await room.ScopeTo(f.Client).Rounds.CompleteAsync()) == RoomOutcome.Ok);
		Assert.True((await room.ScopeTo(f.Client).Members.LeaveAsync()) == RoomOutcome.Ok);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result);
		Assert.Null(rejection);
	}

	[Fact]
	public async Task UnknownRoundBeatmap_RejectedOnceDecided()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var room = await StartRoundAsync(f);
		Assert.True((await room.ScopeTo(f.Client).Rounds.CompleteAsync()) == RoomOutcome.Ok);
		f.App.Clock.Advance(ScoreWait);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.UnknownBeatmap, rejection);
	}

	[Fact]
	public async Task RoundOtherBeatmap_RejectedUnknown()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		await StartRoundAsync(f);
		var other = new Md5("11111111111111111111111111111111");

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			ScoreSubmissionFixture.GenuineClient(other), unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.UnknownBeatmap, rejection);
	}

	#endregion
}