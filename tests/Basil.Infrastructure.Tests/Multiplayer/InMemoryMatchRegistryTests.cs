using Basil.Application.Beatmaps;
using Basil.Application.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Channels;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Channels;
using NSubstitute;

namespace Basil.Infrastructure.Tests.Multiplayer;

public class InMemoryMatchRegistryTests
{
	private static readonly User Host = new() { Id = 1, Name = "host" };

	private static MatchCreationData MakeMatchState()
	{
		return new MatchCreationData(
			"test", "", "", null, new string('a', 32), 1,
			GameMode.Standard, Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead,
			false, 0);
	}

	private static InMemoryMatchRegistry MakeRegistry()
	{
		return new InMemoryMatchRegistry(new InMemoryChannelRegistry(), new CountingMatchRepository(),
			MakeBeatmapRepository());
	}

	/// <summary>Resolves any requested id to a beatmap carrying that same id, so a created match's MapId round-trips.</summary>
	private static IBeatmapRepository MakeBeatmapRepository()
	{
		var repository = Substitute.For<IBeatmapRepository>();
		repository
			.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				if (call.ArgAt<int?>(0) is not { } beatmapId) return Task.FromResult<Beatmap?>(null);

				var beatmapset = new Beatmapset(1, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
				return Task.FromResult<Beatmap?>(new Beatmap(new string('a', 32), beatmapId, beatmapset, "Normal",
					"map.osu", new Difficulty(GameMode.Standard, 180, TimeSpan.FromMinutes(2), 4, 8, 8, 5, 5.0),
					new OsuObjects { MaxCombo = 500 }));
			});
		return repository;
	}

	[Fact]
	public async Task CreateAsync_AssignsTheFirstFreeId()
	{
		var registry = MakeRegistry();

		var match = await registry.CreateAsync(MakeMatchState(), Host);

		Assert.Equal(0, match.Id);
	}

	[Fact]
	public async Task CreateAsync_NullMapId_MatchHasNoMapId()
	{
		var registry = MakeRegistry();

		var match = await registry.CreateAsync(MakeMatchState() with { MapId = null }, Host);

		Assert.Null(match.MapId);
	}

	[Fact]
	public async Task CreateAsync_MapIdGiven_MatchHasTheSameMapId()
	{
		var registry = MakeRegistry();

		var match = await registry.CreateAsync(MakeMatchState() with { MapId = 654 }, Host);

		Assert.Equal(654, match.MapId);
	}

	[Fact]
	public async Task CreateAsync_SkipsIdsAlreadyTaken()
	{
		var registry = MakeRegistry();
		await registry.CreateAsync(MakeMatchState(), Host);

		var second = await registry.CreateAsync(MakeMatchState(), Host);

		Assert.Equal(1, second.Id);
	}

	[Fact]
	public async Task CreateAsync_MoreThanSixtyFourMatches_AllSucceedWithDistinctIds()
	{
		var registry = MakeRegistry();

		var ids = new List<int>();
		for (var i = 0; i < 100; i++) ids.Add((await registry.CreateAsync(MakeMatchState(), Host)).Id);

		Assert.Equal(100, ids.Distinct().Count());
		Assert.Equal(100, registry.All.Count);
	}

	[Fact]
	public async Task CreateAsync_ConcurrentCalls_NeverAssignDuplicateIds()
	{
		var registry = MakeRegistry();

		var matches =
			await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => registry.CreateAsync(MakeMatchState(), Host)));

		Assert.Equal(50, matches.Select(m => m.Id).Distinct().Count());
	}

	[Fact]
	public async Task GetById_ReturnsRegisteredMatch()
	{
		var registry = MakeRegistry();
		var created = await registry.CreateAsync(MakeMatchState(), Host);

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
		var created = await registry.CreateAsync(MakeMatchState(), Host);

		Assert.Same(created, registry.GetByDbId(created.DbId));
	}

	[Fact]
	public async Task Remove_FreesTheIdForReuse()
	{
		var registry = MakeRegistry();
		var created = await registry.CreateAsync(MakeMatchState(), Host);

		registry.Remove(created.Id);

		Assert.Null(registry.GetById(created.Id));
		var reused = await registry.CreateAsync(MakeMatchState(), Host);
		Assert.Equal(created.Id, reused.Id);
	}

	[Fact]
	public async Task All_ReturnsOnlyRegisteredMatches()
	{
		var registry = MakeRegistry();
		await registry.CreateAsync(MakeMatchState(), Host);
		await registry.CreateAsync(MakeMatchState(), Host);

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