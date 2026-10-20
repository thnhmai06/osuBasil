using Basil.Application.Objects.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Multiplayer.Rooms;

/// <summary>
///     Who belongs to a room besides its seated players: the users banned from it and the osu!tourney clients
///     observing it.
/// </summary>
public sealed class RoomMembers
{
	private readonly ConcurrentSet<UserRecord> _banned = [];
	private readonly ConcurrentSet<TourneyConnection> _observers = [];

	/// <summary>Gets the users banned from this room.</summary>
	public IReadOnlySet<UserRecord> Banned => _banned;

	/// <summary>Gets the osu!tourney clients observing the room.</summary>
	public IReadOnlySet<TourneyConnection> Observers => _observers;

	/// <summary>Gets the users banned from this room, for changing them.</summary>
	internal ISet<UserRecord> MutableBanned => _banned;

	/// <summary>Gets the osu!tourney clients observing the room, for changing them.</summary>
	internal ISet<TourneyConnection> MutableObservers => _observers;
}