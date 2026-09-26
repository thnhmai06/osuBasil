namespace Basil.Application.Services.Operations.Replies;

/// <summary>
///     The localization keys for BasilBot's own chat commands (<c>!where</c>, <c>!faq</c>,
///     <c>!roll</c>), the single source of truth for that surface.
/// </summary>
/// <remarks>
///     Every reply one of these commands sends is resolved from a named member here (the <c>!mp</c>
///     command surface has its own holder, <see cref="MpReplies" />) through
///     <see cref="Ports.ILocalizer" />, so production code and tests reference the same symbols and
///     cannot drift on the key. The wording itself is Infrastructure's concern; changing it is a
///     public-behavior change made there, not here.
/// </remarks>
public static class BotReplies
{
	// ── !where ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!where</c>.</summary>
	public const string WhereUsage = "General.WhereUsage";

	/// <summary>Reply when the named user is not registered; <c>{0}</c> is the name.</summary>
	public const string NotRegistered = "General.NotRegistered";

	/// <summary>Reply reporting a user's country; <c>{0}</c> is the name, <c>{1}</c> the country.</summary>
	public const string WhereIsIn = "General.WhereIsIn";

	// ── !faq ─────────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!faq</c>.</summary>
	public const string FaqUsage = "General.FaqUsage";

	/// <summary>Reply when no FAQ entry matches; <c>{0}</c> is the requested entry.</summary>
	public const string NoFaqEntryFound = "General.NoFaqEntryFound";

	/// <summary>Reply when the FAQ has no entries.</summary>
	public const string NoFaqEntriesAvailable = "General.NoFaqEntriesAvailable";

	/// <summary>Reply listing the available FAQ entries; <c>{0}</c> is the comma-joined list.</summary>
	public const string AvailableFaqEntries = "General.AvailableFaqEntries";

	// ── !roll ────────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply to <c>!roll</c>; <c>{0}</c> is the sender's name, <c>{1}</c> the roll result.</summary>
	public const string RollResult = "General.RollResult";
}