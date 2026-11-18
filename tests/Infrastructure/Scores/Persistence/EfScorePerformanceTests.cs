using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Game.Scores;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Scores.Tests.Persistence;

[Collection("Persistence")]
public sealed class EfScorePerformanceTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset When = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private IScoreRepository Scores => Get<IScoreRepository>();

	private async Task<ScoreRecord> StoreAsync(string name, double? pp = null, bool passed = true)
	{
		var player = await Get<IUserRepository>().CreateAsync(new UserData { Name = name });
		return await Scores.CreateAsync(
			StoredScores.Data(player, StoredScores.Hash(name), 1000, When) with { PerformancePoints = pp, IsPassed = passed });
	}

	[Fact]
	public async Task NewScore_HasNoPp()
	{
		var score = await StoreAsync("pp_none");

		Assert.Null((await Scores.GetAsync(score.Id, TestContext.Current.CancellationToken))!.Value.PerformancePoints);
	}

	[Fact]
	public async Task StoredPp_ReadBack()
	{
		var score = await StoreAsync("pp_given", 321.5);

		Assert.Equal(321.5, (await Scores.GetAsync(score.Id, TestContext.Current.CancellationToken))!.Value.PerformancePoints);
	}

	[Fact]
	public async Task SetPp_ReplacesPp()
	{
		var score = await StoreAsync("pp_set");

		await Scores.SetPpAsync(score.Id, 77.25);
		await Scores.SetPpAsync(score.Id, 88.5);

		Assert.Equal(88.5, (await Scores.GetAsync(score.Id, TestContext.Current.CancellationToken))!.Value.PerformancePoints);
	}

	[Fact]
	public async Task SetPp_OtherScoresUntouched()
	{
		var target = await StoreAsync("pp_target");
		var other = await StoreAsync("pp_other");

		await Scores.SetPpAsync(target.Id, 10);

		Assert.Null((await Scores.GetAsync(other.Id, TestContext.Current.CancellationToken))!.Value.PerformancePoints);
	}

	[Fact]
	public async Task ListMissing_OnlyPassedScoresWithoutPp()
	{
		var missing = await StoreAsync("list_missing");
		await StoreAsync("list_done", 5);
		await StoreAsync("list_failed", passed: false);

		var listed = await Scores.ListForPpAsync(0, true, 100, TestContext.Current.CancellationToken);

		Assert.Equal(missing.Id, Assert.Single(listed).Id);
	}

	[Fact]
	public async Task ListAll_EveryPassedScoreButNoFailedOne()
	{
		var missing = await StoreAsync("all_missing");
		var done = await StoreAsync("all_done", 5);
		await StoreAsync("all_failed", passed: false);

		var listed = await Scores.ListForPpAsync(0, false, 100, TestContext.Current.CancellationToken);

		Assert.Equal([missing.Id, done.Id], listed.Select(score => score.Id));
	}

	[Fact]
	public async Task List_AfterIdWithLimit_ContinuesInIdOrder()
	{
		var first = await StoreAsync("page_1");
		var second = await StoreAsync("page_2");
		var third = await StoreAsync("page_3");

		var firstPage = await Scores.ListForPpAsync(0, true, 2, TestContext.Current.CancellationToken);
		var secondPage = await Scores.ListForPpAsync(firstPage[^1].Id, true, 2, TestContext.Current.CancellationToken);

		Assert.Equal([first.Id, second.Id], firstPage.Select(score => score.Id));
		Assert.Equal(third.Id, Assert.Single(secondPage).Id);
	}
}
