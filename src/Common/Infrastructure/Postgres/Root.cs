namespace Basil.Common.Infrastructure.Postgres;

/// <summary>What a stored change belongs to; changes that belong to the same root are stored in the order they were made.</summary>
public readonly record struct Root
{
	internal readonly int Id;
	internal readonly RootKind Kind;

	private Root(RootKind kind, int id)
	{
		Kind = kind;
		Id = id;
	}

	/// <summary>Gets the root that identifies work belonging to the server.</summary>
	public static Root Server => new(RootKind.Server, 0);

	/// <summary>Gets the root that identifies work belonging to a user.</summary>
	/// <param name="id">The user's identifier.</param>
	public static Root User(int id)
	{
		return new Root(RootKind.User, id);
	}

	/// <summary>Gets the root that identifies work belonging to a match.</summary>
	/// <param name="id">The match's identifier.</param>
	public static Root Match(int id)
	{
		return new Root(RootKind.Match, id);
	}

	/// <summary>Gets the root that identifies work belonging to a beatmapset.</summary>
	/// <param name="id">The beatmapset's identifier.</param>
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