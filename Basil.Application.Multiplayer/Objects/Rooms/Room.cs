using System.Collections.Concurrent;
using Basil.Application.Multiplayer.Capabilities.Rooms;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using DomainRoom = Basil.Domain.Multiplayer.Room.Room;
using MatchRecord = Basil.Domain.Multiplayer.Report.Matches.MatchRecord;
using MatchSettings = Basil.Domain.Multiplayer.Report.Matches.MatchSettings;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room : IEquatable<Room>
{
	private readonly ConcurrentDictionary<UserRecord, StableSession> _seats = new();

	private ITimer? _timer;
	private RoundSettlement? _timerSettlement;

	internal Room(int id, MatchRecord match, MatchSettings settings, RoomContext context, string password = "")
	{
		State = new DomainRoom(id, match, settings, password);
		Members = new RoomMembers(State.Banned);
		Context = context;
	}

	internal RoomContext Context { get; }

	public DomainRoom State { get; }

	public MatchRecord Match => State.Match;

	public int Id => State.Id;

	public RoomSlots Slots => State.Slots;

	public RoomSettings Settings => State.Settings;

	public RoomAuthority Authority => State.Authority;

	public RoomMembers Members { get; }

	public RoomRounds Rounds => State.Rounds;

	public bool IsClosed => State.IsClosed;

	public StableSession? Host => Authority.Host is { } host ? SessionOf(host) : null;

	public IEnumerable<StableSession> Players => Slots.Select(PlayerAt).OfType<StableSession>();

	internal DateTimeOffset? EmptySince { get; set; }

	internal bool ClosingWarned { get; set; }

	public string Url => $"osump://{Id}/{Settings.Password}";

	public string UrlEmbed => $"({Settings.Name})[{Url}]";

	private DateTimeOffset Now => Context.Clock.GetUtcNow();

	public bool Equals(Room? other)
	{
		return other is not null && Match.Equals(other.Match);
	}

	public StableSession? SessionOf(UserRecord user)
	{
		return _seats.GetValueOrDefault(user);
	}

	public StableSession? PlayerAt(RoomSlot slot)
	{
		return slot.Player is { } player ? SessionOf(player) : null;
	}

	public RoomSlot? SlotOf(StableSession player)
	{
		return Seated(player) is { } record ? Slots.Find(record) : null;
	}

	public ScopedRoom ScopeTo(Session actor)
	{
		return new ScopedRoom(this, actor);
	}

	internal Lock.Scope Enter()
	{
		return State.Enter();
	}

	internal void MarkClosed()
	{
		State.Close();
	}

	private UserRecord? Seated(StableSession player)
	{
		return _seats.TryGetValue(player.Record, out var current) && ReferenceEquals(current, player)
			? player.Record
			: null;
	}

	private UserRecord? PlayerOf(Session actor)
	{
		return actor is StableSession stable ? Seated(stable) : null;
	}

	private StableSession? Unseat(UserRecord user)
	{
		return _seats.TryRemove(user, out var session) ? session : null;
	}

	private void Emit(RoomEvent @event)
	{
		Context.Events.Publish(@event);
		if (!Rounds.TakeSettlePending()) return;

		SettleRound();
	}

	public override bool Equals(object? obj)
	{
		return obj is Room other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Match.GetHashCode();
	}
}