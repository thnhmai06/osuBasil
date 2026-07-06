using Bancho.Application.Abstractions;
using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's changename.</summary>
public class ChangeNameCommandTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IOptions<RegistrationOptions> _registrationOptions = Options.Create(new RegistrationOptions
    {
        DisallowedNames = ["admin"],
    });

    private ChangeNameCommand MakeCommand() =>
        new(_users, new PlayerLogoutService(_sessionRegistry, _channelRegistry), _registrationOptions);

    private static CommandContext MakeContext(PlayerSession player, params string[] args) => new(player, args, null, null);

    [Fact]
    public async Task HandleAsync_TooShort_ReturnsLengthError()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Supporter, 0.0);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "a"));

        Assert.Equal("Must be 2-15 characters in length.", response);
    }

    [Fact]
    public async Task HandleAsync_ContainsBothUnderscoreAndSpace_ReturnsFormatError()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Supporter, 0.0);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "foo_bar", "baz"));

        Assert.Equal("May contain \"_\" and \" \", but not both.", response);
    }

    [Fact]
    public async Task HandleAsync_DisallowedName_ReturnsDisallowedError()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Supporter, 0.0);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "admin"));

        Assert.Equal("Disallowed username; pick another.", response);
    }

    [Fact]
    public async Task HandleAsync_NameAlreadyTaken_ReturnsTakenError()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Supporter, 0.0);
        _users.FetchByNameAsync("newname").Returns(new User(
            2, "newname", "newname", null, 1, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, null));

        var response = await MakeCommand().HandleAsync(MakeContext(player, "newname"));

        Assert.Equal("Username already taken by another player.", response);
    }

    [Fact]
    public async Task HandleAsync_ValidNewName_UpdatesNotifiesAndLogsOut()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Supporter, 0.0);
        _users.FetchByNameAsync("newname").Returns((User?)null);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "newname"));

        await _users.Received(1).UpdateNameAsync(1, "newname", "newname");
        Assert.Equal(ServerPacketWriter.Notification("Your username has been changed to newname!"), player.Dequeue());
        _sessionRegistry.Received(1).Remove(player);
        Assert.Null(response);
    }
}
