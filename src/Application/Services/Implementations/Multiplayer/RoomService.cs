using System.Threading.Channels;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Implementations.Multiplayer.Rooms;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>Changes room membership and records scores submitted during rounds.</summary>
internal sealed class RoomService(
	RoomMembershipService members,
	RoomRoundsService rounds,
	RoomEventStream events) : IRoomService
{
	/// <inheritdoc />
	public ChannelReader<RoomEvent> Events => events.Reader;

	/// <inheritdoc />
	public IRoomMembershipService Members => members;

	/// <inheritdoc />
	public IRoomRoundsService Rounds => rounds;
}