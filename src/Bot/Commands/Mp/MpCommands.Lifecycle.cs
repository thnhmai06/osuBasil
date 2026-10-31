using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Bot.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp make / makeprivate ───────────────────────────────────────────────────────────

	private async Task<bool> MakeAsync(CommandContext context, IReadOnlyList<string> args, bool isPrivate,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		var name = args.Count > 0
			? string.Join(' ', args)
			: context.Replies.Text(MpReplies.DefaultRoomName, new { name = context.Sender.Value.Name });
		if (name.Length > MaxRoomNameLength) name = name[..MaxRoomNameLength];

		var (room, outcome) = await lobby.ScopeTo(by).OpenRoomAsync(name, "", true, isPrivate,
			cancellationToken: cancellationToken);
		if (!outcome.IsOk || room is null)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(MpReplies.CreatedMatch,
			new { room = room.Id, name = room.Settings.Name, privacy = isPrivate ? "private" : "public" },
			cancellationToken);
		return true;
	}

	// ── !mp join ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> JoinAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1 || !int.TryParse(args[0], out var roomId))
		{
			await context.Reply(MpReplies.JoinUsage, cancellationToken);
			return false;
		}

		var room = lobby.Find(roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
			return false;
		}

		var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : string.Empty;
		var outcome = await room.ScopeTo(by).Members.JoinAsync(password, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(MpReplies.JoinedMatch, new { room = roomId, name = room.Settings.Name }, cancellationToken);
		return true;
	}

	// ── !mp in ───────────────────────────────────────────────────────────────────────────

	private async Task<bool> InAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (context.Channel is not null)
		{
			await context.Reply(MpReplies.MpInDmOnly, cancellationToken);
			return false;
		}

		if (args.Count == 0)
		{
			targets.Clear(context.Sender.Id);
			await context.Reply(MpReplies.NotScopedToAnyMatch, cancellationToken);
			return true;
		}

		if (!int.TryParse(args[0], out var roomId))
		{
			await context.Reply(MpReplies.InUsage, cancellationToken);
			return false;
		}

		var room = lobby.Find(roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithHashId, new { room = roomId }, cancellationToken);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(MpReplies.NowTargetingMatch, new { room = roomId, name = room.Settings.Name },
			cancellationToken);
		return true;
	}

	// ── !mp close ────────────────────────────────────────────────────────────────────────

	private static async Task<bool> CloseAsync(CommandContext context, Room room, CancellationToken cancellationToken)
	{
		var outcome = await room.ScopeTo(context.By).CloseAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ClosedMatch, cancellationToken);
		return true;
	}
}