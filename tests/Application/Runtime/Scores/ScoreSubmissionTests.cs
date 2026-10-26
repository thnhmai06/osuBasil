using Basil.Application.Persistence.Scores;
using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Scores;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Runtime.Tests.Scores;

/// <summary>
///     Submitting a score on a game client, as the <c>SubmitAsync</c> contract states it: the client's own
///     permission, one rejection reason per failed check, no duplicates, the replay rule, the statistics and the
///     association with the latest round of the player's room.
/// </summary>
public sealed class ScoreSubmissionTests
{
	private static readonly BeatmapChecksums Checksums = new(ScoreSubmissionFixture.Beatmap, null);

	private static Task<(Result<ScoreSubmissionOutcome> Result, ScoreRejection? Rejection)> SubmitAsync(
		ScoreSubmissionFixture f, Submission submission, SubmittedClient? client = null,
		BeatmapChecksums? beatmap = null, Stream? replay = null, bool unknownBeatmap = false)
	{
		return f.App.Provider.GetRequiredService<ScoreSubmitter>().ScopeTo(f.Client).SubmitAsync(submission, unknownBeatmap ? null : beatmap ?? Checksums,
			client ?? ScoreSubmissionFixture.GenuineClient(), replay, TestContext.Current.CancellationToken);
	}

	private static MemoryStream Replay(int length)
	{
		return new MemoryStream(Enumerable.Range(0, length).Select(i => (byte)(i + 1)).ToArray());
	}

	private static void AssertNothingRecorded(ScoreSubmissionFixture f)
	{
		Assert.Empty(f.Scores);
		Assert.Empty(f.Replays);
		Assert.Null(f.StatsOf(GameMode.Standard));
		Assert.Empty(f.App.ScoreEvents.Events);
	}

	// ----- the submission checksum is the legacy osu! formula -----

	[Fact]
	public async Task SubmitScoreAsync_WithTheChecksumOfTheLegacyFormula_IsAccepted()
	{
		// raw = "chickenmcnuggets100o1500smustard00uu" + beatmap + "100" + "True" + "Submitter" + "1000" + "S" + "0" + "Q" + "True"
		//       + "0" + "20260101" + "260102030405" + "client-hash"; its MD5 was computed by md5sum, not by this code base.
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = new Submission
		{
			Score = ScoreSubmissionFixture.PassedScore(),
			HashByClient = new Md5("cc362f4892f70954e6f38d1c967ba2b1")
		};

		var (result, rejection) = await SubmitAsync(f, submission);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result.Outcome);
		Assert.Null(rejection);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAStoryboardOnTheBeatmap_ExpectsTheStoryboardInTheChecksum()
	{
		// The osu! client appends the storyboard checksum of the beatmap when it has one.
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var storyboard = new Md5("fedcba9876543210fedcba9876543210");
		var score = ScoreSubmissionFixture.PassedScore();

		var withStoryboard = await SubmitAsync(f, f.Genuine(score, storyboard),
			beatmap: new BeatmapChecksums(ScoreSubmissionFixture.Beatmap, storyboard));
		var without = await SubmitAsync(f, f.Genuine(score),
			beatmap: new BeatmapChecksums(ScoreSubmissionFixture.Beatmap, storyboard));

		Assert.Equal(ScoreSubmissionOutcome.Ok, withStoryboard.Result.Outcome);
		Assert.Equal(ScoreRejection.SubmissionHashMismatch, without.Rejection);
	}

	// ----- permission and ownership -----

	[Fact]
	public async Task SubmitScoreAsync_WithoutTheScoreSubmitPermission_IsDeniedWithExactlyThatPermission()
	{
		using var f =
			await ScoreSubmissionFixture.CreateAsync(permissions: UserPermissionsGroups.Default &
			                                                      ~UserPermissions.ScoreSubmit);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ScoreSubmit, result.Missing);
		Assert.Null(rejection);
		AssertNothingRecorded(f);
	}

	// ----- one test per rejection reason -----

	[Fact]
	public async Task SubmitScoreAsync_WithAnotherClientVersion_IsRejectedAsVersionMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient() with { VersionDate = "20250101" };

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.VersionMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAnotherClientFingerprint_IsRejectedAsClientHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var genuine = ScoreSubmissionFixture.GenuineClient();
		var client = genuine with
		{
			Fingerprint = genuine.Fingerprint with { OsuPathHash = new Md5("ffffffffffffffffffffffffffffffff") }
		};

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.ClientHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAnotherUninstallId_IsRejectedAsUninstallerHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient() with { UninstallId = "someone-else" };

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.UninstallerHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAnotherDiskSignature_IsRejectedAsDiskSignatureHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient() with { DiskSignature = "another-disk" };

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.DiskSignatureHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAChecksumThatDoesNotMatchTheScore_IsRejectedAsSubmissionHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var score = ScoreSubmissionFixture.PassedScore();
		var submission = new Submission { Score = score, HashByClient = new Md5("00000000000000000000000000000000") };

		var (result, rejection) = await SubmitAsync(f, submission);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.SubmissionHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SubmitScoreAsync_WhenAFieldOfTheScoreChangedAfterTheChecksum_IsRejectedAsSubmissionHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var genuine = f.Genuine(ScoreSubmissionFixture.PassedScore());
		var tampered = genuine with { Score = genuine.Score with { TotalScore = 999_999 } };

		var (_, rejection) = await SubmitAsync(f, tampered);

		Assert.Equal(ScoreRejection.SubmissionHashMismatch, rejection);
	}

	[Fact]
	public async Task SubmitScoreAsync_OnAnotherBeatmapThanTheOneChecked_IsRejectedAsBeatmapHashMismatch()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var client = ScoreSubmissionFixture.GenuineClient(new Md5("11111111111111111111111111111111"));

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), client);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.BeatmapHashMismatch, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SubmitScoreAsync_OnABeatmapTheServerDoesNotHave_IsRejectedAsUnknownBeatmap()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.UnknownBeatmap, rejection);
		AssertNothingRecorded(f);
	}

	[Fact]
	public async Task SubmitScoreAsync_WhenSeveralChecksFail_ReportsTheFirstInTheDocumentedOrder()
	{
		// Documented order: version, client hash, uninstaller, disk signature, submission checksum, beatmap.
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var genuine = ScoreSubmissionFixture.GenuineClient();
		var badSubmission = new Submission
		{
			Score = ScoreSubmissionFixture.PassedScore(), HashByClient = new Md5("00000000000000000000000000000000")
		};

		var beatmapAndHash = await SubmitAsync(f, badSubmission,
			genuine with { BeatmapHash = new Md5("11111111111111111111111111111111") });
		var disk = await SubmitAsync(f, badSubmission, genuine with { DiskSignature = "x" });
		var uninstall = await SubmitAsync(f, badSubmission, genuine with { DiskSignature = "x", UninstallId = "x" });
		var fingerprint = await SubmitAsync(f, badSubmission, genuine with
		{
			DiskSignature = "x", UninstallId = "x",
			Fingerprint = genuine.Fingerprint with { OsuPathHash = new Md5("ffffffffffffffffffffffffffffffff") }
		});
		var version = await SubmitAsync(f, badSubmission, genuine with
		{
			DiskSignature = "x", UninstallId = "x", VersionDate = "20250101",
			Fingerprint = genuine.Fingerprint with { OsuPathHash = new Md5("ffffffffffffffffffffffffffffffff") }
		});

		Assert.Equal(ScoreRejection.SubmissionHashMismatch, beatmapAndHash.Rejection);
		Assert.Equal(ScoreRejection.DiskSignatureHashMismatch, disk.Rejection);
		Assert.Equal(ScoreRejection.UninstallerHashMismatch, uninstall.Rejection);
		Assert.Equal(ScoreRejection.ClientHashMismatch, fingerprint.Rejection);
		Assert.Equal(ScoreRejection.VersionMismatch, version.Rejection);
	}

	// ----- duplicates -----

	[Fact]
	public async Task SubmitScoreAsync_WithAChecksumAlreadyStored_IsRejectedAsDuplicate()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = f.Genuine(ScoreSubmissionFixture.PassedScore());
		Assert.Equal(ScoreSubmissionOutcome.Ok, (await SubmitAsync(f, submission)).Result.Outcome);

		var (result, rejection) = await SubmitAsync(f, submission, replay: Replay(100));

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.Duplicate, rejection);
		Assert.Single(f.Scores);
		Assert.Single(f.App.ScoreEvents.Events);
		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAValidScore_StoresTheClientChecksumToIdentifyTheSubmission()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = f.Genuine(ScoreSubmissionFixture.PassedScore());

		await SubmitAsync(f, submission);

		Assert.Equal(submission.HashByClient, Assert.Single(f.Scores).Value.Checksum);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithADuplicate_DoesNotCountAgainstTheStatistics()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var submission = f.Genuine(ScoreSubmissionFixture.PassedScore());
		await SubmitAsync(f, submission);

		await SubmitAsync(f, submission);

		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(1, stats.PlayCount);
		Assert.Equal(1000, stats.TotalScore);
	}

	// ----- recording -----

	[Fact]
	public async Task SubmitScoreAsync_WithAValidScore_StoresItForThePlayerAndPublishesOneScoreSubmitted()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		Assert.Equal(ScoreSubmissionOutcome.Ok, result.Outcome);
		Assert.Null(rejection);
		var stored = Assert.Single(f.Scores);
		Assert.Equal(f.User.Id, stored.Value.UserId);
		var published = Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));
		Assert.Equal(f.User, published.Player);
		Assert.Equal(stored, published.Score);
	}

	[Fact]
	public async Task SubmitScoreAsync_ThePublishedStatisticsAreTheOnesAfterTheScore()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		var published = Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));
		Assert.Equal(1, published.Stats.PlayCount);
		Assert.Equal(f.StatsOf(GameMode.Standard), published.Stats);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAPassedPlay_AddsToPlayCountTotalScoreAndRankedScore()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(1, stats.PlayCount);
		Assert.Equal(1000, stats.TotalScore);
		Assert.Equal(1000, stats.RankedScore);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAFailedPlay_AddsToPlayCountAndTotalScoreButNotRankedScore()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var failed = ScoreSubmissionFixture.PassedScore() with { IsPassed = false, Grade = Grade.F };

		var (result, _) = await SubmitAsync(f, f.Genuine(failed));

		Assert.Equal(ScoreSubmissionOutcome.Ok, result.Outcome);
		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(1, stats.PlayCount);
		Assert.Equal(1000, stats.TotalScore);
		Assert.Equal(0, stats.RankedScore);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithSeveralScores_AccumulatesTheStatistics()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var first = ScoreSubmissionFixture.PassedScore();
		var second = first with { TotalScore = 250 };

		await SubmitAsync(f, f.Genuine(first));
		await SubmitAsync(f, f.Genuine(second));

		var stats = f.StatsOf(GameMode.Standard)!;
		Assert.Equal(2, stats.PlayCount);
		Assert.Equal(1250, stats.TotalScore);
		Assert.Equal(1250, stats.RankedScore);
	}

	[Fact]
	public async Task SubmitScoreAsync_InAnotherMode_KeepsTheStatisticsOfTheModesApart()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var taiko = ScoreSubmissionFixture.PassedScore() with { Mode = GameMode.Taiko };

		await SubmitAsync(f, f.Genuine(taiko));

		Assert.Equal(1, f.StatsOf(GameMode.Taiko)!.PlayCount);
		Assert.Null(f.StatsOf(GameMode.Standard));
	}

	// ----- replay: only a passed play, only 24 bytes or more -----

	[Theory]
	[InlineData(24)]
	[InlineData(25)]
	[InlineData(4096)]
	public async Task SubmitScoreAsync_WithAPassedPlayAndAReplayOfAtLeast24Bytes_KeepsTheReplay(int length)
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
	public async Task SubmitScoreAsync_WithAReplayShorterThan24Bytes_KeepsNoReplayButStillRecordsTheScore(int length)
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		using var replay = Replay(length);

		var (result, _) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), replay: replay);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result.Outcome);
		Assert.Single(f.Scores);
		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAFailedPlayAndALongReplay_KeepsNoReplay()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var failed = ScoreSubmissionFixture.PassedScore() with { IsPassed = false, Grade = Grade.F };
		using var replay = Replay(100);

		await SubmitAsync(f, f.Genuine(failed), replay: replay);

		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithNoReplay_RecordsTheScoreWithoutOne()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();

		var (result, _) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));

		Assert.Equal(ScoreSubmissionOutcome.Ok, result.Outcome);
		Assert.Empty(f.Replays);
	}

	[Fact]
	public async Task SubmitScoreAsync_WithAReplay_LeavesTheStreamOpen()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		using var replay = Replay(100);

		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()), replay: replay);

		Assert.True(replay.CanRead);
	}

	// ----- rounds -----

	private static async Task<Room> StartRoundAsync(ScoreSubmissionFixture f,
		GameTeamType teamType = GameTeamType.HeadToHead)
	{
		var (room, opened) = await f.App.Lobby.ScopeTo(f.Client).OpenRoomAsync("round room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var scope = room!.ScopeTo(f.Client);
		Assert.True((await scope.Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap, TeamType: teamType),
			TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await scope.Rounds.StartAsync(TestContext.Current.CancellationToken)).IsOk);
		return room;
	}

	[Fact]
	public async Task SubmitScoreAsync_OnTheRoundBeatmapTheServerDoesNotHave_IsAccepted()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		await StartRoundAsync(f);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result.Outcome);
		Assert.Null(rejection);
		Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));
	}

	[Fact]
	public async Task SubmitScoreAsync_InARound_IsTakenByTheRoundOnceTheHandlerGetsTheEvent()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var room = await StartRoundAsync(f);
		await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()));
		var submitted = Assert.IsType<ScoreSubmitted>(Assert.Single(f.App.ScoreEvents.Events));

		await new ScoreHandler(f.App.Lobby).Handle(submitted, TestContext.Current.CancellationToken);
		Assert.True((await room.ScopeTo(f.Client).Rounds.CompleteAsync(TestContext.Current.CancellationToken)).IsOk);

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
		Assert.True((await room.ScopeTo(stays).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await room.ScopeTo(f.Client).Rounds.CompleteAsync(TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await room.ScopeTo(f.Client).Members.LeaveAsync(TestContext.Current.CancellationToken)).IsOk);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Ok, result.Outcome);
		Assert.Null(rejection);
	}

	[Fact]
	public async Task SubmitScoreAsync_OnTheRoundBeatmapTheServerDoesNotHave_OnceTheRoundIsDecided_IsRejected()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		var room = await StartRoundAsync(f);
		Assert.True((await room.ScopeTo(f.Client).Rounds.CompleteAsync(TestContext.Current.CancellationToken)).IsOk);
		f.App.Clock.Advance(RoomRounds.ScoreWait);

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.UnknownBeatmap, rejection);
	}

	[Fact]
	public async Task SubmitScoreAsync_OnAnotherUnknownBeatmapWhileInARound_IsRejectedAsUnknownBeatmap()
	{
		using var f = await ScoreSubmissionFixture.CreateAsync();
		await StartRoundAsync(f);
		var other = new Md5("11111111111111111111111111111111");

		var (result, rejection) = await SubmitAsync(f, f.Genuine(ScoreSubmissionFixture.PassedScore()),
			ScoreSubmissionFixture.GenuineClient(other), unknownBeatmap: true);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.UnknownBeatmap, rejection);
	}
}