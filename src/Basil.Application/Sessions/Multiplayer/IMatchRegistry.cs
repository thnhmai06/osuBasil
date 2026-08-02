namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Represents the runtime registry of multiplayer match sessions: a fixed 64-slot table,
///     because the osu! tournament client's packets validate that a match id falls in the range
///     0 to 63, rather than an unbounded collection.
/// </summary>
public interface IMatchRegistry
{
	/// <summary>The number of wire-protocol match slots the registry holds.</summary>
	public const int MaxMatches = 64; // TODO: Remove this limits

	/// <summary>Gets a snapshot of every registered match session.</summary>
	IReadOnlyList<MatchSession> All { get; }

	/// <summary>
	///     Gets the match registered in the 0 to 63 wire-protocol slot <paramref name="id" />, or
	///     null if that slot is free.
	/// </summary>
	/// <param name="id">The wire-protocol slot id of the match.</param>
	/// <returns>The match in that slot, or null if the slot is free.</returns>
	MatchSession? GetById(int id);

	/// <summary>
	///     Gets the match with the persistent database id <paramref name="dbId" />, or null if no
	///     such match is currently registered. This lookup keys by the persistent
	///     <see cref="MatchSession.DbId" /> rather than the wire-protocol slot <see cref="GetById" />
	///     uses.
	/// </summary>
	/// <param name="dbId">The persistent database id of the match.</param>
	/// <returns>The matching match, or null if none is registered under that database id.</returns>
	MatchSession? GetByDbId(int dbId);

	/// <summary>
	///     Atomically finds the first free slot (0 to 63) and registers the session that
	///     <paramref name="factory" /> builds for that slot id, returning null when every slot is
	///     occupied.
	/// </summary>
	/// <remarks>
	///     Finding the free slot and registering the session must happen as one step: if they were
	///     separated, two concurrent creations could claim the same slot.
	/// </remarks>
	/// <param name="factory">A factory that builds a match for a given slot id.</param>
	/// <returns>The newly registered match, or null if all slots are occupied.</returns>
	MatchSession? TryCreate(Func<int, MatchSession> factory);

	/// <summary>
	///     Unregisters the match in the wire-protocol slot <paramref name="id" />, called when a
	///     match is torn down.
	/// </summary>
	/// <param name="id">The wire-protocol slot id of the match to remove.</param>
	void Remove(int id);
}