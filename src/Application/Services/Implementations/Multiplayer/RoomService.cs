using System.Threading.Channels;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Implementations.Multiplayer.Rooms;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>Changes rooms through its children: membership, authority, settings, slots and rounds.</summary>
internal sealed class RoomService(
	RoomMembershipService members,
	RoomAuthorityService authority,
	RoomSettingsService settings,
	RoomSlotsService slots,
	RoomRoundsService rounds,
	RoomEventStream events) : IRoomService
{
	/// <inheritdoc />
	public ChannelReader<RoomEvent> Events => events.Reader;

	/// <inheritdoc />
	public IRoomMembershipService Members => members;

	/// <inheritdoc />
	public IRoomAuthorityService Authority => authority;

	/// <inheritdoc />
	public IRoomSettingsService Settings => settings;

	/// <inheritdoc />
	public IRoomSlotsService Slots => slots;

	/// <inheritdoc />
	public IRoomRoundsService Rounds => rounds;
}