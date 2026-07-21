using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's ChangeAction (@register(ClientPackets.CHANGE_ACTION, restricted=True)).</summary>
public class ChangeActionHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private static byte[] Payload(int action, string infoText, string mapMd5, uint mods, byte mode, int mapId)
    {
        return
        [
            (byte)action,
            .. PacketWriter.WriteString(infoText),
            .. PacketWriter.WriteString(mapMd5),
            .. PacketWriter.WriteUInt32(mods),
            mode,
            .. PacketWriter.WriteInt32(mapId)
        ];
    }

    [Fact]
    public async Task Handle_UpdatesStatusFields()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var reader =
            new BanchoPacketReader(Payload((int)UserActivity.Playing, "playing a map", "abc123", (uint)Mods.Hidden, 0, 42));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(UserActivity.Playing, session.Status.UserActivity);
        Assert.Equal("playing a map", session.Status.InfoText);
        Assert.Equal("abc123", session.Status.MapMd5);
        Assert.Equal(Mods.Hidden, session.Status.Mods);
        Assert.Equal(GameMode.Standard, session.Status.Mode);
        Assert.Equal(42, session.Status.MapId);
    }

    [Fact]
    public async Task Handle_RelaxMod_PassesThroughModeAndModsUnchanged()
    {
        // GameMode is a plain 4-value enum — Relax/Autopilot are ordinary Mods bits now, with no
        // effect on Mode, unlike the old Vanilla/Relax/Autopilot-variant enum.
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var reader = new BanchoPacketReader(Payload(0, "", "", (uint)Mods.Relax, 0, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(GameMode.Standard, session.Status.Mode);
        Assert.Equal(Mods.Relax, session.Status.Mods);
    }

    [Fact]
    public async Task Handle_AutopilotModOnMania_PassesThroughModeAndModsUnchanged()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var reader = new BanchoPacketReader(Payload(0, "", "", (uint)Mods.Autopilot, 3, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(GameMode.Mania, session.Status.Mode);
        Assert.Equal(Mods.Autopilot, session.Status.Mods);
    }

    [Fact]
    public async Task Handle_Unrestricted_BroadcastsUpdatedStatsToAllOnlinePlayers()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var other = new PlayerSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.All.Returns([session, other]);
        var reader = new BanchoPacketReader(Payload((int)UserActivity.Idle, "", "", 0, 0, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.NotEmpty(other.Dequeue());
    }

    [Fact]
    public async Task Handle_Restricted_DoesNotBroadcast()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch); // restricted
        var other = new PlayerSession(2, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.All.Returns([session, other]);
        var reader = new BanchoPacketReader(Payload(0, "", "", 0, 0, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Empty(other.Dequeue());
    }
}