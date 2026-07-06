using Bancho.Application.Abstractions;
using Bancho.Application.PacketHandlers;
using Bancho.Domain;
using NSubstitute;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchChangeSettings.</summary>
public class MatchChangeSettingsHandlerTests
{
    private readonly IMapRepository _mapRepository = Substitute.For<IMapRepository>();

    [Fact]
    public async Task Handle_NonHost_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        var handler = new MatchChangeSettingsHandler(_mapRepository, fixture.SessionRegistry, fixture.MatchMembership);

        await handler.HandleAsync(guest, MatchRequestReader(0, "renamed", "", "Some Map", 100, new string('a', 32), guest.Id, teamType: 0));

        Assert.Equal("test match", match.Name);
    }

    [Fact]
    public async Task Handle_RenamesMatch()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var handler = new MatchChangeSettingsHandler(_mapRepository, fixture.SessionRegistry, fixture.MatchMembership);

        await handler.HandleAsync(host, MatchRequestReader(0, "renamed", "", "Some Map", 100, new string('a', 32), host.Id));

        Assert.Equal("renamed", match.Name);
    }

    [Fact]
    public async Task Handle_MapIdMinusOne_ClearsMapAndUnreadiesPlayers()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.Slots[0].Status = SlotStatus.Ready;
        var handler = new MatchChangeSettingsHandler(_mapRepository, fixture.SessionRegistry, fixture.MatchMembership);

        await handler.HandleAsync(host, MatchRequestReader(0, match.Name, "", "", -1, new string('0', 32), host.Id));

        Assert.Equal(-1, match.MapId);
        Assert.Equal("", match.MapMd5);
        Assert.Equal(SlotStatus.NotReady, match.Slots[0].Status);
    }

    [Fact]
    public async Task Handle_MapChosenAndKnownServerSide_UsesServersideMapInfo()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.MapId = -1;
        var newMd5 = new string('b', 32);
        var bmap = new Beatmap(
            Md5: newMd5, Id: 500, SetId: 1, Artist: "A", Title: "T", Version: "V", Creator: "C",
            LastUpdate: DateTime.UtcNow, TotalLength: 60, MaxCombo: 100, Status: RankedStatus.Ranked,
            Frozen: false, Plays: 0, Passes: 0, Mode: GameMode.VanillaOsu, Bpm: 120, Cs: 4, Od: 8, Ar: 9,
            Hp: 5, Diff: 5.0, Filename: "map.osu");
        _mapRepository.FetchOneAsync(md5: newMd5).Returns(bmap);
        var handler = new MatchChangeSettingsHandler(_mapRepository, fixture.SessionRegistry, fixture.MatchMembership);

        await handler.HandleAsync(host, MatchRequestReader(0, match.Name, "", "Client Map Name", 999, newMd5, host.Id));

        Assert.Equal(500, match.MapId);
        Assert.Equal(newMd5, match.MapMd5);
        Assert.Equal(bmap.FullName, match.MapName);
    }

    [Fact]
    public async Task Handle_MapChosenButUnknownServerSide_FallsBackToClientSuppliedInfo()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        match.MapId = -1;
        var newMd5 = new string('c', 32);
        _mapRepository.FetchOneAsync(md5: newMd5).Returns((Beatmap?)null);
        var handler = new MatchChangeSettingsHandler(_mapRepository, fixture.SessionRegistry, fixture.MatchMembership);

        await handler.HandleAsync(host, MatchRequestReader(0, match.Name, "", "Unknown Map", 777, newMd5, host.Id));

        Assert.Equal(777, match.MapId);
        Assert.Equal(newMd5, match.MapMd5);
        Assert.Equal("Unknown Map", match.MapName);
    }
}
