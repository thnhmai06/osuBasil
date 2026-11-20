using System.Collections;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>Everyone taking part in a room, with their roles, and the users banned from it.</summary>
public sealed class RoomMembers : IReadOnlyCollection<RoomUser>
{
	private readonly ConcurrentSet<UserRecord> _banned = [];
	private readonly ConcurrentSet<RoomUser> _members = [];
	private readonly Room _room;

	internal RoomMembers(Room room)
	{
		_room = room;
		Creator = new RoomUser(room.Match.Value.Creator, RoomPrivileges.Creator);
		_members.Add(Creator);
	}

	/// <summary>Gets the users banned from the room.</summary>
	public IReadOnlySet<UserRecord> Banned => _banned;

	/// <summary>Gets the member who created the room.</summary>
	public RoomUser Creator { get; }

	/// <summary>Gets the current host, or <see langword="null"/> when the room has none.</summary>
	public RoomUser? Host => Holding(RoomPrivileges.Host).FirstOrDefault();

	/// <summary>Gets the members who are referees.</summary>
	public IEnumerable<RoomUser> Referees => Holding(RoomPrivileges.Referee);

	/// <summary>Gets the member for a user, or <see langword="null"/> when the user is not taking part.</summary>
	public RoomUser? Find(UserRecord user)
	{
		return this.FirstOrDefault(member => member.Record.Equals(user));
	}

	/// <summary>Gets the member for a user, or a member without roles that is not part of the room when the user is not taking part.</summary>
	/// <param name="user">The user.</param>
	public RoomUser Of(UserRecord user)
	{
		return Find(user) ?? new RoomUser(user);
	}

	/// <summary>Admits a user without roles; refused when the room is closed, the user is banned or already takes part.</summary>
	/// <param name="user">The user to admit.</param>
	public RoomActionResult Add(UserRecord user)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		if (_banned.Contains(user)) return RoomActionResult.Banned;
		return _members.Add(new RoomUser(user)) ? RoomActionResult.Ok : RoomActionResult.AlreadyMember;
	}

	/// <summary>Removes a user from the room; refused when the room is closed, the user does not take part, is the creator or a referee, or holds a slot.</summary>
	/// <param name="user">The user to remove.</param>
	public RoomActionResult Remove(UserRecord user)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		if (Find(user) is not { } member) return RoomActionResult.NotInRoom;
		if (member.Has(RoomPrivileges.Moderators)) return RoomActionResult.IsManager;
		if (_room.Slots.Find(user) is not null) return RoomActionResult.IsPlayer;

		_members.Remove(member);
		return RoomActionResult.Ok;
	}

	/// <summary>Makes a user a referee, admitting them when they are not taking part; refused when the room is closed, the user is banned, the creator or already a referee.</summary>
	/// <param name="user">The user to make a referee.</param>
	public RoomActionResult AddReferee(RoomUser user)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		if (_banned.Contains(user.Record)) return RoomActionResult.Banned;
		if (Creator.Equals(user)) return RoomActionResult.IsCreator;
		if (Find(user.Record)?.Has(RoomPrivileges.Referee) is true) return RoomActionResult.AlreadyReferee;

		Grant(user, RoomPrivileges.Referee);
		return RoomActionResult.Ok;
	}

	/// <summary>Takes the referee role from a user; refused when the room is closed or the user is not a referee.</summary>
	/// <param name="user">The user to remove as referee.</param>
	public RoomActionResult RemoveReferee(RoomUser user)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		if (Find(user.Record)?.Has(RoomPrivileges.Referee) is not true) return RoomActionResult.NotReferee;

		Revoke(user, RoomPrivileges.Referee);
		return RoomActionResult.Ok;
	}

	/// <summary>Makes a user the host, or leaves the room without a host; refused when the room is closed or the user holds no slot.</summary>
	/// <param name="host">The new host, or <see langword="null"/> for no host.</param>
	/// <returns>The outcome, and whether the host changed.</returns>
	public (RoomActionResult Outcome, bool Changed) SetHost(RoomUser? host)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, false);
		if (host is not null && _room.Slots.Find(host.Record) is null) return (RoomActionResult.NotInRoom, false);
		if (Equals(Host, host)) return (RoomActionResult.Ok, false);

		if (Host is { } previous) Revoke(previous, RoomPrivileges.Host);
		if (host is not null) Grant(host, RoomPrivileges.Host);
		return (RoomActionResult.Ok, true);
	}

	/// <summary>Removes a player from their slot; refused when the room is closed, the player is the creator or a referee, or holds no slot.</summary>
	/// <param name="player">The player to remove.</param>
	/// <param name="now">The time of the kick.</param>
	/// <returns>The outcome, the index of the vacated slot, and how the round in progress moved on, or <see langword="null"/> when it did not.</returns>
	public (RoomActionResult Outcome, int Slot, RoomRoundProgress? Progress)
		Kick(RoomUser player, DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, 0, null);
		if (Find(player.Record)?.Has(RoomPrivileges.Moderators) is true) return (RoomActionResult.IsManager, 0, null);
		if (_room.Slots.Find(player.Record) is null) return (RoomActionResult.NotInRoom, 0, null);

		var slot = _room.Slots.Vacate(player.Record)!;
		return (RoomActionResult.Ok, slot.Index, _room.Rounds.Advance(now));
	}

	/// <summary>Bans a user, removing them from their slot and from the room; refused when the room is closed or the user is the creator or a referee.</summary>
	/// <param name="player">The user to ban.</param>
	/// <param name="now">The time of the ban.</param>
	/// <returns>The outcome (<see cref="RoomActionResult.Ok"/> without a change when the user was already banned), whether the ban is new, the index of the slot vacated or <see langword="null"/> when the user held none, and how the round in progress moved on, or <see langword="null"/> when it did not.</returns>
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

	/// <summary>Lifts the ban on a user; refused when the room is closed or the user is not banned.</summary>
	/// <param name="player">The user to unban.</param>
	public RoomActionResult Unban(RoomUser player)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		return _banned.Remove(player.Record) ? RoomActionResult.Ok : RoomActionResult.NotBanned;
	}

	/// <summary>Adds roles to a user, admitting the user when not yet taking part.</summary>
	/// <param name="user">The user.</param>
	/// <param name="roles">The roles to add.</param>
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

	/// <summary>Takes the player and host roles from a user who left their slot, and removes the user from the room when no other role is left.</summary>
	/// <param name="user">The user who left.</param>
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
public sealed class RoomUser : IEquatable<RoomUser>
{
	/// <summary>Creates a member with the given roles.</summary>
	/// <param name="record">The user.</param>
	/// <param name="privileges">The roles the user starts with.</param>
	internal RoomUser(UserRecord record, RoomPrivileges privileges = RoomPrivileges.Viewer)
	{
		Record = record;
		Privileges = privileges;
	}

	/// <summary>Gets the user.</summary>
	public UserRecord Record { get; }

	/// <summary>Gets the roles the user holds in the room.</summary>
	public RoomPrivileges Privileges { get; internal set; }

	/// <summary>Gets whether the user holds at least one of the roles.</summary>
	/// <param name="roles">The roles to look for.</param>
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
