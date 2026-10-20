using Basil.Application.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Multiplayer.Rooms;

/// <summary>Removes a closing connection from room memberships.</summary>
public interface IRoomMembershipService
{
	/// <summary>Removes a connection from every room it plays in or observes.</summary>
	/// <param name="connection">The connection to release.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Called when the connection closes or when the user may no longer take part in multiplayer.</remarks>
	Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default);

	/// <summary>Removes a user whose multiplayer permissions are suspended from every room they play in, observe or referee.</summary>
	/// <param name="session">The user's online session.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Does nothing while the user may still join rooms. Called when a restriction takes effect.</remarks>
	Task RemoveSuspendedAsync(UserSession session, CancellationToken cancellationToken = default);

	/// <summary>Removes a user from the referee list of every open room.</summary>
	/// <param name="user">The user who stops being a referee.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Used for a user who is offline when a restriction suspends multiplayer, so no session reaches the rooms.</remarks>
	Task RemoveRefereeAsync(UserRecord user, CancellationToken cancellationToken = default);
}