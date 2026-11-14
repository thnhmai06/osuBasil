using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Bot.Objects;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;

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
		if (outcome != RoomOutcome.Ok)
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
		if (outcome == RoomOutcome.NotInProgress)
		{
			await context.Reply(MpReplies.NoCountdownRunning, ct);
			return false;
		}

		if (outcome != RoomOutcome.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		// The room announces the abort itself, whoever cancelled the countdown.
		return true;
	}

	/// <summary>Starts a countdown after checking what the server no longer checks for it.</summary>
	/// <param name="context">Who sent the command and how to answer it.</param>
	/// <param name="room">The room the countdown runs in.</param>
	/// <param name="seconds">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the round starts when the countdown ends.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when it started; otherwise the reason was replied.</returns>
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
		if (outcome != RoomOutcome.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		return true;
	}

	private async Task<bool> AbortAsync(CommandContext context, Room room, CancellationToken ct)
	{
		var outcome = await sender.Send(new AbortRound(context.Actor, room), ct);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.AbortedMatch, ct);
		return true;
	}
}