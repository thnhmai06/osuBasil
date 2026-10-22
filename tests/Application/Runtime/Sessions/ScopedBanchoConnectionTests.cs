using Basil.Application.Persistence.Scores;
using Basil.Application.Runtime.Sessions;
using Basil.Testing;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Permissions;
using Basil.Domain.Scores;

namespace Basil.Application.Runtime.Tests.Sessions;

/// <summary>
///     Verifies who may submit a score on an osu! client: the client itself, holding
///     <see cref="Permissions.ScoreSubmit" />.
/// </summary>
public sealed class ScopedBanchoConnectionTests
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
	public async Task SubmitScoreAsync_WithScoreSubmitSuspended_IsDeniedWithThatPermission()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("NoScores", "password");
		app.AddRestriction(user, Permissions.ScoreSubmit);
		var client = Assert.IsType<BanchoConnection>(await app.LoginAsync(user, ConnectionType.Bancho));

		var (result, rejection) = await client.ScopeTo(client)
			.SubmitScoreAsync(Submission(), new BeatmapChecksums(Hash, null), Client(), null, TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(Permissions.ScoreSubmit, result.Missing);
		Assert.Null(rejection);
		Assert.Empty(app.ScoreEvents.Events);
	}

	[Fact]
	public async Task SubmitScoreAsync_ByAnotherConnection_IsRefusedAsOtherClient()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("ScoreOwner", "password");
		var other = await app.CreateUserAsync("ScoreOther", "password");
		var client = Assert.IsType<BanchoConnection>(await app.LoginAsync(owner, ConnectionType.Bancho));
		var stranger = await app.LoginAsync(other, ConnectionType.Bancho);

		var (result, _) = await client.ScopeTo(stranger)
			.SubmitScoreAsync(Submission(), new BeatmapChecksums(Hash, null), Client(), null, TestContext.Current.CancellationToken);

		Assert.Equal(ConnectionOutcome.OtherClient, result.Outcome);
		Assert.Null(result.Missing);
	}

	[Fact]
	public async Task SubmitScoreAsync_OnABeatmapTheServerDoesNotHaveAndNoRound_IsRejectedAsUnknownBeatmap()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("UnknownMap", "password");
		var client = Assert.IsType<BanchoConnection>(await app.LoginAsync(user, ConnectionType.Bancho));

		var (result, rejection) = await client.ScopeTo(client).SubmitScoreAsync(Submission(), null, Client(), null, TestContext.Current.CancellationToken);

		Assert.Equal(ConnectionOutcome.Rejected, result.Outcome);
		Assert.Equal(ScoreRejection.UnknownBeatmap, rejection);
	}
}
