using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Application.Services.Bot;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using NSubstitute;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

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

        await handler.HandleAsync(guest,
            MatchRequestReader(0, "renamed", "", "Some Map", 100, new string('a', 32), guest.Id, teamType: 0));

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

        await handler.HandleAsync(host,
            MatchRequestReader(0, "renamed", "", "Some Map", 100, new string('a', 32), host.Id));

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
        var mapset = new Mapset(1, "A", "T", "C", DateTime.UtcNow, DateTime.UtcNow);
        var bmap = new Beatmap(
            newMd5, 500, mapset, "V", "map.osu", TimeSpan.FromSeconds(60), 100, false, 0, 0,
            new Difficulty(GameMode.Standard, 120, 4, 9, 8, 5, 5.0));
        _mapRepository.FetchOneAsync(md5: newMd5).Returns(bmap);
        var handler = new MatchChangeSettingsHandler(_mapRepository, fixture.SessionRegistry, fixture.MatchMembership);

        await handler.HandleAsync(host, MatchRequestReader(0, match.Name, "", "Client Map Name", 999, newMd5, host.Id));

        Assert.Equal(500, match.MapId);
        Assert.Equal(newMd5, match.MapMd5);
        Assert.Equal(bmap.FullName, match.MapName);
    }

    [Fact]
    public async Task Handle_MapChosenButUnknownServerSide_ReportsErrorAndLeavesMapUnchanged()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
        fixture.RegisterAll(host, bot);
        var match = fixture.CreateMatch(host);
        match.MapId = -1;
        var previousMd5 = match.MapMd5;
        var previousName = match.MapName;
        var newMd5 = new string('c', 32);
        _mapRepository.FetchOneAsync(md5: newMd5).Returns((Beatmap?)null);
        var handler = new MatchChangeSettingsHandler(_mapRepository, fixture.SessionRegistry, fixture.MatchMembership);
        host.Dequeue();

        await handler.HandleAsync(host, MatchRequestReader(0, match.Name, "", "Unknown Map", 777, newMd5, host.Id));

        Assert.Equal(-1, match.MapId);
        Assert.Equal(previousMd5, match.MapMd5);
        Assert.Equal(previousName, match.MapName);
        Assert.Contains(
            ServerPacketWriter.SendMessage(bot.Name, "Beatmap not found locally — map selection ignored.",
                match.ChatChannelName, bot.Id),
            Chunk(host.Dequeue()));
    }
}