using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer;

/// <summary>
///     Tells connected clients what happened to a match. The match services decide the outcome and
///     who has to hear about it; the implementation decides how each client's transport says it.
/// </summary>
public interface IMatchNotifier
{
	/// <summary>Tells a player that their attempt to join a match was refused.</summary>
	/// <param name="player">The player who tried to join.</param>
	void JoinRejected(GameSession player);

	/// <summary>Tells a player that they are no longer in a match, after being removed from one.</summary>
	/// <param name="player">The player who was removed.</param>
	void Removed(GameSession player);

	/// <summary>Tells a player that they have joined a match, with the match as it stands.</summary>
	/// <param name="player">The player who joined.</param>
	/// <param name="match">The match they joined.</param>
	void Joined(GameSession player, MatchSession match);

	/// <summary>Tells a player that they are now the match host.</summary>
	/// <param name="newHost">The player who received host.</param>
	void HostTransferred(GameSession newHost);

	/// <summary>Tells a player that another player has invited them to a match.</summary>
	/// <param name="target">The invited player.</param>
	/// <param name="sender">The player who sent the invitation.</param>
	/// <param name="match">The match the invitation is for.</param>
	void Invited(GameSession target, UserSession sender, MatchSession match);

	/// <summary>Tells the players in a match that a round has started.</summary>
	/// <param name="match">The match whose round started.</param>
	/// <param name="notPlaying">Ids of the players who sit out this round and must not be told.</param>
	void RoundStarted(MatchSession match, IReadOnlyCollection<int> notPlaying);

	/// <summary>Tells the players in a match that the round in progress was aborted.</summary>
	/// <param name="match">The match whose round was aborted.</param>
	void RoundAborted(MatchSession match);

	/// <summary>Tells the players browsing the lobby that a match no longer exists.</summary>
	/// <param name="match">The match that was closed.</param>
	void Disposed(MatchSession match);

	/// <summary>
	///     Tells the players in a match, and for a public match the players browsing the lobby, the
	///     match's current state.
	/// </summary>
	/// <remarks>
	///     Delivered in order of <paramref name="version" />: a call whose version is older than one
	///     already delivered is dropped, so a slow caller never shows clients a state that has since
	///     changed.
	/// </remarks>
	/// <param name="match">The match whose state to send.</param>
	/// <param name="version">The state version this call carries.</param>
	/// <param name="lobby"><see langword="true" /> to also tell the lobby; otherwise, <see langword="false" />.</param>
	void StateChanged(MatchSession match, long version, bool lobby);
}