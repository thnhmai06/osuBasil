using Basil.Application.Runtime.Multiplayer.Rooms;

namespace Basil.Bot.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp start / timer / aborttimer / abort ────────────────────────────────────────────

	private static async Task<bool> StartAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count > 0 && int.TryParse(args[0], out var seconds) && seconds > 0)
			return await StartCountdownAsync(context, room, seconds, true, cancellationToken);

		var outcome = await room.ScopeTo(context.By).Rounds.StartAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.MatchStarted, cancellationToken);
		return true;
	}

	private static async Task<bool> TimerAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var seconds = DefaultTimerSeconds;
		if (args.Count > 0)
			if (!int.TryParse(args[0], out seconds) || seconds <= 0)
			{
				await context.Reply(MpReplies.TimerUsage, cancellationToken);
				return false;
			}

		return await StartCountdownAsync(context, room, seconds, false, cancellationToken);
	}

	private static async Task<bool> AbortTimerAsync(CommandContext context, Room room, CancellationToken cancellationToken)
	{
		var by = context.By;
		var outcome = await room.ScopeTo(by).Rounds.CancelCountdownAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.CountdownAborted, cancellationToken);
		return true;
	}

	/// <summary>Starts a countdown after checking what the server no longer checks for it.</summary>
	/// <param name="context">Who sent the command and how to answer it.</param>
	/// <param name="room">The room the countdown runs in.</param>
	/// <param name="seconds">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the round starts when the countdown ends.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when it started; otherwise the reason was replied.</returns>
	private static async Task<bool> StartCountdownAsync(CommandContext context, Room room, int seconds, bool startsRound,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (seconds < 1 || seconds > 3600)
		{
			await context.Reply(MpReplies.CountdownOutOfRange, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Rounds.StartCountdownAsync(TimeSpan.FromSeconds(seconds), startsRound,
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		return true;
	}

	private static async Task<bool> AbortAsync(CommandContext context, Room room, CancellationToken cancellationToken)
	{
		var outcome = await room.ScopeTo(context.By).Rounds.AbortAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.AbortedMatch, cancellationToken);
		return true;
	}
}