using System.Collections.Concurrent;
using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Objects;
using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Bot.Workers;

/// <summary>Why a running countdown was cancelled; decides what, if anything, BasilBot says in the room.</summary>
internal enum CountdownCancelReason
{
	/// <summary>Someone asked for the countdown to be aborted.</summary>
	Aborted = 1,

	/// <summary>A setting the countdown's round depends on changed.</summary>
	SettingsChanged,

	/// <summary>A round was started by hand while the countdown ran.</summary>
	RoundStarted,

	/// <summary>The round in progress was aborted.</summary>
	RoundAborted,

	/// <summary>The room closed.</summary>
	RoomClosed
}

/// <summary>Runs the countdowns of rooms: announces their marks and, when one elapses, starts the round it was for.</summary>
/// <remarks>A room has at most one countdown; starting another replaces it. The caller checks the privileges.</remarks>
internal sealed class RoomCountdowns(
	TimeProvider clock,
	ISender sender,
	Server server,
	RoomChannels channels,
	IReplies replies,
	ILogger<RoomCountdowns> logger)
{
	private readonly ConcurrentDictionary<Room, Running> _running = new();

	/// <summary>Gets how many countdowns are running.</summary>
	public int Count => _running.Count;

	/// <summary>Tells whether a countdown is running in a room.</summary>
	/// <param name="room">The room.</param>
	/// <returns><see langword="true" /> when a countdown is running.</returns>
	public bool IsRunning(Room room)
	{
		return _running.ContainsKey(room);
	}

	/// <summary>Starts a countdown in a room, replacing the one already running.</summary>
	/// <param name="actor">The connection that starts the countdown.</param>
	/// <param name="room">The room.</param>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <param name="ct">A token that cancels the announcements.</param>
	/// <returns>Ok, InvalidSettings (out of range), InProgress or RoomClosed.</returns>
	public async Task<RoomOutcome> StartAsync(Connection actor, Room room, TimeSpan length, bool startsRound,
		CancellationToken ct)
	{
		if (room.IsClosed) return RoomOutcome.RoomClosed;
		if (length < RoomCountdown.Min || length > RoomCountdown.Max) return RoomOutcome.InvalidSettings;
		if (startsRound && room.Rounds.InProgress) return RoomOutcome.InProgress;

		var running = new Running(new RoomCountdown(actor, length, startsRound, clock.GetUtcNow()));
		running.Timers =
		[
			.. RoomCountdown.MarksFor(length, startsRound).Select(mark =>
				clock.CreateTimer(InBackground(() => TickAsync(room, running, mark)), null, length - mark,
					Timeout.InfiniteTimeSpan)),
			clock.CreateTimer(InBackground(() => ElapseAsync(room, running)), null, length,
				Timeout.InfiniteTimeSpan)
		];

		Running? replaced = null;
		while (true)
		{
			if (_running.TryGetValue(room, out var old))
			{
				if (!_running.TryUpdate(room, running, old)) continue;

				replaced = old;
				break;
			}

			if (_running.TryAdd(room, running)) break;
		}

		if (replaced is not null)
		{
			replaced.Dispose();
			var remaining = Math.Max(0, (int)Math.Round((replaced.Countdown.EndsAt - clock.GetUtcNow()).TotalSeconds));
			await SayAsync(room, replies.Text(AnnouncementReplies.CountdownReplaced, new { seconds = remaining }),
				ct);
		}

		var seconds = (int)length.TotalSeconds;
		await SayAsync(room, startsRound
			? replies.Text(AnnouncementReplies.CountdownStartedForRound, new { seconds })
			: replies.Text(AnnouncementReplies.CountdownStarted, new { seconds }), ct);
		return RoomOutcome.Ok;
	}

	/// <summary>Cancels the countdown of a room and says why in the room.</summary>
	/// <param name="room">The room.</param>
	/// <param name="reason">Why the countdown is cancelled.</param>
	/// <param name="onlyIfStartsRound">Whether a countdown that starts no round is left running.</param>
	/// <param name="ct">A token that cancels the announcement.</param>
	/// <returns><see langword="true" /> when a countdown was cancelled.</returns>
	public async Task<bool> CancelAsync(Room room, CountdownCancelReason reason, bool onlyIfStartsRound,
		CancellationToken ct)
	{
		if (!_running.TryGetValue(room, out var running) || (onlyIfStartsRound && !running.Countdown.StartsRound) ||
		    !_running.TryRemove(new KeyValuePair<Room, Running>(room, running)))
			return false;

		running.Dispose();
		var reply = reason switch
		{
			CountdownCancelReason.Aborted => AnnouncementReplies.CountdownCancelled,
			CountdownCancelReason.SettingsChanged => AnnouncementReplies.SettingsChangedCountdownCancelled,
			CountdownCancelReason.RoundStarted => AnnouncementReplies.CountdownCancelledByRoundStart,
			CountdownCancelReason.RoundAborted => AnnouncementReplies.CountdownCancelledByRoundAbort,
			_ => null
		};
		if (reply is not null) await SayAsync(room, replies.Text(reply), ct);
		return true;
	}

	/// <summary>Wraps work that a timer starts; the work reports its own failures.</summary>
	private static TimerCallback InBackground(Func<Task> work)
	{
		return state => { _ = work(); };
	}

	private async Task TickAsync(Room room, Running running, TimeSpan remaining)
	{
		try
		{
			if (!IsCurrent(room, running)) return;

			var seconds = (int)Math.Round(remaining.TotalSeconds);
			await SayAsync(room, running.Countdown.StartsRound
				? replies.Text(AnnouncementReplies.CountdownTickedForRound, new { seconds })
				: replies.Text(AnnouncementReplies.CountdownTicked, new { seconds }), CancellationToken.None);
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "The countdown of room {RoomId} could not tick", room.Id);
		}
	}

	private async Task ElapseAsync(Room room, Running running)
	{
		try
		{
			if (!_running.TryRemove(new KeyValuePair<Room, Running>(room, running))) return;

			running.Dispose();
			var countdown = running.Countdown;
			if (!countdown.StartsRound)
			{
				await SayAsync(room, replies.Text(AnnouncementReplies.CountdownElapsed), CancellationToken.None);
				return;
			}

			if (!countdown.Actor.IsOpen)
			{
				await SayAsync(room, replies.Text(AnnouncementReplies.CountdownCancelled), CancellationToken.None);
				return;
			}

			var started = await sender.Send(new StartRound(countdown.Actor, room), CancellationToken.None);
			var reply = started == RoomOutcome.Ok
				? AnnouncementReplies.RoundStartedByCountdown
				: started switch
				{
					RoomOutcome.NoBeatmap => AnnouncementReplies.CountdownElapsedForRound,
					RoomOutcome.InProgress or RoomOutcome.RoomClosed => null,
					_ => AnnouncementReplies.CountdownCancelled
				};
			if (reply is not null) await SayAsync(room, replies.Text(reply), CancellationToken.None);
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "The countdown of room {RoomId} could not finish", room.Id);
		}
	}

	private bool IsCurrent(Room room, Running running)
	{
		return _running.TryGetValue(room, out var current) && ReferenceEquals(current, running);
	}

	private Task SayAsync(Room room, string text, CancellationToken ct)
	{
		return room.IsClosed
			? Task.CompletedTask
			: server.SayAsync(channels.Of(room), text, ct: ct);
	}

	/// <summary>A countdown and the timers that announce its marks and its end.</summary>
	private sealed class Running(RoomCountdown countdown) : IDisposable
	{
		/// <summary>The countdown.</summary>
		public RoomCountdown Countdown { get; } = countdown;

		/// <summary>The timers that announce the marks and the end.</summary>
		public ITimer[] Timers { get; set; } = [];

		/// <inheritdoc />
		public void Dispose()
		{
			foreach (var timer in Timers) timer.Dispose();
		}
	}
}