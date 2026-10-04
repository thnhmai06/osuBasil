using System.Threading.Channels;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Multiplayer.Rooms;

namespace Basil.Application.Services.Multiplayer;

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