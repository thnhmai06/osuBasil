using Basil.Application.Sessions.Objects;

namespace Basil.Application.Bot.Objects;

internal sealed record RoomCountdown(Session Actor, TimeSpan Length, bool StartsRound, DateTimeOffset StartedAt)
{
	public static readonly TimeSpan Min = TimeSpan.FromSeconds(1);

	public static readonly TimeSpan Max = TimeSpan.FromHours(1);

	public DateTimeOffset EndsAt => StartedAt + Length;
}