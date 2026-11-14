using Basil.Application.Bot.Objects;

namespace Basil.Application.Bot.Tests.Countdowns;

/// <summary>
///     The remaining times a countdown announces. The expected marks come from the announcement rules of
///     docs/for-developers/multiplayer.md ("Countdown"): a timer announces 60, 30, 10 and 5 seconds; a countdown that
///     starts a round announces every whole minute left (unless the end is within 5 seconds) and then 30, 10, 5, 4 and 3
///     seconds; a mark is only announced while it is shorter than the countdown.
/// </summary>
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