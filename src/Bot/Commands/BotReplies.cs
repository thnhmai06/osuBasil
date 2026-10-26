using Basil.Bot.Replies;

namespace Basil.Bot.Commands;

/// <summary>
///     The user-visible reply text sent by BasilBot's own chat commands (<c>!where</c>,
///     <c>!faq</c>, <c>!roll</c>, <c>!help</c>), the single source of truth for that surface.
/// </summary>
internal static class BotReplies
{
	// ── !help ────────────────────────────────────────────────────────────────────────────────
	/// <summary>The help text listing available commands.</summary>
	public static readonly Reply HelpText = new("bot.helpText", """
	                                                            !roll [max] - roll a random number from 0 to max (default 100)
	                                                            !where <username> - show a user's country
	                                                            !faq <entry>|list - print a FAQ entry, or list every entry
	                                                            !mp help - list multiplayer subcommands
	                                                            """);

	// ── !where ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!where</c>.</summary>
	public static readonly Reply WhereUsage = new("bot.whereUsage", "Usage: !where <username>");

	/// <summary>Reply when the named user is not registered; <c>{name}</c> is the name.</summary>
	public static readonly Reply NotRegistered = new("bot.notRegistered", "{name} is not registered.", "name");

	/// <summary>Reply reporting a user's country; <c>{name}</c> is the name, <c>{country}</c> the country.</summary>
	public static readonly Reply WhereIsIn = new("bot.whereIsIn", "{name} is in {country}", "name", "country");

	// ── !faq ─────────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!faq</c>.</summary>
	public static readonly Reply FaqUsage = new("bot.faqUsage", "Usage: !faq <entry>|list");

	/// <summary>Reply when no FAQ entry matches; <c>{entry}</c> is the requested entry.</summary>
	public static readonly Reply NoFaqEntryFound = new("bot.noFaqEntryFound",
		"No FAQ entry found for ''{entry}''.", "entry");

	/// <summary>Reply when the FAQ has no entries.</summary>
	public static readonly Reply NoFaqEntriesAvailable = new("bot.noFaqEntriesAvailable", "No FAQ entries available.");

	/// <summary>Reply listing the available FAQ entries; <c>{entries}</c> is the comma-joined list.</summary>
	public static readonly Reply AvailableFaqEntries = new("bot.availableFaqEntries",
		"Available FAQ entries: {entries}", "entries");

	// ── !roll ────────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply to <c>!roll</c>; <c>{user}</c> is the sender's name, <c>{points}</c> the roll result.</summary>
	public static readonly Reply RollResult = new("bot.rollResult", "{user} rolls {points} point(s)", "user", "points");

	// ── Chain errors ─────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a chain contains non-<c>!mp</c> commands.</summary>
	public static readonly Reply ChainOnlyMpAllowed =
		new("bot.chainOnlyMpAllowed", "Chains are only allowed for !mp commands.");

	/// <summary>Reply when a chain is used in the #lobby channel.</summary>
	public static readonly Reply ChainNotInLobby = new("bot.chainNotInLobby", "Chains are not allowed in #lobby.");
}