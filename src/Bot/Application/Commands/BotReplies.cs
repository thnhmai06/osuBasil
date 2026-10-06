namespace Basil.Bot.Application.Commands;

/// <summary>
///     The user-visible reply text sent by BasilBot's own chat commands (<c>!where</c>,
///     <c>!faq</c>, <c>!roll</c>, <c>!help</c>), the single source of truth for that surface.
/// </summary>
internal static class BotReplies
{
	// ── !help ────────────────────────────────────────────────────────────────────────────────
	/// <summary>The help text listing available commands.</summary>
	public const string HelpText =
		"!roll [max] - roll a random number from 0 to max (default 100)\n" +
		"!where <username> - show a userSession's country\n" +
		"!faq <entry>|list - print a FAQ entry, or list every entry\n" +
		"!mp help - list multiplayer subcommands";

	// ── !where ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!where</c>.</summary>
	public const string WhereUsage = "Usage: !where <username>";

	/// <summary>Reply when the named user is not registered; <c>{0}</c> is the name.</summary>
	public const string NotRegistered = "{0} is not registered.";

	/// <summary>Reply reporting a user's country; <c>{0}</c> is the name, <c>{1}</c> the country.</summary>
	public const string WhereIsIn = "{0} is in {1}";

	// ── !faq ─────────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!faq</c>.</summary>
	public const string FaqUsage = "Usage: !faq <entry>|list";

	/// <summary>Reply when no FAQ entry matches; <c>{0}</c> is the requested entry.</summary>
	public const string NoFaqEntryFound = "No FAQ entry found for '{0}'.";

	/// <summary>Reply when the FAQ has no entries.</summary>
	public const string NoFaqEntriesAvailable = "No FAQ entries available.";

	/// <summary>Reply listing the available FAQ entries; <c>{0}</c> is the comma-joined list.</summary>
	public const string AvailableFaqEntries = "Available FAQ entries: {0}";

	// ── !roll ────────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply to <c>!roll</c>; <c>{0}</c> is the sender's name, <c>{1}</c> the roll result.</summary>
	public const string RollResult = "{0} rolls {1} point(s)";

	// ── Chain errors ─────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a chain contains non-<c>!mp</c> commands.</summary>
	public const string ChainOnlyMpAllowed = "Chains are only allowed for !mp commands.";

	/// <summary>Reply when a chain is used in the #lobby channel.</summary>
	public const string ChainNotInLobby = "Chains are not allowed in #lobby.";
}
