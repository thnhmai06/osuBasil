namespace Basil.Host.Shared.Documentation;

public sealed record ApiDocument(string Name, string Title, string Overview)
{
	public const string Stable = "osu-stable";
}