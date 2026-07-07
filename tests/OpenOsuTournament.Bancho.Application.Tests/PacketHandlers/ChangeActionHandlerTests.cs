using NSubstitute;
using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;
using Action = OpenOsuTournament.Bancho.Domain.Action;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

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
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader =
            new BanchoPacketReader(Payload((int)Action.Playing, "playing a map", "abc123", (uint)Mods.Hidden, 0, 42));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(Action.Playing, session.Status.Action);
        Assert.Equal("playing a map", session.Status.InfoText);
        Assert.Equal("abc123", session.Status.MapMd5);
        Assert.Equal(Mods.Hidden, session.Status.Mods);
        Assert.Equal(GameMode.VanillaOsu, session.Status.Mode);
        Assert.Equal(42, session.Status.MapId);
    }

    [Fact]
    public async Task Handle_RelaxMod_ShiftsModeToRelaxVariant()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader = new BanchoPacketReader(Payload(0, "", "", (uint)Mods.Relax, 0, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(GameMode.RelaxOsu, session.Status.Mode);
        Assert.Equal(Mods.Relax, session.Status.Mods);
    }

    [Fact]
    public async Task Handle_RelaxModOnMania_StripsRelaxSinceRxManiaDoesNotExist()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader = new BanchoPacketReader(Payload(0, "", "", (uint)Mods.Relax, 3, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(GameMode.VanillaMania, session.Status.Mode);
        Assert.Equal(Mods.NoMod, session.Status.Mods);
    }

    [Fact]
    public async Task Handle_AutopilotMod_ShiftsModeToAutopilotVariant()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader = new BanchoPacketReader(Payload(0, "", "", (uint)Mods.Autopilot, 0, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(GameMode.AutopilotOsu, session.Status.Mode);
    }

    [Fact]
    public async Task Handle_AutopilotModOnNonOsuMode_StripsAutopilot()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader = new BanchoPacketReader(Payload(0, "", "", (uint)Mods.Autopilot, 1, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(GameMode.VanillaTaiko, session.Status.Mode);
        Assert.Equal(Mods.NoMod, session.Status.Mods);
    }

    [Fact]
    public async Task Handle_AutopilotModOnMania_DivergesFromGameModeExtensionsFromParams()
    {
        // Regression test for the deliberate Relax-before-Autopilot check order documented on
        // BeatmapLeaderboardService.ResolveModeAndMods: this handler and that service both check
        // Relax first, unlike GameModeExtensions.FromParams (Autopilot first, matching Python's
        // GameMode.from_params). For mods=Autopilot, mode=mania, FromParams would produce
        // AutopilotMania (11, an "unused" combo); this handler strips Autopilot instead, since mania
        // has no ap! variant, and keeps VanillaMania — the two must keep disagreeing here.
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader = new BanchoPacketReader(Payload(0, "", "", (uint)Mods.Autopilot, 3, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Equal(GameMode.VanillaMania, session.Status.Mode);
        Assert.Equal(Mods.NoMod, session.Status.Mods);
        Assert.NotEqual(GameModeExtensions.FromParams(3, Mods.Autopilot), session.Status.Mode);
    }

    [Fact]
    public async Task Handle_Unrestricted_BroadcastsUpdatedStatsToAllOnlinePlayers()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var other = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([session, other]);
        var reader = new BanchoPacketReader(Payload((int)Action.Idle, "", "", 0, 0, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.NotEmpty(other.Dequeue());
    }

    [Fact]
    public async Task Handle_Restricted_DoesNotBroadcast()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Verified, 0.0); // restricted
        var other = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([session, other]);
        var reader = new BanchoPacketReader(Payload(0, "", "", 0, 0, 0));

        await new ChangeActionHandler(_sessionRegistry).HandleAsync(session, reader);

        Assert.Empty(other.Dequeue());
    }
}