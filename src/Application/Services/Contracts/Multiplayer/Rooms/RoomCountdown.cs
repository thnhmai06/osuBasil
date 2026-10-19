using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>A room countdown, identified by reference, that may start a round when it elapses.</summary>
public sealed class RoomCountdown
{
	/// <summary>Creates a room countdown.</summary>
	/// <param name="by">The user who started the countdown.</param>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <param name="startedAt">When the countdown started.</param>
	public RoomCountdown(UserRecord by, TimeSpan length, bool startsRound, DateTimeOffset startedAt)
	{
		By = by;
		Length = length;
		StartsRound = startsRound;
		StartedAt = startedAt;
	}

	/// <summary>Gets the user who started the countdown.</summary>
	public UserRecord By { get; }

	/// <summary>Gets how long the countdown runs.</summary>
	public TimeSpan Length { get; }

	/// <summary>Gets whether the countdown starts a round when it elapses.</summary>
	public bool StartsRound { get; }

	/// <summary>Gets when the countdown started.</summary>
	public DateTimeOffset StartedAt { get; }

	/// <summary>Gets when the countdown elapses.</summary>
	public DateTimeOffset EndsAt => StartedAt + Length;

	/// <summary>Gets the countdown marks in descending order.</summary>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <returns>The remaining times announced during the countdown.</returns>
	public static IReadOnlyList<TimeSpan> MarksFor(TimeSpan length, bool startsRound)
	{
		int[] seconds = startsRound ? [30, 10, 5, 4, 3] : [60, 30, 10, 5];
		var marks = seconds.Select(second => TimeSpan.FromSeconds(second)).Where(mark => mark < length).ToList();
		if (!startsRound) return [.. marks.OrderByDescending(mark => mark)];

		for (var minute = TimeSpan.FromMinutes(1); minute < length; minute += TimeSpan.FromMinutes(1))
			if (length - minute > TimeSpan.FromSeconds(5))
				marks.Add(minute);

		return [.. marks.OrderByDescending(mark => mark)];
	}
}
