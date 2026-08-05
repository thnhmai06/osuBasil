using System.Text;
using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Login;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Bot;

/// <inheritdoc cref="ICommandDispatcher" />
public sealed class CommandDispatcher(
	IOptions<BotOptions> botOptions,
	MpCommandService mpCommands,
	IUserRepository userRepository,
	IOptions<StorageOptions> storageOptions,
	IMatchRegistry matchRegistry,
	ILogger<CommandDispatcher> logger)
	: ICommandDispatcher
{
	private const int RollMaxCap = int.MaxValue; // highest value int.TryParse can produce

	/// <summary>
	///     The chat commands listed by <c>!help</c>, the single source of truth for that output.
	/// </summary>
	/// <remarks>
	///     Add a command here, and it appears in <c>!help</c> with no separate help string to keep in
	///     sync. The <c>!mp</c> subcommands live in their own list, <see cref="MpCommandService.HelpText" />.
	/// </remarks>
	private static readonly CommandInfo[] ChatCommands =
	[
		new("!roll [max]", "roll a random number from 0 to max (default 100)"),
		new("!where <username>", "show a userSession's country"),
		new("!faq <entry>|list", "print a FAQ entry, or list every entry"),
		new("!mp make <name>", "create a tournament room from anywhere, scoping you to it"),
		new("!mp makeprivate <name>",
			"create a private tournament room from anywhere, scoping you to it (hidden from lobby, invite-only)"),
		new("!mp join <id> [password]", "join a match by id (private rooms need an invite from the host/a referee)"),
		new("!mp in [match_id]", "scope to a live match (DM only - needs referee permission there)"),
		new("!mp help", "list multiplayer subcommands (only usable while scoped to a match)")
	];

	private static readonly string HelpText = BuildHelpText(ChatCommands);

	/// <summary>
	///     The <c>!mp</c> subcommands that reject being run as part of a <c>;</c>/<c>&amp;&amp;</c>
	///     chain.
	/// </summary>
	private static readonly HashSet<string> NonChainableMpSubcommands =
		new(StringComparer.OrdinalIgnoreCase) { "", "help", "make", "makeprivate", "in", "join" };

	/// <summary>
	///     The <c>!mp</c> subcommands reachable from <c>#lobby</c>.
	/// </summary>
	/// <remarks>
	///     Every other subcommand is a silent no-op when issued from <c>#lobby</c>. <c>in</c> is
	///     deliberately excluded: combined with the separate rule that rejects it from the sender's
	///     own match channel, <c>!mp in</c> ends up reachable only via DM to the bot.
	/// </remarks>
	private static readonly HashSet<string> LobbyAllowedMpSubcommands =
		new(StringComparer.OrdinalIgnoreCase) { "make", "makeprivate" };

	private readonly FaqService _faq = new(storageOptions);

	/// <inheritdoc />
	public async Task<bool> DispatchAsync(UserSession sender, string rawMessage, MatchSession? matchScope,
		string? channelName, ICommandReplySink sink, bool prefixOptional = false,
		CancellationToken cancellationToken = default)
	{
		var prefix = botOptions.Value.CommandPrefix;
		if (string.IsNullOrEmpty(prefix)) return false;

		string message;
		if (rawMessage.StartsWith(prefix, StringComparison.Ordinal)) message = rawMessage;
		else if (prefixOptional) message = prefix + rawMessage;
		else return false;

		// Always run the message through the quote/escape-aware splitter, even when it's a single
		// command with no `;`/`&&` at all — that's what lets `!mp name "a ; b"` keep its literal
		// semicolon without the message needing to look like a chain. A lone segment (the common case)
		// just falls through to the same single-command dispatch as before; 2+ segments is a real chain
		// and gets the stricter local-!mp-subcommand-only validation in DispatchChainAsync.
		var segments = ChatCommandChain.Split(message);
		return segments.Count == 1
			? await DispatchSingleAsync(sender, segments[0].Text, prefix, matchScope, channelName, sink,
				cancellationToken)
			: await DispatchChainAsync(sender, segments, matchScope, channelName, prefix, sink, cancellationToken);
	}

	/// <summary>
	///     Builds the <c>!help</c> text from a command listing.
	/// </summary>
	/// <param name="commands">The commands to format.</param>
	/// <returns>The usage and description lines, one command per line.</returns>
	private static string BuildHelpText(IReadOnlyList<CommandInfo> commands)
	{
		return string.Join('\n', commands.Select(c => $"{c.Usage} - {c.Description}"));
	}

	/// <summary>
	///     Dispatches a single command segment, matching its trigger against the known chat commands.
	/// </summary>
	private async Task<bool> DispatchSingleAsync(UserSession sender, string rawMessage, string prefix,
		MatchSession? matchScope, string? channelName, ICommandReplySink sink, CancellationToken cancellationToken)
	{
		var parts = rawMessage[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0) return false;

		var trigger = parts[0].ToLowerInvariant();
		var args = parts[1..];

		switch (trigger)
		{
			case "mp":
				return await DispatchMpAsync(sender, args, matchScope, channelName, sink, cancellationToken);
			case "where":
				return await Where(args, sink, cancellationToken);
			case "faq":
				return await Faq(args, sink, cancellationToken);
			case "help":
				sink.ReplyDm(HelpText);
				return true;
			case "roll":
				sink.Reply(Roll(sender, args));
				return true;
			default:
				logger.LogDebug("Command not recognized: UserId={UserId} RawMessage={RawMessage}",
					sender.Id, Truncate(rawMessage));
				return false;
		}
	}

	/// <summary>
	///     Dispatches a <c>!mp</c> command, applying the channel-eligibility rules and resolving the
	///     command's match scope.
	/// </summary>
	private async Task<bool> DispatchMpAsync(UserSession sender, string[] args, MatchSession? matchScope,
		string? channelName, ICommandReplySink sink, CancellationToken cancellationToken)
	{
		var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "";
		var subArgs = args[1..];

		// `!mp help` never needs a resolved scope — bypass ResolveScope entirely, so it always answers,
		// even for a sender with no MpScopeMatchId and no physical match (see the 1.7a doc comment on
		// ResolveScope for why that fallback exists at all).
		if (subcommand is "" or "help")
		{
			sink.ReplyDm(MpCommandService.HelpText);
			return true;
		}

		// `#lobby` only ever reaches `make`/`makeprivate` — every other subcommand (including `in`) is a
		// silent no-op there.
		if (channelName == "#lobby" && !LobbyAllowedMpSubcommands.Contains(subcommand)) return false;

		switch (subcommand)
		{
			// `make`/`makeprivate` create a match, `join` targets any match by persistent room id, and
			// `in` targets one the sender may not be in at all — all four run with no channel-derived
			// match scope (reachable via PM to the bot), unlike every other !mp subcommand — see
			// MpCommandService.MakeAsync/JoinAsync/SetScopeAsync.
			case "make":
				return await mpCommands.MakeAsync(sender, subArgs, sink, cancellationToken: cancellationToken);
			case "makeprivate":
				return await mpCommands.MakeAsync(sender, subArgs, sink, true, cancellationToken);
			case "join":
				return await mpCommands.JoinAsync(sender, subArgs, sink, cancellationToken);
			case "in":
				// Never from inside the very room's own #multiplayer channel — combined with the #lobby
				// block above, this leaves DM-to-bot as the only place `!mp in` actually runs.
				if (matchScope is not null && channelName == matchScope.ChatChannelName) return false;
				return mpCommands.SetScopeAsync(sender, subArgs, sink);
		}

		var scope = ResolveScope(sender, matchScope);
		if (scope is null)
		{
			// Distinct from the referee-gate's silent no-op (see MpCommandService.TryHandleAsync's doc
			// comment) — not being scoped to ANY match at all is a different, more basic failure than
			// being scoped but lacking permission, so it gets an explicit reply instead of dead silence.
			sink.Reply("You're not scoped to a match — use !mp make, !mp join <id>, or !mp in <id> first.");
			return false;
		}

		return await mpCommands.TryHandleAsync(sender, scope, subcommand, subArgs, sink, cancellationToken);
	}

	/// <summary>
	///     Resolves the match a <c>!mp</c> command should act on, preferring an explicit out-of-room
	///     scope.
	/// </summary>
	/// <remarks>
	///     The scope set by <c>!mp in</c> is preferred over the sender's literal chat channel, so a
	///     referee juggling several matches from one place keeps targeting the match they picked even
	///     when they are sitting in a different match's own channel. The fallbacks are the
	///     channel-derived scope, and finally the match the sender is physically sitting in, so a DM
	///     <c>!mp abort</c> or any other subcommand still resolves for a sender who never ran
	///     <c>!mp make</c> or <c>!mp in</c>. A stored scope whose match no longer exists is cleared.
	/// </remarks>
	/// <param name="sender">The userSession issuing the command.</param>
	/// <param name="channelScope">The match derived from the sender's current chat channel, if any.</param>
	/// <returns>The match the command targets, or <see langword="null" /> when none resolves.</returns>
	private MatchSession? ResolveScope(UserSession sender, MatchSession? channelScope)
	{
		if (sender.MpScopeMatchId is { } dbId)
		{
			var scoped = matchRegistry.GetByDbId(dbId);
			if (scoped is not null) return scoped;

			sender.MpScopeMatchId = null;
		}

		return channelScope ?? sender.Match;
	}

	/// <summary>
	///     Runs a <c>;</c>- or <c>&amp;&amp;</c>-chained line of <c>!mp</c> subcommands sequentially
	///     against the resolved scope.
	/// </summary>
	/// <remarks>
	///     Chaining is only allowed for a sender who is currently a referee of that scope, and only
	///     for <c>!mp</c> subcommands that operate on the existing room. <c>make</c>, <c>makeprivate</c>,
	///     <c>join</c>, <c>in</c>, and <c>help</c> are not chainable: they either create a match or
	///     change the scope elsewhere. Any other segment, such as a bare <c>!roll</c>, <c>!where</c>, or
	///     <c>!faq</c>, is not a <c>!mp</c> command at all, and its presence in a chain rejects the
	///     whole line rather than running part of it silently. <c>#lobby</c> never reaches here with
	///     anything runnable, since every chainable subcommand is already outside that channel's
	///     allowlist.
	/// </remarks>
	private async Task<bool> DispatchChainAsync(UserSession sender,
		IReadOnlyList<ChatCommandChain.Segment> segments, MatchSession? matchScope, string? channelName,
		string prefix, ICommandReplySink sink, CancellationToken cancellationToken)
	{
		if (channelName == "#lobby") return false;

		var scope = ResolveScope(sender, matchScope);
		if (scope is null || !scope.IsReferee(sender.Id)) return false;

		var parsed = new List<(string Subcommand, string[] Args, ChatCommandChain.ChainOperator Operator)>();
		foreach (var segment in segments)
		{
			if (!segment.Text.StartsWith(prefix, StringComparison.Ordinal))
			{
				logger.LogDebug("Command chain rejected: UserId={UserId} RejectedSegment={RejectedSegment}",
					sender.Id, segment.Text);
				sink.Reply($"Chained commands must all be `{prefix}mp <subcommand>` — rejected at: '{segment.Text}'.");
				return false;
			}

			var segParts = segment.Text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (segParts.Length == 0 || !segParts[0].Equals("mp", StringComparison.OrdinalIgnoreCase))
			{
				logger.LogDebug("Command chain rejected: UserId={UserId} RejectedSegment={RejectedSegment}",
					sender.Id, segment.Text);
				sink.Reply($"Chained commands must all be `{prefix}mp <subcommand>` — rejected at: '{segment.Text}'.");
				return false;
			}

			var subcommand = segParts.Length > 1 ? segParts[1].ToLowerInvariant() : "";
			if (NonChainableMpSubcommands.Contains(subcommand))
			{
				logger.LogDebug("Command chain rejected: UserId={UserId} RejectedSegment={RejectedSegment}",
					sender.Id, segment.Text);
				sink.Reply($"`{prefix}mp {subcommand}` can't be chained — rejected at: '{segment.Text}'.");
				return false;
			}

			parsed.Add((subcommand, segParts[2..], segment.Operator));
		}

		var previousSucceeded = true;
		var anySucceeded = false;
		foreach (var (subcommand, args, op) in parsed)
		{
			if (op == ChatCommandChain.ChainOperator.And && !previousSucceeded)
			{
				previousSucceeded = false;
				continue;
			}

			var success = await mpCommands.TryHandleAsync(sender, scope, subcommand, args, sink, cancellationToken);
			previousSucceeded = success;
			anySucceeded |= success;
		}

		return anySucceeded;
	}

	/// <summary>Computes the reply text for <c>!roll</c>.</summary>
	/// <param name="sender">The userSession rolling.</param>
	/// <param name="args">The command arguments, carrying the optional upper bound.</param>
	/// <returns>The formatted roll result.</returns>
	private static string Roll(UserSession sender, IReadOnlyList<string> args)
	{
		var max = 100;
		if (args.Count > 0 && int.TryParse(args[0], out var parsed) && parsed > 0) max = Math.Min(parsed, RollMaxCap);

		var roll = (int)Random.Shared.NextInt64(0, (long)max + 1);
		return $"{sender.Name} rolls {roll} point(s)";
	}

	/// <summary>Answers <c>!where</c>, reporting the registered country of the named userSession.</summary>
	private async Task<bool> Where(IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			sink.Reply("Usage: !where <username>");
			return false;
		}

		var name = string.Join(' ', args);
		var user = await userRepository.FetchByNameAsync(name, cancellationToken);
		if (user is null)
		{
			sink.Reply($"{name} is not registered.");
			return false;
		}

		sink.Reply($"{user.Name} is in {user.Country.Describe()}");
		return true;
	}

	/// <summary>
	///     Answers <c>!faq</c>, printing a stored entry or the list of available entries.
	/// </summary>
	private async Task<bool> Faq(IReadOnlyList<string> args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		switch (args.Count)
		{
			case < 1:
				sink.Reply("Usage: !faq <entry>|list");
				return false;
			case 1 when args[0].Equals("list", StringComparison.OrdinalIgnoreCase):
				sink.Reply(ListFaqEntries());
				return true;
		}

		var requested = string.Join(' ', args);
		var entry = Path.GetFileName(requested);
		var content = await _faq.ReadEntryAsync(entry, cancellationToken);
		if (content is null)
		{
			sink.Reply($"No FAQ entry found for '{entry}'.");
			return false;
		}

		sink.Reply(content);
		return true;
	}

	/// <summary>Builds the <c>!faq list</c> reply text from the stored FAQ entries.</summary>
	/// <returns>The comma-separated entry list, or a "none available" notice.</returns>
	private string ListFaqEntries()
	{
		// "list" is the subcommand keyword itself — a stray list.txt in the folder isn't a real entry.
		var entries = _faq.ListEntries()
			.Where(name => !string.Equals(name, "list", StringComparison.OrdinalIgnoreCase))
			.ToList();

		return entries.Count == 0
			? "No FAQ entries available."
			: $"Available FAQ entries: {string.Join(", ", entries)}";
	}

	/// <summary>Shortens a string for logging, appending an ellipsis when truncated.</summary>
	/// <param name="text">The text to shorten.</param>
	/// <param name="maxLength">The maximum length allowed.</param>
	/// <returns>The shortened text.</returns>
	private static string Truncate(string text, int maxLength = 100)
	{
		return text.Length <= maxLength ? text : text[..maxLength] + "…";
	}

	/// <summary>
	///     A single entry in the auto-generated <c>!help</c> listing.
	/// </summary>
	/// <remarks>
	///     Combines a usage string with a one-line description.
	/// </remarks>
	private readonly record struct CommandInfo(string Usage, string Description);
}

/// <summary>
///     Splits a raw chat line into <c>;</c>- and <c>&amp;&amp;</c>-delimited segments for
///     <see cref="CommandDispatcher" />'s command-chaining feature.
/// </summary>
/// <remarks>
///     A <c>"..."</c>-quoted section protects a delimiter from splitting. The quotes themselves are
///     stripped from the segment text and only matter here, not for the space-based argument tokenizer
///     each segment goes through afterward. <c>\"</c> and <c>\\</c> are the only recognized escapes,
///     resolved everywhere rather than only inside quotes.
/// </remarks>
internal static class ChatCommandChain
{
	public enum ChainOperator : byte
	{
		/// <summary>The first segment on the line; nothing precedes it.</summary>
		None,

		/// <summary>Preceded by <c>;</c>; always runs regardless of the previous segment's outcome.</summary>
		Then,

		/// <summary>Preceded by <c>&amp;&amp;</c>; only runs if the previous segment succeeded.</summary>
		And
	}

	/// <summary>
	///     Splits a raw message into segments and records the operator that precedes each one.
	/// </summary>
	/// <param name="message">The raw chat line to split.</param>
	/// <returns>
	///     The parsed segments, each carrying the text and the operator that precedes it.
	/// </returns>
	public static IReadOnlyList<Segment> Split(string message)
	{
		var segments = new List<Segment>();
		var current = new StringBuilder();
		var inQuotes = false;
		var pendingOp = ChainOperator.None;

		for (var i = 0; i < message.Length; i++)
		{
			var c = message[i];

			switch (c)
			{
				case '\\' when i + 1 < message.Length && (message[i + 1] == '"' || message[i + 1] == '\\'):
					current.Append(message[i + 1]);
					i++;
					continue;
				case '"':
					inQuotes = !inQuotes;
					continue;
			}

			if (!inQuotes)
				switch (c)
				{
					case ';':
						segments.Add(new Segment(current.ToString().Trim(), pendingOp));
						current.Clear();
						pendingOp = ChainOperator.Then;
						continue;
					case '&' when i + 1 < message.Length && message[i + 1] == '&':
						segments.Add(new Segment(current.ToString().Trim(), pendingOp));
						current.Clear();
						pendingOp = ChainOperator.And;
						i++;
						continue;
				}

			current.Append(c);
		}

		segments.Add(new Segment(current.ToString().Trim(), pendingOp));
		return segments;
	}

	/// <summary>
	///     A single segment of a command chain: its text and the operator that preceded it.
	/// </summary>
	public readonly record struct Segment(string Text, ChainOperator Operator);
}