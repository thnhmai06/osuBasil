namespace Basil.Domain.Game;

/// <summary>A rule of the game mechanics was broken: mods, modes and scoring.</summary>
public abstract class GameException(string message) : BasilException(message);

/// <summary>A set of mods is not valid for the game mode, or breaks the freemod rule of the room.</summary>
public sealed class InvalidModsException() : GameException("The mods are not valid.");
