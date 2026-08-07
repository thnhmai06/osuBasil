using Basil.Application.Abstractions.Social;
using Basil.Application.Packets.Users;
using Basil.Application.Sessions;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Application.Tests.Packets;

/// <summary>Verifies the `AddFriend` handler creates a friend relationship, skipping duplicates and self.</summary>
public class FriendAddHandlerTests
{
	private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();

	private FriendAddHandler MakeHandler()
	{
		return new FriendAddHandler(_relationships);
	}

	private static PacketReader TargetReader(int targetId)
	{
		return new PacketReader(BinaryWriter.WriteInt32(targetId));
	}

	[Fact]
	public async Task HandleAsync_NoExistingRelationship_CreatesFriendRelationship()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_relationships.FetchOneAsync(1, 2).Returns((Relationship?)null);

		await MakeHandler().HandleAsync(player, TargetReader(2));

		await _relationships.Received(1).CreateAsync(1, 2, RelationshipType.Friend);
	}

	[Fact]
	public async Task HandleAsync_RelationshipAlreadyExists_DoesNotCreateAgain()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_relationships.FetchOneAsync(1, 2).Returns(new Relationship(1, 2, RelationshipType.Block));

		await MakeHandler().HandleAsync(player, TargetReader(2));

		await _relationships.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, RelationshipType.Friend);
	}

	[Fact]
	public async Task HandleAsync_TargetIsSelf_DoesNothing()
	{
		var player = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		await MakeHandler().HandleAsync(player, TargetReader(1));

		await _relationships.DidNotReceiveWithAnyArgs().FetchOneAsync(0, 0);
		await _relationships.DidNotReceiveWithAnyArgs().CreateAsync(0, 0, RelationshipType.Friend);
	}
}