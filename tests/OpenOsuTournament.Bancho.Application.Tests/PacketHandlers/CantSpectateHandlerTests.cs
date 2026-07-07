using OpenOsuTournament.Bancho.Application.PacketHandlers.Spectating;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's CantSpectate.</summary>
public class CantSpectateHandlerTests
{
    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0);
    }

    [Fact]
    public async Task Handle_NotSpectating_NoOp()
    {
        var player = MakePlayer(1, "alice");

        await new CantSpectateHandler().HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Empty(player.Dequeue());
    }

    [Fact]
    public async Task Handle_Spectating_NotifiesHostAndFellowSpectators()
    {
        var host = MakePlayer(1, "host");
        var player = MakePlayer(2, "alice");
        var fellow = MakePlayer(3, "bob");
        host.AddSpectator(player);
        host.AddSpectator(fellow);
        player.Spectating = host;

        await new CantSpectateHandler().HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        var expected = ServerPacketWriter.SpectatorCantSpectate(player.Id);
        Assert.Equal(expected, host.Dequeue());
        Assert.Equal(expected, fellow.Dequeue());
    }

    [Fact]
    public async Task Handle_Stealth_NoOp()
    {
        var host = MakePlayer(1, "host");
        var player = MakePlayer(2, "alice");
        host.AddSpectator(player);
        player.Spectating = host;
        player.Stealth = true;

        await new CantSpectateHandler().HandleAsync(player, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Empty(host.Dequeue());
    }
}