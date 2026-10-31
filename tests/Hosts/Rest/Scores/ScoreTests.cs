using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Storage;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Scores;

/// <summary>Scores are public; replays need a token.</summary>
[Collection("REST host")]
public sealed class ScoreTests(RestHostFixture fixture)
{
	private async Task<ScoreRecord> StoreAsync(int userId, int totalScore)
	{
		return await fixture.Host.Services.GetRequiredService<IScoreRepository>().CreateAsync(new ScoreData(userId,
			null,
			GameMode.Standard, GameMods.NoMod, new HitCounts(100, 0, 0, 0, 0, 0), totalScore, 100, 100, Grade.S, true,
			true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
	}

	[Fact]
	public async Task Get_AScore_IsPublic_AndShowsWhatThePlayerScored()
	{
		var user = await fixture.Host.CreateUserAsync("ScoreOwner");
		var score = await StoreAsync(user.Id, 123456);
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, $"/v1/scores/{score.Id}");

		Assert.Equal(200, status);
		Assert.Equal(123456, (int)envelope!["data"]!["totalScore"]!);
		Assert.Equal(user.Id, (int)envelope["data"]!["userId"]!);
	}

	[Fact]
	public async Task List_ByUser_ReturnsOnlyThatUsersScores_AndPagingIsBounded()
	{
		var first = await fixture.Host.CreateUserAsync("ScoreListA");
		var second = await fixture.Host.CreateUserAsync("ScoreListB");
		await StoreAsync(first.Id, 10);
		await StoreAsync(second.Id, 20);
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, $"/v1/scores?user={first.Id}");
		var (tooMany, _) = await client.CallAsync(HttpMethod.Get, "/v1/scores?limit=101");

		Assert.Equal(200, status);
		Assert.All(envelope!["data"]!.AsArray(), item => Assert.Equal(first.Id, (int)item!["userId"]!));
		Assert.Equal(422, tooMany);
	}

	[Fact]
	public async Task List_ForAnUnknownBeatmapOrUser_Answers404()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (beatmap, _) = await client.CallAsync(HttpMethod.Get, "/v1/scores?beatmap=999999999");
		var (user, _) = await client.CallAsync(HttpMethod.Get, "/v1/scores?user=999999999");

		Assert.Equal(404, beatmap);
		Assert.Equal(404, user);
	}

	[Fact]
	public async Task Get_UnknownScore_Answers404()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/scores/999999999");

		Assert.Equal(404, status);
	}

	[Fact]
	public async Task Replay_NeedsAToken_AndAScoreWithoutOneAnswers404()
	{
		var user = await fixture.Host.CreateUserAsync("ScoreReplay");
		var score = await StoreAsync(user.Id, 5);
		var (client, _) = await fixture.SignInAsync("ScoreViewer");
		using (client)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, _) = await anon.CallAsync(HttpMethod.Get, $"/v1/scores/{score.Id}/replay");
			var (missing, _) = await client.CallAsync(HttpMethod.Get, $"/v1/scores/{score.Id}/replay");

			Assert.Equal(401, denied);
			Assert.Equal(404, missing);
		}
	}

	[Fact]
	public async Task Replay_OfAStoredReplay_IsReturned()
	{
		var user = await fixture.Host.CreateUserAsync("ScoreHasRep");
		var score = await StoreAsync(user.Id, 6);
		await fixture.Host.Services.GetRequiredService<IReplayStorage>().SaveAsync(score, new MemoryStream([9, 8, 7]));
		var (client, _) = await fixture.SignInAsync("ScoreWatcher");
		using (client)
		{
			using var response = await client.GetAsync($"/v1/scores/{score.Id}/replay");

			Assert.Equal(200, (int)response.StatusCode);
			Assert.Equal([9, 8, 7], await response.Content.ReadAsByteArrayAsync());
		}
	}
}