using System.Collections.Frozen;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Backgrounds;
using Basil.Application.Packets.Channels;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Bot;

/// <summary>
///     Implements the <c>!mp</c> chat subcommands for match control.
/// </summary>
/// <remarks>
///     The subcommand set is matched against the official osu! multiplayer server's own chat
///     behavior; see docs/for-developers/working-scopes.md for what is a deliberate Basil-only
///     addition versus a real Bancho command. <c>makeprivate</c> is a Basil-only variant of <c>make</c> (see
///     <see cref="MakeAsync" />) that creates the room already private; <c>!mp private [0|1]</c> is
///     the way to view or change privacy on a room that already exists. <c>!mp join &lt;id&gt;</c>
///     bypasses the referee gate; it is routed directly from <see cref="CommandDispatcher" />.
///     A read-only subcommand (<c>settings</c>, <c>listrefs</c>, <c>banlist</c>, and <c>private</c>
///     with no argument) runs for anyone the match resolves for; every mutating subcommand requires
///     <see cref="MatchSession.IsReferee" />, and unmet permission is answered with an error reply
///     through the sink the caller provided. Referee is a pure permission flag that does not
///     require physical presence in the room,
///     and the host is not automatically a referee: hosting only grants direct in-client settings
///     control, which ranks below referee authority for <c>!mp</c> purposes.
///     Every method sends its own reply through the <see cref="ICommandReplySink" /> passed in and
///     returns only success or failure; nothing here returns reply text for a caller to route. The
///     actual room-mutation logic lives in <see cref="MatchControlService" />, shared with the
///     <c>api.</c> host's HTTP writes routes so both surfaces call the identical state-mutation and
///     broadcast code. This class owns everything chat-specific: parsing raw argument tokens,
///     resolving a target userSession by name via <see cref="ISessionRegistry{TSession}.GetByName" />, the
///     referee gate, and sending a reply for the result.
/// </remarks>
public sealed class MpCommandService(
	MatchMembershipService matchMembership,
	IMatchRegistry matchRegistry,
	IMatchRepository matchRepository,
	IMatchRoundEndOutbox roundEndOutbox,
	IBeatmapRepository beatmapRepository,
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	IUserRepository userRepository,
	IChannelRegistry channelRegistry,
	ILogger<MpCommandService> logger,
	ILogger<MatchControlService> matchControlLogger)
{
	/// <summary>Max osu! client match name length</summary>
	private const int MaxMatchNameLength = 50;

	/// <summary>Safe under a 512-byte IRC line limit even with the longest wire prefix/command.</summary>
	private const int MaxSettingsLineBytes = 400;

	/// <summary>Subcommands that only report state — runnable by anyone the match resolves for, not just referees.</summary>
	private static readonly FrozenSet<string> ReadOnlySubcommands =
		new[] { "settings", "listrefs", "banlist" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     The <c>!mp</c> subcommands listed by <c>!mp help</c>, the single source of truth for that
	///     output.
	/// </summary>
	/// <remarks>
	///     Add a subcommand here, and it appears in <c>!mp help</c> with no separate help string to
	///     keep in sync. <c>make</c>, <c>join</c>, <c>in</c>, and <c>makeprivate</c> are not listed here;
	///     they run outside this class's subcommand switch, routed directly from
	///     <see cref="CommandDispatcher" />, which lists them in its own <c>!help</c>.
	/// </remarks>
	private static readonly CommandInfo[] Commands =
	[
		new("!mp settings", "show match id, map, team type, win condition, mods, and slots"),
		new("!mp lock", "lock the room, blocking new joins"),
		new("!mp unlock", "unlock the room"),
		new("!mp private [0|1]", "show or set the room's private status (hidden from lobby, invite-only)"),
		new("!mp size <1-16>", "set the number of available slots"),
		new("!mp move <name/id> <slot 1-16>", "move a userSession to another slot"),
		new("!mp host <name/id>", "transfer host to another userSession"),
		new("!mp clearhost", "clear the current host"),
		new("!mp name <text>", "rename the match"),
		new("!mp password [text]", "set the room password; omit to clear it"),
		new("!mp invite <name>", "invite an online userSession"),
		new("!mp addref <name>", "add a referee (creator only)"),
		new("!mp removeref <name>", "remove a referee (creator only)"),
		new("!mp listrefs", "list current referees"),
		new("!mp banlist", "list players banned from this match"),
		new("!mp team <name> <red|blue>", "assign a userSession's team\nTeam: Red, Blue"),
		new("!mp map <beatmap id>", "change the selected map"),
		new("!mp mods <mods>|Freemod|None",
			"set the match mods\nMods: NF, EZ, HD, HR, SD, DT, RX, HT, NC, FL, SO, AP, PF, Freemod, None"),
		new("!mp set <teammode 0-3> [scoremode 0-3] [size 1-16]",
			"set team type, win condition, and size at once\n" +
			"Teammode 0: HeadToHead, 1: TagCoop, 2: TeamVs, 3: TagTeamVs\n" +
			"Scoremode 0: Score, 1: Accuracy, 2: Combo, 3: ScoreV2"),
		new("!mp start [seconds]", "start now, or after a countdown"),
		new("!mp timer [seconds]", "start a countdown without auto-starting"),
		new("!mp aborttimer", "cancel a running countdown"),
		new("!mp abort", "abort the match in progress"),
		new("!mp kick <name>", "remove a userSession from the room"),
		new("!mp ban <name>", "kick and block a userSession from rejoining"),
		new("!mp unban <name>", "allow a banned userSession to rejoin"),
		new("!mp close", "close the match immediately")
	];

	/// <summary>The <c>!mp help</c> text, built once from <see cref="Commands" />.</summary>
	internal static readonly string HelpText = string.Join('\n', Commands.Select(c => $"{c.Usage} - {c.Description}"));

	private readonly MatchControlService _matchControl =
		new(matchMembership, matchRepository, roundEndOutbox, beatmapRepository, gameRegistry, ircRegistry,
			matchControlLogger);

	/// <summary>
	///     Dispatches a <c>!mp</c> subcommand against a resolved match.
	/// </summary>
	/// <remarks>
	///     This is the main subcommand switch. Help bypasses the referee gate and is always delivered
	///     by DM (see <see cref="ICommandReplySink.ReplyDm" />); every other subcommand requires
	///     <see cref="MatchSession.IsReferee" /> and is otherwise rejected with an error reply, which
	///     the caller routes to a DM when the command did not come from the match's own channel.
	///     <c>addref</c>/<c>removeref</c> carry a further restriction on top of the referee gate: only
	///     the match's creator (<see cref="MatchSession.IsCreator" />) may run them, so an ordinary
	///     referee can never grant or revoke referee status on anyone.
	/// </remarks>
	/// <param name="sender">The userSession issuing the subcommand.</param>
	/// <param name="match">The match the subcommand operates on.</param>
	/// <param name="subcommand">The subcommand name without its <c>!mp</c> prefix.</param>
	/// <param name="args">The raw argument tokens following the subcommand.</param>
	/// <param name="sink">The destination for the subcommand's reply.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     A value that indicates whether the subcommand was recognized and ran successfully.
	/// </returns>
	public async Task<bool> TryHandleAsync(UserSession sender, MatchSession match, string subcommand,
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

		var readOnly = ReadOnlySubcommands.Contains(subcommand) || (subcommand == "private" && args.Count == 0);
		if (!readOnly && !match.IsReferee(sender.Id))
		{
			logger.LogDebug("Subcommand rejected: {UserId} is not a referee of MatchId={MatchId}", sender.Id,
				match.DbId);
			sink.Reply(string.Format(MpReplies.NotARefereeOfMatch, match.DbId));
			return false;
		}

		if (subcommand is "addref" or "removeref" && !match.IsCreator(sender.Id))
		{
			logger.LogDebug("Subcommand rejected: {UserId} is not the creator of MatchId={MatchId}", sender.Id,
				match.DbId);
			sink.Reply(string.Format(MpReplies.CreatorOnlyMp, $"!mp {subcommand}"));
			return false;
		}

		return subcommand switch
		{
			"settings" => await SettingsAsync(match, sink, cancellationToken),
			"lock" => await RunLockedAsync(match, () => Task.FromResult(SetRoomLocked(match, true, sink))),
			"unlock" => await RunLockedAsync(match, () => Task.FromResult(SetRoomLocked(match, false, sink))),
			"private" => await SetPrivate(match, args, sink),
			"size" => await RunLockedAsync(match, () => SetSize(match, args, sink)),
			"move" => await RunLockedAsync(match, () => MoveSlot(match, args, sink)),
			"host" => await RunLockedAsync(match, () => SetHost(match, args, sink)),
			"clearhost" => await RunLockedAsync(match, () => ClearHost(match, sink)),
			"name" => await RunLockedAsync(match, () => SetName(match, args, sink)),
			"password" => await RunLockedAsync(match, () => SetPassword(match, args, sink)),
			"invite" => await RunLockedAsync(match, () => Task.FromResult(Invite(sender, match, args, sink))),
			"addref" => await RunLockedAsync(match,
				() => AddRefereeAsync(sender, match, args, sink, cancellationToken)),
			"removeref" => await RunLockedAsync(match,
				() => RemoveRefereeAsync(sender, match, args, sink, cancellationToken)),
			"listrefs" => ListReferees(match, sink),
			"banlist" => BanListAsync(match, sink),
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
			_ => UnknownSubcommand(sink, subcommand)
		};
	}

	/// <summary>
	///     Backs <c>!mp make</c> and <c>!mp makeprivate</c>, creating a tournament room and scoping the
	///     creator to it.
	/// </summary>
	/// <remarks>
	///     Unlike every other subcommand this runs with no <see cref="MatchSession" /> yet, since there
	///     is nothing to be a referee of; it bypasses <see cref="TryHandleAsync" /> entirely and reuses
	///     <see cref="MatchMembershipService.CreateAsync" /> verbatim, exactly like a client-created
	///     match, except the creator is also auto-added as a referee so the room passes the
	///     <see cref="MatchSession.IsReferee" /> gate. An empty room (whether created this way or by a
	///     client that later left) auto-closes after 5 minutes of inactivity rather than tearing down
	///     the instant it empties. <paramref name="isPrivate" /> marks the room private
	///     (<see cref="MatchSession.IsPrivate" />) at creation, distinct from <c>!mp private</c>, which
	///     toggles it on an existing room.
	/// </remarks>
	/// <param name="sender">The userSession creating the room.</param>
	/// <param name="args">The argument tokens forming the room name.</param>
	/// <param name="sink">The destination for the creation reply.</param>
	/// <param name="isPrivate">
	///     <see langword="true" /> to create the room already hidden from the lobby and invite-only
	///     (backs <c>!mp makeprivate</c>); otherwise, <see langword="false" />.
	/// </param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	public async Task<bool> MakeAsync(UserSession sender, IReadOnlyList<string> args, ICommandReplySink sink,
		bool isPrivate = false, CancellationToken cancellationToken = default)
	{
		var name = args.Count > 0 ? string.Join(' ', args) : $"{sender.Name}'s match";
		if (name.Length > MaxMatchNameLength) name = name[..MaxMatchNameLength];

		var data = new MatchState(
			0, false, 0, 0, name, "",
			"", 0, "",
			[], [], [], sender.Id, 0,
			0, 0, false, [], 0);

		var match = await matchMembership.CreateAsync(sender, data, cancellationToken);
		if (match is null)
		{
			sink.Reply(MpReplies.CreateFailed);
			return false;
		}

		match.AddReferee(sender.Id);
		if (isPrivate) await _matchControl.SetPrivateAsync(match, true, cancellationToken);
		sender.MpScopeMatchId = match.DbId;
		sink.Reply(string.Format(MpReplies.CreatedMatch, match.DbId, match.Name, isPrivate ? " (private)" : ""));
		return true;
	}

	/// <summary>
	///     Backs <c>!mp join &lt;id&gt; [password]</c>, letting any userSession join a match by its
	///     persistent room id.
	/// </summary>
	/// <remarks>
	///     The match is resolved by <see cref="MatchSession.DbId" />, the unbounded persistent id
	///     rather than the 0-63 wire-format slot id. For a <see cref="GameSession" />, a private match
	///     rejects everyone but staff and invitees, the host included (see
	///     <see cref="MatchSession.IsPrivate" />); locked rooms and banned players are also rejected,
	///     each with a descriptive reply. An <see cref="IrcSession" /> can never occupy a slot, so this
	///     joins the match's chat channel instead — see <see cref="JoinChatOnly" /> for that gate. The
	///     command runs with no <see cref="MatchSession" /> scope: it is routed directly from
	///     <see cref="CommandDispatcher" />, bypassing scope resolution.
	/// </remarks>
	/// <param name="sender">The userSession joining.</param>
	/// <param name="args">The argument tokens: the match id and an optional password.</param>
	/// <param name="sink">The destination for the join reply.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>A value that indicates whether the userSession joined.</returns>
	public async Task<bool> JoinAsync(UserSession sender, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken = default)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var matchId))
		{
			sink.Reply(MpReplies.JoinUsage);
			return false;
		}

		var match = matchRegistry.GetByDbId(matchId);
		if (match is null)
		{
			sink.Reply(string.Format(MpReplies.NoActiveMatchWithId, matchId));
			return false;
		}

		if (sender is not GameSession gameSender)
			return JoinChatOnly(sender, match, args, sink);

		if (match.IsPrivate && (gameSender.Privilege & UserPrivileges.Staff) == 0 &&
		    !match.InvitedIds.Contains(gameSender.Id))
		{
			sink.Reply(string.Format(MpReplies.PrivateRoomJoinDenied, matchId));
			return false;
		}

		if (gameSender.Match is not null)
		{
			sink.Reply(MpReplies.AlreadyInAMatch);
			return false;
		}

		if (match.BannedIds.Contains(gameSender.Id))
		{
			sink.Reply(MpReplies.BannedFromMatch);
			return false;
		}

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : "";
			var joined = await matchMembership.JoinAsync(gameSender, match, password, cancellationToken);
			if (joined == MatchMembershipService.JoinResult.Ok)
			{
				sink.Reply(string.Format(MpReplies.JoinedMatch, matchId, match.Name));
				return true;
			}

			sink.Reply(joined switch
			{
				MatchMembershipService.JoinResult.WrongPassword => MpReplies.IncorrectPassword,
				MatchMembershipService.JoinResult.NoFreeSlot => MpReplies.MatchIsFull,
				MatchMembershipService.JoinResult.Locked => MpReplies.MatchIsLocked,
				MatchMembershipService.JoinResult.Banned => MpReplies.BannedFromMatch,
				_ => MpReplies.FailedToJoinMatch
			});
			return false;
		}
		finally
		{
			match.Lock.Release();
		}
	}

	/// <summary>
	///     Backs <c>!mp join &lt;id&gt; [password]</c> for an <see cref="IrcSession" /> sender, joining
	///     the match's chat channel instead of a slot — an IRC connection can never be seated.
	/// </summary>
	/// <remarks>
	///     A referee (which already includes the match's creator, see <see cref="MatchSession.IsReferee" />)
	///     bypasses both the private-room gate and the password check. A locked room only ever admits an
	///     invitee regardless of referee status — matching the in-client seat-join path, where a lock
	///     blocks everyone unconditionally. A ban is never bypassable by anyone.
	/// </remarks>
	/// <param name="sender">The IRC session joining.</param>
	/// <param name="match">The match to join.</param>
	/// <param name="args">The argument tokens: the match id and an optional password.</param>
	/// <param name="sink">The destination for the join reply.</param>
	/// <returns>A value that indicates whether the sender joined the match's chat channel.</returns>
	private bool JoinChatOnly(UserSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink)
	{
		if (match.BannedIds.Contains(sender.Id))
		{
			sink.Reply(MpReplies.BannedFromMatch);
			return false;
		}

		if (match.IsLocked && !match.InvitedIds.Contains(sender.Id))
		{
			sink.Reply(MpReplies.MatchIsLocked);
			return false;
		}

		if (match.IsPrivate && !match.IsReferee(sender.Id) && !match.InvitedIds.Contains(sender.Id))
		{
			sink.Reply(string.Format(MpReplies.PrivateRoomJoinDenied, match.DbId));
			return false;
		}

		var password = args.Count > 1 ? string.Join(' ', args.Skip(1)) : "";
		if (password != match.Password && !match.IsReferee(sender.Id))
		{
			sink.Reply(MpReplies.IncorrectPassword);
			return false;
		}

		if (!matchMembership.JoinMatchChat(sender, match, true))
		{
			sink.Reply(MpReplies.FailedToJoinMatchChat);
			return false;
		}

		sink.Reply(string.Format(MpReplies.JoinedMatchChat, match.DbId, match.Name));
		return true;
	}

	/// <summary>
	///     Backs <c>!mp in [match_id]</c>, targeting a match the sender is not physically joined to.
	/// </summary>
	/// <remarks>
	///     Lets a referee point <c>!mp</c> scope at a specific match via
	///     <see cref="UserSession.MpScopeMatchId" /> instead of the sender's current chat channel,
	///     so the dispatcher can resolve later commands against it. With no argument it reports
	///     the current scope, plus every match the sender is a referee of, rather than setting one. It
	///     runs with no <see cref="MatchSession" /> yet, since the point is reaching a match the
	///     sender is not in.
	/// </remarks>
	/// <param name="sender">The userSession issuing the command.</param>
	/// <param name="args">The argument tokens: an optional match id.</param>
	/// <param name="sink">The destination for the reply.</param>
	/// <returns>
	///     A value that indicates whether the requested scope was set, or whether the reported scope
	///     still exists when no argument was given.
	/// </returns>
	public bool SetScopeAsync(UserSession sender, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count == 0)
		{
			string scopeLine;
			bool scopeValid;
			if (sender.MpScopeMatchId is not { } currentId)
			{
				scopeLine = MpReplies.NotScopedToAnyMatch;
				scopeValid = false;
			}
			else
			{
				var current = matchRegistry.GetByDbId(currentId);
				scopeLine = current is null
					? string.Format(MpReplies.WasScopedToGoneMatch, currentId)
					: string.Format(MpReplies.CurrentlyScopedToMatch, current.DbId, current.Name);
				scopeValid = current is not null;
			}

			var refereeMatches = matchRegistry.All.Where(m => m.IsReferee(sender.Id)).ToList();
			sink.Reply(refereeMatches.Count == 0
				? scopeLine
				: scopeLine + "\n" + MpReplies.YouAreARefereeOf + "\n" +
				  string.Join('\n', refereeMatches.Select(m => $"#{m.DbId} {m.Name}")));
			return scopeValid;
		}

		if (!int.TryParse(args[0], out var dbId))
		{
			sink.Reply(MpReplies.InUsage);
			return false;
		}

		var match = matchRegistry.GetByDbId(dbId);
		if (match is null)
		{
			sink.Reply(string.Format(MpReplies.NoActiveMatchWithHashId, dbId));
			return false;
		}

		if (!match.IsReferee(sender.Id))
		{
			sink.Reply(string.Format(MpReplies.NotARefereeOfMatch, dbId));
			return false;
		}

		sender.MpScopeMatchId = dbId;
		sink.Reply(string.Format(MpReplies.NowTargetingMatch, dbId, match.Name));
		return true;
	}

	/// <summary>
	///     Replies to an unrecognized <c>!mp</c> subcommand.
	/// </summary>
	/// <param name="sink">The destination for the reply.</param>
	/// <param name="subcommand">The unrecognized subcommand name.</param>
	/// <returns>Always <see langword="false" />, the "did not run" result.</returns>
	private static bool UnknownSubcommand(ICommandReplySink sink, string subcommand)
	{
		sink.Reply(string.Format(MpReplies.UnknownMpSubcommand, subcommand));
		return false;
	}

	/// <summary>
	///     Executes a subcommand action while holding <see cref="MatchSession.Lock" />.
	/// </summary>
	/// <param name="match">The match whose lock is held for the duration.</param>
	/// <param name="action">The read-mutate-broadcast action to run under the lock.</param>
	/// <returns>The action's result.</returns>
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
	///     Builds the <c>!mp settings</c> report, one field per line so each line becomes its own chat
	///     message.
	/// </summary>
	/// <remarks>
	///     The one-field-per-line layout matches how the client displays multi-line chat messages (see
	///     <see cref="SendPublicMessageHandler" />'s reply splitting). The official server links the
	///     room's history page and each userSession's profile; Basil has neither a public match-history page
	///     nor profile pages, so those are plain text and ids here instead of links.
	/// </remarks>
	private async Task<bool> SettingsAsync(MatchSession match, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		var beatmapLine = match.MapId > 0
			? await beatmapRepository.FetchOneAsync(match.MapId, cancellationToken: cancellationToken) is { } bmap
				? string.Format(MpReplies.SettingsBeatmap, bmap.Id, bmap.FullName)
				: MpReplies.SettingsBeatmapNotFound
			: string.Format(MpReplies.SettingsBeatmap, match.MapId, match.MapName);

		var lines = new List<string>
		{
			string.Format(MpReplies.SettingsRoomName, match.Name, match.DbId),
			beatmapLine,
			string.Format(MpReplies.SettingsTeamMode, match.TeamType, match.WinCondition)
		};

		var activeMods = new List<string>();
		if (match.Mods != Mods.NoMod) activeMods.Add(match.Mods.ToString());
		if (match.Freemods) activeMods.Add("Freemod");
		if (activeMods.Count > 0)
			lines.Add(string.Format(MpReplies.SettingsActiveMods, string.Join(", ", activeMods)));

		if (match.CreatorId is { } creatorId)
		{
			var creatorName =
				((UserSession?)gameRegistry.GetByUserId(creatorId) ?? ircRegistry.GetByUserId(creatorId))?.Name
				?? (await userRepository.FetchByIdAsync(creatorId, cancellationToken))?.Name; // maybe offline
			lines.Add(string.Format(MpReplies.SettingsCreator, creatorId, creatorName ?? "?"));
		}

		var occupied = match.Slots
			.Select((slot, i) => (slot, i))
			.Where(t => !t.slot.Empty)
			.ToList();
		lines.Add(string.Format(MpReplies.SettingsPlayers, occupied.Count));

		var hostSlotId = match.GetSlotId(match.HostId);
		var showTeam = match.TeamType is MatchTeamType.TeamVs or MatchTeamType.TagTeamVs;

		foreach (var (slot, i) in occupied)
		{
			var tags = new List<string>();
			if (i == hostSlotId) tags.Add("Host");
			if (slot.Mods != Mods.NoMod) tags.Add(slot.Mods.ToString());
			var tagText = tags.Count > 0 ? $" [{string.Join(" / ", tags)}]" : "";
			var name = ((UserSession?)gameRegistry.GetByUserId(slot.PlayerId!.Value) ??
			            ircRegistry.GetByUserId(slot.PlayerId!.Value))
			           ?.Name
			           ?? $"#{slot.PlayerId}";
			var teamText = showTeam ? $"{slot.Team,-5} " : string.Empty;

			lines.Add(
				$"Slot {i + 1,2}  {SlotStatusText(slot.Status),-10} {teamText}{slot.PlayerId,6} {name,-16}{tagText}");
		}

		//* Refs — independent of Players: a referee may or may not also occupy a slot.
		// var refNames = new List<string>();
		// foreach (var id in match.Referees.OrderBy(id => id))
		// {
		// 	var user = await userRepository.FetchByIdAsync(id, cancellationToken); // referee can be offline
		// 	refNames.Add(user != null ? $"#{id} {user.Name}" : $"#{id}");
		// }
		//
		// lines.Add($"Refs: {refNames.Count}");
		// lines.AddRange(WrapCsv(refNames, MaxSettingsLineBytes));

		// IRC — independent of Players/Refs: anyone with a live IrcSession in the match's own channel,
		// whether they also occupy a slot or hold referee status.
		var ircNames = (channelRegistry.GetByName(match.ChatChannelName)?.MemberIds ?? [])
			.Select(ircRegistry.GetByUserId)
			.Where(s => s is not null)
			.Cast<IrcSession>()
			.OrderBy(s => s.Id)
			.Select(s => $"#{s.Id} {s.Name}")
			.ToList();
		if (ircNames.Count > 0)
		{
			lines.Add(string.Format(MpReplies.SettingsIrc, ircNames.Count));
			lines.AddRange(WrapCsv(ircNames, MaxSettingsLineBytes));
		}

		sink.Reply(string.Join('\n', lines));
		return true;
	}

	/// <summary>
	///     Joins comma-separated items into as few lines as possible, each at most
	///     <paramref name="maxLineBytes" /> UTF-8 bytes, so a chat line never exceeds the IRC wire
	///     limit. An individual item longer than the limit on its own is still emitted whole on its
	///     own line rather than split mid-item, which would corrupt a name or a UTF-8 sequence.
	/// </summary>
	private static IEnumerable<string> WrapCsv(IReadOnlyList<string> items, int maxLineBytes)
	{
		var line = "";
		foreach (var item in items)
		{
			var candidate = line.Length == 0 ? item : $"{line}, {item}";
			if (Encoding.UTF8.GetByteCount(candidate) > maxLineBytes && line.Length > 0)
			{
				yield return line;
				line = item;
			}
			else
			{
				line = candidate;
			}
		}

		if (line.Length > 0) yield return line;
	}

	/// <summary>Formats a <see cref="SlotStatus" /> as the friendly text <c>!mp settings</c> displays.</summary>
	/// <param name="status">The slot status to format.</param>
	/// <returns>
	///     The friendly label, or the status's own name when no friendlier label exists.
	/// </returns>
	private static string SlotStatusText(SlotStatus status)
	{
		return status switch
		{
			SlotStatus.NotReady => "Not Ready",
			SlotStatus.NoMap => "No Map",
			_ => status.ToString()
		};
	}

	/// <summary>Implements <c>!mp lock</c> and <c>!mp unlock</c>, locking or unlocking the match.</summary>
	/// <param name="match">The match to lock or unlock.</param>
	/// <param name="locked">The new locked state.</param>
	/// <param name="sink">The destination for the reply.</param>
	/// <returns>
	///     <see langword="true" />; the action always succeeds.
	/// </returns>
	private static bool SetRoomLocked(MatchSession match, bool locked, ICommandReplySink sink)
	{
		MatchControlService.SetLocked(match, locked);
		sink.Reply(locked ? MpReplies.LockedMatch : MpReplies.UnlockedMatch);
		return true;
	}

	/// <summary>Implements <c>!mp size &lt;1-16&gt;</c>, resizing the room's slot count.</summary>
	private async Task<bool> SetSize(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var size))
		{
			sink.Reply(MpReplies.SizeUsage);
			return false;
		}

		size = Math.Clamp(size, 1, 16);
		await _matchControl.SetSizeAsync(match, size);
		sink.Reply(string.Format(MpReplies.ChangedMatchSize, size));
		return true;
	}

	/// <summary>Implements <c>!mp move &lt;name&gt; &lt;slot&gt;</c>, moving a userSession to another slot.</summary>
	private async Task<bool> MoveSlot(
		MatchSession match,
		IReadOnlyList<string> args,
		ICommandReplySink sink)
	{
		if (args.Count < 2 || !int.TryParse(args[^1], out var destSlotId))
		{
			sink.Reply(MpReplies.MoveUsage);
			return false;
		}

		destSlotId = Math.Clamp(destSlotId, 1, 16);

		var rawTarget = string.Join(' ', args.Take(args.Count - 1));
		var target = ParseUserSession(rawTarget);
		if (target is null)
		{
			sink.Reply(MpReplies.UserNotInMatchOrUnregistered);
			return false;
		}

		var result = await _matchControl.MoveSlotAsync(match, target, destSlotId - 1);

		return result switch
		{
			MatchControlService.MoveResult.DestinationNotOpen =>
				Reply(MpReplies.DestinationSlotNotOpen),

			MatchControlService.MoveResult.TargetNotInMatch =>
				Reply(string.Format(MpReplies.NotInThisMatch, target.Name)),

			_ =>
				Reply(string.Format(MpReplies.MovedToSlot, target.Name, destSlotId))
		};

		bool Reply(string message)
		{
			sink.Reply(message);
			return result is not MatchControlService.MoveResult.DestinationNotOpen
				and not MatchControlService.MoveResult.TargetNotInMatch;
		}
	}

	/// <summary>Implements <c>!mp host &lt;name&gt;</c>, transferring host to another userSession in the room.</summary>
	private async Task<bool> SetHost(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.HostUsage);
			return false;
		}

		var rawTarget = string.Join(' ', args);
		var target = ParseUserSession(rawTarget);
		if (target is not GameSession gameTarget || gameTarget.Match != match)
		{
			sink.Reply(MpReplies.UserNotInMatchOrUnregistered);
			return false;
		}

		await _matchControl.SetHostAsync(match, gameTarget);
		sink.Reply(string.Format(MpReplies.ChangedMatchHost, gameTarget.Name));
		return true;
	}

	/// <summary>Implements <c>!mp clearhost</c>, clearing the match's current host.</summary>
	private async Task<bool> ClearHost(MatchSession match, ICommandReplySink sink)
	{
		await _matchControl.ClearHostAsync(match);
		sink.Reply(MpReplies.ClearedMatchHost);
		return true;
	}

	/// <summary>Implements <c>!mp name &lt;text&gt;</c>, renaming the match.</summary>
	private async Task<bool> SetName(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.NameUsage);
			return false;
		}

		await _matchControl.SetNameAsync(match, string.Join(' ', args));
		sink.Reply(string.Format(MpReplies.RoomNameUpdated, match.Name));
		return true;
	}

	/// <summary>Implements <c>!mp password [text]</c>, setting or clearing the room password.</summary>
	private async Task<bool> SetPassword(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		var password = args.Count == 0 ? "" : string.Join(' ', args);
		await _matchControl.SetPasswordAsync(match, password);
		sink.Reply(args.Count == 0 ? MpReplies.RemovedMatchPassword : MpReplies.ChangedMatchPassword);
		return true;
	}

	/// <summary>
	///     Implements <c>!mp private [0|1]</c>, viewing or changing the room's privacy.
	/// </summary>
	/// <remarks>
	///     With no argument it reports the current state. The <c>makeprivate</c> trigger also routes
	///     here, passing <c>1</c> to force the room private.
	/// </remarks>
	private async Task<bool> SetPrivate(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count == 0)
		{
			sink.Reply(string.Format(MpReplies.MatchIsPrivateNow, match.IsPrivate ? "private" : "not private"));
			return true;
		}

		if (args[0] is "0" or "1")
		{
			await _matchControl.SetPrivateAsync(match, args[0] == "1");
			sink.Reply(match.IsPrivate ? MpReplies.MatchNowPrivate : MpReplies.MatchNowPublic);
			return true;
		}

		sink.Reply(MpReplies.PrivateUsage);
		return false;
	}

	/// <summary>Implements <c>!mp invite &lt;name&gt;</c>, inviting an online userSession to the room.</summary>
	private bool Invite(UserSession sender, MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.InviteUsage);
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = ParseGameSession(targetName);
		if (target is null)
		{
			sink.Reply(MpReplies.InviteRequiresClient);
			return false;
		}

		var result = MatchControlService.Invite(sender, match, target);
		switch (result)
		{
			case MatchControlService.InviteResult.TargetAlreadyInRoom:
				sink.Reply(MpReplies.UserAlreadyInRoom);
				return false;
			case MatchControlService.InviteResult.TargetIsBot:
				sink.Reply(MpReplies.CannotInviteBot);
				return false;
			default:
				sink.Reply(string.Format(MpReplies.InvitedToRoom, target.Name));
				return true;
		}
	}

	/// <summary>Implements <c>!mp addref &lt;name&gt;</c>, adding a referee to the match.</summary>
	private async Task<bool> AddRefereeAsync(UserSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.AddRefUsage);
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = ParseUserSession(targetName);
		if (target is null)
		{
			sink.Reply(MpReplies.UserNotFound);
			return false;
		}

		var result = await _matchControl.AddRefereeAsync(sender.Id, sender.Name, match, target, cancellationToken);
		if (result == MatchControlService.AddRefereeResult.TargetIsBot)
		{
			sink.Reply(MpReplies.CannotAddBotReferee);
			return false;
		}

		sink.Reply(string.Format(MpReplies.AddedReferee, target.Name));
		return true;
	}

	/// <summary>
	///     Implements <c>!mp removeref &lt;name&gt;</c>, removing a referee from the match.
	/// </summary>
	/// <remarks>
	///     At least one referee must always remain, so removing the last one is rejected instead of
	///     disbanding the room, and the match's creator can never be removed at all (see
	///     <see cref="MatchControlService.RemoveOneRefereeAsync" />). Only the creator can run this
	///     command in the first place (see <see cref="TryHandleAsync" />).
	/// </remarks>
	private async Task<bool> RemoveRefereeAsync(UserSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.RemoveRefUsage);
			return false;
		}

		var targetName = string.Join(' ', args);
		var target = ParseUserSession(targetName);
		if (target is null)
		{
			sink.Reply(MpReplies.UserNotFound);
			return false;
		}

		var result =
			await _matchControl.RemoveOneRefereeAsync(sender.Id, sender.Name, match, target, cancellationToken);
		switch (result)
		{
			case MatchControlService.RemoveRefereeResult.WouldLeaveEmpty:
				sink.Reply(string.Format(MpReplies.CannotRemoveLastReferee, target.Name));
				return false;
			case MatchControlService.RemoveRefereeResult.NotAReferee:
				sink.Reply(string.Format(MpReplies.TargetIsNotAReferee, target.Name));
				return false;
			case MatchControlService.RemoveRefereeResult.TargetIsCreator:
				sink.Reply(string.Format(MpReplies.CannotRemoveCreator, target.Name));
				return false;
			default:
				sink.Reply(string.Format(MpReplies.RemovedReferee, target.Name));
				return true;
		}
	}

	/// <summary>Implements <c>!mp listrefs</c>, listing the match's current referees.</summary>
	private bool ListReferees(MatchSession match, ICommandReplySink sink)
	{
		if (match.Referees.Count == 0)
		{
			sink.Reply(MpReplies.NoReferees);
			return true;
		}

		var referees = match.Referees
			.Select(id =>
			{
				var session = (UserSession?)gameRegistry.GetByUserId(id) ?? ircRegistry.GetByUserId(id);
				return session is null
					? $"#{id}"
					: $"#{id} {session.Name}";
			});

		sink.Reply(MpReplies.MatchReferees + "\n" + string.Join('\n', referees));
		return true;
	}

	/// <summary>Implements <c>!mp banlist</c>, listing the players banned from the match.</summary>
	private bool BanListAsync(MatchSession match, ICommandReplySink sink)
	{
		if (match.BannedIds.Count == 0)
		{
			sink.Reply(MpReplies.NoBannedPlayers);
			return true;
		}

		var players = match.BannedIds
			.Select(id =>
			{
				var session = (UserSession?)gameRegistry.GetByUserId(id) ?? ircRegistry.GetByUserId(id);
				return session is null
					? $"#{id}"
					: $"#{id} {session.Name}";
			});

		sink.Reply(MpReplies.MatchBans + "\n" + string.Join('\n', players));
		return true;
	}

	/// <summary>Implements <c>!mp team &lt;name&gt; &lt;red|blue&gt;</c>, assigning a userSession's team.</summary>
	private async Task<bool> SetTeam(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 2)
		{
			sink.Reply(MpReplies.TeamUsage);
			return false;
		}

		var teamArg = args[^1].ToLowerInvariant();
		if (teamArg is not ("red" or "blue"))
		{
			sink.Reply(MpReplies.TeamUsageName);
			return false;
		}

		var targetName = string.Join(' ', args.Take(args.Count - 1));
		var target = ParseUserSession(targetName);
		if (target is null)
		{
			sink.Reply(MpReplies.UserNotInMatchOrUnregistered);
			return false;
		}

		var team = teamArg == "red" ? MatchTeam.Red : MatchTeam.Blue;
		var result = await _matchControl.SetTeamAsync(match, target, team);
		if (result == MatchControlService.TeamResult.TargetNotInMatch)
		{
			sink.Reply(string.Format(MpReplies.NotInThisMatch, targetName));
			return false;
		}

		var teamDisplay = char.ToUpperInvariant(teamArg[0]) + teamArg[1..];
		sink.Reply(string.Format(MpReplies.MovedToTeam, target.Name, teamDisplay));
		return true;
	}

	/// <summary>
	///     Implements <c>!mp set &lt;teammode 0-3&gt; [scoremode 0-3] [size 1-16]</c>, setting team
	///     type, win condition, and size in one call.
	/// </summary>
	private async Task<bool> Set(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		const string usage = MpReplies.SetUsage;

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
		sink.Reply(string.Format(MpReplies.ChangedMatchSettings, match.TeamType, match.WinCondition,
			size is { } sz ? $", {sz} slots." : "."));
		return true;
	}

	/// <summary>Parses a team-mode argument in the 0-3 range.</summary>
	/// <param name="arg">The argument text to parse.</param>
	/// <param name="teamType">The parsed team type when parsing succeeds.</param>
	/// <returns>
	///     <see langword="true" /> when the argument is an integer in range; otherwise,
	///     <see langword="false" />.
	/// </returns>
	private static bool TryParseTeamType(string arg, out MatchTeamType teamType)
	{
		teamType = default;
		if (!int.TryParse(arg, out var value) || value is < 0 or > 3) return false;

		teamType = (MatchTeamType)value;
		return true;
	}

	/// <summary>Parses a win-condition argument in the 0-3 range.</summary>
	/// <param name="arg">The argument text to parse.</param>
	/// <param name="winCondition">The parsed win condition when parsing succeeds.</param>
	/// <returns>
	///     <see langword="true" /> when the argument is an integer in range; otherwise,
	///     <see langword="false" />.
	/// </returns>
	private static bool TryParseWinCondition(string arg, out MatchWinCondition winCondition)
	{
		winCondition = default;
		if (!int.TryParse(arg, out var value) || value is < 0 or > 3) return false;

		winCondition = (MatchWinCondition)value;
		return true;
	}

	/// <summary>Implements <c>!mp map &lt;beatmap id&gt;</c>, changing the match's selected map.</summary>
	private async Task<bool> SetMapAsync(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var beatmapId))
		{
			sink.Reply(MpReplies.MapUsage);
			return false;
		}

		var (result, beatmap) = await _matchControl.SetMapAsync(match, beatmapId, cancellationToken);
		if (result == MatchControlService.SetMapResult.BeatmapNotFound || beatmap is null)
		{
			sink.Reply(string.Format(MpReplies.NoBeatmapWithId, beatmapId));
			return false;
		}

		sink.Reply(string.Format(MpReplies.ChangedBeatmap, beatmap.Beatmapset.Artist, beatmap.Beatmapset.Title,
			beatmap.Version));
		return true;
	}

	/// <summary>
	///     Implements <c>!mp mods &lt;mods&gt;|Freemod|None</c>, setting the match's mods.
	/// </summary>
	/// <remarks>
	///     <c>!mp mods</c> is the only mod-setting command, matching the official server: freemod is
	///     just one of the values it accepts (<c>!mp mods Freemod</c>), not a separate
	///     <c>!mp freemods</c> toggle. <c>None</c> clears the mods and also disables freemod when it
	///     is on.
	/// </remarks>
	private async Task<bool> SetMods(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.ModsUsage);
			return false;
		}

		var before = match.Mods;
		var wasFreemod = match.Freemods;

		var mods = Mods.NoMod;
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

		await _matchControl.SetModsAsync(match, mods, freemod);
		sink.Reply(DescribeModChange(before, mods, wasFreemod, match.Freemods));

		return true;
	}

	/// <summary>
	///     Builds the reply text describing a mod change relative to the match's previous mods.
	/// </summary>
	/// <remarks>
	///     Reports which mods were enabled and disabled and whether freemod was turned off. The
	///     combined case of mixing a mod list with the <c>Freemod</c> keyword in one call is not
	///     reproduced here: the <c>Freemod</c> branch above reports only "Enabled FreeMod".
	/// </remarks>
	/// <param name="before">The match's mods before the change.</param>
	/// <param name="after">The match's mods after the change.</param>
	/// <param name="wasFreemod">A value that indicates whether freemod was on before the change.</param>
	/// <param name="isFreemod">A value that indicates whether freemod is on after the change.</param>
	/// <returns>The enabled/disabled mod summary, or "No mod changes" when nothing changed.</returns>
	private static string DescribeModChange(Mods before, Mods after, bool wasFreemod, bool isFreemod)
	{
		var enabled = after & ~before;
		var disabled = before & ~after;

		var parts = new List<string>();

		if (enabled != Mods.NoMod)
			parts.Add(string.Format(MpReplies.EnabledMods, enabled));

		if (disabled != Mods.NoMod)
			parts.Add(string.Format(MpReplies.DisabledMods, disabled));

		switch (wasFreemod)
		{
			case true when !isFreemod:
				parts.Add(MpReplies.DisabledFreemod);
				break;
			case false when isFreemod:
				parts.Add(MpReplies.EnabledFreemod);
				break;
		}

		return parts.Count > 0
			? string.Join(", ", parts)
			: MpReplies.NoModChanges;
	}

	/// <summary>
	///     Implements <c>!mp start [seconds]</c>, starting the match now or queueing a countdown.
	/// </summary>
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
				sink.Reply(MpReplies.MatchAlreadyInProgress);
				return false;
			case MatchControlService.StartResult.CountdownQueued:
				sink.Reply(string.Format(MpReplies.MatchStartsInSeconds, countdownSeconds));
				return true;
			case MatchControlService.StartResult.Started:
				sink.Reply(MpReplies.MatchStarted);
				return true;
			case MatchControlService.StartResult.BeatmapMissing:
			default:
				// StartResult.BeatmapMissing — MatchMembershipService.StartAsync already announced this
				// into the match channel itself (the single choke point all 3 start paths share); no
				// second reply here, or the room sees the same message twice.
				return false;
		}
	}

	/// <summary>Implements <c>!mp timer [seconds]</c>, starting a countdown without auto-starting.</summary>
	private bool Timer(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink)
	{
		var seconds = 30;
		if (args.Count > 0 && (!int.TryParse(args[0], out seconds) || seconds <= 0))
		{
			sink.Reply(MpReplies.TimerUsage);
			return false;
		}

		_matchControl.Timer(match, seconds);
		sink.Reply(string.Format(MpReplies.CountdownStarted, seconds));
		return true;
	}

	/// <summary>Implements <c>!mp aborttimer</c>, cancelling a running countdown.</summary>
	private bool AbortTimer(MatchSession match, ICommandReplySink sink)
	{
		var result = _matchControl.AbortTimer(match);
		if (result == MatchControlService.AbortTimerResult.NoTimerRunning)
		{
			sink.Reply(MpReplies.NoCountdownRunning);
			return false;
		}

		sink.Reply(MpReplies.CountdownAborted);
		return true;
	}

	/// <summary>Implements <c>!mp abort</c>, aborting a match in progress.</summary>
	private async Task<bool> AbortAsync(MatchSession match, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		var result = await _matchControl.AbortAsync(match, cancellationToken);
		if (result == MatchControlService.AbortResult.NotInProgress)
		{
			sink.Reply(MpReplies.MatchNotInProgress);
			return false;
		}

		sink.Reply(MpReplies.AbortedMatch);
		return true;
	}

	/// <summary>Implements <c>!mp kick &lt;name&gt;</c>, removing a userSession from the room.</summary>
	private async Task<bool> KickAsync(UserSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.KickUsage);
			return false;
		}

		var targetName = string.Join(' ', args);
		var targetUser = await ParseUser(targetName);
		if (targetUser is null)
		{
			sink.Reply(MpReplies.UserNotInMatchOrUnregistered);
			return false;
		}

		var result = await _matchControl.KickAsync(sender.Id, sender.Name, match, targetUser.Id, targetUser.Name,
			cancellationToken);
		switch (result)
		{
			case MatchControlService.KickResult.TargetIsBot:
				sink.Reply(MpReplies.CannotKickBot);
				return false;
			case MatchControlService.KickResult.TargetIsReferee:
				sink.Reply(string.Format(MpReplies.CannotKickReferee, targetUser.Name));
				return false;
			case MatchControlService.KickResult.TargetNotInMatch:
				sink.Reply(MpReplies.UserNotInMatchOrUnregistered);
				return false;
			default:
				sink.Reply(string.Format(MpReplies.KickedFromMatch, targetUser.Name));
				return true;
		}
	}

	/// <summary>Implements <c>!mp ban &lt;name&gt;</c>, kicking a userSession and blocking them from rejoining.</summary>
	private async Task<bool> BanAsync(UserSession sender, MatchSession match, IReadOnlyList<string> args,
		ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.BanUsage);
			return false;
		}

		var targetName = string.Join(' ', args);
		var targetUser = await ParseUser(targetName);
		if (targetUser is null)
		{
			sink.Reply(MpReplies.UserNotRegistered);
			return false;
		}

		var result = await _matchControl.BanAsync(sender.Id, sender.Name, match, targetUser.Id, targetUser.Name,
			cancellationToken);
		switch (result)
		{
			case MatchControlService.BanResult.TargetIsBot:
				sink.Reply(MpReplies.CannotBanBot);
				return false;
			case MatchControlService.BanResult.TargetIsReferee:
				sink.Reply(string.Format(MpReplies.CannotBanReferee, targetUser.Name));
				return false;
			default:
				sink.Reply(string.Format(MpReplies.BannedPlayerFromMatch, targetUser.Name));
				return true;
		}
	}

	/// <summary>Implements <c>!mp unban &lt;name&gt;</c>, allowing a banned userSession to rejoin.</summary>
	private async Task<bool> UnbanAsync(MatchSession match, IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply(MpReplies.UnbanUsage);
			return false;
		}

		var targetName = string.Join(' ', args);
		var targetUser = await ParseUser(targetName);
		if (targetUser is null)
		{
			sink.Reply(MpReplies.UserNotRegistered);
			return false;
		}

		var result = await _matchControl.UnbanAsync(match, targetUser.Id, cancellationToken);
		if (result == MatchControlService.UnbanResult.NotBanned)
		{
			sink.Reply(string.Format(MpReplies.NotBannedFromMatch, targetUser.Name));
			return false;
		}

		sink.Reply(string.Format(MpReplies.UnbannedFromMatch, targetUser.Name));
		return true;
	}

	/// <summary>Implements <c>!mp close</c>, closing the match immediately.</summary>
	private async Task<bool> CloseAsync(UserSession sender, MatchSession match, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		await _matchControl.CloseAsync(sender.Id, sender.Name, match, cancellationToken);
		sink.Reply(MpReplies.ClosedMatch);
		return true;
	}

	private UserSession? ParseUserSession(string target)
	{
		if (int.TryParse(target, out var playerId))
			return (UserSession?)gameRegistry.GetByUserId(playerId) ??
			       ircRegistry.GetByUserId(playerId) ?? ParseByName(target);
		return ParseByName(target);

		UserSession? ParseByName(string name)
		{
			return (UserSession?)gameRegistry.GetByName(name) ?? ircRegistry.GetByName(name);
		}
	}

	private GameSession? ParseGameSession(string target)
	{
		if (int.TryParse(target, out var playerId))
			return gameRegistry.GetByUserId(playerId) ?? gameRegistry.GetByName(target);
		return gameRegistry.GetByName(target);
	}

	private async Task<User?> ParseUser(string target)
	{
		if (int.TryParse(target, out var playerId))
			return await userRepository.FetchByIdAsync(playerId) ?? await userRepository.FetchByNameAsync(target);
		return await userRepository.FetchByNameAsync(target);
	}

	/// <summary>
	///     Computes the announcement checkpoints for a countdown, forwarding to
	///     <see cref="MatchControlService.ComputeAnnounceCheckpoints" />.
	/// </summary>
	/// <remarks>
	///     Also kept here so existing tests can reference it as
	///     <c>MpCommandService.ComputeAnnounceCheckpoints</c>.
	/// </remarks>
	/// <param name="totalSeconds">The total countdown length in seconds.</param>
	/// <param name="autoStart">
	///     When <see langword="true" />, the final checkpoint triggers the match start.
	/// </param>
	/// <returns>The countdown checkpoints in seconds.</returns>
	public static IReadOnlyList<int> ComputeAnnounceCheckpoints(int totalSeconds, bool autoStart = true)
	{
		return MatchControlService.ComputeAnnounceCheckpoints(totalSeconds, autoStart);
	}

	/// <summary>
	///     A single entry in the auto-generated <c>!mp help</c> listing.
	/// </summary>
	/// <remarks>
	///     Combines a usage string with a one-line description.
	/// </remarks>
	private readonly record struct CommandInfo(string Usage, string Description);
}