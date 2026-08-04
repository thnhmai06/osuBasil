using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

public class InMemoryMatchRegistryTests
{
	private static MatchSession MakeMatch(int id)
	{
		return new MatchSession(
			id, "test", "",
			"", 0, new string('a', 32), 1,
			GameMode.Standard, Mods.NoMod, MatchWinCondition.Score,
			MatchTeamType.HeadToHead, false, 0, $"#multi_{id}");
	}

	[Fact]
	public void TryCreate_AssignsTheFirstFreeId()
	{
		var registry = new InMemoryMatchRegistry();

		var match = registry.TryCreate(id => MakeMatch(id));

		Assert.Equal(0, match.Id);
	}

	[Fact]
	public void TryCreate_SkipsIdsAlreadyTaken()
	{
		var registry = new InMemoryMatchRegistry();
		registry.TryCreate(id => MakeMatch(id));

		var second = registry.TryCreate(id => MakeMatch(id));

		Assert.Equal(1, second.Id);
	}

	[Fact]
	public void TryCreate_MoreThanSixtyFourMatches_AllSucceedWithDistinctIds()
	{
		var registry = new InMemoryMatchRegistry();

		var ids = new List<int>();
		for (var i = 0; i < 100; i++) ids.Add(registry.TryCreate(id => MakeMatch(id)).Id);

		Assert.Equal(100, ids.Distinct().Count());
		Assert.Equal(100, registry.All.Count);
	}

	[Fact]
	public void GetById_ReturnsRegisteredMatch()
	{
		var registry = new InMemoryMatchRegistry();
		var created = registry.TryCreate(id => MakeMatch(id));

		Assert.Same(created, registry.GetById(created.Id));
	}

	[Fact]
	public void GetById_UnknownId_ReturnsNull()
	{
		var registry = new InMemoryMatchRegistry();

		Assert.Null(registry.GetById(-1));
		Assert.Null(registry.GetById(64));
	}

	[Fact]
	public void Remove_FreesTheIdForReuse()
	{
		var registry = new InMemoryMatchRegistry();
		var created = registry.TryCreate(id => MakeMatch(id));

		registry.Remove(created.Id);

		Assert.Null(registry.GetById(created.Id));
		var reused = registry.TryCreate(id => MakeMatch(id));
		Assert.Equal(created.Id, reused.Id);
	}

	[Fact]
	public void All_ReturnsOnlyRegisteredMatches()
	{
		var registry = new InMemoryMatchRegistry();
		registry.TryCreate(id => MakeMatch(id));
		registry.TryCreate(id => MakeMatch(id));

		Assert.Equal(2, registry.All.Count);
	}
}
