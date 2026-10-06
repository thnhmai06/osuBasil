using Basil.Application.Storage.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Multiplayer;

/// <summary>Who has authority over a room: its creator, its referees and its host.</summary>
public sealed class RoomAuthority
{
	/// <summary>The most referees a room can have.</summary>
	public const int MaxReferees = 8;

	private readonly Match _match;
	private readonly ConcurrentSet<User> _referees = [];

	/// <summary>Creates the authority of a room.</summary>
	/// <param name="match">The match the room plays.</param>
	internal RoomAuthority(Match match)
	{
		_match = match;
	}

	/// <summary>Gets the user who created the match.</summary>
	public User? Creator => _match.Value.Creator;

	/// <summary>Gets the users with referee authority for this room.</summary>
	public IReadOnlySet<User> Referees => _referees;

	/// <summary>Gets the connection currently hosting the room.</summary>
	/// <remarks>The host must be seated in this room.</remarks>
	public BanchoConnection? Host { get; internal set; }

	/// <summary>Checks whether a user is the room's creator or one of its referees.</summary>
	/// <param name="user">The user to check.</param>
	/// <returns><see langword="true" /> if the user is the creator or a referee.</returns>
	public bool IsManagedBy(User user)
	{
		return (Creator is not null && Creator.Equals(user)) || Referees.Contains(user);
	}

	/// <summary>Makes a user a referee of the room.</summary>
	/// <param name="user">The user to add.</param>
	/// <returns><see langword="true" /> if the user was added; <see langword="false" /> if they already were a referee.</returns>
	internal bool AddReferee(User user)
	{
		return _referees.Add(user);
	}

	/// <summary>Removes a user from the room's referees.</summary>
	/// <param name="user">The user to remove.</param>
	/// <returns><see langword="true" /> if the user was removed; <see langword="false" /> if they were not a referee.</returns>
	internal bool RemoveReferee(User user)
	{
		return _referees.Remove(user);
	}
}
