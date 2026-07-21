using Basil.Application.Abstractions.Social;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's RemoveFriend.</summary>
public class FriendRemoveHandlerTests
{
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();

    private FriendRemoveHandler MakeHandler()
    {
        return new FriendRemoveHandler(_relationships);
    }

    private static BanchoPacketReader TargetReader(int targetId)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(targetId));
    }

    [Fact]
    public async Task HandleAsync_ExistingFriendRelationship_DeletesIt()
    {
        var player = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _relationships.FetchOneAsync(1, 2).Returns(new Relationship(1, 2, RelationshipType.Friend));

        await MakeHandler().HandleAsync(player, TargetReader(2));

        await _relationships.Received(1).DeleteAsync(1, 2);
    }

    [Fact]
    public async Task HandleAsync_ExistingBlockRelationship_DoesNotDeleteIt()
    {
        var player = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _relationships.FetchOneAsync(1, 2).Returns(new Relationship(1, 2, RelationshipType.Block));

        await MakeHandler().HandleAsync(player, TargetReader(2));

        await _relationships.DidNotReceiveWithAnyArgs().DeleteAsync(0, 0);
    }

    [Fact]
    public async Task HandleAsync_NoExistingRelationship_DoesNothing()
    {
        var player = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _relationships.FetchOneAsync(1, 2).Returns((Relationship?)null);

        await MakeHandler().HandleAsync(player, TargetReader(2));

        await _relationships.DidNotReceiveWithAnyArgs().DeleteAsync(0, 0);
    }
}
