using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Protocol.Packets;
using static Basil.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchJoin.</summary>
public class JoinMatchHandlerTests
{
    private static BanchoPacketReader ReaderFor(int matchId, string password)
    {
        byte[] body = [.. PacketWriter.WriteInt32(matchId), .. PacketWriter.WriteString(password)];
        return new BanchoPacketReader(body);
    }

    [Fact]
    public async Task Handle_UnknownMatch_SendsMatchJoinFail()
    {
        var fixture = new Fixture();
        var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);
        var player = MakePlayer(1, "alice");
        fixture.RegisterAll(player);

        await handler.HandleAsync(player, ReaderFor(0, ""));

        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(player.Dequeue()));
    }

    [Fact]
    public async Task Handle_Restricted_SendsMatchJoinFailWithoutTouchingMatch()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        host.Dequeue();

        var guest = MakePlayer(2, "guest");
        guest.Priv = 0;
        fixture.RegisterAll(host, guest);
        var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

        await handler.HandleAsync(guest, ReaderFor(match.Id, ""));

        Assert.Null(guest.Match);
        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(guest.Dequeue()));
    }

    [Fact]
    public async Task Handle_CorrectPassword_JoinsMatch()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id, password: "pw"))
            .GetAwaiter().GetResult()!;

        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

        await handler.HandleAsync(guest, ReaderFor(match.Id, "pw"));

        Assert.Same(match, guest.Match);
    }

    [Fact]
    public async Task Handle_PrivateMatch_CorrectPassword_UninvitedRejected()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id, password: "pw"))
            .GetAwaiter().GetResult()!;
        match.IsPrivate = true;

        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

        await handler.HandleAsync(guest, ReaderFor(match.Id, "pw"));

        Assert.Null(guest.Match);
        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(guest.Dequeue()));
    }

    [Fact]
    public async Task Handle_PrivateMatch_InvitedGuest_Succeeds()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id, password: "pw"))
            .GetAwaiter().GetResult()!;
        match.IsPrivate = true;

        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        match.AddInvite(guest.Id);
        var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

        await handler.HandleAsync(guest, ReaderFor(match.Id, "pw"));

        Assert.Same(match, guest.Match);
    }

    [Fact]
    public async Task Handle_PrivateMatch_HostRejoinsWithoutInvite_Rejected()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id), createdViaMakeCommand: true)
            .GetAwaiter().GetResult()!;
        match.IsPrivate = true;
        await fixture.MatchMembership.Leave(host, match);
        var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(match.Id, ""));

        Assert.Null(host.Match);
        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(host.Dequeue()));
    }

    [Fact]
    public async Task Handle_PrivateMatch_HostRejoinsWithInvite_Succeeds()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.MatchMembership.CreateAsync(host, MakeMatchData(host.Id), createdViaMakeCommand: true)
            .GetAwaiter().GetResult()!;
        match.IsPrivate = true;
        await fixture.MatchMembership.Leave(host, match);
        match.AddInvite(host.Id);
        var handler = new JoinMatchHandler(fixture.MatchRegistry, fixture.MatchMembership);

        await handler.HandleAsync(host, ReaderFor(match.Id, ""));

        Assert.Same(match, host.Match);
    }
}