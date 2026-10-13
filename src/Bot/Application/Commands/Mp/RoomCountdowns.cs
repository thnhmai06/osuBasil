using System.Collections.Concurrent;
using Basil.Bot.Application.Announcements;
using Basil.Bot.Application.Basil;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Commands.Mp;

/// <summary>The countdowns running in rooms, at most one per room, announced in the room's channel.</summary>
internal sealed class RoomCountdowns(IBasilChat chat, IBasilRooms rooms, TimeProvider time)
{
	/// <summary>The countdown running in each room, keyed by room id.</summary>
	private readonly ConcurrentDictionary<int, RoomCountdown> _running = new();

	/// <summary>Starts a countdown in a room, replacing the one running there, and announces it.</summary>
	/// <param name="roomId">The room.</param>
	/// <param name="actor">The user who started it; a countdown that starts the round starts it as this user.</param>
	/// <param name="length">How long it runs.</param>
	/// <param name="startsRound">Whether the round starts when it ends.</param>
	/// <param name="cancellationToken">A token that cancels the announcement.</param>
	public async Task StartAsync(int roomId, User actor, TimeSpan length, bool startsRound,
		CancellationToken cancellationToken)
	{
		var countdown = new RoomCountdown(roomId, actor, startsRound, length, this, time);
		_running.AddOrUpdate(roomId, countdown, (_, replaced) =>
		{
			replaced.Dispose();
			return countdown;
		});

		countdown.Run();
		var seconds = (int)length.TotalSeconds;
		await chat.PostAsync(Channel(roomId), startsRound
			? string.Format(AnnouncementReplies.CountdownStartedForRound, seconds)
			: string.Format(AnnouncementReplies.CountdownStarted, seconds), cancellationToken);
	}

	/// <summary>Stops a room's countdown and announces that it was aborted.</summary>
	/// <param name="roomId">The room.</param>
	/// <param name="cancellationToken">A token that cancels the announcement.</param>
	/// <returns><see langword="false" /> when no countdown was running.</returns>
	public async Task<bool> CancelAsync(int roomId, CancellationToken cancellationToken)
	{
		if (!Stop(roomId)) return false;
		await chat.PostAsync(Channel(roomId), AnnouncementReplies.CountdownCancelled, cancellationToken);
		return true;
	}

	/// <summary>Stops a room's countdown without announcing it.</summary>
	/// <param name="roomId">The room.</param>
	/// <param name="onlyStartingRound">Whether to stop it only when it would start the round.</param>
	/// <returns><see langword="true" /> when a countdown was stopped.</returns>
	public bool Stop(int roomId, bool onlyStartingRound = false)
	{
		if (!_running.TryGetValue(roomId, out var countdown)) return false;
		if (onlyStartingRound && !countdown.StartsRound) return false;
		if (!_running.TryRemove(new KeyValuePair<int, RoomCountdown>(roomId, countdown))) return false;
		countdown.Dispose();
		return true;
	}

	/// <summary>Gets the remaining times at which a countdown announces itself, longest first.</summary>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts the round when it ends.</param>
	/// <returns>
	///     Every mark is strictly below <paramref name="length" />. A countdown that starts the round announces at 30, 10, 5,
	///     4 and 3 seconds and at every whole minute (60, 120, ...), skipping any whole-minute mark within 5 seconds of the
	///     length. Any other countdown announces at 60, 30, 10 and 5 seconds.
	/// </returns>
	internal static IReadOnlyList<TimeSpan> MarksFor(TimeSpan length, bool startsRound)
	{
		int[] seconds = startsRound ? [30, 10, 5, 4, 3] : [60, 30, 10, 5];
		var marks = seconds.Select(second => TimeSpan.FromSeconds(second)).Where(mark => mark < length).ToList();

		if (startsRound)
			for (var minute = TimeSpan.FromMinutes(1); minute < length; minute += TimeSpan.FromMinutes(1))
				if (length - minute > TimeSpan.FromSeconds(5))
					marks.Add(minute);

		return [.. marks.OrderByDescending(mark => mark)];
	}

	/// <summary>Gets the chat channel of a room, named after its id.</summary>
	/// <param name="roomId">The room id.</param>
	/// <returns>The channel name, such as <c>#mp_5</c>.</returns>
	private static string Channel(int roomId) => $"#mp_{roomId}";

	/// <summary>Announces a mark of a countdown that is still the room's current one.</summary>
	/// <param name="countdown">The countdown whose mark was reached.</param>
	/// <param name="remaining">The time left when the mark was reached.</param>
	private async Task TickAsync(RoomCountdown countdown, TimeSpan remaining)
	{
		if (!IsCurrent(countdown)) return;
		var seconds = (int)remaining.TotalSeconds;
		await chat.PostAsync(Channel(countdown.RoomId), countdown.StartsRound
			? string.Format(AnnouncementReplies.CountdownTickedForRound, seconds)
			: string.Format(AnnouncementReplies.CountdownTicked, seconds), CancellationToken.None);
	}

	/// <summary>Ends a countdown that is still the room's current one: starts the round or announces the end.</summary>
	/// <param name="countdown">The countdown that ran out.</param>
	private async Task ElapseAsync(RoomCountdown countdown)
	{
		if (!_running.TryRemove(new KeyValuePair<int, RoomCountdown>(countdown.RoomId, countdown))) return;
		countdown.Dispose();
		if (!countdown.StartsRound)
		{
			await chat.PostAsync(Channel(countdown.RoomId), AnnouncementReplies.CountdownElapsed,
				CancellationToken.None);
			return;
		}

		var outcome = await rooms.StartAsync(countdown.Actor, countdown.RoomId, CancellationToken.None);
		string? text = outcome switch
		{
			// A round nobody plays has already ended; the round announcement says so instead.
			RoomOutcome.Ok => await rooms.GetAsync(countdown.RoomId, CancellationToken.None) is { InProgress: true }
				? AnnouncementReplies.RoundStartedByCountdown
				: null,
			RoomOutcome.NoBeatmap => AnnouncementReplies.CountdownElapsedForRound,
			_ => MpReplies.Describe(outcome)
		};
		if (text is not null)
			await chat.PostAsync(Channel(countdown.RoomId), text, CancellationToken.None);
	}

	/// <summary>Determines whether a countdown is still the one running in its room.</summary>
	/// <param name="countdown">The countdown to check.</param>
	/// <returns><see langword="true" /> when no newer countdown replaced it.</returns>
	private bool IsCurrent(RoomCountdown countdown) =>
		_running.TryGetValue(countdown.RoomId, out var current) && ReferenceEquals(current, countdown);

	/// <summary>One countdown in one room: a timer per announced mark and one for its end.</summary>
	/// <param name="roomId">The room.</param>
	/// <param name="actor">The user who started it; a countdown that starts the round starts it as this user.</param>
	/// <param name="startsRound">Whether the round starts when it ends.</param>
	/// <param name="length">How long it runs.</param>
	/// <param name="owner">The countdown collection that runs it.</param>
	/// <param name="time">The clock the timers use.</param>
	private sealed class RoomCountdown(
		int roomId,
		User actor,
		bool startsRound,
		TimeSpan length,
		RoomCountdowns owner,
		TimeProvider time) : IDisposable
	{
		/// <summary>The timers that announce the marks and the end; empty until the countdown starts.</summary>
		private ITimer[] _timers = [];

		/// <summary>Gets the room the countdown runs in.</summary>
		public int RoomId => roomId;

		/// <summary>Gets the user who started the countdown.</summary>
		public User Actor => actor;

		/// <summary>Gets whether the round starts when the countdown ends.</summary>
		public bool StartsRound => startsRound;

		/// <summary>Starts the timers.</summary>
		public void Run()
		{
			_timers =
			[
				.. MarksFor(length, startsRound).Select(mark =>
					time.CreateTimer(_ => Fire(() => owner.TickAsync(this, mark)), null, length - mark,
						Timeout.InfiniteTimeSpan)),
				time.CreateTimer(_ => Fire(() => owner.ElapseAsync(this)), null, length, Timeout.InfiniteTimeSpan)
			];
		}

		/// <summary>Stops every timer that has not fired.</summary>
		public void Dispose()
		{
			foreach (var timer in _timers) timer.Dispose();
		}

		/// <summary>Runs an announcement and ignores its failure, so a failed announcement cannot crash the bot.</summary>
		/// <param name="action">The announcement to run.</param>
		private static void Fire(Func<Task> action) =>
			_ = action().ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
	}
}