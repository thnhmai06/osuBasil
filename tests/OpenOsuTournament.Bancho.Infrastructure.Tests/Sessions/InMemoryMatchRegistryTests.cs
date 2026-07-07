using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Infrastructure.Sessions;

namespace OpenOsuTournament.Bancho.Infrastructure.Tests.Sessions;

/// <summary>Ported from app/objects/collections.py's Matches — a fixed 64-slot table, not an unbounded list.</summary>
public class InMemoryMatchRegistryTests
{
    private static MatchSession MakeMatch(int id)
    {
        return new MatchSession(
            id, "test", "", true,
            "", 0, new string('a', 32), 1,
            GameMode.VanillaOsu, Mods.NoMod, MatchWinConditions.Score,
            MatchTeamTypes.HeadToHead, false, 0, $"#multi_{id}");
    }

    [Fact]
    public void TryCreate_AssignsTheFirstFreeId()
    {
        var registry = new InMemoryMatchRegistry();

        var match = registry.TryCreate(id => MakeMatch(id));

        Assert.NotNull(match);
        Assert.Equal(0, match!.Id);
    }

    [Fact]
    public void TryCreate_SkipsIdsAlreadyTaken()
    {
        var registry = new InMemoryMatchRegistry();
        registry.TryCreate(id => MakeMatch(id));

        var second = registry.TryCreate(id => MakeMatch(id));

        Assert.Equal(1, second!.Id);
    }

    [Fact]
    public void TryCreate_ReturnsNullWhenAllSixtyFourSlotsAreTaken()
    {
        var registry = new InMemoryMatchRegistry();
        for (var i = 0; i < 64; i++) Assert.NotNull(registry.TryCreate(id => MakeMatch(id)));

        var overflow = registry.TryCreate(id => MakeMatch(id));

        Assert.Null(overflow);
    }

    [Fact]
    public void GetById_ReturnsRegisteredMatch()
    {
        var registry = new InMemoryMatchRegistry();
        var created = registry.TryCreate(id => MakeMatch(id));

        Assert.Same(created, registry.GetById(created!.Id));
    }

    [Fact]
    public void GetById_OutOfRange_ReturnsNull()
    {
        var registry = new InMemoryMatchRegistry();

        Assert.Null(registry.GetById(-1));
        Assert.Null(registry.GetById(64));
    }

    [Fact]
    public void Remove_FreesTheSlotForReuse()
    {
        var registry = new InMemoryMatchRegistry();
        var created = registry.TryCreate(id => MakeMatch(id));

        registry.Remove(created!.Id);

        Assert.Null(registry.GetById(created.Id));
        var reused = registry.TryCreate(id => MakeMatch(id));
        Assert.Equal(created.Id, reused!.Id);
    }

    [Fact]
    public void All_ReturnsOnlyOccupiedSlots()
    {
        var registry = new InMemoryMatchRegistry();
        registry.TryCreate(id => MakeMatch(id));
        registry.TryCreate(id => MakeMatch(id));

        Assert.Equal(2, registry.All.Count);
    }
}