using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Infrastructure.Bot.Handlers.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp make / makeprivate ───────────────────────────────────────────────────────────

	private async Task<bool> MakeAsync(CommandContext context, IReadOnlyList<string> args, bool isPrivate,
		CancellationToken ct)
	{
		var actor = context.Actor;
		var name = args.Count > 0
			? string.Join(' ', args)
			: context.Replies.Text(MpReplies.DefaultRoomName, new { name = context.Sender.Value.Name });
		if (name.Length > MaxRoomNameLength) name = name[..MaxRoomNameLength];

		var (room, outcome) =
			await sender.Send(new OpenRoom(actor, name, "", RoomSource.Api, isPrivate), ct);
		if (!outcome.IsOk || room is null)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(MpReplies.CreatedMatch,
			new { room = room.Id, name = room.Settings.Name, privacy = isPrivate ? "private" : "public" },
			ct);
		return true;
	}

	// ── !mp join ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> JoinAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var actor = context.Actor;
		if (args.Count < 1 || !int.TryParse(args[0], out var roomId))
		{
			await context.Reply(MpReplies.JoinUsage, ct);
			return false;
		}

		var room = lobby.Find(roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, ct);
			return false;
		}

		var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : string.Empty;
		var outcome = await sender.Send(new JoinRoom(actor, room, password), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(MpReplies.JoinedMatch, new { room = roomId, name = room.Settings.Name }, ct);
		return true;
	}

	// ── !mp in ───────────────────────────────────────────────────────────────────────────

	private async Task<bool> InAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (context.Channel is not null)
		{
			await context.Reply(MpReplies.MpInDmOnly, ct);
			return false;
		}

		if (args.Count == 0)
		{
			targets.Clear(context.Sender.Id);
			await context.Reply(MpReplies.NotScopedToAnyMatch, ct);
			return true;
		}

		if (!int.TryParse(args[0], out var roomId))
		{
			await context.Reply(MpReplies.InUsage, ct);
			return false;
		}

		var room = lobby.Find(roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithHashId, new { room = roomId }, ct);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(MpReplies.NowTargetingMatch, new { room = roomId, name = room.Settings.Name },
			ct);
		return true;
	}

	// ── !mp close ────────────────────────────────────────────────────────────────────────

	private async Task<bool> CloseAsync(CommandContext context, Room room, CancellationToken ct)
	{
		var outcome = await sender.Send(new CloseRoom(context.Actor, room), ct);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		await context.Reply(MpReplies.ClosedMatch, ct);
		return true;
	}
}