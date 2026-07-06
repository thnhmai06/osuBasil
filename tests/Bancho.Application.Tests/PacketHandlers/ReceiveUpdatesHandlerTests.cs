using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's ReceiveUpdates.</summary>
public class ReceiveUpdatesHandlerTests
{
    private static PlayerSession MakeSession() => new(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

    [Theory]
    [InlineData(0, PresenceFilter.Nil)]
    [InlineData(1, PresenceFilter.All)]
    [InlineData(2, PresenceFilter.Friends)]
    public void Handle_ValidValue_UpdatesPresenceFilter(int value, PresenceFilter expected)
    {
        var session = MakeSession();
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(value));

        new ReceiveUpdatesHandler().Handle(session, reader);

        Assert.Equal(expected, session.PresenceFilter);
    }

    [Fact]
    public void Handle_OutOfRangeValue_IgnoredKeepingPreviousFilter()
    {
        var session = MakeSession();
        session.PresenceFilter = PresenceFilter.Friends;
        var reader = new BanchoPacketReader(PacketWriter.WriteInt32(99));

        new ReceiveUpdatesHandler().Handle(session, reader);

        Assert.Equal(PresenceFilter.Friends, session.PresenceFilter);
    }
}
