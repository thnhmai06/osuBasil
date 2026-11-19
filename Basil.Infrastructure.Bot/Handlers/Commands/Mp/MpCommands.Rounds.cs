using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Bot.Objects;
using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Infrastructure.Bot.Handlers.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp start / timer / aborttimer / abort ────────────────────────────────────────────

	private async Task<bool> StartAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (args.Count > 0 && int.TryParse(args[0], out var seconds) && seconds > 0)
			return await StartCountdownAsync(context, room, seconds, true, ct);

		var outcome = await sender.Send(new StartRound(context.Actor, room), ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.MatchStarted, ct);
		return true;
	}

	private async Task<bool> TimerAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var seconds = DefaultTimerSeconds;
		if (args.Count > 0)
			if (!int.TryParse(args[0], out seconds) || seconds <= 0)
			{
				await context.Reply(MpReplies.TimerUsage, ct);
				return false;
			}

		return await StartCountdownAsync(context, room, seconds, false, ct);
	}

	private async Task<bool> AbortTimerAsync(CommandContext context, Room room,
		CancellationToken ct)
	{
		var actor = context.Actor;
		var outcome = await sender.Send(new CancelRoomCountdown(actor, room), ct);
		if (outcome == RoomActionResult.NotInProgress)
		{
			await context.Reply(MpReplies.NoCountdownRunning, ct);
			return false;
		}

		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		// The room announces the abort itself, whoever cancelled the countdown.
		return true;
	}

	private async Task<bool> StartCountdownAsync(CommandContext context, Room room, int seconds,
		bool startsRound,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (seconds < 1 || TimeSpan.FromSeconds(seconds) > RoomCountdown.Max)
		{
			await context.Reply(MpReplies.CountdownOutOfRange, ct);
			return false;
		}

		var outcome = await sender.Send(new StartRoomCountdown(actor, room, TimeSpan.FromSeconds(seconds), startsRound),
			ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		return true;
	}

	private async Task<bool> AbortAsync(CommandContext context, Room room, CancellationToken ct)
	{
		var outcome = await sender.Send(new AbortRound(context.Actor, room), ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.AbortedMatch, ct);
		return true;
	}
}