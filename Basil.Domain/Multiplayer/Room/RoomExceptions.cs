namespace Basil.Domain.Multiplayer.Room;

/// <summary>A rule of the multiplayer module was broken: rooms and matches.</summary>
public abstract class MultiplayerException(string message) : BasilException(message);

/// <summary>A change to a room's settings holds a value the room cannot take.</summary>
public sealed class InvalidRoomSettingsException() : MultiplayerException("The room settings are not valid.");
