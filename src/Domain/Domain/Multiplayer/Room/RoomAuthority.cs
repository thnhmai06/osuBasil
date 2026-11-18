using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Domain.Multiplayer;

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

	public UserRecord? Host { get; internal set; }

	internal ISet<UserRecord> MutableReferees => _referees;

	public bool IsManagedBy(UserRecord user)
	{
		return Creator.Equals(user) || Referees.Contains(user);
	}
}