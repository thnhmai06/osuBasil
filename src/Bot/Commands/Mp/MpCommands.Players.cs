using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Domain.Mechanics;

namespace Basil.Bot.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp move ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> MoveAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 2 || !int.TryParse(args[^1], out var slot))
		{
			await context.Reply(MpReplies.MoveUsage, cancellationToken);
			return false;
		}

		slot = Math.Clamp(slot, 1, MaxSlotIndex);
		var rawTarget = string.Join(' ', args.Take(args.Count - 1));
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Slots.MoveAsync(target, slot, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.MovedToSlot, new { player = target.Value.Name, slot }, cancellationToken);
		return true;
	}

	// ── !mp host / clearhost ──────────────────────────────────────────────────────────────

	private async Task<bool> HostAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.HostUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, cancellationToken);
			return false;
		}

		var host = registry.Online.GetValueOrDefault(target)?.Bancho;
		var outcome = host is not { IsOpen: true }
			? RoomOutcome.TargetOffline
			: await room.ScopeTo(by).Authority.SetHostAsync(host, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ChangedMatchHost, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	private static async Task<bool> ClearHostAsync(CommandContext context, Room room, CancellationToken cancellationToken)
	{
		var outcome = await room.ScopeTo(context.By).Authority.SetHostAsync(null, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ClearedMatchHost, cancellationToken);
		return true;
	}

	// ── !mp invite ───────────────────────────────────────────────────────────────────────

	private async Task<bool> InviteAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.InviteUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.InviteRequiresClient, cancellationToken);
			return false;
		}

		var targetUser = registry.Online.GetValueOrDefault(target);
		var outcome = targetUser is null
			? RoomOutcome.TargetOffline
			: await room.ScopeTo(by).Members.InviteAsync(targetUser, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.InvitedToRoom, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	// ── !mp kick / ban / unban ────────────────────────────────────────────────────────────

	private async Task<bool> KickAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.KickUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotRegistered, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Members.KickAsync(target, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.KickedFromMatch, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	private async Task<bool> BanAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.BanUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotRegistered, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Members.BanAsync(target, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.BannedPlayerFromMatch, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	private async Task<bool> UnbanAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.UnbanUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotRegistered, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Members.UnbanAsync(target, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.UnbannedFromMatch, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	// ── !mp team ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetTeamAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 2)
		{
			await context.Reply(MpReplies.TeamUsage, cancellationToken);
			return false;
		}

		var teamArg = args[^1].ToLowerInvariant();
		if (teamArg is not ("red" or "blue"))
		{
			await context.Reply(MpReplies.TeamUsage, cancellationToken);
			return false;
		}

		var rawTarget = string.Join(' ', args.Take(args.Count - 1));
		var target = await ResolveUserAsync(rawTarget, cancellationToken);
		if (target is null)
		{
			await context.Reply(MpReplies.UserNotFound, cancellationToken);
			return false;
		}

		var team = teamArg == "red" ? GameTeam.Red : GameTeam.Blue;
		var outcome = await room.ScopeTo(by).Slots.SetTeamAsync(target, team, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		var teamDisplay = char.ToUpperInvariant(teamArg[0]) + teamArg[1..];
		await context.Reply(MpReplies.MovedToTeam, new { player = target.Value.Name, team = teamDisplay },
			cancellationToken);
		return true;
	}
}