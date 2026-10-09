namespace Basil.Infrastructure.Storage.Writing;

/// <summary>What a stored change belongs to; changes that belong to the same root are stored in the order they were made.</summary>
internal readonly record struct Root(RootKind Kind, int Id)
{
	public static Root Server => new(RootKind.Server, 0);

	public static Root User(int id)
	{
		return new Root(RootKind.User, id);
	}

	public static Root Match(int id)
	{
		return new Root(RootKind.Match, id);
	}

	public static Root Beatmapset(int id)
	{
		return new Root(RootKind.Beatmapset, id);
	}
}

internal enum RootKind
{
	User,
	Match,
	Beatmapset,
	Server
}