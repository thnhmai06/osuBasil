using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's SetAwayMessage.</summary>
public class SetAwayMessageHandlerTests
{
    [Fact]
    public async Task Handle_SetsAwayMessageFromMessageText()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var payload =
            ServerPacketWriter.SendMessage("cmyui", "gone fishing", "", 1)
                [7..]; // strip packet header, keep message payload
        var reader = new BanchoPacketReader(payload);

        await new SetAwayMessageHandler().HandleAsync(session, reader);

        Assert.Equal("gone fishing", session.AwayMessage);
    }
}