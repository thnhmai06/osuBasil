using Basil.Application.Runtime.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <summary>Who has authority over a room: its creator, its referees and its host.</summary>
public sealed class RoomAuthority
{
	/// <summary>The most referees a room can have.</summary>
	public const int MaxReferees = 8;

	private readonly MatchRecord _match;
	private readonly ConcurrentSet<UserRecord> _referees = [];

	/// <summary>Creates the authority of a room.</summary>
	/// <param name="match">The match the room plays.</param>
	internal RoomAuthority(MatchRecord match)
	{
		_match = match;
	}

	/// <summary>Gets the user who created the match.</summary>
	public UserRecord? Creator => _match.Value.Creator;

	/// <summary>Gets the users with referee authority for this room.</summary>
	public IReadOnlySet<UserRecord> Referees => _referees;

	/// <summary>Gets the connection currently hosting the room.</summary>
	/// <remarks>The host must be seated in this room.</remarks>
	public BanchoConnection? Host { get; internal set; }

	/// <summary>Gets the users with referee authority for this room, for changing them.</summary>
	internal ISet<UserRecord> MutableReferees => _referees;

	/// <summary>Checks whether a user is the room's creator or one of its referees.</summary>
	/// <param name="user">The user to check.</param>
	/// <returns><see langword="true" /> if the user is the creator or a referee.</returns>
	public bool IsManagedBy(UserRecord user)
	{
		return (Creator is not null && Creator.Equals(user)) || Referees.Contains(user);
	}
}