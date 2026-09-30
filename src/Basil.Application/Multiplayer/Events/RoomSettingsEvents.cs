namespace Basil.Application.Multiplayer.Events;

/// <summary>A room's shared settings changed.</summary>
public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

/// <summary>A room's settings changed.</summary>
/// <param name="Room">The room whose settings changed.</param>
/// <param name="Change">The fields that changed; <c>Password</c> is always <see langword="null" /> so the password never travels in events.</param>
/// <param name="PasswordChanged">Whether the password changed.</param>
public sealed record RoomSettingsChanged(Room Room, RoomSettingsChange Change, bool PasswordChanged) : RoomSettingsEvent(Room);
