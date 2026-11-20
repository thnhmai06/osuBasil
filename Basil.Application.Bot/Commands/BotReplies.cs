using Basil.Application.Bot.Replies;

namespace Basil.Application.Bot.Commands;

internal static class BotReplies
{
	// ── !where ───────────────────────────────────────────────────────────────────────────────
	public static readonly Reply WhereUsage = new("bot.whereUsage", "Usage: !where <username>");

	public static readonly Reply NotRegistered = new("bot.notRegistered", "{name} is not registered.", "name");

	public static readonly Reply WhereIsIn = new("bot.whereIsIn", "{name} is in {country}", "name", "country");

	// ── !faq ─────────────────────────────────────────────────────────────────────────────────
	public static readonly Reply FaqUsage = new("bot.faqUsage", "Usage: !faq <entry>|list");

	public static readonly Reply NoFaqEntryFound = new("bot.noFaqEntryFound",
		"No FAQ entry found for ''{entry}''.", "entry");

	public static readonly Reply NoFaqEntriesAvailable = new("bot.noFaqEntriesAvailable", "No FAQ entries available.");

	public static readonly Reply AvailableFaqEntries = new("bot.availableFaqEntries",
		"Available FAQ entries: {entries}", "entries");

	// ── !roll ────────────────────────────────────────────────────────────────────────────────
	public static readonly Reply RollResult = new("bot.rollResult", "{user} rolls {points} point(s)", "user", "points");

	// ── Chain errors ─────────────────────────────────────────────────────────────────────────
	public static readonly Reply ChainOnlyMpAllowed =
		new("bot.chainOnlyMpAllowed", "Chains are only allowed for !mp commands.");

	public static readonly Reply ChainNotInLobby = new("bot.chainNotInLobby", "Chains are not allowed in #lobby.");
}