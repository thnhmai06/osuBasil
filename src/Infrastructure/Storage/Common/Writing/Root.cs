namespace Basil.Infrastructure.Storage.Common.Writing;

/// <summary>What a stored change belongs to; changes that belong to the same root are stored in the order they were made.</summary>
internal readonly record struct Root
{
	public readonly RootKind Kind;

	public readonly int Id;

	private Root(RootKind kind, int id)
	{
		Kind = kind;
		Id = id;
	}

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

internal enum RootKind : byte
{
	User,
	Match,
	Beatmapset,
	Server
}