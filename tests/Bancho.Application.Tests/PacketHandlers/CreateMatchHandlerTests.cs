using Bancho.Application.PacketHandlers;
using Bancho.Protocol;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchCreate.</summary>
public class CreateMatchHandlerTests
{
    private static BanchoPacketReader ReaderFor(int hostId, string name = "test match") =>
        MatchRequestReader(0, name, "", "Some Map", 100, new string('a', 32), hostId);

    [Fact]
    public async Task Handle_HostIdMismatch_NoOp()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var handler = new CreateMatchHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(hostId: 999));

        Assert.Null(host.Match);
    }

    [Fact]
    public async Task Handle_Restricted_SendsMatchJoinFailAndNotification()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        host.Priv = (Domain.Privileges)0;
        fixture.RegisterAll(host);
        var handler = new CreateMatchHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(hostId: 1));

        Assert.Null(host.Match);
        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(host.Dequeue()));
    }

    [Fact]
    public async Task Handle_Valid_CreatesMatchAndJoinsHost()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var handler = new CreateMatchHandler(fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(hostId: 1, name: "my room"));

        Assert.NotNull(host.Match);
        Assert.Equal("my room", host.Match!.Name);
        Assert.Equal(0, host.Match.GetSlotId(host.Id));
    }

    [Fact]
    public async Task Handle_RegistryFull_SendsMatchJoinFail()
    {
        var fixture = new Fixture();
        var handler = new CreateMatchHandler(fixture.MatchMembership);
        for (var i = 0; i < 64; i++)
        {
            var filler = MakePlayer(i + 1, $"p{i}");
            fixture.RegisterAll(filler);
            await handler.HandleAsync(filler, ReaderFor(hostId: filler.Id));
        }

        var overflow = MakePlayer(1000, "overflow");
        fixture.RegisterAll(overflow);

        await handler.HandleAsync(overflow, ReaderFor(hostId: overflow.Id));

        Assert.Null(overflow.Match);
        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(overflow.Dequeue()));
    }
}
