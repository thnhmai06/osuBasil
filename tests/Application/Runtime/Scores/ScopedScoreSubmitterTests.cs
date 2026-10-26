using Basil.Application.Persistence.Scores;
using Basil.Application.Runtime.Scores;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Runtime.Tests.Scores;

/// <summary>
///     Verifies who may submit a score on an osu! client: the client itself, holding
///     <see cref="UserPermissions.ScoreSubmit" />.
/// </summary>
public sealed class ScopedScoreSubmitterTests
{
	private static readonly Md5 Hash = new("0123456789abcdef0123456789abcdef");

	private static Submission Submission()
	{
		var score = new ScoreData(null, null, GameMode.Standard, GameMods.NoMod, new HitCounts(100, 0, 0, 0, 0, 0),
			1000, 100, 100, Grade.S, true, false, DateTimeOffset.UnixEpoch);
		return new Submission { Score = score, HashByClient = Hash };
	}

	private static SubmittedClient Client()
	{
		return new SubmittedClient(Hash.ToString(), TestApplication.CreateClient().Fingerprint, "uninstall", "disk",
			"20260101", Hash);
	}

	[Fact]
	public async Task SubmitAsync_WithScoreSubmitSuspended_IsDeniedWithThatPermission()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("NoScores", "password");
		app.AddRestriction(user, UserPermissions.ScoreSubmit);
		var client = Assert.IsType<BanchoConnection>(await app.LoginAsync(user, ConnectionType.Bancho));

		var (result, rejection) = await app.Provider.GetRequiredService<ScoreSubmitter>().ScopeTo(client)
			.SubmitAsync(Submission(), new BeatmapChecksums(Hash, null), Client(), null,
				TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ScoreSubmit, result.Missing);
		Assert.Null(rejection);
		Assert.Empty(app.ScoreEvents.Events);
	}

	[Fact]
	public async Task SubmitAsync_OnABeatmapTheServerDoesNotHaveAndNoRound_IsRejectedAsUnknownBeatmap()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("UnknownMap", "password");
		var client = Assert.IsType<BanchoConnection>(await app.LoginAsync(user, ConnectionType.Bancho));

		var (result, rejection) = await app.Provider.GetRequiredService<ScoreSubmitter>().ScopeTo(client)
			.SubmitAsync(Submission(), null, Client(), null, TestContext.Current.CancellationToken);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.UnknownBeatmap, rejection);
	}
}