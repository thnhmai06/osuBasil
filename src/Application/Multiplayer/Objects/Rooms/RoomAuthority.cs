using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Shared;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed class RoomAuthority
{
	public const int MaxReferees = 8;

	private readonly MatchRecord _match;
	private readonly ConcurrentSet<UserRecord> _referees = [];

	internal RoomAuthority(MatchRecord match)
	{
		_match = match;
	}

	public UserRecord Creator => _match.Value.Creator;

	public IReadOnlySet<UserRecord> Referees => _referees;

	public StableConnection? Host { get; internal set; }

	internal ISet<UserRecord> MutableReferees => _referees;

	public bool IsManagedBy(UserRecord user)
	{
		return Creator.Equals(user) || Referees.Contains(user);
	}
}