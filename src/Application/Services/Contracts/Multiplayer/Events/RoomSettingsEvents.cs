using Basil.Application.Storage.Contracts.Multiplayer;

namespace Basil.Application.Services.Contracts.Multiplayer.Events;

/// <summary>A room's shared settings changed.</summary>
public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

/// <summary>A room's settings changed.</summary>
/// <param name="Room">The room whose settings changed.</param>
/// <param name="Change">
///     The fields that changed; <c>Password</c> is always <see langword="null" /> so the password never
///     travels in events.
/// </param>
/// <param name="PasswordChanged">Whether the password changed.</param>
/// <param name="CountdownCancelled">Whether the change cancelled a countdown that would have started the round.</param>
public sealed record RoomSettingsChanged(
	Room Room,
	RoomSettingsChange Change,
	bool PasswordChanged,
	bool CountdownCancelled)
	: RoomSettingsEvent(Room);