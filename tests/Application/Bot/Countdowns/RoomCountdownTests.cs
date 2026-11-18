using Basil.Application.Bot.Objects;

namespace Basil.Application.Bot.Tests.Countdowns;

public sealed class RoomCountdownTests
{
	[Fact]
	public void FiveMinuteTimer_FourMarks()
	{
		Assert.Equal(Seconds(60, 30, 10, 5), RoomCountdown.MarksFor(TimeSpan.FromMinutes(5), false));
	}

	[Fact]
	public void TimerAtMark_MarkLeftOut()
	{
		Assert.Equal(Seconds(10, 5), RoomCountdown.MarksFor(TimeSpan.FromSeconds(30), false));
	}

	[Fact]
	public void OneSecondTimer_NoMarks()
	{
		Assert.Empty(RoomCountdown.MarksFor(TimeSpan.FromSeconds(1), false));
	}

	[Fact]
	public void NinetySecondRound_MinuteThenMarks()
	{
		Assert.Equal(Seconds(60, 30, 10, 5, 4, 3), RoomCountdown.MarksFor(TimeSpan.FromSeconds(90), true));
	}

	[Fact]
	public void FiveMinuteRound_EveryMinute()
	{
		Assert.Equal(Seconds(240, 180, 120, 60, 30, 10, 5, 4, 3),
			RoomCountdown.MarksFor(TimeSpan.FromMinutes(5), true));
	}

	[Fact]
	public void MinuteNearEnd_LeftOut()
	{
		Assert.Equal(Seconds(30, 10, 5, 4, 3), RoomCountdown.MarksFor(TimeSpan.FromSeconds(65), true));
	}

	[Fact]
	public void ThirtySecondRound_StartsAtTen()
	{
		Assert.Equal(Seconds(10, 5, 4, 3), RoomCountdown.MarksFor(TimeSpan.FromSeconds(30), true));
	}

	[Fact]
	public void EndsAt_StartPlusLength()
	{
		var started = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var countdown = new RoomCountdown(null!, TimeSpan.FromSeconds(45), false, started);

		Assert.Equal(started.AddSeconds(45), countdown.EndsAt);
	}

	private static TimeSpan[] Seconds(params int[] seconds)
	{
		return [.. seconds.Select(second => TimeSpan.FromSeconds(second))];
	}
}