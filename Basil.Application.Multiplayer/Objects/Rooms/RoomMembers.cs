using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

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

	internal ConcurrentSet<TourneySession> MutableObservers => _observers;
}