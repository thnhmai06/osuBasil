namespace Basil.Domain.Mechanics;

/// <summary>A rule of the game mechanics was broken: mods, modes and scoring.</summary>
public abstract class MechanicsException(string message) : BasilException(message);

/// <summary>A set of mods is not valid for the game mode, or breaks the freemod rule of the room.</summary>
public sealed class InvalidModsException() : MechanicsException("The mods are not valid.");
