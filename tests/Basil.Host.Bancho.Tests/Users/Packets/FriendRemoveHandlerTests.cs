using Basil.Application.Sessions;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Host.Bancho.Users.Packets;
using Basil.Protocol.Packets;
using Basil.Application.Social;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Host.Bancho.Tests.Users.Packets;

/// <summary>Verifies the `RemoveFriend` handler deletes a friend relationship, leaving blocks untouched.</summary>
public class FriendRemoveHandlerTests
{
	private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();

	private FriendRemoveHandler MakeHandler()
	{
		return new FriendRemoveHandler(_relationships);
	}

	private static PacketReader TargetReader(int targetId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(targetId));
	}

	[Fact]
	public async Task HandleAsync_ExistingFriendRelationship_DeletesIt()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_relationships.FetchOneAsync(1, 2).Returns(new Relationship(new User { Id = 1, Name = "cmyui" },
			new User { Id = 2, Name = "target" }, RelationshipType.Friend));

		await MakeHandler().HandleAsync(player, TargetReader(2));

		await _relationships.Received(1).DeleteAsync(1, 2);
	}

	[Fact]
	public async Task HandleAsync_ExistingBlockRelationship_DoesNotDeleteIt()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_relationships.FetchOneAsync(1, 2).Returns(new Relationship(new User { Id = 1, Name = "cmyui" },
			new User { Id = 2, Name = "target" }, RelationshipType.Block));

		await MakeHandler().HandleAsync(player, TargetReader(2));

		await _relationships.DidNotReceiveWithAnyArgs().DeleteAsync(0, 0);
	}

	[Fact]
	public async Task HandleAsync_NoExistingRelationship_DoesNothing()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_relationships.FetchOneAsync(1, 2).Returns((Relationship?)null);

		await MakeHandler().HandleAsync(player, TargetReader(2));

		await _relationships.DidNotReceiveWithAnyArgs().DeleteAsync(0, 0);
	}
}