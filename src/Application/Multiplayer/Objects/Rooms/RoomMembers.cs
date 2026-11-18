using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Shared;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed class RoomMembers
{
	private readonly ConcurrentSet<UserRecord> _banned = [];
	private readonly ConcurrentSet<TourneyConnection> _observers = [];

	public IReadOnlySet<UserRecord> Banned => _banned;

	public IReadOnlySet<TourneyConnection> Observers => _observers;

	internal ISet<UserRecord> MutableBanned => _banned;

	internal ISet<TourneyConnection> MutableObservers => _observers;
}