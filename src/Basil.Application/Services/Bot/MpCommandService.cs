using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.PacketHandlers.Channels;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Bot;

/// <summary>
///     `!mp` subcommands, matched against real osu! Bancho's own wiki/chat behaviour rather than
///     bancho.py — see docs/working-scopes.md for what's a deliberate Basil-only addition vs a real
///     Bancho command. <c>makeprivate</c> is no longer an alias of <c>make</c> — it now redirects to
///     <see cref="SetPrivate" /> (setting the room private), and <c>!mp private [0|1]</c> is the
///     canonical way to view/change privacy. <c>!mp join &lt;id&gt;</c> bypasses the referee gate
///     (routed from <see cref="CommandDispatcher" /> directly). Every subcommand except
///     <c>help</c>/<c>make</c>/<c>join</c>/<c>in</c> requires <see cref="MatchSession.IsReferee" /> —
///     unmet permission is a silent no-op (no error reply), not an upgraded error message. Referee is
///     a pure permission flag (doesn't require physical presence in the room); the host is NOT
///     automatically a referee — hosting only grants direct in-client settings control, which ranks
///     below referee authority for `!mp` purposes.
///     Every method below sends its own reply through the <see cref="ICommandReplySink" /> passed in
///     (see that type's doc comment) and returns only success/failure — nothing here returns reply
///     text for a caller to route.
///     The actual room-mutation logic lives in <see cref="MatchControlService" /> — shared with the
///     `api.` host's HTTP write routes so both surfaces call the identical state-mutation/broadcast
///     code. This class owns everything chat-specific: parsing raw argument tokens, resolving a
///     target player by name (<see cref="IPlayerSessionRegistry.GetByName" />), the referee gate, and
///     sending a reply for the result.
/// </summary>
public sealed class MpCommandService(
	MatchMembershipService matchMembership,
	IMatchRegistry matchRegistry,
	IMatchPersistenceRepository matchPersistence,
	IMapRepository mapRepository,
	IPlayerSessionRegistry sessionRegistry,
	IUserRepository userRepository,
	ILogger<MpCommandService> logger,
	ILogger<MatchControlService> matchControlLogger)
{
	private const int MaxMatchNameLength = 50;

	/// <summary>
	///     Single source of truth for `!mp help`'s output — add a subcommand here, and it shows up with
	///     no separate help string to keep in sync. `make`/`makeprivate`/`in` are listed in
	///     <see cref="CommandDispatcher" />'s own help instead, since they run outside this class'
	///     switch (see <see cref="TryHandleAsync" />'s doc comment).
	/// </summary>
	private static readonly CommandInfo[] Commands =
	[
		new("!mp settings", "show match id, map, team type, win condition, mods, and slots"),
		new("!mp lock", "lock the room, blocking new joins"),
		new("!mp unlock", "unlock the room"),
		new("!mp private [0|1]", "show or set the room's private status (hidden from lobby, invite-only)"),
		new("!mp size <1-16>", "set the number of available slots"),
		new("!mp move <name> <slot 1-16>", "move a player to another slot"),
		new("!mp host <name>", "transfer host to another player"),
		new("!mp clearhost", "clear the current host"),
		new("!mp name <text>", "rename the match"),
		new("!mp password [text]", "set the room password; omit to clear it"),
		new("!mp invite <name>", "invite an online player"),
		new("!mp addref <name>", "add a referee"),
		new("!mp removeref <name>", "remove a referee"),
		new("!mp listrefs", "list current referees"),
		new("!mp banlist", "list players banned from this match"),
		new("!mp team <name> <red|blue>", "assign a player's team\nTeam: Red, Blue"),
		new("!mp map <beatmap id>", "change the selected map"),
		new("!mp mods <mods>|Freemod|None",
			"set the match mods\nMods: NF, EZ, HD, HR, SD, DT, RX, HT, NC, FL, SO, AP, PF, Freemod, None"),
		new("!mp set <teammode 0-3> [scoremode 0-3] [size 1-16]",
			"set team type, win condition, and size at once\n" +
			"Teammode 0: HeadToHead, 1: TagCoop, 2: TeamVs, 3: TagTeamVs\n" +
			"Scoremode 0: Score, 1: Accuracy, 2: Combo, 3: ScoreV2"),
		new("!mp start [seconds]", "start now, or start a countdown"),
		new("!mp timer [seconds]", "start a countdown without auto-starting"),
		new("!mp aborttimer", "cancel a running countdown"),
		new("!mp abort", "abort the match in progress"),
		new("!mp kick <name>", "remove a player from the room"),
		new("!mp ban <name>", "kick and block a player from rejoining"),
		new("!mp unban <name>", "allow a banned player to rejoin"),
		new("!mp close", "close the match immediately")
	];

	internal static readonly string HelpText = string.Join('\n', Commands.Select(c => $"{c.Usage} - {c.Description}"));

	private readonly MatchControlService _matchControl =
		new(matchMembership, matchPersistence, mapRepository, sessionRegistry, matchControlLogger);

	/// <summary>
	///     Main `!mp` subcommand switch — help bypasses the referee gate (always DM'd, see
	///     <see cref="ICommandReplySink.ReplyDm" />) and every other subcommand requires
	///     <see cref="MatchSession.IsReferee" />, rejecting silently (no reply) otherwise.
	/// </summary>
	public async Task<bool> TryHandleAsync(PlayerSession sender, MatchSession match, string subcommand,
		IReadOnlyList<string> args, ICommandReplySink sink, CancellationToken cancellationToken = default)
	{
		if (subcommand is "" or "help")
		{
			sink.ReplyDm(HelpText);
			return true;
		}

		using var _ = logger.BeginScope(new Dictionary<string, object>
		{
			["MatchId"] = match.DbId,
			["Subcommand"] = subcommand
		});

		if (!match.IsReferee(sender.Id))
		{
			logger.LogDebug("Subcommand rejected: {UserId} is not a referee of MatchId={MatchId}", sender.Id,
				match.DbId);
			return false;
		}

		return subcommand switch
		{
			"settings" => await SettingsAsync(match, sink, cancellationToken),
			"lock" => await RunLockedAsync(match, () => Task.FromResult(SetRoomLocked(match, true, sink))),
			"unlock" => await RunLockedAsync(match, () => Task.FromResult(SetRoomLocked(match, false, sink))),
			"private" => await SetPrivate(match, args, sink),
			"makeprivate" => await SetPrivate(match, ["1"], sink),
			"size" => await RunLockedAsync(match, () => SetSize(match, args, sink)),
			"move" => await RunLockedAsync(match, () => MoveSlot(match, args, sink)),
			"host" => await RunLockedAsync(match, () => SetHost(match, args, sink)),
			"clearhost" => await RunLockedAsync(match, () => ClearHost(match, sink)),
			"name" => await RunLockedAsync(match, () => SetName(match, args, sink)),
			"password" => await RunLockedAsync(match, () => SetPassword(match, args, sink)),
			"invite" => await RunLockedAsync(match, () => Task.FromResult(Invite(sender, match, args, sink))),
			"addref" => await RunLockedAsync(match, () => AddRefereeAsync(sender, match, args, sink, cancellationToken)),
			"removeref" => await RunLockedAsync(match,
				() => RemoveRefereeAsync(sender, match, args, sink, cancellationToken)),
			"listrefs" => ListReferees(match, sink),
			"banlist" => await BanListAsync(match, sink, cancellationToken),
			"team" => await RunLockedAsync(match, () => SetTeam(match, args, sink)),
			"set" => await RunLockedAsync(match, () => Set(match, args, sink)),
			"map" => await RunLockedAsync(match, () => SetMapAsync(match, args, sink, cancellationToken)),
			"mods" => await RunLockedAsync(match, () => SetMods(match, args, sink)),
			"start" => await RunLockedAsync(match, () => StartAsync(match, args, sink, cancellationToken)),
			"timer" => await RunLockedAsync(match, () => Task.FromResult(Timer(match, args, sink))),
			"aborttimer" => await RunLockedAsync(match, () => Task.FromResult(AbortTimer(match, sink))),
			"abort" => await RunLockedAsync(match, () => AbortAsync(match, sink, cancellationToken)),
			"kick" => await RunLockedAsync(match, () => KickAsync(sender, match, args, sink, cancellationToken)),
			"ban" => await RunLockedAsync(match, () => BanAsync(sender, match, args, sink, cancellationToken)),
			"unban" => await RunLockedAsync(match, () => UnbanAsync(match, args, sink, cancellationToken)),
			"close" => await RunLockedAsync(match, () => CloseAsync(sender, match, sink, cancellationToken)),
			_ => false
		};
	}

	/// <summary>
	///     Backs `!mp make`/`!mp makeprivate` — one shared implementation, no `isPrivate` flag or any
	///     other parameter distinguishes the two trigger words (see <see cref="CommandDispatcher" />).
	///     Unlike every other subcommand this runs with no <see cref="MatchSession" /> yet (there's
	///     nothing to be a referee of), so it bypasses <see cref="TryHandleAsync" /> entirely — reusing
	///     <see cref="MatchMembershipService.CreateAsync" /> verbatim, exactly like a real
	///     `MATCH_CREATE` packet, except the creator is also auto-added as a referee (required to
	///     bootstrap — nobody could otherwise ever pass the outer <see cref="MatchSession.IsReferee" />
	///     gate on their own brand-new room). Marked <see cref="MatchSession.CreatedViaMakeCommand" />
	///     so it persists until `!mp close` or the referee list empties, instead of auto-tearing down
	///     once every slot empties like a normal client-created room.
	/// </summary>
	public async Task<bool> MakeAsync(PlayerSession sender, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken = default)
	{
		var name = args.Count > 0 ? string.Join(' ', args) : $"{sender.Name}'s match";
		if (name.Length > MaxMatchNameLength) name = name[..MaxMatchNameLength];

		var data = new ReadMatchResult(
			0, false, 0, 0, name, "",
			"", 0, "",
			[], [], [], sender.Id, 0,
			0, 0, false, [], 0);

		var match = await matchMembership.CreateAsync(sender, data, cancellationToken, true);
		if (match is null)
		{
			sink.Reply("Couldn't create the match — server is full.");
			return false;
		}

		match.AddReferee(sender.Id);
		sender.MpScopeMatchId = match.DbId;
		sink.Reply($"Created the match #{match.DbId} {match.Name}. You are now scoped to this match, and added as a referee.");
		return true;
	}

	/// <summary>
	///     Backs `!mp join &lt;id&gt; [password]` — lets any player join a match by its persistent room
	///     id (<see cref="MatchSession.DbId" />, unbounded — not the 0-63 wire-format slot id). A private
	///     match rejects everyone but staff and invitees, host included (see
	///     <see cref="MatchSession.IsPrivate" />); locked rooms and banned players are rejected too,
	///     with a descriptive message. Runs with no <see cref="MatchSession" /> scope (routed directly
	///     from <see cref="CommandDispatcher" />, bypassing scope resolution).
	/// </summary>
	public async Task<bool> JoinAsync(PlayerSession sender, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken = default)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var matchId))
		{
			sink.Reply("Usage: !mp join <id> [password]");
			return false;
		}

		var match = matchRegistry.GetByDbId(matchId);
		if (match is null)
		{
			sink.Reply($"No active match with id {matchId}.");
			return false;
		}

		if (match.IsPrivate && (sender.Privilege & UserPrivileges.Staff) == 0 && !match.InvitedIds.Contains(sender.Id))
		{
			sink.Reply($"Cannot join match #{matchId} — the room is private. Ask a referee for an invite.");
			return false;
		}

		if (sender.Match is not null)
		{
			sink.Reply("You're already in a match.");
			return false;
		}

		if (match.BannedIds.Contains(sender.Id))
		{
			sink.Reply("You're banned from this match.");
			return false;
		}

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : "";
			if (await matchMembership.JoinAsync(sender, match, password))
			{
				sink.Reply($"Joined match #{matchId} {match.Name}");
				return true;
			}

			sink.Reply("Failed to join the match.");
			return false;
		}
		finally
		{
			match.Lock.Release();
		}
	}

	/// <summary>
	///     Backs `!mp in [match_id]` — lets a referee target a match they aren't physically joined to,
	///     so `CommandDispatcher` can resolve `!mp` scope from <see cref="PlayerSession.MpScopeMatchId" />
	///     instead of the sender's current chat channel. No argument reports the current scope instead of
	///     setting one. Runs with no <see cref="MatchSession" /> yet, like `make`/`makeprivate`, since the
	///     whole point is reaching a match the sender isn't in.
	/// </summary>
	public bool SetScopeAsync(PlayerSession sender, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count == 0)
		{
			string scopeLine;
			bool scopeValid;
			if (sender.MpScopeMatchId is not { } currentId)
			{
				scopeLine = "You're not scoped to any match.";
				scopeValid = false;
			}
			else
			{
				var current = matchRegistry.GetByDbId(currentId);
				scopeLine = current is null
					? $"You were scoped to match #{currentId}, but it no longer exists."
					: $"Currently scoped to match #{current.DbId} {current.Name}.";
				scopeValid = current is not null;
			}

			var refereeMatches = matchRegistry.All.Where(m => m.IsReferee(sender.Id)).ToList();
			sink.Reply(refereeMatches.Count == 0
				? scopeLine
				: scopeLine + "\nYou're a referee of:\n" +
				  string.Join('\n', refereeMatches.Select(m => $"#{m.DbId} {m.Name}")));
			return scopeValid;
		}

		if (!int.TryParse(args[0], out var dbId))
		{
			sink.Reply("Usage: !mp in [match_id]");
			return false;
		}

		var match = matchRegistry.GetByDbId(dbId);
		if (match is null)
		{
			sink.Reply($"No active match with id #{dbId}.");
			return false;
		}

		if (!match.IsReferee(sender.Id))
		{
			sink.Reply($"You're not a referee of match #{dbId}.");
			return false;
		}

		sender.MpScopeMatchId = dbId;
		sink.Reply($"Now targeting match #{dbId} {match.Name}.");
		return true;
	}

	private static async Task<bool> RunLockedAsync(MatchSession match, Func<Task<bool>> action)
	{
		await match.Lock.WaitAsync();
		try
		{
			return await action();
		}
		finally
		{
			match.Lock.Release();
		}
	}

	/// <summary>
	///     Ported to match real Bancho's `!mp settings` output shape (one field per line, so each
	///     becomes its own chat message — see <see cref="SendPublicMessageHandler" />'s reply-splitting).
	///     Real Bancho links the room's `osu.ppy.sh/mp/{id}` history page and each player's profile —
	///     Basil has neither (no public match-history page, no profile pages), so those are plain
	///     text/IDs here instead of links.
	/// </summary>
	private async Task<bool> SettingsAsync(MatchSession match, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		var beatmapLine = match.MapId > 0
			? await mapRepository.FetchOneAsync(match.MapId, cancellationToken: cancellationToken) is { } bmap
				? $"Beatmap: {bmap.Id} {bmap.FullName}"
				: "Beatmap: Not found"
			: $"Beatmap: {match.MapId} {match.MapName}";

		var lines = new List<string>
		{
			$"Room name: {match.Name} (#{match.DbId})",
			beatmapLine,
			$"Team mode: {match.TeamType}, Win condition: {match.WinCondition}"
		};

		var activeMods = new List<string>();
		if (match.Mods != Mods.NoMod) activeMods.Add(match.Mods.ToString());
		if (match.Freemods) activeMods.Add("Freemod");
		if (activeMods.Count > 0) lines.Add($"Active mods: {string.Join(", ", activeMods)}");

		var occupied = match.Slots
			.Select((slot, i) => (slot, i))
			.Where(t => !t.slot.Empty)
			.ToList();
		lines.Add($"Players: {occupied.Count}");

		var hostSlotId = match.GetSlotId(match.HostId);
		foreach (var (slot, i) in occupied)
		{
			var tags = new List<string>();
			if (i == hostSlotId) tags.Add("Host");
			if (slot.Mods != Mods.NoMod) tags.Add(slot.Mods.ToString());
			var tagText = tags.Count > 0 ? $"[{string.Join(" / ", tags)}]" : "";

			var name = sessionRegistry.GetById(slot.PlayerId!.Value)?.Name ?? $"#{slot.PlayerId}";
			lines.Add($"Slot {i + 1}  {SlotStatusText(slot.Status)} {slot.PlayerId} {name,-16}{tagText}");
		}

		sink.Reply(string.Join('\n', lines));
		return true;
	}

	private static string SlotStatusText(SlotStatus status)
	{
		return status switch
		{
			SlotStatus.NotReady => "Not Ready",
			SlotStatus.NoMap => "No Map",
			_ => status.ToString()
		};
	}

	private bool SetRoomLocked(MatchSession match, bool locked, ICommandReplySink sink)
	{
		_matchControl.SetLocked(match, locked);
		sink.Reply(locked ? "Locked the match" : "Unlocked the match");
		return true;
	}

	private async Task<bool> SetSize(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var size))
		{
			sink.Reply("Usage: !mp size <1-16>");
			return false;
		}

		size = Math.Clamp(size, 1, 16);
		await _matchControl.SetSizeAsync(match, size);
		sink.Reply($"Changed match to size {size}");
		return true;
	}

	private async Task<bool> MoveSlot(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 2 || !int.TryParse(args[^1], out var destSlotId))
		{
			sink.Reply("Usage: !mp move <name> <slot 1-16>");
			return false;
		}

		destSlotId = Math.Clamp(destSlotId, 1, 16);

		var targetName = string.Join(' ', args.Take(args.Count - 1));
		var target = sessionRegistry.GetByName(targetName);
		if (target is null || target.Match != match)
		{
			sink.Reply($"{targetName} is not in this match.");
			return false;
		}

		var result = await _matchControl.MoveSlotAsync(match, target, destSlotId - 1);
		switch (result)
		{
			case MatchControlService.MoveResult.DestinationNotOpen:
				sink.Reply("Destination slot is not open.");
				return false;
			case MatchControlService.MoveResult.TargetNotInMatch:
				sink.Reply($"{targetName} is not in this match.");
				return false;
			default:
				sink.Reply($"Moved {target.Name} into slot {destSlotId}");
				return true;
		}
	}

	private async Task<bool> SetHost(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp host <name>");
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = sessionRegistry.GetByName(targetName);
		if (target is null || target.Match != match)
		{
			sink.Reply($"{targetName} is not in this match.");
			return false;
		}

		await _matchControl.SetHostAsync(match, target);
		sink.Reply($"Changed match host to {target.Name}");
		return true;
	}

	private async Task<bool> ClearHost(MatchSession match, ICommandReplySink sink)
	{
		await _matchControl.ClearHostAsync(match);
		sink.Reply("Cleared match host");
		return true;
	}

	private async Task<bool> SetName(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp name <text>");
			return false;
		}

		await _matchControl.SetNameAsync(match, string.Join(' ', args));
		sink.Reply($"Room name updated to \"{match.Name}\"");
		return true;
	}

	private async Task<bool> SetPassword(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		var password = args.Count == 0 ? "" : string.Join(' ', args);
		await _matchControl.SetPasswordAsync(match, password);
		sink.Reply(args.Count == 0 ? "Removed the match password" : "Changed the match password");
		return true;
	}

	private async Task<bool> SetPrivate(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count == 0)
		{
			sink.Reply($"This match is {(match.IsPrivate ? "private" : "not private")}.");
			return true;
		}

		if (args[0] is "0" or "1")
		{
			await _matchControl.SetPrivateAsync(match, args[0] == "1");
			sink.Reply(match.IsPrivate
				? "The match is now private. It will be hidden from the lobby."
				: "The match is now public.");
			return true;
		}

		sink.Reply("Usage: !mp private [0|1]");
		return false;
	}

	private bool Invite(PlayerSession sender, MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp invite <name>");
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = sessionRegistry.GetByName(targetName);
		if (target is null)
		{
			sink.Reply($"User not found: {targetName}");
			return false;
		}

		var result = _matchControl.Invite(sender, match, target);
		if (result == MatchControlService.InviteResult.TargetAlreadyInRoom)
		{
			sink.Reply("User is already in the room");
			return false;
		}

		sink.Reply($"Invited {target.Name} to the room");
		return true;
	}

	private async Task<bool> AddRefereeAsync(PlayerSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp addref <name>");
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = sessionRegistry.GetByName(targetName);
		if (target is null)
		{
			sink.Reply($"User not found: {targetName}");
			return false;
		}

		await _matchControl.AddRefereeAsync(sender.Id, sender.Name, match, target, cancellationToken);
		sink.Reply($"Added {target.Name} to the match referees");
		return true;
	}

	/// <summary>
	///     At least one referee must always remain — removing the last one is rejected instead of
	///     disbanding the room (see <see cref="MatchControlService.RemoveOneRefereeAsync" />).
	/// </summary>
	private async Task<bool> RemoveRefereeAsync(PlayerSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp removeref <name>");
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = sessionRegistry.GetByName(targetName);
		if (target is null)
		{
			sink.Reply($"User not found: {targetName}");
			return false;
		}

		var result =
			await _matchControl.RemoveOneRefereeAsync(sender.Id, sender.Name, match, target, cancellationToken);
		switch (result)
		{
			case MatchControlService.RemoveRefereeResult.WouldLeaveEmpty:
				sink.Reply($"Cannot remove {target.Name} — at least one referee must remain.");
				return false;
			case MatchControlService.RemoveRefereeResult.NotAReferee:
				sink.Reply($"{target.Name} is not a referee of this match.");
				return false;
			default:
				sink.Reply($"Removed {target.Name} from the match referees");
				return true;
		}
	}

	private bool ListReferees(MatchSession match, ICommandReplySink sink)
	{
		if (match.Referees.Count == 0)
		{
			sink.Reply("No referees");
			return true;
		}

		var names = match.Referees
			.Select(id => sessionRegistry.GetById(id)?.Name ?? $"#{id}")
			.ToList();
		sink.Reply("Match referees:\n" + string.Join('\n', names));
		return true;
	}

	private async Task<bool> BanListAsync(MatchSession match, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		if (match.BannedIds.Count == 0)
		{
			sink.Reply("No banned players");
			return true;
		}

		var names = new List<string>();
		foreach (var id in match.BannedIds)
		{
			var user = await userRepository.FetchByIdAsync(id, cancellationToken);
			names.Add(user?.Name ?? $"#{id}");
		}

		sink.Reply("Match bans:\n" + string.Join('\n', names));
		return true;
	}

	private async Task<bool> SetTeam(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 2)
		{
			sink.Reply("Usage: !mp team <name> <red|blue>");
			return false;
		}

		var teamArg = args[^1].ToLowerInvariant();
		if (teamArg is not ("red" or "blue"))
		{
			sink.Reply("Usage: !mp team <name> <red|blue>");
			return false;
		}

		var targetName = string.Join(' ', args.Take(args.Count - 1));
		var target = sessionRegistry.GetByName(targetName);
		if (target is null)
		{
			sink.Reply($"{targetName} is not in this match.");
			return false;
		}

		var team = teamArg == "red" ? MatchTeam.Red : MatchTeam.Blue;
		var result = await _matchControl.SetTeamAsync(match, target, team);
		if (result == MatchControlService.TeamResult.TargetNotInMatch)
		{
			sink.Reply($"{targetName} is not in this match.");
			return false;
		}

		var teamDisplay = char.ToUpperInvariant(teamArg[0]) + teamArg[1..];
		sink.Reply($"Moved {target.Name} to team {teamDisplay}");
		return true;
	}

	private async Task<bool> Set(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		const string usage = "Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 1-16]";

		if (args.Count < 1 || !TryParseTeamType(args[0], out var teamType))
		{
			sink.Reply(usage);
			return false;
		}

		MatchWinCondition? winCondition = null;
		if (args.Count >= 2)
		{
			if (!TryParseWinCondition(args[1], out var parsed))
			{
				sink.Reply(usage);
				return false;
			}

			winCondition = parsed;
		}

		int? size = null;
		if (args.Count >= 3)
		{
			if (!int.TryParse(args[2], out var parsedSize))
			{
				sink.Reply(usage);
				return false;
			}

			size = Math.Clamp(parsedSize, 1, 16);
		}

		await _matchControl.SetTeamTypeWinConditionAndSizeAsync(match, teamType, winCondition, size);
		sink.Reply($"Changed match settings to {match.TeamType}, {match.WinCondition}" +
		           (size is { } sz ? $", {sz} slots." : "."));
		return true;
	}

	private static bool TryParseTeamType(string arg, out MatchTeamType teamType)
	{
		teamType = default;
		if (!int.TryParse(arg, out var value) || value is < 0 or > 3) return false;

		teamType = (MatchTeamType)value;
		return true;
	}

	private static bool TryParseWinCondition(string arg, out MatchWinCondition winCondition)
	{
		winCondition = default;
		if (!int.TryParse(arg, out var value) || value is < 0 or > 3) return false;

		winCondition = (MatchWinCondition)value;
		return true;
	}

	private async Task<bool> SetMapAsync(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var beatmapId))
		{
			sink.Reply("Usage: !mp map <beatmap id>");
			return false;
		}

		var (result, bmap) = await _matchControl.SetMapAsync(match, beatmapId, cancellationToken);
		if (result == MatchControlService.SetMapResult.BeatmapNotFound)
		{
			sink.Reply($"No beatmap with id {beatmapId} found locally.");
			return false;
		}

		sink.Reply($"Changed beatmap to {bmap!.Mapset.Artist} - {bmap.Mapset.Title}");
		return true;
	}

	/// <summary>
	///     Ported to match real Bancho exactly: `!mp mods` is the ONLY mod-setting command — freemod is
	///     just one of the values it accepts (`!mp mods Freemod`), not a separate `!mp freemods` toggle.
	///     `None` clears the mods (and freemod, if on).
	/// </summary>
	private async Task<bool> SetMods(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp mods <mods>|Freemod|None");
			return false;
		}

		if (args.Any(a => a.Equals("Freemod", StringComparison.OrdinalIgnoreCase)))
		{
			await _matchControl.SetModsAsync(match, Mods.NoMod, true);
			sink.Reply("Enabled FreeMod");
			return true;
		}

		var before = match.Mods;
		var wasFreemod = match.Freemods;

		var mods = Mods.NoMod;
		foreach (var token in args)
		{
			if (token.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
			mods |= ModsExtensions.FromModString(token);
		}

		await _matchControl.SetModsAsync(match, mods, false);
		sink.Reply(DescribeModChange(before, mods, wasFreemod));
		return true;
	}

	/// <summary>
	///     Reports which mods this call turned on/off relative to the match's previous global mods,
	///     plus the current freemod state — matches real Bancho's wording for the plain (non-`Freemod`)
	///     case. Real Bancho's diff also covers mixing a mod list with the `Freemod` keyword in one
	///     call; that combined case isn't reproduced here (see the `Freemod` branch above, which just
	///     reports "Enabled FreeMod" instead).
	/// </summary>
	private static string DescribeModChange(Mods before, Mods after, bool wasFreemod)
	{
		var enabled = after & ~before;
		var disabled = before & ~after;

		var parts = new List<string>();
		if (enabled != Mods.NoMod) parts.Add($"Enabled {enabled}");
		if (disabled != Mods.NoMod) parts.Add($"Disabled {disabled}");
		if (wasFreemod) parts.Add("disabled FreeMod");

		return parts.Count > 0 ? string.Join(", ", parts) : "No mod changes";
	}

	private async Task<bool> StartAsync(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		int? countdownSeconds = args.Count > 0 && int.TryParse(args[0], out var seconds) && seconds > 0
			? seconds
			: null;

		var result = await _matchControl.StartAsync(match, countdownSeconds, cancellationToken);
		switch (result)
		{
			case MatchControlService.StartResult.AlreadyInProgress:
				sink.Reply("Match is already in progress.");
				return false;
			case MatchControlService.StartResult.CountdownQueued:
				sink.Reply($"Match starts in {countdownSeconds} seconds");
				return true;
			case MatchControlService.StartResult.Started:
				sink.Reply("Match started");
				return true;
			default:
				sink.Reply("Match cannot start because the beatmap does not exist on the server.");
				return false;
		}
	}

	private bool Timer(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		var seconds = 30;
		if (args.Count > 0 && (!int.TryParse(args[0], out seconds) || seconds <= 0))
		{
			sink.Reply("Usage: !mp timer [seconds]");
			return false;
		}

		_matchControl.Timer(match, seconds);
		sink.Reply($"Countdown started: {seconds} seconds");
		return true;
	}

	private bool AbortTimer(MatchSession match, ICommandReplySink sink)
	{
		var result = _matchControl.AbortTimer(match);
		if (result == MatchControlService.AbortTimerResult.NoTimerRunning)
		{
			sink.Reply("No countdown is running.");
			return false;
		}

		sink.Reply("Countdown aborted");
		return true;
	}

	private async Task<bool> AbortAsync(MatchSession match, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		var result = await _matchControl.AbortAsync(match, cancellationToken);
		if (result == MatchControlService.AbortResult.NotInProgress)
		{
			sink.Reply("Match is not in progress.");
			return false;
		}

		sink.Reply("Aborted the match");
		return true;
	}

	private async Task<bool> KickAsync(PlayerSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp kick <name>");
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = sessionRegistry.GetByName(targetName);
		if (target is null || target.Match != match)
		{
			sink.Reply($"{targetName} is not in this match.");
			return false;
		}

		await _matchControl.KickAsync(sender.Id, sender.Name, match, target, cancellationToken);
		sink.Reply($"Kicked {target.Name} from the match");
		return true;
	}

	private async Task<bool> BanAsync(PlayerSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp ban <name>");
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = sessionRegistry.GetByName(targetName);
		if (target is null || target.Match != match)
		{
			sink.Reply($"{targetName} is not in this match.");
			return false;
		}

		await _matchControl.BanAsync(sender.Id, sender.Name, match, target, cancellationToken);
		sink.Reply($"Banned {target.Name} from the match");
		return true;
	}

	private async Task<bool> UnbanAsync(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !mp unban <name>");
			return false;
		}

		var targetName = string.Join(' ', args);
		var targetUser = await userRepository.FetchByNameAsync(targetName, cancellationToken);
		if (targetUser is null)
		{
			sink.Reply($"{targetName} is not registered.");
			return false;
		}

		var result = await _matchControl.UnbanAsync(match, targetUser.Id);
		if (result == MatchControlService.UnbanResult.NotBanned)
		{
			sink.Reply($"{targetUser.Name} is not banned from this match.");
			return false;
		}

		sink.Reply($"Unbanned {targetUser.Name} from the match");
		return true;
	}

	private async Task<bool> CloseAsync(PlayerSession sender, MatchSession match, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		await _matchControl.CloseAsync(sender.Id, sender.Name, match, cancellationToken);
		sink.Reply("Closed the match");
		return true;
	}

	/// <summary>
	///     Forwards to <see cref="MatchControlService.ComputeAnnounceCheckpoints" /> — kept here too since
	///     existing tests reference it as <c>MpCommandService.ComputeAnnounceCheckpoints</c>.
	/// </summary>
	public static IReadOnlyList<int> ComputeAnnounceCheckpoints(int totalSeconds, bool autoStart = true)
	{
		return MatchControlService.ComputeAnnounceCheckpoints(totalSeconds, autoStart);
	}

	/// <summary>One entry in the auto-generated `!mp help` listing — usage plus a one-line description.</summary>
	private readonly record struct CommandInfo(string Usage, string Description);
}
