using Basil.Application.Shared;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to open a room for a new match.</summary>
/// <param name="Actor">The connection that creates the room.</param>
/// <param name="Name">The room's name.</param>
/// <param name="Password">The room's password, or an empty string for none.</param>
/// <param name="Source">Where the request comes from: the creator's game client or the API.</param>
/// <param name="IsPrivate">Whether the room is private.</param>
/// <param name="Settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
/// <remarks>The answer carries the opened room when the outcome is Ok.</remarks>
public sealed record OpenRoom(
	Connection Actor,
	string Name,
	string Password,
	RoomSource Source,
	bool IsPrivate,
	MatchSettings? Settings = null) : IRequest<OpenedRoom>;

/// <summary>The answer to <see cref="OpenRoom" />.</summary>
/// <param name="Room">The opened room, or <see langword="null" /> when it was refused.</param>
/// <param name="Result">The outcome of opening it.</param>
public sealed record OpenedRoom(Room? Room, RoomOutcome Result);