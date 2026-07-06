using Bancho.Application.Abstractions;
using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Domain;
using NSubstitute;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's unblock.</summary>
public class UnblockCommandTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();

    private UnblockCommand MakeCommand() => new(_sessionRegistry, _users, _relationships);

    private static CommandContext MakeContext(PlayerSession player, params string[] args) => new(player, args, null, null);

    [Fact]
    public async Task HandleAsync_UnknownUser_ReturnsNotFound()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("nobody").Returns((PlayerSession?)null);
        _users.FetchByNameAsync("nobody").Returns((User?)null);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "nobody"));

        Assert.Equal("User not found.", response);
    }

    [Fact]
    public async Task HandleAsync_TargetIsSelf_ReturnsWhat()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("cmyui").Returns(player);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "cmyui"));

        Assert.Equal("What?", response);
    }

    [Fact]
    public async Task HandleAsync_NotBlocked_ReturnsNotBlockedMessage()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);
        _relationships.FetchOneAsync(1, 2).Returns((Relationship?)null);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "other"));

        Assert.Equal("other not blocked!", response);
    }

    [Fact]
    public async Task HandleAsync_Blocked_RemovesBlock()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);
        _relationships.FetchOneAsync(1, 2).Returns(new Relationship(1, 2, RelationshipType.Block));

        var response = await MakeCommand().HandleAsync(MakeContext(player, "other"));

        await _relationships.Received(1).DeleteAsync(1, 2);
        Assert.Equal("Removed other from blocked users.", response);
    }
}
