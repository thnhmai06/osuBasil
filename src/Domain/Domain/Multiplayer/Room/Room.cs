using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Domain.Multiplayer.Room;

public sealed partial class Room
{
	private readonly ConcurrentSet<UserRecord> _banned = [];
	private readonly Lock _sync = new();

	public Room(int id, MatchRecord match, MatchSettings settings, string password = "")
	{
		ArgumentOutOfRangeException.ThrowIfNegative(id);
		Id = id;
		Match = match;
		Settings = new RoomSettings(match, settings, password);
		Authority = new RoomAuthority(match);
		Rounds = new RoomRounds();
		Slots = new RoomSlots();
	}

	public int Id { get; }

	public MatchRecord Match { get; }

	public RoomSlots Slots { get; }

	public RoomSettings Settings { get; }

	public RoomAuthority Authority { get; }

	public RoomRounds Rounds { get; }

	public IReadOnlySet<UserRecord> Banned => _banned;

	public bool IsClosed
	{
		get => Volatile.Read(ref field);
		private set => Volatile.Write(ref field, value);
	}

	public Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	public void Close()
	{
		IsClosed = true;
	}
}