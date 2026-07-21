using System.Globalization;
using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Login;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Bot;

/// <inheritdoc cref="ICommandDispatcher" />
public sealed class CommandDispatcher(
    IOptions<BotOptions> botOptions,
    MpCommandService mpCommands,
    IUserRepository userRepository,
    IOptions<StorageOptions> storageOptions,
    IMatchRegistry matchRegistry)
    : ICommandDispatcher
{
    private const int RollMaxCap = int.MaxValue; // highest value int.TryParse can produce

    /// <summary>
    ///     Single source of truth for `!help`'s output — add a chat command here, and it shows up with
    ///     no separate help string to keep in sync. `!mp` subcommands live in their own list, see
    ///     <see cref="MpCommandService.HelpText" />.
    /// </summary>
    private static readonly CommandInfo[] ChatCommands =
    [
        new("!roll [max]", "roll a random number from 0 to max (default 100)"),
        new("!where <username>", "show a player's country"),
        new("!faq <entry>|list", "print a FAQ entry, or list every entry"),
        new("!mp make <name>", "create a tournament room from anywhere, scoping you to it"),
        new("!mp join <id> [password]", "join a match by id (private rooms need an invite from the host/a referee)"),
        new("!mp in [match_id]", "target/show a match you're not physically in (needs referee permission there)"),
        new("!mp help", "list multiplayer subcommands (usable while scoped to a match)")
    ];

    private static readonly string HelpText = BuildHelpText(ChatCommands);

    private static readonly HashSet<string> NonChainableMpSubcommands =
        new(StringComparer.OrdinalIgnoreCase) { "", "help", "make", "in", "join" };

    public async Task<string?> DispatchAsync(PlayerSession sender, string rawMessage, MatchSession? matchScope,
        bool prefixOptional = false, CancellationToken cancellationToken = default)
    {
        var prefix = botOptions.Value.CommandPrefix;
        if (string.IsNullOrEmpty(prefix)) return null;

        string message;
        if (rawMessage.StartsWith(prefix, StringComparison.Ordinal)) message = rawMessage;
        else if (prefixOptional) message = prefix + rawMessage;
        else return null;

        // Always run the message through the quote/escape-aware splitter, even when it's a single
        // command with no `;`/`&&` at all — that's what lets `!mp name "a; b"` keep its literal
        // semicolon without the message needing to look like a chain. A lone segment (the common case)
        // just falls through to the same single-command dispatch as before; 2+ segments is a real chain
        // and gets the stricter local-!mp-subcommand-only validation in DispatchChainAsync.
        var segments = ChatCommandChain.Split(message);
        return segments.Count == 1
            ? await DispatchSingleAsync(sender, segments[0].Text, prefix, matchScope, cancellationToken)
            : await DispatchChainAsync(sender, segments, matchScope, prefix, cancellationToken);
    }

    private static string BuildHelpText(IReadOnlyList<CommandInfo> commands)
    {
        return string.Join('\n', commands.Select(c => $"{c.Usage} - {c.Description}"));
    }

    private async Task<string?> DispatchSingleAsync(PlayerSession sender, string rawMessage, string prefix,
        MatchSession? matchScope, CancellationToken cancellationToken)
    {
        var parts = rawMessage[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var trigger = parts[0].ToLowerInvariant();
        var args = parts[1..];

        switch (trigger)
        {
            case "mp":
            {
                var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "";

                switch (subcommand)
                {
                    // `make` creates a match, `join` targets any match by wire id, and `in` targets one
                    // the sender may not be in at all — all three run with no channel-derived match scope
                    // (reachable via PM to the bot), unlike every other !mp subcommand — see
                    // MpCommandService.MakeAsync/JoinAsync/SetScopeAsync.
                    case "make":
                        return await mpCommands.MakeAsync(sender, args[1..], cancellationToken);
                    case "join":
                        return await mpCommands.JoinAsync(sender, args[1..], cancellationToken);
                    case "in":
                        return mpCommands.SetScopeAsync(sender, args[1..]);
                }

                var scope = ResolveScope(sender, matchScope);
                if (scope is null) return null;
                return await mpCommands.HandleAsync(sender, scope, subcommand, args[1..], cancellationToken);
            }
            case "where":
                return await Where(args, cancellationToken);
            case "faq":
                return await Faq(args, cancellationToken);
            case "help":
                return HelpText;
            case "roll":
                return Roll(sender, args);
            default:
                return null;
        }
    }

    /// <summary>
    ///     Prefers the out-of-room scope set by `!mp in` over the sender's literal chat channel — a
    ///     referee juggling several matches from one place should keep targeting the match they picked,
    ///     even if they happen to be sitting in a different match's own channel. Falls back to the
    ///     channel-derived scope (or null) once the scoped match no longer exists.
    /// </summary>
    private MatchSession? ResolveScope(PlayerSession sender, MatchSession? channelScope)
    {
        if (sender.MpScopeMatchId is { } dbId)
        {
            var scoped = matchRegistry.GetByDbId(dbId);
            if (scoped is not null) return scoped;

            sender.MpScopeMatchId = null;
        }

        return channelScope;
    }

    /// <summary>
    ///     Runs a `;`/`&amp;&amp;`-chained line of `!mp` subcommands sequentially against the resolved
    ///     scope. Only chainable when the sender is currently a referee of that scope, and only for
    ///     `!mp` subcommands that operate on it — `make`/`join`/`in`/`help` don't (they either
    ///     create/match/change scope elsewhere), and a bare `!roll`/`!where`/`!faq` segment isn't a
    ///     `!mp` command at all, so any of those inside a chain reject the whole line rather than run
    ///     part of it silently.
    /// </summary>
    private async Task<string?> DispatchChainAsync(PlayerSession sender,
        IReadOnlyList<ChatCommandChain.Segment> segments,
        MatchSession? matchScope, string prefix, CancellationToken cancellationToken)
    {
        var scope = ResolveScope(sender, matchScope);
        if (scope is null || !scope.IsReferee(sender.Id)) return null;

        var parsed = new List<(string Subcommand, string[] Args, ChatCommandChain.ChainOperator Operator)>();
        foreach (var segment in segments)
        {
            if (!segment.Text.StartsWith(prefix, StringComparison.Ordinal))
                return $"Chained commands must all be `{prefix}mp <subcommand>` — rejected at: '{segment.Text}'.";

            var segParts = segment.Text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (segParts.Length == 0 || !segParts[0].Equals("mp", StringComparison.OrdinalIgnoreCase))
                return $"Chained commands must all be `{prefix}mp <subcommand>` — rejected at: '{segment.Text}'.";

            var subcommand = segParts.Length > 1 ? segParts[1].ToLowerInvariant() : "";
            if (NonChainableMpSubcommands.Contains(subcommand))
                return $"`{prefix}mp {subcommand}` can't be chained — rejected at: '{segment.Text}'.";

            parsed.Add((subcommand, segParts[2..], segment.Operator));
        }

        var replies = new List<string>();
        var previousSucceeded = true;
        foreach (var (subcommand, args, op) in parsed)
        {
            if (op == ChatCommandChain.ChainOperator.And && !previousSucceeded)
            {
                previousSucceeded = false;
                continue;
            }

            var (success, reply) = await mpCommands.TryHandleAsync(sender, scope, subcommand, args, cancellationToken);
            previousSucceeded = success;
            if (reply is not null) replies.Add(reply);
        }

        return replies.Count == 0 ? null : string.Join('\n', replies);
    }

    private static string Roll(PlayerSession sender, IReadOnlyList<string> args)
    {
        var max = 100;
        if (args.Count > 0 && int.TryParse(args[0], out var parsed) && parsed > 0) max = Math.Min(parsed, RollMaxCap);

        var roll = (int)Random.Shared.NextInt64(0, (long)max + 1);
        return $"{sender.Name} rolls {roll} point(s)";
    }

    private async Task<string?> Where(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count < 1) return "Usage: !where <username>";

        var name = string.Join(' ', args);
        var user = await userRepository.FetchByNameAsync(name, cancellationToken);
        return user is null ? $"{name} is not registered." : $"{user.Name} is in {DescribeCountry(user.Country.ToAcronym())}";
    }

    private static string DescribeCountry(string code)
    {
        try
        {
            return new RegionInfo(code.ToUpperInvariant()).EnglishName;
        }
        catch (ArgumentException)
        {
            // Some pseudocode (oc, eu, xx, a2, o1, ...) isn't real ISO regions and throws here —
            // fall back to the bare code rather than maintaining a hand-rolled name table.
            return code.ToUpperInvariant();
        }
    }

    private async Task<string?> Faq(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        switch (args.Count)
        {
            case < 1:
                return "Usage: !faq <entry>|list";
            case 1 when args[0].Equals("list", StringComparison.OrdinalIgnoreCase):
                return ListFaqEntries();
        }

        var requested = string.Join(' ', args);
        var entry = Path.GetFileName(requested);
        if (!IsSafeFaqEntry(entry)) return $"No FAQ entry found for '{requested}'.";

        var path = Path.Combine(storageOptions.Value.FaqsPath, $"{entry}.txt");
        if (!File.Exists(path)) return $"No FAQ entry found for '{entry}'.";

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return string.Join('\n', lines);
    }

    /// <summary>
    ///     Entry names behave like normal filenames — spaces and most punctuation are fine. `Path.GetFileName`
    ///     already strips `/`-based traversal on every OS; `\` is explicitly rejected too since .NET only
    ///     treats it as a separator on Windows (a Linux deployment would otherwise let `..\..\secret`
    ///     through untouched), and a literal `..` is rejected outright as defense in depth.
    /// </summary>
    private static bool IsSafeFaqEntry(string entry)
    {
        return entry.Length > 0 && !entry.Contains('\\') && !entry.Contains("..");
    }

    private string ListFaqEntries()
    {
        if (!Directory.Exists(storageOptions.Value.FaqsPath)) return "No FAQ entries available.";

        // "list" is the subcommand keyword itself — a stray list.txt in the folder isn't a real entry.
        var entries = Directory.EnumerateFiles(storageOptions.Value.FaqsPath, "*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.Equals(name, "list", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return entries.Count == 0
            ? "No FAQ entries available."
            : $"Available FAQ entries: {string.Join(", ", entries)}";
    }

    /// <summary>One entry in the auto-generated `!help` listing — usage plus a one-line description.</summary>
    private readonly record struct CommandInfo(string Usage, string Description);
}

/// <summary>
///     Splits a raw chat line into `;`/`&amp;&amp;`-delimited segments for <see cref="CommandDispatcher" />'s
///     command-chaining feature. `"..."` protects a delimiter from splitting (the quotes themselves are
///     stripped from the segment text — they only matter here, not for the space-based arg tokenizer each
///     segment goes through afterward). `\"` and `\\` are the only recognised escapes, resolved everywhere
///     (not just inside quotes).
/// </summary>
internal static class ChatCommandChain
{
    public enum ChainOperator
    {
        /// <summary>The first segment on the line — nothing precedes it.</summary>
        None,

        /// <summary>Preceded by `;` — always runs regardless of the previous segment's outcome.</summary>
        Then,

        /// <summary>Preceded by `&amp;&amp;` — only runs if the previous segment succeeded.</summary>
        And
    }

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

    public readonly record struct Segment(string Text, ChainOperator Operator);
}