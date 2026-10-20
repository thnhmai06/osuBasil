using System.Threading.Channels;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Services.Multiplayer.Rooms;

namespace Basil.Application.Services.Multiplayer;

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