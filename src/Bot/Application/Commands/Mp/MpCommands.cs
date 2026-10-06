using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Basil.Bot.Application.Basil;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Commands.Mp;

/// <summary>
///     Executes the <c>!mp</c> chat command surface against the Basil server.
/// </summary>
/// <remarks>
///     All operations go through <see cref="IBasilRooms" />, <see cref="IBasilUsers" /> and
///     <see cref="IBasilBeatmaps" />; the server enforces authority and returns a
///     <see cref="RoomOutcome" />, which is mapped to a user-visible reply by
///     <see cref="MpReplies.Describe" />.
/// </remarks>
internal sealed partial class MpCommands(
	IBasilRooms rooms,
	IBasilUsers users,
	IBasilBeatmaps beatmaps,
	MpScopes scopes)
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
			"make" => await MakeAsync(context, subArgs, isPrivate: false, cancellationToken),
			"makeprivate" => await MakeAsync(context, subArgs, isPrivate: true, cancellationToken),
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
		var roomId = await ResolveRoomIdAsync(context, cancellationToken);
		if (roomId is null)
		{
			await context.Reply(MpReplies.NotInARoom, cancellationToken);
			return false;
		}

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

	// ── scope resolution ─────────────────────────────────────────────────────────────────

	private async Task<int?> ResolveRoomIdAsync(CommandContext context, CancellationToken cancellationToken)
	{
		var scoped = scopes.Get(context.Sender.Id);
		if (scoped is not null)
		{
			var scopedRoom = await rooms.GetAsync(scoped.Value, cancellationToken);
			if (scopedRoom is not null) return scoped.Value;
			scopes.Clear(context.Sender.Id);
		}

		if (context.Channel is not null)
		{
			var match = MpChannelRegex.Match(context.Channel);
			if (match.Success && int.TryParse(match.Groups[1].Value, out var channelRoomId))
			{
				var channelRoom = await rooms.GetAsync(channelRoomId, cancellationToken);
				if (channelRoom is not null) return channelRoomId;
			}
		}

		var seated = await rooms.FindByPlayerAsync(context.Sender, cancellationToken);
		return seated?.Id;
	}

	// ── !mp make / makeprivate ───────────────────────────────────────────────────────────

	private async Task<bool> MakeAsync(CommandContext context, IReadOnlyList<string> args, bool isPrivate,
		CancellationToken cancellationToken)
	{
		var name = args.Count > 0
			? string.Join(' ', args)
			: $"{context.Sender.Value.Name}'s match";
		if (name.Length > MaxRoomNameLength) name = name[..MaxRoomNameLength];

		var (room, outcome) = await rooms.OpenAsync(context.Sender, name, isPrivate, cancellationToken);
		if (outcome != RoomOutcome.Ok || room is null)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		scopes.Set(context.Sender.Id, room.Id);
		var privacySuffix = isPrivate ? " (private)" : string.Empty;
		await context.Reply(
			string.Format(MpReplies.CreatedMatch, room.Id, room.Name, privacySuffix), cancellationToken);
		return true;
	}

	// ── !mp join ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> JoinAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var roomId))
		{
			await context.Reply(MpReplies.JoinUsage, cancellationToken);
			return false;
		}

		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			await context.Reply(string.Format(MpReplies.NoActiveRoomWithId, roomId), cancellationToken);
			return false;
		}

		var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : string.Empty;
		var outcome = await rooms.JoinAsync(context.Sender, roomId, password, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		scopes.Set(context.Sender.Id, roomId);
		await context.Reply(string.Format(MpReplies.JoinedMatch, roomId, room.Name), cancellationToken);
		return true;
	}

	// ── !mp in ───────────────────────────────────────────────────────────────────────────

	private async Task<bool> InAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			await context.Reply(string.Format(MpReplies.NoActiveRoomWithHashId, roomId), cancellationToken);
			return false;
		}

		scopes.Set(context.Sender.Id, roomId);
		await context.Reply(string.Format(MpReplies.NowTargetingMatch, roomId, room.Name), cancellationToken);
		return true;
	}

	// ── !mp settings ─────────────────────────────────────────────────────────────────────

	private async Task<bool> SettingsAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			await context.Reply(string.Format(MpReplies.NoActiveRoomWithId, roomId), cancellationToken);
			return false;
		}

		var lines = new List<string>
		{
			string.Format(MpReplies.SettingsRoomName, room.Name, room.Id),
			room.Beatmap is null
				? MpReplies.SettingsBeatmapNotSelected
				: string.Format(MpReplies.SettingsBeatmap, room.Beatmap.Id, room.Beatmap.Name),
			string.Format(MpReplies.SettingsTeamMode, room.Settings.TeamType, room.Settings.WinCondition)
		};

		var activeMods = new List<string>();
		if (room.Settings.Mods != GameMods.NoMod) activeMods.Add(room.Settings.Mods.ToString());
		if (room.Settings.Freemods) activeMods.Add("Freemod");
		if (activeMods.Count > 0)
			lines.Add(string.Format(MpReplies.SettingsActiveMods, string.Join(", ", activeMods)));

		if (room.Creator is { } creator)
			lines.Add(string.Format(MpReplies.SettingsCreator, creator.Id, creator.Value.Name));

		var occupied = room.Slots.Count(s => s.Player is not null);
		lines.Add(string.Format(MpReplies.SettingsPlayers, occupied));

		var showTeam = room.Settings.TeamType is GameTeamType.TeamVs or GameTeamType.TagTeamVs;
		foreach (var slot in room.Slots)
		{
			if (slot.Player is null) continue;
			var tags = new List<string>();
			if (room.Host is not null && slot.Player == room.Host) tags.Add("Host");
			if (slot.Mods is { } slotMods && slotMods != GameMods.NoMod) tags.Add(slotMods.ToString());
			var tagText = tags.Count > 0 ? $" [{string.Join(" / ", tags)}]" : string.Empty;
			var teamText = showTeam && slot.Team is { } team ? $"{team,-5} " : string.Empty;
			lines.Add(
				$"Slot {slot.Index,2}  {SlotStatusText(slot.Status),-10} {teamText}{slot.Player.Id,6} {slot.Player.Value.Name,-16}{tagText}");
		}

		await context.Reply(string.Join('\n', lines), cancellationToken);
		return true;
	}

	private static string SlotStatusText(RoomSlotStatus? status) =>
		status switch
		{
			RoomSlotStatus.NotReady => "Not Ready",
			RoomSlotStatus.NoMap => "No Map",
			_ => status?.ToString() ?? string.Empty
		};

	// ── !mp lock / unlock ─────────────────────────────────────────────────────────────────

	private async Task<bool> SetLockedAsync(CommandContext context, int roomId, bool locked,
		CancellationToken cancellationToken)
	{
		var outcome = await rooms.SetLockedAsync(context.Sender, roomId, locked, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(locked ? MpReplies.LockedMatch : MpReplies.UnlockedMatch, cancellationToken);
		return true;
	}

	// ── !mp private ──────────────────────────────────────────────────────────────────────

	private async Task<bool> PrivateAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count == 0)
		{
			var room = await rooms.GetAsync(roomId, cancellationToken);
			if (room is null)
			{
				await context.Reply(string.Format(MpReplies.NoActiveRoomWithId, roomId), cancellationToken);
				return false;
			}

			await context.Reply(
				string.Format(MpReplies.MatchIsPrivateNow, room.IsPrivate ? "private" : "not private"),
				cancellationToken);
			return true;
		}

		if (args[0] is not ("0" or "1"))
		{
			await context.Reply(MpReplies.PrivateUsage, cancellationToken);
			return false;
		}

		var outcome = await rooms.ConfigureAsync(context.Sender, roomId,
			new RoomChange(IsPrivate: args[0] == "1"), cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
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
		var outcome = await rooms.ConfigureAsync(context.Sender, roomId, new RoomChange(Size: size),
			cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.ChangedMatchSize, size), cancellationToken);
		return true;
	}

	// ── !mp move ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> MoveAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var outcome = await rooms.MoveAsync(context.Sender, roomId, target, slot, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.MovedToSlot, target.Value.Name, slot), cancellationToken);
		return true;
	}

	// ── !mp host / clearhost ──────────────────────────────────────────────────────────────

	private async Task<bool> HostAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var outcome = await rooms.SetHostAsync(context.Sender, roomId, target, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.ChangedMatchHost, target.Value.Name), cancellationToken);
		return true;
	}

	private async Task<bool> ClearHostAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var outcome = await rooms.SetHostAsync(context.Sender, roomId, null, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
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

		var outcome = await rooms.ConfigureAsync(context.Sender, roomId, new RoomChange(Name: name),
			cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.RoomNameUpdated, name), cancellationToken);
		return true;
	}

	private async Task<bool> SetPasswordAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var password = args.Count == 0 ? string.Empty : string.Join(' ', args);
		var outcome = await rooms.ConfigureAsync(context.Sender, roomId, new RoomChange(Password: password),
			cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
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

		var outcome = await rooms.InviteAsync(context.Sender, roomId, target, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.InvitedToRoom, target.Value.Name), cancellationToken);
		return true;
	}

	// ── !mp addref / removeref / listrefs ────────────────────────────────────────────────

	private async Task<bool> AddRefereeAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var outcome = await rooms.AddRefereeAsync(context.Sender, roomId, target, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.AddedReferee, target.Value.Name), cancellationToken);
		return true;
	}

	private async Task<bool> RemoveRefereeAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var outcome = await rooms.RemoveRefereeAsync(context.Sender, roomId, target, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.RemovedReferee, target.Value.Name), cancellationToken);
		return true;
	}

	private async Task<bool> ListRefsAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			await context.Reply(string.Format(MpReplies.NoActiveRoomWithId, roomId), cancellationToken);
			return false;
		}

		if (room.Referees.Count == 0)
		{
			await context.Reply(MpReplies.NoReferees, cancellationToken);
			return true;
		}

		var lines = room.Referees
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Reply(MpReplies.MatchReferees + "\n" + string.Join('\n', lines), cancellationToken);
		return true;
	}

	// ── !mp banlist ──────────────────────────────────────────────────────────────────────

	private async Task<bool> BanListAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			await context.Reply(string.Format(MpReplies.NoActiveRoomWithId, roomId), cancellationToken);
			return false;
		}

		if (room.Banned.Count == 0)
		{
			await context.Reply(MpReplies.NoBannedPlayers, cancellationToken);
			return true;
		}

		var lines = room.Banned
			.Select(u => $"#{u.Id} {u.Value.Name}");
		await context.Reply(MpReplies.MatchBans + "\n" + string.Join('\n', lines), cancellationToken);
		return true;
	}

	// ── !mp team ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetTeamAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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
		var outcome = await rooms.SetTeamAsync(context.Sender, roomId, target, team, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		var teamDisplay = char.ToUpperInvariant(teamArg[0]) + teamArg[1..];
		await context.Reply(string.Format(MpReplies.MovedToTeam, target.Value.Name, teamDisplay), cancellationToken);
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

		var change = new RoomChange(TeamType: (GameTeamType)teamValue, WinCondition: winCondition, Size: size);
		var outcome = await rooms.ConfigureAsync(context.Sender, roomId, change, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		var live = await rooms.GetAsync(roomId, cancellationToken);
		var liveTeamType = live?.Settings.TeamType ?? (GameTeamType)teamValue;
		var liveWinCondition = live?.Settings.WinCondition ?? (winCondition ?? default);
		var sizeSuffix = size is { } sz ? $", {sz} slots." : ".";
		await context.Reply(string.Format(MpReplies.ChangedMatchSettings, liveTeamType, liveWinCondition, sizeSuffix),
			cancellationToken);
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
			await context.Reply(string.Format(MpReplies.NoBeatmapWithId, beatmapId), cancellationToken);
			return false;
		}

		var outcome = await rooms.ConfigureAsync(context.Sender, roomId,
			new RoomChange(BeatmapId: beatmapId, Mode: mode ?? beatmap.Value.Difficulty.Mode),
			cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(
			string.Format(MpReplies.ChangedBeatmap, beatmap.Value.Beatmapset.Value.Artist,
				beatmap.Value.Beatmapset.Value.Title, beatmap.Value.Version),
			cancellationToken);
		return true;
	}

	// ── !mp mods ─────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetModsAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.ModsUsage, cancellationToken);
			return false;
		}

		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			await context.Reply(string.Format(MpReplies.NoActiveRoomWithId, roomId), cancellationToken);
			return false;
		}

		var before = room.Settings.Mods;
		var wasFreemod = room.Settings.Freemods;
		var mode = room.Settings.Mode;

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

		var outcome = await rooms.ConfigureAsync(context.Sender, roomId,
			new RoomChange(Mods: mods.RemoveInvalidMods(mode), Freemods: freemod), cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		var updated = await rooms.GetAsync(roomId, cancellationToken);
		var after = updated?.Settings.Mods ?? mods;
		var isFreemod = updated?.Settings.Freemods ?? freemod;
		await context.Reply(DescribeModChange(before, after, wasFreemod, isFreemod), cancellationToken);
		return true;
	}

	private static string DescribeModChange(GameMods before, GameMods after, bool wasFreemod, bool isFreemod)
	{
		var enabled = after & ~before;
		var disabled = before & ~after;
		var parts = new List<string>();

		if (enabled != GameMods.NoMod) parts.Add(string.Format(MpReplies.EnabledMods, enabled));
		if (disabled != GameMods.NoMod) parts.Add(string.Format(MpReplies.DisabledMods, disabled));
		if (wasFreemod && !isFreemod) parts.Add(MpReplies.DisabledFreemod);
		else if (!wasFreemod && isFreemod) parts.Add(MpReplies.EnabledFreemod);

		return parts.Count > 0 ? string.Join(", ", parts) : MpReplies.NoModChanges;
	}

	// ── !mp start / timer / aborttimer / abort ────────────────────────────────────────────

	private async Task<bool> StartAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		RoomOutcome outcome;
		if (args.Count > 0 && int.TryParse(args[0], out var seconds) && seconds > 0)
			outcome = await rooms.StartCountdownAsync(context.Sender, roomId, TimeSpan.FromSeconds(seconds),
				startsRound: true, cancellationToken);
		else
			outcome = await rooms.StartAsync(context.Sender, roomId, cancellationToken);

		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		if (args.Count > 0 && int.TryParse(args[0], out var counted) && counted > 0)
			await context.Reply(string.Format(MpReplies.MatchStartsInSeconds, counted), cancellationToken);
		else
			await context.Reply(MpReplies.MatchStarted, cancellationToken);
		return true;
	}

	private async Task<bool> TimerAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var seconds = DefaultTimerSeconds;
		if (args.Count > 0)
		{
			if (!int.TryParse(args[0], out seconds) || seconds <= 0)
			{
				await context.Reply(MpReplies.TimerUsage, cancellationToken);
				return false;
			}
		}

		var outcome = await rooms.StartCountdownAsync(context.Sender, roomId, TimeSpan.FromSeconds(seconds),
			startsRound: false, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.CountdownStarted, seconds), cancellationToken);
		return true;
	}

	private async Task<bool> AbortTimerAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var outcome = await rooms.CancelCountdownAsync(context.Sender, roomId, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.CountdownAborted, cancellationToken);
		return true;
	}

	private async Task<bool> AbortAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var outcome = await rooms.AbortAsync(context.Sender, roomId, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.AbortedMatch, cancellationToken);
		return true;
	}

	// ── !mp kick / ban / unban / close ────────────────────────────────────────────────────

	private async Task<bool> KickAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var outcome = await rooms.KickAsync(context.Sender, roomId, target, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.KickedFromMatch, target.Value.Name), cancellationToken);
		return true;
	}

	private async Task<bool> BanAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var outcome = await rooms.BanAsync(context.Sender, roomId, target, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.BannedPlayerFromMatch, target.Value.Name), cancellationToken);
		return true;
	}

	private async Task<bool> UnbanAsync(CommandContext context, int roomId, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
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

		var outcome = await rooms.UnbanAsync(context.Sender, roomId, target, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(string.Format(MpReplies.UnbannedFromMatch, target.Value.Name), cancellationToken);
		return true;
	}

	private async Task<bool> CloseAsync(CommandContext context, int roomId, CancellationToken cancellationToken)
	{
		var outcome = await rooms.CloseAsync(context.Sender, roomId, cancellationToken);
		if (outcome != RoomOutcome.Ok)
		{
			await context.Reply(MpReplies.Describe(outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ClosedMatch, cancellationToken);
		return true;
	}

	// ── unknown subcommand ───────────────────────────────────────────────────────────────

	private async Task<bool> UnknownSubcommandAsync(CommandContext context, string subcommand,
		CancellationToken cancellationToken)
	{
		await context.Reply(string.Format(MpReplies.UnknownMpSubcommand, subcommand), cancellationToken);
		return false;
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────

	private async ValueTask<User?> ResolveUserAsync(string raw, CancellationToken cancellationToken)
	{
		if (raw.StartsWith('#') && int.TryParse(raw[1..], out var id))
			return await users.GetAsync(id, cancellationToken);
		return await users.GetByNameAsync(raw, cancellationToken);
	}

	[GeneratedRegex(@"^#mp_(\d+)$")]
	private static partial Regex MpChannelRegexPattern();
}