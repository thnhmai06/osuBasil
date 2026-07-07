using Bancho.Application.Abstractions;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's FriendRemove.</summary>
public class FriendRemoveHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();

    private FriendRemoveHandler MakeHandler() => new(_sessionRegistry, _relationships);

    private static BanchoPacketReader UserIdReader(int userId) => new(PacketWriter.WriteInt32(userId));

    [Fact]
    public async Task HandleAsync_TargetNotOnline_NoOp()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetById(2).Returns((PlayerSession?)null);

        await MakeHandler().HandleAsync(player, UserIdReader(2));

        await _relationships.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task HandleAsync_TargetOnline_RemovesFriendRelationship()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "other", "other-token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.GetById(2).Returns(target);

        await MakeHandler().HandleAsync(player, UserIdReader(2));

        await _relationships.Received(1).DeleteAsync(1, 2);
    }
}
