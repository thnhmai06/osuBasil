using Basil.Application.Services.Anticheat;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Tests.PacketHandlers;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.UseCases.Anticheat;

/// <summary>
///     Ported from app/services/client_integrity.py's ClientIntegrityService.handle_lastfm_flags.
///     Per explicit user decision, restrict/force-logout/random-ban-roll/Discord-webhook side effects
///     are dropped entirely (Basil has no restrict machinery); instead, a flagged player currently in
///     a match gets a BasilBot warning on that match's own chat channel plus a DM to every referee —
///     a flagged player outside any match produces no side effect at all.
/// </summary>
public class ClientIntegrityServiceTests
{
    private readonly MultiplayerTestSupport.Fixture _fixture = new();

    private ClientIntegrityService MakeService()
    {
        return new ClientIntegrityService(_fixture.SessionRegistry, _fixture.MatchMembership);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_NotAnticheatFlag_ReturnsStopSending()
    {
        var player = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var result = await MakeService().HandleLastFmFlagsAsync(player, "12345");

        Assert.Equal(ClientIntegrityResult.StopSending, result);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_EmptyFlag_ReturnsStopSending()
    {
        var player = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var result = await MakeService().HandleLastFmFlagsAsync(player, "");

        Assert.Equal(ClientIntegrityResult.StopSending, result);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_MalformedFlagSuffix_ReturnsStopSendingWithoutThrowing()
    {
        var player = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var result = await MakeService().HandleLastFmFlagsAsync(player, "anotanumber");

        Assert.Equal(ClientIntegrityResult.StopSending, result);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_NoSuspiciousFlag_ReturnsEmpty()
    {
        var player = MultiplayerTestSupport.MakePlayer(1, "cmyui");
        var flag = $"a{(int)LastFmFlags.ConsoleOpen}";

        var result = await MakeService().HandleLastFmFlagsAsync(player, flag);

        Assert.Equal(ClientIntegrityResult.Empty, result);
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_HqOsuFlag_PlayerNotInMatch_NoSideEffect()
    {
        var player = MultiplayerTestSupport.MakePlayer(1, "cmyui");
        _fixture.RegisterAll(player);
        var flag = $"a{(int)LastFmFlags.HqAssembly}";

        var result = await MakeService().HandleLastFmFlagsAsync(player, flag);

        Assert.Equal(ClientIntegrityResult.StopSending, result);
        Assert.Empty(player.Dequeue());
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_HqOsuFlag_PlayerInMatch_WarnsMatchChannelAndDmsReferees()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var referee = MultiplayerTestSupport.MakePlayer(2, "ref");
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
            UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch) { IsBot = true };
        _fixture.RegisterAll(host, referee, bot);
        var match = _fixture.CreateMatch(host, hostIsReferee: false);
        match.AddReferee(referee.Id);
        host.Dequeue(); // drop the MatchJoinSuccess/UpdateMatch packets queued by CreateMatch itself

        var flag = $"a{(int)LastFmFlags.HqAssembly}";
        var result = await MakeService().HandleLastFmFlagsAsync(host, flag);

        Assert.Equal(ClientIntegrityResult.StopSending, result);

        var expectedWarning = ServerPacketWriter.SendMessage("BasilBot",
            "Anti-cheat flag for host: hq!osu running (HqAssembly)", match.ChatChannelName, BotBootstrapService.BotId);
        Assert.Contains(expectedWarning, MultiplayerTestSupport.Chunk(host.Dequeue()));

        var expectedDm = ServerPacketWriter.SendMessage("BasilBot",
            $"Anti-cheat flag in match #{match.DbId} {match.Name}: host — hq!osu running (HqAssembly)",
            "ref", BotBootstrapService.BotId);
        Assert.Equal(expectedDm, referee.Dequeue());
    }

    [Fact]
    public async Task HandleLastFmFlagsAsync_RegistryEditsFlag_PlayerInMatch_WarnsMatchChannel()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var bot = new PlayerSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
            UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch) { IsBot = true };
        _fixture.RegisterAll(host, bot);
        var match = _fixture.CreateMatch(host);
        host.Dequeue();

        var flag = $"a{(int)LastFmFlags.RegistryEdits}";
        var result = await MakeService().HandleLastFmFlagsAsync(host, flag);

        Assert.Equal(ClientIntegrityResult.StopSending, result);

        var expectedWarning = ServerPacketWriter.SendMessage("BasilBot",
            "Anti-cheat flag for host: hq!osu relife multiaccounting tool registry edits detected",
            match.ChatChannelName, BotBootstrapService.BotId);
        Assert.Contains(expectedWarning, MultiplayerTestSupport.Chunk(host.Dequeue()));
    }
}
