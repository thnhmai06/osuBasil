using Bancho.Application.Abstractions;
using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Domain;
using NSubstitute;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's block.</summary>
public class BlockCommandTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();

    private BlockCommand MakeCommand() => new(_sessionRegistry, _users, _relationships);

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
    public async Task HandleAsync_TargetIsBot_ReturnsWhat()
    {
        var player = new PlayerSession(5, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var bot = new PlayerSession(1, "BanchoBot", "bot-token", Privileges.Unrestricted, 0.0, isBotClient: true);
        _sessionRegistry.GetByName("BanchoBot").Returns(bot);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "BanchoBot"));

        Assert.Equal("What?", response);
    }

    [Fact]
    public async Task HandleAsync_AlreadyBlocked_ReturnsAlreadyBlockedMessage()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);
        _relationships.FetchOneAsync(1, 2).Returns(new Relationship(1, 2, RelationshipType.Block));

        var response = await MakeCommand().HandleAsync(MakeContext(player, "other"));

        Assert.Equal("other already blocked!", response);
    }

    [Fact]
    public async Task HandleAsync_WasFriend_RemovesFriendshipThenBlocks()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);
        _relationships.FetchOneAsync(1, 2).Returns(new Relationship(1, 2, RelationshipType.Friend));

        var response = await MakeCommand().HandleAsync(MakeContext(player, "other"));

        await _relationships.Received(1).DeleteAsync(1, 2);
        await _relationships.Received(1).CreateAsync(1, 2, RelationshipType.Block);
        Assert.Equal("Added other to blocked users.", response);
    }

    [Fact]
    public async Task HandleAsync_NoExistingRelationship_CreatesBlock()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetByName("other").Returns(target);
        _relationships.FetchOneAsync(1, 2).Returns((Relationship?)null);

        var response = await MakeCommand().HandleAsync(MakeContext(player, "other"));

        await _relationships.Received(1).CreateAsync(1, 2, RelationshipType.Block);
        Assert.Equal("Added other to blocked users.", response);
    }
}
