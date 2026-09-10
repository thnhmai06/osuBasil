using Basil.Server.Shared.Localization;

namespace Basil.Server.Features.Bot;

/// <summary>
///     The user-visible reply text sent by BasilBot's own chat commands (<c>!where</c>,
///     <c>!faq</c>, <c>!roll</c>), the single source of truth for that surface.
/// </summary>
/// <remarks>
///     Every reply one of these commands sends through an <see cref="ICommandReplySink" /> is a
///     named member here (the <c>!mp</c> command surface has its own holder,
///     <c>Multiplayer.MpReplies</c>). Production services read these members and tests assert
///     against the same symbols, so the two cannot drift. The wording itself lives outside the
///     code, in this slice's <c>Locale/bot.en.json</c> fragment, so it can be edited without a
///     rebuild; each member's lookup key (via <see cref="LocaleCatalog.Get" />) is a dotted key
///     mirroring the command it belongs to, so a member and its wording cannot silently fall out of
///     sync. Format strings use <see cref="string.Format(string, object?[])" /> placeholders; fixed
///     strings are plain text. Deliberately changing the wording of a reply is a public-behavior
///     change: an edit to the locale file, and every test that pins it stays in sync.
/// </remarks>
public static class BotReplies
{
	// ── !where ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!where</c>.</summary>
	public static readonly string WhereUsage = LocaleCatalog.Get($"General.{nameof(WhereUsage)}");

	/// <summary>Reply when the named user is not registered; <c>{0}</c> is the name.</summary>
	public static readonly string NotRegistered = LocaleCatalog.Get($"General.{nameof(NotRegistered)}");

	/// <summary>Reply reporting a user's country; <c>{0}</c> is the name, <c>{1}</c> the country.</summary>
	public static readonly string WhereIsIn = LocaleCatalog.Get($"General.{nameof(WhereIsIn)}");

	// ── !faq ─────────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!faq</c>.</summary>
	public static readonly string FaqUsage = LocaleCatalog.Get($"General.{nameof(FaqUsage)}");

	/// <summary>Reply when no FAQ entry matches; <c>{0}</c> is the requested entry.</summary>
	public static readonly string NoFaqEntryFound = LocaleCatalog.Get($"General.{nameof(NoFaqEntryFound)}");

	/// <summary>Reply when the FAQ has no entries.</summary>
	public static readonly string NoFaqEntriesAvailable = LocaleCatalog.Get($"General.{nameof(NoFaqEntriesAvailable)}");

	/// <summary>Reply listing the available FAQ entries; <c>{0}</c> is the comma-joined list.</summary>
	public static readonly string AvailableFaqEntries = LocaleCatalog.Get($"General.{nameof(AvailableFaqEntries)}");

	// ── !roll ────────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply to <c>!roll</c>; <c>{0}</c> is the sender's name, <c>{1}</c> the roll result.</summary>
	public static readonly string RollResult = LocaleCatalog.Get($"General.{nameof(RollResult)}");
}
