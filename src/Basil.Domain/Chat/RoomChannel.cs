using Basil.Domain.Client;
using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Domain.Chat;

public sealed class RoomChannel(Room room) : IChannel //! only exist in Registry
{
	public string Name => $"mp_{room.Match.Id}";

	public string Topic => room.Match.Name;

	public string DisplayName => "multiplayer";

	public ClientPrivileges ReadPrivilege => ClientPrivileges.Player;

	public ClientPrivileges WritePrivilege => ClientPrivileges.Player;

	public bool AutoJoin => false;

	public bool Visible => false;

	public bool Equals(IChannel? other)
	{
		if (other is null) return false;
		return Name == other.Name;
	}

	public override bool Equals(object? obj)
	{
		return obj is IChannel other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Name.GetHashCode();
	}
}