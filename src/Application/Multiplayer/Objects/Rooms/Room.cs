using Basil.Application.Multiplayer.Capabilities.Rooms;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using MatchRecord = Basil.Domain.Multiplayer.MatchRecord;
using MatchSettings = Basil.Domain.Multiplayer.MatchSettings;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room : IEquatable<Room>
{
	private readonly Lock _sync = new();

	private bool _settlePending;

	internal Room(int id, MatchRecord match, MatchSettings settings, RoomContext context)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(id);
		Id = id;
		Match = match;
		Settings = new RoomSettings(match, settings);
		Authority = new RoomAuthority(match);
		Members = new RoomMembers();
		Rounds = new RoomRounds();
		Slots = new RoomSlots(this);
		Context = context;
	}

	internal RoomContext Context { get; }

	public MatchRecord Match { get; }

	public int Id { get; }

	public RoomSlots Slots { get; }

	public RoomSettings Settings { get; }

	public RoomAuthority Authority { get; }

	public RoomMembers Members { get; }

	public RoomRounds Rounds { get; }

	public bool IsClosed
	{
		get => Volatile.Read(ref field);
		internal set => Volatile.Write(ref field, value);
	}

	internal DateTimeOffset? EmptySince { get; set; }

	internal bool ClosingWarned { get; set; }

	public string Url => $"osump://{Id}/{Settings.Password}";

	public string UrlEmbed => $"({Settings.Name})[{Url}]";

	public bool Equals(Room? other)
	{
		return other is not null && Match.Equals(other.Match);
	}

	public ScopedRoom ScopeTo(Session actor)
	{
		return new ScopedRoom(this, actor);
	}

	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	private void Emit(RoomEvent @event)
	{
		Context.Events.Publish(@event);
		if (!_settlePending) return;

		_settlePending = false;
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