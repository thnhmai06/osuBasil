using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Infrastructure.Scores.Handlers;
using Basil.Testing.Persistence;
using NSubstitute;

namespace Basil.Infrastructure.Scores.Tests.Handlers;

/// <summary>Contract tests of the score search: which scores a search names, and which searches are refused.</summary>
[Collection("Persistence")]
public sealed class SearchScoresHandlerTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset When = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly PageRequest Page = new(0, 50);

	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();

	[Fact]
	public async Task UnknownUser_NoUser()
	{
		var result = await SearchAsync(new SearchScores(UserReference.ByName("nobody_here"), null, null, Page));

		Assert.Equal(ScoreListOutcome.NoUser, result.Outcome);
	}

	[Fact]
	public async Task UnknownBeatmapId_NoBeatmap()
	{
		_beatmaps.GetAsync(999999, Arg.Any<CancellationToken>()).Returns((BeatmapRecord?)null);

		var result = await SearchAsync(new SearchScores(null, 999999, null, Page));

		Assert.Equal(ScoreListOutcome.NoBeatmap, result.Outcome);
	}

	[Fact]
	public async Task UserFilter_OnlyThatUsersScores()
	{
		var hash = StoredScores.Hash("shared_map");
		var alice = await Get<IUserRepository>().CreateAsync(new UserData { Name = "search_alice" });
		var bob = await Get<IUserRepository>().CreateAsync(new UserData { Name = "search_bob" });
		var aliceScore = await Get<IScoreRepository>().CreateAsync(StoredScores.Data(alice, hash, 1000, When));
		await Get<IScoreRepository>().CreateAsync(StoredScores.Data(bob, hash, 2000, When));

		var result = await SearchAsync(new SearchScores(UserReference.ById(alice.Id), null, null, Page));

		Assert.Equal(aliceScore.Id, Assert.Single(result.Scores!.Items).Id);
	}

	[Fact]
	public async Task BeatmapIdAndQuery_OnlyScoresOnBothMatches()
	{
		var mapA = Map(701, "intersect_a");
		var mapB = Map(702, "intersect_b");
		var player = await Get<IUserRepository>().CreateAsync(new UserData { Name = "srch_both" });
		var onA = await Get<IScoreRepository>().CreateAsync(StoredScores.Data(player, mapA.Hash, 1000, When));
		await Get<IScoreRepository>().CreateAsync(StoredScores.Data(player, mapB.Hash, 2000, When));
		var query = new BeatmapQuery(Text: "intersect");
		_beatmaps.GetAsync(701, Arg.Any<CancellationToken>()).Returns(mapA);
		_beatmaps.ListAsync(query, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
			.Returns(new Page<BeatmapRecord>([mapA, mapB], false));

		var result = await SearchAsync(new SearchScores(UserReference.ById(player.Id), 701, query, Page));

		Assert.Equal(onA.Id, Assert.Single(result.Scores!.Items).Id);
	}

	private static BeatmapRecord Map(int id, string name)
	{
		var template = StoredBeatmaps.Beatmap();
		return new BeatmapRecord
		{
			Id = id,
			Beatmapset = template.Beatmapset,
			Hash = StoredScores.Hash(name),
			Metadata = template.Metadata,
			Assets = template.Assets,
			UpdatedAt = template.UpdatedAt,
			CreatedAt = template.CreatedAt
		};
	}

	private async Task<ScoreListing> SearchAsync(SearchScores request)
	{
		var handler = new SearchScoresHandler(Get<IScoreRepository>(), _beatmaps, Get<IUserRepository>());
		return await handler.Handle(request, TestContext.Current.CancellationToken);
	}
}
