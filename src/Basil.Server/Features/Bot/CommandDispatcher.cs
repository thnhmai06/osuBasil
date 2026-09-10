using System.Text;
using Basil.Server.Features.Bot;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Configuration;
using Basil.Server.Features.Content;
using Basil.Server.Shared.Sessions;
using Basil.Server.Features.Multiplayer;
using Basil.Domain.Login;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Bot;

/// <inheritdoc cref="ICommandDispatcher" />
public sealed class CommandDispatcher(
	IOptions<BotOptions> botOptions,
	IMpCommandService mpCommands,
	IUserRepository userRepository,
	IOptions<StorageOptions> storageOptions,
	ILogger<CommandDispatcher> logger)
	: ICommandDispatcher
{
	private const int RollMaxCap = int.MaxValue; // highest value int.TryParse can produce

	/// <summary>
	///     The chat commands listed by <c>!help</c>, the single source of truth for that output.
	/// </summary>
	/// <remarks>
	///     Add a command here, and it appears in <c>!help</c> with no separate help string to keep in
	///     sync. The <c>!mp</c> subcommands live in their own list, <c>MpCommandService.HelpText</c>.
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

	private readonly FaqService _faq = new(storageOptions);

	/// <inheritdoc />
	public async Task<bool> DispatchAsync(UserSession sender, string rawMessage, int? matchScopeDbId,
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
		// and gets Multiplayer.MpCommandService.DispatchChainAsync's stricter local-!mp-subcommand-only
		// validation.
		var segments = ChatCommandChain.Split(message);
		if (segments.Count == 1)
			return await DispatchSingleAsync(sender, segments[0].Text, prefix, matchScopeDbId, channelName, sink,
				cancellationToken);

		var chainSegments = segments
			.Select(s => (s.Text, RequiresPreviousSuccess: s.Operator == ChatCommandChain.ChainOperator.And))
			.ToArray();
		return await mpCommands.DispatchChainAsync(sender, chainSegments, matchScopeDbId, channelName, prefix, sink,
			cancellationToken);
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
		int? matchScopeDbId, string? channelName, ICommandReplySink sink, CancellationToken cancellationToken)
	{
		var parts = rawMessage[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0) return false;

		var trigger = parts[0].ToLowerInvariant();
		var args = parts[1..];

		switch (trigger)
		{
			case "mp":
				return await mpCommands.DispatchAsync(sender, args, matchScopeDbId, channelName, sink,
					cancellationToken);
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

	/// <summary>Computes the reply text for <c>!roll</c>.</summary>
	/// <param name="sender">The userSession rolling.</param>
	/// <param name="args">The command arguments, carrying the optional upper bound.</param>
	/// <returns>The formatted roll result.</returns>
	private static string Roll(UserSession sender, string[] args)
	{
		var max = 100;
		if (args.Length > 0 && int.TryParse(args[0], out var parsed) && parsed > 0) max = Math.Min(parsed, RollMaxCap);

		var roll = (int)Random.Shared.NextInt64(0, (long)max + 1);
		return string.Format(BotReplies.RollResult, sender.Name, roll);
	}

	/// <summary>Answers <c>!where</c>, reporting the registered country of the named userSession.</summary>
	private async Task<bool> Where(string[] args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		if (args.Length < 1)
		{
			sink.Reply(BotReplies.WhereUsage);
			return false;
		}

		var name = string.Join(' ', args);
		var user = await userRepository.FetchByNameAsync(name, cancellationToken);
		if (user is null)
		{
			sink.Reply(string.Format(BotReplies.NotRegistered, name));
			return false;
		}

		sink.Reply(string.Format(BotReplies.WhereIsIn, user.Name, user.Country.Describe()));
		return true;
	}

	/// <summary>
	///     Answers <c>!faq</c>, printing a stored entry or the list of available entries.
	/// </summary>
	private async Task<bool> Faq(string[] args, ICommandReplySink sink,
		CancellationToken cancellationToken)
	{
		switch (args.Length)
		{
			case < 1:
				sink.Reply(BotReplies.FaqUsage);
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
			sink.Reply(string.Format(BotReplies.NoFaqEntryFound, entry));
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
			? BotReplies.NoFaqEntriesAvailable
			: string.Format(BotReplies.AvailableFaqEntries, string.Join(", ", entries));
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