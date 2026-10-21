using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Users;
using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Users;
using Basil.Bot.Replies;
using Basil.Domain.Mechanics;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Bot.Commands.Mp;

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
	MpScopes scopes,
	TimeProvider time)
{
	/// <summary>The maximum length of a room name as accepted by the osu! client.</summary>
	private const int MaxRoomNameLength = 50;

	/// <summary>The maximum slot index a player can be moved to.</summary>
	private const int MaxSlotIndex = 16;

	/// <summary>The maximum number of usable slots in a room.</summary>
	private const int MaxRoomSize = 16;

	/// <summary>Default <c>!mp timer</c> length in seconds.</summary>
	private const int DefaultTimerSeconds = 30;

	/// <summary>Matches a room channel name like <c>#mp_5</c> and captures the id.</summary>
	private static readonly Regex MpChannelRegex = MpChannelRegexPattern();

	/// <summary>The <c>!mp</c> subcommands that reject being chained on a <c>;</c> or <c>&amp;&amp;</c> line.</summary>
	private static readonly FrozenSet<string> NonChainableSubcommands =
		new[] { "", "help", "make", "makeprivate", "in", "join" }
			.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Runs a <c>!mp &lt;subcommand&gt; [args...]</c> command for the sender.
	/// </summary>
	/// <param name="context">Who sent the command, where it was sent, and how to answer it.</param>
	/// <param name="args">The tokens after <c>!mp</c>; <paramref name="args" />[0] is the subcommand.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the command was recognized and the server reported success;
	///     <see langword="false" /> for usage errors, unknown subcommands, and any non-<see cref="RoomOutcome.Ok" />
	///     result from the server.
	/// </returns>
	public async Task<bool> ExecuteAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var subcommand = args.Count > 0 ? args[0] : "";
		var subArgs = args.Count > 1 ? args.Skip(1).ToArray() : Array.Empty<string>();

		if (subcommand.Equals("help", StringComparison.OrdinalIgnoreCase) || subcommand.Length == 0)
		{
			await context.ReplyPrivately(MpReplies.HelpText, cancellationToken);
			return true;
		}

		return subcommand.ToLowerInvariant() switch
		{
			"make" => await MakeAsync(context, subArgs, false, cancellationToken),
			"makeprivate" => await MakeAsync(context, subArgs, true, cancellationToken),
			"join" => await JoinAsync(context, subArgs, cancellationToken),
			"in" => await InAsync(context, subArgs, cancellationToken),
			_ => await ScopedAsync(context, subcommand, subArgs, cancellationToken)
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
		CancellationToken cancellationToken)
	{
		var roomId = ResolveRoomId(context);
		if (roomId is null)
		{
			await context.Reply(MpReplies.NotInARoom, cancellationToken);
			return false;
		}

		context = NameRoom(context, roomId.Value);

		return subcommand.ToLowerInvariant() switch
		{
			"settings" => await SettingsAsync(context, roomId.Value, cancellationToken),
			"lock" => await SetLockedAsync(context, roomId.Value, true, cancellationToken),
			"unlock" => await SetLockedAsync(context, roomId.Value, false, cancellationToken),
			"private" => await PrivateAsync(context, roomId.Value, args, cancellationToken),
			"size" => await SetSizeAsync(context, roomId.Value, args, cancellationToken),
			"move" => await MoveAsync(context, roomId.Value, args, cancellationToken),
			"host" => await HostAsync(context, roomId.Value, args, cancellationToken),
			"clearhost" => await ClearHostAsync(context, roomId.Value, cancellationToken),
			"name" => await SetNameAsync(context, roomId.Value, args, cancellationToken),
			"password" => await SetPasswordAsync(context, roomId.Value, args, cancellationToken),
			"invite" => await InviteAsync(context, roomId.Value, args, cancellationToken),
			"addref" => await AddRefereeAsync(context, roomId.Value, args, cancellationToken),
			"removeref" => await RemoveRefereeAsync(context, roomId.Value, args, cancellationToken),
			"listrefs" => await ListRefsAsync(context, roomId.Value, cancellationToken),
			"banlist" => await BanListAsync(context, roomId.Value, cancellationToken),
			"team" => await SetTeamAsync(context, roomId.Value, args, cancellationToken),
			"map" => await SetMapAsync(context, roomId.Value, args, cancellationToken),
			"mods" => await SetModsAsync(context, roomId.Value, args, cancellationToken),
			"set" => await SetAsync(context, roomId.Value, args, cancellationToken),
			"start" => await StartAsync(context, roomId.Value, args, cancellationToken),
			"timer" => await TimerAsync(context, roomId.Value, args, cancellationToken),
			"aborttimer" => await AbortTimerAsync(context, roomId.Value, cancellationToken),
			"abort" => await AbortAsync(context, roomId.Value, cancellationToken),
			"kick" => await KickAsync(context, roomId.Value, args, cancellationToken),
			"ban" => await BanAsync(context, roomId.Value, args, cancellationToken),
			"unban" => await UnbanAsync(context, roomId.Value, args, cancellationToken),
			"close" => await CloseAsync(context, roomId.Value, cancellationToken),
			_ => await UnknownSubcommandAsync(context, subcommand, cancellationToken)
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
			Say = context.Channel == $"#mp_{roomId}"
				? context.Say
				: (text, cancellationToken) => context.Say(NameLines(text), cancellationToken),
			SayPrivately = (text, cancellationToken) => context.SayPrivately(NameLines(text), cancellationToken)
		};

		string NameLines(string text)
		{
			return string.Join('\n',
				text.Split('\n').Select(line => line.Length == 0 ? line : prefix + line));
		}
	}

	// ── scope resolution ─────────────────────────────────────────────────────────────────

	private int? ResolveRoomId(CommandContext context)
	{
		var by = context.By;
		// (a) Channel takes precedence: if sent in #mp_{id}, use that room
		if (context.Channel is not null)
		{
			var match = MpChannelRegex.Match(context.Channel);
			if (match.Success && int.TryParse(match.Groups[1].Value, out var channelRoomId))
			{
				var channelRoom = ViewRoom(by, channelRoomId);
				if (channelRoom is not null) return channelRoomId;
			}
		}

		// (b) PM scope: !mp in <id> sets a target room
		var scoped = scopes.Get(context.Sender.Id);
		if (scoped is not null)
		{
			var scopedRoom = ViewRoom(by, scoped.Value);
			if (scopedRoom is not null) return scoped.Value;
			scopes.Clear(context.Sender.Id);
		}

		// (c) Seated room: where the sender is currently playing
		var seated = by.User.Bancho is { } client ? lobby.RoomOf(client) : null;
		return seated?.Id;
	}

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

		scopes.Set(context.Sender.Id, room.Id);
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

		var room = ViewRoom(by, roomId);
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

		scopes.Set(context.Sender.Id, roomId);
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
			scopes.Clear(context.Sender.Id);
			await context.Reply(MpReplies.NotScopedToAnyMatch, cancellationToken);
			return true;
		}

		if (!int.TryParse(args[0], out var roomId))
		{
			await context.Reply(MpReplies.InUsage, cancellationToken);
			return false;
		}

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithHashId, new { room = roomId }, cancellationToken);
			return false;
		}

		scopes.Set(context.Sender.Id, roomId);
		await context.Reply(MpReplies.NowTargetingMatch, new { room = roomId, name = room.Settings.Name },
			cancellationToken);
		return true;
	}

	// ── !mp settings ─────────────────────────────────────────────────────────────────────

	private async Task<bool> SettingsAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
			return false;
		}

		var replies = context.Replies;
		var lines = new List<string>
		{
			replies.Text(MpReplies.SettingsRoomName, new { name = room.Settings.Name, room = room.Id }),
			room.Settings.Beatmap is not { } beatmap
				? replies.Text(MpReplies.SettingsBeatmapNotSelected)
				: replies.Text(MpReplies.SettingsBeatmap, new { beatmap = beatmap.Id, name = beatmap.Name }),
			replies.Text(MpReplies.SettingsTeamMode,
				new
				{
					teamType = room.Settings.MatchSettings.TeamType.ToString(),
					winCondition = room.Settings.MatchSettings.WinCondition.ToString()
				})
		};

		var activeMods = new List<string>();
		if (room.Settings.MatchSettings.Mods != GameMods.NoMod)
			activeMods.Add(room.Settings.MatchSettings.Mods.ToString());
		if (room.Settings.MatchSettings.Freemods) activeMods.Add(replies.Text(MpReplies.Freemod));
		if (activeMods.Count > 0)
			lines.Add(replies.Text(MpReplies.SettingsActiveMods, new { mods = string.Join(", ", activeMods) }));

		if (room.Authority.Creator is { } creator)
			lines.Add(replies.Text(MpReplies.SettingsCreator, new { id = creator.Id, name = creator.Value.Name }));

		var occupied = room.Slots.Count(slot => slot.Player is not null);
		lines.Add(replies.Text(MpReplies.SettingsPlayers, new { count = occupied }));

		var showTeam = room.Settings.MatchSettings.TeamType is GameTeamType.TeamVs or GameTeamType.TagTeamVs;
		foreach (var slot in room.Slots)
		{
			if (slot.Player is not { } player) continue;
			var tags = new List<string>();
			if (room.Authority.Host is not null && player == room.Authority.Host)
				tags.Add(replies.Text(MpReplies.HostTag));
			if (slot.Mods is { } slotMods && slotMods != GameMods.NoMod) tags.Add(slotMods.ToString());
			var tagText = tags.Count > 0
				? replies.Text(MpReplies.Tags, new { tags = string.Join(" / ", tags) })
				: string.Empty;
			var teamText = showTeam && slot.Team is { } team ? $"{team,-5} " : string.Empty;
			lines.Add(replies.Text(MpReplies.SettingsSlot, new
			{
				slot = $"{slot.Index,2}",
				status = $"{SlotStatusText(replies, slot.Status),-10}",
				team = teamText,
				id = $"{player.Record.Id,6}",
				name = $"{player.Record.Value.Name,-16}",
				tags = tagText
			}));
		}

		await context.Say(string.Join('\n', lines), cancellationToken);
		return true;
	}

	private static string SlotStatusText(IReplies replies, RoomSlotStatus? status)
	{
		return status switch
		{
			RoomSlotStatus.NotReady => replies.Text(MpReplies.SlotStatusNotReady),
			RoomSlotStatus.NoMap => replies.Text(MpReplies.SlotStatusNoMap),
			_ => status?.ToString() ?? string.Empty
		};
	}

	// ── !mp lock / unlock ─────────────────────────────────────────────────────────────────

	private async Task<bool> SetLockedAsync(CommandContext context, int roomId, bool locked,
		CancellationToken cancellationToken)
	{
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).Settings.SetLockedAsync(locked, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(locked ? MpReplies.LockedMatch : MpReplies.UnlockedMatch, cancellationToken);
		return true;
	}

	// ── !mp private ──────────────────────────────────────────────────────────────────────

	private async Task<bool> PrivateAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count == 0)
		{
			var room = ViewRoom(by, roomId);
			if (room is null)
			{
				await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
				return false;
			}

			await context.Reply(MpReplies.MatchIsPrivateNow,
				new { privacy = room.Settings.IsPrivate ? "private" : "public" }, cancellationToken);
			return true;
		}

		if (args[0] is not ("0" or "1"))
		{
			await context.Reply(MpReplies.PrivateUsage, cancellationToken);
			return false;
		}

		var roomToChange = ViewRoom(by, roomId);
		if (roomToChange is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await roomToChange.ScopeTo(by).Settings.ConfigureAsync(
			new RoomSettingsChange(IsPrivate: args[0] == "1"), cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(args[0] == "1" ? MpReplies.MatchNowPrivate : MpReplies.MatchNowPublic,
			cancellationToken);
		return true;
	}

	// ── !mp size ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetSizeAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var size))
		{
			await context.Reply(MpReplies.SizeUsage, cancellationToken);
			return false;
		}

		size = Math.Clamp(size, 1, MaxRoomSize);
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(new RoomSettingsChange(Size: size),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ChangedMatchSize, new { size }, cancellationToken);
		return true;
	}

	// ── !mp move ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> MoveAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
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

	private async Task<bool> HostAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
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

	private async Task<bool> ClearHostAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).Authority.SetHostAsync(null, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ClearedMatchHost, cancellationToken);
		return true;
	}

	// ── !mp name / password ──────────────────────────────────────────────────────────────

	private async Task<bool> SetNameAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.NameUsage, cancellationToken);
			return false;
		}

		var name = string.Join(' ', args);
		if (name.Length > MaxRoomNameLength) name = name[..MaxRoomNameLength];

		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(new RoomSettingsChange(name),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.RoomNameUpdated, new { name }, cancellationToken);
		return true;
	}

	private async Task<bool> SetPasswordAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		var password = args.Count == 0 ? string.Empty : string.Join(' ', args);
		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Settings.ConfigureAsync(new RoomSettingsChange(Password: password),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(args.Count == 0 ? MpReplies.RemovedMatchPassword : MpReplies.ChangedMatchPassword,
			cancellationToken);
		return true;
	}

	// ── !mp invite ───────────────────────────────────────────────────────────────────────

	private async Task<bool> InviteAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
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

	// ── !mp addref / removeref / listrefs ────────────────────────────────────────────────

	private async Task<bool> AddRefereeAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Authority.AddRefereeAsync(target, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.AddedReferee, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	private async Task<bool> RemoveRefereeAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Authority.RemoveRefereeAsync(target, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.RemovedReferee, new { player = target.Value.Name }, cancellationToken);
		return true;
	}

	private async Task<bool> ListRefsAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
			return false;
		}

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

	private async Task<bool> BanListAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
			return false;
		}

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

	// ── !mp team ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetTeamAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
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
		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

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

	// ── !mp set ──────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var teamValue) || teamValue is < 0 or > 3)
		{
			await context.Reply(MpReplies.SetUsage, cancellationToken);
			return false;
		}

		GameWinCondition? winCondition = null;
		if (args.Count >= 2)
		{
			if (!int.TryParse(args[1], out var winValue) || winValue is < 0 or > 3)
			{
				await context.Reply(MpReplies.SetUsage, cancellationToken);
				return false;
			}

			winCondition = (GameWinCondition)winValue;
		}

		int? size = null;
		if (args.Count >= 3)
		{
			if (!int.TryParse(args[2], out var parsedSize))
			{
				await context.Reply(MpReplies.SetUsage, cancellationToken);
				return false;
			}

			size = Math.Clamp(parsedSize, 1, MaxRoomSize);
		}

		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var change = new RoomSettingsChange(TeamType: (GameTeamType)teamValue, WinCondition: winCondition,
			Size: size);
		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(change, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ChangedMatchSettings,
			new
			{
				teamType = room.Settings.MatchSettings.TeamType.ToString(),
				winCondition = room.Settings.MatchSettings.WinCondition.ToString(),
				size = size?.ToString() ?? "none"
			}, cancellationToken);
		return true;
	}

	// ── !mp map ──────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetMapAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var beatmapId))
		{
			await context.Reply(MpReplies.MapUsage, cancellationToken);
			return false;
		}

		GameMode? mode = null;
		if (args.Count > 1)
		{
			if (!int.TryParse(args[1], out var modeValue) || modeValue is < 0 or > 3)
			{
				await context.Reply(MpReplies.MapUsage, cancellationToken);
				return false;
			}

			mode = (GameMode)modeValue;
		}

		var beatmap = await beatmaps.GetAsync(beatmapId, cancellationToken);
		if (beatmap is null)
		{
			await context.Reply(MpReplies.NoBeatmapWithId, new { beatmap = beatmapId }, cancellationToken);
			return false;
		}

		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var reference = new BeatmapReference(beatmap.Hash, beatmap.Id, beatmap.Name,
			beatmap.Metadata.Difficulty.Mode, beatmap);
		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: reference, Mode: mode ?? beatmap.Metadata.Difficulty.Mode),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ChangedBeatmap,
			new
			{
				artist = beatmap.Beatmapset.Metadata.Artist,
				title = beatmap.Beatmapset.Metadata.Title,
				version = beatmap.Metadata.Version
			}, cancellationToken);
		return true;
	}

	// ── !mp mods ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetModsAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.ModsUsage, cancellationToken);
			return false;
		}

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
			return false;
		}

		var before = room.Settings.MatchSettings.Mods;
		var wasFreemod = room.Settings.MatchSettings.Freemods;
		var mode = room.Settings.MatchSettings.Mode;

		var mods = GameMods.NoMod;
		var freemod = false;
		foreach (var token in args)
		{
			if (token.Equals("None", StringComparison.OrdinalIgnoreCase))
				continue;

			if (token.Equals("Freemod", StringComparison.OrdinalIgnoreCase))
			{
				freemod = true;
				continue;
			}

			mods |= ModsExtensions.FromModString(token);
		}

		var outcome = await room.ScopeTo(by).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: mods.RemoveInvalidMods(mode), Freemods: freemod), cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		var after = room.Settings.MatchSettings.Mods;
		var isFreemod = room.Settings.MatchSettings.Freemods;
		await context.Say(DescribeModChange(context.Replies, before, after, wasFreemod, isFreemod), cancellationToken);
		return true;
	}

	private static string DescribeModChange(IReplies replies, GameMods before, GameMods after, bool wasFreemod,
		bool isFreemod)
	{
		var enabled = after & ~before;
		var disabled = before & ~after;
		var parts = new List<string>();

		if (enabled != GameMods.NoMod)
			parts.Add(replies.Text(MpReplies.EnabledMods, new { mods = enabled.ToString() }));
		if (disabled != GameMods.NoMod)
			parts.Add(replies.Text(MpReplies.DisabledMods, new { mods = disabled.ToString() }));
		if (wasFreemod && !isFreemod) parts.Add(replies.Text(MpReplies.DisabledFreemod));
		else if (!wasFreemod && isFreemod) parts.Add(replies.Text(MpReplies.EnabledFreemod));

		return parts.Count > 0 ? string.Join(", ", parts) : replies.Text(MpReplies.NoModChanges);
	}

	// ── !mp start / timer / aborttimer / abort ────────────────────────────────────────────

	private async Task<bool> StartAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count > 0 && int.TryParse(args[0], out var seconds) && seconds > 0)
			return await StartCountdownAsync(context, roomId, seconds, true, cancellationToken);

		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).Rounds.StartAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.MatchStarted, cancellationToken);
		return true;
	}

	private async Task<bool> TimerAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var seconds = DefaultTimerSeconds;
		if (args.Count > 0)
			if (!int.TryParse(args[0], out seconds) || seconds <= 0)
			{
				await context.Reply(MpReplies.TimerUsage, cancellationToken);
				return false;
			}

		return await StartCountdownAsync(context, roomId, seconds, false, cancellationToken);
	}

	private async Task<bool> AbortTimerAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var by = context.By;
		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Rounds.CancelCountdownAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.CountdownAborted, cancellationToken);
		return true;
	}

	/// <summary>Starts a countdown after checking what the server no longer checks for it.</summary>
	/// <param name="context">Who sent the command and how to answer it.</param>
	/// <param name="roomId">The room the countdown runs in.</param>
	/// <param name="seconds">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the round starts when the countdown ends.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when it started; otherwise the reason was replied.</returns>
	private async Task<bool> StartCountdownAsync(CommandContext context, int roomId, int seconds, bool startsRound,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NoActiveRoomWithId, new { room = roomId }, cancellationToken);
			return false;
		}

		if (seconds < 1 || seconds > 3600)
		{
			await context.Reply(MpReplies.CountdownOutOfRange, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Rounds.StartCountdownAsync(TimeSpan.FromSeconds(seconds), startsRound,
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		return true;
	}

	private async Task<bool> AbortAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).Rounds.AbortAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.AbortedMatch, cancellationToken);
		return true;
	}

	// ── !mp kick / ban / unban / close ────────────────────────────────────────────────────

	private async Task<bool> KickAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
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

	private async Task<bool> BanAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
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

	private async Task<bool> UnbanAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
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

		var room = ViewRoom(by, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
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

	private async Task<bool> CloseAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = ViewRoom(context.By, roomId);
		if (room is null)
		{
			await context.Reply(MpReplies.NotFound, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).CloseAsync(cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ClosedMatch, cancellationToken);
		return true;
	}

	// ── unknown subcommand ───────────────────────────────────────────────────────────────

	private static async Task<bool> UnknownSubcommandAsync(CommandContext context, string subcommand,
		CancellationToken cancellationToken)
	{
		await context.Reply(MpReplies.UnknownMpSubcommand, new { subcommand }, cancellationToken);
		return false;
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────

	private async ValueTask<UserRecord?> ResolveUserAsync(string raw, CancellationToken cancellationToken)
	{
		if (raw.StartsWith('#') && int.TryParse(raw[1..], out var id))
			return await users.GetAsync(id, cancellationToken);
		return await users.GetByNameAsync(raw, cancellationToken);
	}

	private Room? ViewRoom(Connection by, int id)
	{
		if (!lobby.Rooms.TryGetValue(id, out var room) || room.IsClosed) return null;
		if (!room.Settings.IsPrivate || room.Slots.Find(by.Record) is not null ||
		    room.Members.Observers.Any(observer => observer.Record.Equals(by.Record)) ||
		    room.Authority.IsManagedBy(by.Record) ||
		    by.User.EffectivePermissions(time.GetUtcNow()).Allows(Permissions.RoomViewPrivate))
			return room;

		return null;
	}

	[GeneratedRegex(@"^#mp_(\d+)$")]
	private static partial Regex MpChannelRegexPattern();
}