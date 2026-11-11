using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;

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
			await context.Reply(MpReplies.MoveUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args.Take(args.Count - 1));
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, ct);
			return false;
		}

		var outcome = await sender.Send(new MovePlayer(actor, room, UserReference.ById(target.Id), slot), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.MovedToSlot, new { player = target.Value.Name, slot }, ct);
		return true;
	}

	// ── !mp host / clearhost ──────────────────────────────────────────────────────────────

	private async Task<bool> HostAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.HostUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, ct);
			return false;
		}

		var host = registry.Online.GetValueOrDefault(target)?.Stable;
		var outcome = host is not { IsOpen: true }
			? RoomOutcome.TargetOffline
			: await sender.Send(new SetRoomHost(actor, room, host), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.ChangedMatchHost, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> ClearHostAsync(CommandContext context, Room room,
		CancellationToken ct)
	{
		var outcome = await sender.Send(new SetRoomHost(context.Actor, room, null), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.ClearedMatchHost, ct);
		return true;
	}

	// ── !mp invite ───────────────────────────────────────────────────────────────────────

	private async Task<bool> InviteAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.InviteUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, ct);
			return false;
		}

		var targetUser = registry.Online.GetValueOrDefault(target);
		var outcome = targetUser is null
			? RoomOutcome.TargetOffline
			: await sender.Send(new InvitePlayer(actor, room, targetUser), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.InvitedToRoom, new { player = target.Value.Name }, ct);
		return true;
	}

	// ── !mp kick / ban / unban ────────────────────────────────────────────────────────────

	private async Task<bool> KickAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.KickUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotRegistered, ct);
			return false;
		}

		var outcome = await sender.Send(new KickPlayer(actor, room, UserReference.ById(target.Id)), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.KickedFromMatch, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> BanAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.BanUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotRegistered, ct);
			return false;
		}

		var outcome = await sender.Send(new BanPlayer(actor, room, UserReference.ById(target.Id)), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.BannedPlayerFromMatch, new { player = target.Value.Name }, ct);
		return true;
	}

	private async Task<bool> UnbanAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.UnbanUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotRegistered, ct);
			return false;
		}

		var outcome = await sender.Send(new UnbanPlayer(actor, room, UserReference.ById(target.Id)), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.UnbannedFromMatch, new { player = target.Value.Name }, ct);
		return true;
	}

	// ── !mp team ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetTeamAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 2)
		{
			await context.Reply(MpReplies.TeamUsage, ct);
			return false;
		}

		var teamArg = args[^1].ToLowerInvariant();
		if (teamArg is not ("red" or "blue"))
		{
			await context.Reply(MpReplies.TeamUsage, ct);
			return false;
		}

		var rawTarget = string.Join(' ', args.Take(args.Count - 1));
		var target = await ResolveUserAsync(rawTarget, ct);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, ct);
			return false;
		}

		var team = teamArg == "red" ? GameTeam.Red : GameTeam.Blue;
		var outcome = await sender.Send(new SetPlayerTeam(actor, room, UserReference.ById(target.Id), team), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		var teamDisplay = char.ToUpperInvariant(teamArg[0]) + teamArg[1..];
		await context.Reply(MpReplies.MovedToTeam, new { player = target.Value.Name, team = teamDisplay },
			ct);
		return true;
	}
}