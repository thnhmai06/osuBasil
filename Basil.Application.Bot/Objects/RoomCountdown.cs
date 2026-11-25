using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Bot.Objects;

internal sealed record RoomCountdown(Session Actor, TimeSpan Length, bool StartsRound, DateTimeOffset StartedAt)
{
	public static readonly TimeSpan Min = TimeSpan.FromSeconds(1);

	public static readonly TimeSpan Max = TimeSpan.FromHours(1);

	public DateTimeOffset EndsAt => StartedAt + Length;

	/// <summary>Tells whether a countdown may last this long.</summary>
	public static bool AllowsLength(TimeSpan length)
	{
		return length >= Min && length <= Max;
	}

	/// <summary>Gives the reason a countdown cannot start in a room, or <see langword="null"/> when it can.</summary>
	/// <exception cref="InvalidRoomSettingsException">The length is outside the allowed range.</exception>
	public static RoomActionResult? Refusal(Room room, TimeSpan length, bool startsRound)
	{
		if (room.IsClosed) return RoomActionResult.RoomClosed;
		if (!AllowsLength(length)) throw new InvalidRoomSettingsException();
		if (startsRound && room.Rounds.InProgress) return RoomActionResult.InProgress;
		return null;
	}
}