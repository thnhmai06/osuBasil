using Basil.Application.Scores.Persistence;
using Basil.Domain.Client;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Scores;

[Collection("REST host")]
public sealed class ScorePerformanceTests(RestHostFixture fixture)
{
	private async Task<ScoreRecord> StoreAsync(int userId, double? pp, bool passed = true, Md5? beatmap = null)
	{
		return await fixture.Host.Services.GetRequiredService<IScoreRepository>().CreateAsync(new ScoreData(userId,
			beatmap, GameMode.Standard, GameMods.NoMod,
			new ScoreState(new HitCounts(100, 0, 0, 0, 0, 0), 1000, 100, 100, true), Grade.S, passed,
			new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { Pp = pp });
	}

	private async Task<Md5> ServerBeatmapAsync()
	{
		return (await fixture.Host.ImportSampleAsync("Sets/selene-ui-mania.osz")).First().Hash;
	}

	private async Task<System.Text.Json.Nodes.JsonNode> GetAsync(ScoreRecord score)
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		var (_, envelope) = await client.CallAsync(HttpMethod.Get, $"/v1/scores/{score.Id}");
		return envelope!["data"]!;
	}

	[Fact]
	public async Task ScoreWithoutPp_ShowsNull()
	{
		var user = await fixture.Host.CreateUserAsync("PpPending");

		var data = await GetAsync(await StoreAsync(user.Id, null, beatmap: await ServerBeatmapAsync()));

		Assert.Null(data["pp"]);
	}

	[Fact]
	public async Task ScoreWithPp_ShowsIt()
	{
		var user = await fixture.Host.CreateUserAsync("PpDone");

		var data = await GetAsync(await StoreAsync(user.Id, 250.5));

		Assert.Equal(250.5, (double)data["pp"]!);
	}

	[Fact]
	public async Task ScoreOnBeatmapTheServerLacks_ShowsNull()
	{
		var user = await fixture.Host.CreateUserAsync("PpUnknownMap");

		var data = await GetAsync(await StoreAsync(user.Id, null, beatmap: new Md5(new string('a', 32))));

		Assert.Null(data["pp"]);
	}

	[Fact]
	public async Task FailedScore_ShowsNull()
	{
		var user = await fixture.Host.CreateUserAsync("PpFailed");

		var data = await GetAsync(await StoreAsync(user.Id, null, false));

		Assert.Null(data["pp"]);
	}

	[Fact]
	public async Task Recalculate_DeveloperOnly()
	{
		var (developer, _) = await fixture.SignInAsync("PpDeveloper",
			UserPrivilegesExtensions.Default | UserPrivileges.Developer);
		var (plain, _) = await fixture.SignInAsync("PpPlain");
		using (developer)
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (unauthorized, _) = await anon.CallAsync(HttpMethod.Post, "/v1/scores/performance/recalculate");
			var (denied, _) = await plain.CallAsync(HttpMethod.Post, "/v1/scores/performance/recalculate");
			var (accepted, _) = await developer.CallAsync(HttpMethod.Post, "/v1/scores/performance/recalculate");

			Assert.Equal(401, unauthorized);
			Assert.Equal(403, denied);
			Assert.Equal(202, accepted);
		}
	}
}
