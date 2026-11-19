using System.Collections;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>Everyone taking part in a room, with their roles, and the users banned from it.</summary>
public sealed class RoomMembers : IReadOnlyCollection<RoomUser>
{
	private readonly HashSet<UserRecord> _banned = [];
	private readonly HashSet<RoomUser> _members = [];
	private readonly Room _room;

	internal RoomMembers(Room room)
	{
		_room = room;
		Creator = new RoomUser(room.Match.Value.Creator, RoomPrivileges.Creator);
		_members.Add(Creator);
	}

	public IReadOnlySet<UserRecord> Banned => _banned;

	public RoomUser Creator { get; }

	public RoomUser? Host => Holding(RoomPrivileges.Host).FirstOrDefault();

	public IEnumerable<RoomUser> Referees => Holding(RoomPrivileges.Referee);

	public RoomUser? Find(UserRecord user)
	{
		return this.FirstOrDefault(member => member.Record.Equals(user));
	}

	/// <summary>Gives the member for a user, or a new user without roles when the user is not taking part.</summary>
	public RoomUser Of(UserRecord user)
	{
		return Find(user) ?? new RoomUser(user);
	}

	/// <summary>Admits a user with the given roles; refused when the room is closed, the user is banned or already takes part.</summary>
	public bool Add(RoomUser user)
	{
		return !_room.IsClosed && !_banned.Contains(user.Record) && _members.Add(user);
	}

	/// <summary>Removes a user from the room; refused when the room is closed or the user manages it.</summary>
	public bool Remove(RoomUser user)
	{
		return !_room.IsClosed && Find(user.Record) is { } member &&
		       !member.Has(RoomPrivileges.Moderators) && _members.Remove(member);
	}

	public RoomActionResult AddReferee(RoomUser user)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		if (Creator.Equals(user)) return RoomActionResult.IsCreator;
		if (Find(user.Record)?.Has(RoomPrivileges.Referee) is true) return RoomActionResult.AlreadyReferee;
		if (Referees.Count() >= RoomRules.MaxReferees) return RoomActionResult.TooManyReferees;

		Grant(user, RoomPrivileges.Referee);
		return RoomActionResult.Ok;
	}

	public RoomActionResult RemoveReferee(RoomUser user)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		if (Find(user.Record)?.Has(RoomPrivileges.Referee) is not true) return RoomActionResult.NotReferee;

		Revoke(user, RoomPrivileges.Referee);
		return RoomActionResult.Ok;
	}

	public (RoomActionResult Outcome, bool Changed) SetHost(RoomUser? host)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, false);
		if (host is not null && _room.Slots.Find(host.Record) is null) return (RoomActionResult.NotInRoom, false);
		if (Equals(Host, host)) return (RoomActionResult.Ok, false);

		if (Host is { } previous) Revoke(previous, RoomPrivileges.Host);
		if (host is not null) Grant(host, RoomPrivileges.Host);
		return (RoomActionResult.Ok, true);
	}

	public (RoomActionResult Outcome, int Slot, RoomRoundProgress? Progress)
		Kick(RoomUser player, DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, 0, null);
		if (Find(player.Record)?.Has(RoomPrivileges.Moderators) is true) return (RoomActionResult.IsManager, 0, null);
		if (_room.Slots.Find(player.Record) is null) return (RoomActionResult.NotInRoom, 0, null);

		var slot = _room.Slots.Vacate(player.Record)!;
		return (RoomActionResult.Ok, slot.Index, _room.Rounds.Advance(now));
	}

	public (RoomActionResult Outcome, bool Added, int? Vacated, RoomRoundProgress? Progress) Ban(RoomUser player,
		DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, false, null, null);
		if (Find(player.Record)?.Has(RoomPrivileges.Moderators) is true) return (RoomActionResult.IsManager, false, null, null);
		if (!_banned.Add(player.Record)) return (RoomActionResult.Ok, false, null, null);

		int? vacated = null;
		RoomRoundProgress? progress = null;
		if (_room.Slots.Find(player.Record) is not null)
		{
			vacated = _room.Slots.Vacate(player.Record)?.Index;
			progress = _room.Rounds.Advance(now);
		}

		if (Find(player.Record) is { } member) _members.Remove(member);
		return (RoomActionResult.Ok, true, vacated, progress);
	}

	public RoomActionResult Unban(RoomUser player)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		return _banned.Remove(player.Record) ? RoomActionResult.Ok : RoomActionResult.NotBanned;
	}

	// Adds the roles to the member for the user, admitting the user when not yet taking part.
	internal void Grant(RoomUser user, RoomPrivileges roles)
	{
		var member = Find(user.Record);
		if (member is null)
		{
			user.Privileges |= roles;
			_members.Add(user);
			return;
		}

		member.Privileges |= roles;
	}

	internal void Revoke(RoomUser user, RoomPrivileges roles)
	{
		if (Find(user.Record) is { } member) member.Privileges &= ~roles;
	}

	// A user who leaves the room keeps only the roles the room itself gave them: creator and referee.
	internal void Depart(UserRecord user)
	{
		if (Find(user) is not { } member) return;

		member.Privileges &= ~(RoomPrivileges.Player | RoomPrivileges.Host);
		if (member.Privileges == RoomPrivileges.Viewer) _members.Remove(member);
	}

	private IEnumerable<RoomUser> Holding(RoomPrivileges role)
	{
		return _members.Where(user => user.Has(role));
	}

	public int Count => _members.Count;

	public IEnumerator<RoomUser> GetEnumerator()
	{
		return _members.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}

/// <summary>A user taking part in one room, with the roles the user holds there; two values are equal when they name the same user.</summary>
public sealed class RoomUser(UserRecord record, RoomPrivileges privileges = RoomPrivileges.Viewer)
	: IEquatable<RoomUser>
{
	public UserRecord Record { get; } = record;

	public RoomPrivileges Privileges { get; internal set; } = privileges;

	public bool Has(RoomPrivileges roles)
	{
		return (Privileges & roles) != 0;
	}

	public bool Equals(RoomUser? other)
	{
		return other is not null && Record.Equals(other.Record);
	}

	public override bool Equals(object? obj)
	{
		return obj is RoomUser other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Record.GetHashCode();
	}
}
