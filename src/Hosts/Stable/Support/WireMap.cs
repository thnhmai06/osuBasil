using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Mechanics;
using Basil.Protocol.Stable.Models.Multiplayer;

namespace Basil.Host.Stable.Support;

internal static class WireMap
{
	private static readonly long AllMods = Enum.GetValues<GameMods>().Aggregate(0L, (all, mod) => all | (long)mod);

	public static T? Known<T>(long wire) where T : struct, Enum
	{
		var value = (T)Enum.ToObject(typeof(T), wire);
		return Convert.ToInt64(value) == wire && Enum.IsDefined(value) ? value : null;
	}

	public static GameMods Mods(long wire)
	{
		return (GameMods)(wire & AllMods);
	}

	public static WireSlotTeam ToWire(GameTeam? team)
	{
		return team switch
		{
			null => WireSlotTeam.Neutral,
			GameTeam.Blue => WireSlotTeam.Blue,
			GameTeam.Red => WireSlotTeam.Red,
			_ => throw Unmapped(team.Value)
		};
	}

	public static GameTeam? FromWire(WireSlotTeam team)
	{
		return team switch
		{
			WireSlotTeam.Blue => GameTeam.Blue,
			WireSlotTeam.Red => GameTeam.Red,
			_ => null
		};
	}

	public static WireSlotStatus ToWire(RoomSlotStatus status)
	{
		return status switch
		{
			RoomSlotStatus.NoMap => WireSlotStatus.NoMap,
			RoomSlotStatus.NotReady => WireSlotStatus.NotReady,
			RoomSlotStatus.Ready => WireSlotStatus.Ready,
			RoomSlotStatus.Playing => WireSlotStatus.Playing,
			RoomSlotStatus.Complete => WireSlotStatus.Complete,
			_ => throw Unmapped(status)
		};
	}

	public static RoomSlotStatus? FromWire(WireSlotStatus status)
	{
		return status switch
		{
			WireSlotStatus.NoMap => RoomSlotStatus.NoMap,
			WireSlotStatus.NotReady => RoomSlotStatus.NotReady,
			WireSlotStatus.Ready => RoomSlotStatus.Ready,
			WireSlotStatus.Playing => RoomSlotStatus.Playing,
			WireSlotStatus.Complete => RoomSlotStatus.Complete,
			_ => null
		};
	}

	private static ArgumentOutOfRangeException Unmapped<T>(T value) where T : struct, Enum
	{
		return new ArgumentOutOfRangeException(nameof(value), value, "The value has no wire counterpart.");
	}
}