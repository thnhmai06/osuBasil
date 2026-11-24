using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Bot.Commands;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

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
			await context.Reply(BotReply.MpAddRefUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new AddReferee(actor, room, target.Id), ct);
		if (outcome != RoomTargetResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpAddedReferee, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> RemoveRefereeAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(BotReply.MpRemoveRefUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new RemoveReferee(actor, room, target.Id), ct);
		if (outcome != RoomTargetResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpRemovedReferee, new { player = target.Value.Name }, ct);
		return true;
	}

	private static async Task<bool> ListRefsAsync(CommandContext context, Room room,
		CancellationToken ct)
	{
		if (room.Referees.Count == 0)
		{
			await context.Reply(BotReply.MpNoReferees, ct);
			return true;
		}

		var lines = room.Referees
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Say(context.Replies.Text(BotReply.MpMatchReferees) + "\n" + string.Join('\n', lines),
			ct);
		return true;
	}

	// ── !mp banlist ──────────────────────────────────────────────────────────────────────

	private static async Task<bool> BanListAsync(CommandContext context, Room room, CancellationToken ct)
	{
		if (room.Members.Banned.Count == 0)
		{
			await context.Reply(BotReply.MpNoBannedPlayers, ct);
			return true;
		}

		var lines = room.Members.Banned
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Say(context.Replies.Text(BotReply.MpMatchBans) + "\n" + string.Join('\n', lines),
			ct);
		return true;
	}
}