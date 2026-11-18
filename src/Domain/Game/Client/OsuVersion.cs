namespace Basil.Domain.Game.Client;

public readonly record struct OsuVersion(DateOnly Date, int? Revision, OsuVersionStream Stream);

public enum OsuVersionStream : byte
{
	Stable,

	Beta,

	CuttingEdge,

	Tourney,

	Dev
}