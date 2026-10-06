namespace Basil.Bot.Application.Announcements;

/// <summary>
///     The user-visible reply text sent by BasilBot's room and anticheat announcements,
///     the single source of truth for that surface.
/// </summary>
internal static class AnnouncementReplies
{
	// ── Room countdown ─────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a countdown starts and will start the round; <c>{0}</c> is seconds.</summary>
	public const string CountdownStartedForRound = "Queued the match to start in {0} seconds";

	/// <summary>Reply when a countdown starts but won't start the round; <c>{0}</c> is seconds.</summary>
	public const string CountdownStarted = "Started a {0}-second countdown.";

	/// <summary>Reply when a countdown ticks and will start the round; <c>{0}</c> is seconds remaining.</summary>
	public const string CountdownTickedForRound = "Match starts in {0} seconds";

	/// <summary>Reply when a countdown ticks but won't start the round; <c>{0}</c> is seconds remaining.</summary>
	public const string CountdownTicked = "{0} seconds remaining";

	/// <summary>Reply when a countdown elapses and was meant to start the round.</summary>
	public const string CountdownElapsedForRound = "Match cannot start because no beatmap has been selected.";

	/// <summary>Reply when a countdown elapses but wasn't meant to start the round.</summary>
	public const string CountdownElapsed = "Countdown finished";

	/// <summary>Reply when a countdown is cancelled.</summary>
	public const string CountdownCancelled = "Countdown aborted.";

	// ── Room round ─────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a round starts after a countdown.</summary>
	public const string RoundStartedByCountdown = "Good luck, have fun!";

	/// <summary>Reply when a round cannot start because the room has no players.</summary>
	public const string RoundStartedNoPlayers = "Match cannot start because the room has no players.";

	/// <summary>Reply when a round is aborted.</summary>
	public const string RoundAborted = "Match aborted.";

	// ── Room settings ──────────────────────────────────────────────────────────────────────
	/// <summary>Reply when room settings change cancels a countdown that would start the round.</summary>
	public const string SettingsChangedCountdownCancelled = "Match start cancelled — room settings changed.";

	// ── Room closing ───────────────────────────────────────────────────────────────────────
	/// <summary>Reply when an empty room announces it will close; <c>{0}</c> is minutes.</summary>
	public const string RoomClosingAnnounced = "This room is empty and will close in {0} minutes unless a player joins.";

	/// <summary>Reply when a player joins a room that was closing.</summary>
	public const string RoomPlayerJoinedClosing = "A player joined — the room will no longer close.";

	// ── Anticheat ──────────────────────────────────────────────────────────────────────────
	/// <summary>Reply in room channel when a player is flagged; <c>{0}</c> is player, <c>{1}</c> is reason.</summary>
	public const string AnticheatFlagRoom = "Anti-cheat flag for {0}: {1}";

	/// <summary>
	///     Reply in PM when a player is flagged in a match; <c>{0}</c> is room id, <c>{1}</c> is room name,
	///     <c>{2}</c> is player, <c>{3}</c> is reason.
	/// </summary>
	public const string AnticheatFlagPm = "Anti-cheat flag in match #{0} {1}: {2} — {3}";
}
