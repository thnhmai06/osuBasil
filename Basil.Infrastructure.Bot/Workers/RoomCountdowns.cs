using Basil.Application.Shared.Replies;
using System.Collections.Concurrent;
using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Objects;
using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Microsoft.Extensions.Logging;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Infrastructure.Bot.Workers;

internal enum CountdownCancelReason
{
	Aborted = 1,

	SettingsChanged,

	RoundStarted,

	RoundAborted,

	RoomClosed
}

internal sealed class RoomCountdowns(
	TimeProvider clock,
	ISender sender,
	Server server,
	RoomChannels channels,
	IReplies<BotReply> replies,
	ILogger<RoomCountdowns> logger)
{
	private readonly ConcurrentDictionary<Room, Running> _running = new();

	public int Count => _running.Count;

	public bool IsRunning(Room room)
	{
		return _running.ContainsKey(room);
	}

	public async Task<RoomActionResult> StartAsync(Session actor, Room room, TimeSpan length, bool startsRound,
		CancellationToken ct)
	{
		if (RoomCountdown.Refusal(room, length, startsRound) is { } refusal) return refusal;

		var running = new Running(new RoomCountdown(actor, length, startsRound, clock.GetUtcNow()));
		running.Timers =
		[
			.. CountdownMarks.For(length, startsRound).Select(mark =>
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
			await SayAsync(room, replies.Text(BotReply.AnnounceCountdownReplaced, new { seconds = remaining }),
				ct);
		}

		var seconds = (int)length.TotalSeconds;
		await SayAsync(room, startsRound
			? replies.Text(BotReply.AnnounceCountdownStartedForRound, new { seconds })
			: replies.Text(BotReply.AnnounceCountdownStarted, new { seconds }), ct);
		return RoomActionResult.Ok;
	}

	public async Task<bool> CancelAsync(Room room, CountdownCancelReason reason, bool onlyIfStartsRound,
		CancellationToken ct)
	{
		if (!_running.TryGetValue(room, out var running) || (onlyIfStartsRound && !running.Countdown.StartsRound) ||
		    !_running.TryRemove(new KeyValuePair<Room, Running>(room, running)))
			return false;

		running.Dispose();
		var reply = reason switch
		{
			CountdownCancelReason.Aborted => BotReply.AnnounceCountdownCancelled,
			CountdownCancelReason.SettingsChanged => BotReply.AnnounceSettingsChangedCountdownCancelled,
			CountdownCancelReason.RoundStarted => BotReply.AnnounceCountdownCancelledByRoundStart,
			CountdownCancelReason.RoundAborted => BotReply.AnnounceCountdownCancelledByRoundAbort,
			_ => (BotReply?)null
		};
		if (reply is { } text) await SayAsync(room, replies.Text(text), ct);
		return true;
	}

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
				? replies.Text(BotReply.AnnounceCountdownTickedForRound, new { seconds })
				: replies.Text(BotReply.AnnounceCountdownTicked, new { seconds }), CancellationToken.None);
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
				await SayAsync(room, replies.Text(BotReply.AnnounceCountdownElapsed), CancellationToken.None);
				return;
			}

			if (!countdown.Actor.IsOpen)
			{
				await SayAsync(room, replies.Text(BotReply.AnnounceCountdownCancelled), CancellationToken.None);
				return;
			}

			var started = await sender.Send(new StartRound(countdown.Actor, room), CancellationToken.None);
			var reply = started == RoomActionResult.Ok
				? (BotReply?)BotReply.AnnounceRoundStartedByCountdown
				: started switch
				{
					RoomActionResult.NoBeatmap => BotReply.AnnounceCountdownElapsedForRound,
					RoomActionResult.InProgress or RoomActionResult.RoomClosed => null,
					_ => BotReply.AnnounceCountdownCancelled
				};
			if (reply is { } text) await SayAsync(room, replies.Text(text), CancellationToken.None);
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

	private sealed class Running(RoomCountdown countdown) : IDisposable
	{
		public RoomCountdown Countdown { get; } = countdown;

		public ITimer[] Timers { get; set; } = [];

		public void Dispose()
		{
			foreach (var timer in Timers) timer.Dispose();
		}
	}
}