namespace Basil.Application.Services.Content;

/// <summary>
///     Server-level text that is not specific to BasilBot's chat commands or the IRC gateway, the
///     single source of truth for that surface.
/// </summary>
/// <remarks>
///     The wording lives outside the code, in <see cref="ReplyLocale" />'s <c>Server.json</c>, so it
///     can be edited without a rebuild -- see <see cref="ReplyLocale.Server" />. Unlike
///     <see cref="Bot.MpReplies" />/<see cref="Irc.IrcReplies" />, this is not tied to a live
///     protocol exchange it must match member-for-member; it holds whatever server-wide text isn't
///     owned by either of those two surfaces.
/// </remarks>
public static class ServerReplies
{
	// ── message of the day ───────────────────────────────────────────────────────────────────
	/// <summary>
	///     The message shown to a player as a login notification, and returned by the IRC
	///     gateway's <c>/MOTD</c> command. Blank means no MOTD is configured.
	/// </summary>
	public static readonly string MotdText = ReplyLocale.Server("Motd.Text");
}