using Basil.Application.Bot.Replies;
using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Objects;
using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Chat;
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

		await context.Reply(BotReply.MpMatchStarted, ct);
		return true;
	}

	private async Task<bool> TimerAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var seconds = DefaultTimerSeconds;
		if (args.Count > 0)
			if (!int.TryParse(args[0], out seconds) || seconds <= 0)
			{
				await context.Reply(BotReply.MpTimerUsage, ct);
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
			await context.Reply(BotReply.MpNoCountdownRunning, ct);
			return false;
		}

		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		// The room channel announces the abort itself; a typist elsewhere is told here.
		if (!IsRoomChannel(context, room)) await context.Reply(BotReply.AnnounceCountdownCancelled, ct);
		return true;
	}

	private async Task<bool> StartCountdownAsync(CommandContext context, Room room, int seconds,
		bool startsRound,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (seconds < 1 || TimeSpan.FromSeconds(seconds) > RoomCountdown.Max)
		{
			await context.Reply(BotReply.MpCountdownOutOfRange, ct);
			return false;
		}

		var outcome = await sender.Send(new StartRoomCountdown(actor, room, TimeSpan.FromSeconds(seconds), startsRound),
			ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		// The room channel announces the countdown itself; a typist elsewhere is told here.
		if (!IsRoomChannel(context, room))
			await context.Reply(startsRound ? BotReply.AnnounceCountdownStartedForRound : BotReply.AnnounceCountdownStarted,
				new { seconds }, ct);
		return true;
	}

	private static bool IsRoomChannel(CommandContext context, Room room)
	{
		return context.Channel == RoomChannelRules.NameFor(room.Id);
	}

	private async Task<bool> AbortAsync(CommandContext context, Room room, CancellationToken ct)
	{
		var outcome = await sender.Send(new AbortRound(context.Actor, room), ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpAbortedMatch, ct);
		return true;
	}
}