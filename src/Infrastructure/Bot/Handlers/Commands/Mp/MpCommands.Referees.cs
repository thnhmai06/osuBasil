using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Infrastructure.Bot.Handlers.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp addref / removeref / listrefs ────────────────────────────────────────────────

	private async Task<bool> AddRefereeAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.AddRefUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, cancellationToken);
			return false;
		}

		var outcome = await sender.Send(new AddReferee(actor, room, target), cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.AddedReferee, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	private async Task<bool> RemoveRefereeAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.RemoveRefUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, cancellationToken);
			return false;
		}

		var outcome = await sender.Send(new RemoveReferee(actor, room, target), cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.RemovedReferee, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	private static async Task<bool> ListRefsAsync(CommandContext context, Room room,
		CancellationToken cancellationToken)
	{
		if (room.Authority.Referees.Count == 0)
		{
			await context.Reply(MpReplies.NoReferees, cancellationToken);
			return true;
		}

		var lines = room.Authority.Referees
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Say(context.Replies.Text(MpReplies.MatchReferees) + "\n" + string.Join('\n', lines),
			cancellationToken);
		return true;
	}

	// ── !mp banlist ──────────────────────────────────────────────────────────────────────

	private static async Task<bool> BanListAsync(CommandContext context, Room room, CancellationToken cancellationToken)
	{
		if (room.Members.Banned.Count == 0)
		{
			await context.Reply(MpReplies.NoBannedPlayers, cancellationToken);
			return true;
		}

		var lines = room.Members.Banned
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Say(context.Replies.Text(MpReplies.MatchBans) + "\n" + string.Join('\n', lines),
			cancellationToken);
		return true;
	}
}