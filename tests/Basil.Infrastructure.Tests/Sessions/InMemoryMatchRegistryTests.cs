using Basil.Application.Abstractions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Infrastructure.Sessions;
using Basil.Protocol.Multiplayer;

namespace Basil.Infrastructure.Tests.Sessions;

public class InMemoryMatchRegistryTests
{
	private static MatchState MakeMatchState()
	{
		return new MatchState(
			0, false, 0, 0, "test", "",
			"", 0, new string('a', 32),
			[], [], [], 1, (int)GameMode.Standard, (int)MatchWinCondition.Score,
			(int)MatchTeamType.HeadToHead, false, [], 0);
	}

	private static InMemoryMatchRegistry MakeRegistry()
	{
		return new InMemoryMatchRegistry(new InMemoryChannelRegistry(), new CountingMatchRepository());
	}

	[Fact]
	public async Task CreateAsync_AssignsTheFirstFreeId()
	{
		var registry = MakeRegistry();

		var match = await registry.CreateAsync(MakeMatchState(), 1);

		Assert.Equal(0, match.Id);
	}

	[Fact]
	public async Task CreateAsync_SkipsIdsAlreadyTaken()
	{
		var registry = MakeRegistry();
		await registry.CreateAsync(MakeMatchState(), 1);

		var second = await registry.CreateAsync(MakeMatchState(), 1);

		Assert.Equal(1, second.Id);
	}

	[Fact]
	public async Task CreateAsync_MoreThanSixtyFourMatches_AllSucceedWithDistinctIds()
	{
		var registry = MakeRegistry();

		var ids = new List<int>();
		for (var i = 0; i < 100; i++) ids.Add((await registry.CreateAsync(MakeMatchState(), 1)).Id);

		Assert.Equal(100, ids.Distinct().Count());
		Assert.Equal(100, registry.All.Count);
	}

	[Fact]
	public async Task CreateAsync_ConcurrentCalls_NeverAssignDuplicateIds()
	{
		var registry = MakeRegistry();

		var matches = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => registry.CreateAsync(MakeMatchState(), 1)));

		Assert.Equal(50, matches.Select(m => m.Id).Distinct().Count());
	}

	[Fact]
	public async Task GetById_ReturnsRegisteredMatch()
	{
		var registry = MakeRegistry();
		var created = await registry.CreateAsync(MakeMatchState(), 1);

		Assert.Same(created, registry.GetById(created.Id));
	}

	[Fact]
	public void GetById_UnknownId_ReturnsNull()
	{
		var registry = MakeRegistry();

		Assert.Null(registry.GetById(-1));
		Assert.Null(registry.GetById(64));
	}

	[Fact]
	public async Task GetByDbId_ReturnsMatchWithMatchingPersistentId()
	{
		var registry = MakeRegistry();
		var created = await registry.CreateAsync(MakeMatchState(), 1);

		Assert.Same(created, registry.GetByDbId(created.DbId));
	}

	[Fact]
	public async Task Remove_FreesTheIdForReuse()
	{
		var registry = MakeRegistry();
		var created = await registry.CreateAsync(MakeMatchState(), 1);

		registry.Remove(created.Id);

		Assert.Null(registry.GetById(created.Id));
		var reused = await registry.CreateAsync(MakeMatchState(), 1);
		Assert.Equal(created.Id, reused.Id);
	}

	[Fact]
	public async Task All_ReturnsOnlyRegisteredMatches()
	{
		var registry = MakeRegistry();
		await registry.CreateAsync(MakeMatchState(), 1);
		await registry.CreateAsync(MakeMatchState(), 1);

		Assert.Equal(2, registry.All.Count);
	}

	private sealed class CountingMatchRepository : IMatchRepository
	{
		private int _nextId = 1;

		public Task<int> CreateMatchAsync(string name, DateTime createdAt,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_nextId++);
		}

		public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5, GameMode mode,
			MatchWinCondition winCondition, MatchTeamType teamType, Mods mods, DateTime startedAt,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<Match?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Round>> FetchRoundsAsync(int matchId, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Match>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task CreateEventAsync(MatchEvent row, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<MatchEvent>> FetchEventsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Match>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Round>> FetchUnrecoveredRoundsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
