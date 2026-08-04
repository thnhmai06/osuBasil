using Basil.Protocol.Multiplayer;

namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Represents the runtime registry of multiplayer match sessions.
/// </summary>
public interface IMatchRegistry
{
	/// <summary>Gets a snapshot of every registered match session.</summary>
	IReadOnlyCollection<MatchSession> All { get; }

	/// <summary>
	///     Gets the match registered under the wire-protocol id <paramref name="id" />, or null if
	///     no match holds that id.
	/// </summary>
	/// <param name="id">The wire-protocol id of the match.</param>
	/// <returns>The match with that id, or null if none is registered.</returns>
	MatchSession? GetById(int id);

	/// <summary>
	///     Gets the match with the persistent database id <paramref name="dbId" />, or null if no
	///     such match is currently registered. This lookup keys by the persistent
	///     <see cref="MatchSession.DbId" /> rather than the wire-protocol id <see cref="GetById" />
	///     uses.
	/// </summary>
	/// <param name="dbId">The persistent database id of the match.</param>
	/// <returns>The matching match, or null if none is registered under that database id.</returns>
	MatchSession? GetByDbId(int dbId);

	/// <summary>
	///     Atomically allocates the lowest free wire-protocol id and registers the session.
	/// </summary>
	/// <remarks>
	///     Finding the free id and registering the session must happen as one step: if they were
	///     separated, two concurrent creations could claim the same id.
	/// </remarks>
	/// <param name="data">The parsed match-create data.</param>
	/// <param name="hostId">The id of the userSession who created the room.</param>
	/// <param name="createdViaMakeCommand">
	///     <see langword="true" /> when created via <c>!mp make</c>; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the create operations.</param>
	/// <returns>The newly registered match.</returns>
	Task<MatchSession> CreateAsync(MatchState data, int hostId, bool createdViaMakeCommand = false,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Unregisters the match with the wire-protocol id <paramref name="id" />, called when a
	///     match is torn down.
	/// </summary>
	/// <param name="id">The wire-protocol id of the match to remove.</param>
	void Remove(int id);
}