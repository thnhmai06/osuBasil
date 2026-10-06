using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Contracts.Multiplayer;

/// <summary>Who belongs to a room besides its seated players: the users banned from it and the osu!tourney clients observing it.</summary>
public sealed class RoomMembers
{
	private readonly ConcurrentSet<User> _banned = [];
	private readonly ConcurrentSet<TourneyConnection> _observers = [];

	/// <summary>Gets the users banned from this room.</summary>
	public IReadOnlySet<User> Banned => _banned;

	/// <summary>Gets the osu!tourney clients observing the room.</summary>
	public IReadOnlySet<TourneyConnection> Observers => _observers;

	/// <summary>Bans a user from the room.</summary>
	/// <param name="user">The user to ban.</param>
	/// <returns><see langword="true" /> if the user was banned; <see langword="false" /> if they already were.</returns>
	internal bool AddBanned(User user)
	{
		return _banned.Add(user);
	}

	/// <summary>Lifts a user's ban from the room.</summary>
	/// <param name="user">The user to unban.</param>
	/// <returns><see langword="true" /> if the ban was lifted; <see langword="false" /> if the user was not banned.</returns>
	internal bool RemoveBanned(User user)
	{
		return _banned.Remove(user);
	}

	/// <summary>Adds an osu!tourney client to the room's observers.</summary>
	/// <param name="observer">The client to add.</param>
	/// <returns><see langword="true" /> if the client was added; <see langword="false" /> if it already observed the room.</returns>
	internal bool AddObserver(TourneyConnection observer)
	{
		return _observers.Add(observer);
	}

	/// <summary>Removes an osu!tourney client from the room's observers.</summary>
	/// <param name="observer">The client to remove.</param>
	/// <returns><see langword="true" /> if the client was removed; <see langword="false" /> if it did not observe the room.</returns>
	internal bool RemoveObserver(TourneyConnection observer)
	{
		return _observers.Remove(observer);
	}

	/// <summary>Removes every observer from the room.</summary>
	internal void ClearObservers()
	{
		_observers.Clear();
	}
}
