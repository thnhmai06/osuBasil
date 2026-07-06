using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Spectating;
using Bancho.Domain;
using NSubstitute;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's reconnect.</summary>
public class ReconnectCommandTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();

    private ReconnectCommand MakeCommand() => new(_sessionRegistry, new PlayerLogoutService(
        _sessionRegistry, _channelRegistry, new SpectatorService(Substitute.For<IChannelRegistry>(), new ChannelMembershipService(Substitute.For<IPlayerSessionRegistry>()))));

    private static CommandContext MakeContext(PlayerSession player, params string[] args) =>
        new(player, args, null, null);

    [Fact]
    public async Task HandleAsync_NoArgs_LogsOutSelf()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await MakeCommand().HandleAsync(MakeContext(player));

        _sessionRegistry.Received(1).Remove(player);
        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_TargetArgWithoutAdminPriv_ReturnsNullAndDoesNothing()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "someoneelse"));

        _sessionRegistry.DidNotReceive().Remove(Arg.Any<PlayerSession>());
        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_TargetArgWithAdminPriv_LogsOutTarget()
    {
        var admin = new PlayerSession(1, "admin", "token", Privileges.Unrestricted | Privileges.Administrator, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);

        var response = await MakeCommand().HandleAsync(MakeContext(admin, "other"));

        _sessionRegistry.Received(1).Remove(target);
        Assert.Null(response);
    }

    [Fact]
    public async Task HandleAsync_TargetArgAdminPriv_UnknownPlayer_ReturnsNotFound()
    {
        var admin = new PlayerSession(1, "admin", "token", Privileges.Unrestricted | Privileges.Administrator, 0.0);
        _sessionRegistry.GetByName("nobody").Returns((PlayerSession?)null);

        var response = await MakeCommand().HandleAsync(MakeContext(admin, "nobody"));

        Assert.Equal("Player not found", response);
    }
}
