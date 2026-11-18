using Basil.Application.Users.Objects.Sessions;
using Basil.Shared.Structures.Data;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed class RoomMembers
{
	private readonly ConcurrentSet<TourneySession> _observers = [];

	internal RoomMembers(IReadOnlySet<UserRecord> banned)
	{
		Banned = banned;
	}

	public IReadOnlySet<UserRecord> Banned { get; }

	public IReadOnlySet<TourneySession> Observers => _observers;

	internal ISet<TourneySession> MutableObservers => _observers;
}