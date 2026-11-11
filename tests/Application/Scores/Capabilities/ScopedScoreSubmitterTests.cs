using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Scores.Tests.Capabilities;

/// <summary>
///     Verifies who may submit a score on an osu! client: the client itself, holding
///     <see cref="UserPrivileges.Player" />.
/// </summary>
public sealed class ScopedScoreSubmitterTests
{
	private static readonly Md5 Hash = new("0123456789abcdef0123456789abcdef");

	private static Submission Submission()
	{
		var score = new ScoreData(null, null, GameMode.Standard, GameMods.NoMod,
			new ScoreState(new HitCounts(100, 0, 0, 0, 0, 0), 1000, 100, 100, false), Grade.S, true,
			DateTimeOffset.UnixEpoch);
		return new Submission { Score = score, HashByClient = Hash, Flags = OsuFlags.Clean };
	}

	private static SubmittedClient Client()
	{
		return new SubmittedClient(Hash.ToString(), TestApplication.CreateClient().Fingerprint, "uninstall", "disk",
			"20260101", Hash);
	}

	[Fact]
	public async Task UnknownBeatmap_Rejected()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("UnknownMap", "password");
		var client = Assert.IsType<StableConnection>(await app.LoginAsync(user, SessionType.Stable));

		var (result, rejection) = await app.Provider.GetRequiredService<ScoreSubmitter>().ScopeTo(client)
			.SubmitAsync(Submission(), null, Client(), null, TestContext.Current.CancellationToken);

		Assert.Equal(ScoreSubmissionOutcome.Rejected, result);
		Assert.Equal(ScoreRejectionReason.UnknownBeatmap, rejection);
	}
}