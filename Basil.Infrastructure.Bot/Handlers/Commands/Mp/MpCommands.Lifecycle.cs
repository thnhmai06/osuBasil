using Basil.Application.Bot.Replies;
using Basil.Application.Bot.Commands;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

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
			: context.Replies.Text(BotReply.MpDefaultRoomName, new { name = context.Sender.Value.Name });

		var (room, outcome) =
			await sender.Send(new OpenRoom(actor, name, "", RoomSource.Api, isPrivate), ct);
		if (outcome != LobbyResult.Ok || room is null)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(BotReply.MpCreatedMatch,
			new
			{
				room = room.Id, name = room.Settings.Name, privacy = isPrivate ? "private" : "public",
				channel = RoomChannelRules.NameFor(room.Id)
			},
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
			await context.Reply(BotReply.MpJoinUsage, ct);
			return false;
		}

		var room = lobby.Find(roomId);
		if (room is null)
		{
			await context.Reply(BotReply.MpNoActiveRoomWithId, new { room = roomId }, ct);
			return false;
		}

		var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : string.Empty;
		var outcome = await sender.Send(new JoinRoom(actor, room, password), ct);
		if (outcome != RoomJoinResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(BotReply.MpJoinedMatch, new { room = roomId, name = room.Settings.Name }, ct);
		return true;
	}

	// ── !mp in ───────────────────────────────────────────────────────────────────────────

	private async Task<bool> InAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (context.Channel is not null)
		{
			await context.Reply(BotReply.MpMpInDmOnly, ct);
			return false;
		}

		if (args.Count == 0)
		{
			targets.Clear(context.Sender.Id);
			await context.Reply(BotReply.MpNotScopedToAnyMatch, ct);
			return true;
		}

		if (!int.TryParse(args[0], out var roomId))
		{
			await context.Reply(BotReply.MpInUsage, ct);
			return false;
		}

		var room = lobby.Find(roomId);
		if (room is null || !room.CanBeViewedBy(context.Actor))
		{
			await context.Reply(BotReply.MpNoActiveRoomWithHashId, new { room = roomId }, ct);
			return false;
		}

		targets.Set(context.Sender.Id, room);
		await context.Reply(BotReply.MpNowTargetingMatch, new { room = roomId, name = room.Settings.Name },
			ct);
		return true;
	}

	// ── !mp close ────────────────────────────────────────────────────────────────────────

	private async Task<bool> CloseAsync(CommandContext context, Room room, CancellationToken ct)
	{
		var outcome = await sender.Send(new CloseRoom(context.Actor, room), ct);
		if (outcome != RoomActionResult.Ok)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), ct);
			return false;
		}

		// The room channel closes with the room, so the typist is told privately.
		await context.ReplyPrivately(BotReply.MpClosedMatch, ct);
		return true;
	}
}