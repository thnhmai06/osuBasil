using Basil.Application.Users.Objects.Sessions;

using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed class RoomMembers
{
	private readonly ConcurrentSet<UserRecord> _banned = [];
	private readonly ConcurrentSet<TourneySession> _observers = [];

	public IReadOnlySet<UserRecord> Banned => _banned;

	public IReadOnlySet<TourneySession> Observers => _observers;

	internal ISet<UserRecord> MutableBanned => _banned;

	internal ISet<TourneySession> MutableObservers => _observers;
}