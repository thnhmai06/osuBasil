using System.Collections.Frozen;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Bot.Registry;
using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers.Commands.Mp;

/// <summary>
///     Executes the <c>!mp</c> chat command surface against the Basil server.
/// </summary>
/// <remarks>
///     Operations run on the lobby and rooms scoped to the connection of the user who typed the command; the server
///     enforces authority and returns a <see cref="RoomOutcome" />, which is mapped to a user-visible reply by
///     <see cref="MpReplies.Describe" />.
/// </remarks>
internal sealed partial class MpCommands(
	Lobby lobby,
	UserRegistry registry,
	IUserRepository users,
	IBeatmapRepository beatmaps,
	MpTargets targets,
	ISender sender)
{
	/// <summary>The maximum length of a room name as accepted by the osu! client.</summary>
	private const int MaxRoomNameLength = 50;

	/// <summary>Default <c>!mp timer</c> length in seconds.</summary>
	private const int DefaultTimerSeconds = 30;

	/// <summary>The <c>!mp</c> subcommands that reject being chained on a <c>;</c> or <c>&amp;&amp;</c> line.</summary>
	private static readonly FrozenSet<string> NonChainableSubcommands =
		new[] { "", "help", "make", "makeprivate", "in", "join" }
			.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Runs a <c>!mp &lt;subcommand&gt; [args...]</c> command for the sender.
	/// </summary>
	/// <param name="context">Who sent the command, where it was sent, and how to answer it.</param>
	/// <param name="args">The tokens after <c>!mp</c>; <paramref name="args" />[0] is the subcommand.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the command was recognized and the server reported success;
	///     <see langword="false" /> for usage errors, unknown subcommands, and any non-<see cref="RoomOutcome.Ok" />
	///     result from the server.
	/// </returns>
	public async Task<bool> ExecuteAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var subcommand = args.Count > 0 ? args[0] : "";
		var subArgs = args.Count > 1 ? args.Skip(1).ToArray() : Array.Empty<string>();

		if (subcommand.Equals("help", StringComparison.OrdinalIgnoreCase) || subcommand.Length == 0)
		{
			await context.SayPrivately(CommandCatalog.Lines(context.Replies, "mp"), ct);
			return true;
		}

		return subcommand.ToLowerInvariant() switch
		{
			"make" => await MakeAsync(context, subArgs, false, ct),
			"makeprivate" => await MakeAsync(context, subArgs, true, ct),
			"join" => await JoinAsync(context, subArgs, ct),
			"in" => await InAsync(context, subArgs, ct),
			_ => await ScopedAsync(context, subcommand, subArgs, ct)
		};
	}

	/// <summary>
	///     Reports whether a <c>!mp &lt;subcommand&gt;</c> segment is allowed inside a
	///     <c>;</c> / <c>&amp;&amp;</c> chain.
	/// </summary>
	/// <param name="subcommand">The subcommand name as written by the user, case-insensitive.</param>
	/// <returns>
	///     <see langword="false" /> for <c>help</c>, <c>make</c>, <c>makeprivate</c>, <c>in</c>, <c>join</c>, and the bare
	///     prefix with no subcommand; <see langword="true" /> otherwise.
	/// </returns>
	public static bool IsChainable(string subcommand)
	{
		return !NonChainableSubcommands.Contains(subcommand);
	}

	private async Task<bool> ScopedAsync(CommandContext context, string subcommand, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var room = ResolveRoom(context);
		if (room is null)
		{
			await context.Reply(MpReplies.NotInARoom, ct);
			return false;
		}

		context = NameRoom(context, room.Id);

		return subcommand.ToLowerInvariant() switch
		{
			"settings" => await SettingsAsync(context, room, ct),
			"lock" => await SetLockedAsync(context, room, true, ct),
			"unlock" => await SetLockedAsync(context, room, false, ct),
			"private" => await PrivateAsync(context, room, args, ct),
			"size" => await SetSizeAsync(context, room, args, ct),
			"move" => await MoveAsync(context, room, args, ct),
			"host" => await HostAsync(context, room, args, ct),
			"clearhost" => await ClearHostAsync(context, room, ct),
			"name" => await SetNameAsync(context, room, args, ct),
			"password" => await SetPasswordAsync(context, room, args, ct),
			"invite" => await InviteAsync(context, room, args, ct),
			"addref" => await AddRefereeAsync(context, room, args, ct),
			"removeref" => await RemoveRefereeAsync(context, room, args, ct),
			"listrefs" => await ListRefsAsync(context, room, ct),
			"banlist" => await BanListAsync(context, room, ct),
			"team" => await SetTeamAsync(context, room, args, ct),
			"map" => await SetMapAsync(context, room, args, ct),
			"mods" => await SetModsAsync(context, room, args, ct),
			"set" => await SetAsync(context, room, args, ct),
			"start" => await StartAsync(context, room, args, ct),
			"timer" => await TimerAsync(context, room, args, ct),
			"aborttimer" => await AbortTimerAsync(context, room, ct),
			"abort" => await AbortAsync(context, room, ct),
			"kick" => await KickAsync(context, room, args, ct),
			"ban" => await BanAsync(context, room, args, ct),
			"unban" => await UnbanAsync(context, room, args, ct),
			"close" => await CloseAsync(context, room, ct),
			_ => await UnknownSubcommandAsync(context, subcommand, ct)
		};
	}

	/// <summary>
	///     Answers a room-scoped command with the room named on every line when the reply is not posted in
	///     that room's own channel.
	/// </summary>
	/// <param name="context">The context the command was sent with.</param>
	/// <param name="roomId">The room the command resolved to.</param>
	/// <returns>
	///     A context whose replies posted outside <c>#mp_{roomId}</c>
	///     prefix each non-empty line with <see cref="MpReplies.RoomPrefix" />; a private reply always goes to a
	///     direct message, so <see cref="CommandContext.ReplyPrivately(Reply, CancellationToken)" /> is always prefixed.
	/// </returns>
	private static CommandContext NameRoom(CommandContext context, int roomId)
	{
		var prefix = context.Replies.Text(MpReplies.RoomPrefix, new { room = roomId });

		return context with
		{
			Say = context.Channel == RoomChannel.NameFor(roomId)
				? context.Say
				: (text, ct) => context.Say(NameLines(text), ct),
			SayPrivately = (text, ct) => context.SayPrivately(NameLines(text), ct)
		};

		string NameLines(string text)
		{
			return string.Join('\n',
				text.Split('\n').Select(line => line.Length == 0 ? line : prefix + line));
		}
	}

	// ── scope resolution ─────────────────────────────────────────────────────────────────

	/// <summary>Finds the room a room-scoped command applies to.</summary>
	/// <param name="context">The context the command was sent with.</param>
	/// <returns>
	///     The room of the channel the command came from, else the room the sender last named with <c>!mp in</c>, else
	///     the room the sender plays in; <see langword="null" /> when the sender is in none of them.
	/// </returns>
	private Room? ResolveRoom(CommandContext context)
	{
		var actor = context.Actor;

		// (a) Channel takes precedence: if sent in #mp_{id}, use that room
		if (context.Channel is not null && RoomChannel.TryParseName(context.Channel, out var channelRoomId))
		{
			var channelRoom = lobby.Find(channelRoomId);
			if (channelRoom is not null) return channelRoom;
		}

		// (b) PM scope: !mp in <id> sets a target room
		var targeted = targets.Get(context.Sender.Id);
		if (targeted is not null)
		{
			// A room id is reused after a room closes, so the remembered room must still be the open room.
			if (ReferenceEquals(lobby.Find(targeted.Id), targeted)) return targeted;
			targets.Clear(context.Sender.Id);
		}

		// (c) Seated room: where the sender is currently playing
		var seated = actor.User.Stable is { } client ? lobby.RoomOf(client) : null;
		return seated;
	}

	// ── unknown subcommand ───────────────────────────────────────────────────────────────

	private static async Task<bool> UnknownSubcommandAsync(CommandContext context, string subcommand,
		CancellationToken ct)
	{
		await context.Reply(MpReplies.UnknownMpSubcommand, new { subcommand }, ct);
		return false;
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────

	private async ValueTask<UserRecord?> ResolveUserAsync(string raw, CancellationToken ct)
	{
		if (raw.StartsWith('#') && int.TryParse(raw[1..], out var id))
			return await users.GetAsync(id, ct);
		return await users.GetByNameAsync(raw, ct);
	}
}