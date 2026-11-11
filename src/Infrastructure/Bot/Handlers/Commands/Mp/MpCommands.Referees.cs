using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;

namespace Basil.Infrastructure.Bot.Handlers.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp addref / removeref / listrefs ────────────────────────────────────────────────

	private async Task<bool> AddRefereeAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.AddRefUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new AddReferee(actor, room, UserReference.ById(target.Id)), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.AddedReferee, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> RemoveRefereeAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.RemoveRefUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new RemoveReferee(actor, room, UserReference.ById(target.Id)), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.RemovedReferee, new { player = target.Value.Name }, ct);
		return true;
	}

	private static async Task<bool> ListRefsAsync(CommandContext context, Room room,
		CancellationToken ct)
	{
		if (room.Authority.Referees.Count == 0)
		{
			await context.Reply(MpReplies.NoReferees, ct);
			return true;
		}

		var lines = room.Authority.Referees
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Say(context.Replies.Text(MpReplies.MatchReferees) + "\n" + string.Join('\n', lines),
			ct);
		return true;
	}

	// ── !mp banlist ──────────────────────────────────────────────────────────────────────

	private static async Task<bool> BanListAsync(CommandContext context, Room room, CancellationToken ct)
	{
		if (room.Members.Banned.Count == 0)
		{
			await context.Reply(MpReplies.NoBannedPlayers, ct);
			return true;
		}

		var lines = room.Members.Banned
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Say(context.Replies.Text(MpReplies.MatchBans) + "\n" + string.Join('\n', lines),
			ct);
		return true;
	}
}