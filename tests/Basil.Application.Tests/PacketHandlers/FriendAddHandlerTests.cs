using Basil.Application.Abstractions.Social;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's AddFriend.</summary>
public class FriendAddHandlerTests
{
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();

    private FriendAddHandler MakeHandler()
    {
        return new FriendAddHandler(_relationships);
    }

    private static BanchoPacketReader TargetReader(int targetId)
    {
        return new BanchoPacketReader(PacketWriter.WriteInt32(targetId));
    }

    [Fact]
    public async Task HandleAsync_NoExistingRelationship_CreatesFriendRelationship()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _relationships.FetchOneAsync(1, 2).Returns((Relationship?)null);

        await MakeHandler().HandleAsync(player, TargetReader(2));

        await _relationships.Received(1).CreateAsync(1, 2, RelationshipType.Friend);
    }

    [Fact]
    public async Task HandleAsync_RelationshipAlreadyExists_DoesNotCreateAgain()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _relationships.FetchOneAsync(1, 2).Returns(new Relationship(1, 2, RelationshipType.Block));

        await MakeHandler().HandleAsync(player, TargetReader(2));

        await _relationships.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, RelationshipType.Friend);
    }

    [Fact]
    public async Task HandleAsync_TargetIsSelf_DoesNothing()
    {
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        await MakeHandler().HandleAsync(player, TargetReader(1));

        await _relationships.DidNotReceiveWithAnyArgs().FetchOneAsync(0, 0);
        await _relationships.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, RelationshipType.Friend);
    }
}
