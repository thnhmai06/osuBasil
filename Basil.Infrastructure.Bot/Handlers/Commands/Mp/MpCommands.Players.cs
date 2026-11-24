using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Bot.Commands;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Users;
using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Infrastructure.Bot.Handlers.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp move ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> MoveAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 2 || !int.TryParse(args[^1], out var slot) || slot is < 1 or > RoomRules.MaxPlayers)
		{
			await context.Reply(BotReply.MpMoveUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args.Take(args.Count - 1));
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new MovePlayer(actor, room, target.Id, slot), ct);
		if (outcome != RoomTargetResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpMovedToSlot, new { player = target.Value.Name, slot }, ct);
		return true;
	}

	// ── !mp host / clearhost ──────────────────────────────────────────────────────────────

	private async Task<bool> HostAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(BotReply.MpHostUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new SetRoomHost(actor, room, target.Id), ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpChangedMatchHost, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> ClearHostAsync(CommandContext context, Room room,
		CancellationToken ct)
	{
		var outcome = await sender.Send(new SetRoomHost(context.Actor, room, null), ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpClearedMatchHost, ct);
		return true;
	}

	// ── !mp invite ───────────────────────────────────────────────────────────────────────

	private async Task<bool> InviteAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(BotReply.MpInviteUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new InvitePlayer(actor, room, target.Id), ct);
		if (outcome != RoomInviteResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpInvitedToRoom, new { player = target.Value.Name }, ct);
		return true;
	}

	// ── !mp kick / ban / unban ────────────────────────────────────────────────────────────

	private async Task<bool> KickAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(BotReply.MpKickUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new KickPlayer(actor, room, target.Id), ct);
		if (outcome != RoomTargetResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpKickedFromMatch, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> BanAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(BotReply.MpBanUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new BanPlayer(actor, room, target.Id), ct);
		if (outcome != RoomTargetResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpBannedPlayerFromMatch, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> UnbanAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(BotReply.MpUnbanUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new UnbanPlayer(actor, room, target.Id), ct);
		if (outcome != RoomTargetResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(BotReply.MpUnbannedFromMatch, new { player = target.Value.Name }, ct);
		return true;
	}

	// ── !mp team ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetTeamAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 2)
		{
			await context.Reply(BotReply.MpTeamUsage, ct);
			return false;
		}

		var teamArg = args[^1].ToLowerInvariant();
		if (teamArg is not ("red" or "blue"))
		{
			await context.Reply(BotReply.MpTeamUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args.Take(args.Count - 1));
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return false;
		}

		var team = teamArg == "red" ? GameTeam.Red : GameTeam.Blue;
		var outcome = await sender.Send(new SetPlayerTeam(actor, room, target.Id, team), ct);
		if (outcome != RoomTargetResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		var teamDisplay = char.ToUpperInvariant(teamArg[0]) + teamArg[1..];
		await context.Reply(BotReply.MpMovedToTeam, new { player = target.Value.Name, team = teamDisplay },
			ct);
		return true;
	}
}