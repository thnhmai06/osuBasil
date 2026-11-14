using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Mechanics;
using Basil.Protocol.Stable.Models.Multiplayer;

namespace Basil.Host.Stable.Support;

/// <summary>
///     Checks the numbers the osu! client sends against the Domain values they stand for and translates the two
///     client concepts that have no Domain twin (a slot's team and an occupied slot's status).
/// </summary>
/// <remarks>
///     Domain enums carry the client's numbering, so a value that is defined in Domain crosses the wire as is.
///     <c>Known</c> and <c>Mods</c> are the guard for client input: a number Domain does not define is never passed on.
///     <c>ToWire</c> throws <see cref="ArgumentOutOfRangeException" /> for a Domain value that has no wire
///     counterpart. <c>FromWire</c> returns <see langword="null" /> for a wire value that Domain does not know.
/// </remarks>
internal static class WireMap
{
	private static readonly long AllMods = Enum.GetValues<GameMods>().Aggregate(0L, (all, mod) => all | (long)mod);

	/// <summary>Gets the Domain value a client number stands for.</summary>
	/// <typeparam name="T">The Domain enum.</typeparam>
	/// <param name="wire">The number the client sent.</param>
	/// <returns>The value, or <see langword="null" /> when the number is not a value of <typeparamref name="T" />.</returns>
	public static T? Known<T>(long wire) where T : struct, Enum
	{
		var value = (T)Enum.ToObject(typeof(T), wire);
		return Convert.ToInt64(value) == wire && Enum.IsDefined(value) ? value : null;
	}

	/// <summary>Gets the mods a client's mod bits stand for.</summary>
	/// <param name="wire">The client's mod bits.</param>
	/// <returns>The mods; a bit Domain does not define is dropped.</returns>
	public static GameMods Mods(long wire)
	{
		return (GameMods)(wire & AllMods);
	}

	/// <summary>Gets the number the client uses for the team of a slot.</summary>
	/// <param name="team">The slot's team, or <see langword="null" /> when it is on none.</param>
	/// <returns>The client's slot team.</returns>
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

	/// <summary>Gets the team a client number stands for.</summary>
	/// <param name="team">The client's slot team.</param>
	/// <returns>The team, or <see langword="null" /> when the slot is on none or the number is unknown.</returns>
	public static GameTeam? FromWire(WireSlotTeam team)
	{
		return team switch
		{
			WireSlotTeam.Blue => GameTeam.Blue,
			WireSlotTeam.Red => GameTeam.Red,
			_ => null
		};
	}

	/// <summary>Gets the number the client uses for the status of an occupied slot.</summary>
	/// <param name="status">The occupant's status.</param>
	/// <returns>The client's slot status.</returns>
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

	/// <summary>Gets the occupant status a client number stands for.</summary>
	/// <param name="status">The client's slot status.</param>
	/// <returns>The status, or <see langword="null" /> when the slot is empty or the number is unknown.</returns>
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